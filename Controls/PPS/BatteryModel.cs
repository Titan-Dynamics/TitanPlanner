using System;

namespace MissionPlanner.Controls.PPS
{
    public enum BatteryChemistry
    {
        /// <summary>18650/21700 NMC cells, 4.2 V full.</summary>
        LiIon,

        /// <summary>Lithium polymer, 4.2 V full.</summary>
        LiPo,

        /// <summary>High-voltage lithium polymer, 4.35 V full.</summary>
        LiHV
    }

    /// <summary>
    /// A pack's voltage at a state of charge: each cell's resting voltage from a typical curve for its chemistry, less
    /// the sag of drawing a power through the pack's internal resistance.
    /// </summary>
    public sealed class BatteryModel
    {
        // resting volts per cell at 0, 10, ... 100 % charge
        static readonly double[] LiIonCurve = { 3.00, 3.35, 3.48, 3.57, 3.65, 3.73, 3.81, 3.89, 3.98, 4.08, 4.20 };
        static readonly double[] LiPoCurve = { 3.27, 3.68, 3.73, 3.77, 3.80, 3.84, 3.87, 3.95, 4.02, 4.11, 4.20 };
        static readonly double[] LiHVCurve = { 3.35, 3.74, 3.80, 3.85, 3.89, 3.94, 3.99, 4.07, 4.15, 4.24, 4.35 };

        public BatteryChemistry Chemistry = BatteryChemistry.LiIon;

        /// <summary>Cells in series.</summary>
        public int Cells = 6;

        /// <summary>The whole pack's internal resistance and wiring, ohms: by default 6S1P of 8 mΩ cells.</summary>
        public double ResistanceOhm = 0.048;

        static double[] Curve(BatteryChemistry chemistry)
        {
            switch (chemistry)
            {
                case BatteryChemistry.LiPo:
                    return LiPoCurve;
                case BatteryChemistry.LiHV:
                    return LiHVCurve;
                default:
                    return LiIonCurve;
            }
        }

        /// <summary>A cell's resting voltage at a state of charge (%, held to 0 to 100).</summary>
        public static double CellVoltage(BatteryChemistry chemistry, double percent)
        {
            var curve = Curve(chemistry);
            double p = Math.Max(0, Math.Min(100, percent)) / 10.0;
            int i = Math.Min(9, (int) Math.Floor(p));
            return curve[i] + (curve[i + 1] - curve[i]) * (p - i);
        }

        /// <summary>The pack's resting voltage at a state of charge.</summary>
        public double RestingVoltage(double percent) => Cells * CellVoltage(Chemistry, percent);

        /// <summary>
        /// The pack's voltage while it delivers <paramref name="watts"/> at a state of charge: V = Voc − (P/V)·R, solved
        /// for V. NaN below 0 % charge, or when the pack can't deliver the power at all.
        /// </summary>
        public double LoadedVoltage(double percent, double watts)
        {
            if (double.IsNaN(percent) || percent < 0)
                return double.NaN;
            double open = RestingVoltage(percent);
            double load = Math.Max(0, watts) * Math.Max(0, ResistanceOhm);
            double root = open * open - 4 * load;
            return root < 0 ? double.NaN : (open + Math.Sqrt(root)) / 2;
        }

        /// <summary>
        /// The cells in series of a pack reading <paramref name="volts"/> under <paramref name="watts"/> at a state of
        /// charge, the count whose predicted voltage is nearest; 0 when the reading says nothing.
        /// </summary>
        public static int CellsFrom(double volts, double percent, double watts, BatteryChemistry chemistry,
            double resistanceOhm)
        {
            if (!(volts > 2) || double.IsNaN(percent))
                return 0;
            int best = 0;
            double nearest = double.MaxValue;
            for (int cells = 1; cells <= 24; cells++)
            {
                var model = new BatteryModel { Chemistry = chemistry, Cells = cells, ResistanceOhm = resistanceOhm };
                double predicted = model.LoadedVoltage(Math.Max(0, percent), watts);
                if (double.IsNaN(predicted))
                    continue;
                double off = Math.Abs(predicted - volts);
                if (off < nearest)
                {
                    nearest = off;
                    best = cells;
                }
            }

            // no count within half a cell of the reading
            return nearest <= CellVoltage(chemistry, 50) / 2 ? best : 0;
        }
    }
}
