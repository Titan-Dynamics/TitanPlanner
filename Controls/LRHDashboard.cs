#if !LIB
using MissionPlanner.Controls.PPS;
using MissionPlanner.Controls.TerrainProfile;
using MissionPlanner.GCSViews;
#endif
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Linq;
using System.Reactive.Disposables;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Xml;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The Long Range Hooligans dashboard (Ctrl+L): the last minute of the battery's voltage, current and power, the
    /// efficiency (Wh/km and mAh/km), the link's packet rates each way and the radio RSSI, with the range left from the
    /// battery and the last minute's efficiency, against the distance to home.
    /// </summary>
    public class LRHDashboard : Form
    {
        private const double Window = 60;
        private const int SampleMs = 250;

        /// <summary>Range left under this many times the distance to home is getting close.</summary>
        private const double CloseRatio = 1.3;

        private static LRHDashboard open;

        private readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer {Interval = SampleMs};
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly RangeSummary summary;
        private readonly TrendChart voltage, current, power, whPerKm, mahPerKm, range, link, rssi;

        // the link's counts, from its reading and writing threads
        private MAVLinkInterface linkPort;
        private IDisposable linkSubscription;
        private long packetsDown, packetsUp, bytesDown, bytesUp;
        private readonly Queue<(double T, long Down, long Up, long BytesDown, long BytesUp)> linkCounts =
            new Queue<(double, long, long, long, long)>();

        // the last minute's current and ground speed, for the efficiency the range is worked out from
        private readonly List<(double T, double Amps, double Speed)> flight = new List<(double, double, double)>();

#if !LIB
        // the PPS export, and the Bingo and PPS tabs that use it
        private const string PpsSetting = "lrh_pps_file";
        private readonly ThemedTabStrip tabs;
        private readonly Label ppsLabel;
        private readonly BingoPanel bingoPanel;
        private readonly PpsDeltaPanel deltaPanel;
        private readonly PpsDelta ppsDelta = new PpsDelta();
        private PpsPerformance pps;
        private string ppsError;

        // the mission the bingo levels were last worked out for, rebuilt when it changes
        private string missionSignature;
        private MissionProfile missionProfile;
        private List<Locationwp> missionItems;
#endif

        /// <summary>Shows the dashboard, or brings it forward if it is open already.</summary>
        public static void ShowWindow()
        {
            if (open != null && !open.IsDisposed)
            {
                if (open.WindowState == FormWindowState.Minimized)
                    open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }

            var form = new LRHDashboard();
            form.FormClosed += (s, e) =>
            {
                if (open == form)
                    open = null;
            };
            open = form;
            ThemeManager.ApplyThemeTo(form);
            form.Show();
        }

        private LRHDashboard()
        {
            Text = "Long Range Hooligans Dashboard";
            Font = new Font("Segoe UI", 9F);
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1320, area.Width - 40), Math.Min(880, area.Height - 40));
            MinimumSize = new Size(960, 680);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            summary = new RangeSummary {Dock = DockStyle.Top, Height = 172};

            var orange = Color.FromArgb(255, 145, 27);
            voltage = new TrendChart("Battery Voltage", "V") {MinSpan = 1};
            voltage.Add("Voltage", Color.FromArgb(79, 195, 247), "0.00");
            current = new TrendChart("Current", "A") {FromZero = true, MinSpan = 2};
            current.Add("Current", Color.FromArgb(239, 83, 80), "0.0");
            power = new TrendChart("Power", "W") {FromZero = true, MinSpan = 20};
            power.Add("Power", Color.FromArgb(255, 213, 79), "0");
            range = new TrendChart("Range Left vs Distance to Home", "km") {FromZero = true, MinSpan = 1};
            range.Add("Range", Color.FromArgb(102, 187, 106), "0.0");
            range.Add("Home", Color.FromArgb(189, 189, 189), "0.0");
            whPerKm = new TrendChart("Efficiency", "Wh/km") {FromZero = true, MinSpan = 0.5};
            whPerKm.Add("Wh/km", Color.FromArgb(77, 208, 225), "0.00");
            mahPerKm = new TrendChart("Efficiency", "mAh/km") {FromZero = true, MinSpan = 20};
            mahPerKm.Add("mAh/km", Color.FromArgb(186, 104, 200), "0");
            // as the Link Stats window has them: down in green, up in orange
            link = new TrendChart("MAVLink Packets", "/s") {FromZero = true, MinSpan = 10};
            link.Add("Down", Color.LimeGreen, "0");
            link.Add("Up", orange, "0");
            rssi = new TrendChart("Radio RSSI", "") {FromZero = true, MinSpan = 20};
            rssi.Add("Local", Color.FromArgb(100, 181, 246), "0");
            rssi.Add("Remote", Color.FromArgb(240, 98, 146), "0");

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 2,
                Padding = new Padding(8, 4, 8, 8)
            };
            for (int c = 0; c < 4; c++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            for (int r = 0; r < 2; r++)
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
            var charts = new[] {voltage, current, power, range, whPerKm, mahPerKm, link, rssi};
            for (int i = 0; i < charts.Length; i++)
            {
                charts[i].Dock = DockStyle.Fill;
                charts[i].Margin = new Padding(4);
                grid.Controls.Add(charts[i], i % 4, i / 4);
            }

            // fill first so the panels docked to the top are laid out before it
#if LIB
            Controls.Add(grid);
            Controls.Add(summary);
#else
            var live = new Panel();
            live.Controls.Add(grid);
            live.Controls.Add(summary);

            bingoPanel = new BingoPanel();
            bingoPanel.MarginChanged += (s, e) => UpdateBingo(MainV2.comPort, true);
            deltaPanel = new PpsDeltaPanel();

            tabs = new ThemedTabStrip {Dock = DockStyle.Fill};
            tabs.AddTab("Live", live);
            tabs.AddTab("Bingo", bingoPanel);
            tabs.AddTab("PPS", deltaPanel);

            // the PPS export the Bingo and PPS tabs work from
            var ppsStrip = new Panel {Dock = DockStyle.Top, Height = 34, Padding = new Padding(12, 6, 12, 2)};
            var loadButton = new MyButton {Text = "Load PPS…", Dock = DockStyle.Right, Width = 100};
            loadButton.Click += (s, e) => ChoosePps();
            ppsLabel = new Label {Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true};
            ppsStrip.Controls.Add(ppsLabel);
            ppsStrip.Controls.Add(loadButton);

            Controls.Add(tabs);
            Controls.Add(ppsStrip);

            var saved = Settings.Instance[PpsSetting];
            if (!string.IsNullOrEmpty(saved))
                LoadPps(saved, false);
            else
                ShowPpsState();
#endif

            timer.Tick += (s, e) => Sample();
            Shown += (s, e) =>
            {
                Sample();
                timer.Start();
            };
            FormClosed += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();
                linkSubscription?.Dispose();
            };
        }

        private static bool IsDark(Color color) => 0.299 * color.R + 0.587 * color.G + 0.114 * color.B < 128;

        private void Subscribe(MAVLinkInterface port)
        {
            linkSubscription?.Dispose();
            linkSubscription = null;
            linkPort = port;
            linkCounts.Clear();
            if (port == null)
                return;

            linkSubscription = new CompositeDisposable(
                port.WhenPacketReceived.Subscribe(n => Interlocked.Add(ref packetsDown, n)),
                port.BytesReceived.Subscribe(n => Interlocked.Add(ref bytesDown, n)),
                // one for each packet sent
                port.BytesSent.Subscribe(n =>
                {
                    Interlocked.Increment(ref packetsUp);
                    Interlocked.Add(ref bytesUp, n);
                }));
        }

        private void Sample()
        {
            double now = clock.Elapsed.TotalSeconds;
            var port = MainV2.comPort;
            if (port != linkPort)
                Subscribe(port);

            var cs = port?.MAV?.cs;
            bool connected = port?.BaseStream?.IsOpen == true && cs != null;

            // the link's rates over the last second
            linkCounts.Enqueue((now, Interlocked.Read(ref packetsDown), Interlocked.Read(ref packetsUp),
                Interlocked.Read(ref bytesDown), Interlocked.Read(ref bytesUp)));
            while (linkCounts.Count > 2 && linkCounts.Peek().T < now - 1.0)
                linkCounts.Dequeue();
            var first = linkCounts.Peek();
            double span = now - first.T;
            double Rate(long a, long b) => span >= 0.5 ? (b - a) / span : double.NaN;
            var last = (Down: Interlocked.Read(ref packetsDown), Up: Interlocked.Read(ref packetsUp),
                BytesDown: Interlocked.Read(ref bytesDown), BytesUp: Interlocked.Read(ref bytesUp));

            if (!connected)
            {
                foreach (var chart in new[] {voltage, current, power, whPerKm, mahPerKm, range, link, rssi})
                    chart.PushNothing(now);
                flight.Clear();
                summary.SetEstimate(RangeSummary.Estimate.NotConnected);
#if !LIB
                ppsDelta.Clear();
                UpdatePpsTabs(port, false);
#endif
                Redraw();
                return;
            }

            double speed = cs.groundspeed / CurrentState.multiplierspeed;
            double NaNIfZero(double v) => v > 0 ? v : double.NaN;

            voltage.Push(now, NaNIfZero(cs.battery_voltage));
            current.Push(now, cs.current);
            power.Push(now, cs.watts);
            // 0 while not moving, where there is no efficiency
            whPerKm.Push(now, NaNIfZero(cs.battery_whperkm));
            mahPerKm.Push(now, NaNIfZero(cs.battery_mahperkm));
            link.Push(now, Rate(first.Down, last.Down), Rate(first.Up, last.Up));
            link.Footer = double.IsNaN(Rate(first.BytesDown, last.BytesDown))
                ? null
                : "Down " + ConnectionStats.ToHumanReadableByteCount((int) Rate(first.BytesDown, last.BytesDown)) +
                  "/s   ·   Up " + ConnectionStats.ToHumanReadableByteCount((int) Rate(first.BytesUp, last.BytesUp)) +
                  "/s   ·   Link quality " + cs.linkqualitygcs + "%";
            // RADIO_STATUS, in the radio's own units; 0 from a radio that doesn't send it
            rssi.Push(now, NaNIfZero(cs.rssi), NaNIfZero(cs.remrssi));

            flight.Add((now, cs.current, speed));
            flight.RemoveAll(f => f.T < now - Window);

            var estimate = Estimate(port, cs);
            range.Push(now, estimate.RangeKm, estimate.HomeKm);
            summary.SetEstimate(estimate);

#if !LIB
            if (pps != null)
                ppsDelta.Add(new PpsDelta.Reading
                {
                    T = now,
                    Armed = cs.armed,
                    Airspeed = cs.airspeed / CurrentState.multiplierspeed,
                    ClimbRate = cs.climbrate / CurrentState.multiplierspeed,
                    Roll = cs.roll,
                    Pitch = cs.pitch,
                    Watts = cs.watts,
                    Volts = cs.battery_voltage,
                    Throttle = cs.ch3percent,
                    GroundWhPerKm = cs.battery_whperkm,
                    Rpm = cs.esc1_rpm > 0 ? cs.esc1_rpm : cs.rpm1
                }, pps);
            UpdatePpsTabs(port, true);
#endif
            Redraw();
        }

