using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MissionPlanner.Utilities;
using MissionPlanner.Utilities.Mission;

namespace MissionPlanner.Controls.TerrainProfile
{
    /// <summary>What the terrain profile is built from, as plain data so it can be built off the UI thread.</summary>
    public sealed class ProfileInput
    {
        /// <summary>Planned home, its altitude in metres AMSL; 0, 0 or null when it isn't set.</summary>
        public PointLatLngAlt Home;

        public List<Locationwp> Mission = new List<Locationwp>();

        /// <summary>Rally points, from a list of any items (only the RALLY_POINTs count).</summary>
        public List<Locationwp> Rally = new List<Locationwp>();

        /// <summary>null when there is no fence to check against.</summary>
        public MissionFence Fence;

        /// <summary>Multirotors (and an unknown vehicle) land straight down, planes glide in; splines follow the map.</summary>
        public VehicleClass VehicleClass;

        /// <summary>Metres; the map's spline curves out of a plane's loiter start on it.</summary>
        public double LoiterRadius;

        /// <summary>A text form of the whole input: two inputs with the same signature build the same profile.</summary>
        public string Signature()
        {
            var sb = new StringBuilder();
            Action<double> add = v => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            if (Home != null)
            {
                add(Home.Lat);
                add(Home.Lng);
                add(Home.Alt);
            }

            foreach (var list in new[] { Mission, Rally })
            {
                sb.Append('|');
                foreach (var item in list)
                {
                    sb.Append(item.id).Append(':').Append(item.frame).Append(':');
                    add(item.lat);
                    add(item.lng);
                    add(item.alt);
                    sb.Append(';');
                }
            }

            sb.Append('|').Append(VehicleClass);
            add(LoiterRadius);
            Fence?.AppendSignature(sb);
            return sb.ToString();
        }
    }

    /// <summary>
    /// A mission's altitude and the ground under it along its route, flown in mission order from home, for the PLAN
    /// screen's terrain profile, with the rally points and the vehicle's fence. Altitudes are metres above sea level
    /// and distances metres along the route.
    /// </summary>
    public sealed class MissionProfile
    {
        /// <summary>Home, a mission item the route passes through, or a rally point.</summary>
        public sealed class Waypoint
        {
            /// <summary>"H" for home, "RTL", "R" and its number for a rally point, else the item's number as the map shows it.</summary>
            public string Label;

            /// <summary>The item's index in its list; -1 for home and RTL.</summary>
            public int Item = -1;

            public double Lat, Lng;
            public double Distance;
            public double Altitude;

            /// <summary>The ground's altitude under the point, NaN where it isn't known.</summary>
            public double Terrain = double.NaN;

            /// <summary>Home or a landing.</summary>
            public bool OnGround;

            /// <summary>Rally points: metres from the nearest point of the route.</summary>
            public double OffRoute;

            /// <summary>How the point is outside the fence the vehicle will hold to.</summary>
            public FenceBreach Breach;
        }

        /// <summary>A point along the route.</summary>
        public struct Sample
        {
            public double Distance, Lat, Lng;

            /// <summary>The vehicle's altitude; NaN along the rally points' line, which isn't flown.</summary>
            public double Altitude;

            /// <summary>The ground's altitude, NaN where it isn't known.</summary>
            public double Terrain;

            /// <summary>
            /// Whether going below the ground here is a conflict: false next to home and landing points, where the
            /// route meets the ground on purpose and the terrain data's few metres of error would read as one.
            /// </summary>
            public bool Checked;

            /// <summary>
            /// Whether this is on a leg between two points in the air, so not the climb from home or the way down to
            /// a landing, which come close to the ground by design.
            /// </summary>
            public bool EnRoute;

            public double Clearance => Altitude - Terrain;
        }

        /// <summary>Home and the mission items along the route, in the order they are flown.</summary>
        public List<Waypoint> Waypoints { get; } = new List<Waypoint>();

        public List<Waypoint> RallyPoints { get; } = new List<Waypoint>();

