using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Utilities;
#if !LIB
using MissionPlanner.Controls.Autoland;
#endif

namespace MissionPlanner.GCSViews
{
    /// <summary>
    /// The fixed wing autoland sequence on the PLAN screen (see <see cref="AutolandPattern"/>). The grid stays the
    /// mission: the sequence is eight plain rows, found again in the grid after every change, and drawn with its
    /// landing box and handles. Dragging a handle or editing the panel lays the rows out again in place.
    /// </summary>
    public partial class FlightPlanner
    {
        /// <summary>Auto WP > Fixed Wing Autoland: a sequence landing where the map was clicked.</summary>
        public void fixedWingAutolandToolStripMenuItem_Click(object sender, EventArgs e)
        {
#if !LIB
            InsertAutoland(new PointLatLngAlt(MouseDownEnd.Lat, MouseDownEnd.Lng, 0));
#endif
        }

#if !LIB
        private GMapOverlay autolandOverlay;
        private AutolandPanel autolandPanel;

        // the button under the polygon and zoom buttons, and the menu it opens
        private readonly AutolandIcon autolandIcon = new AutolandIcon();
        private ContextMenuStrip autolandMenu;

        // the sequences in the grid, by the index of their DO_LAND_START row
        private List<KeyValuePair<int, AutolandLayout>> autolands = new List<KeyValuePair<int, AutolandLayout>>();

        // the selected sequence's DO_LAND_START row and LAND, or -1
        private int autolandSelected = -1;
        private PointLatLngAlt autolandSelectedLand;

        // the box widths, which no row carries, by LAND
        private readonly Dictionary<string, double> autolandBoxWidths = new Dictionary<string, double>();

        // the handle under the mouse, and the one being dragged
        private GMapMarkerAutolandHandle autolandHover;
        private AutolandDrag autolandDrag;

        private class AutolandDrag
        {
            public int Start;
            public AutolandHandle Handle;
            public AutolandLayout From;
            public AutolandLayout Preview;
        }

        private static readonly Brush AutolandBoxFill = new SolidBrush(Color.FromArgb(80, 0, 200, 0));
        private static readonly Pen AutolandBoxPen = new Pen(Color.FromArgb(230, 0, 170, 0), 2);
        private static readonly Pen AutolandBoxSelectedPen = new Pen(Color.FromArgb(255, 60, 230, 60), 3);
        private static readonly Pen AutolandBoxSteepPen = new Pen(Color.FromArgb(230, 255, 176, 32), 2);
        private static readonly Pen AutolandBoxSteepSelectedPen = new Pen(Color.FromArgb(255, 255, 176, 32), 3);

        private void InitAutoland()
        {
            autolandOverlay = new GMapOverlay("autoland");
            MainMap.Overlays.Add(autolandOverlay);

            autolandPanel = new AutolandPanel {Visible = false, Anchor = AnchorStyles.Top | AnchorStyles.Right};
            autolandPanel.ParamsChanged += p =>
            {
                if (autolandSelected >= 0)
                    UpdateAutoland(autolandSelected, p, autolandSelectedLand, true);
            };
            autolandPanel.RemoveClicked += RemoveSelectedAutoland;
            autolandPanel.CloseClicked += () => SelectAutoland(-1);
            MainMap.Controls.Add(autolandPanel);
            MainMap.Resize += (s, e) => PlaceAutolandPanel();
            // Advanced mode changes the panel's size: keep it in the corner
            autolandPanel.SizeChanged += (s, e) => PlaceAutolandPanel();
        }

        /// <summary>Paints the autoland button under <paramref name="above"/> (the zoom button).</summary>
        private void PaintAutolandIcon(Graphics g, MissionPlanner.Controls.Icon.Icon above)
        {
            autolandIcon.Location = new Point(above.Location.X, above.Location.Y + above.Height + 5);
            autolandIcon.IsSelected = autolandSelected >= 0;
            autolandIcon.Paint(g);
            g.ResetTransform();
        }