#if !LIB
        private void ChoosePps()
        {
            using (var dialog = new OpenFileDialog
                   {
                       Title = "Load a PPS export",
                       Filter = "PPS export (*.csv)|*.csv|All files (*.*)|*.*"
                   })
            {
                var current = Settings.Instance[PpsSetting];
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        dialog.InitialDirectory = System.IO.Path.GetDirectoryName(current);
                    }
                    catch (ArgumentException)
                    {
                    }
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                    LoadPps(dialog.FileName, true);
            }
        }

        private void LoadPps(string path, bool chosen)
        {
            try
            {
                pps = PpsPerformance.Load(path);
                ppsError = null;
                Settings.Instance[PpsSetting] = path;
            }
            catch (Exception ex) when (ex is System.IO.IOException || ex is FormatException ||
                                       ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                pps = null;
                ppsError = "Could not load " + System.IO.Path.GetFileName(path) + ": " + ex.Message;
                if (chosen)
                    CustomMessageBox.Show(ppsError, "PPS export");
            }

            ppsDelta.Clear();
            ppsDelta.Points.Clear();
            missionSignature = null;
            ShowPpsState();
            UpdatePpsTabs(MainV2.comPort, MainV2.comPort?.BaseStream?.IsOpen == true && MainV2.comPort.MAV?.cs != null);
        }

        private void ShowPpsState()
        {
            if (pps == null)
            {
                ppsLabel.Text = ppsError ?? "No PPS export loaded: load one for the Bingo and PPS tabs";
                return;
            }

            ppsLabel.Text = "PPS: " + System.IO.Path.GetFileName(pps.Path) + "   ·   " +
                            (double.IsNaN(pps.UsableWh) ? "battery energy not given" : pps.UsableWh.ToString("0") + " Wh") +
                            "   ·   " + pps.MinAirspeed.ToString("0.0") + " to " + pps.MaxAirspeed.ToString("0.0") + " m/s";
        }

        private void UpdatePpsTabs(MAVLinkInterface port, bool connected)
        {
            UpdateBingo(port, connected);
            if (pps == null)
                deltaPanel.ShowMessage("Load a PPS export to compare the aircraft with its prediction.");
            else
                deltaPanel.Show(pps, ppsDelta, connected);
        }

        /// <summary>
        /// The bingo levels: the vehicle's mission while connected (from the waypoint it is flying to), else the plan
        /// on the PLAN screen from a full battery.
        /// </summary>
        private void UpdateBingo(MAVLinkInterface port, bool connected)
        {
            if (pps == null)
            {
                bingoPanel.ShowMessage("Load a PPS export to see the bingo battery level at each waypoint.");
                return;
            }

            var mav = port?.MAV;
            var cs = mav?.cs;
            string source;
            ProfileInput input;
            bool vehicleMission = false;
            try
            {
                input = connected ? VehicleMission(mav) : null;
                if (input != null)
                {
                    source = "Vehicle mission";
                    vehicleMission = true;
                }
                else
                {
                    input = FlightPlanner.instance?.GetPlanForBingo();
                    source = "PLAN screen";
                }
            }
            catch (Exception ex)
            {
                // a cell of the plan that isn't a number, or the parameters held by a download
                Debug.WriteLine("LRH bingo: " + ex.Message);
                return;
            }

            if (input == null)
            {
                bingoPanel.ShowMessage("No mission: plan one on the PLAN screen, or connect to a vehicle with one.");
                return;
            }

            input.VehicleClass = VehicleClass.Plane;
            var signature = input.Signature();
            if (signature != missionSignature)
            {
                missionSignature = signature;
                missionItems = input.Mission;
                missionProfile = MissionProfile.Build(input, (lat, lng) => double.NaN);
            }

            double Param(string name)
            {
                var value = mav?.param[name];
                return value != null ? value.Value : double.NaN;
            }

            double cruise = Param("AIRSPEED_CRUISE");
            if (!(cruise > 0) && Param("TRIM_ARSPD_CM") > 0)
                cruise = Param("TRIM_ARSPD_CM") / 100.0;
            if (!(cruise > 0))
                cruise = pps.CruiseSpeed;

            double homeAlt = missionProfile != null && missionProfile.Waypoints.Count > 0
                ? missionProfile.Waypoints[0].Altitude
                : double.NaN;
            double rtl = double.NaN;
            if (Param("RTL_ALTITUDE") > 0)
                rtl = homeAlt + Param("RTL_ALTITUDE");
            else if (Param("ALT_HOLD_RTL") > 0)
                rtl = homeAlt + Param("ALT_HOLD_RTL") / 100.0;

            bool live = connected && cs != null && (cs.lat != 0 || cs.lng != 0);
            double windSpeed = live ? cs.wind_vel / CurrentState.multiplierspeed : 0;
            double windFrom = live ? cs.wind_dir : 0;
            double usedWh = 0;
            if (live)
                usedWh = cs.battery_usedwh > 0 ? cs.battery_usedwh : cs.battery_usedmah / 1000.0 * cs.battery_voltage;

            var result = BingoPlan.Compute(new BingoInput
            {
                Profile = missionProfile,
                Mission = missionItems,
                Pps = pps,
                CruiseAirspeed = cruise,
                RtlAltitude = rtl,
                WindSpeed = windSpeed,
                WindFrom = windFrom,
                MarginPercent = bingoPanel.MarginPercent,
                LoiterRadius = Param("WP_LOITER_RAD") is double r && Math.Abs(r) > 1 ? Math.Abs(r) : 60,
                Live = live,
                UsedWh = usedWh,
                // the vehicle's mission starts with home, which the list leaves out
                NextItem = live && vehicleMission && cs.wpno >= 1 ? (int) cs.wpno - 1 : -1,
                Lat = live ? cs.lat : 0,
                Lng = live ? cs.lng : 0,
                AltitudeAmsl = live ? cs.altasl / CurrentState.multiplieralt : 0
            });

            // the autopilot's battery against the energy PPS flies on
            double capacity = Param("BATT_CAPACITY");
            if (live && capacity > 0 && cs.battery_voltage > 1)
            {
                int cells = (int) Math.Ceiling(cs.battery_voltage / 4.25);
                double wh = capacity / 1000.0 * cells * 3.7;
                if (Math.Abs(wh - pps.UsableWh) > pps.UsableWh * 0.1)
                    result.Notes.Insert(0, "BATT_CAPACITY (about " + wh.ToString("0") + " Wh) differs from PPS's " +
                                           pps.UsableWh.ToString("0") + " Wh.");
            }

            bingoPanel.Show(result, source, cruise, windSpeed, windFrom);
        }

        /// <summary>The mission downloaded from the vehicle, home first; null when there is none.</summary>
        private static ProfileInput VehicleMission(MAVState mav)
        {
            if (mav == null || mav.wps.Count < 2)
                return null;

            var items = mav.wps.OrderBy(a => a.Key).Select(a => (Locationwp) a.Value).ToList();
            bool ardupilot = mav.apname == MAVLink.MAV_AUTOPILOT.ARDUPILOTMEGA;
            PointLatLngAlt home = null;
            if (ardupilot && (items[0].lat != 0 || items[0].lng != 0))
                home = new PointLatLngAlt(items[0].lat, items[0].lng, items[0].alt);
            else if (mav.cs.HomeLocation != null && (mav.cs.HomeLocation.Lat != 0 || mav.cs.HomeLocation.Lng != 0))
                home = new PointLatLngAlt(mav.cs.HomeLocation);

            return new ProfileInput
            {
                Home = home,
                Mission = ardupilot ? items.Skip(1).ToList() : items
            };
        }
