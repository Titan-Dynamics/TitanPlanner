using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The selected vehicle's sensors as SYS_STATUS reports them: enabled, present, and healthy. Those present come
    /// first; the rest are dimmed, as their health means nothing.
    /// </summary>
    public partial class ControlSensorsStatus : UserControl
    {
        private static readonly Dictionary<string, string> Names = new Dictionary<string, string>
        {
            {"3D_GYRO", "Gyro"},
            {"3D_ACCEL", "Accelerometer"},
            {"3D_MAG", "Compass"},
            {"3D_GYRO2", "Gyro 2"},
            {"3D_ACCEL2", "Accelerometer 2"},
            {"3D_MAG2", "Compass 2"},
            {"ABSOLUTE_PRESSURE", "Barometer"},
            {"DIFFERENTIAL_PRESSURE", "Airspeed"},
            {"ANGULAR_RATE_CONTROL", "Rate Control"},
            {"ATTITUDE_STABILIZATION", "Attitude Control"},
            {"Z_ALTITUDE_CONTROL", "Altitude Control"},
            {"XY_POSITION_CONTROL", "Position Control"},
            {"RC_RECEIVER", "RC Receiver"},
            {"PREARM_CHECK", "Pre-arm Checks"},
            {"GPS", "GPS"},
            {"AHRS", "AHRS"},
        };

        private static readonly (uint Bit, string Name)[] Sensors = Enum.GetValues(typeof(MAVLink.MAV_SYS_STATUS_SENSOR))
            .Cast<MAVLink.MAV_SYS_STATUS_SENSOR>()
            .Where(s => s != MAVLink.MAV_SYS_STATUS_SENSOR.MAV_SYS_STATUS_EXTENSION_USED)
            .Select(s => ((uint) s, Pretty(s.ToString())))
            .ToArray();

        private (bool Seen, uint Enabled, uint Present, uint Health) shown;

        public ControlSensorsStatus()
        {
            InitializeComponent();
            DoubleBuffered = true;
            ResizeRedraw = true;
            AutoScroll = true;
        }

        private static string Pretty(string name)
        {
            name = name.Replace("MAV_SYS_STATUS_", "").Trim('_');
            if (Names.TryGetValue(name, out var pretty))
                return pretty;
            return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(name.Replace('_', ' ').ToLowerInvariant());
        }

        private int RowHeight => Font.Height + 8;

        private static (bool, uint, uint, uint) Read()
        {
            var cs = MainV2.comPort?.MAV?.cs;
            if (cs == null)
                return (false, 0, 0, 0);
            return (cs.sensors_present.seen, cs.sensors_enabled.Value, cs.sensors_present.Value, cs.sensors_health.Value);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            shown = Read();
            AutoScrollMinSize = new Size(0, RowHeight * (Sensors.Length + 1));
            timer1.Start();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            AutoScrollMinSize = new Size(0, RowHeight * (Sensors.Length + 1));
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            var now = Read();
            if (now.Equals(shown))
                return;
            shown = now;
            Invalidate();
        }

        private static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            var fore = ForeColor;
            var dim = Blend(BackColor, ForeColor, 0.5);
            var line = Blend(BackColor, ForeColor, 0.15);
            bool dark = BackColor.GetBrightness() < 0.5f;
            var good = dark ? Color.FromArgb(102, 187, 106) : Color.FromArgb(46, 125, 50);
            var bad = dark ? Color.FromArgb(239, 83, 80) : Color.FromArgb(198, 40, 40);

            if (!shown.Seen)
            {
                TextRenderer.DrawText(g, "Connect to see the vehicle's sensors.", Font, ClientRectangle, dim,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                return;
            }

            int row = RowHeight;
            int width = ClientSize.Width;
            int top = AutoScrollPosition.Y;

            // three columns on the right, each as wide as its heading; the name has the rest
            var headings = new[] {"Enabled", "Present", "Health"};
            var lefts = new int[headings.Length];
            var widths = new int[headings.Length];
            int right = width - 4;
            for (int c = headings.Length - 1; c >= 0; c--)
            {
                widths[c] = TextRenderer.MeasureText(headings[c], Font).Width + 8;
                right -= widths[c];
                lefts[c] = right;
            }

            void Cell(string text, int column, int cellTop, Color color)
            {
                TextRenderer.DrawText(g, text, Font, new Rectangle(lefts[column], cellTop, widths[column], row), color,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            var nameFlags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                            TextFormatFlags.NoPrefix;
            int nameWidth = lefts[0] - 4;

            TextRenderer.DrawText(g, "Sensor", Font, new Rectangle(4, top, nameWidth, row), dim, nameFlags);
            for (int c = 0; c < headings.Length; c++)
                Cell(headings[c], c, top, dim);
            using (var pen = new Pen(line))
                g.DrawLine(pen, 4, top + row - 1, width - 4, top + row - 1);

            int y = top + row;
            foreach (var sensor in Sensors.OrderBy(s => (shown.Present & s.Bit) == 0))
            {
                bool enabled = (shown.Enabled & sensor.Bit) != 0;
                bool present = (shown.Present & sensor.Bit) != 0;
                bool healthy = (shown.Health & sensor.Bit) != 0;

                if (y + row >= 0 && y <= ClientSize.Height)
                {
                    TextRenderer.DrawText(g, sensor.Name, Font, new Rectangle(4, y, nameWidth, row),
                        present ? fore : dim, nameFlags);
                    Cell(enabled ? "On" : "Off", 0, y, enabled ? fore : dim);
                    Cell(present ? "Yes" : "No", 1, y, present ? fore : dim);
                    if (present)
                        Cell(healthy ? "OK" : "Bad", 2, y, healthy ? good : bad);
                    else
                        Cell("–", 2, y, dim);
                }

                y += row;
            }
        }
    }
}