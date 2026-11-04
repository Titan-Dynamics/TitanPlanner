using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// White text with a black outline, centred in a texture 128 px high and square unless the text needs it wider, as
    /// the 3D map's markers are labelled. Each text is drawn once and kept.
    /// </summary>
    public sealed class LabelTextures : IDisposable
    {
        const int Size = 128;

        struct Label
        {
            public int Texture;
            public float Aspect, Bottom;
        }

        readonly Dictionary<string, Label> _textures = new Dictionary<string, Label>();

        public int Get(string label)
        {
            float aspect, bottom;
            return Get(label, out aspect, out bottom);
        }

        /// <summary>
        /// The label's texture, its width over its height, and how far below the texture's middle the text's lowest
        /// point is, as a share of half its height.
        /// </summary>
        public int Get(string label, out float aspect, out float bottom)
        {
            Label made;
            if (!_textures.TryGetValue(label, out made))
            {
                made = Make(label);
                _textures[label] = made;
            }

            aspect = made.Aspect;
            bottom = made.Bottom;
            return made.Texture;
        }

        static Label Make(string label)
        {
            using (var font = new Font("Arial", 48, FontStyle.Bold))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var path = new GraphicsPath())
            using (var outline = new Pen(Color.Black, 6) { LineJoin = LineJoin.Round })
            {
                // laid out about the origin first, to measure it
                path.AddString(label, font.FontFamily, (int)font.Style, font.Size, new PointF(0, 0), format);
                var bounds = path.GetBounds();
                int width = Math.Max(Size, (int)Math.Ceiling(bounds.Width + outline.Width + 8));
                using (var matrix = new Matrix())
                {
                    matrix.Translate(width / 2f, Size / 2f);
                    path.Transform(matrix);
                }

                using (var bitmap = new Bitmap(width, Size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
                {
                    using (var g = Graphics.FromImage(bitmap))
                    {
                        g.Clear(Color.Transparent);
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.TextRenderingHint = TextRenderingHint.AntiAlias;
                        g.DrawPath(outline, path);
                        g.FillPath(Brushes.White, path);
                    }

                    return new Label
                    {
                        Texture = GLTextures.CreateFromImage(bitmap, true),
                        Aspect = (float)width / Size,
                        // the outline reaches half its width past the glyphs
                        Bottom = (bounds.Bottom + outline.Width / 2) / (Size / 2f)
                    };
                }
            }
        }

        public void Dispose()
        {
            foreach (var made in _textures.Values)
            {
                int texture = made.Texture;
                GLTextures.Delete(ref texture);
            }

            _textures.Clear();
        }
    }
}