        /// <summary>
        /// A click on the autoland button: a menu to edit each sequence in the mission, or add one at the map's center.
        /// True when the click was on the button.
        /// </summary>
        private bool AutolandIconClick(Point at)
        {
            if (!autolandIcon.Rectangle.Contains(at))
                return false;

            isMouseDown = false;
            isMouseDraging = false;

            if (autolandMenu == null)
                autolandMenu = new ContextMenuStrip();
            autolandMenu.Items.Clear();

            foreach (var a in autolands)
            {
                int start = a.Key;
                var text = string.Format(CultureInfo.CurrentCulture,
                    "Edit autoland: rows {0} to {1}, landing heading {2:000}", start + 1,
                    start + AutolandPattern.RowCount, Math.Round(a.Value.Params.HeadingDeg) % 360);
                var item = new ToolStripMenuItem(text) {Checked = start == autolandSelected};
                item.Click += (s, e) => EditAutoland(start);
                autolandMenu.Items.Add(item);
            }

            if (autolands.Count > 0)
                autolandMenu.Items.Add(new ToolStripSeparator());

            var add = new ToolStripMenuItem(autolands.Count > 0
                ? "Add another autoland at the map center"
                : "Add autoland at the map center");
            add.Click += (s, e) => InsertAutoland(new PointLatLngAlt(MainMap.Position.Lat, MainMap.Position.Lng, 0));
            autolandMenu.Items.Add(add);

            autolandMenu.Show(MainMap, new Point(autolandIcon.Rectangle.Right + 4, autolandIcon.Rectangle.Top));
            return true;
        }

        /// <summary>Selects a sequence for editing, bringing its landing into view if it is off the map.</summary>
        private void EditAutoland(int start)
        {
            var layout = AutolandAt(start);
            if (layout == null)
                return;

            if (!MainMap.ViewArea.Contains(layout.Land.Lat, layout.Land.Lng))
                MainMap.Position = layout.Land;

            SelectAutoland(start);
        }

        private void PlaceAutolandPanel()
        {
            if (autolandPanel == null)
                return;
            autolandPanel.Location = new Point(Math.Max(0, MainMap.Width - autolandPanel.Width - 8), 34);
        }

        private static string AutolandKey(PointLatLngAlt land)
        {
            return land.Lat.ToString("F6", CultureInfo.InvariantCulture) + "," +
                   land.Lng.ToString("F6", CultureInfo.InvariantCulture);
        }

        private double? AutolandBoxWidth(PointLatLngAlt land)
        {
            if (autolandBoxWidths.TryGetValue(AutolandKey(land), out var width))
                return width;
            return Settings.Instance.GetDouble("autoland_box_width", AutolandPattern.DefaultBoxWidthM);
        }

        /// <summary>
        /// The radius a sequence's loiter is placed with: the vehicle's WP_LOITER_RAD (anticlockwise when negative,
        /// so the rows are then given a clockwise radius), else the loiter radius box, else 60 m.
        /// </summary>
        private static double AutolandLoiterRadius(out bool anticlockwise, out string source)
        {
            anticlockwise = false;
            try
            {
                var param = MainV2.comPort?.MAV?.param;
                if (param != null && param.ContainsKey("WP_LOITER_RAD"))
                {
                    double value = (double) param["WP_LOITER_RAD"];
                    if (value != 0)
                    {
                        anticlockwise = value < 0;
                        source = "WP_LOITER_RAD";
                        return Math.Abs(value);
                    }
                }
            }
            catch
            {
                // no vehicle parameters: use the box
            }

            var radius = LoiterRadiusMeters();
            if (radius.HasValue)
            {
                source = "loiter radius box";
                return radius.Value;
            }

            source = "default";
            return AutolandPattern.DefaultLoiterRadiusM;
        }

        /// <summary>
        /// A new sequence's inputs. In the panel's simple mode these are the factory defaults with the last obstacle
        /// height; in Advanced mode, the last inputs set.
        /// </summary>
        private static AutolandParams AutolandDefaults()
        {
            var s = Settings.Instance;
            if (!AutolandPanel.Advanced)
            {
                var factory = new AutolandParams();
                factory.HoldAltM = AutolandPattern.HoldForObstacle(factory,
                    s.GetDouble("autoland_obstacle_height", AutolandPattern.DefaultObstacleHeightM));
                return factory;
            }

            return new AutolandParams
            {
                HoldAltM = s.GetDouble("autoland_hold_alt", AutolandPattern.DefaultHoldAltM),
                PreApproachDeg = s.GetDouble("autoland_pre_angle", AutolandPattern.DefaultPreApproachDeg),
                FinalApproachDeg = s.GetDouble("autoland_final_angle", AutolandPattern.DefaultFinalApproachDeg),
                LoiterTimeS = s.GetDouble("autoland_loiter_time", AutolandPattern.DefaultLoiterTimeS),
                DownwindOffsetM = s.GetDouble("autoland_downwind_offset", AutolandPattern.DefaultDownwindOffsetM),
                BoxLengthM = s.GetDouble("autoland_box_length", AutolandPattern.DefaultBoxLengthM),
                BoxWidthM = s.GetDouble("autoland_box_width", AutolandPattern.DefaultBoxWidthM),
            };
        }