        public List<Sample> Samples { get; } = new List<Sample>();
        public double Length { get; private set; }

        /// <summary>
        /// There is no mission route, so the ground is laid out along a line through the rally points instead, home
        /// first. Nothing flies along it, so its samples have no altitude.
        /// </summary>
        public bool RallyChain { get; private set; }

        /// <summary>False while the ground under part of the route isn't known yet.</summary>
        public bool TerrainComplete { get; private set; } = true;

        /// <summary>
        /// The least height above the ground on the legs between points in the air (of the rally points themselves
        /// along their line), NaN when there are none or no ground is known there.
        /// </summary>
        public double MinClearance { get; private set; } = double.NaN;

        public double MinClearanceDistance { get; private set; }

        /// <summary>How far below the ground the route goes at worst, 0 when it never does.</summary>
        public double Conflict { get; private set; }

        public double ConflictDistance { get; private set; }

        public bool HasConflict => Conflict > 0;

        /// <summary>The fence checked against, null for none.</summary>
        public MissionFence Fence { get; private set; }

        /// <summary>The fence ceiling and floor AMSL (they are measured from home), NaN without one or without home.</summary>
        public double FenceCeiling { get; private set; } = double.NaN;

        public double FenceFloor { get; private set; } = double.NaN;

        /// <summary>Where the route crosses the circle fence the vehicle will hold to, in order.</summary>
        public List<double> FenceCircleCrossings { get; } = new List<double>();

        /// <summary>The waypoints and rally points outside the fence the vehicle will hold to, waypoints first.</summary>
        public List<Waypoint> OutsideFence { get; } = new List<Waypoint>();

        // within this of where the route meets the ground, its clearance doesn't count
        const double GroundContactMargin = 30;

        // samples along the route: no further apart than this (finer than SRTM1's 30 m grid, so a narrow ridge isn't
        // stepped over), and at least the first count for a short route, but no more than the second for a long one
        const double MaxSpacing = 20;
        const int MinSamples = 600, MaxSamples = 6000;

        const double MetresPerDegree = 6371000 * Math.PI / 180;

        sealed class RoutePoint
        {
            public double Lat, Lng, Altitude;
            public string Label;
            public int Item = -1;

            // the leg into this point holds a height above the ground (a terrain-frame item)
            public bool FollowsTerrain;

            // home, or where a landing touches down
            public bool OnGround;

            // a spline waypoint: the map's curve into it, neither end included; null for a straight leg
            public List<PointLatLngAlt> Curve;

            // a rally point the line through them passes
            public Waypoint Rally;
        }

        /// <summary>
        /// Builds the profile of a mission alone. Landings descend vertically when <paramref name="verticalLanding"/>
        /// is set (multirotors), else glide in from the previous point.
        /// </summary>
        public static MissionProfile Build(PointLatLngAlt home, IList<Locationwp> items, bool verticalLanding,
            Func<double, double, double> terrain)
        {
            if (items == null)
                return null;

            return Build(new ProfileInput
            {
                Home = home, Mission = new List<Locationwp>(items),
                VehicleClass = verticalLanding ? VehicleClass.Copter : VehicleClass.Plane
            }, terrain);
        }

