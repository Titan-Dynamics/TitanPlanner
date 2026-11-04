using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using GMap.NET.WindowsForms;
using MissionPlanner.Utilities;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// The mission: flight plan line, rotating waypoint markers with their numbers, and the guided/target
    /// markers. Waypoint altitudes are relative, so they are drawn above the terrain under each point.
    /// </summary>
    public sealed class MissionOverlay : IDisposable
    {
        /// <summary>
        /// Markers closer than this to the vehicle (200 ft) fade, and are gone at a quarter of it, rather than vanishing
        /// at once as the vehicle nears them.
        /// </summary>
        public const double MarkerFadeDistance = 61.0;

        readonly VertexList _planLine = new VertexList();
        readonly QuadRenderer _markers = new QuadRenderer();
        readonly QuadRenderer _homeMarker = new QuadRenderer();
        readonly LabelTextures _labels = new LabelTextures();
        readonly Dictionary<long, double> _terrainCache = new Dictionary<long, double>();
        readonly List<PointLatLngAlt> _waypoints = new List<PointLatLngAlt>();
        int _waypointsHash;
        int _planHash;
        int _planFrameVersion = -1;
        bool _planNeedsTerrain;
        bool _terrainMissing;
        DateTime _planBuilt;
        int _markerTexture;
        int _markerAltTexture;
        int _dotTexture;

        /// <summary>Markers drawn last frame; they spin, so the view keeps animating while any are visible.</summary>
        public int MarkersDrawn { get; private set; }

        /// <summary>
        /// Home plus the navigable mission items, in mission order. Entries are null for items without a
        /// position so indices match waypoint numbers.
        /// </summary>
        public List<PointLatLngAlt> Waypoints()
        {
            try
            {
                var wps = MainV2.comPort.MAV.wps;
                if (wps == null || wps.Count == 0)
                {
                    _waypoints.Clear();
                    _waypointsHash = 0;
                    return _waypoints;
                }

                int hash;
                unchecked
                {
                    hash = 17;
                    foreach (var kvp in wps)
                    {
                        var wp = kvp.Value;
                        hash = hash * 31 + wp.seq.GetHashCode();
                        hash = hash * 31 + wp.x.GetHashCode();
                        hash = hash * 31 + wp.y.GetHashCode();
                        hash = hash * 31 + wp.z.GetHashCode();
                        hash = hash * 31 + wp.command.GetHashCode();
                    }

                    var homeLocation = MainV2.comPort.MAV.cs.HomeLocation;
                    hash = hash * 31 + homeLocation.Lat.GetHashCode();
                    hash = hash * 31 + homeLocation.Lng.GetHashCode();
                }

                if (hash == _waypointsHash && _waypoints.Count > 0)
                    return _waypoints;

                _waypointsHash = hash;
                _waypoints.Clear();

                var cs = MainV2.comPort.MAV.cs;
                var home = PointLatLngAlt.Zero;
                if (cs.HomeLocation.Lat != 0 || cs.HomeLocation.Lng != 0)
                    home = new PointLatLngAlt(cs.HomeLocation.Lat, cs.HomeLocation.Lng,
                        cs.HomeLocation.Alt / CurrentState.multiplieralt, "H");
                else if (cs.PlannedHomeLocation.Lat != 0 || cs.PlannedHomeLocation.Lng != 0)
                    home = new PointLatLngAlt(cs.PlannedHomeLocation.Lat, cs.PlannedHomeLocation.Lng,
                        cs.PlannedHomeLocation.Alt / CurrentState.multiplieralt, "H");

                if (home != PointLatLngAlt.Zero)
                    _waypoints.Add(home);

                foreach (var wp in wps.Values.OrderBy(a => a.seq))
                {
                    if (wp.seq == 0)
                        continue;

                    var cmd = wp.command;
                    // Navigable items, as on the 2D map.
                    if (cmd < (ushort)MAVLink.MAV_CMD.LAST &&
                        cmd != (ushort)MAVLink.MAV_CMD.RETURN_TO_LAUNCH &&
                        cmd != (ushort)MAVLink.MAV_CMD.CONTINUE_AND_CHANGE_ALT &&
                        cmd != (ushort)MAVLink.MAV_CMD.DELAY &&
                        cmd != (ushort)MAVLink.MAV_CMD.GUIDED_ENABLE
                        || cmd == (ushort)MAVLink.MAV_CMD.DO_SET_ROI || cmd == (ushort)MAVLink.MAV_CMD.DO_LAND_START)
                    {
                        double lat = wp.x / 1e7;
                        double lng = wp.y / 1e7;

                        if ((cmd == (ushort)MAVLink.MAV_CMD.LAND || cmd == (ushort)MAVLink.MAV_CMD.VTOL_LAND) &&
                            lat == 0 && lng == 0)
                            continue;

                        if (lat == 0 && lng == 0)
                        {
                            _waypoints.Add(null);
                            continue;
                        }

                        var point = new PointLatLngAlt(lat, lng, wp.z, wp.seq.ToString());
                        if (cmd == (ushort)MAVLink.MAV_CMD.DO_SET_ROI)
                            point.Tag = "ROI" + wp.seq;
                        _waypoints.Add(point);
                    }
                    else
                    {
                        _waypoints.Add(null);
                    }
                }
            }
            catch
            {
            }

            return _waypoints;
        }

        /// <summary>
        /// What the vehicle is navigating to in auto, guided and return modes, or null.
        /// </summary>
        public PointLatLngAlt NavigationTarget()
        {
            var mav = MainV2.comPort?.MAV;
            var mode = mav?.cs?.mode?.ToLower() ?? "";

            if (mode == "guided" && mav.GuidedMode.x != 0)
                return new PointLatLngAlt(mav.GuidedMode) { Alt = mav.GuidedMode.z + mav.cs.HomeAlt, Tag = "G" };

            if (mode == "rtl" || mode == "land" || mode == "smart_rtl")
                return Waypoints().FirstOrDefault(p => p != null && p.Tag == "H");

            if (mode == "auto")
            {
                int wpno = (int)(mav?.cs?.wpno ?? 0);
                var list = Waypoints().Where(a => a != null).ToList();
                if (wpno > 0 && wpno < list.Count)
                    return list[wpno];
            }

            return null;
        }

        /// <summary>
        /// Altitude AMSL at which a mission point is drawn: home on the ground, guided at the guided height
        /// above ground, everything else at its relative altitude above the terrain below it.
        /// </summary>
        public double DrawAltitude(PointLatLngAlt point)
        {
            double terrain = TerrainAltitude(point.Lat, point.Lng);
            if (point.Tag == "H")
                return terrain;
            if (point.Tag == "G")
                return terrain + (MainV2.comPort?.MAV?.GuidedMode.z ?? 0);
            return point.Alt + terrain;
        }

        double TerrainAltitude(double lat, double lng)
        {
            long key = (long)Math.Round(lat * 1e6) * 1000000000L + (long)Math.Round(lng * 1e6);
            double alt;
            if (_terrainCache.TryGetValue(key, out alt))
                return alt;

            // Non-blocking: until the cell is in memory the point draws at sea level and the plan line retries.
            if (srtm.TryGetLoadedAltitude(lat, lng, out alt))
            {
                if (_terrainCache.Count > 5000)
                    _terrainCache.Clear();
                _terrainCache[key] = alt;
            }
            else
            {
                _terrainMissing = true;
            }

            return alt;
        }

        public void DrawPlanLine(LineRenderer lines, WorldFrame frame, ref Matrix4 projection, ref Matrix4 view)
        {
            // the line runs from waypoint 1 on, as on the 2D map; home keeps its marker but isn't joined to it
            var waypoints = Waypoints();
            if (waypoints.Count(a => a != null && a.Tag != "H") < 2)
                return;

            // Rebuilt when the mission or origin changes, and every second while terrain heights are still loading.
            bool retryTerrain = _planNeedsTerrain && DateTime.Now - _planBuilt > TimeSpan.FromSeconds(1);
            if (_planHash != _waypointsHash || _planFrameVersion != frame.Version || _planLine.Count == 0 || retryTerrain)
            {
                _terrainMissing = false;
                _planLine.Clear();
                foreach (var point in waypoints)
                {
                    if (point == null || point.Tag == "H")
                        continue;
                    double x, y;
                    frame.ToLocal(point.Lat, point.Lng, out x, out y);
                    _planLine.AddColored(x, y, DrawAltitude(point), 1, 1, 0, 1);
                }

                _planHash = _waypointsHash;
                _planFrameVersion = frame.Version;
                _planNeedsTerrain = _terrainMissing;
                _planBuilt = DateTime.Now;
            }

            GL.Enable(EnableCap.DepthTest);
            lines.Draw(_planLine, PrimitiveType.LineStrip, 3.0f, ref projection, ref view);
        }

        /// <summary>
        /// Billboarded markers that face the camera and slowly spin, with an upright label. vehicleX and vehicleY are
        /// where the vehicle is drawn, in scene coordinates: markers fade by their distance from it, which moves
        /// smoothly where the telemetry steps.
        /// </summary>
        public void DrawMarkers(WorldFrame frame, double vehicleX, double vehicleY, double cameraX, double cameraY,
            double markerHalfSize, ref Matrix4 projection, ref Matrix4 view)
        {
            if (_markerTexture == 0)
                _markerTexture = MarkerTexture(GMap.NET.Drawing.Properties.Resources.wp_3d.ToBitmap());
            if (_markerAltTexture == 0)
                _markerAltTexture = MarkerTexture(GMap.NET.Drawing.Properties.Resources.wp_3d_alt.ToBitmap());
            if (_dotTexture == 0)
                _dotTexture = DotTexture();

            var waypoints = Waypoints();
            var points = waypoints.Where(a => a != null).ToList();
            var mav = MainV2.comPort.MAV;
            if (mav.cs.mode.ToLower() == "guided")
                points.Add(new PointLatLngAlt(mav.GuidedMode) { Alt = mav.GuidedMode.z + mav.cs.HomeAlt, Tag = "G" });
            if (mav.cs.TargetLocation != PointLatLngAlt.Zero)
                points.Add(mav.cs.TargetLocation);

            var numbered = waypoints.Where(a => a != null).ToList();
            double spin = (DateTime.Now.TimeOfDay.TotalSeconds * 30.0) % 360.0 * Math.PI / 180;
            double cosSpin = Math.Cos(spin);
            double sinSpin = Math.Sin(spin);

            _markers.Begin();
            _homeMarker.Begin();
            MarkersDrawn = 0;

            // Far to near so nearer markers blend over farther ones.
            foreach (var point in points.OrderByDescending(a => a.GetDistance(mav.cs.Location)))
            {
                if (point.Lat == 0 && point.Lng == 0)
                    continue;

                double x, y;
                frame.ToLocal(point.Lat, point.Lng, out x, out y);
                float opacity = MarkerOpacity(Math.Sqrt((x - vehicleX) * (x - vehicleX) + (y - vehicleY) * (y - vehicleY)));
                if (opacity <= 0)
                    continue;

                double z = DrawAltitude(point);

                string label = null;
                int index = numbered.IndexOf(point);
                if (point.Tag == "H")
                    label = "H";
                else if (point.Tag != null && point.Tag.StartsWith("ROI"))
                    label = "R";
                else if (IsGuidedTarget(point))
                    label = "G";
                else if (index >= 0)
                    label = index.ToString();

                bool special = label != null && !char.IsDigit(label[0]);
                var batch = point.Tag == "H" ? _homeMarker : _markers;

                // Face the camera: the billboard's horizontal axis is perpendicular to the view direction.
                double toCamera = Math.Atan2(cameraX - x, cameraY - y);
                double axisX = Math.Sin(toCamera + Math.PI / 2);
                double axisY = Math.Cos(toCamera + Math.PI / 2);

                Func<double, double, Vector3d> spun = (h, v) =>
                {
                    double rh = h * cosSpin - v * sinSpin;
                    double rv = h * sinSpin + v * cosSpin;
                    return new Vector3d(x + axisX * rh * markerHalfSize, y + axisY * rh * markerHalfSize, z + rv * markerHalfSize);
                };
                batch.Add(special ? _markerAltTexture : _markerTexture,
                    spun(-1, 1), spun(1, 1), spun(-1, -1), spun(1, -1), opacity: opacity);
                MarkersDrawn++;

                // a yellow dot in the middle of each numbered waypoint, its radius a fifteenth of the Marker Size
                if (label != null && !special)
                {
                    double dot = markerHalfSize / 15;
                    batch.Add(_dotTexture,
                        new Vector3d(x - axisX * dot, y - axisY * dot, z + dot),
                        new Vector3d(x + axisX * dot, y + axisY * dot, z + dot),
                        new Vector3d(x - axisX * dot, y - axisY * dot, z - dot),
                        new Vector3d(x + axisX * dot, y + axisY * dot, z - dot), opacity: opacity);
                }

                if (label == null)
                    continue;

                // Letters sit centred in the marker; numbers sit above it. Mirrored so text reads correctly.
                double labelHalfSize = special ? markerHalfSize * 0.6 : markerHalfSize * 0.4;
                double labelOffset = special ? -(markerHalfSize / 20) : markerHalfSize * 0.5;
                Func<double, double, Vector3d> upright = (h, v) => new Vector3d(
                    x - axisX * h * labelHalfSize, y - axisY * h * labelHalfSize, z + v * labelHalfSize + labelOffset);
                batch.Add(_labels.Get(label), upright(-1, 1), upright(1, 1), upright(-1, -1), upright(1, -1),
                    opacity: opacity);
            }

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);

            GL.Enable(EnableCap.DepthTest);
            _markers.Draw(ref projection, ref view);

            // Home is on the ground, so it would be half hidden by the terrain-level depth; always draw it whole.
            GL.Disable(EnableCap.DepthTest);
            _homeMarker.Draw(ref projection, ref view);
            GL.Enable(EnableCap.DepthTest);

            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }

        /// <summary>A marker's opacity at a distance (metres) from the vehicle.</summary>
        public static float MarkerOpacity(double distance)
        {
            double gone = MarkerFadeDistance / 4;
            return (float)Math.Max(0, Math.Min(1, (distance - gone) / (MarkerFadeDistance - gone)));
        }

        static bool IsGuidedTarget(PointLatLngAlt point)
        {
            var guided = MainV2.comPort?.MAV?.GuidedMode;
            if (guided == null || guided.Value.x == 0 && guided.Value.y == 0)
                return false;

            const double tolerance = 0.0001;
            return Math.Abs(point.Lat - guided.Value.x / 1e7) < tolerance &&
                   Math.Abs(point.Lng - guided.Value.y / 1e7) < tolerance;
        }

        static int MarkerTexture(Bitmap bitmap)
        {
            using (bitmap)
            {
                bitmap.MakeTransparent();
                return GLTextures.CreateFromImage(bitmap, true);
            }
        }

        static int DotTexture()
        {
            const int size = 64;
            using (var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                {
                    // cleared to the dot's colour, so the smaller mipmaps don't fringe it with another
                    g.Clear(Color.FromArgb(0, Color.Yellow));
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.FillEllipse(Brushes.Yellow, 1, 1, size - 2, size - 2);
                }

                return GLTextures.CreateFromImage(bitmap, true);
            }
        }

        public void Dispose()
        {
            GLTextures.Delete(ref _markerTexture);
            GLTextures.Delete(ref _markerAltTexture);
            GLTextures.Delete(ref _dotTexture);
            _labels.Dispose();
            _markers.Dispose();
            _homeMarker.Dispose();
        }
    }
}