        /// <summary>
        /// Keeps a sequence's inputs for the next one: the obstacle height always, the rest only in Advanced mode, so
        /// simple mode leaves the advanced defaults alone.
        /// </summary>
        private static void SaveAutolandDefaults(AutolandLayout layout)
        {
            var s = Settings.Instance;
            s["autoland_obstacle_height"] = layout.ObstacleHeightM.ToString(CultureInfo.InvariantCulture);
            if (!AutolandPanel.Advanced)
                return;

            var p = layout.Params;
            s["autoland_hold_alt"] = p.HoldAltM.ToString(CultureInfo.InvariantCulture);
            s["autoland_pre_angle"] = p.PreApproachDeg.ToString(CultureInfo.InvariantCulture);
            s["autoland_final_angle"] = p.FinalApproachDeg.ToString(CultureInfo.InvariantCulture);
            s["autoland_loiter_time"] = p.LoiterTimeS.ToString(CultureInfo.InvariantCulture);
            s["autoland_downwind_offset"] = p.DownwindOffsetM.ToString(CultureInfo.InvariantCulture);
            s["autoland_box_length"] = p.BoxLengthM.ToString(CultureInfo.InvariantCulture);
            s["autoland_box_width"] = p.BoxWidthM.ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Appends a sequence landing at <paramref name="land"/>, lined up with the takeoff.</summary>
        private void InsertAutoland(PointLatLngAlt land)
        {
            if ((MAVLink.MAV_MISSION_TYPE) cmb_missiontype.SelectedValue != MAVLink.MAV_MISSION_TYPE.MISSION)
            {
                CustomMessageBox.Show("The autoland sequence is part of a mission: switch the list to Mission first.",
                    Strings.ERROR);
                return;
            }

            List<Locationwp> cmds;
            try
            {
                cmds = GetCommandList();
            }
            catch (FormatException ex)
            {
                CustomMessageBox.Show(Strings.InvalidNumberEntered + "\n" + ex.Message, Strings.ERROR);
                return;
            }

            var p = AutolandDefaults();
            p.HeadingDeg = AutolandPattern.TakeoffHeading(GetPlannedHome(), cmds);
            p.LoiterRadiusM = AutolandLoiterRadius(out var anticlockwise, out _);
            p.WriteClockwiseRadius = anticlockwise;

            var layout = AutolandPattern.Generate(land, p);

            updateUndoBuffer(true);
            int start = Commands.Rows.Count;
            WriteAutolandRows(start, layout, true);
            SelectAutoland(start);
        }

        /// <summary>
        /// Lays the sequence at <paramref name="start"/> out again from new inputs, in its own rows.
        /// </summary>
        private void UpdateAutoland(int start, AutolandParams p, PointLatLngAlt land, bool saveDefaults)
        {
            p = p.Clone();
            p.LoiterRadiusM = AutolandLoiterRadius(out var anticlockwise, out _);
            p.WriteClockwiseRadius = anticlockwise;

            var layout = AutolandPattern.Generate(land, p);

            updateUndoBuffer(true);
            WriteAutolandRows(start, layout, false);
            if (saveDefaults)
                SaveAutolandDefaults(layout);
            SelectAutoland(start);
        }

        /// <summary>
        /// Writes a sequence's rows into the grid at <paramref name="start"/>, as new rows or over its old ones, then
        /// redraws (which finds the sequence again).
        /// </summary>
        private void WriteAutolandRows(int start, AutolandLayout layout, bool insert)
        {
            autolandBoxWidths[AutolandKey(layout.Land)] = layout.Params.BoxWidthM;

            quickadd = true;
            try
            {
                for (int i = 0; i < layout.Rows.Count; i++)
                {
                    int row = start + i;
                    if (insert)
                    {
                        if (row >= Commands.Rows.Count)
                            Commands.Rows.Add();
                        else
                            Commands.Rows.Insert(row, 1);
                    }

                    WriteAutolandRow(row, layout.Rows[i]);
                }
            }
            finally
            {
                quickadd = false;
            }

            writeKML();
        }

        // as processToScreen writes a row
        private void WriteAutolandRow(int row, Locationwp wp)
        {
            var cells = Commands.Rows[row].Cells;
            var cmd = (DataGridViewComboBoxCell) cells[Command.Index];
            string name = ((MAVLink.MAV_CMD) wp.id).ToString();
            if (Program.MONO || cmd.Items.Contains(name))
            {
                cmd.Value = name;
            }
            else
            {
                cmd.Value = "UNKNOWN";
                cmd.Tag = wp.id;
            }

            double[] m;
            if (!cmdParamMultipliers.TryGetValue(cmd.Value.ToString(), out m))
                m = new double[] {1, 1, 1, 1, 1, 1, 1};

            cells[Frame.Index].Value = (int) wp.frame;
            cells[Param1.Index].Value = wp.p1 * m[0];
            cells[Param2.Index].Value = wp.p2 * m[1];
            cells[Param3.Index].Value = wp.p3 * m[2];
            cells[Param4.Index].Value = wp.p4 * m[3];
            cells[Lat.Index].Value = wp.lat * m[4];
            cells[Lon.Index].Value = wp.lng * m[5];
            cells[Alt.Index].Value = Math.Round(wp.alt * m[6], 2);
            cells[TagData.Index].Value = null;
        }

        private void RemoveSelectedAutoland()
        {
            int start = autolandSelected;
            if (start < 0 || start + AutolandPattern.RowCount > Commands.Rows.Count)
                return;

            SelectAutoland(-1);
            updateUndoBuffer(true);

            quickadd = true;
            try
            {
                Commands.CurrentCell = null;
                for (int i = 0; i < AutolandPattern.RowCount; i++)
                    Commands.Rows.RemoveAt(start);
            }
            finally
            {
                quickadd = false;
            }

            writeKML();
        }

        /// <summary>
        /// Finds the sequences in the grid again and draws them. Called at the end of every writeKML.
        /// </summary>
        private void RefreshAutolands()
        {
            if (autolandOverlay == null)
                return;

            var found = new List<KeyValuePair<int, AutolandLayout>>();
            try
            {
                if ((MAVLink.MAV_MISSION_TYPE) cmb_missiontype.SelectedValue == MAVLink.MAV_MISSION_TYPE.MISSION)
                    found = AutolandPattern.FindAll(GetCommandList(), AutolandBoxWidth);
            }
            catch (FormatException)
            {
                // writeKML has said so
            }

            autolands = found;

            // keep the selection on the same sequence: by its row, else by its LAND
            if (autolandSelected >= 0)
            {
                int keep = -1;
                if (found.Any(a => a.Key == autolandSelected))
                    keep = autolandSelected;
                else if (autolandSelectedLand != null)
                {
                    var near = found.Where(a => a.Value.Land.GetDistance(autolandSelectedLand) < 2).ToList();
                    if (near.Count > 0)
                        keep = near[0].Key;
                }

                autolandSelected = keep;
            }

            DrawAutolands();
            ShowAutolandPanel();
        }

        private AutolandLayout AutolandAt(int start)
        {
            foreach (var a in autolands)
                if (a.Key == start)
                    return a.Value;
            return null;
        }

        /// <summary>The sequence holding grid row <paramref name="row"/> (from 0), or -1.</summary>
        private int AutolandHoldingRow(int row)
        {
            foreach (var a in autolands)
                if (row >= a.Key && row < a.Key + AutolandPattern.RowCount)
                    return a.Key;
            return -1;
        }

        private void SelectAutoland(int start)
        {
            if (start < 0)
                autolandPanel?.Flush();

            var layout = start >= 0 ? AutolandAt(start) : null;
            autolandSelected = layout != null ? start : -1;
            autolandSelectedLand = layout?.Land;

            DrawAutolands();
            ShowAutolandPanel();
        }

        private void ShowAutolandPanel()
        {
            if (autolandPanel == null)
                return;

            var layout = autolandSelected >= 0 ? AutolandAt(autolandSelected) : null;
            if (layout == null)
            {
                autolandPanel.Visible = false;
                return;
            }

            AutolandLoiterRadius(out _, out var source);
            autolandPanel.ShowLayout(layout, source);
            PlaceAutolandPanel();
            autolandPanel.Visible = true;
            autolandPanel.BringToFront();
        }

        private void DrawAutolands()
        {
            if (autolandOverlay == null)
                return;

            autolandHover = null;
            autolandOverlay.Markers.Clear();
            var old = autolandOverlay.Polygons.ToList();
            autolandOverlay.Polygons.Clear();
            old.ForEach(a => a.Dispose());

            foreach (var a in autolands)
            {
                var layout = autolandDrag != null && autolandDrag.Start == a.Key ? autolandDrag.Preview : a.Value;
                bool selected = a.Key == autolandSelected;

                autolandOverlay.Polygons.Add(new GMapPolygon(layout.BoxCorners.Select(c => (PointLatLng) c).ToList(),
                    "autoland" + a.Key)
                {
                    Fill = AutolandBoxFill,
                    // amber when the final approach is steeper than recommended, so it shows with the panel closed
                    Stroke = layout.FinalApproachSteep
                        ? (selected ? AutolandBoxSteepSelectedPen : AutolandBoxSteepPen)
                        : (selected ? AutolandBoxSelectedPen : AutolandBoxPen),
                    IsHitTestVisible = false
                });

                // the altitudes down the pre-approach leg, amber when it is steeper than recommended
                foreach (var point in layout.ApproachPoints)
                    autolandOverlay.Markers.Add(new GMapMarkerAutolandAltitude(point,
                        layout.Outside(point, false),
                        (point.Alt * CurrentState.multiplieralt).ToString("0", CultureInfo.CurrentCulture) + " " +
                        CurrentState.AltUnit, layout.PreApproachSteep));

                // the obstacle height the base leg clears, at its middle (under the handles)
                autolandOverlay.Markers.Add(new GMapMarkerAutolandObstacle(layout.BaseMiddle,
                    layout.Outside(layout.BaseMiddle, true),
                    (Math.Max(0, layout.ObstacleHeightM) * CurrentState.multiplieralt).ToString("0",
                        CultureInfo.CurrentCulture) + " " + CurrentState.AltUnit));

                if (!selected)
                    continue;

                foreach (var corner in layout.BoxCorners)
                    autolandOverlay.Markers.Add(new GMapMarkerAutolandHandle(corner, a.Key, AutolandHandle.Corner)
                        {ToolTipText = "Drag to resize the landing box", ToolTipMode = MarkerTooltipMode.OnMouseOver});
                autolandOverlay.Markers.Add(new GMapMarkerAutolandHandle(layout.DownwindMiddle, a.Key,
                        AutolandHandle.Offset)
                    {ToolTipText = "Drag to move the downwind leg", ToolTipMode = MarkerTooltipMode.OnMouseOver});
                autolandOverlay.Markers.Add(new GMapMarkerAutolandHandle(layout.BaseEnd, a.Key, AutolandHandle.Rotate)
                    {ToolTipText = "Drag to turn the landing heading", ToolTipMode = MarkerTooltipMode.OnMouseOver});
                autolandOverlay.Markers.Add(new GMapMarkerAutolandHandle(layout.Land, a.Key, AutolandHandle.Move)
                    {ToolTipText = "Drag to move the landing", ToolTipMode = MarkerTooltipMode.OnMouseOver});
            }

            // above the mission, so the handles win over the waypoints they sit on
            if (MainMap.Overlays.LastOrDefault() != autolandOverlay)
            {
                MainMap.Overlays.Remove(autolandOverlay);
                MainMap.Overlays.Add(autolandOverlay);
            }
            autolandOverlay.ForceUpdate();
            MainMap.Invalidate();
        }

        private void AutolandMarkerEnter(GMapMarker item)
        {
            if (item is GMapMarkerAutolandHandle handle)
                autolandHover = handle;
        }

        private void AutolandMarkerLeave(GMapMarker item)
        {
            if (item == autolandHover)
                autolandHover = null;
        }

        /// <summary>
        /// A press on a sequence's handle, or on a waypoint of a sequence (which drags as its handle, or not at all):
        /// starts dragging it. True when the press is the sequence's.
        /// </summary>
        private bool AutolandMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || Control.ModifierKeys != Keys.None)
                return false;

