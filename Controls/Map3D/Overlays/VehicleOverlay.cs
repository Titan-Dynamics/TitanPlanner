using System;
using System.Collections.Generic;
using System.Drawing;
using MissionPlanner.Utilities;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// The vehicle model and everything drawn from it: heading, navigation bearing, GPS course, predicted
    /// turn, and the flown trail.
    /// </summary>
    public sealed class VehicleOverlay : IDisposable
    {
        const double LineLength = 100;
        const double TurnArcLength = 200;
        const int TurnSegments = 50;

        /// <summary>Frames of non-zero altitude to wait for before recording the trail, so it never starts at 0 m.</summary>
        const int TrailStabilityFrames = 30;

        // the trail's moving average spans these many points (altitude, being noisier, more), and the curve into the
        // vehicle is drawn in this many segments
        const int TrailWindowXY = 20, TrailWindowZ = 60;
        const int TailSegments = 24;

        readonly VertexList _vertices = new VertexList();
        readonly List<double[]> _trail = new List<double[]>();

        // the smoothed trail, x, y, z per point
        readonly List<double> _smoothed = new List<double>();
        readonly ModelRenderer _model;
        int _trailZoneVersion = -1;
        int _trailStableFrames;

        public VehicleOverlay(STLModelLoader loader)
        {
            _model = new ModelRenderer(loader);
        }

        /// <summary>Records the trail in absolute UTM while armed; call once per frame.</summary>
        public void RecordTrail(WorldFrame frame, VehicleTracker vehicle, bool enabled, bool armed)
        {
            if (!enabled || !armed)
            {
                _trailStableFrames = 0;
                return;
            }

            if (vehicle.Z < 0.5)
            {
                _trailStableFrames = 0;
                return;
            }

            if (++_trailStableFrames < TrailStabilityFrames)
                return;

            if (_trailZoneVersion != frame.ZoneVersion)
            {
                _trail.Clear();
                _trailZoneVersion = frame.ZoneVersion;
            }

            int maxPoints = Settings.Instance.GetInt32("NUM_tracklength", 200) * 15;
            if (_trail.Count > maxPoints)
                _trail.RemoveRange(0, _trail.Count - maxPoints);
            _trail.Add(new[] { vehicle.X + frame.OriginEasting, vehicle.Y + frame.OriginNorthing, vehicle.Z });
        }

        public void DrawIndicators(LineRenderer lines, Map3DSettings settings, VehicleTracker vehicle,
            PointLatLngAlt navTarget, double navTargetAltitude, WorldFrame frame, ref Matrix4 projection, ref Matrix4 view)
        {
            var cs = MainV2.comPort?.MAV?.cs;
            double pitch = vehicle.Pitch * Math.PI / 180;
            GL.Enable(EnableCap.DepthTest);

            if (settings.ShowHeadingLine)
            {
                _vertices.Clear();
                AddDirection(vehicle, vehicle.Yaw, pitch, 1, 0, 0);
                lines.Draw(_vertices, PrimitiveType.Lines, 1.5f, ref projection, ref view);
            }

            if (settings.ShowNavBearingLine)
            {
                _vertices.Clear();
                if (navTarget != null && navTarget.Lat != 0 && navTarget.Lng != 0)
                {
                    double x, y;
                    frame.ToLocal(navTarget.Lat, navTarget.Lng, out x, out y);
                    _vertices.AddColored(vehicle.X, vehicle.Y, vehicle.Z, 1, 0.5f, 0, 1);
                    _vertices.AddColored(x, y, navTargetAltitude, 1, 0.5f, 0, 1);
                }
                else
                {
                    AddDirection(vehicle, vehicle.NavBearing, pitch, 1, 0.5f, 0);
                }

                lines.Draw(_vertices, PrimitiveType.Lines, 1.5f, ref projection, ref view);
            }

            if (settings.ShowGpsHeadingLine)
            {
                _vertices.Clear();
                AddDirection(vehicle, vehicle.GpsHeading, 0, 0, 0, 0);
                lines.Draw(_vertices, PrimitiveType.Lines, 1.5f, ref projection, ref view);
            }

            double radius = cs?.radius ?? 0;
            if (settings.ShowTurnRadius && Math.Abs(radius) > 1)
            {
                _vertices.Clear();
                AddTurnArc(vehicle, radius, pitch);
                lines.Draw(_vertices, PrimitiveType.LineStrip, 2f, ref projection, ref view);
            }
        }

        /// <summary>A line from the vehicle along a bearing (degrees), climbing with pitch (radians).</summary>
        void AddDirection(VehicleTracker vehicle, double bearing, double pitch, float r, float g, float b)
        {
            double bearingRad = bearing * Math.PI / 180;
            double horizontal = LineLength * Math.Cos(pitch);
            _vertices.AddColored(vehicle.X, vehicle.Y, vehicle.Z, r, g, b, 1);
            _vertices.AddColored(vehicle.X + Math.Sin(bearingRad) * horizontal, vehicle.Y + Math.Cos(bearingRad) * horizontal,
                vehicle.Z + LineLength * Math.Sin(pitch), r, g, b, 1);
        }

        /// <summary>The path the vehicle will fly at its current turn radius (negative turns left).</summary>
        void AddTurnArc(VehicleTracker vehicle, double radius, double pitch)
        {
            const float r = 1.0f, g = 105f / 255f, b = 180f / 255f;
            double sweep = Math.Min(Math.PI, TurnArcLength / Math.Abs(radius));
            double course = vehicle.GpsHeading * Math.PI / 180;
            double perpendicular = course + (radius > 0 ? Math.PI / 2 : -Math.PI / 2);
            double horizontalRadius = Math.Abs(radius) * Math.Cos(pitch);
            double centerX = vehicle.X + Math.Sin(perpendicular) * horizontalRadius;
            double centerY = vehicle.Y + Math.Cos(perpendicular) * horizontalRadius;
            double start = Math.Atan2(vehicle.X - centerX, vehicle.Y - centerY);
            double step = sweep / TurnSegments;
            double direction = radius > 0 ? 1 : -1;
            double climbPerSegment = Math.Abs(radius) * step * Math.Sin(pitch);

            _vertices.AddColored(vehicle.X, vehicle.Y, vehicle.Z, r, g, b, 1);
            for (int i = 1; i <= TurnSegments; i++)
            {
                double angle = start + direction * step * i;
                _vertices.AddColored(centerX + Math.Sin(angle) * horizontalRadius, centerY + Math.Cos(angle) * horizontalRadius,
                    vehicle.Z + climbPerSegment * i, r, g, b, 1);
            }
        }

        /// <summary>
        /// Draws the trail, smoothed or as recorded. Velocity is the vehicle's in m/s (north, east, down), the
        /// direction a smoothed trail arrives at it along.
        /// </summary>
        public void DrawTrail(LineRenderer lines, WorldFrame frame, VehicleTracker vehicle, bool armed, bool smooth,
            double velocityNorth, double velocityEast, double velocityDown, ref Matrix4 projection, ref Matrix4 view)
        {
            if (!armed || _trail.Count < 2 || _trailZoneVersion != frame.ZoneVersion)
                return;

            // MidnightBlue, as the 2D map's track.
            const float r = 25f / 255f, g = 25f / 255f, b = 112f / 255f, a = 144f / 255f;

            if (!smooth)
            {
                _vertices.Clear();
                foreach (var point in _trail)
                    _vertices.AddColored(point[0] - frame.OriginEasting, point[1] - frame.OriginNorthing, point[2], r, g, b, a);
                _vertices.AddColored(vehicle.X, vehicle.Y, vehicle.Z, r, g, b, a);
                DrawLines(lines, ref projection, ref view);
                return;
            }

            _smoothed.Clear();
            SmoothInto(_trail, TrailWindowXY, TrailWindowZ, frame, (x, y, z) =>
            {
                _smoothed.Add(x);
                _smoothed.Add(y);
                _smoothed.Add(z);
            });

            // The newest points have no later ones to average with, so they keep the pose's frame-to-frame wobble.
            // Draw the trail up to where its average is whole, then bend into the vehicle on a curve that leaves the
            // trail along it and arrives along the vehicle's velocity.
            int count = _smoothed.Count / 3;
            int end = Math.Max(0, count - 1 - TrailWindowZ / 2);
            _vertices.Clear();
            for (int i = 0; i <= end; i++)
                _vertices.AddColored(_smoothed[3 * i], _smoothed[3 * i + 1], _smoothed[3 * i + 2], r, g, b, a);
            AddTail(end, vehicle, velocityEast, velocityNorth, -velocityDown,
                (x, y, z) => _vertices.AddColored(x, y, z, r, g, b, a));
            DrawLines(lines, ref projection, ref view);
        }

        // the trail's vertices as a translucent line
        void DrawLines(LineRenderer lines, ref Matrix4 projection, ref Matrix4 view)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            lines.Draw(_vertices, PrimitiveType.LineStrip, 5f, ref projection, ref view);
            GL.Disable(EnableCap.Blend);
        }

        // a cubic Hermite from the smoothed trail's point at end to the vehicle; velocity is east, north, up (scene axes)
        void AddTail(int end, VehicleTracker vehicle, double velocityX, double velocityY, double velocityZ,
            Action<double, double, double> emit)
        {
            double x0 = _smoothed[3 * end], y0 = _smoothed[3 * end + 1], z0 = _smoothed[3 * end + 2];
            double x1 = vehicle.X, y1 = vehicle.Y, z1 = vehicle.Z;
            double dx = x1 - x0, dy = y1 - y0, dz = z1 - z0;
            double chord = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (chord < 0.01)
            {
                emit(x1, y1, z1);
                return;
            }

            // leaving: the trail's direction over its last few points; arriving: the vehicle's velocity, or straight
            // in while it barely moves. Both scaled to the gap, for an even curve.
            int back = Math.Max(0, end - 5);
            double ax = x0 - _smoothed[3 * back], ay = y0 - _smoothed[3 * back + 1], az = z0 - _smoothed[3 * back + 2];
            Scale(ref ax, ref ay, ref az, chord, dx, dy, dz);

            double bx = velocityX, by = velocityY, bz = velocityZ;
            if (Math.Sqrt(bx * bx + by * by + bz * bz) < 0.5)
            {
                bx = dx;
                by = dy;
                bz = dz;
            }

            Scale(ref bx, ref by, ref bz, chord, dx, dy, dz);

            for (int k = 1; k <= TailSegments; k++)
            {
                double t = (double)k / TailSegments, t2 = t * t, t3 = t2 * t;
                double h00 = 2 * t3 - 3 * t2 + 1, h10 = t3 - 2 * t2 + t, h01 = -2 * t3 + 3 * t2, h11 = t3 - t2;
                emit(h00 * x0 + h10 * ax + h01 * x1 + h11 * bx,
                    h00 * y0 + h10 * ay + h01 * y1 + h11 * by,
                    h00 * z0 + h10 * az + h01 * z1 + h11 * bz);
            }
        }

        // scales a direction to a length; a zero one takes the fallback's direction
        static void Scale(ref double x, ref double y, ref double z, double length, double fx, double fy, double fz)
        {
            double size = Math.Sqrt(x * x + y * y + z * z);
            if (size < 1e-9)
            {
                x = fx;
                y = fy;
                z = fz;
                size = Math.Sqrt(x * x + y * y + z * z);
            }

            x *= length / size;
            y *= length / size;
            z *= length / size;
        }

        /// <summary>
        /// Centred moving average (wider for altitude, which is noisier), computed with running sums. Near the ends
        /// the window narrows on both sides so it stays centred: one cut off at the newest point would average only
        /// older ones, lagging behind the vehicle and leaving a kink where the line meets it.
        /// </summary>
        static void SmoothInto(List<double[]> points, int windowXY, int windowZ, WorldFrame frame, Action<double, double, double> emit)
        {
            int n = points.Count;
            var sumX = new double[n + 1];
            var sumY = new double[n + 1];
            var sumZ = new double[n + 1];
            for (int i = 0; i < n; i++)
            {
                sumX[i + 1] = sumX[i] + (points[i][0] - frame.OriginEasting);
                sumY[i + 1] = sumY[i] + (points[i][1] - frame.OriginNorthing);
                sumZ[i + 1] = sumZ[i] + points[i][2];
            }

            for (int i = 0; i < n; i++)
            {
                int toEnd = Math.Min(i, n - 1 - i);
                int half = Math.Min(windowXY / 2, toEnd);
                int halfZ = Math.Min(windowZ / 2, toEnd);
                emit((sumX[i + half + 1] - sumX[i - half]) / (2 * half + 1),
                    (sumY[i + half + 1] - sumY[i - half]) / (2 * half + 1),
                    (sumZ[i + halfZ + 1] - sumZ[i - halfZ]) / (2 * halfZ + 1));
            }
        }

        public void DrawModel(VehicleTracker vehicle, float scale, Color color, ref Matrix4 projection, ref Matrix4 view)
        {
            // The STL is in millimetres.
            var model = Matrix4.CreateScale(scale / 1000f);
            model = Matrix4.Mult(model, Matrix4.CreateRotationY(vehicle.Roll * (float)Math.PI / 180));
            model = Matrix4.Mult(model, Matrix4.CreateRotationX(vehicle.Pitch * (float)Math.PI / 180));
            model = Matrix4.Mult(model, Matrix4.CreateRotationZ(-vehicle.Yaw * (float)Math.PI / 180));
            model = Matrix4.Mult(model, Matrix4.CreateTranslation((float)vehicle.X, (float)vehicle.Y, (float)vehicle.Z));
            _model.Draw(ref projection, ref view, model, color);
        }

        public void Dispose()
        {
            _model.Dispose();
        }
    }
}
