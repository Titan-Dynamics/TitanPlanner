using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Controls.PPS
{
    /// <summary>
    /// The LRH dashboard's PPS tab: the airframe PPS predicts for, the live readings against its prediction at the
    /// airspeed flown, and the power curve with this session's steady readings over it.
    /// </summary>
    public class PpsDeltaPanel : UserControl
    {
        private readonly Label airframe;
        private readonly Label state;
        private readonly DataGridView grid;
        private readonly PolarChart polar;

        public PpsDeltaPanel()
        {
            Padding = new Padding(8, 4, 8, 8);

            airframe = new Label { Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, Font = new Font("Segoe UI", 9.5F, FontStyle.Bold) };
            state = new Label { Dock = DockStyle.Top, Height = 24, AutoEllipsis = true, Font = new Font("Segoe UI", 9F) };

            grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BorderStyle = BorderStyle.None,
                Margin = new Padding(0, 0, 8, 0)
            };
            foreach (var (name, header) in new[]
                     {
                         ("name", "Parameter"), ("pps", "PPS"), ("actual", "Actual"), ("delta", "Δ"), ("pct", "Δ %")
                     })
            {
                var column = grid.Columns[grid.Columns.Add(name, header)];
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                if (name != "name")
                    column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            }

            grid.Columns["name"].FillWeight = 150;
            grid.SelectionChanged += (s, e) => grid.ClearSelection();

            polar = new PolarChart { Dock = DockStyle.Fill, Margin = new Padding(8, 0, 0, 0) };

            var split = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42F));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58F));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            split.Controls.Add(grid, 0, 0);
            split.Controls.Add(polar, 1, 0);

            Controls.Add(split);
            Controls.Add(state);
            Controls.Add(airframe);
        }

        public void ShowMessage(string message)
        {
            airframe.Text = message;
            state.Text = "";
            grid.Rows.Clear();
            BingoPanel.StyleGrid(grid);
            polar.Set(null, null);
        }

        public void Show(PpsPerformance pps, PpsDelta delta, bool connected)
        {
            string N(double v, string format, string unit) => double.IsNaN(v) ? "–" : v.ToString(format) + unit;
            airframe.Text = pps.Name + "   ·   AUW " + N(pps.Auw, "0.00", " kg") + "   ·   " +
                            (pps.Prop.Length > 0 ? pps.Prop + " on " : "") + pps.Motor +
                            "   ·   stall " + N(pps.StallSpeed, "0.0", "") + ", cruise " + N(pps.CruiseSpeed, "0.0", "") +
                            ", best range " + N(pps.BestRangeSpeed, "0.0", " m/s") +
                            "   ·   " + N(pps.UsableWh, "0", " Wh");

            var dim = TrendChart.Blend(BackColor, ForeColor, 0.6);
            if (!connected)
            {
                state.Text = "Not connected";
                state.ForeColor = dim;
            }
            else if (delta.Steady)
            {
                state.Text = "Steady at " + delta.Airspeed.ToString("0.0") + " m/s: comparing the last " +
                             PpsDelta.Window.ToString("0") + " s with PPS";
                state.ForeColor = BingoPanel.StatusColor(BingoStatus.Ok);
            }
            else
            {
                state.Text = "Not steady: " + delta.NotSteadyBecause + ". The table holds until level, straight, steady flight.";
                state.ForeColor = dim;
            }

            if (delta.Steady)
            {
                var rows = delta.Rows;
                if (grid.Rows.Count != rows.Count)
                {
                    grid.Rows.Clear();
                    if (rows.Count > 0)
                        grid.Rows.Add(rows.Count);
                }

                BingoPanel.StyleGrid(grid);
                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    var cells = grid.Rows[i].Cells;
                    string unit = r.Unit == "°" ? "°" : " " + r.Unit;
                    cells["name"].Value = r.Name;
                    cells["pps"].Value = r.Predicted.ToString(r.Format) + unit;
                    cells["actual"].Value = r.Actual.ToString(r.Format) + unit;
                    cells["delta"].Value = r.Delta.ToString("+" + r.Format + ";-" + r.Format) + unit;
                    cells["pct"].Value = double.IsNaN(r.DeltaPercent) ? "" : r.DeltaPercent.ToString("+0;-0") + "%";

                }
            }

            polar.Set(pps, delta);
        }

        /// <summary>The PPS power against airspeed, with this session's steady readings and the latest window on it.</summary>
        private class PolarChart : Control
        {
            private static readonly Font TitleFont = new Font("Segoe UI", 9F, FontStyle.Bold);
            private static readonly Font SmallFont = new Font("Segoe UI", 8.25F);

            private PpsPerformance pps;
            private PpsDelta delta;

            public PolarChart()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            }

            public void Set(PpsPerformance pps, PpsDelta delta)
            {
                this.pps = pps;
                this.delta = delta;
                Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var back = BackColor;
                var fore = ForeColor;
                var tile = TrendChart.Blend(back, fore, 0.05);
                var gridColor = TrendChart.Blend(back, fore, 0.09);
                var dim = TrendChart.Blend(back, fore, 0.55);
                var curveColor = Color.FromArgb(255, 213, 79);
                var pointColor = Color.FromArgb(79, 195, 247);

                var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
                using (var brush = new SolidBrush(tile))
                    g.FillRectangle(brush, bounds);
                using (var pen = new Pen(TrendChart.Blend(back, fore, 0.12)))
                    g.DrawRectangle(pen, bounds);
                TextRenderer.DrawText(g, "Power vs Airspeed", TitleFont, new Point(10, 8), dim, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "— PPS", SmallFont, new Point(150, 9), curveColor, TextFormatFlags.NoPrefix);
                TextRenderer.DrawText(g, "● steady flight", SmallFont, new Point(200, 9), pointColor, TextFormatFlags.NoPrefix);

                var plot = new Rectangle(52, 34, Width - 52 - 16, Height - 34 - 34);
                if (pps == null || plot.Width < 40 || plot.Height < 40)
                    return;

                var points = delta?.Points ?? new System.Collections.Generic.List<(double, double)>();
                double minV = pps.MinAirspeed, maxV = pps.MaxAirspeed;
                double maxP = Math.Max(pps.Sweep.Max(p => p.Power), points.Count > 0 ? points.Max(p => p.Item2) : 0) * 1.1;
                if (!(maxP > 0))
                    return;

                float X(double v) => (float) (plot.Left + (v - minV) / (maxV - minV) * plot.Width);
                float Y(double p) => (float) (plot.Bottom - p / maxP * plot.Height);

                using (var pen = new Pen(gridColor))
                {
                    for (int i = 0; i <= 4; i++)
                    {
                        float y = plot.Bottom - plot.Height * i / 4f;
                        g.DrawLine(pen, plot.Left, y, plot.Right, y);
                        TextRenderer.DrawText(g, (maxP * i / 4).ToString("0") + " W", SmallFont,
                            new Rectangle(0, (int) y - 8, plot.Left - 4, 16), dim,
                            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }

                    for (double v = Math.Ceiling(minV / 5) * 5; v <= maxV; v += 5)
                    {
                        float x = X(v);
                        g.DrawLine(pen, x, plot.Top, x, plot.Bottom);
                        TextRenderer.DrawText(g, v.ToString("0"), SmallFont, new Rectangle((int) x - 20, plot.Bottom + 2, 40, 14),
                            dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
                    }
                }

                TextRenderer.DrawText(g, "airspeed, m/s", SmallFont, new Rectangle(plot.Left, plot.Bottom + 16, plot.Width, 14),
                    dim, TextFormatFlags.Right | TextFormatFlags.NoPrefix);

                g.SmoothingMode = SmoothingMode.AntiAlias;

                // the cruise and best-range speeds
                void Marker(double v, string label)
                {
                    if (double.IsNaN(v) || v < minV || v > maxV)
                        return;
                    using (var pen = new Pen(dim) { DashStyle = DashStyle.Dash })
                        g.DrawLine(pen, X(v), plot.Top, X(v), plot.Bottom);
                    TextRenderer.DrawText(g, label, SmallFont, new Point((int) X(v) + 3, plot.Top), dim, TextFormatFlags.NoPrefix);
                }

                Marker(pps.CruiseSpeed, "cruise");
                Marker(pps.BestRangeSpeed, "best range");

                using (var brush = new SolidBrush(Color.FromArgb(150, pointColor)))
                    foreach (var (v, p) in points)
                        g.FillEllipse(brush, X(v) - 2.5f, Y(p) - 2.5f, 5, 5);

                using (var pen = new Pen(curveColor, 2.5f))
                    g.DrawLines(pen, pps.Sweep.Where(p => !double.IsNaN(p.Power)).Select(p => new PointF(X(p.Airspeed), Y(p.Power))).ToArray());

                if (delta != null && delta.Steady && points.Count > 0)
                {
                    var (v, p) = points[points.Count - 1];
                    using (var pen = new Pen(fore, 2f))
                        g.DrawEllipse(pen, X(v) - 6, Y(p) - 6, 12, 12);
                }

                g.SmoothingMode = SmoothingMode.Default;
            }
        }
    }
}
