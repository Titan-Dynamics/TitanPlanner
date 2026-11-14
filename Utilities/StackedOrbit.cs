using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// A stacked orbit: several aircraft circling one point, one above the other from a floor altitude up by a fixed
    /// separation. The lowest aircraft takes the floor, the next lowest the slot above and so on, so none has to pass
    /// another's level to reach its own. Everything here is in metres.
    /// </summary>
    public static class StackedOrbit
    {
        /// <summary>A gap cut by more than this share of the separation is warned about.</summary>
        public const double WarnGapLoss = 0.1;

        /// <summary>A gap under this share of the separation blocks the plan.</summary>
        public const double MinGapShare = 0.5;

        /// <summary>The least separation: each aircraft's altitude comes from its own baro and GPS.</summary>
        public const double MinSeparationM = 10;

        /// <summary>A separation under this is warned about.</summary>
        public const double WarnSeparationM = 20;

        /// <summary>The least height of every slot above the ground at the center (a Terrain floor's height).</summary>
        public const double MinClearanceM = 30;

        public class Aircraft
        {
            /// <summary>What the operator knows the aircraft by, for the messages.</summary>
            public string Name;

            public uint SysId;

            /// <summary>Where it is now, above sea level.</summary>
            public double AmslM;

            /// <summary>The height above sea level its relative altitudes count from.</summary>
            public double HomeAmslM;

            /// <summary>The caller's own reference (the vehicle), carried through untouched.</summary>
            public object Tag;
        }

        public class Slot
        {
            public Aircraft Aircraft;

            /// <summary>0 is the floor.</summary>
            public int Index;

            /// <summary>The altitude to command, in the plan's frame.</summary>
            public double TargetM;

            /// <summary>The same altitude above sea level, NaN when it can't be known.</summary>
            public double TargetAmslM;

            public bool Climbs => !(TargetAmslM < Aircraft.AmslM);
        }

        public class Plan
        {
            public MAVLink.MAV_FRAME Frame;
            public double FloorM;
            public double SeparationM;
            public IReadOnlyList<Slot> Slots = new List<Slot>();
            public IReadOnlyList<string> Errors = new List<string>();
            public IReadOnlyList<string> Warnings = new List<string>();

            public bool Valid => Errors.Count == 0;
        }

        /// <summary>
        /// The slots for these aircraft. groundAmslAtCentreM is the ground under the orbit's centre (NaN if not known),
        /// and formatAlt writes an altitude for the messages (metres if not given).
        /// </summary>
        public static Plan Build(IEnumerable<Aircraft> aircraft, MAVLink.MAV_FRAME frame, double floorM,
            double separationM, double groundAmslAtCentreM = double.NaN, Func<double, string> formatAlt = null)
        {
            if (formatAlt == null)
                formatAlt = m => m.ToString("0") + " m";

            var errors = new List<string>();
            var warnings = new List<string>();

            // by where they are now: sea level is the one datum they all share
            var ordered = (aircraft ?? Enumerable.Empty<Aircraft>())
                .OrderBy(a => a.AmslM).ThenBy(a => a.SysId).ToList();

            bool absolute = frame == MAVLink.MAV_FRAME.GLOBAL || frame == MAVLink.MAV_FRAME.GLOBAL_INT;
            bool terrain = MissionAltitude.FollowsTerrain(frame);

            var slots = new List<Slot>();
            for (int i = 0; i < ordered.Count; i++)
            {
                var target = floorM + i * separationM;
                var a = ordered[i];
                slots.Add(new Slot
                {
                    Aircraft = a,
                    Index = i,
                    TargetM = target,
                    TargetAmslM = terrain && double.IsNaN(groundAmslAtCentreM)
                        ? double.NaN
                        : MissionAltitude.ToAsl(target, frame, a.HomeAmslM, groundAmslAtCentreM)
                });
            }

            if (ordered.Count == 0)
                errors.Add("No aircraft are selected.");

            if (!(separationM > 0))
                errors.Add("The separation has to be more than zero.");
            else if (separationM < MinSeparationM)
                errors.Add("The separation (" + formatAlt(separationM) + ") is under the least allowed, " +
                           formatAlt(MinSeparationM) + ".");
            else if (separationM < WarnSeparationM)
                warnings.Add("The separation (" + formatAlt(separationM) + ") leaves little room for the aircraft's " +
                             "altitude errors.");

            if (absolute)
            {
                // a height above home typed with Absolute selected would put the stack in the ground
                if (ordered.Count > 0)
                {
                    var field = ordered.Max(a => a.HomeAmslM);
                    if (!(floorM > field))
                        errors.Add("The floor (" + formatAlt(floorM) + " above sea level) is not above the field (" +
                                   formatAlt(field) + ").");
                }
            }
            else if (!terrain && !(floorM > 0))
            {
                errors.Add("The floor has to be more than zero.");
            }

            if (terrain)
            {
                // above the ground under each aircraft, which it follows round the circle
                if (!(floorM >= MinClearanceM))
                    errors.Add("The floor (" + formatAlt(floorM) + " above the ground) is under " +
                               formatAlt(MinClearanceM) + ".");
            }
            else if (!double.IsNaN(groundAmslAtCentreM) && slots.Count > 0)
            {
                // the lowest slot above sea level: in Relative each counts from its own aircraft's home
                var lowest = slots.OrderBy(s => s.TargetAmslM).First();
                if (!(lowest.TargetAmslM - groundAmslAtCentreM >= MinClearanceM))
                    errors.Add(lowest.Aircraft.Name + "'s slot (" + formatAlt(lowest.TargetAmslM) +
                               " above sea level) is less than " + formatAlt(MinClearanceM) +
                               " above the ground at the center (" + formatAlt(groundAmslAtCentreM) + ").");
            }

            if (!absolute && !terrain && separationM > 0)
            {
                // each aircraft counts its altitude from its own home, so homes at different heights close the gaps
                for (int i = 0; i + 1 < slots.Count; i++)
                {
                    var gap = slots[i + 1].TargetAmslM - slots[i].TargetAmslM;
                    var names = slots[i].Aircraft.Name + " and " + slots[i + 1].Aircraft.Name;
                    if (gap < separationM * MinGapShare)
                        errors.Add("Home heights differ: " + names + " would be " + formatAlt(gap) +
                                   " apart, not " + formatAlt(separationM) + ". Use Absolute.");
                    else if (separationM - gap > separationM * WarnGapLoss)
                        warnings.Add("Home heights differ: " + names + " will be " + formatAlt(gap) +
                                     " apart, not " + formatAlt(separationM) + ".");
                }
            }

            return new Plan
            {
                Frame = frame,
                FloorM = floorM,
                SeparationM = separationM,
                Slots = slots,
                Errors = errors,
                Warnings = warnings
            };
        }

        /// <summary>
        /// The order to command the aircraft in: those climbing from the top down, then those descending from the
        /// bottom up. Each moves away from the ones still waiting, so a stack sent part way is still in order.
        /// </summary>
        public static List<Slot> SendOrder(Plan plan)
        {
            var climbing = plan.Slots.Where(s => s.Climbs).OrderByDescending(s => s.Index);
            var descending = plan.Slots.Where(s => !s.Climbs).OrderBy(s => s.Index);
            return climbing.Concat(descending).ToList();
        }

        /// <summary>
        /// For a retry: the aircraft still to go that would pass a level the stack already holds. Each climbs or
        /// descends from where it is to its slot, and must neither cross the level of an aircraft already in the stack
        /// nor start within half a separation of it. The levels share one datum (above sea level, or above the ground
        /// for Terrain); one not known (NaN) can't be checked and counts as a conflict. A message for each conflict.
        /// </summary>
        public static List<string> RetryConflicts(IEnumerable<(string Name, double NowM, double TargetM)> remaining,
            IEnumerable<(string Name, double LevelM)> taken, double separationM, Func<double, string> formatAlt = null)
        {
            if (formatAlt == null)
                formatAlt = m => m.ToString("0") + " m";

            var held = (taken ?? Enumerable.Empty<(string Name, double LevelM)>()).ToList();
            var messages = new List<string>();
            foreach (var aircraft in remaining ?? Enumerable.Empty<(string Name, double NowM, double TargetM)>())
            {
                if (double.IsNaN(aircraft.NowM) || double.IsNaN(aircraft.TargetM))
                {
                    messages.Add(aircraft.Name + "'s height isn't known, so its way to its slot can't be checked.");
                    continue;
                }

                var low = Math.Min(aircraft.NowM, aircraft.TargetM) - separationM / 2;
                var high = Math.Max(aircraft.NowM, aircraft.TargetM) + separationM / 2;
                foreach (var level in held)
                {
                    if (double.IsNaN(level.LevelM))
                        messages.Add(level.Name + "'s level isn't known, so " + aircraft.Name +
                                     "'s way to its slot can't be checked.");
                    else if (level.LevelM >= low && level.LevelM <= high)
                        messages.Add(aircraft.Name + " would pass " + level.Name + " at " + formatAlt(level.LevelM) +
                                     ".");
                }
            }

            return messages;
        }
    }
}