        /// <summary>
        /// Builds the profile. <paramref name="terrain"/> gives the ground's altitude at a lat/lng, NaN where it
        /// isn't known; it is called many times, so it must be quick. Rally points sit at the route's nearest point,
        /// or along a line of their own when the mission has no route. Returns null when there is nothing to draw.
        /// </summary>
        public static MissionProfile Build(ProfileInput input, Func<double, double, double> terrain)
        {
            bool hasHome = input.Home != null && (input.Home.Lat != 0 || input.Home.Lng != 0);
            var home = hasHome ? input.Home : null;

            double? reference;
            var route = BuildRoute(home, input.Mission ?? new List<Locationwp>(),
                input.VehicleClass != VehicleClass.Plane, terrain, out reference);
            FollowSplines(route, input);

            var profile = new MissionProfile { Fence = input.Fence };
            var rally = RallyPointsOf(input.Rally, reference, terrain);
            profile.RallyPoints.AddRange(rally);

            // without a mission route, the ground along a line through the rally points, home first
            if (route.Count < 2)
            {
                if (rally.Count == 0)
                    return null;

                profile.RallyChain = true;
                route.RemoveAll(a => a.Label != "H");
                foreach (var point in rally)
                    route.Add(new RoutePoint { Lat = point.Lat, Lng = point.Lng, Altitude = double.NaN, Rally = point });
                if (route.Count < 2)
                    return null;
            }

            profile.SampleRoute(route, terrain);
            if (profile.Length <= 0)
                return null;

            if (profile.RallyChain)
            {
                // nothing flies between them, so the clearance is the points' own
                foreach (var point in rally)
                    profile.NoteClearance(point.Altitude - point.Terrain, point.Distance);
            }
            else
            {
                foreach (var point in rally)
                    profile.PlaceOnRoute(point);
            }

            foreach (var point in rally)
            {
                if (double.IsNaN(point.Terrain))
                    profile.TerrainComplete = false;
            }

            profile.CheckFence(home, reference);
            return profile;
        }

        static List<RoutePoint> BuildRoute(PointLatLngAlt home, IList<Locationwp> items, bool verticalLanding,
            Func<double, double, double> terrain, out double? reference)
        {
            var route = new List<RoutePoint>();

            // the altitude relative-frame items count from: home's, or the ground under the first item without one
            reference = null;
            if (home != null)
            {
                // The vehicle takes off from the ground at home and its relative altitudes count from there, so home
                // sits on the terrain the route is drawn against. The altitude typed in or reported for home is only
                // used until that terrain loads: a simulator often reports 0, and one set before its tile loaded reads 0.
                double ground = terrain(home.Lat, home.Lng);
                double homeAlt = double.IsNaN(ground) ? home.Alt : ground;

                reference = homeAlt;
                route.Add(new RoutePoint { Lat = home.Lat, Lng = home.Lng, Altitude = homeAlt, Label = "H", OnGround = true });
            }

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var command = (MAVLink.MAV_CMD)item.id;
                if (!IsRoutePoint(command))
                    continue;

                var last = route.Count > 0 ? route[route.Count - 1] : null;
                string label = (i + 1).ToString();

                if (command == MAVLink.MAV_CMD.RETURN_TO_LAUNCH)
                {
                    // back to home at the altitude it is flying at
                    if (home != null && last != null)
                        route.Add(new RoutePoint { Lat = home.Lat, Lng = home.Lng, Altitude = last.Altitude, Label = "RTL" });
                    continue;
                }

                // takeoffs climb where the vehicle is (their lat/lng are ignored); items at 0,0 mean "here"
                bool takeoff = command == MAVLink.MAV_CMD.TAKEOFF || command == MAVLink.MAV_CMD.VTOL_TAKEOFF;
                bool located = !takeoff && (item.lat != 0 || item.lng != 0);
                if (!located && last == null)
                    continue;

                double lat = located ? item.lat : last.Lat;
                double lng = located ? item.lng : last.Lng;

                if (reference == null)
                {
                    double ground = terrain(lat, lng);
                    reference = double.IsNaN(ground) ? 0 : ground;
                }

                if (command == MAVLink.MAV_CMD.LAND || command == MAVLink.MAV_CMD.VTOL_LAND)
                {
                    double ground = terrain(lat, lng);
                    if (double.IsNaN(ground))
                        ground = reference.Value;

                    // a vertical landing flies there first, then comes straight down
                    if ((verticalLanding || command == MAVLink.MAV_CMD.VTOL_LAND) && last != null && located)
                        route.Add(new RoutePoint { Lat = lat, Lng = lng, Altitude = last.Altitude, Item = i });

                    route.Add(new RoutePoint { Lat = lat, Lng = lng, Altitude = ground, Label = label, Item = i, OnGround = true });
                    continue;
                }

                bool followsTerrain;
                double altitude = ItemAltitude(item, lat, lng, reference.Value, terrain, out followsTerrain);
                route.Add(new RoutePoint
                {
                    Lat = lat, Lng = lng, Altitude = altitude, Label = label, Item = i, FollowsTerrain = followsTerrain
                });
            }

