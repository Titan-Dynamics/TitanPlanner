using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Controls.TerrainProfile;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.TerrainProfile.Tests
{
    [TestClass]
    public class MissionProfileTests
    {
        // home at 1N 1E and 100 m (0,0 means "no position" to a mission); 0.01 degrees of longitude is about 1113 m
        const double Origin = 1;
        static readonly PointLatLngAlt Home = new PointLatLngAlt(Origin, Origin, 100);
        const double Leg = 0.01;

        // an item east of home by lng degrees; 0 east with a place keeps home's longitude
        static Locationwp Item(MAVLink.MAV_CMD command, double lng, double alt, MAVLink.MAV_FRAME frame = MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT)
        {
            var item = new Locationwp().Set(Origin, Origin + lng, alt, (ushort)command);
            item.frame = (byte)frame;
            return item;
        }

        static Locationwp Takeoff(double alt) => Item(MAVLink.MAV_CMD.TAKEOFF, 0, alt);

        static Locationwp Here(MAVLink.MAV_CMD command) => new Locationwp().Set(0, 0, 0, (ushort)command);

        static double Flat(double lat, double lng) => 100;

        [TestMethod]
        public void RelativeItemsClimbInAStraightLineFromHome()
        {
            var profile = MissionProfile.Build(Home, new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50) },
                true, Flat);

            Assert.AreEqual(1113, profile.Length, 5);
            CollectionAssert.AreEqual(new[] { "H", "1" }, profile.Waypoints.Select(w => w.Label).ToArray());
            Assert.AreEqual(100, profile.Samples.First().Altitude, 0.01);
            Assert.AreEqual(150, profile.Samples.Last().Altitude, 0.01);
            Assert.AreEqual(125, profile.At(profile.Length / 2).Altitude, 0.5);
            Assert.IsTrue(profile.TerrainComplete);
        }

        [TestMethod]
        public void AbsoluteItemsUseTheirAltitudeAsIs()
        {
            var profile = MissionProfile.Build(Home,
                new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 300, MAVLink.MAV_FRAME.GLOBAL) }, true, Flat);

            Assert.AreEqual(300, profile.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void TakeoffClimbsVerticallyAtHome()
        {
            var profile = MissionProfile.Build(Home,
                new List<Locationwp> { Takeoff(30), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 30) }, true, Flat);

            Assert.AreEqual(0, profile.Samples[1].Distance, 1e-9);
            Assert.AreEqual(130, profile.Samples[1].Altitude, 0.01);
            Assert.AreEqual(130, profile.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void TerrainFrameLegsHoldTheirHeightAboveARisingGround()
        {
            // the ground climbs 100 m per leg eastwards
            Func<double, double, double> slope = (lat, lng) => 100 + (lng - Origin) / Leg * 100;
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(40),
                Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 40, MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT),
                Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 40, MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT)
            }, true, slope);

            var secondLeg = profile.Samples.Where(s => s.Distance > 1113 + 1).ToList();
            Assert.IsTrue(secondLeg.Count > 10);
            foreach (var sample in secondLeg)
                Assert.AreEqual(40, sample.Clearance, 0.5, "at " + sample.Distance);

            Assert.AreEqual(340, profile.Samples.Last().Altitude, 0.5);
        }

        [TestMethod]
        public void AHillAboveTheRouteIsAConflict()
        {
            // a 200 m hill in the middle of the leg
            Func<double, double, double> hill = (lat, lng) => Math.Abs(lng - Origin - Leg / 2) < Leg / 10 ? 300 : 100;
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50)
            }, true, hill);

            Assert.IsTrue(profile.HasConflict);
            Assert.AreEqual(150, profile.Conflict, 0.5);
            Assert.AreEqual(profile.Length / 2, profile.ConflictDistance, profile.Length / 10);
            Assert.AreEqual(-150, profile.MinClearance, 0.5);
        }

        [TestMethod]
        public void TheClimbFromHomeAndTheGlideInDoNotSetTheMinimumClearance()
        {
            // no takeoff item, so the route climbs from home on a slope, and a plane glides down to land
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 50),
                Item(MAVLink.MAV_CMD.LAND, 3 * Leg, 0)
            }, false, Flat);

            Assert.AreEqual(50, profile.MinClearance, 0.01, "only the leg between the two waypoints counts");
            Assert.AreEqual(1113 * 1.5, profile.MinClearanceDistance, 1113 * 0.6);
            Assert.IsFalse(profile.HasConflict);
        }

        [TestMethod]
        public void AHillInTheGlideIsStillAConflict()
        {
            // a hill half way down a plane's glide to land
            Func<double, double, double> hill = (lat, lng) => Math.Abs(lng - Origin - 2.5 * Leg) < Leg / 10 ? 200 : 100;
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 50),
                Item(MAVLink.MAV_CMD.LAND, 3 * Leg, 0)
            }, false, hill);

            Assert.IsTrue(profile.HasConflict);
            Assert.AreEqual(1113 * 2.5, profile.ConflictDistance, 1113 * 0.2);
            Assert.AreEqual(50, profile.MinClearance, 0.01, "the glide doesn't count towards the clearance");
        }

        [TestMethod]
        public void ClearanceNextToHomeAndTheLandingDoesNotCount()
        {
            Func<double, double, double> ground = (lat, lng) => 105;
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.LAND, 2 * Leg, 0)
            }, true, ground);

            Assert.IsFalse(profile.HasConflict);
            Assert.AreEqual(50, profile.MinClearance, 0.5);
        }

        [TestMethod]
        public void HomeSitsOnTheTerrainWhateverAltitudeItReports()
        {
            // a simulator's home at 0 m, over ground 300 m up
            Func<double, double, double> ground = (lat, lng) => 300;
            var profile = MissionProfile.Build(new PointLatLngAlt(Origin, Origin, 0), new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50)
            }, true, ground);

            Assert.AreEqual(300, profile.Waypoints[0].Altitude, 0.01, "home");
            Assert.AreEqual(350, profile.Samples.Last().Altitude, 0.01, "50 m above home");
            Assert.IsFalse(profile.HasConflict);
        }

        [TestMethod]
        public void HomesReportedAltitudeStandsInUntilTheTerrainLoads()
        {
            Func<double, double, double> loading = (lat, lng) => double.NaN;
            var profile = MissionProfile.Build(new PointLatLngAlt(Origin, Origin, 250), new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50)
            }, true, loading);

            Assert.AreEqual(250, profile.Waypoints[0].Altitude, 0.01);
            Assert.AreEqual(300, profile.Samples.Last().Altitude, 0.01);
            Assert.IsFalse(profile.TerrainComplete);
        }

        [TestMethod]
        public void AMultirotorLandsStraightDownAndAPlaneGlidesIn()
        {
            var items = new List<Locationwp> { Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.LAND, 2 * Leg, 0) };

            var copter = MissionProfile.Build(Home, items, true, Flat);
            var last = copter.Samples[copter.Samples.Count - 1];
            var before = copter.Samples[copter.Samples.Count - 2];
            Assert.AreEqual(last.Distance, before.Distance, 1e-9, "descends where it lands");
            Assert.AreEqual(150, before.Altitude, 0.01);
            Assert.AreEqual(100, last.Altitude, 0.01);

            var plane = MissionProfile.Build(Home, items, false, Flat);
            Assert.AreEqual(125, plane.At(plane.Length * 0.75).Altitude, 0.5, "half way down the glide");
            Assert.AreEqual(100, plane.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void ReturnToLaunchFliesBackHome()
        {
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Here(MAVLink.MAV_CMD.RETURN_TO_LAUNCH)
            }, true, Flat);

            Assert.AreEqual(2 * 1113, profile.Length, 10);
            Assert.AreEqual("RTL", profile.Waypoints.Last().Label);
            Assert.AreEqual(150, profile.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void CommandsWithoutAPlaceAreSkipped()
        {
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Here(MAVLink.MAV_CMD.DO_CHANGE_SPEED), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50)
            }, true, Flat);

            CollectionAssert.AreEqual(new[] { "H", "1", "3" }, profile.Waypoints.Select(w => w.Label).ToArray());
        }

        [TestMethod]
        public void GroundStillLoadingLeavesTheProfileIncomplete()
        {
            Func<double, double, double> partly = (lat, lng) => lng - Origin > Leg / 2 ? double.NaN : 100;
            var profile = MissionProfile.Build(Home, new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50)
            }, true, partly);

            Assert.IsFalse(profile.TerrainComplete);
            Assert.IsTrue(double.IsNaN(profile.Samples.Last().Terrain));
            Assert.AreEqual(150, profile.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void WithoutHomeTheRouteStartsAtTheFirstPlace()
        {
            var profile = MissionProfile.Build(new PointLatLngAlt(), new List<Locationwp>
            {
                Item(MAVLink.MAV_CMD.WAYPOINT, 0, 50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 80)
            }, true, Flat);

            CollectionAssert.AreEqual(new[] { "1", "2" }, profile.Waypoints.Select(w => w.Label).ToArray());
            Assert.AreEqual(150, profile.Samples.First().Altitude, 0.01, "relative to the ground there");
            Assert.AreEqual(180, profile.Samples.Last().Altitude, 0.01);
        }

        [TestMethod]
        public void AMissionWithNoRouteHasNoProfile()
        {
            Assert.IsNull(MissionProfile.Build(Home, new List<Locationwp>(), true, Flat));
            Assert.IsNull(MissionProfile.Build(Home, new List<Locationwp> { Takeoff(50) }, true, Flat));
        }
    }
}
