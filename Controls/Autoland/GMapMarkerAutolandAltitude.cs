using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// A point on an autoland sequence's pre-approach leg with the altitude the descent passes it at: a small dot on
    /// the leg and a tag beside it, on the side away from the circuit. Amber while the pre-approach is steep. It takes
    /// no clicks, and is only drawn on the PLAN map.
    /// </summary>
    public class GMapMarkerAutolandAltitude : GMapMarker
    {
        const int Dot = 8;
        const int Gap = 7;
        const int BoxHeight = 18;

        static readonly Font TextFont = new Font("Segoe UI", 8f, FontStyle.Bold);
        static readonly Color Amber = Color.FromArgb(255, 176, 32);

        public string Text;
        public bool Steep;

        /// <summary>A point on the side the tag goes.</summary>
        public PointLatLng Outside;

        public GMapMarkerAutolandAltitude(PointLatLng position, PointLatLng outside, string text, bool steep)
            : base(position)
        {
            Text = text;
            Outside = outside;
            Steep = steep;
            IsHitTestVisible = false;
            Size = new Size(Dot, Dot);
            Offset = new Point(-Dot / 2, -Dot / 2);
        }

        public override void OnRender(IGraphics g)
        {
            int x = LocalPosition.X - Offset.X;
            int y = LocalPosition.Y - Offset.Y;

            var smoothing = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color accent = Steep ? Amber : Color.White;

            using (var fill = new SolidBrush(accent))
                g.FillEllipse(fill, x - Dot / 2f, y - Dot / 2f, Dot, Dot);
            using (var ring = new Pen(Color.FromArgb(200, 18, 20, 24), 1.5f))
                g.DrawEllipse(ring, x - Dot / 2f, y - Dot / 2f, Dot, Dot);

            var textSize = g.MeasureString(Text, TextFont, new SizeF(200, BoxHeight));
            var box = AutolandTag.Place(x, y, AutolandTag.Direction(this, Outside), textSize.Width + 10, BoxHeight,
                Dot / 2f + Gap);
            using (var path = MapButton.RoundedRect(box, 4))
            {
                using (var back = new SolidBrush(Color.FromArgb(200, 18, 20, 24)))
                    g.FillPath(back, path);
                using (var border = new Pen(Steep ? Amber : Color.FromArgb(150, 255, 255, 255), Steep ? 1.5f : 1f))
                    g.DrawPath(border, path);
            }

            using (var text = new SolidBrush(accent))
                g.DrawString(Text, TextFont, text, box.X + 5, box.Y + (BoxHeight - textSize.Height) / 2);

            g.SmoothingMode = smoothing;
        }
    }
}
