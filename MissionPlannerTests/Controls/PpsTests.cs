using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Controls.TerrainProfile;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.PPS.Tests
{
    [TestClass]
    public class PpsTests
    {
        // the first rows of a real PPS export (Raptor), summary on the first row after the empty column
        const string Raptor =
            "Airspeed (m/s),L/D,Range (km),Endurance (h),Climb Rate (m/s),Induced Drag (N),Parasite Drag (N),Total Drag (N),Efficiency (Wh/km),Power (W),Fuselage AoA,Prop RPM,Throttle (%),Prop+Motor Efficiency (%),,AUW,Stall Speed,Cruise Speed,Loiter Speed,Best Eff,Max Endurance,Max Climb,Max Range,Min Current,Cruise Current,Cruise Power,Cruise AoA,Wing Loading,Prop,Motor\n" +
            "11.45554222506041,10.47440839682393,158.35290268192944,3.839793573054993,2.2692668378695,2.882832258021887,0.7767202195789473,3.659552477600834,2.0460629045164547,84.37953599214478,11.77863158209732,3867.4435050947422,28.324219708422888,54.88663604383805,,3.9088,11.45554222506041,16.455542225060412,13.95554222506041,1.5620597300769343,3.8886105144282594,2.827363176631634,207.4184448657686,3.7063500430377,4.168303758867156,92.53634344685086,2.732548446601223,121.015479876161,APC 10x10E,BH 2816 620Kv\n" +
            "11.95554222506041,10.97463855078293,167.66034551744877,3.895458467928752,2.3533554709214095,2.6467451057522937,0.8460029262332723,3.492748031985566,1.9324784223725735,83.17377855969634,10.341186588185709,3879.032992970694,28.362290819500956,55.548220906229716\n" +
            "12.45554222506041,11.419240291510595,176.16547004250592,3.9287613421701226,2.4264839120162294,2.4385148083198396,0.9182450304670619,3.3567598387869015,1.8391799478173787,82.46874059828555,9.073351402121844,3903.822112304449,28.497255874065242,56.14471733440299\n" +
            "12.95554222506041,11.803899025399357,183.65539778929815,3.937726988849914,2.4962392850363537,2.2539251300423495,0.993446532280316,3.2473716623226654,1.7641735767097606,82.28097095543696,7.949454966348258,3939.685622646831,28.71646286922665,56.63827512018609\n";

        // a made-up aircraft: 100 W at any speed, 100 Wh of battery, 2 kg, at 20 m/s half its power pushing it along
        const string Flat =
            "Airspeed (m/s),Range (km),Endurance (h),Total Drag (N),Efficiency (Wh/km),Power (W),,AUW,Cruise Speed\n" +
            "10,36,1,3.5,2.7777777777777777,100,,2,20\n" +
            "30,108,1,1.5,0.9259259259259259,100\n";

        const double Origin = 1;
        static readonly PointLatLngAlt Home = new PointLatLngAlt(Origin, Origin, 100);
        const double Leg = 0.1;

        static Locationwp Item(MAVLink.MAV_CMD command, double lng, double alt)
        {
            var item = new Locationwp().Set(Origin, Origin + lng, alt, (ushort) command);
            item.frame = (byte) MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT;
            return item;
        }

        static Locationwp Rtl() => new Locationwp().Set(0, 0, 0, (ushort) MAVLink.MAV_CMD.RETURN_TO_LAUNCH);

        static Locationwp ChangeSpeed(double airspeed)
        {
            var item = new Locationwp().Set(0, 0, 0, (ushort) MAVLink.MAV_CMD.DO_CHANGE_SPEED);
            item.p1 = 0;
            item.p2 = (float) airspeed;
            return item;
        }

        static double LegMetres => new PointLatLngAlt(Origin, Origin).GetDistance(new PointLatLngAlt(Origin, Origin + Leg));

        // 100 W for the leg at 20 m/s, as % of 100 Wh
        static double LegPercent => 100 * LegMetres / 20 / 3600;

        static BingoResult Bingo(List<Locationwp> mission, double margin = 20, double windSpeed = 0, double windFrom = 0,
            Action<BingoInput> more = null)
        {
            var input = new BingoInput
            {
                Profile = MissionProfile.Build(new ProfileInput { Home = Home, Mission = mission, VehicleClass = VehicleClass.Plane },
                    (lat, lng) => double.NaN),
                Mission = mission,
                Pps = PpsPerformance.Parse(Flat, "Flat"),
                CruiseAirspeed = 20,
                MarginPercent = margin,
                WindSpeed = windSpeed,
                WindFrom = windFrom
            };
            more?.Invoke(input);
            return BingoPlan.Compute(input);
        }

        static List<Locationwp> OutAndBack(double alt = 0) =>
            new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, alt), Rtl() };

        [TestMethod]
        public void ReadsTheSweepAndSummaryByHeader()
        {
            var pps = PpsPerformance.Parse(Raptor, "Raptor_data");

            Assert.AreEqual(4, pps.Sweep.Count);
            Assert.AreEqual(3.9088, pps.Auw, 1e-6);
            Assert.AreEqual(16.4555, pps.CruiseSpeed, 1e-3);
            Assert.AreEqual("APC 10x10E", pps.Prop);
            Assert.AreEqual("BH 2816 620Kv", pps.Motor);
            Assert.AreEqual(82.469, pps.Sweep[2].Power, 1e-3);
            Assert.AreEqual(56.145, pps.Sweep[2].PropMotorEfficiency, 1e-3);
        }

        [TestMethod]
        public void UsableEnergyIsRangeTimesEfficiency()
        {
            Assert.AreEqual(324, PpsPerformance.Parse(Raptor, "Raptor_data").UsableWh, 1);
        }

        [TestMethod]
        public void InterpolatesBetweenAirspeedsAndClampsOutside()
        {
            var pps = PpsPerformance.Parse(Raptor, "Raptor_data");

            var mid = pps.At(11.7055422250604, out bool midOutside);
            Assert.IsFalse(midOutside);
            Assert.AreEqual((84.37953599214478 + 83.17377855969634) / 2, mid.Power, 1e-6);

            var fast = pps.At(40, out bool outside);
            Assert.IsTrue(outside);
            Assert.AreEqual(pps.Sweep.Last().Power, fast.Power, 1e-9);
        }

        [TestMethod]
        public void RejectsAFileWithoutAirspeedAndPower()
        {
            Assert.ThrowsException<FormatException>(() => PpsPerformance.Parse("a,b\n1,2\n3,4\n", "x"));
        }

        [TestMethod]
        public void BingoOutAndBackInStillAir()
        {
            var result = Bingo(OutAndBack());

            Assert.IsNull(result.Problem);
            CollectionAssert.AreEqual(new[] { "1", "RTL" }, result.Rows.Select(r => r.Label).ToArray());
            var wp = result.Rows[0];
            Assert.AreEqual(LegMetres / 1000, wp.DistanceHomeKm, 0.01);
            Assert.AreEqual(100 - LegPercent, wp.ArrivalPercent, 0.01);
            Assert.AreEqual(LegPercent + 20, wp.BingoPercent, 0.01);
            Assert.AreEqual(BingoStatus.Ok, wp.Status);

            var back = result.Rows[1];
            Assert.AreEqual(100 - 2 * LegPercent, back.ArrivalPercent, 0.01);
            Assert.AreEqual(20, back.BingoPercent, 0.01);
            Assert.IsNull(result.PointOfNoReturn);
            Assert.IsFalse(result.MoMode);
        }

        [TestMethod]
        public void AHeadwindHomeRaisesBingoAndATailwindLowersIt()
        {
            double still = Bingo(OutAndBack()).Rows[0].BingoPercent;
            // the waypoint is east of home, so home is west: wind from the west is a headwind on the way back
            double headwind = Bingo(OutAndBack(), windSpeed: 5, windFrom: 270).Rows[0].BingoPercent;
            double tailwind = Bingo(OutAndBack(), windSpeed: 5, windFrom: 90).Rows[0].BingoPercent;

            Assert.AreEqual(LegMetres / 15 * 100 / 3600 + 20, headwind, 0.05);
            Assert.IsTrue(headwind > still && still > tailwind);
        }

        [TestMethod]
        public void WindStrongerThanTheAirspeedIsUnreachable()
        {
            var result = Bingo(OutAndBack(), windSpeed: 25, windFrom: 270);

            Assert.AreEqual(BingoStatus.Unreachable, result.Rows[0].Status);
            Assert.AreSame(result.Rows[0], result.PointOfNoReturn);
        }

        [TestMethod]
        public void PointOfNoReturnIsTheFirstWaypointBelowBingo()
        {
            var result = Bingo(OutAndBack(), margin: 80);

            Assert.AreEqual(BingoStatus.Below, result.Rows[0].Status);
            Assert.AreSame(result.Rows[0], result.PointOfNoReturn);
        }

        [TestMethod]
        public void ZeroMarginIsMoMode()
        {
            var result = Bingo(OutAndBack(), margin: 0);

            Assert.IsTrue(result.MoMode);
            Assert.AreEqual(LegPercent, result.Rows[0].BingoPercent, 0.01);
        }

        [TestMethod]
        public void ChangeSpeedAppliesToTheLegsAfterIt()
        {
            var mission = new List<Locationwp> { ChangeSpeed(30), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 0), Rtl() };
            var result = Bingo(mission);

            Assert.AreEqual(100 - 100 * LegMetres / 30 / 3600, result.Rows[0].ArrivalPercent, 0.01);
        }

        [TestMethod]
        public void AClimbCostsItsPotentialEnergyOverTheEfficiency()
        {
            double level = Bingo(OutAndBack()).Rows[0].ArrivalPercent;
            double climbing = Bingo(OutAndBack(100)).Rows[0].ArrivalPercent;

            // 2 kg up 100 m at half efficiency, as % of 100 Wh
            Assert.AreEqual(2 * 9.80665 * 100 / 0.5 / 3600, level - climbing, 0.01);
        }

        [TestMethod]
        public void InFlightStartsFromTheVehicleAndTheEnergyUsed()
        {
            var result = Bingo(OutAndBack(), more: input =>
            {
                input.Live = true;
                input.UsedWh = 10;
                input.NextItem = 0;
                input.Lat = Origin;
                input.Lng = Origin;
                input.AltitudeAmsl = 100;
            });

            Assert.AreEqual(90, result.Now.ArrivalPercent, 0.01);
            Assert.AreEqual(20, result.Now.BingoPercent, 0.01);
            Assert.AreEqual(90 - LegPercent, result.Rows[0].ArrivalPercent, 0.01);
        }

        [TestMethod]
        public void WaypointsAlreadyFlownArePassed()
        {
            var result = Bingo(OutAndBack(), more: input =>
            {
                input.Live = true;
                input.NextItem = 1;
                input.Lat = Origin;
                input.Lng = Origin + Leg;
                input.AltitudeAmsl = 100;
            });

            Assert.AreEqual(BingoStatus.Passed, result.Rows[0].Status);
            Assert.AreEqual(100 - LegPercent, result.Rows[1].ArrivalPercent, 0.01);
        }

        [TestMethod]
        public void CellVoltageFollowsTheChemistrysCurve()
        {
            Assert.AreEqual(4.20, BatteryModel.CellVoltage(BatteryChemistry.LiIon, 100), 1e-9);
            Assert.AreEqual(3.57, BatteryModel.CellVoltage(BatteryChemistry.LiIon, 30), 1e-9);
            Assert.AreEqual((3.57 + 3.65) / 2, BatteryModel.CellVoltage(BatteryChemistry.LiIon, 35), 1e-9);
            Assert.AreEqual(3.77, BatteryModel.CellVoltage(BatteryChemistry.LiPo, 30), 1e-9);
            Assert.AreEqual(4.35, BatteryModel.CellVoltage(BatteryChemistry.LiHV, 120), 1e-9);
        }

        [TestMethod]
        public void LoadedVoltageSagsByTheCurrentThroughTheResistance()
        {
            var pack = new BatteryModel { Chemistry = BatteryChemistry.LiIon, Cells = 6, ResistanceOhm = 0.1 };
            double v = pack.LoadedVoltage(100, 100);

            // V = Voc - (P/V)·R
            Assert.AreEqual(25.2 - 100 / v * 0.1, v, 1e-9);
            Assert.AreEqual(25.2, pack.LoadedVoltage(100, 0), 1e-9);
            Assert.IsTrue(double.IsNaN(pack.LoadedVoltage(-5, 100)));
        }

        [TestMethod]
        public void CellsComeFromTheVoltageAtTheChargeLeft()
        {
            Assert.AreEqual(6, BatteryModel.CellsFrom(25.1, 100, 50, BatteryChemistry.LiIon, 0.05));
            // a 6S Li-ion pack near empty reads under what 5S reads full
            Assert.AreEqual(6, BatteryModel.CellsFrom(20.6, 15, 90, BatteryChemistry.LiIon, 0.05));
            Assert.AreEqual(3, BatteryModel.CellsFrom(12.5, 100, 20, BatteryChemistry.LiPo, 0.05));
            Assert.AreEqual(0, BatteryModel.CellsFrom(0, 100, 0, BatteryChemistry.LiIon, 0.05));
        }

        [TestMethod]
        public void RowsCarryTheVoltageOnArrivalAndAtBingo()
        {
            var pack = new BatteryModel { Chemistry = BatteryChemistry.LiIon, Cells = 6, ResistanceOhm = 0.05 };
            var result = Bingo(OutAndBack(), more: input => input.Battery = pack);
            var wp = result.Rows[0];

            Assert.AreEqual(pack.LoadedVoltage(wp.ArrivalPercent, 100), wp.ArrivalVolts, 1e-9);
            Assert.AreEqual(pack.LoadedVoltage(wp.BingoPercent, 100), wp.BingoVolts, 1e-9);
            Assert.IsTrue(wp.ArrivalVolts > wp.BingoVolts);
            Assert.IsTrue(double.IsNaN(Bingo(OutAndBack()).Rows[0].BingoVolts));
        }

        // a 24 V pack of 40 mΩ: the voltage sags by the current through it, and arrives smoothed as the HUD has it
        static PackResistanceEstimator Fly(Func<double, double> amps, double seconds, double ohm = 0.04,
            double drainPerSecond = 0)
        {
            var estimator = new PackResistanceEstimator();
            double smoothed = 0;
            for (double t = 0; t <= seconds; t += 0.25)
            {
                double a = amps(t);
                double v = 24 - drainPerSecond * t - a * ohm;
                smoothed = smoothed == 0 ? v : v * 0.4 + smoothed * 0.6;
                estimator.Add(t, smoothed, a);
            }

            return estimator;
        }

        [TestMethod]
        public void PackResistanceComesFromThrottleSteps()
        {
            // 4 A cruise and 12 A climbs, alternating every 8 s
            var estimator = Fly(t => (int) (t / 8) % 2 == 0 ? 4 : 12, 120);

            Assert.IsTrue(estimator.Ready);
            Assert.AreEqual(0.04, estimator.Ohm, 0.004);
        }

        [TestMethod]
        public void PackResistanceIgnoresSteadyFlightAndRamps()
        {
            // steady cruise as the pack drains, then a slow ramp in current: no steps to measure
            Assert.AreEqual(0, Fly(t => 4, 120, drainPerSecond: 0.01).Steps.Count);
            Assert.AreEqual(0, Fly(t => 4 + t * 0.1, 60).Steps.Count);
            Assert.IsTrue(double.IsNaN(Fly(t => 4, 10).Ohm));
        }

        static PpsDelta.Reading Steady(double t) => new PpsDelta.Reading
        {
            T = t, Armed = true, Airspeed = 20, Watts = 110, Volts = 22, Throttle = 40, Roll = 2, Pitch = 3
        };

        [TestMethod]
        public void DeltaComparesSteadyFlightWithPps()
        {
            var pps = PpsPerformance.Parse(Flat, "Flat");
            var delta = new PpsDelta();
            for (double t = 0; t <= 10; t += 0.25)
                delta.Add(Steady(t), pps);

            Assert.IsTrue(delta.Steady, delta.NotSteadyBecause);
            var power = delta.Rows.Single(r => r.Name == "Power");
            Assert.AreEqual(100, power.Predicted, 1e-9);
            Assert.AreEqual(110, power.Actual, 1e-9);
            Assert.AreEqual(10, power.DeltaPercent, 1e-9);
            Assert.IsTrue(delta.Points.Count > 0);
        }

        [TestMethod]
        public void DeltaWaitsOutClimbsAndTurns()
        {
            var pps = PpsPerformance.Parse(Flat, "Flat");

            var climbing = new PpsDelta();
            for (double t = 0; t <= 10; t += 0.25)
            {
                var r = Steady(t);
                r.ClimbRate = 2;
                climbing.Add(r, pps);
            }

            var turning = new PpsDelta();
            for (double t = 0; t <= 10; t += 0.25)
            {
                var r = Steady(t);
                r.Roll = 30;
                turning.Add(r, pps);
            }

            Assert.IsFalse(climbing.Steady);
            StringAssert.StartsWith(climbing.NotSteadyBecause, "Climbing");
            Assert.IsFalse(turning.Steady);
            StringAssert.StartsWith(turning.NotSteadyBecause, "Turning");
            Assert.AreEqual(0, climbing.Rows.Count);
        }
    }
}