#endif

        private void Redraw()
        {
            foreach (var chart in new[] {voltage, current, power, whPerKm, mahPerKm, range, link, rssi})
                chart.Invalidate();
        }

        /// <summary>
        /// The range left: the charge left over the last minute's efficiency, the mean current over the mean ground
        /// speed while moving (a minute's consumption over its distance, not an average of instants).
        /// </summary>
        private RangeSummary.Estimate Estimate(MAVLinkInterface port, CurrentState cs)
        {
            var estimate = new RangeSummary.Estimate
            {
                Connected = true,
                Mode = cs.mode,
                Armed = cs.armed,
                HomeKm = cs.DistToHome / CurrentState.multiplierdist / 1000.0,
                BatteryPercent = cs.battery_remaining,
                UsedMah = cs.battery_usedmah
            };

            // the charge left: from the capacity set, else from the share of it used
            var capacityParam = port.MAV.param["BATT_CAPACITY"];
            double capacity = capacityParam != null ? capacityParam.Value : 0;
            if (capacity > 0)
                estimate.LeftMah = Math.Max(0, capacity - cs.battery_usedmah);
            else if (cs.battery_remaining > 0 && cs.battery_remaining < 100 && cs.battery_usedmah > 0)
                estimate.LeftMah = cs.battery_usedmah * cs.battery_remaining / (100.0 - cs.battery_remaining);

            var moving = flight.Where(f => f.Speed > 1).ToList();
            // a few seconds of it
            if (moving.Count >= 12)
            {
                double amps = moving.Average(f => f.Amps);
                double speed = moving.Average(f => f.Speed);
                if (amps > 0 && speed > 1)
                    estimate.MahPerKm = amps * 1000.0 / (speed * 3.6);
            }

            if (flight.Count > 0)
            {
                double amps = flight.Average(f => f.Amps);
                if (amps > 0.1 && !double.IsNaN(estimate.LeftMah))
                    estimate.MinutesLeft = estimate.LeftMah / (amps * 1000.0) * 60.0;
            }

            if (!double.IsNaN(estimate.LeftMah) && !double.IsNaN(estimate.MahPerKm))
                estimate.RangeKm = estimate.LeftMah / estimate.MahPerKm;

            return estimate;
        }

        /// <summary>
        /// The logo down the left; the range left, the distance to home and the rest beside it; and under both, how the
        /// range compares with the distance to home.
        /// </summary>
        private class RangeSummary : Control
        {
            public class Estimate
            {
                public static readonly Estimate NotConnected = new Estimate();

                public bool Connected;
                public string Mode;
                public bool Armed;
                public double RangeKm = double.NaN;
                public double HomeKm = double.NaN;
                public double LeftMah = double.NaN;
                public double MahPerKm = double.NaN;
                public double MinutesLeft = double.NaN;
                public double UsedMah = double.NaN;
                public int BatteryPercent = -1;
            }

            private static readonly Font CaptionFont = new Font("Segoe UI", 9F);
            // a number too long for its cell steps down a size rather than being cut short
            private static readonly Font[] ValueFonts = new[] {18F, 16F, 14F, 12F}
                .Select(size => new Font("Segoe UI", size, FontStyle.Bold)).ToArray();
            private static readonly Font BannerFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);

            private const int Pad = 16;
            private const int BannerHeight = 32;

            private Estimate shown = Estimate.NotConnected;
            private int flash;

            public RangeSummary()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            public void SetEstimate(Estimate estimate)
            {
                shown = estimate;
                flash++;
                Invalidate();
            }

            private enum Level
            {
                Unknown,
                Ok,
                Close,
                Short
            }

            private Level Status(out string message)
            {
                var e = shown;
                if (!e.Connected)
                {
                    message = "Not connected";
                    return Level.Unknown;
                }

                if (double.IsNaN(e.LeftMah))
                {
                    message = "No range estimate: set BATT_CAPACITY, or use a battery monitor that reports its remaining %";
                    return Level.Unknown;
                }

                if (double.IsNaN(e.RangeKm))
                {
                    message = "The range estimate starts after a few seconds of flying (over 1 m/s)";
                    return Level.Unknown;
                }

                if (double.IsNaN(e.HomeKm) || e.HomeKm < 0.05)
                {
                    message = "At home: " + e.RangeKm.ToString("0.0") + " km of range left";
                    return Level.Ok;
                }

                double ratio = e.RangeKm / e.HomeKm;
                if (ratio < 1)
                {
                    message = "NOT ENOUGH RANGE TO GET HOME: " + e.RangeKm.ToString("0.0") + " km left, home is " +
                              e.HomeKm.ToString("0.0") + " km away";
                    return Level.Short;
                }

                if (ratio < CloseRatio)
                {
                    message = "Getting close: the range left is only " + ratio.ToString("0.0") + "× the distance to home";
                    return Level.Close;
                }

                message = "The range left is " + ratio.ToString("0.0") + "× the distance to home";
                return Level.Ok;
            }

            protected override void OnPaint(PaintEventArgs pe)
            {
                var g = pe.Graphics;
                var back = BackColor;
                var fore = ForeColor;
                var dim = TrendChart.Blend(back, fore, 0.55);
                var level = Status(out var message);
                var e = shown;

                Color levelColor;
                switch (level)
                {
                    case Level.Ok:
                        levelColor = Color.FromArgb(56, 142, 60);
                        break;
                    case Level.Close:
                        levelColor = Color.FromArgb(245, 166, 35);
                        break;
                    case Level.Short:
                        levelColor = Color.FromArgb(211, 47, 47);
                        break;
                    default:
                        levelColor = TrendChart.Blend(back, fore, 0.12);
                        break;
                }

                string Km(double v) => double.IsNaN(v) ? "–" : v.ToString("0.0") + " km";
                var stats = new[]
                {
                    ("Range left", Km(e.RangeKm), level == Level.Ok || level == Level.Unknown ? fore : levelColor),
                    ("Distance to home", Km(e.HomeKm), fore),
                    ("Margin", double.IsNaN(e.RangeKm) || double.IsNaN(e.HomeKm)
                        ? "–"
                        : (e.RangeKm - e.HomeKm).ToString("+0.0;-0.0") + " km", fore),
                    ("Time left", double.IsNaN(e.MinutesLeft) ? "–" : e.MinutesLeft.ToString("0") + " min", fore),
                    ("Efficiency (60 s)", double.IsNaN(e.MahPerKm) ? "–" : e.MahPerKm.ToString("0") + " mAh/km", fore),
                    ("Battery", e.BatteryPercent < 0 ? "–" : e.BatteryPercent + "%", fore),
                    ("Used", double.IsNaN(e.UsedMah) ? "–" : e.UsedMah.ToString("#,0") + " mAh", fore),
                    ("Mode", e.Connected ? e.Mode + (e.Armed ? "  ·  Armed" : "  ·  Disarmed") : "Not connected", fore),
                };

                // the logo down the left, as tall as the numbers beside it: white on a dark theme, black on a light one
                int top = 12;
                int bottom = Height - BannerHeight - Pad;
                var logo = new RectangleF(Pad, top, Width * 0.3f, bottom - top);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                float logoWidth = LRHLogo.Draw(g, logo, IsDark(back) ? Color.White : Color.Black);
                g.SmoothingMode = SmoothingMode.Default;

                // the numbers right of it, four across and two down
                int left = Pad + (int) logoWidth + 32;
                int width = Width - Pad - left;
                const int across = 4;
                int rowHeight = (bottom - top) / 2;
                for (int i = 0; i < stats.Length; i++)
                {
                    var (caption, value, color) = stats[i];
                    int x = left + width * (i % across) / across;
                    int y = top + rowHeight * (i / across);
                    int w = width / across - 8;
                    TextRenderer.DrawText(g, caption, CaptionFont, new Rectangle(x, y, w, 18), dim,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix);
                    var font = ValueFonts.FirstOrDefault(f =>
                                   TextRenderer.MeasureText(g, value, f, Size.Empty, TextFormatFlags.NoPrefix).Width <= w)
                               ?? ValueFonts[ValueFonts.Length - 1];
                    TextRenderer.DrawText(g, value, font, new Rectangle(x, y + 17, w, 36), color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);
                }

                // the verdict under all of it, flashing while there isn't the range to get home
                var banner = new Rectangle(Pad, Height - BannerHeight - 8, Width - Pad * 2, BannerHeight);
                bool lit = level != Level.Short || flash / 2 % 2 == 0;
                var bannerBack = lit ? levelColor : TrendChart.Blend(back, levelColor, 0.35);
                using (var brush = new SolidBrush(bannerBack))
                    g.FillRectangle(brush, banner);
                var bannerText = level == Level.Unknown ? fore : level == Level.Close ? Color.Black : Color.White;
                TextRenderer.DrawText(g, message, BannerFont, banner, bannerText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>
    /// A minute of one or more readings: the latest large, their line over the minute, and the minute's range.
    /// </summary>
    internal class TrendChart : Control
    {
        public class Series
        {
            public string Name;
            public Color Color;
            public string Format;
            public readonly List<(double T, double V)> Points = new List<(double, double)>();

            public double Last => Points.Count == 0 ? double.NaN : Points[Points.Count - 1].V;
        }

        private const double Window = 60;

        private static readonly Font TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
        private static readonly Font ValueFont = new Font("Segoe UI", 20F, FontStyle.Bold);
        private static readonly Font PairValueFont = new Font("Segoe UI", 15F, FontStyle.Bold);
        private static readonly Font SmallFont = new Font("Segoe UI", 8.25F);

        private readonly string title;
        private readonly string unit;
        private readonly List<Series> lines = new List<Series>();
        private double now;

        /// <summary>The axis starts at 0 (amounts, rates); otherwise it fits the readings.</summary>
        public bool FromZero { get; set; }

        /// <summary>The least the axis spans, so a steady reading's noise doesn't fill the chart.</summary>
        public double MinSpan { get; set; }

        /// <summary>A line under the chart in place of the minute's low, mean and high.</summary>
        public string Footer { get; set; }

        public TrendChart(string title, string unit)
        {
            this.title = title;
            this.unit = unit;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        public static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

        public void Add(string name, Color color, string format)
        {
            lines.Add(new Series {Name = name, Color = color, Format = format});
        }

        /// <summary>A reading of each line, in the order added; NaN for none.</summary>
        public void Push(double time, params double[] values)
        {
            now = time;
            for (int i = 0; i < lines.Count; i++)
            {
                var v = i < values.Length ? values[i] : double.NaN;
                lines[i].Points.Add((time, double.IsInfinity(v) ? double.NaN : v));
                lines[i].Points.RemoveAll(p => p.T < time - Window - 1);
            }
        }

        public void PushNothing(double time)
        {
            Push(time, lines.Select(l => double.NaN).ToArray());
        }

        private string Value(Series s)
        {
            return double.IsNaN(s.Last) ? "–" : s.Last.ToString(s.Format, CultureInfo.CurrentCulture);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = BackColor;
            var fore = ForeColor;
            var tile = Blend(back, fore, 0.05);
            var border = Blend(back, fore, 0.12);
            var gridColor = Blend(back, fore, 0.09);
            var dim = Blend(back, fore, 0.55);

            var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var brush = new SolidBrush(tile))
                g.FillRectangle(brush, bounds);
            using (var pen = new Pen(border))
                g.DrawRectangle(pen, bounds);

            const int pad = 10;
            TextRenderer.DrawText(g, title, TitleFont, new Point(pad, 8), dim, TextFormatFlags.NoPrefix);

            // the latest: one large, or each of a pair beside its name
            if (lines.Count == 1)
            {
                var text = Value(lines[0]);
                TextRenderer.DrawText(g, text, ValueFont, new Point(pad - 2, 24), fore, TextFormatFlags.NoPrefix);
                var size = TextRenderer.MeasureText(g, text, ValueFont, Size.Empty, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, unit, SmallFont, new Point(pad + size.Width - 4, 40), dim,
                    TextFormatFlags.NoPrefix);
            }
            else
            {
                int x = pad;
                foreach (var s in lines)
                {
                    TextRenderer.DrawText(g, s.Name, SmallFont, new Point(x, 28), s.Color, TextFormatFlags.NoPrefix);
                    x += TextRenderer.MeasureText(g, s.Name, SmallFont, Size.Empty, TextFormatFlags.NoPrefix).Width;
                    var text = Value(s) + (unit.Length > 0 ? " " + unit : "");
                    TextRenderer.DrawText(g, text, PairValueFont, new Point(x, 22), fore, TextFormatFlags.NoPrefix);
                    x += TextRenderer.MeasureText(g, text, PairValueFont, Size.Empty, TextFormatFlags.NoPrefix).Width +
                         12;
                }
            }

            var plot = new Rectangle(pad + 36, 66, Width - pad * 2 - 40, Height - 66 - 42);
            if (plot.Width < 20 || plot.Height < 20)
                return;

            // the minute's span of every line
            var values = lines.SelectMany(l => l.Points).Select(p => p.V).Where(v => !double.IsNaN(v)).ToList();
            if (values.Count == 0)
            {
                TextRenderer.DrawText(g, "No data", SmallFont, plot, dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            double min = values.Min(), max = values.Max();
            if (FromZero)
                min = Math.Min(0, min);
            if (max - min < MinSpan)
            {
                // about the readings, or up from 0
                double mid = (max + min) / 2;
                min = mid - MinSpan / 2;
                max = mid + MinSpan / 2;
                if (FromZero && min < 0 && values.Min() >= 0)
                {
                    max -= min;
                    min = 0;
                }
            }
            if (max - min < 1e-6)
            {
                double half = Math.Max(Math.Abs(max) * 0.05, 0.5);
                min -= FromZero && min >= 0 ? 0 : half;
                max += half;
            }
            else
            {
                max += (max - min) * 0.08;
                if (!FromZero)
                    min -= (max - min) * 0.08;
            }

            // grid: three levels, and every 15 s
            var format = lines[0].Format;
            using (var pen = new Pen(gridColor))
            {
                for (int i = 0; i <= 2; i++)
                {
                    int y = plot.Bottom - plot.Height * i / 2;
                    g.DrawLine(pen, plot.Left, y, plot.Right, y);
                    var level = min + (max - min) * i / 2;
                    TextRenderer.DrawText(g, level.ToString(format), SmallFont,
                        new Rectangle(0, y - 8, plot.Left - 4, 16), dim,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }

                for (int s = 0; s <= 60; s += 15)
                {
                    int x = plot.Left + plot.Width * s / 60;
                    g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                }
            }

            TextRenderer.DrawText(g, "-60 s", SmallFont, new Point(plot.Left - 4, plot.Bottom + 2), dim,
                TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "-30 s", SmallFont,
                new Rectangle(plot.Left, plot.Bottom + 2, plot.Width, 14), dim,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, "now", SmallFont, new Rectangle(plot.Left, plot.Bottom + 2, plot.Width, 14), dim,
                TextFormatFlags.Right | TextFormatFlags.NoPrefix);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var clip = g.Clip;
            g.SetClip(plot);
            float X(double t) => (float) (plot.Left + (t - (now - Window)) / Window * plot.Width);
            float Y(double v) => (float) (plot.Bottom - (v - min) / (max - min) * plot.Height);

            for (int li = lines.Count - 1; li >= 0; li--)
            {
                var series = lines[li];
                // runs of readings, broken where there were none
                var run = new List<PointF>();

                void Flush()
                {
                    if (run.Count >= 2)
                    {
                        if (lines.Count == 1)
                        {
                            var area = new List<PointF>(run)
                            {
                                new PointF(run[run.Count - 1].X, plot.Bottom),
                                new PointF(run[0].X, plot.Bottom)
                            };
                            using (var fill = new SolidBrush(Color.FromArgb(40, series.Color)))
                                g.FillPolygon(fill, area.ToArray());
                        }

                        using (var pen = new Pen(series.Color, 2f))
                            g.DrawLines(pen, run.ToArray());
                    }
                    else if (run.Count == 1)
                    {
                        using (var brush = new SolidBrush(series.Color))
                            g.FillEllipse(brush, run[0].X - 2, run[0].Y - 2, 4, 4);
                    }

                    run.Clear();
                }

                double lastT = double.NaN;
                foreach (var (t, v) in series.Points)
                {
                    if (double.IsNaN(v) || (!double.IsNaN(lastT) && t - lastT > 1.5))
                        Flush();
                    if (!double.IsNaN(v))
                        run.Add(new PointF(X(t), Y(v)));
                    lastT = t;
                }

                Flush();
            }

            g.Clip = clip;
            g.SmoothingMode = SmoothingMode.Default;

            // the minute's low, mean and high of the first line, or what the window says instead
            var footer = Footer;
            if (footer == null)
            {
                var first = lines[0].Points.Select(p => p.V).Where(v => !double.IsNaN(v)).ToList();
                if (first.Count > 0)
                    footer = "low " + first.Min().ToString(format) + "   ·   mean " + first.Average().ToString(format) +
                             "   ·   high " + first.Max().ToString(format) + (unit.Length > 0 ? " " + unit : "");
            }

            if (footer != null)
                TextRenderer.DrawText(g, footer, SmallFont, new Rectangle(pad, Height - 22, Width - pad * 2, 16), dim,
                    TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>
    /// The Long Range Hooligans logo (Resources/LRH.svg), one even-odd path of absolute moves, lines and cubic curves,
    /// drawn in one colour at any size.
    /// </summary>
    internal static class LRHLogo
    {
        private static GraphicsPath path;
        private static RectangleF box;

        private static GraphicsPath Path
        {
            get
            {
                if (path == null)
                    Load();
                return path;
            }
        }

        private static void Load()
        {
            var result = new GraphicsPath(FillMode.Alternate);
            box = new RectangleF(0, 0, 2048, 758);
            try
            {
                using (var stream = typeof(LRHLogo).Assembly.GetManifestResourceStream("MissionPlanner.Resources.LRH.svg"))
                {
                    var doc = new XmlDocument();
                    doc.Load(stream);
                    var viewBox = doc.DocumentElement?.GetAttribute("viewBox");
                    if (!string.IsNullOrEmpty(viewBox))
                    {
                        var v = viewBox.Split(new[] {' ', ','}, StringSplitOptions.RemoveEmptyEntries)
                            .Select(s => float.Parse(s, CultureInfo.InvariantCulture)).ToArray();
                        if (v.Length == 4)
                            box = new RectangleF(v[0], v[1], v[2], v[3]);
                    }

                    foreach (XmlElement element in doc.GetElementsByTagName("path"))
                        AddPath(result, element.GetAttribute("d"));
                }
            }
            catch (Exception ex)
            {
                // without the logo the header is just the status
                Debug.WriteLine("LRH logo: " + ex.Message);
            }

            path = result;
        }

        private static void AddPath(GraphicsPath target, string d)
        {
            var tokens = Regex.Matches(d, @"[MmLlCcZz]|-?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?")
                .Cast<Match>().Select(m => m.Value).ToList();
            int i = 0;
            char command = 'M';
            var at = PointF.Empty;
            var start = PointF.Empty;

            float Number() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);

            PointF Point(bool relative)
            {
                var p = new PointF(Number(), Number());
                return relative ? new PointF(at.X + p.X, at.Y + p.Y) : p;
            }

            while (i < tokens.Count)
            {
                if (char.IsLetter(tokens[i][0]))
                    command = tokens[i++][0];

                bool relative = char.IsLower(command);
                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        target.StartFigure();
                        at = start = Point(relative);
                        // pairs after a move are lines
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L':
                    {
                        var p = Point(relative);
                        target.AddLine(at, p);
                        at = p;
                        break;
                    }
                    case 'C':
                    {
                        var c1 = Point(relative);
                        var c2 = Point(relative);
                        var p = Point(relative);
                        target.AddBezier(at, c1, c2, p);
                        at = p;
                        break;
                    }
                    case 'Z':
                        target.CloseFigure();
                        at = start;
                        break;
                    default:
                        // a command this logo doesn't use: stop here rather than misdraw
                        return;
                }
            }
        }

        /// <summary>
        /// Draws the logo as large as fits the height (and width) of the bounds, at their left, and gives the width it
        /// took.
        /// </summary>
        public static float Draw(Graphics g, RectangleF bounds, Color color)
        {
            var logo = Path;
            if (logo.PointCount == 0 || box.Width <= 0 || box.Height <= 0)
                return 0;

            float scale = Math.Min(bounds.Width / box.Width, bounds.Height / box.Height);
            using (var matrix = new System.Drawing.Drawing2D.Matrix())
            using (var drawn = (GraphicsPath) logo.Clone())
            using (var brush = new SolidBrush(color))
            {
                matrix.Translate(bounds.X, bounds.Y + (bounds.Height - box.Height * scale) / 2);
                matrix.Scale(scale, scale);
                matrix.Translate(-box.X, -box.Y);
                drawn.Transform(matrix);
                g.FillPath(brush, drawn);
            }

            return box.Width * scale;
        }
    }
}
