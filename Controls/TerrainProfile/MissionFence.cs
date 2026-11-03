using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.TerrainProfile
{
    /// <summary>Whether the vehicle's fence is on.</summary>
    public enum FenceState
    {
        Enabled,

        /// <summary>off; an RC switch or a DO_FENCE_ENABLE command can still turn it on</summary>
        Disabled,

        /// <summary>on with FENCE_ACTION 0: a breach is only reported</summary>
        ReportOnly,

        /// <summary>off until an automatic takeoff completes (FENCE_AUTOENABLE 1 or 2)</summary>
        EnablesAfterTakeoff,

        /// <summary>off until the vehicle arms (FENCE_AUTOENABLE 3)</summary>
        EnablesWhenArmed
    }

    /// <summary>How a point of the plan is outside the fence the vehicle will hold to.</summary>
    [Flags]
    public enum FenceBreach
    {
        None = 0,
        AboveCeiling = 1,

        /// <summary>below the floor once the route has been above it, which is when ArduPilot arms the floor</summary>
        BelowFloor = 2,

        /// <summary>further from home than FENCE_RADIUS</summary>
        OutsideCircle = 4,

        /// <summary>outside an inclusion polygon or circle (outside all of them with FENCE_OPTIONS bit 1)</summary>
        OutsideInclusion = 8,

        /// <summary>inside an exclusion polygon or circle</summary>
        InsideExclusion = 16
    }

    /// <summary>A fence altitude limit, in metres above home.</summary>
    public sealed class FenceLimit
    {
        public double Altitude;

        /// <summary>FENCE_TYPE includes the limit (older Plane: it is set). Whether the fence is on is the state's.</summary>
        public bool Used;
    }

    /// <summary>
    /// An area of the polygon fence: a polygon, or a circle around a point. The vehicle stays inside an inclusion
    /// area and out of an exclusion area.
    /// </summary>
    public sealed class FenceArea
    {
        public bool Inclusion;

        /// <summary>The polygon's corners in order; null for a circle.</summary>
        public List<PointLatLngAlt> Vertices;

        /// <summary>A circle's centre; null for a polygon.</summary>
        public PointLatLngAlt Centre;

        /// <summary>A circle's radius in metres.</summary>
        public double Radius;

        public bool Contains(double lat, double lng)
        {
            if (Vertices == null)
                return Centre.GetDistance(new PointLatLngAlt(lat, lng)) <= Radius;

            // a ray from the point crosses the edges an odd number of times when it is inside
            bool inside = false;
            for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
            {
                var a = Vertices[i];
                var b = Vertices[j];
                if ((a.Lat > lat) != (b.Lat > lat) &&
                    lng < (b.Lng - a.Lng) * (lat - a.Lat) / (b.Lat - a.Lat) + a.Lng)
                    inside = !inside;
            }

            return inside;
        }
    }

    /// <summary>
    /// The vehicle's fence as the terrain profile checks the plan against it: the ceiling, floor and circle from its
    /// parameters, the polygon fence from the plan's fence items, and whether the fence is on. Offline only the
    /// polygon fence is known, and an unknown state counts as on.
    /// </summary>
    public sealed class MissionFence
    {
        /// <summary>null when not connected, or the vehicle has no such parameter.</summary>
        public FenceLimit Ceiling, Floor;

        /// <summary>FENCE_RADIUS in metres around home; 0 when the vehicle has none.</summary>
        public double CircleRadius;

        /// <summary>FENCE_TYPE has the circle.</summary>
        public bool CircleUsed;

        public List<FenceArea> Areas = new List<FenceArea>();

        /// <summary>FENCE_TYPE has the polygon; true when not known, as for a plan made offline.</summary>
        public bool PolygonUsed = true;

        /// <summary>FENCE_OPTIONS bit 1: inside any one inclusion area is enough, instead of inside all of them.</summary>
        public bool InclusionUnion;

        /// <summary>null when not known (not connected).</summary>
        public FenceState? State;

        /// <summary>
        /// The vehicle will hold to its fence on this flight: the fence is on, or turns itself on at takeoff or arming.
        /// Not knowing the state counts as on.
        /// </summary>
        public bool Active => State != FenceState.Disabled && State != FenceState.ReportOnly;

        public bool Enforces(FenceLimit limit) => limit != null && limit.Used && Active;

        public bool EnforcesCircle => CircleUsed && CircleRadius > 0 && Active;

        public bool EnforcesPolygon => PolygonUsed && Areas.Count > 0 && Active;

        /// <summary>Whether there is anything of a fence to draw or check.</summary>
        public bool IsEmpty => Ceiling == null && Floor == null && CircleRadius <= 0 && Areas.Count == 0;

        /// <summary>
        /// The fence from the vehicle's parameters (<paramref name="param"/> gives null for one it doesn't have) and
        /// its own report of whether the fence is on (the geofence bit of SYS_STATUS, null when it hasn't reported a
        /// fence), with the polygon fence's <paramref name="areas"/>.
        /// </summary>
        public static MissionFence FromParameters(Func<string, double?> param, bool? reportedEnabled,
            List<FenceArea> areas)
        {
            var fence = new MissionFence { Areas = areas ?? new List<FenceArea>() };
            var type = param("FENCE_TYPE");
            int bits = type.HasValue ? (int)type.Value : 0;

            // AC_Fence (Copter, and Plane since it moved to AC_Fence): FENCE_ALT_MAX with FENCE_TYPE bit 0, and
            // FENCE_ALT_MIN with bit 3. Older Plane has FENCE_MAXALT and FENCE_MINALT, where 0 turns a limit off and
            // both are off unless the maximum is above the minimum.
            var max = param("FENCE_ALT_MAX");
            var min = param("FENCE_ALT_MIN");
            if (max.HasValue || min.HasValue)
            {
                if (max.HasValue)
                    fence.Ceiling = new FenceLimit { Altitude = max.Value, Used = (bits & 1) != 0 };
                if (min.HasValue)
                    fence.Floor = new FenceLimit { Altitude = min.Value, Used = (bits & 8) != 0 };
            }
            else
            {
                var oldMax = param("FENCE_MAXALT");
                var oldMin = param("FENCE_MINALT");
                bool ordered = (oldMax ?? 0) > (oldMin ?? 0);
                if (oldMax.HasValue)
                    fence.Ceiling = new FenceLimit { Altitude = oldMax.Value, Used = oldMax.Value != 0 && ordered };
                if (oldMin.HasValue)
                    fence.Floor = new FenceLimit { Altitude = oldMin.Value, Used = oldMin.Value != 0 && ordered };
            }

            // the circle is centred on home
            var radius = param("FENCE_RADIUS");
            if (radius.HasValue)
            {
                fence.CircleRadius = radius.Value;
                fence.CircleUsed = (bits & 2) != 0;
            }

            fence.PolygonUsed = !type.HasValue || (bits & 4) != 0;
            fence.InclusionUnion = ((int)(param("FENCE_OPTIONS") ?? 0) & 2) != 0;

            if (!fence.IsEmpty)
                fence.State = StateOf(param, reportedEnabled);
            return fence;
        }

        /// <summary>
        /// Whether the fence is on. A FENCE_ACTION that only reports a breach, or turns the fence off, decides first.
        /// Otherwise the vehicle's own report follows FENCE_ENABLE, an RC switch, a DO_FENCE_ENABLE command and
        /// auto-enable alike, so it decides when there is one; FENCE_ENABLE stands in otherwise.
        /// </summary>
        public static FenceState StateOf(Func<string, double?> param, bool? reportedEnabled)
        {
            var enable = param("FENCE_ENABLE");
            var action = param("FENCE_ACTION");
            var autoEnable = AutoEnableState(param);

            // older Plane, without FENCE_ENABLE, turned the fence off with FENCE_ACTION 0 and only reported with 2
            if (!enable.HasValue && action == 0)
                return FenceState.Disabled;
            if (!enable.HasValue && action == 2)
                return FenceState.ReportOnly;

            // AC_Fence's FENCE_ACTION 0 only reports a breach however the fence comes on, and SYS_STATUS then shows
            // the fence off, so only the parameters say whether it will be on
            if (enable.HasValue && action == 0)
                return enable.Value != 0 || autoEnable != FenceState.Disabled ? FenceState.ReportOnly : FenceState.Disabled;

            if (reportedEnabled ?? (!enable.HasValue || enable.Value != 0))
                return FenceState.Enabled;

            return autoEnable;
        }

        // what FENCE_AUTOENABLE does with a fence that is off
        static FenceState AutoEnableState(Func<string, double?> param)
        {
            switch ((int)(param("FENCE_AUTOENABLE") ?? 0))
            {
                case 1:
                case 2:
                    return FenceState.EnablesAfterTakeoff;
                case 3:
                    return FenceState.EnablesWhenArmed;
                default:
                    return FenceState.Disabled;
            }
        }

        /// <summary>
        /// The areas of the polygon fence among plan items, as ArduPilot loads them: a polygon's vertices follow one
        /// another, each with the polygon's vertex count in param 1, and a circle has its radius in param 1. Polygons
        /// of fewer than three vertices and circles without a radius are left out. Other items are skipped, so they
        /// don't split a polygon whose vertices they sit between.
        /// </summary>
        public static List<FenceArea> AreasOf(IList<Locationwp> items)
        {
            var areas = new List<FenceArea>();
            if (items == null)
                return areas;

            FenceArea polygon = null;
            int count = 0;
            foreach (var item in items)
            {
                var id = (MAVLink.MAV_CMD)item.id;
                if (id == MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION || id == MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_EXCLUSION)
                {
                    bool inclusion = id == MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION;
                    if (polygon == null || polygon.Inclusion != inclusion || polygon.Vertices.Count >= count)
                    {
                        count = (int)item.p1;
                        polygon = new FenceArea { Inclusion = inclusion, Vertices = new List<PointLatLngAlt>() };
                        areas.Add(polygon);
                    }

                    polygon.Vertices.Add(new PointLatLngAlt(item.lat, item.lng));
                    continue;
                }

                if (!IsFenceItem(id))
                    continue;

                polygon = null;
                if ((id == MAVLink.MAV_CMD.FENCE_CIRCLE_INCLUSION || id == MAVLink.MAV_CMD.FENCE_CIRCLE_EXCLUSION) &&
                    item.p1 > 0)
                {
                    areas.Add(new FenceArea
                    {
                        Inclusion = id == MAVLink.MAV_CMD.FENCE_CIRCLE_INCLUSION,
                        Centre = new PointLatLngAlt(item.lat, item.lng),
                        Radius = item.p1
                    });
                }
            }

            areas.RemoveAll(a => a.Vertices != null && a.Vertices.Count < 3);
            return areas;
        }

        static bool IsFenceItem(MAVLink.MAV_CMD id)
        {
            switch (id)
            {
                case MAVLink.MAV_CMD.FENCE_RETURN_POINT:
                case MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION:
                case MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_EXCLUSION:
                case MAVLink.MAV_CMD.FENCE_CIRCLE_INCLUSION:
                case MAVLink.MAV_CMD.FENCE_CIRCLE_EXCLUSION:
                case MAVLink.MAV_CMD.FENCE_HOME_CIRCLE_INCLUSION:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>How a point is outside the fence, as ArduPilot checks it; the floor only once it is armed.</summary>
        public FenceBreach Check(double lat, double lng, double altitude, bool onGround, bool floorArmed,
            double ceilingAmsl, double floorAmsl, PointLatLngAlt home)
        {
            var breach = FenceBreach.None;
            if (Enforces(Ceiling) && !onGround && altitude > ceilingAmsl)
                breach |= FenceBreach.AboveCeiling;
            if (Enforces(Floor) && floorArmed && !onGround && altitude < floorAmsl)
                breach |= FenceBreach.BelowFloor;
            if (EnforcesCircle && home != null && home.GetDistance(new PointLatLngAlt(lat, lng)) > CircleRadius)
                breach |= FenceBreach.OutsideCircle;

            if (EnforcesPolygon)
            {
                int inclusions = 0, outside = 0;
                foreach (var area in Areas)
                {
                    if (area.Inclusion)
                    {
                        inclusions++;
                        if (!area.Contains(lat, lng))
                            outside++;
                    }
                    else if (area.Contains(lat, lng))
                    {
                        breach |= FenceBreach.InsideExclusion;
                    }
                }

                if (InclusionUnion ? outside > 0 && outside == inclusions : outside > 0)
                    breach |= FenceBreach.OutsideInclusion;
            }

            return breach;
        }

        /// <summary>A text form of the fence: two with the same signature check and draw alike.</summary>
        public void AppendSignature(StringBuilder sb)
        {
            Action<double> add = v => sb.Append(v.ToString("R", CultureInfo.InvariantCulture)).Append(',');
            foreach (var limit in new[] { Ceiling, Floor })
            {
                sb.Append('|');
                if (limit != null)
                {
                    add(limit.Altitude);
                    sb.Append(limit.Used ? 'u' : '-');
                }
            }

            sb.Append('|');
            add(CircleRadius);
            sb.Append(CircleUsed ? 'u' : '-').Append(PolygonUsed ? 'p' : '-').Append(InclusionUnion ? 'n' : '-');
            foreach (var area in Areas)
            {
                sb.Append(area.Inclusion ? 'i' : 'e');
                if (area.Vertices == null)
                {
                    add(area.Centre.Lat);
                    add(area.Centre.Lng);
                    add(area.Radius);
                }
                else
                {
                    foreach (var vertex in area.Vertices)
                    {
                        add(vertex.Lat);
                        add(vertex.Lng);
                    }
                }

                sb.Append(';');
            }

            sb.Append('|').Append(State);
        }
    }
}
