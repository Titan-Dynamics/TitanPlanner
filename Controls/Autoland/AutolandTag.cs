using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// Where the autoland markers put their tags: on the side of their point away from the circuit, so the tags stay
    /// clear of the legs, the waypoints and each other whatever the landing heading.
    /// </summary>
    internal static class AutolandTag
    {
        /// <summary>The screen direction (a unit vector) from the marker's point towards <paramref name="outside"/>.
        /// </summary>
        public static PointF Direction(GMapMarker marker, PointLatLng outside)
        {
            var control = marker.Overlay?.Control;
            if (control == null)
                return new PointF(0, -1);

            var from = control.FromLatLngToLocal(marker.Position);
            var to = control.FromLatLngToLocal(outside);
            double dx = to.X - from.X, dy = to.Y - from.Y;
            double length = Math.Sqrt(dx * dx + dy * dy);
            return length < 1e-6 ? new PointF(0, -1) : new PointF((float) (dx / length), (float) (dy / length));
        }

        /// <summary>A box of this size whose near side is <paramref name="gap"/> pixels from (x, y) along
        /// <paramref name="direction"/>.</summary>
        public static RectangleF Place(float x, float y, PointF direction, float width, float height, float gap)
        {
            float reach = gap + Math.Abs(direction.X) * width / 2 + Math.Abs(direction.Y) * height / 2;
            float cx = x + direction.X * reach, cy = y + direction.Y * reach;
            return new RectangleF(cx - width / 2, cy - height / 2, width, height);
        }
    }
}
