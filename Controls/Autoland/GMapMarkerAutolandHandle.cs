using System;
using System.Drawing;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.Autoland
{
    /// <summary>
    /// A handle on a fixed wing autoland sequence: a small disc the PLAN map drags to reshape it.
    /// </summary>
    public class GMapMarkerAutolandHandle : GMapMarker
    {
        const int Diameter = 14;

        /// <summary>The index of the sequence's DO_LAND_START row.</summary>
        public readonly int SequenceStart;

        public readonly AutolandHandle Handle;

        public GMapMarkerAutolandHandle(PointLatLng position, int sequenceStart, AutolandHandle handle)
            : base(position)
        {
            SequenceStart = sequenceStart;
            Handle = handle;
            // a little larger than the disc, to be easy to grab
            Size = new Size(Diameter + 8, Diameter + 8);
            Offset = new Point(-Size.Width / 2, -Size.Height / 2);
        }

        public override void OnRender(IGraphics g)
        {
            var disc = new Rectangle(LocalPosition.X + (Size.Width - Diameter) / 2,
                LocalPosition.Y + (Size.Height - Diameter) / 2, Diameter, Diameter);

            Color fill = Handle == AutolandHandle.Offset ? MapButton.Accent : Color.FromArgb(0, 170, 0);
            using (var brush = new SolidBrush(IsMouseOver ? Color.White : fill))
                g.FillEllipse(brush, disc);
            using (var pen = new Pen(IsMouseOver ? fill : Color.White, 2))
                g.DrawEllipse(pen, disc);
        }
    }
}
