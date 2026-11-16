using System;
using System.Collections.Generic;
using System.Globalization;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// The inputs of a fixed wing autoland sequence. Distances and altitudes are in meters, angles in degrees.
    /// </summary>
    public class AutolandParams
    {
        /// <summary>The direction of travel on final, towards and past LAND.</summary>
        public double HeadingDeg;

        /// <summary>The altitude of the loiter (DO_LAND_START, LOITER_TO_ALT, LOITER_TIME) and the downwind start.</summary>
        public double HoldAltM = AutolandPattern.DefaultHoldAltM;

        /// <summary>The slope from the end of the base leg down to the start of the landing zone.</summary>
        public double PreApproachDeg = AutolandPattern.DefaultPreApproachDeg;

        /// <summary>The slope from the start of the landing zone (the box's near short end) down to LAND.</summary>
        public double FinalApproachDeg = AutolandPattern.DefaultFinalApproachDeg;

        /// <summary>LOITER_TIME's time, in seconds.</summary>
        public double LoiterTimeS = AutolandPattern.DefaultLoiterTimeS;

        /// <summary>How far the downwind leg runs to the right of the final approach line.</summary>
        public double DownwindOffsetM = AutolandPattern.DefaultDownwindOffsetM;

        /// <summary>The landing box's length along the runway; LAND is in its middle.</summary>
        public double BoxLengthM = AutolandPattern.DefaultBoxLengthM;

        /// <summary>The landing box's width across the runway. Only drawn: no row carries it.</summary>
        public double BoxWidthM = AutolandPattern.DefaultBoxWidthM;

        /// <summary>The radius the loiter is placed with (the vehicle's WP_LOITER_RAD).</summary>
        public double LoiterRadiusM = AutolandPattern.DefaultLoiterRadiusM;

        /// <summary>
        /// Write the radius into the loiter rows (positive, so clockwise) instead of 0. Needed when the vehicle's own
        /// loiter radius is negative, which would loiter anticlockwise and miss the tangent onto downwind.
        /// </summary>
        public bool WriteClockwiseRadius;

        /// <summary>LAND's altitude.</summary>
        public double LandAltM;

        /// <summary>The altitude frame of every row (3 = relative to home).</summary>
        public byte Frame = (byte) MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT;

        public AutolandParams Clone()
        {
            return (AutolandParams) MemberwiseClone();
        }
    }

    /// <summary>
    /// What the map can drag on an autoland sequence.
    /// </summary>
    public enum AutolandHandle
    {
        /// <summary>Clicks select the sequence; dragging does nothing.</summary>
        None,

        /// <summary>LAND: moves the whole sequence.</summary>
        Move,

        /// <summary>The end of the base leg on the extended centerline: turns the sequence about LAND.</summary>
        Rotate,

        /// <summary>A corner of the landing box: sets its length and width.</summary>
        Corner,

        /// <summary>The middle of the downwind leg: sets the downwind offset.</summary>
        Offset
    }

    /// <summary>
    /// A fixed wing autoland sequence laid out on the ground: its eight mission rows and the shapes the map draws.
    /// </summary>
    public class AutolandLayout
    {
        /// <summary>The inputs after clamping, so the rows were made from exactly these.</summary>
        public AutolandParams Params;

        public PointLatLngAlt Land;

        /// <summary>DO_LAND_START, LOITER_TO_ALT, LOITER_TIME, four WAYPOINTs (downwind start, downwind end, base end,
        /// landing zone start) and LAND, in meters.</summary>
        public List<Locationwp> Rows = new List<Locationwp>();

        /// <summary>The altitude at the landing zone start (the box's near short end).</summary>
        public double ZoneAltM;

        /// <summary>The altitude of the flat base leg.</summary>
        public double BaseAltM;

        /// <summary>How far before LAND the base leg crosses the extended centerline.</summary>
        public double BaseDistanceM;

        /// <summary>The obstacle height the base leg clears by <see cref="AutolandPattern.ObstacleMarginM"/>.</summary>
        public double ObstacleHeightM => BaseAltM - AutolandPattern.ObstacleMarginM;

        public PointLatLngAlt LoiterCenter;
        public PointLatLngAlt DownwindStart;
        public PointLatLngAlt DownwindEnd;
        public PointLatLngAlt BaseEnd;
        public PointLatLngAlt ZoneStart;
        public PointLatLngAlt DownwindMiddle;

        /// <summary>The middle of the base leg, where the map shows the obstacle height it clears.</summary>
        public PointLatLngAlt BaseMiddle;

        /// <summary>
        /// Evenly spaced points from the base leg's end to the landing zone start, each at the altitude the descent
        /// passes it. Only drawn on the PLAN map: they are not mission rows.
        /// </summary>
        public List<PointLatLngAlt> ApproachPoints = new List<PointLatLngAlt>();

        /// <summary>The pre-approach angle is above <see cref="AutolandPattern.SteepPreApproachDeg"/>, to the 0.1 deg
        /// the panel shows (an angle read back from the rows carries a little rounding).</summary>
        public bool PreApproachSteep =>
            Math.Round(Params.PreApproachDeg, 1) > AutolandPattern.SteepPreApproachDeg;

        /// <summary>The final approach angle is above <see cref="AutolandPattern.SteepFinalApproachDeg"/>, to 0.1 deg.
        /// </summary>
        public bool FinalApproachSteep =>
            Math.Round(Params.FinalApproachDeg, 1) > AutolandPattern.SteepFinalApproachDeg;

        /// <summary>
        /// A point beside <paramref name="point"/>, <paramref name="meters"/> outside the circuit: to the left of the
        /// final approach line, or before the base leg. The map puts a point's tag on that side.
        /// </summary>
        public PointLatLngAlt Outside(PointLatLngAlt point, bool beforeBase, double meters = 50)
        {
            ToLocal(point, out var along, out var right);
            return beforeBase
                ? AutolandPattern.FromLocal(Land, Params.HeadingDeg, along - meters, right, point.Alt)
                : AutolandPattern.FromLocal(Land, Params.HeadingDeg, along, right - meters, point.Alt);
        }

        /// <summary>Inputs that make the sequence but are worth a second look (steep angles), for the panel. Unlike
        /// <see cref="Warnings"/> nothing was changed for them.</summary>
        public List<string> Advisories = new List<string>();

        /// <summary>The landing box: near left, near right, far right, far left (near is the approach end).</summary>
        public PointLatLngAlt[] BoxCorners = new PointLatLngAlt[4];

        /// <summary>Why an input was changed to make the sequence, for the panel.</summary>
        public List<string> Warnings = new List<string>();

        /// <summary>A point's place in the sequence's frame: meters along the landing heading from LAND (positive past
        /// touchdown) and meters to the right of it.</summary>
        public void ToLocal(PointLatLngAlt point, out double along, out double right)
        {
            AutolandPattern.ToLocal(Land, Params.HeadingDeg, point, out along, out right);
        }

        /// <summary>Whether the point is inside the landing box.</summary>
        public bool BoxContains(PointLatLngAlt point)
        {
            ToLocal(point, out var along, out var right);
            return Math.Abs(along) <= Params.BoxLengthM / 2 && Math.Abs(right) <= Params.BoxWidthM / 2;
        }

        /// <summary>The handle a row of the sequence stands for on the map (its index from DO_LAND_START).</summary>
        public static AutolandHandle HandleForRow(int rowInSequence)
        {
            switch (rowInSequence)
            {
                case AutolandPattern.RowLand:
                    return AutolandHandle.Move;
                case AutolandPattern.RowBaseEnd:
                    return AutolandHandle.Rotate;
                default:
                    return AutolandHandle.None;
            }
        }
    }

    /// <summary>
    /// A fixed wing autoland sequence, as TitanGroundControl's landing pattern but with the whole circuit: a loiter
    /// to the hold altitude past the far end of the landing box, a downwind leg to the right of the final approach line
    /// that starts where the loiter touches it, a flat base leg, and a final approach that crosses the box's near short
    /// end and lands in its middle.
    ///
    /// A third of the descent from the hold altitude to the landing zone start is flown on downwind; the rest is flown
    /// from the end of the base leg at the pre-approach angle, which sets how far out the base leg is. The final
    /// approach angle sets the landing zone start's height above LAND.
    ///
    /// The sequence is plain mission rows: <see cref="FindAll"/> recognizes it again in a mission, from its commands
    /// and its geometry, so it survives saving, loading, undo and a round trip through the vehicle.
    /// </summary>
    public static class AutolandPattern
    {
        public const double DefaultHoldAltM = 50;
        public const double DefaultPreApproachDeg = 4.5;

        /// <summary>A pre-approach angle above this is warned about as steep.</summary>
        public const double SteepPreApproachDeg = 6;

        /// <summary>A final approach angle above this is warned about as steep.</summary>
        public const double SteepFinalApproachDeg = 3;

        /// <summary>How many altitude points the map shows between the base leg's end and the landing zone start.</summary>
        public const int ApproachPointCount = 3;
        public const double DefaultFinalApproachDeg = 3;
        public const double DefaultLoiterTimeS = 60;
        public const double DefaultObstacleHeightM = 25;

        /// <summary>How far above the obstacle height the base leg is flown.</summary>
        public const double ObstacleMarginM = 10;
        public const double DefaultDownwindOffsetM = 170;
        public const double DefaultBoxLengthM = 175;
        public const double DefaultBoxWidthM = 25;
        public const double DefaultLoiterRadiusM = 60;

        /// <summary>The share of the descent flown on downwind.</summary>
        public const double DownwindShare = 1.0 / 3.0;

        public const double MinBoxLengthM = 40;
        public const double MinBoxWidthM = 5;
        public const double MinAngleDeg = 0.5;
        public const double MaxAngleDeg = 30;

        /// <summary>The least height of the hold above the landing zone start.</summary>
        public const double MinDescentM = 1;

        /// <summary>How far a row may sit from where the sequence puts it and still be recognized.</summary>
        public const double MatchDistanceM = 1.5;

        public const double MatchAltM = 0.5;
        public const double MatchParam = 0.01;

        public const int RowCount = 8;
        public const int RowLandStart = 0;
        public const int RowLoiterToAlt = 1;
        public const int RowLoiterTime = 2;
        public const int RowDownwindStart = 3;
        public const int RowDownwindEnd = 4;
        public const int RowBaseEnd = 5;
        public const int RowZoneStart = 6;
        public const int RowLand = 7;

        static readonly ushort[] Commands =
        {
            (ushort) MAVLink.MAV_CMD.DO_LAND_START,
            (ushort) MAVLink.MAV_CMD.LOITER_TO_ALT,
            (ushort) MAVLink.MAV_CMD.LOITER_TIME,
            (ushort) MAVLink.MAV_CMD.WAYPOINT,
            (ushort) MAVLink.MAV_CMD.WAYPOINT,
            (ushort) MAVLink.MAV_CMD.WAYPOINT,
            (ushort) MAVLink.MAV_CMD.WAYPOINT,
            (ushort) MAVLink.MAV_CMD.LAND,
        };

        // a flat frame about LAND, the same both ways, so a sequence read back and laid out again lands on itself
        const double EarthRadiusM = 6378137.0;

        public static void ToLocal(PointLatLngAlt origin, double headingDeg, PointLatLngAlt point, out double along,
            out double right)
        {
            double north = (point.Lat - origin.Lat) * MathHelper.deg2rad * EarthRadiusM;
            double east = (point.Lng - origin.Lng) * MathHelper.deg2rad * EarthRadiusM *
                          Math.Cos(origin.Lat * MathHelper.deg2rad);
            double h = headingDeg * MathHelper.deg2rad;
            along = north * Math.Cos(h) + east * Math.Sin(h);
            right = -north * Math.Sin(h) + east * Math.Cos(h);
        }

        public static PointLatLngAlt FromLocal(PointLatLngAlt origin, double headingDeg, double along, double right,
            double alt)
        {
            double h = headingDeg * MathHelper.deg2rad;
            double north = along * Math.Cos(h) - right * Math.Sin(h);
            double east = along * Math.Sin(h) + right * Math.Cos(h);
            double lat = origin.Lat + north / EarthRadiusM * MathHelper.rad2deg;
            double lng = origin.Lng + east / (EarthRadiusM * Math.Cos(origin.Lat * MathHelper.deg2rad)) *
                         MathHelper.rad2deg;
            return new PointLatLngAlt(lat, lng, alt);
        }

        static double Wrap360(double deg)
        {
            deg %= 360;
            return deg < 0 ? deg + 360 : deg;
        }

        /// <summary>The landing zone start's altitude for these inputs.</summary>
        public static double ZoneAlt(AutolandParams p)
        {
            double length = Math.Max(MinBoxLengthM, p.BoxLengthM);
            double angle = Math.Min(MaxAngleDeg, Math.Max(MinAngleDeg, p.FinalApproachDeg));
            return p.LandAltM + length / 2 * Math.Tan(angle * MathHelper.deg2rad);
        }

        /// <summary>
        /// The hold altitude that puts the base leg <see cref="ObstacleMarginM"/> above obstacles of this height, with
        /// a third of the descent flown on downwind as always.
        /// </summary>
        public static double HoldForObstacle(AutolandParams p, double obstacleM)
        {
            double baseAlt = Math.Max(0, obstacleM) + ObstacleMarginM;
            double zone = ZoneAlt(p);
            return (baseAlt - zone * DownwindShare) / (1 - DownwindShare);
        }

        /// <summary>
        /// Lays the sequence out about LAND. Inputs out of range are clamped, and the layout says which.
        /// </summary>
        public static AutolandLayout Generate(PointLatLngAlt land, AutolandParams input)
        {
            var p = input.Clone();
            var layout = new AutolandLayout {Params = p};

            p.HeadingDeg = Wrap360(p.HeadingDeg);

            if (!(p.LoiterRadiusM > 0))
                p.LoiterRadiusM = DefaultLoiterRadiusM;

            if (!(p.BoxLengthM >= MinBoxLengthM))
            {
                p.BoxLengthM = MinBoxLengthM;
                layout.Warnings.Add($"Box length raised to {MinBoxLengthM:0} m");
            }

            if (!(p.BoxWidthM >= MinBoxWidthM))
            {
                p.BoxWidthM = MinBoxWidthM;
                layout.Warnings.Add($"Box width raised to {MinBoxWidthM:0} m");
            }

            p.PreApproachDeg = ClampAngle(p.PreApproachDeg, "Pre-approach angle", layout.Warnings);
            p.FinalApproachDeg = ClampAngle(p.FinalApproachDeg, "Final approach angle", layout.Warnings);

            if (!(p.LoiterTimeS >= 0))
                p.LoiterTimeS = 0;

            if (!(p.DownwindOffsetM >= p.LoiterRadiusM))
            {
                p.DownwindOffsetM = p.LoiterRadiusM;
                layout.Warnings.Add($"Downwind offset raised to the loiter radius ({p.LoiterRadiusM:0} m)");
            }

            double half = p.BoxLengthM / 2;
            double zoneAlt = p.LandAltM + half * Math.Tan(p.FinalApproachDeg * MathHelper.deg2rad);

            if (!(p.HoldAltM >= zoneAlt + MinDescentM))
            {
                p.HoldAltM = Math.Ceiling(zoneAlt + MinDescentM);
                layout.Warnings.Add($"Hold altitude raised to {p.HoldAltM:0} m, above the landing zone start");
            }

            double descent = p.HoldAltM - zoneAlt;
            double baseAlt = p.HoldAltM - descent * DownwindShare;
            double baseDistance = half + (baseAlt - zoneAlt) / Math.Tan(p.PreApproachDeg * MathHelper.deg2rad);

            double r = p.LoiterRadiusM;
            double d = p.DownwindOffsetM;
            double h = p.HeadingDeg;

            PointLatLngAlt At(double along, double right, double alt) => FromLocal(land, h, along, right, alt);

            layout.Land = new PointLatLngAlt(land.Lat, land.Lng, p.LandAltM);
            layout.ZoneAltM = zoneAlt;
            layout.BaseAltM = baseAlt;
            layout.BaseDistanceM = baseDistance;
            layout.LoiterCenter = At(half, d - r, p.HoldAltM);
            layout.DownwindStart = At(half, d, p.HoldAltM);
            layout.DownwindEnd = At(-baseDistance, d, baseAlt);
            layout.BaseEnd = At(-baseDistance, 0, baseAlt);
            layout.ZoneStart = At(-half, 0, zoneAlt);
            layout.DownwindMiddle = At((half - baseDistance) / 2, d, (p.HoldAltM + baseAlt) / 2);
            layout.BaseMiddle = At(-baseDistance, d / 2, baseAlt);

            for (int i = 1; i <= ApproachPointCount; i++)
            {
                double f = (double) i / (ApproachPointCount + 1);
                layout.ApproachPoints.Add(At(-baseDistance + (baseDistance - half) * f, 0,
                    baseAlt + (zoneAlt - baseAlt) * f));
            }

            if (layout.PreApproachSteep)
                layout.Advisories.Add(
                    $"Pre-approach angle is steeper than {SteepPreApproachDeg:0.#} deg ({p.PreApproachDeg:0.0} deg)");
            if (layout.FinalApproachSteep)
                layout.Advisories.Add(
                    $"Final approach angle is steeper than {SteepFinalApproachDeg:0.#} deg ({p.FinalApproachDeg:0.0} deg)");

            double w = p.BoxWidthM / 2;
            layout.BoxCorners[0] = At(-half, -w, p.LandAltM);
            layout.BoxCorners[1] = At(-half, w, p.LandAltM);
            layout.BoxCorners[2] = At(half, w, p.LandAltM);
            layout.BoxCorners[3] = At(half, -w, p.LandAltM);

            float radius = p.WriteClockwiseRadius ? (float) r : 0;

            layout.Rows.Add(Row(MAVLink.MAV_CMD.DO_LAND_START, 0, 0, 0, 0, layout.LoiterCenter, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.LOITER_TO_ALT, 1, radius, 0, 0, layout.LoiterCenter, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.LOITER_TIME, (float) p.LoiterTimeS, 1, radius, 0, layout.LoiterCenter,
                p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, layout.DownwindStart, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, layout.DownwindEnd, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, layout.BaseEnd, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.WAYPOINT, 0, 0, 0, 0, layout.ZoneStart, p.Frame));
            layout.Rows.Add(Row(MAVLink.MAV_CMD.LAND, 0, 0, 0, 0, layout.Land, p.Frame));

            return layout;
        }

        static double ClampAngle(double deg, string name, List<string> warnings)
        {
            if (deg >= MinAngleDeg && deg <= MaxAngleDeg)
                return deg;
            double clamped = deg > MaxAngleDeg ? MaxAngleDeg : MinAngleDeg;
            warnings.Add($"{name} limited to {clamped:0.#} deg");
            return clamped;
        }

        static Locationwp Row(MAVLink.MAV_CMD cmd, float p1, float p2, float p3, float p4, PointLatLngAlt at,
            byte frame)
        {
            return new Locationwp
            {
                id = (ushort) cmd,
                p1 = p1,
                p2 = p2,
                p3 = p3,
                p4 = p4,
                lat = at.Lat,
                lng = at.Lng,
                alt = (float) Math.Round(at.Alt, 2),
                frame = frame
            };
        }

        /// <summary>
        /// Reads the sequence starting at <paramref name="start"/> back into its inputs, or null when those rows aren't
        /// one: the commands must be the sequence's, and the rows must sit where the inputs read from them would put
        /// them (so a sequence edited by hand is left as plain rows). The box width isn't in the rows, so it is given.
        /// </summary>
        public static AutolandLayout TryRecognize(IList<Locationwp> cmds, int start, double boxWidthM)
        {
            if (cmds == null || start < 0 || start + RowCount > cmds.Count)
                return null;

            for (int i = 0; i < RowCount; i++)
            {
                if (cmds[start + i].id != Commands[i] || cmds[start + i].frame != cmds[start].frame)
                    return null;
            }

            var land = cmds[start + RowLand];
            var landPoint = new PointLatLngAlt(land.lat, land.lng, land.alt);
            var zone = cmds[start + RowZoneStart];
            var zonePoint = new PointLatLngAlt(zone.lat, zone.lng, zone.alt);

            // the landing heading runs from the landing zone start to LAND
            ToLocal(landPoint, 0, zonePoint, out var zoneNorth, out var zoneEast);
            double half = Math.Sqrt(zoneNorth * zoneNorth + zoneEast * zoneEast);
            if (half < MinBoxLengthM / 2 - MatchDistanceM)
                return null;
            double heading = Wrap360(Math.Atan2(-zoneEast, -zoneNorth) * MathHelper.rad2deg);

            double zoneHeight = zone.alt - land.alt;
            if (!(zoneHeight > 0))
                return null;

            var loiter = cmds[start + RowLoiterToAlt];
            var downwindStart = cmds[start + RowDownwindStart];
            var baseEnd = cmds[start + RowBaseEnd];

            ToLocal(landPoint, heading, new PointLatLngAlt(loiter.lat, loiter.lng, 0), out _, out var loiterRight);
            ToLocal(landPoint, heading, new PointLatLngAlt(downwindStart.lat, downwindStart.lng, 0), out _,
                out var offset);
            ToLocal(landPoint, heading, new PointLatLngAlt(baseEnd.lat, baseEnd.lng, 0), out var baseAlong, out _);

            double radius = offset - loiterRight;
            double baseDistance = -baseAlong;
            double hold = loiter.alt;
            double descent = hold - zone.alt;
            if (!(radius > 0) || !(baseDistance > half) || !(descent > 0))
                return null;

            var p = new AutolandParams
            {
                HeadingDeg = heading,
                HoldAltM = hold,
                FinalApproachDeg = Math.Atan(zoneHeight / half) * MathHelper.rad2deg,
                PreApproachDeg = Math.Atan(descent * (1 - DownwindShare) / (baseDistance - half)) *
                                 MathHelper.rad2deg,
                LoiterTimeS = cmds[start + RowLoiterTime].p1,
                DownwindOffsetM = offset,
                BoxLengthM = half * 2,
                BoxWidthM = boxWidthM > 0 ? boxWidthM : DefaultBoxWidthM,
                LoiterRadiusM = radius,
                WriteClockwiseRadius = loiter.p2 > 0,
                LandAltM = land.alt,
                Frame = cmds[start].frame
            };

            var layout = Generate(landPoint, p);
            if (layout.Warnings.Count > 0)
                return null;

            for (int i = 0; i < RowCount; i++)
            {
                if (!Matches(layout.Rows[i], cmds[start + i]))
                    return null;
            }

            return layout;
        }

        static bool Matches(Locationwp expected, Locationwp actual)
        {
            if (Math.Abs(expected.p1 - actual.p1) > MatchParam || Math.Abs(expected.p2 - actual.p2) > MatchParam ||
                Math.Abs(expected.p3 - actual.p3) > MatchParam || Math.Abs(expected.p4 - actual.p4) > MatchParam)
            {
                // the loiter rows' radius is read back from where the loiter is, so give it the distance tolerance
                bool radiusRow = expected.id == (ushort) MAVLink.MAV_CMD.LOITER_TO_ALT ||
                                 expected.id == (ushort) MAVLink.MAV_CMD.LOITER_TIME;
                if (!radiusRow || Math.Abs(expected.p1 - actual.p1) > MatchParam ||
                    Math.Abs(expected.p4 - actual.p4) > MatchParam ||
                    Math.Abs(expected.p2 - actual.p2) > MatchDistanceM ||
                    Math.Abs(expected.p3 - actual.p3) > MatchDistanceM)
                    return false;
            }

            if (Math.Abs(expected.alt - actual.alt) > MatchAltM)
                return false;

            var e = new PointLatLngAlt(expected.lat, expected.lng, 0);
            ToLocal(e, 0, new PointLatLngAlt(actual.lat, actual.lng, 0), out var north, out var east);
            return Math.Sqrt(north * north + east * east) <= MatchDistanceM;
        }

        /// <summary>How far apart, in degrees, two fingerprints' coordinates may be and still match (about 20 cm):
        /// more than a .waypoints file's or the vehicle's rounding, less than any real move.</summary>
        public const double FingerprintToleranceDeg = 2e-6;

        /// <summary>
        /// What identifies the sequence starting at <paramref name="start"/> once it has been turned into plain
        /// waypoints: where its DO_LAND_START and its LAND are. Compare them with <see cref="SameFingerprint"/>, which
        /// allows for the rounding of a file or the vehicle. A sequence laid out anywhere else, or moved, has another.
        /// </summary>
        public static string Fingerprint(IList<Locationwp> cmds, int start)
        {
            if (cmds == null || start < 0 || start + RowCount > cmds.Count)
                return "";

            string At(Locationwp wp) => wp.lat.ToString("F8", CultureInfo.InvariantCulture) + "," +
                                        wp.lng.ToString("F8", CultureInfo.InvariantCulture);

            return At(cmds[start + RowLandStart]) + "|" + At(cmds[start + RowLand]);
        }

        /// <summary>Whether two <see cref="Fingerprint"/>s are the same sequence, to
        /// <see cref="FingerprintToleranceDeg"/>.</summary>
        public static bool SameFingerprint(string a, string b)
        {
            double[] Parse(string s)
            {
                var parts = (s ?? "").Split(',', '|');
                if (parts.Length != 4)
                    return null;
                var values = new double[4];
                for (int i = 0; i < 4; i++)
                    if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                        return null;
                return values;
            }

            var x = Parse(a);
            var y = Parse(b);
            if (x == null || y == null)
                return false;
            for (int i = 0; i < 4; i++)
                if (Math.Abs(x[i] - y[i]) > FingerprintToleranceDeg)
                    return false;
            return true;
        }

        /// <summary>
        /// Every autoland sequence in a mission, by the index of its DO_LAND_START. boxWidth gives the width to draw a
        /// sequence's box with, from its LAND point (null for the default). Sequences whose
        /// <see cref="Fingerprint"/> <paramref name="converted"/> holds were turned into plain waypoints and are left
        /// as such.
        /// </summary>
        public static List<KeyValuePair<int, AutolandLayout>> FindAll(IList<Locationwp> cmds,
            Func<PointLatLngAlt, double?> boxWidth = null, ICollection<string> converted = null)
        {
            var found = new List<KeyValuePair<int, AutolandLayout>>();
            if (cmds == null)
                return found;

            for (int i = 0; i + RowCount <= cmds.Count; i++)
            {
                if (cmds[i].id != Commands[0] || cmds[i + RowLand].id != Commands[RowLand])
                    continue;

                if (converted != null && converted.Count > 0)
                {
                    var fingerprint = Fingerprint(cmds, i);
                    bool isConverted = false;
                    foreach (var f in converted)
                        isConverted |= SameFingerprint(f, fingerprint);
                    if (isConverted)
                        continue;
                }

                var land = cmds[i + RowLand];
                double width = boxWidth?.Invoke(new PointLatLngAlt(land.lat, land.lng, land.alt)) ??
                               DefaultBoxWidthM;
                var layout = TryRecognize(cmds, i, width);
                if (layout == null)
                    continue;

                found.Add(new KeyValuePair<int, AutolandLayout>(i, layout));
                i += RowCount - 1;
            }

            return found;
        }

        /// <summary>
        /// The sequence's inputs and LAND after dragging one of its handles to <paramref name="to"/>.
        /// </summary>
        public static AutolandParams Drag(AutolandLayout layout, AutolandHandle handle, PointLatLngAlt to,
            out PointLatLngAlt land)
        {
            var p = layout.Params.Clone();
            land = layout.Land;

            layout.ToLocal(to, out var along, out var right);

            switch (handle)
            {
                case AutolandHandle.Move:
                    land = new PointLatLngAlt(to.Lat, to.Lng, layout.Land.Alt);
                    break;
                case AutolandHandle.Rotate:
                    // the handle stays on the extended centerline, before LAND
                    ToLocal(layout.Land, 0, to, out var north, out var east);
                    if (north * north + east * east > 1)
                        p.HeadingDeg = Wrap360(Math.Atan2(-east, -north) * MathHelper.rad2deg);
                    break;
                case AutolandHandle.Corner:
                    p.BoxLengthM = Math.Max(MinBoxLengthM, Math.Abs(along) * 2);
                    p.BoxWidthM = Math.Max(MinBoxWidthM, Math.Abs(right) * 2);
                    break;
                case AutolandHandle.Offset:
                    p.DownwindOffsetM = Math.Max(p.LoiterRadiusM, right);
                    break;
            }

            return p;
        }

        /// <summary>
        /// The landing heading a new sequence is given: along the takeoff, from home to the first mission point
        /// at least 30 m from it, else north.
        /// </summary>
        public static double TakeoffHeading(PointLatLngAlt home, IEnumerable<Locationwp> cmds)
        {
            if (home == null || (home.Lat == 0 && home.Lng == 0) || cmds == null)
                return 0;

            foreach (var cmd in cmds)
            {
                if (cmd.lat == 0 && cmd.lng == 0)
                    continue;
                var point = new PointLatLngAlt(cmd.lat, cmd.lng, 0);
                if (home.GetDistance(point) >= 30)
                    return home.GetBearing(point);
            }

            return 0;
        }
    }
}
