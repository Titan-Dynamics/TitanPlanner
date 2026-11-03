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

        readonly Dictionary<string, KeyValuePair<int, float>> _textures = new Dictionary<string, KeyValuePair<int, float>>();

        public int Get(string label)
        {
            float aspect;
            return Get(label, out aspect);
        }

        /// <summary>The label's texture, and its width over its height.</summary>
        public int Get(string label, out float aspect)
        {
            KeyValuePair<int, float> made;
            if (_textures.TryGetValue(label, out made))
            {
                aspect = made.Value;
                return made.Key;
            }

            using (var font = new Font("Arial", 48, FontStyle.Bold))
            using (var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            using (var path = new GraphicsPath())
            using (var outline = new Pen(Color.Black, 6) { LineJoin = LineJoin.Round })
            {
                // laid out about the origin first, to measure it
                path.AddString(label, font.FontFamily, (int)font.Style, font.Size, new PointF(0, 0), format);
                int width = Math.Max(Size, (int)Math.Ceiling(path.GetBounds().Width + outline.Width + 8));
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

                    aspect = (float)width / Size;
                    int texture = GLTextures.CreateFromImage(bitmap, true);
                    _textures[label] = new KeyValuePair<int, float>(texture, aspect);
                    return texture;
                }
            }
        }

        public void Dispose()
        {
            foreach (var made in _textures.Values)
            {
                int texture = made.Key;
                GLTextures.Delete(ref texture);
            }

            _textures.Clear();
        }
    }
}
