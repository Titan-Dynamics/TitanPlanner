using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using GMap.NET;
using log4net;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.TerrainProfile
{
    /// <summary>
    /// The terrain profile along the foot of the PLAN map: the mission's altitude in the theme's accent over the ground
    /// under its route, filled grey, with the route's length, peak and least clearance, terrain conflicts in red, and
    /// a readout (and a marker on the map) for the point under the pointer. The profile is built on a worker thread
    /// whenever the mission changes, and again while terrain tiles are still downloading.
    /// </summary>
    public sealed class TerrainProfilePanel : IMapPanel, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string SettingOpen = "plan_terrain_profile_open";
        const int Margin = 10;
        const int HeaderHeight = 28;
        const int PlotLeft = 52, PlotRight = 18, PlotTop = 34, PlotBottom = 24;

        // clearance under this (metres) is drawn as a warning
        const double LowClearance = 10;

        // tries at loading the route's terrain before giving up on it, a second apart
        const int TerrainRetries = 30;

        static readonly Color PanelFill = Color.FromArgb(205, 17, 19, 23);
        static readonly Color PanelEdge = Color.FromArgb(40, 255, 255, 255);
        static readonly Color Text = Color.FromArgb(236, 238, 241, 245);
        static readonly Color TextDim = Color.FromArgb(150, 205, 210, 218);
        static readonly Color Grid = Color.FromArgb(26, 255, 255, 255);
        static readonly Color TerrainTop = Color.FromArgb(245, 104, 110, 118);
        static readonly Color TerrainBottom = Color.FromArgb(245, 50, 54, 60);
        static readonly Color TerrainEdge = Color.FromArgb(255, 164, 171, 180);
        static readonly Color Conflict = Color.FromArgb(255, 255, 78, 78);
        static readonly Color Warning = Color.FromArgb(255, 255, 196, 64);

        readonly myGMAP _map;
        readonly System.Windows.Forms.Timer _rebuild = new System.Windows.Forms.Timer();
        readonly Font _font = new Font("Segoe UI", 8.25f);
        readonly Font _small = new Font("Segoe UI", 7.5f);
        readonly Font _bold = new Font("Segoe UI", 7.5f, FontStyle.Bold);

        PointLatLngAlt _home;
        List<Locationwp> _items;
        bool _verticalLanding;
        int _version;
        int _retries;
        bool _terrainUnavailable;
        MissionProfile _profile;

        bool _open;
        Rectangle _bounds, _plot, _toggle;
        bool _toggleHover;
        int? _hoverX;
        bool _disposed;

        public TerrainProfilePanel(myGMAP map)
        {
            _map = map;
            _open = Settings.Instance.GetBoolean(SettingOpen, true);
            _rebuild.Tick += (s, e) => Rebuild();
        }

        /// <summary>
        /// Shows the profile of the mission (null hides it). Rebuilt shortly after, so a run of edits builds it once.
        /// </summary>
        public void SetMission(PointLatLngAlt home, IList<Locationwp> items, bool verticalLanding)
        {
            _home = home;
            _items = items == null ? null : new List<Locationwp>(items);
            _verticalLanding = verticalLanding;
            _retries = 0;
            _terrainUnavailable = false;

            _rebuild.Stop();
            _rebuild.Interval = 150;
            _rebuild.Start();
        }

        void Rebuild()
        {
            _rebuild.Stop();
            int version = ++_version;
            var home = _home;
            var items = _items;
            bool verticalLanding = _verticalLanding;

            if (items == null || items.Count == 0)
            {
                Apply(version, null);
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                MissionProfile profile = null;
                try
                {
                    profile = MissionProfile.Build(home, items, verticalLanding, TerrainAt);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }

                try
                {
                    if (!_disposed && _map.IsHandleCreated && !_map.IsDisposed)
                        _map.BeginInvoke((Action)(() => Apply(version, profile)));
                }
                catch (InvalidOperationException)
                {
                    // the map went away meanwhile
                }
            });
        }

        void Apply(int version, MissionProfile profile)
        {
            if (_disposed || version != _version)
                return;

            _profile = profile;
            if (profile != null && !profile.TerrainComplete)
            {
                if (_retries++ < TerrainRetries)
                {
                    _rebuild.Interval = 1000;
                    _rebuild.Start();
                }
                else
                {
                    _terrainUnavailable = true;
                }
            }

            Invalidate();
        }

        // the ground's altitude from SRTM: NaN while its tile is downloading or where there is none
        static double TerrainAt(double lat, double lng)
        {
            short[,] data;
            switch (srtm.GetCell(lat, lng, out data))
            {
                case srtm.CellStatus.Loaded:
                    return srtm.SampleCell(data, lat, lng);
                case srtm.CellStatus.Ocean:
                    return 0;
                default:
                    return double.NaN;
            }
        }

        void Invalidate()
        {
            if (!_map.IsDisposed)
                ((Control)_map).Invalidate();
        }

        // lays the panel out on the map; false when there's nothing to show or no room for it
        bool Layout()
        {
            if (_profile == null || _map.Width < 320 || _map.Height < 240)
                return false;

            int height = _open ? Math.Max(140, Math.Min(210, (int)(_map.Height * 0.26))) : HeaderHeight;
            _bounds = new Rectangle(Margin, _map.Height - Margin - height, _map.Width - 2 * Margin, height);
            _toggle = new Rectangle(_bounds.Right - 30, _bounds.Top + (HeaderHeight - 22) / 2, 24, 22);
            _plot = Rectangle.FromLTRB(_bounds.Left + PlotLeft, _bounds.Top + PlotTop, _bounds.Right - PlotRight,
                _bounds.Bottom - PlotBottom);
            return true;
        }

        public bool HitTest(Point location)
        {
            return _profile != null && _bounds.Contains(location);
        }

        public void OnMouseMove(MouseEventArgs e)
        {
            bool toggleHover = _toggle.Contains(e.Location);
            int? hoverX = _open && _plot.Width > 0 && e.X >= _plot.Left - 4 && e.X <= _plot.Right + 4 &&
                          e.Y >= _bounds.Top + HeaderHeight
                ? Math.Max(_plot.Left, Math.Min(_plot.Right, e.X))
                : (int?)null;

            if (toggleHover != _toggleHover || hoverX != _hoverX)
            {
                _toggleHover = toggleHover;
                _hoverX = hoverX;
                Invalidate();
            }
        }

        public void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;

            // the chevron folds the panel to its header; a closed one opens from anywhere on it
            if (_toggle.Contains(e.Location) || !_open)
            {
                _open = !_open;
                _hoverX = null;
                Settings.Instance[SettingOpen] = _open.ToString();
                Invalidate();
            }
        }

        public void OnMouseUp(MouseEventArgs e)
        {
        }

        public void OnMouseLeave()
        {
            if (_hoverX != null || _toggleHover)
            {
                _hoverX = null;
                _toggleHover = false;
                Invalidate();
            }
        }

        /// <summary>Draws the panel; call from the map's Paint event.</summary>
        public void Paint(Graphics g)
        {
            var profile = _profile;
            if (profile == null || !Layout())
                return;

            var state = g.Save();
            try
            {
                g.ResetTransform();
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                var accent = MapButton.Accent;
                MissionProfile.Sample? hover = null;
                if (_open && _hoverX != null)
                {
                    hover = profile.At((_hoverX.Value - _plot.Left) / (double)Math.Max(1, _plot.Width) * profile.Length);
                    DrawMapMarker(g, hover.Value, accent);
                }

                using (var path = MapButton.RoundedRect(_bounds, 10))
                {
                    using (var fill = new SolidBrush(PanelFill))
                        g.FillPath(fill, path);
                    using (var edge = new Pen(PanelEdge))
                        g.DrawPath(edge, path);
                }

                DrawHeader(g, profile, accent);
                if (_open)
                {
                    DrawPlot(g, profile, accent);
                    if (hover != null)
                        DrawHover(g, profile, hover.Value, accent);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                g.Restore(state);
            }
        }

        void DrawHeader(Graphics g, MissionProfile profile, Color accent)
        {
            int middle = _bounds.Top + HeaderHeight / 2;

            // a small mountain in the accent, then the title
            float x = _bounds.Left + 12;
            using (var brush = new SolidBrush(accent))
            {
                g.FillPolygon(brush, new[]
                {
                    new PointF(x, middle + 5), new PointF(x + 5.5f, middle - 4), new PointF(x + 8.5f, middle + 1),
                    new PointF(x + 10.5f, middle - 1.5f), new PointF(x + 15, middle + 5)
                });
            }

            x += 21;
            x = DrawText(g, "TERRAIN PROFILE", _bold, TextDim, x, middle) + 14;

            double highest = double.MinValue;
            foreach (var sample in profile.Samples)
                highest = Math.Max(highest, sample.Altitude);

            x = DrawChip(g, "Length", FormatDistance(profile.Length), Text, Color.FromArgb(26, 255, 255, 255), x, middle);
            x = DrawChip(g, "Peak", FormatAltitude(highest), Text, Color.FromArgb(26, 255, 255, 255), x, middle);

            if (!double.IsNaN(profile.MinClearance))
            {
                if (profile.HasConflict)
                {
                    x = DrawChip(g, "Below terrain",
                        FormatAltitude(-profile.MinClearance) + " at " + FormatDistance(profile.MinClearanceDistance),
                        Conflict, Color.FromArgb(70, Conflict), x, middle);
                }
                else
                {
                    var color = profile.MinClearance < LowClearance ? Warning : Text;
                    x = DrawChip(g, "Clearance", FormatAltitude(profile.MinClearance), color,
                        Color.FromArgb(26, 255, 255, 255), x, middle);
                }
            }

            if (!profile.TerrainComplete)
            {
                x = DrawChip(g, null, _terrainUnavailable ? "No terrain data for part of the route" : "Loading terrain…",
                    TextDim, Color.Empty, x, middle);
            }

            // the chevron: down folds the panel, up opens it
            var chevron = _toggleHover ? accent : Color.FromArgb(200, 225, 230, 236);
            float cx = _toggle.Left + _toggle.Width / 2f, cy = _toggle.Top + _toggle.Height / 2f;
            float dy = _open ? 2.5f : -2.5f;
            using (var pen = new Pen(chevron, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - 5, cy - dy), new PointF(cx, cy + dy), new PointF(cx + 5, cy - dy) });
        }

        // draws a label and value in a pill, if it fits before the chevron; returns where the next one goes
        float DrawChip(Graphics g, string label, string value, Color valueColor, Color fill, float x, int middle)
        {
            float labelWidth = label == null ? 0 : g.MeasureString(label, _small, PointF.Empty, StringFormat.GenericTypographic).Width + 5;
            float valueWidth = g.MeasureString(value, _font, PointF.Empty, StringFormat.GenericTypographic).Width;
            var pill = new RectangleF(x, middle - 9, labelWidth + valueWidth + 16, 18);
            if (pill.Right > _toggle.Left - 8)
                return x;

            if (fill.A > 0)
            {
                using (var path = MapButton.RoundedRect(pill, 9))
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
            }

            float tx = pill.Left + 8;
            if (label != null)
                tx = DrawText(g, label, _small, TextDim, tx, middle) + 5;
            DrawText(g, value, _font, valueColor, tx, middle);
            return pill.Right + 6;
        }

        // draws text centred vertically on a line; returns its right edge
        static float DrawText(Graphics g, string text, Font font, Color color, float x, float middle)
        {
            var size = g.MeasureString(text, font, PointF.Empty, StringFormat.GenericTypographic);
            using (var brush = new SolidBrush(color))
                g.DrawString(text, font, brush, x, middle - size.Height / 2, StringFormat.GenericTypographic);
            return x + size.Width;
        }

        void DrawPlot(Graphics g, MissionProfile profile, Color accent)
        {
            if (_plot.Width < 40 || _plot.Height < 30)
                return;

            double low, high;
            PlotRange(profile, out low, out high);

            Func<double, float> X = d => (float)(_plot.Left + d / profile.Length * _plot.Width);
            Func<double, float> Y = a => (float)(_plot.Bottom - (a - low) / (high - low) * _plot.Height);

            DrawAxes(g, profile, low, high, Y);

            // clipped below the header, with room either side for the dots and numbers at the route's ends
            var clip = g.Clip;
            g.SetClip(Rectangle.FromLTRB(_plot.Left - 8, _bounds.Top + HeaderHeight, _plot.Right + 8, _plot.Bottom + 1));

            // the mission's altitude as a faint accent wash down to the floor, under the ground
            var line = new PointF[profile.Samples.Count];
            for (int i = 0; i < line.Length; i++)
                line[i] = new PointF(X(profile.Samples[i].Distance), Y(profile.Samples[i].Altitude));

            using (var wash = new GraphicsPath())
            {
                wash.AddLines(line);
                wash.AddLine(line[line.Length - 1], new PointF(line[line.Length - 1].X, _plot.Bottom));
                wash.AddLine(new PointF(line[line.Length - 1].X, _plot.Bottom), new PointF(line[0].X, _plot.Bottom));
                wash.CloseFigure();
                using (var brush = new LinearGradientBrush(new RectangleF(_plot.Left, _plot.Top - 1, _plot.Width, _plot.Height + 2),
                           Color.FromArgb(70, accent), Color.FromArgb(0, accent), LinearGradientMode.Vertical))
                    g.FillPath(brush, wash);
            }

            // the ground, filled grey, in runs where it is known
            foreach (var run in TerrainRuns(profile))
            {
                var ground = new List<PointF>();
                for (int i = run.Item1; i <= run.Item2; i++)
                    ground.Add(new PointF(X(profile.Samples[i].Distance), Y(profile.Samples[i].Terrain)));
                if (ground.Count < 2)
                    continue;

                using (var fill = new GraphicsPath())
                {
                    fill.AddLines(ground.ToArray());
                    fill.AddLine(ground[ground.Count - 1], new PointF(ground[ground.Count - 1].X, _plot.Bottom + 1));
                    fill.AddLine(new PointF(ground[ground.Count - 1].X, _plot.Bottom + 1), new PointF(ground[0].X, _plot.Bottom + 1));
                    fill.CloseFigure();
                    using (var brush = new LinearGradientBrush(new RectangleF(_plot.Left, _plot.Top - 1, _plot.Width, _plot.Height + 2),
                               TerrainTop, TerrainBottom, LinearGradientMode.Vertical))
                        g.FillPath(brush, fill);
                }

                using (var edge = new Pen(TerrainEdge, 1.4f) { LineJoin = LineJoin.Round })
                    g.DrawLines(edge, ground.ToArray());
            }

            DrawConflicts(g, profile, X, Y);

            // the mission's altitude: a soft glow under a crisp accent line
            using (var glow = new Pen(Color.FromArgb(55, accent), 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(glow, line);
            using (var pen = new Pen(accent, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLines(pen, line);

            DrawConflictLine(g, profile, X, Y);
            DrawWaypoints(g, profile, accent, X, Y);

            g.Clip = clip;
        }

        void DrawAxes(Graphics g, MissionProfile profile, double low, double high, Func<double, float> Y)
        {
            // altitude gridlines and labels, in the user's units
            double scale = CurrentState.multiplieralt;
            double step = NiceStep((high - low) * scale / 3.5);
            double first = Math.Ceiling(low * scale / step) * step;
            using (var grid = new Pen(Grid))
            using (var brush = new SolidBrush(TextDim))
            {
                for (double v = first; v <= high * scale; v += step)
                {
                    float y = Y(v / scale);
                    g.DrawLine(grid, _plot.Left, y, _plot.Right, y);
                    string text = v.ToString("0") + " " + AltUnit;
                    var size = g.MeasureString(text, _small, PointF.Empty, StringFormat.GenericTypographic);
                    g.DrawString(text, _small, brush, _plot.Left - 7 - size.Width, y - size.Height / 2,
                        StringFormat.GenericTypographic);
                }

                // distance labels along the foot
                string unit;
                double divisor;
                DistanceUnits(profile.Length, out unit, out divisor);
                double length = profile.Length * CurrentState.multiplierdist / divisor;
                double dstep = NiceStep(length / Math.Max(2, _plot.Width / 110.0));
                for (double d = 0; d <= length + dstep * 0.01; d += dstep)
                {
                    float x = (float)(_plot.Left + d / length * _plot.Width);
                    string text = d == 0 ? "0" : FormatNumber(d, dstep) + " " + unit;
                    var size = g.MeasureString(text, _small, PointF.Empty, StringFormat.GenericTypographic);
                    float tx = Math.Max(_plot.Left - 2, Math.Min(_plot.Right - size.Width + 2, x - size.Width / 2));
                    g.DrawString(text, _small, brush, tx, _plot.Bottom + 6, StringFormat.GenericTypographic);
                    g.DrawLine(grid, x, _plot.Bottom, x, _plot.Bottom + 3);
                }
            }
        }

        // where the route goes below the ground: the gap between them, filled red
        void DrawConflicts(Graphics g, MissionProfile profile, Func<double, float> X, Func<double, float> Y)
        {
            if (!profile.HasConflict)
                return;

            foreach (var run in ConflictRuns(profile))
            {
                var top = new List<PointF>();
                var bottom = new List<PointF>();
                for (int i = run.Item1; i <= run.Item2; i++)
                {
                    var s = profile.Samples[i];
                    top.Add(new PointF(X(s.Distance), Y(s.Terrain)));
                    bottom.Add(new PointF(X(s.Distance), Y(s.Altitude)));
                }

                bottom.Reverse();
                top.AddRange(bottom);
                if (top.Count < 3)
                    continue;
                using (var brush = new SolidBrush(Color.FromArgb(120, Conflict)))
                    g.FillPolygon(brush, top.ToArray());
            }
        }

        void DrawConflictLine(Graphics g, MissionProfile profile, Func<double, float> X, Func<double, float> Y)
        {
            if (!profile.HasConflict)
                return;

            using (var pen = new Pen(Conflict, 2.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var run in ConflictRuns(profile))
                {
                    var points = new List<PointF>();
                    for (int i = run.Item1; i <= run.Item2; i++)
                        points.Add(new PointF(X(profile.Samples[i].Distance), Y(profile.Samples[i].Altitude)));
                    if (points.Count >= 2)
                        g.DrawLines(pen, points.ToArray());
                }
            }
        }

        void DrawWaypoints(Graphics g, MissionProfile profile, Color accent, Func<double, float> X, Func<double, float> Y)
        {
            float lastLabelRight = float.MinValue;
            using (var ring = new Pen(accent, 1.8f))
            using (var centre = new SolidBrush(Color.FromArgb(255, 24, 26, 30)))
            using (var home = new SolidBrush(accent))
            using (var label = new SolidBrush(Color.FromArgb(215, 238, 241, 245)))
            {
                foreach (var waypoint in profile.Waypoints)
                {
                    float x = X(waypoint.Distance), y = Y(waypoint.Altitude);
                    var dot = new RectangleF(x - 4, y - 4, 8, 8);
                    if (waypoint.Label == "H")
                    {
                        g.FillEllipse(home, dot);
                    }
                    else
                    {
                        g.FillEllipse(centre, dot);
                        g.DrawEllipse(ring, dot);
                    }

                    // numbers above the dots, skipping any that would overlap the last
                    var size = g.MeasureString(waypoint.Label, _small, PointF.Empty, StringFormat.GenericTypographic);
                    float left = x - size.Width / 2;
                    if (left < lastLabelRight + 3)
                        continue;
                    float top = Math.Max(_bounds.Top + HeaderHeight + 1, y - 7 - size.Height);
                    g.DrawString(waypoint.Label, _small, label, left, top, StringFormat.GenericTypographic);
                    lastLabelRight = left + size.Width;
                }
            }
        }

        void DrawHover(Graphics g, MissionProfile profile, MissionProfile.Sample sample, Color accent)
        {
            float x = _hoverX.Value;
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, x, _bounds.Top + HeaderHeight + 2, x, _plot.Bottom);

            double low, high;
            PlotRange(profile, out low, out high);
            Func<double, float> Y = a => (float)(_plot.Bottom - (a - low) / (high - low) * _plot.Height);

            float pathY = Y(sample.Altitude);
            if (!double.IsNaN(sample.Terrain))
            {
                using (var brush = new SolidBrush(TerrainEdge))
                    g.FillEllipse(brush, x - 3.5f, Y(sample.Terrain) - 3.5f, 7, 7);
            }

            using (var halo = new SolidBrush(Color.FromArgb(70, accent)))
                g.FillEllipse(halo, x - 8, pathY - 8, 16, 16);
            using (var brush = new SolidBrush(accent))
                g.FillEllipse(brush, x - 4.5f, pathY - 4.5f, 9, 9);
            using (var ring = new Pen(Color.White, 1.5f))
                g.DrawEllipse(ring, x - 4.5f, pathY - 4.5f, 9, 9);

            // the readout card
            var lines = new List<Tuple<string, string, Color>>
            {
                Tuple.Create("Altitude", FormatAltitude(sample.Altitude) + " AMSL", Text)
            };
            if (!double.IsNaN(sample.Terrain))
            {
                lines.Add(Tuple.Create("Terrain", FormatAltitude(sample.Terrain) + " AMSL", Text));
                var clearance = sample.Clearance;
                var color = !sample.Checked ? Text : clearance < 0 ? Conflict : clearance < LowClearance ? Warning : Text;
                lines.Add(Tuple.Create("Clearance", FormatAltitude(clearance), color));
            }

            string title = FormatDistance(sample.Distance);
            const float rowHeight = 15, pad = 8, gap = 12;
            float labelWidth = 0, valueWidth = 0;
            foreach (var row in lines)
            {
                labelWidth = Math.Max(labelWidth, g.MeasureString(row.Item1, _small, PointF.Empty, StringFormat.GenericTypographic).Width);
                valueWidth = Math.Max(valueWidth, g.MeasureString(row.Item2, _font, PointF.Empty, StringFormat.GenericTypographic).Width);
            }

            float width = Math.Max(labelWidth + gap + valueWidth, g.MeasureString(title, _bold, PointF.Empty, StringFormat.GenericTypographic).Width) + pad * 2;
            float height = pad * 2 + rowHeight * (lines.Count + 1);
            float cardX = x + 14;
            if (cardX + width > _bounds.Right - 6)
                cardX = x - 14 - width;
            float cardY = Math.Max(_bounds.Top + HeaderHeight + 2, Math.Min(_bounds.Bottom - height - 6, pathY - height / 2));
            var card = new RectangleF(cardX, cardY, width, height);

            using (var path = MapButton.RoundedRect(card, 6))
            {
                using (var fill = new SolidBrush(Color.FromArgb(240, 26, 29, 34)))
                    g.FillPath(fill, path);
                using (var edge = new Pen(Color.FromArgb(120, accent)))
                    g.DrawPath(edge, path);
            }

            float rowY = card.Top + pad + rowHeight / 2;
            DrawText(g, title, _bold, Text, card.Left + pad, rowY);
            foreach (var row in lines)
            {
                rowY += rowHeight;
                DrawText(g, row.Item1, _small, TextDim, card.Left + pad, rowY);
                var valueSize = g.MeasureString(row.Item2, _font, PointF.Empty, StringFormat.GenericTypographic);
                DrawText(g, row.Item2, _font, row.Item3, card.Right - pad - valueSize.Width, rowY);
            }
        }

        // the point under the pointer, marked on the map itself
        void DrawMapMarker(Graphics g, MissionProfile.Sample sample, Color accent)
        {
            var local = _map.FromLatLngToLocal(new PointLatLng(sample.Lat, sample.Lng));
            float x = local.X, y = local.Y;
            using (var halo = new SolidBrush(Color.FromArgb(80, accent)))
                g.FillEllipse(halo, x - 13, y - 13, 26, 26);
            using (var ring = new Pen(Color.White, 2f))
                g.DrawEllipse(ring, x - 7, y - 7, 14, 14);
            using (var dot = new SolidBrush(accent))
                g.FillEllipse(dot, x - 4.5f, y - 4.5f, 9, 9);
        }

        // the altitudes the plot spans, with room above and below
        static void PlotRange(MissionProfile profile, out double low, out double high)
        {
            low = double.MaxValue;
            high = double.MinValue;
            foreach (var sample in profile.Samples)
            {
                low = Math.Min(low, sample.Altitude);
                high = Math.Max(high, sample.Altitude);
                if (!double.IsNaN(sample.Terrain))
                {
                    low = Math.Min(low, sample.Terrain);
                    high = Math.Max(high, sample.Terrain);
                }
            }

            double pad = Math.Max(5, (high - low) * 0.14);
            low -= pad;
            high += pad;
        }

        // index ranges of samples where the ground is known
        static IEnumerable<Tuple<int, int>> TerrainRuns(MissionProfile profile)
        {
            int start = -1;
            for (int i = 0; i <= profile.Samples.Count; i++)
            {
                bool known = i < profile.Samples.Count && !double.IsNaN(profile.Samples[i].Terrain);
                if (known && start < 0)
                    start = i;
                else if (!known && start >= 0)
                {
                    yield return Tuple.Create(start, i - 1);
                    start = -1;
                }
            }
        }

        // index ranges of samples below the ground, widened by one each side so the red meets the lines
        static IEnumerable<Tuple<int, int>> ConflictRuns(MissionProfile profile)
        {
            int start = -1;
            for (int i = 0; i <= profile.Samples.Count; i++)
            {
                bool below = i < profile.Samples.Count && profile.Samples[i].Checked &&
                             !double.IsNaN(profile.Samples[i].Terrain) && profile.Samples[i].Clearance < 0;
                if (below && start < 0)
                    start = i;
                else if (!below && start >= 0)
                {
                    yield return Tuple.Create(Math.Max(0, start - 1), Math.Min(profile.Samples.Count - 1, i));
                    start = -1;
                }
            }
        }

        static double NiceStep(double rough)
        {
            if (rough <= 0 || double.IsNaN(rough) || double.IsInfinity(rough))
                return 1;
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(rough)));
            double fraction = rough / magnitude;
            double nice = fraction < 1.5 ? 1 : fraction < 3.5 ? 2 : fraction < 7.5 ? 5 : 10;
            return nice * magnitude;
        }

        static string FormatNumber(double value, double step)
        {
            return step < 1 ? value.ToString("0.#") : value.ToString("0");
        }

        // long routes in km (or mi)
        static void DistanceUnits(double metres, out string unit, out double divisor)
        {
            double display = metres * CurrentState.multiplierdist;
            if (CurrentState.DistanceUnit == "ft" && display >= 5280)
            {
                unit = "mi";
                divisor = 5280;
            }
            else if (CurrentState.DistanceUnit != "ft" && display >= 1000)
            {
                unit = "km";
                divisor = 1000;
            }
            else
            {
                unit = CurrentState.DistanceUnit == "" ? "m" : CurrentState.DistanceUnit;
                divisor = 1;
            }
        }

        static string FormatDistance(double metres)
        {
            string unit;
            double divisor;
            DistanceUnits(metres, out unit, out divisor);
            double value = metres * CurrentState.multiplierdist / divisor;
            return (divisor > 1 ? value.ToString("0.00") : value.ToString("0")) + " " + unit;
        }

        static string AltUnit => string.IsNullOrEmpty(CurrentState.AltUnit) ? "m" : CurrentState.AltUnit;

        static string FormatAltitude(double metres)
        {
            return (metres * CurrentState.multiplieralt).ToString("0") + " " + AltUnit;
        }

        public void Dispose()
        {
            _disposed = true;
            _rebuild.Dispose();
            _font.Dispose();
            _small.Dispose();
            _bold.Dispose();
        }
    }
}
