using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// A translucent round gear button in the top-left corner of the 3D view. Drawn in GL so it blends over
    /// the scene (a WinForms button can't be transparent over a GL surface). Hit testing is pure maths, so it
    /// works before the first frame.
    /// </summary>
    public sealed class SettingsButton : IDisposable
    {
        public const int Size = 30;
        const int Margin = 8;

        /// <summary>Textures are rendered at twice the on-screen size so the gear stays crisp.</summary>
        const int TextureSize = Size * 2;

        readonly QuadRenderer _quad = new QuadRenderer();
        int _normal;
        int _hover;
        Color _hoverColor;

        public bool Hovered { get; private set; }

        public bool HitTest(int x, int y)
        {
            float center = Margin + Size / 2f;
            float dx = x - center, dy = y - center;
            return dx * dx + dy * dy <= (Size / 2f + 2) * (Size / 2f + 2);
        }

        /// <summary>Returns true if the hover state changed.</summary>
        public bool SetHovered(bool hovered)
        {
            if (hovered == Hovered)
                return false;
            Hovered = hovered;
            return true;
        }

        public void Draw(int viewportWidth, int viewportHeight)
        {
            if (viewportWidth <= 0 || viewportHeight <= 0)
                return;

            if (_normal == 0)
                _normal = CreateTexture(false, Color.Empty);

            // The hover colour follows the theme, which can change while the app runs.
            var accent = ThemeColors.Accent;
            if (_hover == 0 || accent != _hoverColor)
            {
                GLTextures.Delete(ref _hover);
                _hover = CreateTexture(true, accent);
                _hoverColor = accent;
            }

            var projection = Matrix4.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, -1, 1);
            var identity = Matrix4.Identity;

            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            _quad.Begin();
            _quad.Add(Hovered ? _hover : _normal,
                new Vector3d(Margin, Margin, 0), new Vector3d(Margin + Size, Margin, 0),
                new Vector3d(Margin, Margin + Size, 0), new Vector3d(Margin + Size, Margin + Size, 0));
            _quad.Draw(ref projection, ref identity);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        static int CreateTexture(bool hover, Color accent)
        {
            using (var bitmap = new Bitmap(TextureSize, TextureSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.Clear(Color.Transparent);
                    g.SmoothingMode = SmoothingMode.AntiAlias;

                    var disc = new RectangleF(1, 1, TextureSize - 2, TextureSize - 2);
                    var gearColor = MapButton.DrawDisc(g, disc, hover, accent, 2f);
                    using (var gear = Gear(TextureSize / 2f, TextureSize / 2f, TextureSize * 0.30f, TextureSize * 0.235f,
                        TextureSize * 0.10f, 8))
                    using (var brush = new SolidBrush(gearColor))
                    using (var soften = new Pen(gearColor, 1.5f) { LineJoin = LineJoin.Round })
                    {
                        g.FillPath(brush, gear);
                        g.DrawPath(soften, gear);
                    }
                }

                return GLTextures.CreateFromImage(bitmap, false);
            }
        }

        /// <summary>A gear outline with a hole in the middle (filled with the alternate rule).</summary>
        static GraphicsPath Gear(float cx, float cy, float outer, float root, float hole, int teeth)
        {
            var points = new PointF[teeth * 5];
            double step = 2 * Math.PI / teeth;
            int o = 0;
            for (int i = 0; i < teeth; i++)
            {
                double a = i * step - Math.PI / 2;
                points[o++] = Polar(cx, cy, root, a - step * 0.27);
                points[o++] = Polar(cx, cy, outer, a - step * 0.16);
                points[o++] = Polar(cx, cy, outer, a + step * 0.16);
                points[o++] = Polar(cx, cy, root, a + step * 0.27);
                points[o++] = Polar(cx, cy, root, a + step * 0.5);
            }

            var path = new GraphicsPath(FillMode.Alternate);
            path.AddPolygon(points);
            path.AddEllipse(cx - hole, cy - hole, hole * 2, hole * 2);
            return path;
        }

        static PointF Polar(float cx, float cy, float radius, double angle)
        {
            return new PointF(cx + radius * (float)Math.Cos(angle), cy + radius * (float)Math.Sin(angle));
        }

        public void Dispose()
        {
            GLTextures.Delete(ref _normal);
            GLTextures.Delete(ref _hover);
            _quad.Dispose();
        }
    }
}