            return route;
        }

        static bool IsRoutePoint(MAVLink.MAV_CMD command)
        {
            switch (command)
            {
                case MAVLink.MAV_CMD.WAYPOINT:
                case MAVLink.MAV_CMD.SPLINE_WAYPOINT:
                case MAVLink.MAV_CMD.LOITER_UNLIM:
                case MAVLink.MAV_CMD.LOITER_TURNS:
                case MAVLink.MAV_CMD.LOITER_TIME:
                case MAVLink.MAV_CMD.LOITER_TO_ALT:
                case MAVLink.MAV_CMD.TAKEOFF:
                case MAVLink.MAV_CMD.VTOL_TAKEOFF:
                case MAVLink.MAV_CMD.LAND:
                case MAVLink.MAV_CMD.VTOL_LAND:
                case MAVLink.MAV_CMD.RETURN_TO_LAUNCH:
                    return true;
                default:
                    return false;
            }
        }

        static double ItemAltitude(Locationwp item, double lat, double lng, double reference,
            Func<double, double, double> terrain, out bool followsTerrain)
        {
            followsTerrain = false;
            switch ((MAVLink.MAV_FRAME)item.frame)
            {
                case MAVLink.MAV_FRAME.GLOBAL:
                case MAVLink.MAV_FRAME.GLOBAL_INT:
                    return item.alt;

                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT:
                case MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT:
                    followsTerrain = true;
                    double ground = terrain(lat, lng);
                    return (double.IsNaN(ground) ? reference : ground) + item.alt;

                default:
                    return reference + item.alt;
            }
        }

        // The rally points (RALLY_POINT items away from 0, 0), labelled R and their row. Their altitude follows the
        // frame as the mission's does; without home or a mission it counts from the ground under the first of them.
        static List<Waypoint> RallyPointsOf(IList<Locationwp> items, double? reference,
            Func<double, double, double> terrain)
        {
            var points = new List<Waypoint>();
            if (items == null)
                return points;

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.id != (ushort)MAVLink.MAV_CMD.RALLY_POINT || (item.lat == 0 && item.lng == 0))
                    continue;

                double ground = terrain(item.lat, item.lng);
                if (reference == null)
                    reference = double.IsNaN(ground) ? 0 : ground;

