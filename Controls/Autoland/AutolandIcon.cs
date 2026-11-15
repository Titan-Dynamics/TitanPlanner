using System.Drawing;
using System.Drawing.Drawing2D;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// The PLAN map's autoland button, under the polygon and zoom buttons: a runway in perspective with an approach
    /// coming down onto it.
    /// </summary>
    public class AutolandIcon : MissionPlanner.Controls.Icon.Icon
    {
        internal override void doPaint(Graphics g)
        {
            float w = Width, h = Height;

            // the runway, narrowing away from the viewer, and its centerline
            var runway = new[]
            {
                new PointF(w * 0.40f, h * 0.80f), new PointF(w * 0.68f, h * 0.80f),
                new PointF(w * 0.60f, h * 0.42f), new PointF(w * 0.48f, h * 0.42f)
            };
            g.DrawPolygon(LinePen, runway);
            using (var dash = new Pen(LinePen.Color, LinePen.Width) {DashStyle = DashStyle.Dash})
                g.DrawLine(dash, w * 0.54f, h * 0.76f, w * 0.54f, h * 0.48f);

            // the approach, down from the upper left to the runway's near end
            g.DrawLine(LinePen, w * 0.16f, h * 0.22f, w * 0.40f, h * 0.62f);
            g.DrawLine(LinePen, w * 0.40f, h * 0.62f, w * 0.30f, h * 0.58f);
            g.DrawLine(LinePen, w * 0.40f, h * 0.62f, w * 0.40f, h * 0.51f);
        }
    }
}
