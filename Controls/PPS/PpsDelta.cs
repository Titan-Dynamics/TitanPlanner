using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Controls.PPS
{
    /// <summary>
    /// The aircraft's live performance against the PPS prediction at the airspeed it is flying, compared only in steady,
    /// level, straight flight, which is what PPS predicts.
    /// </summary>
    public sealed class PpsDelta
    {
        public struct Reading
        {
            public double T;
            public bool Armed;

            /// <summary>m/s</summary>
            public double Airspeed;

            /// <summary>m/s, up</summary>
            public double ClimbRate;

            /// <summary>degrees</summary>
            public double Roll, Pitch;

            public double Watts, Volts, Throttle;

            /// <summary>Over the ground; NaN or 0 for none.</summary>
            public double GroundWhPerKm;

            /// <summary>0 or NaN without RPM telemetry.</summary>
            public double Rpm;
        }

        public sealed class Row
        {
            public string Name;
            public string Unit;
            public string Format;
            public double Predicted = double.NaN;
            public double Actual = double.NaN;
            public double Delta => Actual - Predicted;

            /// <summary>NaN for an angle, where a share of it says nothing.</summary>
            public double DeltaPercent = double.NaN;
        }

        public const double Window = 10;
        public const double MaxClimbRate = 0.5;
        public const double MaxRoll = 10;
        public const double MaxAirspeedSpread = 0.5;
        const int MaxPoints = 3000;

        private readonly List<Reading> readings = new List<Reading>();
        private double lastPointT = double.NegativeInfinity;

        /// <summary>Whether the last window was steady enough to compare.</summary>
        public bool Steady { get; private set; }

        /// <summary>What kept the last window from being steady; null when it was.</summary>
        public string NotSteadyBecause { get; private set; } = "No readings yet";

        /// <summary>The window's mean airspeed, m/s; NaN when there is none.</summary>
        public double Airspeed { get; private set; } = double.NaN;

        public List<Row> Rows { get; } = new List<Row>();

        /// <summary>This session's steady airspeed and power, one a second, for the polar chart.</summary>
        public List<(double Airspeed, double Watts)> Points { get; } = new List<(double, double)>();

        public void Clear()
        {
            readings.Clear();
            Rows.Clear();
            Steady = false;
            NotSteadyBecause = "No readings yet";
            Airspeed = double.NaN;
        }

        public void Add(Reading reading, PpsPerformance pps)
        {
            readings.Add(reading);
            readings.RemoveAll(r => r.T < reading.T - Window);
            Update(pps);
            if (Steady && reading.T - lastPointT >= 1)
            {
                lastPointT = reading.T;
                Points.Add((Airspeed, readings.Average(r => r.Watts)));
                if (Points.Count > MaxPoints)
                    Points.RemoveAt(0);
            }
        }

        private void Update(PpsPerformance pps)
        {
            Rows.Clear();
            Steady = false;
            Airspeed = readings.Count > 0 ? readings.Average(r => r.Airspeed) : double.NaN;
            NotSteadyBecause = WhyNotSteady(pps);
            if (NotSteadyBecause != null)
                return;
            Steady = true;

            var at = pps.At(Airspeed);
            double watts = readings.Average(r => r.Watts);
            double volts = readings.Average(r => r.Volts);

            void Add(string name, string unit, string format, double predicted, double actual, bool angle = false)
            {
                if (double.IsNaN(predicted) || double.IsNaN(actual))
                    return;
                Rows.Add(new Row
                {
                    Name = name, Unit = unit, Format = format, Predicted = predicted, Actual = actual,
                    DeltaPercent = angle || Math.Abs(predicted) < 1e-9 ? double.NaN : (actual - predicted) / predicted * 100
                });
            }

            Add("Power", "W", "0.0", at.Power, watts);
            Add("Current", "A", "0.00", volts > 1 ? at.Power / volts : double.NaN,
                volts > 1 ? watts / volts : double.NaN);
            Add("Throttle", "%", "0", at.Throttle, readings.Average(r => r.Throttle));
            Add("Efficiency (air)", "Wh/km", "0.000", at.WhPerKm, watts / Airspeed / 3.6);
            var ground = readings.Where(r => r.GroundWhPerKm > 0).Select(r => r.GroundWhPerKm).ToList();
            Add("Efficiency (ground)", "Wh/km", "0.000", at.WhPerKm, ground.Count > 0 ? ground.Average() : double.NaN);
            Add("Pitch vs AoA", "°", "0.0", at.FuselageAoA, readings.Average(r => r.Pitch), angle: true);
            var rpm = readings.Where(r => r.Rpm > 0).Select(r => r.Rpm).ToList();
            if (rpm.Count == readings.Count)
                Add("Prop RPM", "rpm", "0", at.PropRpm, rpm.Average());
        }

        private string WhyNotSteady(PpsPerformance pps)
        {
            if (pps == null)
                return "No PPS export loaded";
            if (readings.Count < 4 || readings[readings.Count - 1].T - readings[0].T < Window * 0.8)
                return "Collecting " + Window.ToString("0") + " s of flight";
            if (readings.Any(r => !r.Armed))
                return "Not armed";
            if (readings.Any(r => double.IsNaN(r.Airspeed) || r.Airspeed <= 0))
                return "No airspeed";
            if (readings.Any(r => Math.Abs(r.ClimbRate) > MaxClimbRate))
                return "Climbing or descending (over " + MaxClimbRate.ToString("0.0") + " m/s)";
            if (readings.Any(r => Math.Abs(r.Roll) > MaxRoll))
                return "Turning (bank over " + MaxRoll.ToString("0") + "°)";
            double mean = Airspeed;
            double spread = Math.Sqrt(readings.Average(r => (r.Airspeed - mean) * (r.Airspeed - mean)));
            if (spread > MaxAirspeedSpread)
                return "Airspeed changing (σ " + spread.ToString("0.0") + " m/s)";
            if (mean < pps.MinAirspeed || mean > pps.MaxAirspeed)
                return "Airspeed " + mean.ToString("0.0") + " m/s is outside the PPS sweep (" +
                       pps.MinAirspeed.ToString("0.0") + " to " + pps.MaxAirspeed.ToString("0.0") + " m/s)";
            if (readings.Any(r => !(r.Watts > 0)))
                return "No battery power reading";
            return null;
        }
    }
}