            int start = -1;
            var handle = AutolandHandle.None;

            // the hover fields can outlive their markers (a redraw replaces them under the pointer), so only a marker
            // still on the map and under the press counts
            if (IsUnderPointer(autolandHover, e.Location))
            {
                start = autolandHover.SequenceStart;
                handle = autolandHover.Handle;
            }
            else if (IsUnderPointer(CurentRectMarker, e.Location) && CurentRectMarker.InnerMarker?.Tag != null &&
                     int.TryParse(CurentRectMarker.InnerMarker.Tag.ToString(), out var number))
            {
                start = AutolandHoldingRow(number - 1);
                if (start >= 0)
                    handle = AutolandLayout.HandleForRow(number - 1 - start);
            }

            var layout = start >= 0 ? AutolandAt(start) : null;
            if (layout == null)
                return false;

            autolandDrag = new AutolandDrag {Start = start, Handle = handle, From = layout, Preview = layout};
            CurentRectMarker = null;
            isMouseDown = true;
            isMouseDraging = false;
            return true;
        }

        private bool IsUnderPointer(GMapMarker marker, Point at)
        {
            return marker != null && marker.IsVisible && marker.Overlay != null &&
                   MainMap.Overlays.Contains(marker.Overlay) && marker.Overlay.Markers.Contains(marker) &&
                   marker.LocalAreaInControlSpace.Contains(at);
        }

