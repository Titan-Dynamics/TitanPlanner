using System;
using System.Collections.Generic;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.TerrainProfile
{
    /// <summary>
    /// A mission's altitude and the ground under it along its route, flown in mission order from home, for the PLAN
    /// screen's terrain profile. Altitudes are metres above sea level and distances metres along the route.
    /// </summary>
    public sealed class MissionProfile
    {
        /// <summary>Home or a mission item the route passes through.</summary>
        public sealed class Waypoint
        {
            /// <summary>"H" for home, otherwise the mission item's number as the map shows it.</summary>
            public string Label;
            public double Lat, Lng;
            public double Distance;
            public double Altitude;
        }

        /// <summary>A point along the route.</summary>
        public struct Sample
        {
            public double Distance, Lat, Lng;

            /// <summary>The vehicle's altitude.</summary>
            public double Altitude;

            /// <summary>The ground's altitude, NaN where it isn't known.</summary>
            public double Terrain;

            /// <summary>
            /// Whether the clearance here counts: false next to home and landing points, where the route meets the
            /// ground on purpose and the terrain data's few metres of error would read as a conflict.
            /// </summary>
            public bool Checked;

            public double Clearance => Altitude - Terrain;
        }

        public List<Waypoint> Waypoints { get; } = new List<Waypoint>();
        public List<Sample> Samples { get; } = new List<Sample>();
        public double Length { get; private set; }

        /// <summary>False while the ground under part of the route isn't known yet.</summary>
        public bool TerrainComplete { get; private set; } = true;

        /// <summary>The least height above the ground along the route, NaN when no ground is known.</summary>
        public double MinClearance { get; private set; } = double.NaN;

        public double MinClearanceDistance { get; private set; }

        /// <summary>Whether the route goes below the ground anywhere its clearance counts.</summary>
        public bool HasConflict => MinClearance < 0;

        // within this of where the route meets the ground, its clearance doesn't count
        const double GroundContactMargin = 30;

        sealed class RoutePoint
        {
            public double Lat, Lng, Altitude;
            public string Label;

            // the leg into this point holds a height above the ground (a terrain-frame item)
            public bool FollowsTerrain;

            // home, or where a landing touches down
            public bool OnGround;
        }

        /// <summary>
        /// Builds the profile. <paramref name="terrain"/> gives the ground's altitude at a lat/lng, NaN where it
        /// isn't known; it is called many times, so it must be quick. Landings descend vertically when
        /// <paramref name="verticalLanding"/> is set (multirotors), else glide in from the previous point. Returns
        /// null when the mission has no route to draw.
        /// </summary>
        public static MissionProfile Build(PointLatLngAlt home, IList<Locationwp> items, bool verticalLanding,
            Func<double, double, double> terrain, int sampleCount = 600)
        {
            if (items == null)
                return null;

            var route = BuildRoute(home, items, verticalLanding, terrain);
            if (route.Count < 2)
                return null;

            var profile = new MissionProfile();
            profile.SampleRoute(route, terrain, sampleCount);
            return profile.Length > 0 ? profile : null;
        }

        static List<RoutePoint> BuildRoute(PointLatLngAlt home, IList<Locationwp> items, bool verticalLanding,
            Func<double, double, double> terrain)
        {
            var route = new List<RoutePoint>();
            bool hasHome = home != null && (home.Lat != 0 || home.Lng != 0);

            // the altitude relative-frame items count from: home's, or the ground under the first item without one
            double? reference = null;
            if (hasHome)
            {
                double homeAlt = home.Alt;
                if (homeAlt == 0)
                {
                    double ground = terrain(home.Lat, home.Lng);
                    if (!double.IsNaN(ground))
                        homeAlt = ground;
                }

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
                    if (hasHome && last != null)
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
                        route.Add(new RoutePoint { Lat = lat, Lng = lng, Altitude = last.Altitude });

                    route.Add(new RoutePoint { Lat = lat, Lng = lng, Altitude = ground, Label = label, OnGround = true });
                    continue;
                }

                bool followsTerrain;
                double altitude = ItemAltitude(item, lat, lng, reference.Value, terrain, out followsTerrain);
                route.Add(new RoutePoint
                {
                    Lat = lat, Lng = lng, Altitude = altitude, Label = label, FollowsTerrain = followsTerrain
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

        void SampleRoute(List<RoutePoint> route, Func<double, double, double> terrain, int sampleCount)
        {
            var distances = new double[route.Count];
            for (int i = 1; i < route.Count; i++)
                distances[i] = distances[i - 1] + Distance(route[i - 1], route[i]);
            Length = distances[route.Count - 1];
            if (Length <= 0)
                return;

            for (int i = 0; i < route.Count; i++)
            {
                if (route[i].Label != null)
                {
                    Waypoints.Add(new Waypoint
                    {
                        Label = route[i].Label, Lat = route[i].Lat, Lng = route[i].Lng, Distance = distances[i],
                        Altitude = route[i].Altitude
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

            double spacing = Math.Max(1, Length / Math.Max(1, sampleCount));
            for (int leg = 1; leg < route.Count; leg++)
            {
                var a = route[leg - 1];
                var b = route[leg];
                double legLength = distances[leg] - distances[leg - 1];
                int steps = Math.Max(1, (int)Math.Ceiling(legLength / spacing));

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
                    double lat = a.Lat + (b.Lat - a.Lat) * t;
                    double lng = a.Lng + (b.Lng - a.Lng) * t;
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
                        Checked = !nearContact
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

            if (sample.Checked && (double.IsNaN(MinClearance) || sample.Clearance < MinClearance))
            {
                MinClearance = sample.Clearance;
                MinClearanceDistance = sample.Distance;
            }
        }

        static double Distance(RoutePoint a, RoutePoint b)
        {
            if (a.Lat == b.Lat && a.Lng == b.Lng)
                return 0;
            return new PointLatLngAlt(a.Lat, a.Lng).GetDistance(new PointLatLngAlt(b.Lat, b.Lng));
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
                Checked = s0.Checked && s1.Checked
            };
        }
    }
}
