using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The FLY screen's Swarm tab: the aircraft on the links, and commands sent to several of them at once. The first
    /// is the stacked orbit (Utilities/StackedOrbit): the ticked planes circle one point, one above the other.
    /// </summary>
    public class SwarmControl : UserControl
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        // settings, in metres whatever the units shown
        private const string FloorSetting = "swarm_stack_floor";
        private const string SeparationSetting = "swarm_stack_sep";
        private const string FrameSetting = "swarm_stack_frame";

        /// <summary>A vehicle not heard from for this long has no link.</summary>
        private const double StaleSeconds = 5;

        private enum Sent
        {
            None,
            Sending,
            Accepted,
            Failed,
            NotSent
        }

        private class Row
        {
            public MAVLinkInterface Port;
            public MAVState Mav;
            public DataGridViewRow GridRow;
            public bool Selectable;
            public Sent Sent;
            public string Result = "";
        }

        // one aircraft of a confirmed plan
        private class SendItem
        {
            public Row Row;
            public StackedOrbit.Slot Slot;
        }

        private class FrameChoice
        {
            public string Name;
            public MAVLink.MAV_FRAME Frame;

            public override string ToString()
            {
                return Name;
            }
        }

        private DataGridView grid;
        private DataGridViewCheckBoxColumn colInclude;
        private DataGridViewTextBoxColumn colAircraft, colMode, colAlt, colSlot, colParams, colStatus;
        private FlowLayoutPanel panel;
        private Label lblCentre, lblFloor, lblFloorUnit, lblSeparation, lblSeparationUnit, lblFrame, lblMessage;
        private NumericUpDown numFloor, numSeparation;
        private ComboBox cmbFrame;
        private MyButton butSend, butRetry, butCancel;
        private System.Windows.Forms.Timer timer;

        private readonly List<Row> rows = new List<Row>();

        private double floorM = 100;
        private double separationM = 30;
        private float shownMultiplier = float.NaN;
        private bool settingInputs;

        private PointLatLngAlt centre;
        private double groundAtCentreM = double.NaN;
        private DateTime groundChecked = DateTime.MinValue;
        private double? shownRadiusM;

        // the plan as confirmed, in the order sent: what Retry sends again
        private MAVLink.MAV_FRAME sentFrame;
        private PointLatLngAlt sentCentre;
        private List<SendItem> sentItems;
        private double sentSeparationM;
        private volatile bool sending;
        private volatile bool cancelSend;

        private bool isActivated;

        /// <summary>The centre or the radius to draw on the map changed.</summary>
        public event EventHandler MapChanged;

        /// <summary>The orbit's centre, null until one is chosen on the map.</summary>
        public PointLatLngAlt Centre => centre;

        /// <summary>The widest loiter radius of the ticked aircraft, for the circle on the map.</summary>
        public double? OrbitRadiusM => shownRadiusM;

        public SwarmControl()
        {
            InitializeComponent();

            floorM = SavedMetres(FloorSetting, floorM);
            separationM = SavedMetres(SeparationSetting, separationM);

            var frame = (MAVLink.MAV_FRAME) Settings.Instance.GetByte(FrameSetting,
                (byte) MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT);

            // the inputs aren't showing the saved values yet: selecting the frame mustn't read them back
            settingInputs = true;
            cmbFrame.SelectedItem = cmbFrame.Items.Cast<FrameChoice>().FirstOrDefault(f => f.Frame == frame) ??
                                    cmbFrame.Items[0];

            ShowInputs();
            ShowCentre();
            UpdateButtons();
        }

        private MAVLink.MAV_FRAME Frame => ((FrameChoice) cmbFrame.SelectedItem).Frame;

        // saved with the invariant culture, so read with it: Settings.GetDouble reads with the PC's, where 91.44 is 9144
        private static double SavedMetres(string key, double fallback)
        {
            return double.TryParse(Settings.Instance[key], NumberStyles.Float, CultureInfo.InvariantCulture,
                       out var value) && value > 0
                ? value
                : fallback;
        }

        public void Activate()
        {
            if (isActivated)
                return;

            isActivated = true;
            ThemeManager.ApplyThemeTo(this);
            Tick();
            timer.Start();
        }

        public void Deactivate()
        {
            if (!isActivated)
                return;

            isActivated = false;

            // a send in progress still shows its results
            if (!sending)
                timer.Stop();
        }

        /// <summary>Sets the point the stack orbits (from the map).</summary>
        public void SetCentre(PointLatLngAlt point)
        {
            centre = point;
            groundAtCentreM = double.NaN;
            groundChecked = DateTime.MinValue;
            ForgetSentPlan();
            ShowCentre();
            Tick();
            MapChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ShowCentre()
        {
            lblCentre.Text = centre == null
                ? "Center: right-click the map and choose Stacked Orbit Here"
                : "Center: " + centre.Lat.ToString("0.000000") + ", " + centre.Lng.ToString("0.000000");
        }

        private static string Alt(double metres)
        {
            if (double.IsNaN(metres))
                return "?";
            return (metres * CurrentState.multiplieralt).ToString("0") + " " + CurrentState.AltUnit;
        }

        // the floor and separation in the units the application shows altitudes in
        private void ShowInputs()
        {
            settingInputs = true;
            try
            {
                shownMultiplier = CurrentState.multiplieralt;
                numFloor.Value = Clamp(numFloor, floorM * shownMultiplier);
                numSeparation.Value = Clamp(numSeparation, separationM * shownMultiplier);
                lblFloorUnit.Text = CurrentState.AltUnit;
                lblSeparationUnit.Text = CurrentState.AltUnit;
            }
            finally
            {
                settingInputs = false;
            }
        }

        private static decimal Clamp(NumericUpDown num, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return num.Minimum;
            return Math.Max(num.Minimum, Math.Min(num.Maximum, (decimal) Math.Round(value)));
        }

        private void Inputs_Changed(object sender, EventArgs e)
        {
            if (settingInputs)
                return;

            // by the units they are showing, which a change of the setting hasn't reached until the next tick
            floorM = (double) numFloor.Value / shownMultiplier;
            separationM = (double) numSeparation.Value / shownMultiplier;

            Settings.Instance[FloorSetting] = floorM.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Settings.Instance[SeparationSetting] =
                separationM.ToString(System.Globalization.CultureInfo.InvariantCulture);
            Settings.Instance[FrameSetting] = ((byte) Frame).ToString();

            ForgetSentPlan();
            Tick();
        }

        // Retry sends the plan as it was confirmed: once what it was made from changes, there is nothing to retry
        private void ForgetSentPlan()
        {
            if (sending)
                return;

            sentItems = null;
            foreach (var row in rows)
            {
                row.Sent = Sent.None;
                row.Result = "";
            }

            UpdateButtons();
        }

        private static bool Fresh(MAVState mav)
        {
            return (DateTime.UtcNow - mav.lastvalidpacket).TotalSeconds < StaleSeconds;
        }

        private static bool IsGuided(MAVState mav)
        {
            return string.Equals(mav.cs.mode, "GUIDED", StringComparison.OrdinalIgnoreCase);
        }

        // the stack is for planes, which circle a guided point; a copter would hover on it
        private static bool IsPlane(MAVState mav)
        {
            return mav.apname == MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA && mav.cs.firmware == Firmwares.ArduPlane;
        }

        private static double AmslM(MAVState mav)
        {
            return mav.cs.altasl / CurrentState.multiplieralt;
        }

        // above home, in metres: cs.alt is in the units shown, less the home offset
        private static double RelativeM(MAVState mav)
        {
            return mav.cs.alt / CurrentState.multiplieralt + mav.cs.altoffsethome;
        }

        // where the aircraft is now in the frame's terms: above sea level, above the ground under it, or above home
        private static double NowM(MAVState mav, MAVLink.MAV_FRAME frame)
        {
            if (frame == MAVLink.MAV_FRAME.GLOBAL)
                return AmslM(mav);
            if (MissionAltitude.FollowsTerrain(frame))
                return AmslM(mav) - GroundM(mav.cs.lat, mav.cs.lng);
            return RelativeM(mav);
        }

        // the ground under a point, NaN while its elevation isn't known
        private static double GroundM(double lat, double lng)
        {
            try
            {
                var ground = srtm.getAltitude(lat, lng);
                return ground.currenttype == srtm.tiletype.valid ? ground.alt : double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }

        // a slot's level, and an aircraft's now, in one datum for comparing aircraft: above the ground for Terrain,
        // above sea level otherwise (in Relative each aircraft counts from its own home)
        private static double SlotLevelM(StackedOrbit.Slot slot, MAVLink.MAV_FRAME frame)
        {
            return MissionAltitude.FollowsTerrain(frame) ? slot.TargetM : slot.TargetAmslM;
        }

        private static double LevelNowM(MAVState mav, MAVLink.MAV_FRAME frame)
        {
            return MissionAltitude.FollowsTerrain(frame) ? NowM(mav, frame) : AmslM(mav);
        }

        /// <summary>The radius a plane circles a guided point at, from its parameters.</summary>
        private static double? LoiterRadiusM(MAVState mav)
        {
            try
            {
                foreach (var name in new[] {"LOITER_RADIUS", "WP_LOITER_RAD", "LOITER_RAD"})
                {
                    if (!mav.param.ContainsKey(name))
                        continue;
                    var radius = Math.Abs((double) mav.param[name]);
                    if (radius > 0)
                        return radius;
                }
            }
            catch
            {
            }

            return null;
        }

        private void Timer_Tick(object sender, EventArgs e)
        {
            Tick();

            if (!isActivated && !sending)
                timer.Stop();
        }

        // the list changing its own cells isn't the operator ticking one
        private bool ticking;

        private void Tick()
        {
            if (ticking)
                return;

            ticking = true;
            try
            {
                if (shownMultiplier != CurrentState.multiplieralt)
                    ShowInputs();

                UpdateRows();
                UpdateGround();

                StackedOrbit.Plan preview = null;
                if (!sending)
                {
                    preview = BuildPlan();
                    foreach (var row in rows)
                    {
                        var slot = preview.Slots.FirstOrDefault(s => s.Aircraft.Tag == row);
                        row.GridRow.Cells[colSlot.Index].Value = slot == null ? "" : Alt(slot.TargetM);
                    }
                }

                ShowMessage(preview);
                UpdateRadius();
                UpdateButtons();
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                ticking = false;
            }
        }

        private void UpdateRows()
        {
            var seen = new HashSet<MAVState>();

            foreach (var port in MainV2.Comports.ToArray())
            {
                foreach (var mav in port.MAVlist)
                {
                    if (!VehicleParamLoader.Eligible(port, mav))
                        continue;

                    seen.Add(mav);

                    var row = rows.FirstOrDefault(r => r.Mav == mav);
                    if (row == null)
                    {
                        row = new Row {Port = port, Mav = mav, Selectable = IsPlane(mav)};
                        var index = grid.Rows.Add();
                        row.GridRow = grid.Rows[index];
                        row.GridRow.Tag = row;
                        rows.Add(row);
                        // none starts ticked: the operator chooses each aircraft to command
                        row.GridRow.Cells[colInclude.Index].Value = false;
                    }

                    UpdateRow(row);
                }
            }

            // a disconnect or a reconnect: those vehicles are gone (the plan being sent keeps its rows)
            foreach (var row in rows.Where(r => !seen.Contains(r.Mav)).ToArray())
            {
                if (sending && sentItems != null && sentItems.Any(i => i.Row == row))
                    continue;

                grid.Rows.Remove(row.GridRow);
                rows.Remove(row);
            }
        }

        private void UpdateRow(Row row)
        {
            var mav = row.Mav;
            var cells = row.GridRow.Cells;

            // the type can come after the first heartbeat
            var selectable = IsPlane(mav);
            if (selectable != row.Selectable)
            {
                row.Selectable = selectable;
                if (!selectable)
                    cells[colInclude.Index].Value = false;
            }

            cells[colInclude.Index].ReadOnly = !row.Selectable || sending;
            cells[colAircraft.Index].Value = mav.sysid + (MainV2.Comports.Count > 1 ? " " + PortName(row.Port) : "");
            cells[colMode.Index].Value = (mav.cs.mode ?? "") + (mav.cs.armed ? "" : " (disarmed)");
            cells[colAlt.Index].Value = Alt(NowM(mav, Frame));
            cells[colParams.Index].Value = ParamText(mav);
            cells[colStatus.Index].Value = StatusText(row);

            var colour = row.Selectable && Fresh(mav) ? Color.Empty : Color.Gray;
            if (row.GridRow.DefaultCellStyle.ForeColor != colour)
                row.GridRow.DefaultCellStyle.ForeColor = colour;
        }

        private static string PortName(MAVLinkInterface port)
        {
            try
            {
                return port.BaseStream.PortName;
            }
            catch
            {
                return "";
            }
        }

        private static string ParamText(MAVState mav)
        {
            switch (VehicleParamLoader.Instance.StateOf(mav))
            {
                case ParamLoadState.Loaded:
                    return "Loaded";
                case ParamLoadState.Pending:
                    return "Waiting";
                case ParamLoadState.Loading:
                    return "Loading";
                case ParamLoadState.Failed:
                    return "Failed";
                case ParamLoadState.NeedsManual:
                    return "On switch";
                default:
                    return mav.param.TotalReported > 0
                        ? mav.param.TotalReceived + "/" + mav.param.TotalReported
                        : "Not loaded";
            }
        }

        private bool Included(Row row)
        {
            return row.Selectable && true.Equals(row.GridRow.Cells[colInclude.Index].Value);
        }

        // the result of the last send, else what the operator should know before one
        private string StatusText(Row row)
        {
            if (row.Sent != Sent.None)
                return row.Result;

            var mav = row.Mav;

            if (!row.Selectable)
                return "Not a plane";
            if (!Fresh(mav))
                return "No link";
            if (!Included(row))
                return "";
            if (!mav.cs.armed)
                return "Disarmed";
            if (MissionAltitude.FollowsTerrain(Frame) && !mav.cs.terrainactive)
                return "No terrain data";

            try
            {
                // a quadplane told to hold guided points as a copter stops over the centre
                if (ArduPilot.Common.IsQuadPlane(mav) && mav.param.ContainsKey("Q_GUIDED_MODE") &&
                    mav.param["Q_GUIDED_MODE"].Value != 0)
                    return "Q_GUIDED_MODE: will hover";
            }
            catch
            {
            }

            return "";
        }

        // the ground under the centre, for Terrain and for checking an Absolute floor; the tiles may still be coming
        private void UpdateGround()
        {
            if (centre == null || !double.IsNaN(groundAtCentreM) ||
                (DateTime.UtcNow - groundChecked).TotalSeconds < 2)
                return;

            groundChecked = DateTime.UtcNow;
            try
            {
                var ground = srtm.getAltitude(centre.Lat, centre.Lng);
                if (ground.currenttype == srtm.tiletype.valid)
                    groundAtCentreM = ground.alt;
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private StackedOrbit.Plan BuildPlan()
        {
            var aircraft = rows.Where(Included).Select(row =>
            {
                var amsl = AmslM(row.Mav);
                return new StackedOrbit.Aircraft
                {
                    Name = "Aircraft " + row.Mav.sysid,
                    SysId = row.Mav.sysid,
                    AmslM = amsl,
                    HomeAmslM = amsl - RelativeM(row.Mav),
                    Tag = row
                };
            });

            var plan = StackedOrbit.Build(aircraft, Frame, floorM, separationM, groundAtCentreM, Alt);

            // the clearance above the ground at the center is checked once its elevation is known
            if (centre != null && double.IsNaN(groundAtCentreM) && !MissionAltitude.FollowsTerrain(Frame))
                plan.Warnings = plan.Warnings.Concat(new[]
                {
                    "The ground at the center isn't known (no elevation data), so the clearance above it isn't checked."
                }).ToList();

            return plan;
        }

        private void ShowMessage(StackedOrbit.Plan preview)
        {
            string text;
            var problem = false;

            if (sending)
            {
                text = "Sending...";
            }
            else if (preview == null || preview.Slots.Count == 0)
            {
                text = "Tick the aircraft to stack.";
            }
            else if (!preview.Valid)
            {
                text = string.Join("\n", preview.Errors);
                problem = true;
            }
            else if (preview.Warnings.Count > 0)
            {
                text = string.Join("\n", preview.Warnings);
                problem = true;
            }
            else if (centre == null)
            {
                text = "";
            }
            else
            {
                text = preview.Slots.Count + " aircraft, " + Alt(preview.Slots.First().TargetM) + " to " +
                       Alt(preview.Slots.Last().TargetM) + ".";
            }

            if (lblMessage.Text != text)
                lblMessage.Text = text;

            var colour = problem ? Color.Orange : lblCentre.ForeColor;
            if (lblMessage.ForeColor != colour)
                lblMessage.ForeColor = colour;
        }

        private void UpdateRadius()
        {
            double? radius = null;
            foreach (var row in rows.Where(Included))
            {
                var r = LoiterRadiusM(row.Mav);
                if (r != null && (radius == null || r > radius))
                    radius = r;
            }

            if (radius == shownRadiusM)
                return;

            shownRadiusM = radius;
            MapChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateButtons()
        {
            butSend.Enabled = !sending && centre != null;
            butRetry.Enabled = !sending && sentItems != null && sentItems.Any(i => i.Row.Sent != Sent.Accepted);
            butCancel.Enabled = sending;
            numFloor.Enabled = numSeparation.Enabled = cmbFrame.Enabled = !sending;
        }

        private void ButSend_Click(object sender, EventArgs e)
        {
            if (sending || centre == null)
                return;

            UpdateGround();
            var plan = BuildPlan();

            if (!plan.Valid)
            {
                CustomMessageBox.Show(string.Join("\n", plan.Errors), "Stacked Orbit");
                return;
            }

            var frameName = ((FrameChoice) cmbFrame.SelectedItem).Name;
            var text = new StringBuilder();
            text.AppendLine("Send " + plan.Slots.Count + " aircraft to orbit " + centre.Lat.ToString("0.000000") +
                            ", " + centre.Lng.ToString("0.000000") + "?");
            text.AppendLine();
            text.AppendLine("Altitudes are " + frameName.ToLower() + ", " + Alt(plan.SeparationM) + " apart.");
            text.AppendLine();
            foreach (var slot in plan.Slots.Reverse())
            {
                var row = (Row) slot.Aircraft.Tag;
                var now = NowM(row.Mav, plan.Frame);
                var line = "Aircraft " + row.Mav.sysid + ": " + Alt(slot.TargetM) + " (now " + Alt(now) + ")";
                var note = PreSendNote(row);
                if (note != "")
                    line += "  [" + note + "]";
                text.AppendLine(line);
            }

            if (plan.Warnings.Count > 0)
            {
                text.AppendLine();
                foreach (var warning in plan.Warnings)
                    text.AppendLine(warning);
            }

            if ((int) CustomMessageBox.Show(text.ToString(), "Stacked Orbit", MessageBoxButtons.YesNo) !=
                (int) DialogResult.Yes)
                return;

            // from here the plan is fixed: what was confirmed is what is sent, in this order
            var items = StackedOrbit.SendOrder(plan)
                .Select(slot => new SendItem {Row = (Row) slot.Aircraft.Tag, Slot = slot}).ToList();

            foreach (var row in rows)
            {
                row.Sent = Sent.None;
                row.Result = "";
            }

            sentSeparationM = plan.SeparationM;
            StartSend(items, plan.Frame, centre, items);
        }

        private string PreSendNote(Row row)
        {
            row.Sent = Sent.None;
            return StatusText(row);
        }

        private void ButRetry_Click(object sender, EventArgs e)
        {
            if (sending || sentItems == null)
                return;

            var remaining = sentItems.Where(i => i.Row.Sent != Sent.Accepted).ToList();
            if (remaining.Count == 0)
                return;

            // the slots were set by where the aircraft were: those still to go mustn't pass a level the others now hold
            var conflicts = StackedOrbit.RetryConflicts(
                remaining.Select(i => ("Aircraft " + i.Row.Mav.sysid, LevelNowM(i.Row.Mav, sentFrame),
                    SlotLevelM(i.Slot, sentFrame))),
                sentItems.Where(i => i.Row.Sent == Sent.Accepted)
                    .Select(i => ("Aircraft " + i.Row.Mav.sysid, SlotLevelM(i.Slot, sentFrame))),
                sentSeparationM, Alt);
            if (conflicts.Count > 0)
            {
                CustomMessageBox.Show(
                    "Retry would send aircraft through levels the stack now holds:\n\n" + string.Join("\n", conflicts) +
                    "\n\nSend a new stack instead.", "Stacked Orbit");
                return;
            }

            var text = new StringBuilder();
            text.AppendLine("Send the same slots to the " + remaining.Count + " aircraft not yet in the stack?");
            text.AppendLine();
            foreach (var item in remaining.OrderByDescending(i => i.Slot.Index))
                text.AppendLine("Aircraft " + item.Row.Mav.sysid + ": " + Alt(item.Slot.TargetM));

            if ((int) CustomMessageBox.Show(text.ToString(), "Stacked Orbit", MessageBoxButtons.YesNo) !=
                (int) DialogResult.Yes)
                return;

            StartSend(remaining, sentFrame, sentCentre, sentItems);
        }

        private void ButCancel_Click(object sender, EventArgs e)
        {
            cancelSend = true;
        }

        private void StartSend(List<SendItem> items, MAVLink.MAV_FRAME frame, PointLatLngAlt at, List<SendItem> plan)
        {
            sentItems = plan;
            sentFrame = frame;
            sentCentre = at;
            sending = true;
            cancelSend = false;

            foreach (var item in items)
            {
                item.Row.Sent = Sent.NotSent;
                item.Row.Result = "Waiting";
                // the slot confirmed, which the preview may have moved on from
                item.Row.GridRow.Cells[colSlot.Index].Value = Alt(item.Slot.TargetM);
            }

            UpdateButtons();
            timer.Start();
            Tick();

            Task.Run(() => SendAll(items, frame, at));
        }

        // one aircraft at a time, on a background thread, stopping at the first that doesn't take its slot
        private void SendAll(List<SendItem> items, MAVLink.MAV_FRAME frame, PointLatLngAlt at)
        {
            try
            {
                // the parameter downloads stay off the link while commands are going out
                using (VehicleParamLoader.Instance.Suspend())
                {
                    var stopped = false;
                    foreach (var item in items)
                    {
                        if (stopped || cancelSend)
                        {
                            item.Row.Sent = Sent.NotSent;
                            item.Row.Result = "Not sent";
                            continue;
                        }

                        item.Row.Sent = Sent.Sending;
                        item.Row.Result = "Sending";

                        string result;
                        bool ok;
                        try
                        {
                            ok = SendOne(item, frame, at, out result);
                        }
                        catch (Exception ex)
                        {
                            log.Error(ex);
                            ok = false;
                            result = "Failed: " + ex.Message;
                        }

                        item.Row.Sent = ok ? Sent.Accepted : Sent.Failed;
                        item.Row.Result = result;

                        if (!ok)
                            stopped = true;
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                sending = false;
                try
                {
                    this.BeginInvokeIfRequired(() =>
                    {
                        Tick();
                        if (!isActivated)
                            timer.Stop();
                    });
                }
                catch
                {
                }
            }
        }

        private bool SendOne(SendItem item, MAVLink.MAV_FRAME frame, PointLatLngAlt at, out string result)
        {
            var port = item.Row.Port;
            var mav = item.Row.Mav;

            // the aircraft as it is now, not as it was when the plan was confirmed
            if (port.BaseStream == null || !port.BaseStream.IsOpen ||
                !port.MAVlist.Contains(mav.sysid, mav.compid, false) ||
                !ReferenceEquals(port.MAVlist[mav.sysid, mav.compid], mav))
            {
                result = "Disconnected";
                return false;
            }

            if (port.ReadOnly)
            {
                result = "Link is read only";
                return false;
            }

            if (!Fresh(mav))
            {
                result = "No link";
                return false;
            }

            if (!mav.cs.armed)
            {
                result = "Disarmed";
                return false;
            }

            // setWP and doCommand read their answer off the port: wait for whatever has it
            var until = DateTime.UtcNow.AddSeconds(5);
            while (port.giveComport && DateTime.UtcNow < until)
                Thread.Sleep(50);

            var changedMode = false;
            if (!IsGuided(mav))
            {
                var mode = new MAVLink.mavlink_set_mode_t();
                if (!port.translateMode(mav.sysid, mav.compid, "GUIDED", ref mode))
                {
                    result = "No GUIDED mode";
                    return false;
                }

                try
                {
                    // acknowledged, where setMode sends and hopes
                    if (!port.doCommand(mav.sysid, mav.compid, MAVLink.MAV_CMD.DO_SET_MODE, mode.base_mode,
                            mode.custom_mode, 0, 0, 0, 0, 0))
                    {
                        result = "GUIDED rejected";
                        return false;
                    }
                }
                catch (TimeoutException)
                {
                    result = "No response";
                    return false;
                }

                until = DateTime.UtcNow.AddSeconds(3);
                while (!IsGuided(mav) && DateTime.UtcNow < until)
                    Thread.Sleep(50);

                if (!IsGuided(mav))
                {
                    result = "GUIDED not confirmed";
                    return false;
                }

                changedMode = true;
            }

            // setWP stores the point as the aircraft's guided target on any answer, a refusal too
            var before = mav.GuidedMode;
            var loc = new Locationwp
            {
                id = (ushort) MAVLink.MAV_CMD.WAYPOINT,
                lat = at.Lat,
                lng = at.Lng,
                alt = (float) item.Slot.TargetM,
                frame = (byte) frame
            };

            var held = changedMode ? ", holding in GUIDED" : "";
            try
            {
                // current = 2: the guided target, which a plane circles at its loiter radius
                var answer = port.setWP(mav.sysid, mav.compid, loc, 0, frame, 2);
                if (answer == MAVLink.MAV_MISSION_RESULT.MAV_MISSION_ACCEPTED)
                {
                    result = "Accepted";
                    return true;
                }

                mav.GuidedMode = before;
                result = "Rejected (" + answer.ToString().Replace("MAV_MISSION_", "") + ")" + held;
                return false;
            }
            catch (TimeoutException)
            {
                mav.GuidedMode = before;
                result = "No response" + held;
                return false;
            }
        }

        private void Grid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            // a tick counts when it is made, not when the cell is left
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void Grid_CellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (ticking || e.RowIndex < 0 || e.ColumnIndex != colInclude.Index)
                return;

            ForgetSentPlan();
            Tick();
        }

        private void InitializeComponent()
        {
            grid = new DataGridView();
            colInclude = new DataGridViewCheckBoxColumn();
            colAircraft = new DataGridViewTextBoxColumn();
            colMode = new DataGridViewTextBoxColumn();
            colAlt = new DataGridViewTextBoxColumn();
            colSlot = new DataGridViewTextBoxColumn();
            colParams = new DataGridViewTextBoxColumn();
            colStatus = new DataGridViewTextBoxColumn();
            panel = new FlowLayoutPanel();
            lblCentre = new Label();
            lblFloor = new Label();
            numFloor = new NumericUpDown();
            lblFloorUnit = new Label();
            lblSeparation = new Label();
            numSeparation = new NumericUpDown();
            lblSeparationUnit = new Label();
            lblFrame = new Label();
            cmbFrame = new ComboBox();
            butSend = new MyButton();
            butRetry = new MyButton();
            butCancel = new MyButton();
            lblMessage = new Label();
            timer = new System.Windows.Forms.Timer();

            SuspendLayout();

            colInclude.HeaderText = "";
            colInclude.Width = 28;
            colInclude.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            colInclude.Resizable = DataGridViewTriState.False;

            TextColumn(colAircraft, "Aircraft", 50);
            TextColumn(colMode, "Mode", 80);
            TextColumn(colAlt, "Altitude", 60);
            TextColumn(colSlot, "Slot", 60);
            TextColumn(colParams, "Params", 60);
            TextColumn(colStatus, "Status", 120);

            grid.Name = "grid";
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.RowHeadersVisible = false;
            grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            grid.Columns.AddRange(colInclude, colAircraft, colMode, colAlt, colSlot, colParams, colStatus);
            grid.CurrentCellDirtyStateChanged += Grid_CurrentCellDirtyStateChanged;
            grid.CellValueChanged += Grid_CellValueChanged;

            panel.Name = "panel";
            panel.Dock = DockStyle.Bottom;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.WrapContents = true;
            panel.Padding = new Padding(3);

            SetupCaption(lblCentre, "");
            panel.Controls.Add(lblCentre);
            panel.SetFlowBreak(lblCentre, true);

            SetupCaption(lblFloor, "Floor");
            SetupNumber(numFloor);
            SetupCaption(lblFloorUnit, "m");
            SetupCaption(lblSeparation, "Separation");
            SetupNumber(numSeparation);
            SetupCaption(lblSeparationUnit, "m");
            SetupCaption(lblFrame, "Altitudes");

            cmbFrame.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbFrame.Width = 80;
            // as the Fly To Here Alt box offers them
            cmbFrame.Items.Add(new FrameChoice {Name = "Relative", Frame = MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT});
            cmbFrame.Items.Add(new FrameChoice {Name = "Absolute", Frame = MAVLink.MAV_FRAME.GLOBAL});
            cmbFrame.Items.Add(new FrameChoice {Name = "Terrain", Frame = MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT});
            cmbFrame.SelectedIndex = 0;
            cmbFrame.SelectedIndexChanged += Inputs_Changed;

            panel.Controls.AddRange(new Control[]
            {
                lblFloor, numFloor, lblFloorUnit, lblSeparation, numSeparation, lblSeparationUnit, lblFrame, cmbFrame
            });
            panel.SetFlowBreak(cmbFrame, true);

            SetupButton(butSend, "Send Stacked Orbit", 130, ButSend_Click);
            SetupButton(butRetry, "Retry", 60, ButRetry_Click);
            SetupButton(butCancel, "Cancel", 60, ButCancel_Click);
            panel.Controls.AddRange(new Control[] {butSend, butRetry, butCancel});
            panel.SetFlowBreak(butCancel, true);

            SetupCaption(lblMessage, "");
            panel.Controls.Add(lblMessage);

            timer.Interval = 500;
            timer.Tick += Timer_Tick;

            Controls.Add(grid);
            Controls.Add(panel);
            Name = "SwarmControl";
            Size = new Size(527, 311);

            ResumeLayout(false);
            PerformLayout();
        }

        private static void TextColumn(DataGridViewTextBoxColumn column, string header, int weight)
        {
            column.HeaderText = header;
            column.ReadOnly = true;
            column.FillWeight = weight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
        }

        private static void SetupCaption(Label label, string text)
        {
            label.AutoSize = true;
            label.Text = text;
            label.Margin = new Padding(3, 6, 0, 3);
        }

        private void SetupNumber(NumericUpDown num)
        {
            num.Minimum = 0;
            num.Maximum = 100000;
            num.Width = 62;
            num.ValueChanged += Inputs_Changed;
        }

        private static void SetupButton(MyButton button, string text, int width, EventHandler click)
        {
            button.Text = text;
            button.Size = new Size(width, 24);
            button.Click += click;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                cancelSend = true;
                timer?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
