using GMap.NET;
using GMap.NET.WindowsForms;
using GMap.NET.WindowsForms.Markers;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Mono handles calls from other thread difrently - this prevents those crashs
    /// </summary>
    public class myGMAP : GMap.NET.WindowsForms.GMapControl
    {
        public bool inOnPaint = false;
        string otherthread = "";
        int lastx = 0;
        int lasty = 0;
        public myGMAP()
            : base()
        {
            this.Text = "Map";
            IgnoreMarkerOnMouseWheel = true;
            GestureHappened += MyGMAP_GestureHappened;
            _zoomTimer.Tick += ZoomTick;
        }

        GMapMarker p1 = new GMarkerGoogle(PointLatLng.Empty, GMarkerGoogleType.blue_small);
        GMapMarker p2 = new GMarkerGoogle(PointLatLng.Empty, GMarkerGoogleType.blue_small);

        private void MyGMAP_GestureHappened(object sender, GestureEventArgs e)
        {
            if(e.Operation == Gestures.Pan)
            {
                p1.Position = e.FirstPointLL;
                p2.Position = e.SecondPointLL;
                Overlays.First().Markers.Add(p1);
                Overlays.First().Markers.Add(p2);

                //Core.Position = e.FirstPointLL;

                PointLatLng point = FromLocalToLatLng(e.SecondPoint.X, e.SecondPoint.Y);

                double latdif = e.FirstPointLL.Lat - point.Lat;
                double lngdif = e.FirstPointLL.Lng - point.Lng;

                //Position = new PointLatLng(Position.Lat + latdif,
                  //  Position.Lng + lngdif);
            }
            if(e.Operation== Gestures.Zoom)
            {
                // aim is to get the ll listed under the users finger

                var startlocal = FromLatLngToLocal(e.FirstPointLL);

                p1.Position = e.FirstPointLL;
                p2.Position = e.SecondPointLL;
                Overlays.First().Markers.Add(p1);
                Overlays.First().Markers.Add(p2);


                var scale = e.DistanceBetweenNow / (float)e.DistanceBetweenStart;

                // use the start center as our center;
                //Position = FromLocalToLatLng((int)startlocal.X, (int)startlocal.Y);
                Core.Position = e.FirstPointLL;
                //Core.mouseLastZoom.X = e.SecondPoint.X;
                //Core.mouseLastZoom.Y = e.SecondPoint.Y;

                Core.MouseWheelZooming = true;
                Zoom += (scale - 1);
                Core.MouseWheelZooming = false;

                iArguments = e.DistanceBetweenNow;

                Console.WriteLine("zoom " + e.FirstPoint + " " + e.SecondPoint + " ") ;
            }
        }

        protected override void OnPaint(System.Windows.Forms.PaintEventArgs e)
        {
            var start = DateTime.Now;

            if (inOnPaint)
            {
                Console.WriteLine("Was in onpaint Gmap th:" + System.Threading.Thread.CurrentThread.Name + " in " + otherthread);
                return;
            }

            otherthread = System.Threading.Thread.CurrentThread.Name;

            inOnPaint = true;

            try
            {
                base.OnPaint(e);
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }

            inOnPaint = false;

            var end = DateTime.Now;

            System.Diagnostics.Debug.WriteLine("map draw time " + (end-start).TotalMilliseconds);
        }

        protected override void OnInvalidated(InvalidateEventArgs e)
        {
            base.OnInvalidated(e);
        }

        public new void Invalidate()
        {
            base.Invalidate();
        }

        // Smooth mouse wheel zoom - 0.2 per scroll tick
        private const double ZoomIncrement = 0.2;

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (!Core.IsDragging)
            {
                // standard delta is 120, so normalize; ticks during the animation add to where it is heading
                AnimateZoom(ZoomTarget + (e.Delta / 120.0) * ZoomIncrement);
            }
        }

        // Zoom animation: the zoom eases to its level over a few frames instead of jumping there
        private const double ZoomEaseSeconds = 0.06;
        private readonly Timer _zoomTimer = new Timer { Interval = 15 };
        private readonly Stopwatch _zoomClock = new Stopwatch();
        private double _zoomTarget;
        private double _zoomSet;

        /// <summary>
        /// The zoom level the map is heading to, or the current one when it isn't animating.
        /// </summary>
        public double ZoomTarget => _zoomTimer.Enabled ? _zoomTarget : Zoom;

        /// <summary>
        /// Eases the zoom to the level about the map centre. A call during the animation changes where it heads.
        /// </summary>
        public void AnimateZoom(double zoom)
        {
            zoom = Math.Max(MinZoom, Math.Min(MaxZoom, zoom));
            if (!_zoomTimer.Enabled)
            {
                if (zoom == Zoom)
                    return;
                if (!Visible || !IsHandleCreated)
                {
                    Zoom = zoom;
                    return;
                }

                _zoomSet = Zoom;
                _zoomClock.Restart();
                _zoomTimer.Start();
            }

            _zoomTarget = zoom;
        }

        /// <summary>
        /// Zooms to the level, easing there when animate is set. A level the zoom is already heading to is left
        /// alone, so the zoom controls writing the target back don't cut the animation short.
        /// </summary>
        public void SetZoom(double zoom, bool animate)
        {
            if (Math.Abs(zoom - ZoomTarget) < 1e-6)
                return;

            if (animate)
            {
                AnimateZoom(zoom);
            }
            else
            {
                _zoomTimer.Stop();
                CentreZoomPivot();
                Zoom = zoom;
            }
        }

        // GMap draws a fractional zoom scaled (and rotated) about Core.renderOffset, which is the view centre only
        // after a whole-level zoom change or a Position set. A pan moves it along with the map, so a zoom after a pan
        // would grow from where the centre used to be, and Core works out Position without the fractional scale.

        /// <summary>
        /// The scale a fractional zoom draws the whole level it is between at (1 at whole levels).
        /// </summary>
        public double FractionalScale => ScaleMode == ScaleModes.Fractional ? Math.Pow(2, Zoom - Core.Zoom) : 1;

        /// <summary>
        /// A lat/lng in local pixels of the map before rotation (the frame Core.renderOffset and Pan work in), with
        /// the fractional zoom applied, which Core.FromLatLngToLocal leaves out.
        /// </summary>
        public PointF FromLatLngToMapLocal(PointLatLng point)
        {
            var local = Core.FromLatLngToLocal(point);
            var origin = Core.renderOffset;
            double scale = FractionalScale;
            return new PointF((float)(origin.X + (local.X - origin.X) * scale),
                (float)(origin.Y + (local.Y - origin.Y) * scale));
        }

        private double _panCarryX, _panCarryY;

        /// <summary>
        /// Moves the map by the offset in pixels (before rotation) without re-projecting the overlays, keeping
        /// Position on the view centre.
        /// </summary>
        public void Pan(GPoint offset)
        {
            if (IsRotated)
            {
                // GMap turns the tiles about Core.renderOffset but the overlays about the view centre, so on a rotated
                // map the two only agree while that is the centre: move the centre itself, which re-centres the map
                // and re-projects the overlays. The carry keeps the fractions of a whole level's pixel.
                double scale = FractionalScale;
                _panCarryX += offset.X / scale;
                _panCarryY += offset.Y / scale;
                long stepX = (long)Math.Truncate(_panCarryX), stepY = (long)Math.Truncate(_panCarryY);
                if (stepX == 0 && stepY == 0)
                    return;

                _panCarryX -= stepX;
                _panCarryY -= stepY;
                var centre = CentrePixel();
                var target = new GPoint(centre.X - stepX, centre.Y - stepY);
                Position = MapProvider.Projection.FromPixelToLatLng(target, Core.Zoom);
                // the lat/lng round trip can land a pixel short: keep that for the next step
                _panCarryX += Core.positionPixel.X - target.X;
                _panCarryY += Core.positionPixel.Y - target.Y;
                return;
            }

            Core.DragOffset(offset);

            var pixel = CentrePixel();
            Core.positionPixel = pixel;
            Core.position = MapProvider.Projection.FromPixelToLatLng(pixel, Core.Zoom);
        }

        // the world pixel, at the whole level the map is drawn from, under the view centre
        private GPoint CentrePixel()
        {
            var origin = Core.renderOffset;
            double scale = FractionalScale;
            return new GPoint(
                Core.compensationOffset.X + (long)Math.Round((Core.Width / 2 - origin.X) / scale),
                Core.compensationOffset.Y + (long)Math.Round((Core.Height / 2 - origin.Y) / scale));
        }

        /// <summary>
        /// Moves the point a zoom grows from back to the view centre. The overlays need re-projecting after, which
        /// the zoom change that follows does.
        /// </summary>
        private void CentreZoomPivot()
        {
            // Before the map starts, Position is only stored (a restored last position, say) and the offsets aren't
            // laid out yet; starting centres the map on Position.
            if (!Core.IsStarted || IsDragging)
                return;

            var centre = new GPoint(Core.Width / 2, Core.Height / 2);
            if (Core.renderOffset.X == centre.X && Core.renderOffset.Y == centre.Y)
                return;

            Core.compensationOffset = CentrePixel();
            Core.renderOffset = centre;
            // refreshes the tiles to draw and sets Position to the new centre
            Core.DragOffset(GPoint.Empty);
        }

        private void ZoomTick(object sender, EventArgs e)
        {
            // Something else set the zoom (a zoom to fit, a restored level): leave it there. A zoom slider writing
            // the level back through its own rounding lands within the tolerance and doesn't count.
            if (Math.Abs(Zoom - _zoomSet) > 0.01)
            {
                _zoomTimer.Stop();
                return;
            }

            double dt = Math.Min(_zoomClock.Elapsed.TotalSeconds, 0.1);
            _zoomClock.Restart();

            // the zoom setter ignores changes mid-drag
            if (IsDragging)
                return;

            // cover the same share of the gap per second whatever the frame rate
            double gap = _zoomTarget - Zoom;
            double zoom = Math.Abs(gap) < 0.002 ? _zoomTarget : Zoom + gap * (1 - Math.Exp(-dt / ZoomEaseSeconds));

            try
            {
                if (zoom != Zoom)
                {
                    CentreZoomPivot();
                    Zoom = zoom;
                }
                else
                {
                    // the last frame set the zoom but its re-projection was cut short: re-project without moving
                    CentreZoomPivot();
                    Offset(0, 0);
                }

                if (zoom == _zoomTarget)
                    _zoomTimer.Stop();
            }
            catch (Exception ex) when (ex is NullReferenceException || ex is InvalidOperationException ||
                                       ex is ArgumentException || ex is IndexOutOfRangeException)
            {
                // The DATA screen's update loop changes overlays on its own thread, and one changed while they were
                // being re-projected. The zoom is set; the next frame re-projects them again.
                Debug.WriteLine(ex);
            }
            catch
            {
                // anything else would come back every frame
                _zoomTimer.Stop();
                throw;
            }

            _zoomSet = Zoom;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _zoomTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnMouseMove(System.Windows.Forms.MouseEventArgs e)
        {
            try
            {
                var buffer = 1;
                // try prevent alot of cpu usage
                if (e.X >= lastx - buffer && e.X <= lastx + buffer && e.Y >= lasty - buffer && e.Y <= lasty + buffer)
                    return;

                // While the DATA screen's update loop holds invalidation, hover is skipped, but a drag still moves the
                // map so it never freezes mid-drag; the repaint follows when the hold ends.
                if (HoldInvalidation && e.Button == System.Windows.Forms.MouseButtons.None)
                    return;

                lastx = e.X;
                lasty = e.Y;

                base.OnMouseMove(e);
            }
            catch (Exception ex) { Console.WriteLine(ex.ToString()); }
        }










        private Point first_point = new Point();
        private Point second_point = new Point();
        PointLatLng first_pointLL = new PointLatLng();
        PointLatLng second_pointLL = new PointLatLng();
        private Int64 iArguments = 0;
        private const Int64 ULL_ARGUMENTS_BIT_MASK = 0xFFFFFFFFL;
        private const int WM_GESTURENOTIFY = 0x11A;
        private const int WM_GESTURE = 0x119;
        private const int GC_ALLGESTURES = 0x1;
        private const int GID_BEGIN = 1;
        private const int GID_END = 2;
        private const int GID_ZOOM = 3;
        private const int GID_PAN = 4;
        private const int GID_ROTATE = 5;
        private const int GID_TWOFINGERTAP = 6;
        private const int GID_PRESSANDTAP = 7;
        private const int GF_BEGIN = 0x1;
        private const int GF_INERTIA = 0x2;
        private const int GF_END = 0x4;
        private struct GESTURECONFIG
        {
            public int dwID;
            public int dwWant;
            public int dwBlock;
        }
        public struct POINTS
        {
            public short x;
            public short y;
        }
        private struct GESTUREINFO
        {
            public int cbSize;
            public int dwFlags;
            public int dwID;
            public IntPtr hwndTarget;
            [MarshalAs(UnmanagedType.Struct)]
            public POINTS ptsLocation;
            public int dwInstanceID;
            public int dwSequenceID;
            public Int64 ullArguments;
            public int cbExtraArgs;
        }
        [DllImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetGestureConfig(IntPtr hWnd, int dwReserved, int cIDs, ref GESTURECONFIG pGestureConfig, int cbSize);

        [DllImport("user32")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetGestureInfo(IntPtr hGestureInfo, ref GESTUREINFO pGestureInfo);


        private int _gestureConfigSize;
        private int _gestureInfoSize;
        [SecurityPermission(SecurityAction.Demand)]
        private void SetupStructSizes()
        {
            _gestureConfigSize = Marshal.SizeOf(new GESTURECONFIG());
            _gestureInfoSize = Marshal.SizeOf(new GESTUREINFO());
        }
        [PermissionSet(SecurityAction.Demand, Name = "FullTrust")]
        protected override void WndProc(ref Message m)
        {
            if (_gestureConfigSize == 0)
                SetupStructSizes();

            bool handled;
            switch (m.Msg)
            {
                case WM_GESTURENOTIFY:
                    {
                        GESTURECONFIG gc = new GESTURECONFIG();
                        gc.dwID = GID_ZOOM;
                        gc.dwWant = GC_ALLGESTURES;
                        gc.dwBlock = 0;
                        bool bResult = SetGestureConfig(Handle, 0, 1, ref gc, _gestureConfigSize);
                        if (!bResult)
                            throw new Exception("Error in execution of SetGestureConfig");
                        handled = true;
                        break;
                    }

                case WM_GESTURE:
                    {
                        handled = DecodeGesture(ref m);
                        break;
                    }

                default:
                    {
                        handled = false;
                        break;
                    }
            }
            base.WndProc(ref m);
            if (handled)
            {
                try
                {
                    m.Result = new IntPtr(1);
                }
                catch (Exception excep)
                {
                    Debug.Print("Could not allocate result ptr");
                    Debug.Print(excep.ToString());
                }
            }
        }
        private bool DecodeGesture(ref Message m)
        {
            GESTUREINFO gi;
            try
            {
                gi = new GESTUREINFO();
            }
            catch (Exception excep)
            {
                Debug.Print("Could not allocate resources to decode gesture");
                Debug.Print(excep.ToString());
                return false;
            }
            gi.cbSize = _gestureInfoSize;
            if (!GetGestureInfo(m.LParam, ref gi))
                return false;
            switch (gi.dwID)
            {
                case GID_BEGIN:
                case GID_END:
                    {
                        break;
                    }

                case GID_TWOFINGERTAP:
                    {
                        break;
                    }

                case GID_ZOOM:
                    {
                        switch (gi.dwFlags)
                        {
                            case GF_BEGIN:
                                {
                                    iArguments = System.Convert.ToInt64(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK);
                                    first_point.X = gi.ptsLocation.x;
                                    first_point.Y = gi.ptsLocation.y;
                                    first_pointLL = FromLocalToLatLng(PointToClient(first_point));
                                    break;
                                }

                            default:
                                {
                                    second_point.X = gi.ptsLocation.x;
                                    second_point.Y = gi.ptsLocation.y;
                                    second_pointLL = FromLocalToLatLng(PointToClient(second_point));
                                    GestureHappened?.Invoke(this, new GestureEventArgs() { Operation = Gestures.Zoom, DistanceBetweenStart = (int)iArguments, DistanceBetweenNow = System.Convert.ToInt32(gi.ullArguments & ULL_ARGUMENTS_BIT_MASK), FirstPoint = first_point, SecondPoint = second_point, FirstPointLL = first_pointLL, SecondPointLL = second_pointLL });
                                    break;
                                }
                        }

                        break;
                    }

                case GID_PAN:
                    {
                        switch (gi.dwFlags)
                        {
                            case GF_BEGIN:
                                {
                                    first_point.X = gi.ptsLocation.x;
                                    first_point.Y = gi.ptsLocation.y;
                                    first_pointLL = FromLocalToLatLng(PointToClient(first_point));
                                    break;
                                }

                            default:
                                {
                                    second_point.X = gi.ptsLocation.x;
                                    second_point.Y = gi.ptsLocation.y;
                                    second_pointLL = FromLocalToLatLng(PointToClient(second_point));
                                    GestureHappened?.Invoke(this, new GestureEventArgs() { Operation = Gestures.Pan, FirstPoint = first_point, SecondPoint = second_point, FirstPointLL = first_pointLL, SecondPointLL = second_pointLL });
                                    break;
                                }
                        }

                        break;
                    }

                case GID_PRESSANDTAP:
                    {
                        break;
                    }

                case GID_ROTATE:
                    {
                        break;
                    }
            }
            return true;
        }


        public enum Gestures
        {
            Pan,
            Zoom
        }

        public class GestureEventArgs : EventArgs
        {
            public Gestures Operation { get; set; }
            public Point FirstPoint { get; set; }
            public Point SecondPoint { get; set; }
            public PointLatLng FirstPointLL { get; set; }
            public PointLatLng SecondPointLL { get; set; }
            public int DistanceBetweenStart { get; set; }
            public int DistanceBetweenNow { get; set; }
        }

        public event GestureHappenedEventHandler GestureHappened;

        public delegate void GestureHappenedEventHandler(object sender, GestureEventArgs e);
    }
}
