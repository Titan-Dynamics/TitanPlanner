using System;
using System.Collections.Generic;
using System.Text;
using MissionPlanner.Utilities;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// ADS-B traffic as camera-facing circles coloured by distance (red close, yellow, green far; grey on
    /// the ground), depth-tested against the terrain, with screen positions kept for hover tooltips.
    /// </summary>
    public sealed class AdsbOverlay
    {
        const double MaxDistance = 50000;
        const double RedDistance = 5000;
        const double YellowDistance = 10000;
        const double GreenDistance = 20000;
        const int CircleSegments = 24;

        public struct ScreenPosition
        {
            public float X;
            public float Y;
            public float Radius;
            public adsb.PointLatLngAltHdg Plane;
            public double DistanceToOwn;
        }

        readonly VertexList _vertices = new VertexList();
        /// <summary>Plane, where it is predicted to be this frame, and its distance from our vehicle.</summary>
        readonly List<Tuple<adsb.PointLatLngAltHdg, PointLatLngAlt, double>> _planes =
            new List<Tuple<adsb.PointLatLngAltHdg, PointLatLngAlt, double>>();

        /// <summary>Where each aircraft was drawn last frame, nearest last.</summary>
        public readonly List<ScreenPosition> ScreenPositions = new List<ScreenPosition>();

        public void Draw(LineRenderer lines, WorldFrame frame, PointLatLngAlt ownPosition, double circleDiameter,
            double cameraX, double cameraY, double cameraZ, double fovDegrees, int viewportWidth, int viewportHeight,
            ref Matrix4 projection, ref Matrix4 view)
        {
            ScreenPositions.Clear();
            _planes.Clear();

            if (MainV2.instance == null)
                return;

            var now = DateTime.Now;
            lock (MainV2.instance.adsblock)
            {
                foreach (var kvp in MainV2.instance.adsbPlanes)
                {
                    var plane = kvp.Value;
                    if (plane == null || plane.Time < now.AddSeconds(-30))
                        continue;
                    if (plane.Lat == 0 && plane.Lng == 0)
                        continue;

                    // Carried forward between feed updates so traffic moves at the frame rate.
                    var position = plane.Predict(now);
                    double distance = ownPosition.GetDistance(position);
                    if (distance <= MaxDistance)
                        _planes.Add(Tuple.Create(plane, position, distance));
                }
            }

            if (_planes.Count == 0)
                return;

            // Farthest first so nearer circles draw over farther ones.
            _planes.Sort((a, b) => b.Item3.CompareTo(a.Item3));

            var viewProjection = Matrix4.Mult(view, projection);
            double tanHalfFov = Math.Tan(fovDegrees * Math.PI / 180 / 2);
            _vertices.Clear();

            foreach (var item in _planes)
            {
                var plane = item.Item1;
                var position = item.Item2;
                double distanceToOwn = item.Item3;
                bool grounded = plane.IsOnGround;
                double radius = circleDiameter / 2.0 / (grounded ? 5.0 : 1.0);

                double x, y;
                frame.ToLocal(position.Lat, position.Lng, out x, out y);
                double z = position.Alt;
                double terrain;
                if (grounded && srtm.TryGetLoadedAltitude(position.Lat, position.Lng, out terrain))
                {
                    // Keep grounded traffic visible above the terrain.
                    z = Math.Max(z, terrain + 10);
                }

                double dx = x - cameraX, dy = y - cameraY, dz = z - cameraZ;
                double distanceToCamera = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distanceToCamera < 1.0)
                    continue;

                var clip = Vector4.Transform(new Vector4((float)x, (float)y, (float)z, 1), viewProjection);
                if (clip.W > 0)
                {
                    float screenRadius = (float)(radius / distanceToCamera * (viewportHeight / 2.0) / tanHalfFov);
                    ScreenPositions.Add(new ScreenPosition
                    {
                        X = (clip.X / clip.W + 1) * 0.5f * viewportWidth,
                        Y = (1 - clip.Y / clip.W) * 0.5f * viewportHeight,
                        Radius = Math.Max(screenRadius, 20),
                        Plane = plane,
                        DistanceToOwn = distanceToOwn
                    });
                }

                float r, g, b;
                DistanceColor(distanceToOwn, grounded, out r, out g, out b);
                AddBillboardCircle(x, y, z, radius, dx / distanceToCamera, dy / distanceToCamera, dz / distanceToCamera, r, g, b);
            }

            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Lequal);
            lines.Draw(_vertices, PrimitiveType.Lines, 3f, ref projection, ref view);
        }

        static void DistanceColor(double distance, bool grounded, out float r, out float g, out float b)
        {
            b = 0;
            if (grounded)
            {
                r = g = b = 0.7f;
            }
            else if (distance <= RedDistance)
            {
                r = 1;
                g = 0;
            }
            else if (distance <= YellowDistance)
            {
                r = 1;
                g = (float)((distance - RedDistance) / (YellowDistance - RedDistance));
            }
            else if (distance <= GreenDistance)
            {
                r = 1 - (float)((distance - YellowDistance) / (GreenDistance - YellowDistance));
                g = 1;
            }
            else
            {
                r = 0;
                g = 1;
            }
        }

        /// <summary>A circle facing the camera, as separate segments for GL_LINES.</summary>
        void AddBillboardCircle(double cx, double cy, double cz, double radius, double viewX, double viewY, double viewZ,
            float r, float g, float b)
        {
            // Right = view x world-up; up = right x view.
            double rightX = viewY, rightY = -viewX, rightZ = 0;
            double rightLength = Math.Sqrt(rightX * rightX + rightY * rightY);
            if (rightLength > 0.001)
            {
                rightX /= rightLength;
                rightY /= rightLength;
            }
            else
            {
                rightX = 1;
                rightY = 0;
            }

            double upX = rightY * viewZ - rightZ * viewY;
            double upY = rightZ * viewX - rightX * viewZ;
            double upZ = rightX * viewY - rightY * viewX;

            for (int i = 0; i < CircleSegments; i++)
            {
                double a1 = 2 * Math.PI * i / CircleSegments;
                double a2 = 2 * Math.PI * (i + 1) / CircleSegments;
                double c1 = Math.Cos(a1), s1 = Math.Sin(a1), c2 = Math.Cos(a2), s2 = Math.Sin(a2);
                _vertices.AddColored(cx + radius * (c1 * rightX + s1 * upX), cy + radius * (c1 * rightY + s1 * upY),
                    cz + radius * (c1 * rightZ + s1 * upZ), r, g, b, 1);
                _vertices.AddColored(cx + radius * (c2 * rightX + s2 * upX), cy + radius * (c2 * rightY + s2 * upY),
                    cz + radius * (c2 * rightZ + s2 * upZ), r, g, b, 1);
            }
        }

        /// <summary>The aircraft under a screen point, if any.</summary>
        public bool HitTest(int x, int y, out ScreenPosition hit)
        {
            for (int i = ScreenPositions.Count - 1; i >= 0; i--)
            {
                var p = ScreenPositions[i];
                float dx = x - p.X, dy = y - p.Y;
                if (dx * dx + dy * dy <= p.Radius * p.Radius)
                {
                    hit = p;
                    return true;
                }
            }

            hit = default(ScreenPosition);
            return false;
        }

        public static string Tooltip(ScreenPosition hit)
        {
            var plane = hit.Plane;
            var sb = new StringBuilder();
            sb.AppendLine("ICAO: " + plane.Tag);
            sb.AppendLine("Callsign: " + plane.CallSign);

            string category = "";
            try
            {
                var raw = plane.Raw as Newtonsoft.Json.Linq.JObject;
                if (raw != null && raw.ContainsKey("t"))
                    category = raw["t"] + " ";
            }
            catch
            {
            }

            category += plane.GetCategoryFriendlyString();
            sb.AppendLine("Type\\Category: " + category);
            sb.AppendLine("Squawk: " + plane.Squawk);
            sb.AppendLine("Altitude: " + (plane.Alt * CurrentState.multiplieralt).ToString("0") + " " + CurrentState.AltUnit);
            sb.AppendLine("Speed: " + (plane.Speed / 100.0 * CurrentState.multiplierspeed).ToString("0") + " " + CurrentState.SpeedUnit);
            sb.AppendLine("Heading: " + plane.Heading.ToString("0") + "°");

            sb.AppendLine("Distance: " + (hit.DistanceToOwn > 1000
                ? (hit.DistanceToOwn / 1000).ToString("0.#") + " km"
                : (hit.DistanceToOwn * CurrentState.multiplierdist).ToString("0") + " " + CurrentState.DistanceUnit));

            double ownAlt = MainV2.comPort?.MAV?.cs?.alt ?? 0;
            double altDelta = (plane.Alt - ownAlt) * CurrentState.multiplieralt;
            sb.AppendLine("Alt Delta: " + (altDelta >= 0 ? "+" : "") + altDelta.ToString("0") + " " + CurrentState.AltUnit);

            if (plane.ThreatLevel != MAVLink.MAV_COLLISION_THREAT_LEVEL.NONE)
                sb.AppendLine("Collision risk: " + (plane.ThreatLevel == MAVLink.MAV_COLLISION_THREAT_LEVEL.LOW ? "Warning" : "Danger"));

            return sb.ToString().TrimEnd();
        }
    }
}