        /// <summary>Drags the sequence's handle: redraws the sequence as it would be. True while dragging one.</summary>
        private bool AutolandMouseMove(PointLatLng point)
        {
            if (autolandDrag == null)
                return false;

            if (autolandDrag.Handle != AutolandHandle.None)
            {
                var p = AutolandPattern.Drag(autolandDrag.From, autolandDrag.Handle,
                    new PointLatLngAlt(point.Lat, point.Lng, 0), out var land);
                autolandDrag.Preview = AutolandPattern.Generate(land, p);
                DrawAutolands();
            }

            return true;
        }

        /// <summary>
        /// Ends a press on the sequence: a drag lays its rows out where it was dragged, a click selects it. True when
        /// the press was the sequence's.
        /// </summary>
        private bool AutolandMouseUp()
        {
            var drag = autolandDrag;
            if (drag == null)
                return false;

            autolandDrag = null;
            isMouseDown = false;

            if (isMouseDraging && drag.Handle != AutolandHandle.None && drag.Preview != drag.From)
                UpdateAutoland(drag.Start, drag.Preview.Params, drag.Preview.Land, true);
            else
                SelectAutoland(drag.Start);

            isMouseDraging = false;
            return true;
        }

        /// <summary>
        /// A click on the map off any marker: selects the sequence whose box it is in, or closes the selected one's
        /// panel. True when it did either, so the click adds no waypoint.
        /// </summary>
        private bool AutolandClick(PointLatLng point)
        {
            var at = new PointLatLngAlt(point.Lat, point.Lng, 0);
            foreach (var a in autolands)
            {
                if (a.Value.BoxContains(at))
                {
                    SelectAutoland(a.Key);
                    return true;
                }
            }

            if (autolandSelected < 0)
                return false;
            SelectAutoland(-1);
            return true;
        }

        /// <summary>
        /// Whether a dragged waypoint is a sequence's, which only moves with its sequence; the map is redrawn to put it
        /// back.
        /// </summary>
        private bool AutolandOwnsRow(int row)
        {
            if (AutolandHoldingRow(row) < 0)
                return false;
            writeKML();
            return true;
        }

        /// <summary>A row picked in the grid selects its sequence.</summary>
        private void AutolandRowEntered(int row)
        {
            if (!Commands.ContainsFocus)
                return;
            int start = AutolandHoldingRow(row);
            if (start >= 0 && start != autolandSelected)
                SelectAutoland(start);
        }
#endif
    }
}
