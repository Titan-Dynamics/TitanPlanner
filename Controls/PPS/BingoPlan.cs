using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Controls.TerrainProfile;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.PPS
{
    public enum BingoStatus
    {
        /// <summary>Arrives with more than the bingo level to spare.</summary>
        Ok,

        /// <summary>Arrives within <see cref="BingoPlan.CloseMargin"/> % of the bingo level.</summary>
        Close,

        /// <summary>Arrives under the bingo level.</summary>
        Below,

        /// <summary>Already flown.</summary>
        Passed,

        /// <summary>The wind is stronger than the airspeed on the way there or back.</summary>
        Unreachable
    }

    public sealed class BingoRow
    {
        public string Label;

        /// <summary>The mission item's index in the list the plan was built from; -1 for RTL.</summary>
        public int Item = -1;

        public double DistanceAlongKm;
        public double DistanceHomeKm;

        /// <summary>The battery predicted on arrival, % of the PPS energy; NaN when it can't be worked out.</summary>
        public double ArrivalPercent = double.NaN;

        /// <summary>The battery needed here to get home with the margin, % of the PPS energy.</summary>
        public double BingoPercent = double.NaN;

        public double Slack => ArrivalPercent - BingoPercent;
        public BingoStatus Status;

        /// <summary>The airspeed flown into the point, m/s.</summary>
        public double Airspeed = double.NaN;

        /// <summary>
        /// The pack's voltage under cruise load on arrival (for the Now row, the voltage read), and at the bingo level
        /// turning for home; NaN without a battery model or below 0 %.
        /// </summary>
        public double ArrivalVolts = double.NaN;

        public double BingoVolts = double.NaN;
    }

    public sealed class BingoInput
    {
        /// <summary>Home first, then the route as flown (<see cref="MissionProfile.Build(ProfileInput, Func{double, double, double})"/>).</summary>
        public MissionProfile Profile;

        /// <summary>The items the profile was built from, for speed changes and loiters.</summary>
        public IList<Locationwp> Mission;

        public PpsPerformance Pps;

        /// <summary>The airspeed the mission and RTL fly at, m/s.</summary>
        public double CruiseAirspeed;

        /// <summary>The altitude RTL climbs or descends to, metres AMSL; NaN to hold the altitude it starts at.</summary>
        public double RtlAltitude = double.NaN;

        /// <summary>m/s, and the direction it blows from in degrees.</summary>
        public double WindSpeed, WindFrom;

        public double MarginPercent;

        /// <summary>Metres, for LOITER_TURNS with no radius of their own.</summary>
        public double LoiterRadius = 60;

        /// <summary>In flight: from the vehicle's position and the energy it has used, not from a full battery at home.</summary>
        public bool Live;

        public double UsedWh;

        /// <summary>The index in <see cref="Mission"/> of the item being flown to; -1 when not known.</summary>
        public int NextItem = -1;

        public double Lat, Lng, AltitudeAmsl;

        /// <summary>For the voltages; null for none.</summary>
        public BatteryModel Battery;

        /// <summary>The pack's voltage now, in flight; NaN for none.</summary>
        public double MeasuredVolts = double.NaN;
    }

    public sealed class BingoResult
    {
        public List<BingoRow> Rows { get; } = new List<BingoRow>();

        /// <summary>The vehicle now: its battery, the bingo from where it is, and the slack; null before flight.</summary>
        public BingoRow Now;

        /// <summary>The first row still to fly that arrives under bingo or can't be reached, null when none does.</summary>
        public BingoRow PointOfNoReturn;

        /// <summary>The margin is 0.</summary>
        public bool MoMode;

        /// <summary>How far from home the energy above the margin gets back from, in still air; km.</summary>
        public double RangeHomeKm = double.NaN;

        /// <summary>Why there is nothing to show; null when there is.</summary>
        public string Problem;

        /// <summary>What the numbers leave out.</summary>
        public List<string> Notes { get; } = new List<string>();
    }

    /// <summary>
    /// The bingo battery level along a mission, from a PPS export: at each waypoint, the battery predicted on arrival and
    /// the battery needed there to return straight home (RTL) with a margin to spare.
    /// </summary>
    public static class BingoPlan
    {
        /// <summary>Arriving within this many % of bingo is close.</summary>
        public const double CloseMargin = 5;

        const double G = 9.80665;

        public static BingoResult Compute(BingoInput input)
        {
            var result = new BingoResult { MoMode = input.MarginPercent <= 0 };
            var pps = input.Pps;
            var profile = input.Profile;
            if (pps == null)
            {
                result.Problem = "Load a PPS export to see the bingo levels.";
                return result;
            }

            double usable = pps.UsableWh;
            if (!(usable > 0))
            {
                result.Problem = "The PPS export doesn't give the battery energy (Range × Wh/km).";
                return result;
            }

            double cruise = input.CruiseAirspeed > 0 ? input.CruiseAirspeed : pps.CruiseSpeed;
            if (!(cruise > 0))
                cruise = pps.BestRangeSpeed;
            if (cruise < pps.MinAirspeed || cruise > pps.MaxAirspeed)
                result.Notes.Add("Cruise airspeed " + cruise.ToString("0.0") + " m/s is outside the PPS sweep; its end is used.");

            double leftWh = input.Live ? usable - Math.Max(0, input.UsedWh) : usable;
            double Percent(double wh) => wh / usable * 100.0;

            if (profile == null || profile.Waypoints.Count == 0 || profile.Waypoints[0].Label != "H")
            {
                result.Problem = "No mission with a home position to work out the bingo levels for.";
                if (input.Live)
                    result.Now = new BingoRow { Label = "Now", ArrivalPercent = Percent(leftWh) };
                return result;
            }

            var home = profile.Waypoints[0];
            var mission = input.Mission ?? new List<Locationwp>();

            // the energy to go straight home from a point, climbing or descending to the RTL altitude first
            double HomeWh(double lat, double lng, double alt, out bool unreachable)
            {
                double rtlAlt = double.IsNaN(input.RtlAltitude) ? alt : input.RtlAltitude;
                double climb = ClimbWh(pps, cruise, rtlAlt - alt);
                return climb + LegWh(pps, cruise, input.WindSpeed, input.WindFrom, lat, lng, home.Lat, home.Lng,
                    Distance(lat, lng, home.Lat, home.Lng), 0, out unreachable);
            }

            double BingoPercent(double lat, double lng, double alt, out bool unreachable)
            {
                double wh = HomeWh(lat, lng, alt, out unreachable);
                return unreachable ? double.NaN : Percent(wh) + Math.Max(0, input.MarginPercent);
            }

            // the rows, home left out (it is where the route starts)
            var points = profile.Waypoints;
            int start = 1;
            if (input.Live && input.NextItem >= 0)
            {
                int next = FirstAtOrAfter(points, mission, input.NextItem);
                if (next > 0)
                    start = next;
            }

            double speed = cruise;
            int lastItem = -1;
            double energy = 0;
            bool lost = false;
            for (int i = 1; i < points.Count; i++)
            {
                var from = points[i - 1];
                var to = points[i];

                // speed changes between the two points apply from here on
                if (to.Item >= 0)
                {
                    for (int k = lastItem + 1; k <= to.Item && k < mission.Count; k++)
                        speed = SpeedAfter(mission[k], speed, pps);
                    lastItem = to.Item;
                }

                var row = new BingoRow
                {
                    Label = to.Label,
                    Item = to.Item,
                    Airspeed = speed,
                    DistanceAlongKm = to.Distance / 1000.0,
                    DistanceHomeKm = Distance(to.Lat, to.Lng, home.Lat, home.Lng) / 1000.0
                };
                result.Rows.Add(row);

                bool unreachable = false;
                if (i < start)
                {
                    row.Status = BingoStatus.Passed;
                    row.BingoPercent = BingoPercent(to.Lat, to.Lng, to.Altitude, out _);
                    continue;
                }

                if (i == start && input.Live)
                {
                    // from where the vehicle is to the point it is flying to
                    energy = leftWh - LegWh(pps, speed, input.WindSpeed, input.WindFrom, input.Lat, input.Lng, to.Lat,
                        to.Lng, Distance(input.Lat, input.Lng, to.Lat, to.Lng), to.Altitude - input.AltitudeAmsl,
                        out unreachable);
                }
                else
                {
                    if (i == start)
                        energy = usable;
                    energy -= LegWh(pps, speed, input.WindSpeed, input.WindFrom, from.Lat, from.Lng, to.Lat, to.Lng,
                        to.Distance - from.Distance, to.Altitude - from.Altitude, out unreachable);
                }

                if (to.Item >= 0 && to.Item < mission.Count)
                    energy -= LoiterWh(mission[to.Item], speed, input.LoiterRadius, pps, result);

                lost |= unreachable;
                row.ArrivalPercent = lost ? double.NaN : Percent(energy);
                row.BingoPercent = BingoPercent(to.Lat, to.Lng, to.Altitude, out bool homeUnreachable);
                row.Status = lost || homeUnreachable ? BingoStatus.Unreachable : Status(row.Slack);

                if (result.PointOfNoReturn == null &&
                    (row.Status == BingoStatus.Below || row.Status == BingoStatus.Unreachable))
                    result.PointOfNoReturn = row;
            }

            if (input.Live)
            {
                var now = new BingoRow
                {
                    Label = "Now",
                    DistanceHomeKm = Distance(input.Lat, input.Lng, home.Lat, home.Lng) / 1000.0,
                    ArrivalPercent = Percent(leftWh),
                    BingoPercent = BingoPercent(input.Lat, input.Lng, input.AltitudeAmsl, out bool unreachable)
                };
                now.Status = unreachable ? BingoStatus.Unreachable : Status(now.Slack);
                result.Now = now;
            }

            // the voltages: on arrival at the speed flown in, and at bingo turning for home at cruise
            var battery = input.Battery;
            if (battery != null)
            {
                double homePower = pps.PowerAt(cruise);
                foreach (var row in result.Rows)
                {
                    if (row.Status != BingoStatus.Passed)
                        row.ArrivalVolts = battery.LoadedVoltage(row.ArrivalPercent, pps.PowerAt(row.Airspeed));
                    row.BingoVolts = battery.LoadedVoltage(row.BingoPercent, homePower);
                }

                if (result.Now != null)
                {
                    result.Now.ArrivalVolts = input.MeasuredVolts > 0
                        ? input.MeasuredVolts
                        : battery.LoadedVoltage(result.Now.ArrivalPercent, homePower);
                    result.Now.BingoVolts = battery.LoadedVoltage(result.Now.BingoPercent, homePower);
                }
            }

            // still air, level, at cruise
            double whPerKm = pps.PowerAt(cruise) / cruise / 3.6;
            double spare = leftWh - usable * Math.Max(0, input.MarginPercent) / 100.0;
            result.RangeHomeKm = Math.Max(0, spare) / whPerKm;

            if (input.WindSpeed > 0.5)
                result.Notes.Add("Wind " + input.WindSpeed.ToString("0.0") + " m/s from " + input.WindFrom.ToString("0") +
                                 "° is held steady along the whole route.");
            result.Notes.Add("Descents give nothing back, and turns at the waypoints aren't counted.");
            return result;
        }

        /// <summary>
        /// The first point of the route at or after a mission item; RTL points, which carry no item, count as the first
        /// RTL command after the item before them. -1 when none is.
        /// </summary>
        private static int FirstAtOrAfter(List<MissionProfile.Waypoint> points, IList<Locationwp> mission, int item)
        {
            int last = -1;
            for (int i = 1; i < points.Count; i++)
            {
                int index = points[i].Item;
                if (index < 0 && points[i].Label == "RTL")
                {
                    index = -1;
                    for (int k = last + 1; k < mission.Count; k++)
                        if (mission[k].id == (ushort) MAVLink.MAV_CMD.RETURN_TO_LAUNCH)
                        {
                            index = k;
                            break;
                        }
                }

                if (index >= item)
                    return i;
                if (index >= 0)
                    last = index;
            }

            return -1;
        }

        private static BingoStatus Status(double slack)
        {
            if (double.IsNaN(slack))
                return BingoStatus.Unreachable;
            if (slack < 0)
                return BingoStatus.Below;
            return slack < CloseMargin ? BingoStatus.Close : BingoStatus.Ok;
        }

        /// <summary>
        /// The energy of one leg in Wh: the power in level flight at the airspeed for the time the leg takes at the
        /// ground speed the wind leaves, and the climb on top. <paramref name="unreachable"/> when the wind is stronger
        /// than the airspeed along it.
        /// </summary>
        public static double LegWh(PpsPerformance pps, double airspeed, double windSpeed, double windFrom,
            double fromLat, double fromLng, double toLat, double toLng, double distance, double climb,
            out bool unreachable)
        {
            unreachable = false;
            double wh = ClimbWh(pps, airspeed, climb);
            if (distance < 1)
                return wh;

            double groundSpeed = airspeed;
            if (windSpeed > 0)
            {
                double track = Bearing(fromLat, fromLng, toLat, toLng);
                groundSpeed = GroundSpeed(airspeed, track, windSpeed, windFrom);
                if (double.IsNaN(groundSpeed) || groundSpeed <= 1)
                {
                    unreachable = true;
                    return double.PositiveInfinity;
                }
            }

            return wh + pps.PowerAt(airspeed) * (distance / groundSpeed) / 3600.0;
        }

        /// <summary>
        /// The ground speed along a track at an airspeed, heading into the wind enough to hold the track; NaN when
        /// the wind across the track is stronger than the airspeed.
        /// </summary>
        public static double GroundSpeed(double airspeed, double track, double windSpeed, double windFrom)
        {
            double toward = (windFrom + 180) * Math.PI / 180;
            double t = track * Math.PI / 180;
            double along = windSpeed * Math.Cos(toward - t);
            double across = windSpeed * Math.Sin(toward - t);
            if (Math.Abs(across) >= airspeed)
                return double.NaN;
            return Math.Sqrt(airspeed * airspeed - across * across) + along;
        }

        /// <summary>The energy of a climb on top of level flight, in Wh; a descent costs nothing extra.</summary>
        public static double ClimbWh(PpsPerformance pps, double airspeed, double climb)
        {
            if (!(climb > 0))
                return 0;
            double mass = pps.Auw > 0 ? pps.Auw : 0;
            return mass * G * climb / pps.At(airspeed).Efficiency / 3600.0;
        }

        // DO_CHANGE_SPEED's airspeed (type 0) changes the speed from there on; -1 or 0 leaves it
        private static double SpeedAfter(Locationwp item, double speed, PpsPerformance pps)
        {
            if (item.id != (ushort) MAVLink.MAV_CMD.DO_CHANGE_SPEED)
                return speed;
            if ((int) item.p1 != 0 || !(item.p2 > 0))
                return speed;
            return item.p2;
        }

        private static double LoiterWh(Locationwp item, double airspeed, double defaultRadius, PpsPerformance pps,
            BingoResult result)
        {
            switch ((MAVLink.MAV_CMD) item.id)
            {
                case MAVLink.MAV_CMD.LOITER_TIME:
                    return pps.PowerAt(airspeed) * Math.Max(0, item.p1) / 3600.0;
                case MAVLink.MAV_CMD.LOITER_TURNS:
                {
                    double radius = Math.Abs(item.p3) > 1 ? Math.Abs(item.p3) : defaultRadius;
                    double distance = 2 * Math.PI * radius * Math.Max(0, item.p1);
                    return pps.PowerAt(airspeed) * distance / airspeed / 3600.0;
                }
                case MAVLink.MAV_CMD.LOITER_UNLIM:
                    if (!result.Notes.Contains(UnlimitedLoiter))
                        result.Notes.Add(UnlimitedLoiter);
                    return 0;
                default:
                    return 0;
            }
        }

        private const string UnlimitedLoiter = "An unlimited loiter is counted as no time at all.";

        private static double Distance(double lat1, double lng1, double lat2, double lng2)
        {
            return new PointLatLngAlt(lat1, lng1).GetDistance(new PointLatLngAlt(lat2, lng2));
        }

        private static double Bearing(double lat1, double lng1, double lat2, double lng2)
        {
            return new PointLatLngAlt(lat1, lng1).GetBearing(new PointLatLngAlt(lat2, lng2));
        }
    }
}
