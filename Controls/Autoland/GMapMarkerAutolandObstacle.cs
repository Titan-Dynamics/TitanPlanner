using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// The obstacle height an autoland sequence's base leg clears, at the middle of the base leg: a tag with the
    /// charted obstacle symbol (an inverted V under a light) and the height, on a short stem out from the leg on the
    /// side away from the circuit, so the waypoint insert marker at the same point stays visible and clickable. It
    /// takes no clicks.
    /// </summary>
    public class GMapMarkerAutolandObstacle : GMapMarker
    {
        const int Stem = 10;
        const int BoxHeight = 22;
        const int Symbol = 14;

        static readonly Font TextFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        static readonly Color Amber = Color.FromArgb(255, 176, 32);

        /// <summary>The height, with its unit.</summary>
        public string Text;

        /// <summary>A point on the side the tag goes.</summary>
        public PointLatLng Outside;

        public GMapMarkerAutolandObstacle(PointLatLng position, PointLatLng outside, string text)
            : base(position)
        {
            Text = text;
            Outside = outside;
            IsHitTestVisible = false;
            Size = new Size(2, 2);
            Offset = new Point(-1, -1);
        }

        public override void OnRender(IGraphics g)
        {
            // the leg's middle, where the stem ends
            int x = LocalPosition.X - Offset.X;
            int y = LocalPosition.Y - Offset.Y;

            var direction = AutolandTag.Direction(this, Outside);
            var textSize = g.MeasureString(Text, TextFont, new SizeF(200, BoxHeight));
            float width = 6 + Symbol + 4 + textSize.Width + 6;
            var box = AutolandTag.Place(x, y, direction, width, BoxHeight, Stem);

            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var stem = new Pen(Amber, 2))
                g.DrawLine(stem, x + direction.X * 2, y + direction.Y * 2, x + direction.X * Stem,
                    y + direction.Y * Stem);

            using (var path = MapButton.RoundedRect(box, 5))
            {
                using (var fill = new SolidBrush(Color.FromArgb(215, 18, 20, 24)))
                    g.FillPath(fill, path);
                using (var border = new Pen(Amber, 1.5f))
                    g.DrawPath(border, path);
            }

            // the obstacle symbol: an inverted V with a light above it
            float sx = box.X + 6, sy = box.Y + (BoxHeight - Symbol) / 2f;
            using (var pen = new Pen(Amber, 2) {LineJoin = LineJoin.Round})
                g.DrawLines(pen, new[]
                {
                    new PointF(sx, sy + Symbol), new PointF(sx + Symbol / 2f, sy + Symbol * 0.3f),
                    new PointF(sx + Symbol, sy + Symbol)
                });
            using (var light = new SolidBrush(Color.FromArgb(255, 70, 60)))
                g.FillEllipse(light, sx + Symbol / 2f - 2.5f, sy - 1, 5, 5);

            g.DrawString(Text, TextFont, Brushes.White, box.X + 6 + Symbol + 4,
                box.Y + (BoxHeight - textSize.Height) / 2);

            g.SmoothingMode = smoothing;
        }
    }
}
