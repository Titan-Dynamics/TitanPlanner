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
    /// under its route, filled grey, with the route's length, peak and least clearance, terrain conflicts in red, the
    /// rally points in purple, the vehicle's fence ceiling, floor and circle, the points outside the fence ringed in
    /// red, and a readout (and a marker on the map) for the point under the pointer. Drag a box to zoom, Ctrl+drag
    /// to pan, use the wheel to zoom about the pointer, and drag the top edge to resize it. The profile is built on a
    /// worker thread whenever the plan changes, and again while terrain tiles are still downloading.
    /// </summary>
    public sealed class TerrainProfilePanel : IMapPanel, IDisposable
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        const string SettingOpen = "plan_terrain_profile_open";
        const string SettingHeight = "plan_terrain_profile_height";
        const int Margin = 10;
        const int HeaderHeight = 32;
        const int PlotLeft = 60, PlotRight = 18, PlotTop = 40, PlotBottom = 27;
        const int MinHeight = 120;

        // the strip along the top edge that resizes the panel
        const int ResizeGrip = 5;

        // a drag shorter than this is a click
        const int DragThreshold = 8;

        // how near the pointer has to be to a waypoint for the readout to be the waypoint's
        const float MarkerHitRadius = 9;

        // the closest zoom, in metres across the plot
        const double MinSpan = 40;

        // more points outside the fence than this and the header names only the first few
        const int MaxOutsideNamed = 8;

        // clearance under this (metres) is drawn as a warning
        const double LowClearance = 10;

        // tries at loading the route's terrain before giving up on it, a second apart
        const int TerrainRetries = 30;

        static readonly Color PanelFill = Color.FromArgb(205, 17, 19, 23);
        static readonly Color PanelEdge = Color.FromArgb(40, 255, 255, 255);
        static readonly Color Text = Color.FromArgb(236, 238, 241, 245);
        static readonly Color TextDim = Color.FromArgb(150, 205, 210, 218);
        static readonly Color Grid = Color.FromArgb(26, 255, 255, 255);
        static readonly Color ChipFill = Color.FromArgb(26, 255, 255, 255);
        static readonly Color TerrainTop = Color.FromArgb(245, 104, 110, 118);
        static readonly Color TerrainBottom = Color.FromArgb(245, 50, 54, 60);
        static readonly Color TerrainEdge = Color.FromArgb(255, 164, 171, 180);
        static readonly Color Conflict = Color.FromArgb(255, 255, 78, 78);
        static readonly Color Warning = Color.FromArgb(255, 255, 196, 64);
        static readonly Color Rally = Color.FromArgb(255, 186, 133, 255);
        static readonly Color FenceOn = Color.FromArgb(255, 255, 92, 92);
        static readonly Color FenceOff = Color.FromArgb(170, 170, 176, 186);

        enum DragMode { None, Resize, Zoom, Pan }

        // the part of the profile on show: distances along the route (NaN for the whole route) and altitudes (NaN to
        // fit what is on show)
        struct View
        {
            public double Start, End, Low, High;
        }

        static readonly View WholeRoute = new View
            { Start = double.NaN, End = double.NaN, Low = double.NaN, High = double.NaN };

        readonly myGMAP _map;
        readonly System.Windows.Forms.Timer _rebuild = new System.Windows.Forms.Timer();
        readonly Font _font = new Font("Segoe UI", 9f);
        readonly Font _small = new Font("Segoe UI", 8.25f);
        readonly Font _bold = new Font("Segoe UI", 8.25f, FontStyle.Bold);

        ProfileInput _input;
        string _signature;
        int _version;
        int _retries;
        bool _terrainUnavailable;
        MissionProfile _profile;

        bool _open;
        int _height;
        bool _tall;
        Rectangle _bounds, _plot, _toggle, _showAll;
        bool _toggleHover, _showAllHover;
        Point? _hover;
        bool _disposed;

        View _view = WholeRoute;
        readonly Stack<View> _zoomHistory = new Stack<View>();
        DateTime _lastWheel, _foldedAt;

        DragMode _drag;
        Point _dragStart, _dragNow;
        View _dragView;
        int _dragHeight;
        bool _dragMoved;
        bool _cursorSet;
        Cursor _mapCursor;

        ContextMenuStrip _menu;
        ToolStripMenuItem _undoZoom, _wholeRoute, _tallItem;

        public TerrainProfilePanel(myGMAP map)
        {
            _map = map;
            _open = Settings.Instance.GetBoolean(SettingOpen, true);
            _height = Settings.Instance.GetInt32(SettingHeight, 0);
            _rebuild.Tick += (s, e) => Rebuild();
        }

        /// <summary>
        /// Shows the profile of the plan (null hides it). Rebuilt shortly after a change, so a run of edits builds it
        /// once; an input like the one on show changes nothing.
        /// </summary>
        public void SetInput(ProfileInput input)
        {
            string signature = input?.Signature();
            if (signature == _signature)
                return;

            _input = input;
            _signature = signature;
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
            var input = _input;

            if (input == null)
            {
                Apply(version, null);
                return;
            }

            ThreadPool.QueueUserWorkItem(_ =>
            {
                MissionProfile profile = null;
                try
                {
                    profile = MissionProfile.Build(input, TerrainAt);
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

            var previous = _profile;
            _profile = profile;
            KeepView(previous, profile);

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

        // A zoom stays through a rebuild (an edit, terrain arriving), unless the profile is of something else now (the
        // rally points' line instead of a route, or the other way round) or none of the route is left in the view.
        void KeepView(MissionProfile previous, MissionProfile profile)
        {
            if (!IsZoomed)
                return;

            if (profile == null || previous == null || previous.RallyChain != profile.RallyChain ||
                _view.Start >= profile.Length || _view.End <= 0)
            {
                ResetView();
                return;
            }

            _view = Clamp(_view, profile.Length);
            if (!IsZoomed)
                _zoomHistory.Clear();
        }

        // keeps a view's distances on the route, sliding it back on rather than shrinking it; one as long as the route
        // is the whole route
        static View Clamp(View view, double length)
        {
            if (double.IsNaN(view.Start))
                return view;

            double span = view.End - view.Start;
            if (span >= length)
                return WholeRoute;

            if (view.Start < 0)
            {
                view.Start = 0;
                view.End = span;
            }
            else if (view.End > length)
            {
                view.End = length;
                view.Start = length - span;
            }

            return view;
        }

        // The ground's altitude: from a GeoTIFF elevation model the user has loaded, as srtm.getAltitude prefers, else
        // from SRTM. NaN while its tile is downloading or where there is none.
        static double TerrainAt(double lat, double lng)
        {
            try
            {
                var tiff = GeoTiff.getAltitude(lat, lng);
                if (tiff.currenttype == srtm.tiletype.valid)
                    return tiff.alt;
            }
            catch (Exception ex)
            {
                log.Debug(ex);
            }

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

            int height = _open ? OpenHeight() : HeaderHeight;
            _bounds = new Rectangle(Margin, _map.Height - Margin - height, _map.Width - 2 * Margin, height);
            _toggle = new Rectangle(_bounds.Right - 30, _bounds.Top + (HeaderHeight - 22) / 2, 24, 22);
            _plot = Rectangle.FromLTRB(_bounds.Left + PlotLeft, _bounds.Top + PlotTop, _bounds.Right - PlotRight,
                _bounds.Bottom - PlotBottom);
            return true;
        }

        // the height dragged to, two thirds of the map when tall, else a share of the map; never so tall the map goes
        int OpenHeight()
        {
            int height = _tall ? _map.Height * 2 / 3
                : _height > 0 ? _height
                : Math.Max(150, Math.Min(225, (int)(_map.Height * 0.27)));
            return Math.Max(MinHeight, Math.Min(MaxHeight, height));
        }

        int MaxHeight => Math.Max(MinHeight, _map.Height - 2 * Margin - 60);

        bool IsZoomed => !double.IsNaN(_view.Start);

        double ViewStart => IsZoomed ? _view.Start : 0;

        double ViewEnd => IsZoomed ? _view.End : _profile?.Length ?? 1;

        bool InResizeGrip(Point p) => _open && p.Y >= _bounds.Top && p.Y < _bounds.Top + ResizeGrip &&
                                      p.X > _bounds.Left + 8 && p.X < _bounds.Right - 8;

        bool InPlot(Point p) => _open && _plot.Width > 0 && p.X >= _plot.Left - 4 && p.X <= _plot.Right + 4 &&
                                p.Y >= _bounds.Top + HeaderHeight && p.Y <= _bounds.Bottom;

        double DistanceAt(int x) =>
            ViewStart + (Math.Max(_plot.Left, Math.Min(_plot.Right, x)) - _plot.Left) / (double)Math.Max(1, _plot.Width) *
            (ViewEnd - ViewStart);

        public bool HitTest(Point location)
        {
            return _profile != null && _bounds.Contains(location);
        }

        public void OnMouseMove(MouseEventArgs e)
        {
            if (_drag != DragMode.None)
            {
                Drag(e.Location);
                return;
            }

            bool toggleHover = _toggle.Contains(e.Location);
            bool showAllHover = IsZoomed && _showAll.Contains(e.Location);
            Point? hover = InPlot(e.Location) ? e.Location : (Point?)null;
            SetCursor(InResizeGrip(e.Location) ? Cursors.SizeNS : null);

            if (toggleHover != _toggleHover || showAllHover != _showAllHover || hover != _hover)
            {
                _toggleHover = toggleHover;
                _showAllHover = showAllHover;
                _hover = hover;
                Invalidate();
            }
        }

        public void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Middle && InPlot(e.Location))
            {
                StartDrag(DragMode.Pan, e.Location);
                return;
            }

            if (e.Button != MouseButtons.Left)
                return;

            // the chevron folds the panel to its header; a closed one opens from anywhere on it
            if (_toggle.Contains(e.Location) || !_open)
            {
                _open = !_open;
                _foldedAt = DateTime.Now;
                _hover = null;
                Settings.Instance[SettingOpen] = _open.ToString();
                Invalidate();
                return;
            }

            if (IsZoomed && _showAll.Contains(e.Location))
            {
                ShowWholeRoute();
                return;
            }

            if (InResizeGrip(e.Location))
            {
                StartDrag(DragMode.Resize, e.Location);
                return;
            }

            if (e.Y < _bounds.Top + HeaderHeight)
            {
                // a double click on the header makes the profile tall, or its own height again; not the one whose
                // first click opened the panel
                if (e.Clicks == 2 && (DateTime.Now - _foldedAt).TotalMilliseconds > SystemInformation.DoubleClickTime)
                    SetTall(!_tall);
                return;
            }

            if (!InPlot(e.Location))
                return;

            if (e.Clicks == 2)
            {
                ShowWholeRoute();
                return;
            }

            StartDrag((Control.ModifierKeys & Keys.Control) != 0 ? DragMode.Pan : DragMode.Zoom, e.Location);
        }

        public void OnMouseUp(MouseEventArgs e)
        {
            var drag = _drag;
            _drag = DragMode.None;

            switch (drag)
            {
                case DragMode.Resize:
                    Settings.Instance[SettingHeight] = _height.ToString();
                    break;

                case DragMode.Zoom:
                    ZoomToBox(_dragStart, e.Location);
                    break;

                case DragMode.None:
                    if (e.Button == MouseButtons.Right && HitTest(e.Location))
                        ShowMenu(e.Location);
                    break;
            }

            SetCursor(InResizeGrip(e.Location) ? Cursors.SizeNS : null);
            Invalidate();
        }

        public void OnMouseWheel(MouseEventArgs e)
        {
            if (_profile == null || !InPlot(e.Location) || e.Delta == 0)
                return;

            double factor = Math.Pow(1.25, -e.Delta / 120.0);
            double start = ViewStart, end = ViewEnd;
            double span = end - start;
            double newSpan = Math.Max(MinSpan, span * factor);
            if (newSpan >= _profile.Length)
            {
                ShowWholeRoute();
                return;
            }

            // a run of wheel ticks is one step back for Undo Zoom
            if (DateTime.Now - _lastWheel > TimeSpan.FromSeconds(1))
                _zoomHistory.Push(_view);
            _lastWheel = DateTime.Now;

            // the distance under the pointer stays under it
            double at = DistanceAt(e.X);
            double fraction = span > 0 ? (at - start) / span : 0.5;
            var view = _view;
            view.Start = at - fraction * newSpan;
            view.End = view.Start + newSpan;

            // a fixed altitude range (from a zoom box) zooms with it, about the pointer's height
            if (!double.IsNaN(view.Low))
            {
                double height = view.High - view.Low;
                double pointer = view.High - (e.Y - _plot.Top) / (double)Math.Max(1, _plot.Height) * height;
                double scale = newSpan / span;
                view.Low = pointer - (pointer - view.Low) * scale;
                view.High = pointer + (view.High - pointer) * scale;
            }

            SetView(view);
        }

        public void OnMouseLeave()
        {
            if (_drag == DragMode.None)
                SetCursor(null);

            if (_hover != null || _toggleHover || _showAllHover)
            {
                _hover = null;
                _toggleHover = false;
                _showAllHover = false;
                Invalidate();
            }
        }

        void StartDrag(DragMode mode, Point location)
        {
            _drag = mode;
            _dragStart = _dragNow = location;
            _dragView = _view;
            _dragHeight = _bounds.Height;
            _dragMoved = false;
            _hover = null;
            if (mode == DragMode.Pan)
                SetCursor(Cursors.SizeAll);
        }

        void Drag(Point location)
        {
            _dragNow = location;
            switch (_drag)
            {
                case DragMode.Resize:
                    _tall = false;
                    _height = Math.Max(MinHeight, Math.Min(MaxHeight, _dragHeight + _dragStart.Y - location.Y));
                    break;

                case DragMode.Pan:
                    Pan(location);
                    break;
            }

            Invalidate();
        }

        // moves the view with the pointer: along the route when zoomed, and up and down with a fixed altitude range
        void Pan(Point location)
        {
            if (_profile == null || _plot.Width <= 0)
                return;

            var view = _dragView;
            if (!double.IsNaN(view.Start))
            {
                double span = view.End - view.Start;
                double shift = -(location.X - _dragStart.X) / (double)_plot.Width * span;
                shift = Math.Max(-view.Start, Math.Min(_profile.Length - view.End, shift));
                view.Start += shift;
                view.End += shift;
            }

            if (!double.IsNaN(view.Low))
            {
                double rise = (location.Y - _dragStart.Y) / (double)Math.Max(1, _plot.Height) * (view.High - view.Low);
                view.Low += rise;
                view.High += rise;
            }

            // a pan is one step back for Undo Zoom, as a zoom is
            if (!_dragMoved && location != _dragStart && (IsZoomed || !double.IsNaN(view.Low)))
            {
                _zoomHistory.Push(_dragView);
                _dragMoved = true;
            }

            _view = view;
        }

        // zooms to the box dragged over the plot: its distances, and its altitudes when it is tall enough to mean them
        void ZoomToBox(Point from, Point to)
        {
            if (_profile == null || Math.Abs(to.X - from.X) < DragThreshold)
                return;

            double low, high;
            PlotRange(_profile, out low, out high);
            Func<int, double> altitude = y =>
                high - (Math.Max(_plot.Top, Math.Min(_plot.Bottom, y)) - _plot.Top) / (double)Math.Max(1, _plot.Height) *
                (high - low);

            var view = new View
            {
                Start = DistanceAt(Math.Min(from.X, to.X)), End = DistanceAt(Math.Max(from.X, to.X)),
                Low = double.NaN, High = double.NaN
            };
            if (view.End - view.Start < MinSpan)
            {
                double middle = (view.Start + view.End) / 2;
                view.Start = middle - MinSpan / 2;
                view.End = middle + MinSpan / 2;
            }

            if (Math.Abs(to.Y - from.Y) >= DragThreshold)
            {
                view.Low = altitude(Math.Max(from.Y, to.Y));
                view.High = altitude(Math.Min(from.Y, to.Y));
            }

            _zoomHistory.Push(_view);
            SetView(view);
        }

        void SetView(View view)
        {
            _view = Clamp(view, _profile?.Length ?? 0);
            Invalidate();
        }

        void ResetView()
        {
            _view = WholeRoute;
            _zoomHistory.Clear();
        }

        void ShowWholeRoute()
        {
            ResetView();
            Invalidate();
        }

        void UndoZoom()
        {
            _view = _zoomHistory.Count > 0 ? _zoomHistory.Pop() : WholeRoute;
            Invalidate();
        }

        void SetTall(bool tall)
        {
            _tall = tall;
            Invalidate();
        }

        // the map's own cursor comes back when the panel is done with it
        void SetCursor(Cursor cursor)
        {
            var control = (Control)_map;
            if (cursor != null)
            {
                if (!_cursorSet)
                    _mapCursor = control.Cursor;
                _cursorSet = true;
                control.Cursor = cursor;
            }
            else if (_cursorSet)
            {
                _cursorSet = false;
                control.Cursor = _mapCursor ?? Cursors.Default;
            }
        }

        void ShowMenu(Point location)
        {
            if (_menu == null)
            {
                _menu = new ContextMenuStrip();
                _undoZoom = new ToolStripMenuItem("Undo Zoom", null, (s, e) => UndoZoom());
                _wholeRoute = new ToolStripMenuItem("Show the Whole Route", null, (s, e) => ShowWholeRoute());
                _tallItem = new ToolStripMenuItem("Tall Profile", null, (s, e) => SetTall(!_tall));
                _menu.Items.Add(_undoZoom);
                _menu.Items.Add(_wholeRoute);
                _menu.Items.Add(new ToolStripSeparator());
                _menu.Items.Add(_tallItem);
                ThemeManager.ApplyThemeTo(_menu);
            }

            _undoZoom.Enabled = IsZoomed;
            _wholeRoute.Enabled = IsZoomed;
            _tallItem.Checked = _tall;
            _tallItem.Enabled = _open;
            _menu.Show((Control)_map, location);
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
                double low, high;
                PlotRange(profile, out low, out high);
                Func<double, float> X = d => (float)(_plot.Left + (d - ViewStart) / (ViewEnd - ViewStart) * _plot.Width);
                Func<double, float> Y = a => (float)(_plot.Bottom - (a - low) / (high - low) * _plot.Height);

                // under the pointer: a waypoint near it, else the route at its distance
                MissionProfile.Waypoint hoverPoint = null;
                MissionProfile.Sample? hover = null;
                if (_open && _hover != null && _drag == DragMode.None)
                {
                    hoverPoint = MarkerAt(profile, _hover.Value, X, Y);
                    if (hoverPoint != null)
                        DrawMapMarker(g, hoverPoint.Lat, hoverPoint.Lng, profile.RallyPoints.Contains(hoverPoint) ? Rally : accent);
                    else
                    {
                        hover = profile.At(DistanceAt(_hover.Value.X));
                        DrawMapMarker(g, hover.Value.Lat, hover.Value.Lng, accent);
                    }
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
                    DrawResizeGrip(g);
                    DrawPlot(g, profile, accent, low, high, X, Y);
                    if (_drag == DragMode.Zoom)
                        DrawZoomBox(g, accent);
                    if (hoverPoint != null)
                        DrawPointReadout(g, profile, hoverPoint, X, Y, accent);
                    else if (hover != null)
                        DrawHover(g, hover.Value, X(hover.Value.Distance), Y, accent);
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

            // the way back to the whole route, next to the chevron, while zoomed
            float right = _toggle.Left - 8;
            if (IsZoomed && _open)
            {
                const string label = "Show all";
                float width = g.MeasureString(label, _font, PointF.Empty, StringFormat.GenericTypographic).Width + 18;
                _showAll = Rectangle.Round(new RectangleF(right - width, middle - 10.5f, width, 21));
                using (var path = MapButton.RoundedRect(_showAll, 10.5f))
                using (var brush = new SolidBrush(_showAllHover ? Color.FromArgb(90, accent) : ChipFill))
                    g.FillPath(brush, path);
                DrawText(g, label, _font, _showAllHover ? Text : accent, _showAll.Left + 9, middle);
                right = _showAll.Left - 8;
            }
            else
            {
                _showAll = Rectangle.Empty;
            }

            if (profile.RallyChain)
            {
                x = DrawChip(g, "Rally points", profile.RallyPoints.Count.ToString(), Text, ChipFill, x, middle, right);
                if (!double.IsNaN(profile.MinClearance))
                {
                    var color = profile.MinClearance < LowClearance ? Warning : Text;
                    x = DrawChip(g, "Min clearance", FormatAltitude(profile.MinClearance), color, ChipFill, x, middle, right);
                }
            }
            else
            {
                x = DrawChip(g, "Length", FormatDistance(profile.Length), Text, ChipFill, x, middle, right);

                // the deepest the route goes into the ground, else how close the legs in the air come to it
                if (profile.HasConflict)
                {
                    x = DrawChip(g, "Below terrain",
                        FormatAltitude(profile.Conflict) + " at " + FormatDistance(profile.ConflictDistance),
                        Conflict, Color.FromArgb(70, Conflict), x, middle, right);
                }
                else if (!double.IsNaN(profile.MinClearance))
                {
                    var color = profile.MinClearance < LowClearance ? Warning : Text;
                    x = DrawChip(g, "Min clearance", FormatAltitude(profile.MinClearance), color, ChipFill, x, middle, right);
                }
            }

            // the points outside the fence by name, so they can be found even when crowded
            if (profile.OutsideFence.Count > 0)
            {
                var named = profile.OutsideFence.ConvertAll(a => a.Label);
                if (named.Count > MaxOutsideNamed)
                {
                    named.RemoveRange(MaxOutsideNamed, named.Count - MaxOutsideNamed);
                    named.Add("…");
                }

                x = DrawChip(g, "Outside fence", string.Join(", ", named), FenceOn, Color.FromArgb(70, FenceOn), x,
                    middle, right);
            }

            // what the vehicle says of its fence, so the fence lines can be checked against it
            var fenceState = profile.Fence?.State;
            if (fenceState != null)
                x = DrawChip(g, "Fence", FenceStateText(fenceState.Value), profile.Fence.Active ? FenceOn : TextDim,
                    ChipFill, x, middle, right);

            if (!profile.RallyChain)
            {
                double highest = double.MinValue;
                foreach (var sample in profile.Samples)
                {
                    if (!double.IsNaN(sample.Altitude))
                        highest = Math.Max(highest, sample.Altitude);
                }

                if (highest > double.MinValue)
                    x = DrawChip(g, "Peak", FormatAltitude(highest), Text, ChipFill, x, middle, right);
            }

            if (!profile.TerrainComplete)
            {
                DrawChip(g, null, _terrainUnavailable ? "No terrain data for part of the route" : "Loading terrain…",
                    TextDim, Color.Empty, x, middle, right);
            }

            // the chevron: down folds the panel, up opens it
            var chevron = _toggleHover ? accent : Color.FromArgb(200, 225, 230, 236);
            float cx = _toggle.Left + _toggle.Width / 2f, cy = _toggle.Top + _toggle.Height / 2f;
            float dy = _open ? 2.5f : -2.5f;
            using (var pen = new Pen(chevron, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                g.DrawLines(pen, new[] { new PointF(cx - 5, cy - dy), new PointF(cx, cy + dy), new PointF(cx + 5, cy - dy) });
        }

        static string FenceStateText(FenceState state)
        {
            switch (state)
            {
                case FenceState.Enabled:
                    return "on";
                case FenceState.EnablesAfterTakeoff:
                    return "on after takeoff";
                case FenceState.EnablesWhenArmed:
                    return "on when armed";
                case FenceState.ReportOnly:
                    return "reports only";
                default:
                    return "off";
            }
        }

        // a short grip centred on the top edge, brighter while it is being dragged
        void DrawResizeGrip(Graphics g)
        {
            float cx = _bounds.Left + _bounds.Width / 2f, y = _bounds.Top + 3.5f;
            var color = _drag == DragMode.Resize || _cursorSet && _drag == DragMode.None && _hover == null
                ? Color.FromArgb(150, 255, 255, 255)
                : Color.FromArgb(45, 255, 255, 255);
            using (var pen = new Pen(color, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(pen, cx - 16, y, cx + 16, y);
        }

        // draws a label and value in a pill, if it fits before the right limit; returns where the next one goes
        float DrawChip(Graphics g, string label, string value, Color valueColor, Color fill, float x, int middle, float right)
        {
            float labelWidth = label == null ? 0 : g.MeasureString(label, _small, PointF.Empty, StringFormat.GenericTypographic).Width + 5;
            float valueWidth = g.MeasureString(value, _font, PointF.Empty, StringFormat.GenericTypographic).Width;
            var pill = new RectangleF(x, middle - 10.5f, labelWidth + valueWidth + 18, 21);
            if (pill.Right > right)
                return x;

            if (fill.A > 0)
            {
                using (var path = MapButton.RoundedRect(pill, 10.5f))
                using (var brush = new SolidBrush(fill))
                    g.FillPath(brush, path);
            }

            float tx = pill.Left + 9;
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

        void DrawPlot(Graphics g, MissionProfile profile, Color accent, double low, double high,
            Func<double, float> X, Func<double, float> Y)
        {
            if (_plot.Width < 40 || _plot.Height < 30)
                return;

            DrawAxes(g, low, high, X, Y);

            // clipped below the header, with room either side for the dots and numbers at the route's ends (none while
            // zoomed, where the route carries on past the edges)
            var clip = g.Clip;
            int spare = IsZoomed ? 0 : 8;
            g.SetClip(Rectangle.FromLTRB(_plot.Left - spare, _bounds.Top + HeaderHeight, _plot.Right + spare, _plot.Bottom + 1));

            // the samples on show, and one either side so the lines run to the edges
            int first = Math.Max(0, profile.IndexAt(ViewStart) - 1);
            int last = Math.Min(profile.Samples.Count - 1, profile.IndexAt(ViewEnd));

            PointF[] line = null;
            if (!profile.RallyChain && last > first)
            {
                line = new PointF[last - first + 1];
                for (int i = first; i <= last; i++)
                    line[i - first] = new PointF(X(profile.Samples[i].Distance), Y(profile.Samples[i].Altitude));

                // the mission's altitude as a faint accent wash down to the floor, under the ground
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
            }

            // the ground, filled grey, in runs where it is known
            foreach (var run in TerrainRuns(profile, first, last))
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

            DrawCircleCrossings(g, profile, X);
            DrawConflicts(g, profile, first, last, X, Y);
            DrawFenceLimit(g, profile, profile.Fence?.Ceiling, profile.FenceCeiling, "Fence ceiling", false, Y);
            DrawFenceLimit(g, profile, profile.Fence?.Floor, profile.FenceFloor, "Fence floor", true, Y);

            if (line != null)
            {
                // the mission's altitude: a soft glow under a crisp accent line
                using (var glow = new Pen(Color.FromArgb(55, accent), 7f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(glow, line);
                using (var pen = new Pen(accent, 2.2f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawLines(pen, line);
            }

            DrawConflictLine(g, profile, first, last, X, Y);
            DrawWaypoints(g, profile, accent, X, Y);
            DrawRallyPoints(g, profile, X, Y);

            g.Clip = clip;
        }

        void DrawAxes(Graphics g, double low, double high, Func<double, float> X, Func<double, float> Y)
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
                    string text = FormatNumber(v, step) + " " + AltUnit;
                    var size = g.MeasureString(text, _small, PointF.Empty, StringFormat.GenericTypographic);
                    g.DrawString(text, _small, brush, _plot.Left - 7 - size.Width, y - size.Height / 2,
                        StringFormat.GenericTypographic);
                }

                // distance labels along the foot, in units that suit the stretch on show
                double start = ViewStart, end = ViewEnd;
                string unit;
                double divisor;
                DistanceUnits(end - start, out unit, out divisor);
                double perMetre = CurrentState.multiplierdist / divisor;
                double dstep = NiceStep((end - start) * perMetre / Math.Max(2, _plot.Width / 110.0));
                for (long k = (long)Math.Ceiling(start * perMetre / dstep - 1e-9); k * dstep <= end * perMetre + dstep * 0.01; k++)
                {
                    double d = k * dstep;
                    float x = X(d / perMetre);
                    string text = k == 0 ? "0" : FormatNumber(d, dstep) + " " + unit;
                    var size = g.MeasureString(text, _small, PointF.Empty, StringFormat.GenericTypographic);
                    float tx = Math.Max(_plot.Left - 2, Math.Min(_plot.Right - size.Width + 2, x - size.Width / 2));
                    g.DrawString(text, _small, brush, tx, _plot.Bottom + 6, StringFormat.GenericTypographic);
                    g.DrawLine(grid, x, _plot.Bottom, x, _plot.Bottom + 3);
                }
            }
        }

        // where the route goes below the ground: the gap between them, filled red
        void DrawConflicts(Graphics g, MissionProfile profile, int first, int last, Func<double, float> X, Func<double, float> Y)
        {
            if (!profile.HasConflict)
                return;

            foreach (var run in ConflictRuns(profile, first, last))
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

        void DrawConflictLine(Graphics g, MissionProfile profile, int first, int last, Func<double, float> X, Func<double, float> Y)
        {
            if (!profile.HasConflict)
                return;

            using (var pen = new Pen(Conflict, 2.4f) { LineJoin = LineJoin.Round, StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                foreach (var run in ConflictRuns(profile, first, last))
                {
                    var points = new List<PointF>();
                    for (int i = run.Item1; i <= run.Item2; i++)
                        points.Add(new PointF(X(profile.Samples[i].Distance), Y(profile.Samples[i].Altitude)));
                    if (points.Count >= 2)
                        g.DrawLines(pen, points.ToArray());
                }
            }
        }

        // A fence limit as a level line across the plot, its name just above it: dashed red when the vehicle will
        // hold to it, dotted grey when the fence is off or doesn't use it.
        void DrawFenceLimit(Graphics g, MissionProfile profile, FenceLimit limit, double altitude, string name, bool floor,
            Func<double, float> Y)
        {
            if (limit == null || double.IsNaN(altitude))
                return;

            bool enforced = profile.Fence.Enforces(limit);
            string label = name + " " + FormatAltitude(Math.Abs(limit.Altitude)) +
                           (limit.Altitude < 0 ? " below home" : " above home");
            if (!limit.Used)
                label += " · not in FENCE_TYPE";
            else if (floor && enforced)
                // ArduPilot arms the floor once the vehicle has climbed above it, so a climb out from below is no breach
                label += " · active once above it";

            float y = Y(altitude);
            if (y < _plot.Top - 1 || y > _plot.Bottom + 1)
                return;

            var color = enforced ? FenceOn : FenceOff;
            using (var pen = new Pen(color, 1.5f) { DashStyle = enforced ? DashStyle.Dash : DashStyle.Dot })
                g.DrawLine(pen, _plot.Left, y, _plot.Right, y);

            var size = g.MeasureString(label, _small, PointF.Empty, StringFormat.GenericTypographic);
            float top = floor && y - size.Height - 2 < _plot.Top ? y + 2 : y - size.Height - 2;
            DrawLabel(g, label, color, _plot.Left + 6, top, size);
        }

        // a small label on a dark backing, so it reads over the ground and the route
        void DrawLabel(Graphics g, string text, Color color, float x, float y, SizeF size)
        {
            using (var path = MapButton.RoundedRect(new RectangleF(x - 3, y - 1, size.Width + 6, size.Height + 2), 3))
            using (var backing = new SolidBrush(Color.FromArgb(170, 17, 19, 23)))
                g.FillPath(backing, path);
            using (var brush = new SolidBrush(color))
                g.DrawString(text, _small, brush, x, y, StringFormat.GenericTypographic);
        }

        // where the route crosses the fence circle: a dashed red line from top to bottom, with the radius beside it
        void DrawCircleCrossings(Graphics g, MissionProfile profile, Func<double, float> X)
        {
            if (profile.FenceCircleCrossings.Count == 0)
                return;

            string label = "Fence radius " + FormatDistance(profile.Fence.CircleRadius);
            var size = g.MeasureString(label, _small, PointF.Empty, StringFormat.GenericTypographic);
            float lastLabel = float.MinValue;
            using (var pen = new Pen(FenceOn, 1.5f) { DashStyle = DashStyle.Dash })
            {
                foreach (var distance in profile.FenceCircleCrossings)
                {
                    float x = X(distance);
                    if (x < _plot.Left - 1 || x > _plot.Right + 1)
                        continue;
                    g.DrawLine(pen, x, _plot.Top - 6, x, _plot.Bottom);

                    // one label for crossings close together; on the right it goes on the line's left, so it isn't cut off
                    if (x - lastLabel < size.Width + 16)
                        continue;
                    lastLabel = x;
                    bool left = x + 6 + size.Width > _plot.Right;
                    DrawLabel(g, label, FenceOn, left ? x - 6 - size.Width : x + 6, _plot.Top - 4, size);
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
                    if (x < _plot.Left - 10 || x > _plot.Right + 10)
                        continue;

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

                    lastLabelRight = DrawMarkerLabel(g, waypoint, x, y, label, lastLabelRight);
                }
            }
        }

        // the rally points: purple diamonds at their own altitude, at the route's nearest point
        void DrawRallyPoints(Graphics g, MissionProfile profile, Func<double, float> X, Func<double, float> Y)
        {
            float lastLabelRight = float.MinValue;
            using (var fill = new SolidBrush(Rally))
            using (var edge = new Pen(Color.FromArgb(255, 24, 26, 30), 1.2f))
            using (var label = new SolidBrush(Rally))
            {
                foreach (var point in profile.RallyPoints)
                {
                    float x = X(point.Distance), y = Y(point.Altitude);
                    if (x < _plot.Left - 10 || x > _plot.Right + 10)
                        continue;

                    var diamond = new[]
                        { new PointF(x, y - 6), new PointF(x + 6, y), new PointF(x, y + 6), new PointF(x - 6, y) };
                    g.FillPolygon(fill, diamond);
                    g.DrawPolygon(edge, diamond);
                    lastLabelRight = DrawMarkerLabel(g, point, x, y, label, lastLabelRight);
                }
            }
        }

        // A point's label above it, skipped where it would overlap the last; a point outside the fence is ringed in red
        // and always labelled, white on red. Returns the right edge of the last label drawn.
        float DrawMarkerLabel(Graphics g, MissionProfile.Waypoint point, float x, float y, Brush brush, float lastLabelRight)
        {
            bool outside = point.Breach != FenceBreach.None;
            if (outside)
            {
                using (var ring = new Pen(FenceOn, 2.4f))
                    g.DrawEllipse(ring, x - 9, y - 9, 18, 18);
            }

            var size = g.MeasureString(point.Label, outside ? _bold : _small, PointF.Empty, StringFormat.GenericTypographic);
            float left = x - size.Width / 2;
            if (left < lastLabelRight + 3 && !outside)
                return lastLabelRight;

            float top = Math.Max(_bounds.Top + HeaderHeight + 1, y - (outside ? 12 : 7) - size.Height);
            if (outside)
            {
                var pill = new RectangleF(left - 4, top - 1, size.Width + 8, size.Height + 2);
                using (var path = MapButton.RoundedRect(pill, 4))
                using (var red = new SolidBrush(FenceOn))
                    g.FillPath(red, path);
                using (var white = new SolidBrush(Color.White))
                    g.DrawString(point.Label, _bold, white, left, top, StringFormat.GenericTypographic);
                return left + size.Width + 4;
            }

            g.DrawString(point.Label, _small, brush, left, top, StringFormat.GenericTypographic);
            return left + size.Width;
        }

        void DrawZoomBox(Graphics g, Color accent)
        {
            var box = Rectangle.FromLTRB(Math.Min(_dragStart.X, _dragNow.X), Math.Min(_dragStart.Y, _dragNow.Y),
                Math.Max(_dragStart.X, _dragNow.X), Math.Max(_dragStart.Y, _dragNow.Y));
            box.Intersect(_plot);

            // a box too short to mean altitudes zooms the whole height
            if (Math.Abs(_dragNow.Y - _dragStart.Y) < DragThreshold)
                box = Rectangle.FromLTRB(box.Left, _plot.Top, box.Right, _plot.Bottom);
            if (box.Width <= 0)
                return;

            using (var fill = new SolidBrush(Color.FromArgb(40, accent)))
                g.FillRectangle(fill, box);
            using (var edge = new Pen(Color.FromArgb(180, accent)))
                g.DrawRectangle(edge, box);
        }

        // the waypoint or rally point nearest the pointer, if it is close enough to mean it
        MissionProfile.Waypoint MarkerAt(MissionProfile profile, Point pointer, Func<double, float> X, Func<double, float> Y)
        {
            MissionProfile.Waypoint best = null;
            float bestDistance = MarkerHitRadius;
            foreach (var list in new[] { profile.Waypoints, profile.RallyPoints })
            {
                foreach (var point in list)
                {
                    float dx = X(point.Distance) - pointer.X, dy = Y(point.Altitude) - pointer.Y;
                    float distance = (float)Math.Sqrt(dx * dx + dy * dy);
                    if (distance <= bestDistance)
                    {
                        best = point;
                        bestDistance = distance;
                    }
                }
            }

            return best;
        }

        void DrawHover(Graphics g, MissionProfile.Sample sample, float x, Func<double, float> Y, Color accent)
        {
            using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255)) { DashStyle = DashStyle.Dash })
                g.DrawLine(pen, x, _bounds.Top + HeaderHeight + 2, x, _plot.Bottom);

            if (!double.IsNaN(sample.Terrain))
            {
                using (var brush = new SolidBrush(TerrainEdge))
                    g.FillEllipse(brush, x - 3.5f, Y(sample.Terrain) - 3.5f, 7, 7);
            }

            // along the rally points' line nothing flies, so there is only the ground
            float anchor;
            var lines = new List<Tuple<string, string, Color>>();
            if (double.IsNaN(sample.Altitude))
            {
                anchor = double.IsNaN(sample.Terrain) ? _plot.Top + _plot.Height / 2f : Y(sample.Terrain);
            }
            else
            {
                anchor = Y(sample.Altitude);
                using (var halo = new SolidBrush(Color.FromArgb(70, accent)))
                    g.FillEllipse(halo, x - 8, anchor - 8, 16, 16);
                using (var brush = new SolidBrush(accent))
                    g.FillEllipse(brush, x - 4.5f, anchor - 4.5f, 9, 9);
                using (var ring = new Pen(Color.White, 1.5f))
                    g.DrawEllipse(ring, x - 4.5f, anchor - 4.5f, 9, 9);
                lines.Add(Tuple.Create("Altitude", FormatAltitude(sample.Altitude) + " AMSL", Text));
            }

            if (!double.IsNaN(sample.Terrain))
            {
                lines.Add(Tuple.Create("Terrain", FormatAltitude(sample.Terrain) + " AMSL", Text));
                if (!double.IsNaN(sample.Altitude))
                {
                    var clearance = sample.Clearance;
                    var color = sample.Checked && clearance < 0 ? Conflict
                        : sample.EnRoute && clearance < LowClearance ? Warning
                        : Text;
                    lines.Add(Tuple.Create("Clearance", FormatAltitude(clearance), color));
                }
            }

            DrawCard(g, FormatDistance(sample.Distance), lines, x, anchor, accent);
        }

        // the readout for a waypoint or rally point under the pointer, with how it is outside the fence
        void DrawPointReadout(Graphics g, MissionProfile profile, MissionProfile.Waypoint point, Func<double, float> X,
            Func<double, float> Y, Color accent)
        {
            bool rally = profile.RallyPoints.Contains(point);
            float x = X(point.Distance), y = Y(point.Altitude);
            using (var halo = new SolidBrush(Color.FromArgb(80, rally ? Rally : accent)))
                g.FillEllipse(halo, x - 10, y - 10, 20, 20);

            string title = point.Label == "H" ? "Home"
                : point.Label == "RTL" ? "Return to launch"
                : rally ? "Rally point " + point.Label
                : "Waypoint " + point.Label;

            var lines = new List<Tuple<string, string, Color>>
            {
                Tuple.Create("Altitude", FormatAltitude(point.Altitude) + " AMSL", Text)
            };
            if (!double.IsNaN(point.Terrain))
            {
                lines.Add(Tuple.Create("Terrain", FormatAltitude(point.Terrain) + " AMSL", Text));
                double clearance = point.Altitude - point.Terrain;
                lines.Add(Tuple.Create("Clearance", FormatAltitude(clearance),
                    !point.OnGround && clearance < LowClearance ? Warning : Text));
            }

            if (rally && !profile.RallyChain)
                lines.Add(Tuple.Create("From route", FormatDistance(point.OffRoute), Text));

            foreach (var reason in BreachText(point.Breach))
                lines.Add(Tuple.Create("Outside fence", reason, FenceOn));

            DrawCard(g, title, lines, x, y, rally ? Rally : accent);
        }

        static IEnumerable<string> BreachText(FenceBreach breach)
        {
            if ((breach & FenceBreach.AboveCeiling) != 0)
                yield return "above the ceiling";
            if ((breach & FenceBreach.BelowFloor) != 0)
                yield return "below the floor";
            if ((breach & FenceBreach.OutsideCircle) != 0)
                yield return "outside the circle";
            if ((breach & FenceBreach.OutsideInclusion) != 0)
                yield return "outside the fence area";
            if ((breach & FenceBreach.InsideExclusion) != 0)
                yield return "inside an exclusion zone";
        }

        // a readout card beside a point of the plot: a title, then label and value rows
        void DrawCard(Graphics g, string title, List<Tuple<string, string, Color>> lines, float x, float anchor, Color accent)
        {
            const float rowHeight = 18, pad = 9, gap = 14;
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
            float cardY = Math.Max(_bounds.Top + HeaderHeight + 2, Math.Min(_bounds.Bottom - height - 6, anchor - height / 2));
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
        void DrawMapMarker(Graphics g, double lat, double lng, Color color)
        {
            var local = _map.FromLatLngToLocal(new PointLatLng(lat, lng));
            float x = local.X, y = local.Y;
            using (var halo = new SolidBrush(Color.FromArgb(80, color)))
                g.FillEllipse(halo, x - 13, y - 13, 26, 26);
            using (var ring = new Pen(Color.White, 2f))
                g.DrawEllipse(ring, x - 7, y - 7, 14, 14);
            using (var dot = new SolidBrush(color))
                g.FillEllipse(dot, x - 4.5f, y - 4.5f, 9, 9);
        }

        // The altitudes the plot spans: the box zoomed to, else what is on show (the route, the ground, the points and
        // the fence limits), with room above and below.
        void PlotRange(MissionProfile profile, out double low, out double high)
        {
            if (!double.IsNaN(_view.Low))
            {
                low = _view.Low;
                high = Math.Max(_view.High, _view.Low + 1);
                return;
            }

            double lowest = double.MaxValue, highest = double.MinValue;
            Action<double> include = v =>
            {
                if (double.IsNaN(v))
                    return;
                lowest = Math.Min(lowest, v);
                highest = Math.Max(highest, v);
            };

            double start = ViewStart, end = ViewEnd;
            int first = Math.Max(0, profile.IndexAt(start) - 1);
            int last = Math.Min(profile.Samples.Count - 1, profile.IndexAt(end));
            for (int i = first; i <= last; i++)
            {
                include(profile.Samples[i].Altitude);
                include(profile.Samples[i].Terrain);
            }

            foreach (var list in new[] { profile.Waypoints, profile.RallyPoints })
            {
                foreach (var point in list)
                {
                    if (point.Distance >= start && point.Distance <= end)
                        include(point.Altitude);
                }
            }

            include(profile.FenceCeiling);
            include(profile.FenceFloor);

            if (lowest > highest)
            {
                lowest = 0;
                highest = 100;
            }

            double pad = Math.Max(5, (highest - lowest) * 0.14);
            low = lowest - pad;
            high = highest + pad;
        }

        // index ranges of samples where the ground is known, among those from first to last
        static IEnumerable<Tuple<int, int>> TerrainRuns(MissionProfile profile, int first, int last)
        {
            int start = -1;
            for (int i = first; i <= last + 1; i++)
            {
                bool known = i <= last && !double.IsNaN(profile.Samples[i].Terrain);
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
        static IEnumerable<Tuple<int, int>> ConflictRuns(MissionProfile profile, int first, int last)
        {
            int start = -1;
            for (int i = first; i <= last + 1; i++)
            {
                bool below = i <= last && profile.Samples[i].Checked &&
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

        // a value on an axis with as many decimals as its step needs
        static string FormatNumber(double value, double step)
        {
            int decimals = step >= 1 ? 0 : (int)Math.Ceiling(-Math.Log10(step) - 1e-9);
            return value.ToString("F" + decimals);
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
            _menu?.Dispose();
            _font.Dispose();
            _small.Dispose();
            _bold.Dispose();
        }
    }
}
