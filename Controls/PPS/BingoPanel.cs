using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.PPS
{
    /// <summary>
    /// The LRH dashboard's Bingo tab: the margin, the vehicle's battery against the bingo from where it is, and a row for
    /// each waypoint with its distance from home, the battery predicted on arrival and the bingo level there.
    /// </summary>
    public class BingoPanel : UserControl
    {
        public const string MarginSetting = "lrh_bingo_margin";
        public const string ChemistrySetting = "lrh_batt_chemistry";
        public const string CellsSetting = "lrh_batt_cells";
        public const string ParallelSetting = "lrh_batt_parallel";
        public const string CellResistanceSetting = "lrh_batt_cell_mohm";
        public const string LiveResistanceSetting = "lrh_batt_live_r";
        private const double DefaultMargin = 20;
        private const double DefaultCellMilliohm = 8;

        private static readonly (string Text, BatteryChemistry Chemistry)[] Chemistries =
        {
            ("Li-ion", BatteryChemistry.LiIon), ("LiPo", BatteryChemistry.LiPo), ("LiHV", BatteryChemistry.LiHV)
        };

        private readonly NumericUpDown margin;
        private readonly ComboBox chemistry;
        private readonly ComboBox cells;
        private readonly ComboBox parallel;
        private readonly NumericUpDown resistance;
        private readonly CheckBox liveResistance;
        private readonly Label marginLabel;
        private readonly Label details;
        private readonly BingoSummary summary;
        private readonly DataGridView grid;
        private Font bold;

        /// <summary>The margin or the battery was changed.</summary>
        public event EventHandler SettingsChanged;

        public double MarginPercent => (double) margin.Value;

        public BatteryChemistry Chemistry => Chemistries[Math.Max(0, chemistry.SelectedIndex)].Chemistry;

        /// <summary>Cells in series as set; 0 to work them out from the live voltage.</summary>
        public int Cells => cells.SelectedIndex <= 0 ? 0 : cells.SelectedIndex + 1;

        /// <summary>Cells in parallel.</summary>
        public int Parallel => Math.Max(0, parallel.SelectedIndex) + 1;

        /// <summary>One cell's internal resistance, ohms.</summary>
        public double CellResistanceOhm => (double) resistance.Value / 1000.0;

        /// <summary>Estimate the pack's resistance from the live voltage and current, in place of the cells'.</summary>
        public bool LiveResistance => liveResistance.Checked;

        public BingoPanel()
        {
            Padding = new Padding(8, 4, 8, 8);

            var strip = new Panel { Dock = DockStyle.Top, Height = 58 };
            marginLabel = new Label
            {
                Text = "Margin", AutoSize = true, Location = new Point(2, 8), Font = new Font("Segoe UI", 9F)
            };
            margin = new NumericUpDown
            {
                Minimum = 0, Maximum = 50, DecimalPlaces = 0, Increment = 1, Width = 60, Location = new Point(56, 5),
                Value = (decimal) Math.Max(0, Math.Min(50, Settings.Instance.GetDouble(MarginSetting, DefaultMargin)))
            };
            var percent = new Label { Text = "% battery", AutoSize = true, Location = new Point(120, 8) };

            var chemistryLabel = new Label { Text = "Battery", AutoSize = true, Location = new Point(204, 8) };
            chemistry = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 72, Location = new Point(256, 4) };
            chemistry.Items.AddRange(Chemistries.Select(c => (object) c.Text).ToArray());
            var savedChemistry = Settings.Instance[ChemistrySetting];
            int chemistryIndex = Array.FindIndex(Chemistries, c => c.Chemistry.ToString() == savedChemistry);
            chemistry.SelectedIndex = Math.Max(0, chemistryIndex);

            cells = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 64, Location = new Point(334, 4) };
            cells.Items.Add("Auto");
            for (int n = 2; n <= 24; n++)
                cells.Items.Add(n + "S");
            int savedCells = (int) Settings.Instance.GetDouble(CellsSetting, 0);
            cells.SelectedIndex = savedCells >= 2 && savedCells <= 24 ? savedCells - 1 : 0;

            parallel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 52, Location = new Point(402, 4) };
            for (int n = 1; n <= 12; n++)
                parallel.Items.Add(n + "P");
            int savedParallel = (int) Settings.Instance.GetDouble(ParallelSetting, 1);
            parallel.SelectedIndex = savedParallel >= 1 && savedParallel <= 12 ? savedParallel - 1 : 0;

            var resistanceLabel = new Label { Text = "IR", AutoSize = true, Location = new Point(466, 8) };
            resistance = new NumericUpDown
            {
                Minimum = 0, Maximum = 200, DecimalPlaces = 1, Increment = 0.5M, Width = 56, Location = new Point(488, 5),
                Value = (decimal) Math.Max(0, Math.Min(200, Settings.Instance.GetDouble(CellResistanceSetting, DefaultCellMilliohm)))
            };
            var milliohm = new Label { Text = "mΩ/cell", AutoSize = true, Location = new Point(548, 8) };
            liveResistance = new CheckBox
            {
                Text = "Live pack resistance", AutoSize = true, Location = new Point(614, 6),
                Checked = Settings.Instance[LiveResistanceSetting] == "True"
            };

            details = new Label
            {
                AutoSize = false, Location = new Point(2, 34), Height = 18,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top, AutoEllipsis = true
            };
            strip.Resize += (s, e) => details.Width = Math.Max(10, strip.Width - details.Left - 4);
            strip.Controls.AddRange(new Control[]
            {
                marginLabel, margin, percent, chemistryLabel, chemistry, cells, parallel, resistanceLabel, resistance,
                milliohm, liveResistance, details
            });
            var invariant = System.Globalization.CultureInfo.InvariantCulture;
            margin.ValueChanged += (s, e) =>
            {
                Settings.Instance[MarginSetting] = margin.Value.ToString(invariant);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            chemistry.SelectedIndexChanged += (s, e) =>
            {
                Settings.Instance[ChemistrySetting] = Chemistry.ToString();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            cells.SelectedIndexChanged += (s, e) =>
            {
                Settings.Instance[CellsSetting] = Cells.ToString(invariant);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            parallel.SelectedIndexChanged += (s, e) =>
            {
                Settings.Instance[ParallelSetting] = Parallel.ToString(invariant);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            resistance.ValueChanged += (s, e) =>
            {
                Settings.Instance[CellResistanceSetting] = resistance.Value.ToString(invariant);
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };
            liveResistance.CheckedChanged += (s, e) =>
            {
                Settings.Instance[LiveResistanceSetting] = liveResistance.Checked.ToString();
                SettingsChanged?.Invoke(this, EventArgs.Empty);
            };

            summary = new BingoSummary { Dock = DockStyle.Top, Height = 118 };

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                MultiSelect = false,
                BorderStyle = BorderStyle.None
            };
            foreach (var (name, header) in new[]
                     {
                         ("wp", "Waypoint"), ("along", "Along route"), ("home", "From home"),
                         ("arrival", "Arrival"), ("arrivalV", "Arrival V"), ("bingo", "Bingo"), ("bingoV", "Bingo V"),
                         ("slack", "Slack"), ("status", "")
                     })
            {
                var column = grid.Columns[grid.Columns.Add(name, header)];
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (name != "wp" && name != "status")
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            grid.Columns["status"].FillWeight = 160;
            grid.SelectionChanged += (s, e) => grid.ClearSelection();

            Controls.Add(grid);
            Controls.Add(summary);
            Controls.Add(strip);
        }

        /// <summary>Shows a prompt in place of the numbers.</summary>
        public void ShowMessage(string message)
        {
            summary.Show(null, message, MarginPercent <= 0);
            grid.Rows.Clear();
            StyleGrid(grid);
            details.Text = "";
            UpdateMarginLabel();
        }

        /// <param name="battery">The pack the voltages are for, e.g. "6S Li-ion (from the live voltage), 50 mΩ".</param>
        public void Show(BingoResult result, string source, double cruise, double windSpeed, double windFrom,
            string battery)
        {
            UpdateMarginLabel();
            details.Text = source + "   ·   " + battery + "   ·   cruise " + cruise.ToString("0.0") + " m/s" +
                           (windSpeed > 0.5
                               ? "   ·   wind " + windSpeed.ToString("0.0") + " m/s from " + windFrom.ToString("000") + "°"
                               : "   ·   no wind") +
                           (result.Notes.Count > 0 ? "   ·   " + string.Join(" ", result.Notes) : "");

            summary.Show(result, result.Problem, result.MoMode);

            var rows = result.Rows;
            if (grid.Rows.Count != rows.Count)
            {
                grid.Rows.Clear();
                if (rows.Count > 0)
                    grid.Rows.Add(rows.Count);
            }

            StyleGrid(grid);
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                var cells = grid.Rows[i].Cells;
                cells["wp"].Value = r.Label;
                cells["along"].Value = r.DistanceAlongKm.ToString("0.0") + " km";
                cells["home"].Value = r.DistanceHomeKm.ToString("0.0") + " km";
                cells["arrival"].Value = Percent(r.ArrivalPercent, r.Status == BingoStatus.Passed);
                cells["arrivalV"].Value = r.Status == BingoStatus.Passed ? "" : Volts(r.ArrivalVolts);
                cells["bingo"].Value = Percent(r.BingoPercent, false);
                cells["bingoV"].Value = Volts(r.BingoVolts);
                cells["slack"].Value = r.Status == BingoStatus.Passed || double.IsNaN(r.Slack)
                    ? "–"
                    : r.Slack.ToString("+0;-0") + "%";
                cells["status"].Value = StatusText(r, r == result.PointOfNoReturn);

                grid.Rows[i].DefaultCellStyle.Font = r == result.PointOfNoReturn ? (bold ?? (bold = new Font(grid.Font, FontStyle.Bold))) : null;
            }
        }

        internal static readonly Color TableBack = Color.FromArgb(0x43, 0x44, 0x45);
        internal static readonly Color TableHeader = Color.FromArgb(0x33, 0x34, 0x35);

        /// <summary>White text on grey, whatever the theme, rows and headers alike.</summary>
        internal static void StyleGrid(DataGridView grid)
        {
            if (grid.BackgroundColor == TableBack)
                return;
            grid.BackgroundColor = TableBack;
            grid.GridColor = Color.FromArgb(0x5a, 0x5b, 0x5c);
            grid.EnableHeadersVisualStyles = false;
            foreach (var style in new[] { grid.DefaultCellStyle, grid.RowsDefaultCellStyle, grid.AlternatingRowsDefaultCellStyle })
            {
                style.BackColor = TableBack;
                style.ForeColor = Color.White;
                style.SelectionBackColor = TableBack;
                style.SelectionForeColor = Color.White;
            }

            var header = grid.ColumnHeadersDefaultCellStyle;
            header.BackColor = TableHeader;
            header.ForeColor = Color.White;
            header.SelectionBackColor = TableHeader;
            header.SelectionForeColor = Color.White;
        }

        private void UpdateMarginLabel()
        {
            marginLabel.Text = MarginPercent <= 0 ? "MO MODE" : "Margin";
            marginLabel.ForeColor = MarginPercent <= 0 ? BingoSummary.MoColor : ForeColor;
        }

        internal static string Volts(double value) => double.IsNaN(value) ? "–" : value.ToString("0.0") + " V";

        private static string Percent(double value, bool passed)
        {
            if (passed)
                return "passed";
            return double.IsNaN(value) ? "–" : value.ToString("0") + "%";
        }

        private static string StatusText(BingoRow r, bool pnr)
        {
            switch (r.Status)
            {
                case BingoStatus.Passed:
                    return "";
                case BingoStatus.Unreachable:
                    return pnr ? "POINT OF NO RETURN: wind stronger than airspeed" : "Wind stronger than airspeed";
                case BingoStatus.Below:
                    return pnr ? "POINT OF NO RETURN: below bingo" : "Below bingo";
                case BingoStatus.Close:
                    return "Close to bingo";
                default:
                    return "";
            }
        }

        internal static Color StatusColor(BingoStatus status)
        {
            switch (status)
            {
                case BingoStatus.Ok:
                    return Color.FromArgb(56, 142, 60);
                case BingoStatus.Close:
                    return Color.FromArgb(245, 166, 35);
                case BingoStatus.Below:
                case BingoStatus.Unreachable:
                    return Color.FromArgb(211, 47, 47);
                default:
                    return Color.Empty;
            }
        }

        /// <summary>The battery now against the bingo from here, the range home, the point of no return, and a banner.</summary>
        private class BingoSummary : Control
        {
            // the LRH orange
            public static readonly Color MoColor = Color.FromArgb(255, 145, 27);

            private static readonly Font CaptionFont = new Font("Segoe UI", 9F);
            private static readonly Font[] ValueFonts = new[] { 18F, 16F, 14F, 12F }
                .Select(size => new Font("Segoe UI", size, FontStyle.Bold)).ToArray();
            private static readonly Font BannerFont = new Font("Segoe UI", 10.5F, FontStyle.Bold);

            private const int BannerHeight = 32;

            private BingoResult result;
            private string message;
            private bool mo;
            private int tick;

            public BingoSummary()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            public void Show(BingoResult result, string message, bool mo)
            {
                this.result = result;
                this.message = message;
                this.mo = mo;
                tick++;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs pe)
            {
                var g = pe.Graphics;
                var back = BackColor;
                var fore = ForeColor;
                var dim = TrendChart.Blend(back, fore, 0.55);
                var r = result;
                var now = r?.Now;
                var pnr = r?.PointOfNoReturn;

                // Mo mode: the logo pulses on the left in the accent colour
                int left = 4;
                if (mo)
                {
                    double pulse = 0.55 + 0.45 * Math.Abs(Math.Sin(tick * 0.5));
                    var color = TrendChart.Blend(back, MoColor, pulse);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float w = LRHLogo.Draw(g, new RectangleF(left, 6, 200, Height - BannerHeight - 18), color);
                    g.SmoothingMode = SmoothingMode.Default;
                    left += (int) w + 24;
                }

                string Pct(double v) => double.IsNaN(v) ? "–" : v.ToString("0") + "%";
                string PctVolts(double p, double v) => double.IsNaN(v) ? Pct(p) : Pct(p) + " · " + v.ToString("0.0") + " V";
                var stats = new[]
                {
                    ("Battery now", now == null ? "–" : PctVolts(now.ArrivalPercent, now.ArrivalVolts), fore),
                    ("Bingo from here", now == null ? "–" : PctVolts(now.BingoPercent, now.BingoVolts), fore),
                    ("Slack", now == null || double.IsNaN(now.Slack) ? "–" : now.Slack.ToString("+0;-0") + "%",
                        now == null ? fore : StatusOr(now.Status, fore)),
                    ("Range home (still air)", r == null || double.IsNaN(r.RangeHomeKm) ? "–" : r.RangeHomeKm.ToString("0.0") + " km", fore),
                    ("Point of no return", pnr == null ? (r == null || r.Rows.Count == 0 ? "–" : "None") : pnr.Label,
                        pnr == null ? fore : BingoPanel.StatusColor(BingoStatus.Below)),
                };

                int width = Width - left - 4;
                int bottom = Height - BannerHeight - 12;
                for (int i = 0; i < stats.Length; i++)
                {
                    var (caption, value, color) = stats[i];
                    int x = left + width * i / stats.Length;
                    int w = width / stats.Length - 8;
                    TextRenderer.DrawText(g, caption, CaptionFont, new Rectangle(x, 10, w, 18), dim,
                        TextFormatFlags.Left | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    var font = ValueFonts.FirstOrDefault(f =>
                                   TextRenderer.MeasureText(g, value, f, Size.Empty, TextFormatFlags.NoPrefix).Width <= w)
                               ?? ValueFonts[ValueFonts.Length - 1];
                    TextRenderer.DrawText(g, value, font, new Rectangle(x, 28, w, Math.Max(20, bottom - 28)), color,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);
                }

                // the banner: the verdict, or Mo mode's
                Color bannerColor;
                string text;
                bool flashing = false;
                if (r == null || r.Problem != null)
                {
                    bannerColor = TrendChart.Blend(back, fore, 0.12);
                    text = message ?? "";
                }
                else if (now != null && now.Status == BingoStatus.Below)
                {
                    bannerColor = BingoPanel.StatusColor(BingoStatus.Below);
                    text = "BELOW BINGO: " + Pct(now.ArrivalPercent) + " left, " + Pct(now.BingoPercent) + " needed to get home";
                    flashing = true;
                }
                else if (pnr != null)
                {
                    bannerColor = BingoPanel.StatusColor(BingoStatus.Below);
                    text = "Point of no return at " + pnr.Label + ": " +
                           (pnr.Status == BingoStatus.Unreachable ? "the wind is stronger than the airspeed" : "arrives below bingo");
                }
                else if (mo)
                {
                    bannerColor = MoColor;
                    text = "MO MODE: no reserve, no regrets";
                }
                else
                {
                    bannerColor = BingoPanel.StatusColor(BingoStatus.Ok);
                    var last = r.Rows.LastOrDefault();
                    text = last == null
                        ? "The mission has no waypoints"
                        : "Every waypoint stays above bingo; " + Pct(last.ArrivalPercent) + " left at " + last.Label;
                }

                if (mo && (pnr != null || flashing))
                    text = "MO MODE  ·  " + text;

                var banner = new Rectangle(4, Height - BannerHeight - 6, Width - 8, BannerHeight);
                bool lit = !flashing || tick % 2 == 0;
                using (var brush = new SolidBrush(lit ? bannerColor : TrendChart.Blend(back, bannerColor, 0.35)))
                    g.FillRectangle(brush, banner);
                var bannerText = r == null || r.Problem != null ? fore
                    : bannerColor == MoColor ? Color.Black
                    : Color.White;
                TextRenderer.DrawText(g, text, BannerFont, banner, bannerText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPrefix);
            }

            private static Color StatusOr(BingoStatus status, Color otherwise)
            {
                var c = BingoPanel.StatusColor(status);
                return c.IsEmpty || status == BingoStatus.Ok ? otherwise : c;
            }
        }
    }
}
