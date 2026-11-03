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

        static Locationwp At(MAVLink.MAV_CMD command, double lat, double lng, double alt)
        {
            var item = new Locationwp().Set(lat, lng, alt, (ushort)command);
            item.frame = (byte)MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT;
            return item;
        }

        static Locationwp RallyPoint(double lat, double lng, double alt) => At(MAVLink.MAV_CMD.RALLY_POINT, lat, lng, alt);

        static MissionProfile Build(List<Locationwp> mission, List<Locationwp> rally = null, MissionFence fence = null,
            Func<double, double, double> terrain = null)
        {
            return MissionProfile.Build(new ProfileInput
            {
                Home = Home, Mission = mission, Rally = rally ?? new List<Locationwp>(), Fence = fence,
                VehicleClass = VehicleClass.Copter
            }, terrain ?? Flat);
        }

        // a vehicle's parameters from name, value pairs
        static Func<string, double?> Params(params object[] pairs)
        {
            var values = new Dictionary<string, double>();
            for (int i = 0; i < pairs.Length; i += 2)
                values[(string)pairs[i]] = Convert.ToDouble(pairs[i + 1]);
            return name => values.ContainsKey(name) ? values[name] : (double?)null;
        }

        [TestMethod]
        public void RallyPointsSitAtTheRoutesNearestPoint()
        {
            // 0.002 degrees (about 221 m) north of the middle of the leg
            var profile = Build(new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50) },
                new List<Locationwp> { Item(MAVLink.MAV_CMD.DO_SET_SERVO, 0, 0), RallyPoint(Origin + 0.002, Origin + Leg / 2, 30) });

            var rally = profile.RallyPoints.Single();
            Assert.IsFalse(profile.RallyChain);
            Assert.AreEqual("R2", rally.Label, "numbered by its row");
            Assert.AreEqual(profile.Length / 2, rally.Distance, 5);
            Assert.AreEqual(221, rally.OffRoute, 3);
            Assert.AreEqual(130, rally.Altitude, 0.01, "relative to home");
            Assert.AreEqual(30, rally.Altitude - rally.Terrain, 0.01);
        }

        [TestMethod]
        public void WithoutAMissionTheGroundRunsThroughTheRallyPoints()
        {
            Func<double, double, double> slope = (lat, lng) => 100 + (lng - Origin) / Leg * 100;
            var profile = Build(new List<Locationwp>(),
                new List<Locationwp> { RallyPoint(Origin, Origin + Leg, 30), RallyPoint(Origin, Origin + 2 * Leg, 120) },
                terrain: slope);

            Assert.IsTrue(profile.RallyChain);
            Assert.AreEqual(2226, profile.Length, 10);
            Assert.AreEqual(1113, profile.RallyPoints[0].Distance, 5);
            Assert.AreEqual(2226, profile.RallyPoints[1].Distance, 10);
            Assert.IsTrue(profile.Samples.All(s => double.IsNaN(s.Altitude)), "nothing flies along the line");
            Assert.AreEqual(300, profile.Samples.Last().Terrain, 0.5);

            // R1 is 130 m over ground at 200, R2 220 m over ground at 300: the lowest is R2's
            Assert.AreEqual(-80, profile.MinClearance, 0.5);
            Assert.IsFalse(profile.HasConflict);
        }

        [TestMethod]
        public void PointsAboveTheCeilingAreOutsideTheFence()
        {
            var fence = MissionFence.FromParameters(
                Params("FENCE_TYPE", 1, "FENCE_ALT_MAX", 100, "FENCE_ENABLE", 1, "FENCE_ACTION", 1), null, null);
            var profile = Build(new List<Locationwp>
            {
                Takeoff(50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 80), Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 120),
                Item(MAVLink.MAV_CMD.LAND, 2 * Leg, 0)
            }, fence: fence);

            Assert.AreEqual(200, profile.FenceCeiling, 0.01, "measured from home");
            CollectionAssert.AreEqual(new[] { "3" }, profile.OutsideFence.Select(w => w.Label).ToArray());
            Assert.AreEqual(FenceBreach.AboveCeiling, profile.OutsideFence[0].Breach);
        }

        [TestMethod]
        public void TheFloorCountsOnceTheRouteHasBeenAboveIt()
        {
            var fence = MissionFence.FromParameters(
                Params("FENCE_TYPE", 8, "FENCE_ALT_MIN", 20, "FENCE_ENABLE", 1, "FENCE_ACTION", 1), null, null);
            var profile = Build(new List<Locationwp>
            {
                Takeoff(10), Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 10)
            }, fence: fence);

            // the takeoff to 10 m is under the floor before it has armed; the last waypoint is under it after
            CollectionAssert.AreEqual(new[] { "3" }, profile.OutsideFence.Select(w => w.Label).ToArray());
            Assert.AreEqual(FenceBreach.BelowFloor, profile.OutsideFence[0].Breach);
        }

        [TestMethod]
        public void TheRouteCrossesTheCircleOutAndBackIn()
        {
            var fence = MissionFence.FromParameters(
                Params("FENCE_TYPE", 2, "FENCE_RADIUS", 1500, "FENCE_ENABLE", 1, "FENCE_ACTION", 1), null, null);
            var profile = Build(new List<Locationwp>
            {
                Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 50),
                Item(MAVLink.MAV_CMD.WAYPOINT, Leg / 2, 50)
            }, fence: fence);

            Assert.AreEqual(2, profile.FenceCircleCrossings.Count);
            Assert.AreEqual(1500, profile.FenceCircleCrossings[0], 5);
            Assert.AreEqual(2226 + (2226 - 1500), profile.FenceCircleCrossings[1], 10);
            CollectionAssert.AreEqual(new[] { "2" }, profile.OutsideFence.Select(w => w.Label).ToArray());
            Assert.AreEqual(FenceBreach.OutsideCircle, profile.OutsideFence[0].Breach);
        }

        [TestMethod]
        public void AFenceThatIsOffDrawsItsLimitsButFlagsNothing()
        {
            var fence = MissionFence.FromParameters(
                Params("FENCE_TYPE", 3, "FENCE_ALT_MAX", 100, "FENCE_RADIUS", 500, "FENCE_ENABLE", 0, "FENCE_ACTION", 1),
                null, null);
            var profile = Build(new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 150) }, fence: fence);

            Assert.AreEqual(FenceState.Disabled, fence.State);
            Assert.AreEqual(200, profile.FenceCeiling, 0.01);
            Assert.IsFalse(fence.Enforces(fence.Ceiling));
            Assert.AreEqual(0, profile.FenceCircleCrossings.Count);
            Assert.AreEqual(0, profile.OutsideFence.Count);
        }

        [TestMethod]
        public void PolygonFencesHoldInclusionsAndExclusions()
        {
            // a square about home, 0.015 degrees each way, and an exclusion circle around the first waypoint
            var items = new List<Locationwp>();
            foreach (var corner in new[] { new[] { -1, -1 }, new[] { -1, 1 }, new[] { 1, 1 }, new[] { 1, -1 } })
            {
                var vertex = At(MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION, Origin + corner[0] * 0.015,
                    Origin + corner[1] * 0.015, 0);
                vertex.p1 = 4;
                items.Add(vertex);
            }

            var circle = At(MAVLink.MAV_CMD.FENCE_CIRCLE_EXCLUSION, Origin, Origin + Leg, 0);
            circle.p1 = 100;
            items.Add(circle);

            var fence = new MissionFence { Areas = MissionFence.AreasOf(items) };
            var profile = Build(new List<Locationwp>
            {
                Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50), Item(MAVLink.MAV_CMD.WAYPOINT, Leg * 1.2, 50),
                Item(MAVLink.MAV_CMD.WAYPOINT, 2 * Leg, 50)
            }, fence: fence);

            Assert.AreEqual(2, fence.Areas.Count);
            Assert.IsNull(fence.State, "offline, the state isn't known and counts as on");
            CollectionAssert.AreEqual(new[] { "1", "3" }, profile.OutsideFence.Select(w => w.Label).ToArray());
            Assert.AreEqual(FenceBreach.InsideExclusion, profile.OutsideFence[0].Breach);
            Assert.AreEqual(FenceBreach.OutsideInclusion, profile.OutsideFence[1].Breach);
        }

        [TestMethod]
        public void TheUnionOfInclusionsNeedsOnlyOne()
        {
            Func<double, List<Locationwp>> square = east =>
            {
                var items = new List<Locationwp>();
                foreach (var corner in new[] { new[] { -1, -1 }, new[] { -1, 1 }, new[] { 1, 1 }, new[] { 1, -1 } })
                {
                    var vertex = At(MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION, Origin + corner[0] * 0.005,
                        Origin + east + corner[1] * 0.005, 0);
                    vertex.p1 = 4;
                    items.Add(vertex);
                }

                return items;
            };

            var areas = MissionFence.AreasOf(square(0).Concat(square(Leg)).ToList());
            Assert.AreEqual(2, areas.Count);

            var mission = new List<Locationwp> { Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50) };
            var all = Build(mission, fence: MissionFence.FromParameters(
                Params("FENCE_TYPE", 4, "FENCE_ENABLE", 1, "FENCE_ACTION", 1), null, areas));
            var union = Build(mission, fence: MissionFence.FromParameters(
                Params("FENCE_TYPE", 4, "FENCE_OPTIONS", 2, "FENCE_ENABLE", 1, "FENCE_ACTION", 1), null, areas));

            Assert.AreEqual(1, all.OutsideFence.Count, "outside the square about home");
            Assert.AreEqual(0, union.OutsideFence.Count, "inside the other one");
        }

        [TestMethod]
        public void OtherItemsDoNotSplitAPolygon()
        {
            var items = new List<Locationwp>();
            for (int i = 0; i < 3; i++)
            {
                var vertex = At(MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION, Origin + i * 0.001, Origin + (i % 2) * 0.001, 0);
                vertex.p1 = 3;
                items.Add(vertex);
                if (i == 0)
                    items.Add(Item(MAVLink.MAV_CMD.WAYPOINT, Leg, 50));
            }

            var areas = MissionFence.AreasOf(items);
            Assert.AreEqual(1, areas.Count);
            Assert.AreEqual(3, areas[0].Vertices.Count);
        }

        [TestMethod]
        public void TheFenceStateFollowsArduPilot()
        {
            Assert.AreEqual(FenceState.ReportOnly,
                MissionFence.StateOf(Params("FENCE_ENABLE", 1, "FENCE_ACTION", 0), true), "action 0 only reports");
            Assert.AreEqual(FenceState.ReportOnly,
                MissionFence.StateOf(Params("FENCE_ENABLE", 0, "FENCE_AUTOENABLE", 3, "FENCE_ACTION", 0), null));
            Assert.AreEqual(FenceState.EnablesWhenArmed,
                MissionFence.StateOf(Params("FENCE_ENABLE", 0, "FENCE_AUTOENABLE", 3, "FENCE_ACTION", 1), null));
            Assert.AreEqual(FenceState.EnablesAfterTakeoff,
                MissionFence.StateOf(Params("FENCE_ENABLE", 0, "FENCE_AUTOENABLE", 1, "FENCE_ACTION", 1), false));
            Assert.AreEqual(FenceState.Enabled,
                MissionFence.StateOf(Params("FENCE_ENABLE", 0, "FENCE_ACTION", 1), true), "an RC switch turned it on");
            Assert.AreEqual(FenceState.Disabled,
                MissionFence.StateOf(Params("FENCE_ENABLE", 1, "FENCE_ACTION", 1), false), "the vehicle says it is off");
            Assert.AreEqual(FenceState.Disabled,
                MissionFence.StateOf(Params("FENCE_ACTION", 0), null), "older Plane");
        }

        [TestMethod]
        public void OlderPlaneLimitsNeedTheMaximumAboveTheMinimum()
        {
            var fence = MissionFence.FromParameters(Params("FENCE_MAXALT", 120, "FENCE_MINALT", 30, "FENCE_ACTION", 1),
                null, null);
            Assert.IsTrue(fence.Ceiling.Used && fence.Floor.Used);

            fence = MissionFence.FromParameters(Params("FENCE_MAXALT", 0, "FENCE_MINALT", 30, "FENCE_ACTION", 1),
                null, null);
            Assert.IsFalse(fence.Ceiling.Used || fence.Floor.Used);
        }

        [TestMethod]
        public void SplineLegsFollowTheMapsCurve()
        {
            var mission = new List<Locationwp>
            {
                Takeoff(30),
                At(MAVLink.MAV_CMD.SPLINE_WAYPOINT, Origin + 0.005, Origin + Leg, 30),
                At(MAVLink.MAV_CMD.SPLINE_WAYPOINT, Origin - 0.005, Origin + 2 * Leg, 30),
                At(MAVLink.MAV_CMD.WAYPOINT, Origin, Origin + 3 * Leg, 30)
            };
            var profile = Build(mission);

            double straight = 0;
            var points = new[] { Home }.Concat(mission.Skip(1).Select(a => new PointLatLngAlt(a.lat, a.lng))).ToList();
            for (int i = 1; i < points.Count; i++)
                straight += points[i - 1].GetDistance(points[i]);

            Assert.IsTrue(profile.Length > straight + 1, "the curves are longer than the straight legs");
            var distances = profile.Waypoints.Select(w => w.Distance).ToList();
            for (int i = 1; i < distances.Count; i++)
                Assert.IsTrue(distances[i] >= distances[i - 1]);

            // the samples follow the curve, so some are off the straight line from home to the first spline waypoint
            Assert.IsTrue(profile.Samples.Any(s => s.Distance > 0 && s.Distance < profile.Waypoints[2].Distance &&
                                                   Math.Abs(s.Lat - (Origin + 0.005 * (s.Lng - Origin) / Leg)) > 1e-5));
        }
    }
}
