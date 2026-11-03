using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Maps
{
    [Serializable]
    public class GMapMarkerCustom : GMapMarkerBase
    {
        float heading = 0;
        float cog = -1;
        float target = -1;
        float nav_bearing = -1;

        public float Heading { get => heading; set => heading = value; }
        public float Cog { get => cog; set => cog = value; }
        public float Target { get => target; set => target = value; }
        public float Nav_bearing { get => nav_bearing; set => nav_bearing = value; }

        public GMapMarkerCustom(PointLatLng p, float heading, float cog, float nav_bearing, float target)
            : base(p)
        {
            this.heading = heading;
            this.cog = cog;
            this.nav_bearing = nav_bearing;
            this.target = target;
            if (CustomIcon != null)
            {
                Size = CustomIcon.Size;
            }
        }

        public override void OnRender(IGraphics g)
        {
            if (IsHidden || CustomIcon == null)
            {
                return;
            }

            var temp = g.Transform;
            g.TranslateTransform(LocalPosition.X, LocalPosition.Y);

            g.RotateTransform(-Overlay.Control.Bearing);

            try
            {
                if (DisplayHeading)
                    g.DrawLine(new Pen(Color.Red, 2), 0.0f, 0.0f,
                        (float)Math.Cos((heading - 90) * MathHelper.deg2rad) * length,
                        (float)Math.Sin((heading - 90) * MathHelper.deg2rad) * length);
            }
            catch { }

            if (DisplayNavBearing)
                g.DrawLine(new Pen(Color.Green, 2), 0.0f, 0.0f,
                    (float)Math.Cos((nav_bearing - 90) * MathHelper.deg2rad) * length,
                    (float)Math.Sin((nav_bearing - 90) * MathHelper.deg2rad) * length);
            if (DisplayCOG)
                g.DrawLine(new Pen(Color.Black, 2), 0.0f, 0.0f,
                    (float)Math.Cos((cog - 90) * MathHelper.deg2rad) * length,
                    (float)Math.Sin((cog - 90) * MathHelper.deg2rad) * length);
            if (DisplayTarget)
                g.DrawLine(new Pen(Color.Orange, 2), 0.0f, 0.0f,
                    (float)Math.Cos((target - 90) * MathHelper.deg2rad) * length,
                    (float)Math.Sin((target - 90) * MathHelper.deg2rad) * length);

            try
            {
                g.RotateTransform(heading);
            }
            catch { }

            var icon = CustomIcon;
            g.TranslateTransform(-icon.Width / 2, -icon.Height / 2);

            if (IsTransparent)
            {
                var colorMatrix = new System.Drawing.Imaging.ColorMatrix();
                colorMatrix.Matrix33 = InactiveOpacity;
                var imageAttributes = new System.Drawing.Imaging.ImageAttributes();
                imageAttributes.SetColorMatrix(colorMatrix);
                g.DrawImage(icon, new Rectangle(0, 0, icon.Width, icon.Height),
                    0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, imageAttributes);
            }
            else
            {
                g.DrawImage(icon, 0, 0, icon.Width, icon.Height);
            }

            g.Transform = temp;
        }
    }
}
