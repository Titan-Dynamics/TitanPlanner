using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Controls.PPS
{
    /// <summary>
    /// The pack's internal resistance from telemetry: each step in current (a throttle change) and the voltage's answer
    /// to it, R = −ΔV / ΔI, between a second of readings before the step and a second after it has settled. The
    /// voltage arrives smoothed, so the blocks sit a couple of seconds apart; each must hold steady, so a ramp (or the
    /// pack draining) isn't taken for a step. The estimate is the median of the recent steps.
    /// </summary>
    public sealed class PackResistanceEstimator
    {
        /// <summary>Steps needed before there is an estimate.</summary>
        public const int StepsNeeded = 5;

        // a second before, then a second for the step, then the current holds a second while the voltage settles, then
        // the second averaged after
        const double Block = 1.0;
        const double Gap = 1.0;
        const double Settle = 1.0;
        const double Cooldown = 3.0;
        const double MinStepAmps = 1.5;
        const double MinStepShare = 0.25;
        const double MaxOhm = 0.5;
        const int MaxSteps = 40;

        private readonly List<(double T, double Volts, double Amps)> readings = new List<(double, double, double)>();
        private readonly List<double> steps = new List<double>();
        private double lastStepT = double.NegativeInfinity;

        /// <summary>The recent steps' resistances, ohms, oldest first.</summary>
        public IReadOnlyList<double> Steps => steps;

        public bool Ready => steps.Count >= StepsNeeded;

        /// <summary>The median of the recent steps, ohms; NaN until <see cref="Ready"/>.</summary>
        public double Ohm
        {
            get
            {
                if (!Ready)
                    return double.NaN;
                var sorted = steps.OrderBy(r => r).ToList();
                int n = sorted.Count;
                return n % 2 == 1 ? sorted[n / 2] : (sorted[n / 2 - 1] + sorted[n / 2]) / 2;
            }
        }

        public void Clear()
        {
            readings.Clear();
            steps.Clear();
            lastStepT = double.NegativeInfinity;
        }

        public void Add(double t, double volts, double amps)
        {
            if (!(volts > 1) || double.IsNaN(amps) || amps < 0)
                return;
            readings.Add((t, volts, amps));
            readings.RemoveAll(r => r.T < t - (Block * 2 + Gap + Settle + 1));
            if (t - lastStepT < Cooldown)
                return;

            var after = readings.Where(r => r.T > t - Block).ToList();
            var held = readings.Where(r => r.T > t - Block - Settle).ToList();
            double beforeEnd = t - Block - Settle - Gap;
            var before = readings.Where(r => r.T <= beforeEnd && r.T > beforeEnd - Block).ToList();
            if (after.Count < 3 || before.Count < 3)
                return;

            double ampsBefore = before.Average(r => r.Amps), ampsAfter = after.Average(r => r.Amps);
            double step = ampsAfter - ampsBefore;
            if (Math.Abs(step) < Math.Max(MinStepAmps, MinStepShare * Math.Max(ampsBefore, ampsAfter)))
                return;

            // each block steady: its current within a fifth of the step, its voltage settled
            if (Spread(before.Select(r => r.Amps)) > Math.Abs(step) * 0.2 ||
                Spread(held.Select(r => r.Amps)) > Math.Abs(step) * 0.2)
                return;

            double ohm = -(after.Average(r => r.Volts) - before.Average(r => r.Volts)) / step;
            if (ohm <= 0 || ohm > MaxOhm)
                return;

            steps.Add(ohm);
            if (steps.Count > MaxSteps)
                steps.RemoveAt(0);
            lastStepT = t;
        }

        private static double Spread(IEnumerable<double> values)
        {
            var list = values.ToList();
            return list.Max() - list.Min();
        }
    }
}
