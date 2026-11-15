using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace MissionPlanner.Controls.PPS
{
    /// <summary>
    /// A Performance Prediction Software (PPS) export: the aircraft's steady-state performance across a sweep of
    /// airspeeds, and a summary of the design (weight, key speeds, prop and motor). Columns are found by their header,
    /// so a column PPS adds or moves doesn't break the reading; one that is missing reads as NaN.
    /// </summary>
    public sealed class PpsPerformance
    {
        /// <summary>One airspeed of the sweep, or a point interpolated between two.</summary>
        public sealed class Point
        {
            public double Airspeed = double.NaN;
            public double LiftToDrag = double.NaN;
            public double RangeKm = double.NaN;
            public double EnduranceH = double.NaN;
            public double ClimbRate = double.NaN;
            public double InducedDrag = double.NaN;
            public double ParasiteDrag = double.NaN;
            public double TotalDrag = double.NaN;
            public double WhPerKm = double.NaN;
            public double Power = double.NaN;
            public double FuselageAoA = double.NaN;
            public double PropRpm = double.NaN;
            public double Throttle = double.NaN;
            public double PropMotorEfficiency = double.NaN;

            /// <summary>
            /// The share of the battery's power that ends up pushing the aircraft along (drag × airspeed over power),
            /// for the cost of a climb; else the prop and motor's efficiency; else a conservative half.
            /// </summary>
            public double Efficiency
            {
                get
                {
                    double fromDrag = TotalDrag * Airspeed / Power;
                    if (fromDrag > 0.05 && fromDrag < 1)
                        return fromDrag;
                    if (PropMotorEfficiency > 5 && PropMotorEfficiency < 100)
                        return PropMotorEfficiency / 100.0;
                    return 0.5;
                }
            }

            internal static readonly (string Header, Func<Point, double> Get, Action<Point, double> Set)[] Columns =
            {
                ("airspeed", p => p.Airspeed, (p, v) => p.Airspeed = v),
                ("l/d", p => p.LiftToDrag, (p, v) => p.LiftToDrag = v),
                ("range", p => p.RangeKm, (p, v) => p.RangeKm = v),
                ("endurance", p => p.EnduranceH, (p, v) => p.EnduranceH = v),
                ("climb rate", p => p.ClimbRate, (p, v) => p.ClimbRate = v),
                ("induced drag", p => p.InducedDrag, (p, v) => p.InducedDrag = v),
                ("parasite drag", p => p.ParasiteDrag, (p, v) => p.ParasiteDrag = v),
                ("total drag", p => p.TotalDrag, (p, v) => p.TotalDrag = v),
                ("efficiency", p => p.WhPerKm, (p, v) => p.WhPerKm = v),
                ("power", p => p.Power, (p, v) => p.Power = v),
                ("fuselage aoa", p => p.FuselageAoA, (p, v) => p.FuselageAoA = v),
                ("prop rpm", p => p.PropRpm, (p, v) => p.PropRpm = v),
                ("throttle", p => p.Throttle, (p, v) => p.Throttle = v),
                ("prop+motor efficiency", p => p.PropMotorEfficiency, (p, v) => p.PropMotorEfficiency = v),
            };
        }

        /// <summary>The sweep, slowest first.</summary>
        public List<Point> Sweep { get; } = new List<Point>();

        /// <summary>The file's name without its extension, e.g. "Raptor_data".</summary>
        public string Name { get; private set; } = "";

        public string Path { get; private set; } = "";

        // the summary; NaN (or empty) where the export doesn't give it
        public double Auw { get; private set; } = double.NaN;
        public double StallSpeed { get; private set; } = double.NaN;
        public double CruiseSpeed { get; private set; } = double.NaN;
        public double LoiterSpeed { get; private set; } = double.NaN;
        public double BestEfficiency { get; private set; } = double.NaN;
        public double MaxEnduranceH { get; private set; } = double.NaN;
        public double MaxClimb { get; private set; } = double.NaN;
        public double MaxRangeKm { get; private set; } = double.NaN;
        public double MinCurrent { get; private set; } = double.NaN;
        public double CruiseCurrent { get; private set; } = double.NaN;
        public double CruisePower { get; private set; } = double.NaN;
        public double CruiseAoA { get; private set; } = double.NaN;
        public double WingLoading { get; private set; } = double.NaN;
        public string Prop { get; private set; } = "";
        public string Motor { get; private set; } = "";

        /// <summary>
        /// The battery energy PPS flies on, in Wh: each row's range × Wh/km (and endurance × power) gives it, so the
        /// median of them.
        /// </summary>
        public double UsableWh { get; private set; } = double.NaN;

        public double MinAirspeed => Sweep[0].Airspeed;
        public double MaxAirspeed => Sweep[Sweep.Count - 1].Airspeed;

        /// <summary>The sweep's airspeed with the least energy per km in still air.</summary>
        public double BestRangeSpeed
        {
            get
            {
                var best = Sweep.Where(p => !double.IsNaN(p.WhPerKm)).OrderBy(p => p.WhPerKm).FirstOrDefault();
                return best?.Airspeed ?? CruiseSpeed;
            }
        }

        public static PpsPerformance Load(string path)
        {
            var pps = Parse(File.ReadAllText(path), System.IO.Path.GetFileNameWithoutExtension(path));
            pps.Path = path;
            return pps;
        }

        /// <summary>Reads an export. Throws <see cref="FormatException"/> when it isn't one.</summary>
        public static PpsPerformance Parse(string csv, string name)
        {
            var lines = csv.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n')
                .Where(l => l.Trim().Length > 0).ToList();
            if (lines.Count < 3)
                throw new FormatException("A PPS export has a header and at least two rows of the airspeed sweep.");

            var header = SplitLine(lines[0]).Select(Normalise).ToList();

            // the sweep runs up to the first empty column; the summary follows it
            int gap = header.IndexOf("");
            int sweepEnd = gap < 0 ? header.Count : gap;

            var pps = new PpsPerformance { Name = name ?? "" };
            var sweepColumns = Point.Columns
                .Select(c => (c.Set, Index: header.Take(sweepEnd).ToList().IndexOf(c.Header)))
                .Where(c => c.Index >= 0).ToList();
            if (!sweepColumns.Any(c => header[c.Index] == "airspeed") || !sweepColumns.Any(c => header[c.Index] == "power"))
                throw new FormatException("A PPS export needs Airspeed (m/s) and Power (W) columns.");

            var first = SplitLine(lines[1]);
            foreach (var line in lines.Skip(1))
            {
                var cells = SplitLine(line);
                var point = new Point();
                foreach (var (set, index) in sweepColumns)
                    set(point, index < cells.Count ? Number(cells[index]) : double.NaN);
                if (!double.IsNaN(point.Airspeed) && point.Airspeed > 0)
                    pps.Sweep.Add(point);
            }

            pps.Sweep.Sort((a, b) => a.Airspeed.CompareTo(b.Airspeed));
            if (pps.Sweep.Count < 2)
                throw new FormatException("A PPS export needs at least two airspeeds in its sweep.");

            if (gap >= 0)
            {
                double Summary(string key)
                {
                    int i = header.IndexOf(key, gap);
                    return i >= 0 && i < first.Count ? Number(first[i]) : double.NaN;
                }

                string Text(string key)
                {
                    int i = header.IndexOf(key, gap);
                    return i >= 0 && i < first.Count ? first[i].Trim() : "";
                }

                pps.Auw = Summary("auw");
                pps.StallSpeed = Summary("stall speed");
                pps.CruiseSpeed = Summary("cruise speed");
                pps.LoiterSpeed = Summary("loiter speed");
                pps.BestEfficiency = Summary("best eff");
                pps.MaxEnduranceH = Summary("max endurance");
                pps.MaxClimb = Summary("max climb");
                pps.MaxRangeKm = Summary("max range");
                pps.MinCurrent = Summary("min current");
                pps.CruiseCurrent = Summary("cruise current");
                pps.CruisePower = Summary("cruise power");
                pps.CruiseAoA = Summary("cruise aoa");
                pps.WingLoading = Summary("wing loading");
                pps.Prop = Text("prop");
                pps.Motor = Text("motor");
            }

            var energies = pps.Sweep.Select(p => p.RangeKm * p.WhPerKm)
                .Concat(pps.Sweep.Select(p => p.EnduranceH * p.Power))
                .Where(e => e > 0 && !double.IsInfinity(e)).OrderBy(e => e).ToList();
            if (energies.Count > 0)
                pps.UsableWh = energies[energies.Count / 2];

            return pps;
        }

        /// <summary>
        /// The performance at an airspeed, linearly between the sweep's two nearest; outside the sweep, its end, with
        /// <paramref name="outOfRange"/> set.
        /// </summary>
        public Point At(double airspeed, out bool outOfRange)
        {
            outOfRange = double.IsNaN(airspeed) || airspeed < MinAirspeed - 1e-6 || airspeed > MaxAirspeed + 1e-6;
            if (double.IsNaN(airspeed) || airspeed <= MinAirspeed)
                return Sweep[0];
            if (airspeed >= MaxAirspeed)
                return Sweep[Sweep.Count - 1];

            int hi = 1;
            while (Sweep[hi].Airspeed < airspeed)
                hi++;
            var a = Sweep[hi - 1];
            var b = Sweep[hi];
            double t = (airspeed - a.Airspeed) / (b.Airspeed - a.Airspeed);

            var result = new Point();
            foreach (var column in Point.Columns)
                column.Set(result, column.Get(a) + (column.Get(b) - column.Get(a)) * t);
            return result;
        }

        public Point At(double airspeed) => At(airspeed, out _);

        /// <summary>Power in still, level flight at an airspeed, in W.</summary>
        public double PowerAt(double airspeed) => At(airspeed).Power;

        // "Airspeed (m/s)" -> "airspeed", "Prop+Motor Efficiency (%)" -> "prop+motor efficiency"
        private static string Normalise(string header)
        {
            var s = header.Trim().Trim('"').ToLowerInvariant();
            int unit = s.IndexOf('(');
            if (unit >= 0)
                s = s.Substring(0, unit);
            return s.Trim();
        }

        private static double Number(string cell)
        {
            return double.TryParse(cell.Trim().Trim('"'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : double.NaN;
        }

        // commas, with double-quoted cells that may hold them
        private static List<string> SplitLine(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                        quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                    cell.Append(c);
            }

            cells.Add(cell.ToString());
            return cells;
        }
    }
}
