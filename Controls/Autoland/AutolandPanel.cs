using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// The inputs of the selected fixed wing autoland sequence, over the PLAN map. A change is handed on a moment
    /// after the last arrow click or wheel turn, or when typing is committed, so the sequence follows the panel.
    ///
    /// It shows only the obstacle height (which sets the base leg's height, and with it the hold altitude) until
    /// Advanced mode is ticked, which shows every input. The tick is kept in the settings.
    /// </summary>
    public class AutolandPanel : UserControl
    {
        /// <summary>The sequence's new inputs, in meters and degrees.</summary>
        public event Action<AutolandParams> ParamsChanged;

        public event Action RemoveClicked;
        public event Action CloseClicked;

        const string AdvancedSetting = "autoland_advanced";

        /// <summary>Whether the panel shows every input, kept across restarts. Off, new sequences take the factory
        /// defaults for all but the obstacle height.</summary>
        public static bool Advanced
        {
            get => Settings.Instance.GetBoolean(AdvancedSetting, false);
            private set
            {
                Settings.Instance[AdvancedSetting] = value.ToString();
                Settings.Instance.Save();
            }
        }

        readonly NumericUpDown obstacle;
        readonly CheckBox advanced;
        readonly List<Control> advancedControls = new List<Control>();
        readonly NumericUpDown heading;
        readonly NumericUpDown holdAlt;
        readonly NumericUpDown preAngle;
        readonly NumericUpDown finalAngle;
        readonly NumericUpDown loiterTime;
        readonly NumericUpDown downwindOffset;
        readonly NumericUpDown boxLength;
        readonly NumericUpDown boxWidth;
        readonly Label altUnitObstacle;
        readonly Label altUnitHold;
        readonly Label distUnitOffset;
        readonly Label distUnitLength;
        readonly Label distUnitWidth;
        readonly Label readouts;
        readonly Label warnings;
        readonly Timer debounce = new Timer {Interval = 300};

        AutolandParams shown;
        double shownObstacle;
        AutolandLayout shownLayout;
        string shownSource;
        bool loading;

        public AutolandPanel()
        {
            Width = 270;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(8);
            BorderStyle = BorderStyle.FixedSingle;

            var layout = new TableLayoutPanel
            {
                ColumnCount = 3,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            var title = new Label
            {
                Text = "Fixed Wing Autoland",
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(3, 0, 3, 6)
            };
            layout.Controls.Add(title, 0, 0);
            layout.SetColumnSpan(title, 3);
            layout.RowCount = 1;

            obstacle = AddRow(layout, "Obstacle height", 0, 10000, 0, out altUnitObstacle, "m", false);
            obstacle.Increment = 5;

            advanced = new CheckBox
            {
                Text = "Advanced mode", AutoSize = true, Checked = Advanced, Margin = new Padding(3, 4, 3, 2)
            };
            advanced.CheckedChanged += (s, e) =>
            {
                Advanced = advanced.Checked;
                ShowMode();
            };
            layout.Controls.Add(advanced, 0, layout.RowCount++);
            layout.SetColumnSpan(advanced, 3);

            heading = AddRow(layout, "Landing heading", 0, 359.9m, 1, out _, "deg", true);
            holdAlt = AddRow(layout, "Hold altitude", 1, 10000, 0, out altUnitHold, "m", true);
            preAngle = AddRow(layout, "Pre-approach angle", (decimal) AutolandPattern.MinAngleDeg,
                (decimal) AutolandPattern.MaxAngleDeg, 1, out _, "deg", true);
            finalAngle = AddRow(layout, "Final approach angle", (decimal) AutolandPattern.MinAngleDeg,
                (decimal) AutolandPattern.MaxAngleDeg, 1, out _, "deg", true);
            loiterTime = AddRow(layout, "Loiter time", 0, 3600, 0, out _, "s", true);
            downwindOffset = AddRow(layout, "Downwind offset", 1, 100000, 0, out distUnitOffset, "m", true);
            boxLength = AddRow(layout, "Box length", 1, 100000, 0, out distUnitLength, "m", true);
            boxWidth = AddRow(layout, "Box width", 1, 100000, 0, out distUnitWidth, "m", true);

            heading.Increment = 1;
            preAngle.Increment = 0.5m;
            finalAngle.Increment = 0.5m;
            loiterTime.Increment = 5;
            downwindOffset.Increment = 10;
            boxLength.Increment = 5;

            readouts = new Label {AutoSize = true, Margin = new Padding(3, 6, 3, 0)};
            layout.Controls.Add(readouts, 0, layout.RowCount++);
            layout.SetColumnSpan(readouts, 3);

            warnings = new Label {AutoSize = true, ForeColor = Color.Orange, Margin = new Padding(3, 4, 3, 0)};
            layout.Controls.Add(warnings, 0, layout.RowCount++);
            layout.SetColumnSpan(warnings, 3);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight,
                Margin = new Padding(0, 6, 0, 0),
                WrapContents = false
            };
            var remove = new MyButton {Text = "Remove sequence", Width = 120, Height = 24};
            remove.Click += (s, e) => RemoveClicked?.Invoke();
            var close = new MyButton {Text = "Done", Width = 70, Height = 24};
            close.Click += (s, e) => CloseClicked?.Invoke();
            buttons.Controls.Add(remove);
            buttons.Controls.Add(close);
            layout.Controls.Add(buttons, 0, layout.RowCount++);
            layout.SetColumnSpan(buttons, 3);

            Controls.Add(layout);

            debounce.Tick += (s, e) =>
            {
                debounce.Stop();
                Apply();
            };

            ThemeManager.ApplyThemeTo(this);
            ShowMode();
        }

        void ShowMode()
        {
            bool on = advanced.Checked;
            SuspendLayout();
            foreach (var control in advancedControls)
                control.Visible = on;
            ResumeLayout(true);
            if (shownLayout != null)
                ShowReadouts(shownLayout, shownSource);
        }

        NumericUpDown AddRow(TableLayoutPanel layout, string name, decimal min, decimal max, int decimals,
            out Label unitLabel, string unit, bool advancedOnly)
        {
            int row = layout.RowCount++;
            var label = new Label
            {
                Text = name, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 4, 6, 0)
            };
            layout.Controls.Add(label, 0, row);

            var box = new NumericUpDown
            {
                Minimum = min,
                Maximum = max,
                DecimalPlaces = decimals,
                Width = 76,
                TextAlign = HorizontalAlignment.Right,
                Margin = new Padding(0, 1, 0, 1)
            };
            // typing commits on Enter, Tab or leaving the box; the arrows and the wheel at once
            box.ValueChanged += (s, e) => Changed();
            layout.Controls.Add(box, 1, row);

            unitLabel = new Label {Text = unit, AutoSize = true, Anchor = AnchorStyles.Left};
            layout.Controls.Add(unitLabel, 2, row);

            if (advancedOnly)
                advancedControls.AddRange(new Control[] {label, box, unitLabel});
            return box;
        }

        void Changed()
        {
            if (loading || shown == null)
                return;
            debounce.Stop();
            debounce.Start();
        }

        static decimal Fit(NumericUpDown box, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
                return box.Minimum;
            var d = Math.Round((decimal) value, box.DecimalPlaces);
            return Math.Min(box.Maximum, Math.Max(box.Minimum, d));
        }

        static bool Edited(NumericUpDown box, double was)
        {
            return box.Value != Fit(box, was);
        }

        /// <summary>Shows a sequence's inputs and what they make, without handing a change on.</summary>
        public void ShowLayout(AutolandLayout layout, string loiterRadiusSource)
        {
            debounce.Stop();
            loading = true;
            try
            {
                var p = layout.Params;
                shown = p.Clone();

                double alt = CurrentState.multiplieralt;
                double dist = CurrentState.multiplierdist;
                altUnitObstacle.Text = CurrentState.AltUnit;
                altUnitHold.Text = CurrentState.AltUnit;
                distUnitOffset.Text = CurrentState.DistanceUnit;
                distUnitLength.Text = CurrentState.DistanceUnit;
                distUnitWidth.Text = CurrentState.DistanceUnit;

                shownObstacle = layout.ObstacleHeightM;
                obstacle.Value = Fit(obstacle, shownObstacle * alt);
                heading.Value = Fit(heading, p.HeadingDeg % 360);
                holdAlt.Value = Fit(holdAlt, p.HoldAltM * alt);
                preAngle.Value = Fit(preAngle, p.PreApproachDeg);
                finalAngle.Value = Fit(finalAngle, p.FinalApproachDeg);
                loiterTime.Value = Fit(loiterTime, p.LoiterTimeS);
                downwindOffset.Value = Fit(downwindOffset, p.DownwindOffsetM * dist);
                boxLength.Value = Fit(boxLength, p.BoxLengthM * dist);
                boxWidth.Value = Fit(boxWidth, p.BoxWidthM * dist);

                shownLayout = layout;
                shownSource = loiterRadiusSource;
                ShowReadouts(layout, loiterRadiusSource);

                // what was changed to make the sequence, then what is worth a second look
                var lines = new List<string>(layout.Warnings);
                lines.AddRange(layout.Advisories);
                warnings.Text = string.Join("\n", lines);
                warnings.Visible = lines.Count > 0;
            }
            finally
            {
                loading = false;
            }
        }

        void ShowReadouts(AutolandLayout layout, string loiterRadiusSource)
        {
            double alt = CurrentState.multiplieralt;
            double dist = CurrentState.multiplierdist;
            string A(double m) => (m * alt).ToString("0.#", CultureInfo.CurrentCulture) + " " + CurrentState.AltUnit;
            string D(double m) => (m * dist).ToString("0", CultureInfo.CurrentCulture) + " " +
                                  CurrentState.DistanceUnit;

            string baseLeg = "Base leg: " + A(layout.BaseAltM) + ", " + D(layout.BaseDistanceM) + " out";
            readouts.Text = advanced.Checked
                ? "Landing zone start: " + A(layout.ZoneAltM) + "\n" + baseLeg + "\n" +
                  "Loiter radius: " + D(layout.Params.LoiterRadiusM) + " (" + loiterRadiusSource + ")"
                : baseLeg + "\n" + "Hold altitude: " + A(layout.Params.HoldAltM);
        }

        /// <summary>Hands on whatever has been typed but not yet handed on (the panel is about to close).</summary>
        public void Flush()
        {
            if (!debounce.Enabled)
                return;
            debounce.Stop();
            Apply();
        }

        void Apply()
        {
            if (shown == null)
                return;

            // only what was edited is read back, so the rest keep their full precision
            var p = shown.Clone();
            double alt = CurrentState.multiplieralt;
            double dist = CurrentState.multiplierdist;

            if (Edited(heading, p.HeadingDeg % 360))
                p.HeadingDeg = (double) heading.Value;
            if (Edited(holdAlt, p.HoldAltM * alt))
                p.HoldAltM = (double) holdAlt.Value / alt;
            if (Edited(preAngle, p.PreApproachDeg))
                p.PreApproachDeg = (double) preAngle.Value;
            if (Edited(finalAngle, p.FinalApproachDeg))
                p.FinalApproachDeg = (double) finalAngle.Value;
            if (Edited(loiterTime, p.LoiterTimeS))
                p.LoiterTimeS = (double) loiterTime.Value;
            if (Edited(downwindOffset, p.DownwindOffsetM * dist))
                p.DownwindOffsetM = (double) downwindOffset.Value / dist;
            if (Edited(boxLength, p.BoxLengthM * dist))
                p.BoxLengthM = (double) boxLength.Value / dist;
            if (Edited(boxWidth, p.BoxWidthM * dist))
                p.BoxWidthM = (double) boxWidth.Value / dist;

            // the obstacle height is the hold altitude seen from the base leg: set from it unless the hold was typed
            if (!Edited(holdAlt, shown.HoldAltM * alt) && Edited(obstacle, shownObstacle * alt))
                p.HoldAltM = AutolandPattern.HoldForObstacle(p, (double) obstacle.Value / alt);

            ParamsChanged?.Invoke(p);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                debounce.Dispose();
            base.Dispose(disposing);
        }
    }
}