                bool followsTerrain;
                points.Add(new Waypoint
                {
                    Label = "R" + (i + 1), Item = i, Lat = item.lat, Lng = item.lng, Terrain = ground,
                    Altitude = ItemAltitude(item, item.lat, item.lng, reference.Value, terrain, out followsTerrain)
                });
            }

            return points;
        }

        // Gives each spline waypoint's leg the curve the map draws into it (MissionSegmentizer, as WPOverlay2 draws
        // the mission), so the ground is sampled under where the vehicle flies. A leg the map has no curve for stays
        // straight.
        static void FollowSplines(List<RoutePoint> route, ProfileInput input)
        {
            if (!route.Exists(a => a.Label != null && a.Item >= 0 &&
                                   input.Mission[a.Item].id == (ushort)MAVLink.MAV_CMD.SPLINE_WAYPOINT))
                return;

            var curves = new Dictionary<int, List<PointLatLngAlt>>();
            try
            {
                var graph = MissionGraph.Create(input.Home ?? new PointLatLngAlt(), input.Mission);
                foreach (var segment in MissionSegmentizer.BuildSegments(graph, input.VehicleClass, input.LoiterRadius))
                {
                    if (segment.Kind == SegmentKind.Spline && (segment.Flags & SegmentFlags.Alternate) == 0 &&
                        segment.EndNode != null && segment.Path != null && !curves.ContainsKey(segment.EndNode.MissionIndex))
                        curves[segment.EndNode.MissionIndex] = segment.Path;
                }
            }
            catch (Exception)
            {
                // a plan the map can't curve is drawn straight
                return;
            }

            for (int i = 1; i < route.Count; i++)
            {
                List<PointLatLngAlt> curve;
                if (route[i].Label != null && route[i].Item >= 0 && curves.TryGetValue(route[i].Item, out curve) &&
                    curve.Count > 2)
                    route[i].Curve = curve.GetRange(1, curve.Count - 2);
            }
        }

        void SampleRoute(List<RoutePoint> route, Func<double, double, double> terrain)
        {
            var distances = new double[route.Count];
            for (int i = 1; i < route.Count; i++)
                distances[i] = distances[i - 1] + LegLength(route[i - 1], route[i]);
            Length = distances[route.Count - 1];
            if (Length <= 0)
                return;

            for (int i = 0; i < route.Count; i++)
            {
                if (route[i].Rally != null)
                    route[i].Rally.Distance = distances[i];

                if (route[i].Label != null)
                {
                    Waypoints.Add(new Waypoint
                    {
                        Label = route[i].Label, Item = route[i].Label == "RTL" ? -1 : route[i].Item, Lat = route[i].Lat,
                        Lng = route[i].Lng, Distance = distances[i], Altitude = route[i].Altitude,
                        Terrain = terrain(route[i].Lat, route[i].Lng), OnGround = route[i].OnGround
                    });
                }
            }

            // where the route touches the ground, by distance
            var contacts = new List<double>();
            for (int i = 0; i < route.Count; i++)
            {
                if (route[i].OnGround)
                    contacts.Add(distances[i]);
            }

            double spacing = Math.Max(Length / MaxSamples, Math.Min(MaxSpacing, Length / MinSamples));
            for (int leg = 1; leg < route.Count; leg++)
            {
                var a = route[leg - 1];
                var b = route[leg];
                double legLength = distances[leg] - distances[leg - 1];
                int steps = Math.Max(1, (int)Math.Ceiling(legLength / spacing));
                bool enRoute = !RallyChain && !a.OnGround && !b.OnGround;
                var walker = b.Curve != null ? new LegWalker(a, b) : null;

                // a terrain-frame leg holds the height above the ground, eased from the start's to the end's
                double aboveA = double.NaN, aboveB = double.NaN;
                if (b.FollowsTerrain)
                {
                    aboveA = a.Altitude - terrain(a.Lat, a.Lng);
                    aboveB = b.Altitude - terrain(b.Lat, b.Lng);
                }

                // the first leg adds its start; every leg adds its end, so a vertical leg is two points at one distance
                for (int k = leg == 1 ? 0 : 1; k <= steps; k++)
                {
                    double t = (double)k / steps;
                    double lat, lng;
                    if (walker != null)
                    {
                        walker.At(legLength * t, out lat, out lng);
                    }
                    else
                    {
                        lat = a.Lat + (b.Lat - a.Lat) * t;
                        lng = a.Lng + (b.Lng - a.Lng) * t;
                    }

                    double distance = distances[leg - 1] + legLength * t;
                    double ground = terrain(lat, lng);

                    double altitude = a.Altitude + (b.Altitude - a.Altitude) * t;
                    if (b.FollowsTerrain && !double.IsNaN(aboveA) && !double.IsNaN(aboveB) && !double.IsNaN(ground))
                        altitude = ground + aboveA + (aboveB - aboveA) * t;

                    bool nearContact = false;
                    foreach (var contact in contacts)
                    {
                        if (Math.Abs(distance - contact) < GroundContactMargin)
                            nearContact = true;
                    }

                    Add(new Sample
                    {
                        Distance = distance, Lat = lat, Lng = lng, Altitude = altitude, Terrain = ground,
                        Checked = !nearContact, EnRoute = enRoute
                    });
                }
            }
        }

        void Add(Sample sample)
        {
            Samples.Add(sample);

            if (double.IsNaN(sample.Terrain))
            {
                TerrainComplete = false;
                return;
            }

            if (double.IsNaN(sample.Altitude))
                return;

            if (sample.EnRoute)
                NoteClearance(sample.Clearance, sample.Distance);

            if (sample.Checked && -sample.Clearance > Conflict)
            {
                Conflict = -sample.Clearance;
                ConflictDistance = sample.Distance;
            }
        }

        void NoteClearance(double clearance, double distance)
        {
            if (double.IsNaN(clearance))
                return;
            if (double.IsNaN(MinClearance) || clearance < MinClearance)
            {
                MinClearance = clearance;
                MinClearanceDistance = distance;
            }
        }

        static double LegLength(RoutePoint a, RoutePoint b)
        {
            if (b.Curve == null)
                return Distance(a.Lat, a.Lng, b.Lat, b.Lng);

            double length = 0, lat = a.Lat, lng = a.Lng;
            foreach (var point in b.Curve)
            {
                length += Distance(lat, lng, point.Lat, point.Lng);
                lat = point.Lat;
                lng = point.Lng;
            }

            return length + Distance(lat, lng, b.Lat, b.Lng);
        }

        static double Distance(double lat1, double lng1, double lat2, double lng2)
        {
            if (lat1 == lat2 && lng1 == lng2)
                return 0;
            return new PointLatLngAlt(lat1, lng1).GetDistance(new PointLatLngAlt(lat2, lng2));
        }

        // points along a curved leg by distance from its start, asked for in increasing order
        sealed class LegWalker
        {
            readonly List<double> _lat = new List<double>(), _lng = new List<double>(), _distance = new List<double>();
            int _segment = 1;

            public LegWalker(RoutePoint from, RoutePoint to)
            {
                _lat.Add(from.Lat);
                _lng.Add(from.Lng);
                _distance.Add(0);
                foreach (var point in to.Curve)
                    Append(point.Lat, point.Lng);
                Append(to.Lat, to.Lng);
            }

            void Append(double lat, double lng)
            {
                int last = _lat.Count - 1;
                _distance.Add(_distance[last] + Distance(_lat[last], _lng[last], lat, lng));
                _lat.Add(lat);
                _lng.Add(lng);
            }

            public void At(double distance, out double lat, out double lng)
            {
                while (_segment < _lat.Count - 1 && _distance[_segment] < distance)
                    _segment++;
                double span = _distance[_segment] - _distance[_segment - 1];
                double t = span > 0 ? Math.Max(0, Math.Min(1, (distance - _distance[_segment - 1]) / span)) : 1;
                lat = _lat[_segment - 1] + (_lat[_segment] - _lat[_segment - 1]) * t;
                lng = _lng[_segment - 1] + (_lng[_segment] - _lng[_segment - 1]) * t;
            }
        }

        // puts a point off the route (a rally point) at the distance of the route's nearest point, and notes how far
        // off it is
        void PlaceOnRoute(Waypoint point)
        {
            double best = double.MaxValue;
            for (int i = 1; i < Samples.Count; i++)
            {
                var a = Samples[i - 1];
                var b = Samples[i];

                // flat metres around the start of the stretch: close enough to pick the nearest point
                double coslat = Math.Cos(a.Lat * Math.PI / 180);
                double bx = (b.Lng - a.Lng) * coslat * MetresPerDegree, by = (b.Lat - a.Lat) * MetresPerDegree;
                double px = (point.Lng - a.Lng) * coslat * MetresPerDegree, py = (point.Lat - a.Lat) * MetresPerDegree;
                double length2 = bx * bx + by * by;
                double t = length2 > 0 ? Math.Max(0, Math.Min(1, (px * bx + py * by) / length2)) : 0;
                double dx = px - t * bx, dy = py - t * by;
                double off = Math.Sqrt(dx * dx + dy * dy);
                if (off < best)
                {
                    best = off;
                    point.Distance = a.Distance + t * (b.Distance - a.Distance);
                    point.OffRoute = off;
                }
            }
        }

        // Notes which waypoints and rally points are outside the fence the vehicle will hold to, and how, as
        // ArduPilot checks it. The floor arms once the route has been above it anywhere before a point, not only at an
        // earlier waypoint: a terrain-following leg can climb over a ridge between two low ones. Rally points are
        // reached from the air, so the floor always counts for them. Home and landings are on the ground and checked
        // for neither ceiling nor floor.
        void CheckFence(PointLatLngAlt home, double? reference)
        {
            if (Fence == null || Fence.IsEmpty)
                return;

            // the ceiling, floor and circle are measured from home; limits the vehicle won't hold to are still drawn
            if (home != null && reference != null)
            {
                if (Fence.Ceiling != null)
                    FenceCeiling = reference.Value + Fence.Ceiling.Altitude;
                if (Fence.Floor != null)
                    FenceFloor = reference.Value + Fence.Floor.Altitude;
                if (Fence.EnforcesCircle && !RallyChain)
                    FindCircleCrossings(home, Fence.CircleRadius);
            }

            if (!Fence.Active)
                return;

            bool armed = false;
            int sample = 0;
            foreach (var waypoint in Waypoints)
            {
                if (waypoint.Item < 0)
                    continue;
                for (; sample < Samples.Count && Samples[sample].Distance < waypoint.Distance; sample++)
                    armed |= Samples[sample].Altitude > FenceFloor;
                Check(waypoint, home, armed);
                armed |= waypoint.Altitude > FenceFloor;
            }

            foreach (var point in RallyPoints)
                Check(point, home, true);
        }

        void Check(Waypoint point, PointLatLngAlt home, bool floorArmed)
        {
            point.Breach = Fence.Check(point.Lat, point.Lng, point.Altitude, point.OnGround, floorArmed, FenceCeiling,
                FenceFloor, home);
            if (point.Breach != FenceBreach.None)
                OutsideFence.Add(point);
        }

        // where the route crosses the circle of the radius around the centre, going out or coming back in
        void FindCircleCrossings(PointLatLngAlt centre, double radius)
        {
            var middle = new PointLatLngAlt(centre.Lat, centre.Lng);
            double previousOver = 0;
            for (int i = 0; i < Samples.Count; i++)
            {
                // metres outside the circle, negative inside
                double over = middle.GetDistance(new PointLatLngAlt(Samples[i].Lat, Samples[i].Lng)) - radius;
                if (i > 0 && (previousOver > 0) != (over > 0))
                {
                    double t = previousOver / (previousOver - over);
                    FenceCircleCrossings.Add(Samples[i - 1].Distance + (Samples[i].Distance - Samples[i - 1].Distance) * t);
                }

                previousOver = over;
            }
        }

        /// <summary>The route at a distance along it, interpolated between samples.</summary>
        public Sample At(double distance)
        {
            if (Samples.Count == 0)
                return default(Sample);
            if (distance <= Samples[0].Distance)
                return Samples[0];
            if (distance >= Samples[Samples.Count - 1].Distance)
                return Samples[Samples.Count - 1];

            // the last sample at or before the distance
            int lo = 0, hi = Samples.Count - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (Samples[mid].Distance <= distance)
                    lo = mid;
                else
                    hi = mid;
            }

            var s0 = Samples[lo];
            var s1 = Samples[hi];
            double span = s1.Distance - s0.Distance;
            double t = span > 0 ? (distance - s0.Distance) / span : 0;
            return new Sample
            {
                Distance = distance,
                Lat = s0.Lat + (s1.Lat - s0.Lat) * t,
                Lng = s0.Lng + (s1.Lng - s0.Lng) * t,
                Altitude = s0.Altitude + (s1.Altitude - s0.Altitude) * t,
                Terrain = double.IsNaN(s0.Terrain) || double.IsNaN(s1.Terrain)
                    ? double.NaN
                    : s0.Terrain + (s1.Terrain - s0.Terrain) * t,
                Checked = s0.Checked && s1.Checked,
                EnRoute = s0.EnRoute && s1.EnRoute
            };
        }

        /// <summary>The index of the first sample at or after a distance.</summary>
        public int IndexAt(double distance)
        {
            int lo = 0, hi = Samples.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (Samples[mid].Distance < distance)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            return lo;
        }
    }
}
