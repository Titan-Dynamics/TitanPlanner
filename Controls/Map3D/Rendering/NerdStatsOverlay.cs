using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// What the scene was doing this frame, for the "Stats for nerds" panel.
    /// </summary>
    public sealed class SceneStats
    {
        public double CpuMs;
        public double TerrainMs;
        public string Mode = "";
        public bool Connected;
        public double Heading;
        public double Pitch;
        public double ViewRadius;
        public double FogStart, FogEnd;
        public double FovDegrees;
        public int MaxZoom;
        public int UtmZone;
        public int OriginMoves;
        public string Renderer = "";
        public string GLVersion = "";
    }

    /// <summary>
    /// "Stats for nerds": a colour-coded panel in the top-right corner with frame timing, a scrolling frame
    /// time graph, terrain streaming internals, the view and the GPU. Text is rendered with GDI+ a few times a
    /// second; the graph is drawn with GL every frame so it scrolls smoothly.
    /// </summary>
    public sealed class NerdStatsOverlay : IDisposable
    {
        const int PanelWidth = 344;
        const int Margin = 8;
        const int Pad = 12;
        const int RowHeight = 15;
        const int ValueX = 96;
        const int GraphHeight = 54;
        const float GraphMaxMs = 50;
        const int RefreshMs = 250;

        /// <summary>One graph bar per frame, two pixels wide.</summary>
        public const int GraphSamples = (PanelWidth - 2 * Pad) / 2;

        /// <summary>
        /// StringFormat.GenericTypographic builds a new GDI+ object on every access; the panel draws and measures
        /// hundreds of strings per refresh, so keep one.
        /// </summary>
        static readonly StringFormat Typographic = StringFormat.GenericTypographic;

        static readonly Color Background = Color.FromArgb(214, 13, 15, 19);
        static Color Accent
        {
            get { return ThemeColors.Accent; }
        }
        static readonly Color Label = Color.FromArgb(138, 148, 160);
        static readonly Color Value = Color.FromArgb(232, 236, 241);
        static readonly Color Dim = Color.FromArgb(105, 112, 122);
        static readonly Color Good = Color.FromArgb(88, 222, 132);
        static readonly Color Warn = Color.FromArgb(255, 198, 72);
        static readonly Color Bad = Color.FromArgb(255, 92, 92);
        static readonly Color Info = Color.FromArgb(96, 200, 255);

        readonly QuadRenderer _quads = new QuadRenderer();
        readonly VertexList _bars = new VertexList();
        readonly VertexList _lines = new VertexList();
        readonly Stopwatch _sinceRefresh = new Stopwatch();
        readonly Font _titleFont = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font _sectionFont = new Font("Segoe UI", 7.5f, FontStyle.Bold, GraphicsUnit.Point);
        readonly Font _rowFont = new Font("Consolas", 8.5f, FontStyle.Regular, GraphicsUnit.Point);
        readonly Font _smallFont = new Font("Consolas", 7.25f, FontStyle.Regular, GraphicsUnit.Point);
        int _texture;
        int _height = 400;
        int _graphTop;
        bool _disposed;

        public void Draw(LineRenderer lines, int viewportWidth, int viewportHeight, FrameStats frames,
            TerrainStats terrain, SceneStats scene)
        {
            // The fonts are gone after Dispose; GDI+ would throw "Parameter is not valid" from inside the paint.
            if (_disposed || viewportWidth <= 0 || viewportHeight <= 0)
                return;

            if (_texture == 0 || !_sinceRefresh.IsRunning || _sinceRefresh.ElapsedMilliseconds >= RefreshMs)
            {
                RenderPanel(frames, terrain, scene);
                _sinceRefresh.Restart();
            }

            // Shrink to fit short views rather than run off the bottom.
            float scale = Math.Min(1f, Math.Max(0.5f, (viewportHeight - 2f * Margin) / _height));
            float left = viewportWidth - Margin - PanelWidth * scale;
            float top = Margin;

            var projection = Matrix4.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, -1, 1);
            var modelView = Matrix4.Mult(Matrix4.CreateScale(scale), Matrix4.CreateTranslation(left, top, 0));

            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            _quads.Begin();
            _quads.Add(_texture, new Vector3d(0, 0, 0), new Vector3d(PanelWidth, 0, 0),
                new Vector3d(0, _height, 0), new Vector3d(PanelWidth, _height, 0));
            _quads.Draw(ref projection, ref modelView);

            DrawGraph(lines, frames, ref projection, ref modelView);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        /// <summary>Frame-time bars (green within 60 fps, amber within 30, red slower), CPU time line, 60/30 fps guides.</summary>
        void DrawGraph(LineRenderer lines, FrameStats frames, ref Matrix4 projection, ref Matrix4 modelView)
        {
            float x0 = Pad;
            float bottom = _graphTop + GraphHeight;
            float barWidth = (PanelWidth - 2f * Pad) / GraphSamples;
            int count = Math.Min(frames.Count, GraphSamples);
            int offset = GraphSamples - count;

            _bars.Clear();
            for (int i = 0; i < count; i++)
            {
                float ms = frames.Interval(frames.Count - count + i);
                float height = Math.Min(1f, ms / GraphMaxMs) * GraphHeight;
                Color c = ms <= 17.5f ? Good : ms <= 34f ? Warn : Bad;
                float r = c.R / 255f, g = c.G / 255f, b = c.B / 255f;
                float xa = x0 + (offset + i) * barWidth;
                float xb = xa + Math.Max(1f, barWidth - 0.5f);
                float ya = bottom - height;
                // Brighter at the top of each bar.
                _bars.AddColored(xa, bottom, 0, r * 0.55f, g * 0.55f, b * 0.55f, 0.85f);
                _bars.AddColored(xb, bottom, 0, r * 0.55f, g * 0.55f, b * 0.55f, 0.85f);
                _bars.AddColored(xa, ya, 0, r, g, b, 0.95f);
                _bars.AddColored(xb, bottom, 0, r * 0.55f, g * 0.55f, b * 0.55f, 0.85f);
                _bars.AddColored(xb, ya, 0, r, g, b, 0.95f);
                _bars.AddColored(xa, ya, 0, r, g, b, 0.95f);
            }

            lines.Draw(_bars, PrimitiveType.Triangles, 1f, ref projection, ref modelView);

            _lines.Clear();
            foreach (float guideMs in new[] { 1000f / 60, 1000f / 30 })
            {
                float y = bottom - guideMs / GraphMaxMs * GraphHeight;
                _lines.AddColored(x0, y, 0, 1, 1, 1, 0.22f);
                _lines.AddColored(PanelWidth - Pad, y, 0, 1, 1, 1, 0.22f);
            }

            lines.Draw(_lines, PrimitiveType.Lines, 1f, ref projection, ref modelView);

            _lines.Clear();
            for (int i = 0; i < count; i++)
            {
                float ms = frames.Cpu(frames.Count - count + i);
                float y = bottom - Math.Min(1f, ms / GraphMaxMs) * GraphHeight;
                _lines.AddColored(x0 + (offset + i + 0.5f) * barWidth, y, 0, Info.R / 255f, Info.G / 255f, Info.B / 255f, 1f);
            }

            lines.Draw(_lines, PrimitiveType.LineStrip, 1.5f, ref projection, ref modelView);
        }

        void RenderPanel(FrameStats frames, TerrainStats terrain, SceneStats scene)
        {
            // Lay out once to measure, then draw at the final height.
            int height;
            using (var measure = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(measure))
                height = Layout(g, frames, terrain, scene, false);

            using (var bitmap = new Bitmap(PanelWidth, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

                    using (var path = RoundedRect(new RectangleF(0.5f, 0.5f, PanelWidth - 1, height - 1), 7))
                    using (var fill = new SolidBrush(Background))
                    using (var border = new Pen(Color.FromArgb(150, Accent), 1f))
                    {
                        g.FillPath(fill, path);
                        g.DrawPath(border, path);
                    }

                    // Accent strip along the top edge.
                    using (var strip = new LinearGradientBrush(new PointF(0, 0), new PointF(PanelWidth, 0),
                        Color.FromArgb(220, Accent), Color.FromArgb(0, Accent)))
                        g.FillRectangle(strip, 7, 1, PanelWidth - 14, 2);

                    Layout(g, frames, terrain, scene, true);
                }

                GLTextures.Delete(ref _texture);
                _texture = GLTextures.CreateFromImage(bitmap, false);
                _height = height;
            }
        }

        /// <summary>Draws (or, with draw false, only measures) the panel contents; returns its height.</summary>
        int Layout(Graphics g, FrameStats frames, TerrainStats terrain, SceneStats scene, bool draw)
        {
            float y = 8;
            if (draw)
                Text(g, "STATS FOR NERDS", _titleFont, Value, Pad, y);

            y += 23;

            // Frame
            y = Section(g, "FRAME", y, draw);
            double fps = frames.CurrentFps();
            if (draw)
            {
                Row(g, y, "FPS",
                    Part(fps.ToString("0.0"), FpsColor(fps)),
                    Part("  avg ", Dim), Part(frames.AverageFps().ToString("0"), Value),
                    Part("  low ", Dim), Part(frames.LowFps().ToString("0"), FpsColor(frames.LowFps())));
                Row(g, y + RowHeight, "Frame",
                    Part((1000 / Math.Max(0.1, fps)).ToString("0.0") + " ms", Value),
                    Part("  cpu ", Dim), Part(scene.CpuMs.ToString("0.0"), CpuColor(scene.CpuMs)),
                    Part("  terrain ", Dim), Part(scene.TerrainMs.ToString("0.0"), CpuColor(scene.TerrainMs * 2)),
                    Part("  up ", Dim), Part(terrain.UploadMs.ToString("0.0"), CpuColor(terrain.UploadMs * 2)));
            }

            y += 2 * RowHeight + 4;
            _graphTop = (int)y;
            if (draw)
                GraphFrame(g, y);
            y += GraphHeight + 17;

            // Terrain
            y = Section(g, "TERRAIN", y, draw);
            if (draw)
            {
                Row(g, y, "Tiles",
                    Part(terrain.Drawn.ToString(), Value), Part(" drawn  ", Dim),
                    Part(terrain.Resident.ToString(), Value), Part(" on GPU  ", Dim),
                    Part(terrain.Nodes.ToString(), Value), Part(" known", Dim));
                ZoomRow(g, y + RowHeight, terrain);
                MemoryRow(g, y + 2 * RowHeight, terrain);

                int loading = terrain.Queued + terrain.InFlight;
                Row(g, y + 3 * RowHeight, "Queue", loading == 0
                    ? new[] { Part("idle", Good), Part("  " + terrain.Workers + " workers", Dim) }
                    : new[]
                    {
                        Part(terrain.Queued.ToString(), Info), Part(" queued  ", Dim),
                        Part(terrain.InFlight.ToString(), Info), Part(" in flight / " + terrain.Workers, Dim)
                    });
                Row(g, y + 4 * RowHeight, "Loaded",
                    Part(terrain.LoadedTotal.ToString(), Value),
                    Part("  " + terrain.LoadedPerSecond.ToString("0.0") + "/s", terrain.LoadedPerSecond > 0 ? Info : Dim),
                    Part("  fetch ", Dim), Part(terrain.FetchMs.ToString("0") + " ms", Value),
                    Part("  mesh ", Dim), Part(terrain.BuildMs.ToString("0.0") + " ms", Value));
                Row(g, y + 5 * RowHeight, "Health",
                    Part(terrain.Failed.ToString(), terrain.Failed > 0 ? Bad : Good), Part(" failed  ", Dim),
                    Part(terrain.NoData.ToString(), terrain.NoData > 0 ? Warn : Good), Part(" no data  ", Dim),
                    Part(terrain.Offline.ToString(), terrain.Offline > 0 ? Warn : Good), Part(" offline", Dim));
                Row(g, y + 6 * RowHeight, "Elevation", terrain.WaitingForElevation > 0
                    ? new[] { Part(terrain.WaitingForElevation.ToString(), Warn), Part(" tiles waiting for SRTM", Dim) }
                    : new[] { Part("SRTM ready", Good) });
                if (terrain.Failed > 0 && !string.IsNullOrEmpty(terrain.LastError))
                    Row(g, y + 7 * RowHeight, "Last error", Part(Truncate(terrain.LastError, 34), Bad));
            }

            y += (terrain.Failed > 0 && !string.IsNullOrEmpty(terrain.LastError) ? 8 : 7) * RowHeight + 6;

            // View
            y = Section(g, "VIEW", y, draw);
            if (draw)
            {
                Row(g, y, "Camera", Part(scene.Mode, Accent), Part(scene.Connected ? "  vehicle connected" : "  following 2D map", Dim));
                Row(g, y + RowHeight, "Look",
                    Part(scene.Heading.ToString("000") + "°", Value), Part(" hdg  ", Dim),
                    Part(scene.Pitch.ToString("+0;-0") + "°", Value), Part(" pitch  ", Dim),
                    Part(scene.FovDegrees.ToString("0") + "°", Value), Part(" fov", Dim));
                Row(g, y + 2 * RowHeight, "Range",
                    Part((scene.ViewRadius / 1000).ToString("0.0") + " km", Value),
                    Part("  fog ", Dim), Part((scene.FogStart / 1000).ToString("0") + "-" + (scene.FogEnd / 1000).ToString("0") + " km", Value),
                    Part("  max z", Dim), Part(scene.MaxZoom.ToString(), Value));
                Row(g, y + 3 * RowHeight, "Grid",
                    Part("UTM " + Math.Abs(scene.UtmZone) + (scene.UtmZone < 0 ? "S" : "N"), Value),
                    Part("  origin moved ", Dim), Part(scene.OriginMoves + "x", Value));
            }

            y += 4 * RowHeight + 6;

            // System
            y = Section(g, "SYSTEM", y, draw);
            if (draw)
            {
                Row(g, y, "GPU", Part(Truncate(scene.Renderer, 36), Value));
                Row(g, y + RowHeight, "OpenGL", Part(Truncate(scene.GLVersion, 36), Dim));
                Row(g, y + 2 * RowHeight, "Memory",
                    Part((GC.GetTotalMemory(false) / (1024 * 1024)).ToString("0") + " MB", Value), Part(" managed  GC ", Dim),
                    Part(GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2), Value));
            }

            y += 3 * RowHeight + 8;
            return (int)Math.Ceiling(y);
        }

        float Section(Graphics g, string title, float y, bool draw)
        {
            if (draw)
            {
                Text(g, title, _sectionFont, Accent, Pad, y);
                float width = g.MeasureString(title, _sectionFont).Width;
                using (var pen = new Pen(Color.FromArgb(60, Accent)))
                    g.DrawLine(pen, Pad + width + 3, y + 8, PanelWidth - Pad, y + 8);
            }

            return y + 18;
        }

        struct TextPart
        {
            public string Text;
            public Color Color;
        }

        static TextPart Part(string text, Color color)
        {
            return new TextPart { Text = text, Color = color };
        }

        void Row(Graphics g, float y, string label, params TextPart[] parts)
        {
            Text(g, label, _rowFont, Label, Pad, y);
            float x = ValueX;
            foreach (var part in parts)
            {
                Text(g, part.Text, _rowFont, part.Color, x, y);
                x += Width(g, part.Text, _rowFont);
            }
        }

        static void Text(Graphics g, string text, Font font, Color color, float x, float y)
        {
            using (var brush = new SolidBrush(color))
                g.DrawString(text, font, brush, x, y, Typographic);
        }

        static float Width(Graphics g, string text, Font font)
        {
            // GenericTypographic drops trailing spaces from measurements, so measure them separately.
            int trailing = text.Length - text.TrimEnd(' ').Length;
            float width = g.MeasureString(text, font, PointF.Empty, Typographic).Width;
            if (trailing > 0)
                width += trailing * g.MeasureString("x", font, PointF.Empty, Typographic).Width;
            return width;
        }

        /// <summary>The graph's backdrop, legend and fps labels; the bars themselves are drawn in GL.</summary>
        void GraphFrame(Graphics g, float y)
        {
            using (var fill = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                g.FillRectangle(fill, Pad, y, PanelWidth - 2 * Pad, GraphHeight);
            using (var pen = new Pen(Color.FromArgb(40, 255, 255, 255)))
                g.DrawRectangle(pen, Pad, y, PanelWidth - 2 * Pad, GraphHeight);

            foreach (var guide in new[] { Tuple.Create(1000f / 60, "60"), Tuple.Create(1000f / 30, "30") })
            {
                float gy = y + GraphHeight - guide.Item1 / GraphMaxMs * GraphHeight;
                Text(g, guide.Item2, _smallFont, Dim, PanelWidth - Pad - 17, gy - 12);
            }

            float ly = y + GraphHeight + 3;
            float lx = Pad;
            lx = LegendSwatch(g, lx, ly, Good, "<16.7 ms");
            lx = LegendSwatch(g, lx, ly, Warn, "<33 ms");
            lx = LegendSwatch(g, lx, ly, Bad, "slower");
            using (var pen = new Pen(Info, 1.5f))
                g.DrawLine(pen, lx, ly + 6, lx + 10, ly + 6);
            Text(g, "cpu", _smallFont, Dim, lx + 13, ly);
            string scale = "0-" + GraphMaxMs.ToString("0") + " ms";
            Text(g, scale, _smallFont, Dim, PanelWidth - Pad - Width(g, scale, _smallFont), ly);
        }

        float LegendSwatch(Graphics g, float x, float y, Color color, string text)
        {
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, x, y + 2, 8, 8);
            Text(g, text, _smallFont, Dim, x + 11, y);
            return x + 11 + Width(g, text, _smallFont) + 10;
        }

        /// <summary>Drawn tiles per zoom level as a tiny histogram, coarse (blue) to fine (green).</summary>
        void ZoomRow(Graphics g, float y, TerrainStats terrain)
        {
            Text(g, "Zooms", _rowFont, Label, Pad, y);
            const int first = TerrainQuadtree.RootZoom, last = TerrainStats.ZoomLevels - 1;
            int max = 1;
            for (int z = first; z <= last; z++)
                max = Math.Max(max, terrain.ZoomCounts[z]);

            float x = ValueX;
            for (int z = first; z <= last; z++)
            {
                float t = (float)(z - first) / (last - first);
                var color = Blend(Info, Good, t);
                float h = terrain.ZoomCounts[z] == 0 ? 1 : 2 + 9f * terrain.ZoomCounts[z] / max;
                using (var brush = new SolidBrush(terrain.ZoomCounts[z] == 0 ? Color.FromArgb(70, Dim) : color))
                    g.FillRectangle(brush, x, y + 12 - h, 8, h);
                x += 10;
            }

            if (terrain.Drawn > 0)
                Text(g, "z" + terrain.MinZoom + "-" + terrain.MaxZoom, _rowFont, Value, x + 4, y);
        }

        void MemoryRow(Graphics g, float y, TerrainStats terrain)
        {
            Text(g, "GPU mem", _rowFont, Label, Pad, y);
            double fraction = terrain.GpuBudgetBytes > 0 ? (double)terrain.GpuBytes / terrain.GpuBudgetBytes : 0;
            var color = fraction < 0.6 ? Good : fraction < 0.85 ? Warn : Bad;
            const float barWidth = 108;
            using (var back = new SolidBrush(Color.FromArgb(60, 255, 255, 255)))
                g.FillRectangle(back, ValueX, y + 4, barWidth, 7);
            using (var brush = new SolidBrush(color))
                g.FillRectangle(brush, ValueX, y + 4, (float)(barWidth * Math.Min(1, fraction)), 7);

            float x = ValueX + barWidth + 8;
            string used = (terrain.GpuBytes / (1024 * 1024)).ToString("0");
            Text(g, used, _rowFont, color, x, y);
            Text(g, " / " + terrain.GpuBudgetBytes / (1024 * 1024) + " MB", _rowFont, Dim, x + Width(g, used, _rowFont), y);
        }

        static Color FpsColor(double fps)
        {
            return fps >= 55 ? Good : fps >= 30 ? Warn : Bad;
        }

        static Color CpuColor(double ms)
        {
            return ms < 8 ? Good : ms < 16 ? Warn : Bad;
        }

        static Color Blend(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R + (b.R - a.R) * t), (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        static string Truncate(string text, int length)
        {
            text = text ?? "";
            return text.Length > length ? text.Substring(0, length - 1) + "…" : text;
        }

        static GraphicsPath RoundedRect(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            GLTextures.Delete(ref _texture);
            _quads.Dispose();
            _titleFont.Dispose();
            _sectionFont.Dispose();
            _rowFont.Dispose();
            _smallFont.Dispose();
        }
    }
}
