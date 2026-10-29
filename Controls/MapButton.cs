using System.Drawing;
using System.Drawing.Drawing2D;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The round, translucent buttons the maps draw on themselves (the 3D map's settings gear, the 2D map's legend):
    /// a dark disc with a faint ring, darker with an accent ring while hovered.
    /// </summary>
    public static class MapButton
    {
        static readonly Color Fallback = Color.FromArgb(255, 145, 27);

        /// <summary>The theme's button colour, as used by the Settings dialog's buttons.</summary>
        public static Color Accent
        {
            get
            {
                var color = MissionPlanner.Utilities.ThemeManager.ButBG;
                return color.A > 0 ? Color.FromArgb(255, color) : Fallback;
            }
        }

        /// <summary>Draws the disc and ring; returns the colour to draw the icon on it in.</summary>
        public static Color DrawDisc(Graphics g, RectangleF disc, bool hover, Color accent, float ringWidth)
        {
            using (var fill = new SolidBrush(hover ? Color.FromArgb(205, 18, 20, 24) : Color.FromArgb(140, 18, 20, 24)))
                g.FillEllipse(fill, disc);
            using (var ring = new Pen(hover ? Color.FromArgb(210, accent) : Color.FromArgb(55, 255, 255, 255), ringWidth))
                g.DrawEllipse(ring, disc);

            return hover ? accent : Color.FromArgb(225, 236, 240, 245);
        }

        /// <summary>A rectangle with round corners, for the panels drawn over the maps.</summary>
        public static GraphicsPath RoundedRect(RectangleF r, float radius)
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
    }
}
