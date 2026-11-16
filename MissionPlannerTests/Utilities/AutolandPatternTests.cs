using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// An autoland sequence landing on a heading of 324 degrees, at the field in example_autoland.waypoints, with the
    /// default inputs: hold 50 m, pre-approach 6 degrees, final 3 degrees, a 25 m by 175 m box and a 60 m loiter.
    /// </summary>
    [TestClass]
    public class AutolandPatternTests
    {
        static readonly PointLatLngAlt Land = new PointLatLngAlt(34.1750448, -118.4815407, 0);
        const double Heading = 324;

        static AutolandParams Inputs()
        {
            return new AutolandParams {HeadingDeg = Heading, LoiterRadiusM = 60};
        }

        static List<Locationwp> Mission(AutolandLayout layout, int before = 0, int after = 0)
        {
            var rows = new List<Locationwp>();
            for (int i = 0; i < before; i++)
                rows.Add(Waypoint(Land.newpos(90, 500 + i * 100), 100));
            rows.AddRange(layout.Rows);
            for (int i = 0; i < after; i++)
                rows.Add(Waypoint(Land.newpos(270, 500 + i * 100), 100));
            return rows;
        }

        static Locationwp Waypoint(PointLatLngAlt at, double alt)
        {
            return new Locationwp
            {
                id = (ushort) MAVLink.MAV_CMD.WAYPOINT, lat = at.Lat, lng = at.Lng, alt = (float) alt, frame = 3
            };
        }

        static void Local(AutolandLayout layout, Locationwp row, out double along, out double right)
        {
            layout.ToLocal(new PointLatLngAlt(row.lat, row.lng, 0), out along, out right);
        }

        [TestMethod]
        public void TheRowsAreTheSequenceInOrder()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            CollectionAssert.AreEqual(new[]
                {
                    (ushort) MAVLink.MAV_CMD.DO_LAND_START, (ushort) MAVLink.MAV_CMD.LOITER_TO_ALT,
                    (ushort) MAVLink.MAV_CMD.LOITER_TIME, (ushort) MAVLink.MAV_CMD.WAYPOINT,
                    (ushort) MAVLink.MAV_CMD.WAYPOINT, (ushort) MAVLink.MAV_CMD.WAYPOINT,
                    (ushort) MAVLink.MAV_CMD.WAYPOINT, (ushort) MAVLink.MAV_CMD.LAND
                },
                layout.Rows.Select(r => r.id).ToArray());
            Assert.IsTrue(layout.Rows.All(r => r.frame == (byte) MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT));
            Assert.AreEqual(0, layout.Warnings.Count);
        }

        [TestMethod]
        public void TheLoiterRowsWaitForTheHeadingAndUseTheVehiclesRadius()
        {
            var rows = AutolandPattern.Generate(Land, Inputs()).Rows;

            var toAlt = rows[AutolandPattern.RowLoiterToAlt];
            Assert.AreEqual(1f, toAlt.p1);
            Assert.AreEqual(0f, toAlt.p2);
            Assert.AreEqual(0f, toAlt.p4);

            var time = rows[AutolandPattern.RowLoiterTime];
            Assert.AreEqual(60f, time.p1);
            Assert.AreEqual(1f, time.p2);
            Assert.AreEqual(0f, time.p3);
            Assert.AreEqual(0f, time.p4);
        }

        [TestMethod]
        public void AnAnticlockwiseVehicleIsGivenAClockwiseRadius()
        {
            var p = Inputs();
            p.WriteClockwiseRadius = true;
            var rows = AutolandPattern.Generate(Land, p).Rows;

            Assert.AreEqual(60f, rows[AutolandPattern.RowLoiterToAlt].p2);
            Assert.AreEqual(60f, rows[AutolandPattern.RowLoiterTime].p3);
        }

        [TestMethod]
        public void TheAltitudesFollowTheAnglesWithAFlatBase()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());
            var rows = layout.Rows;

            double zone = 87.5 * Math.Tan(3 * MathHelper.deg2rad);
            Assert.AreEqual(zone, layout.ZoneAltM, 1e-9);
            Assert.AreEqual(zone, rows[AutolandPattern.RowZoneStart].alt, 0.01);

            // a third of the descent on downwind
            double baseAlt = 50 - (50 - zone) / 3;
            Assert.AreEqual(baseAlt, layout.BaseAltM, 1e-9);
            Assert.AreEqual(rows[AutolandPattern.RowDownwindEnd].alt, rows[AutolandPattern.RowBaseEnd].alt);
            Assert.AreEqual(baseAlt, rows[AutolandPattern.RowBaseEnd].alt, 0.01);

            for (int i = AutolandPattern.RowLandStart; i <= AutolandPattern.RowDownwindStart; i++)
                Assert.AreEqual(50f, rows[i].alt);
            Assert.AreEqual(0f, rows[AutolandPattern.RowLand].alt);

            // the base end to the landing zone start is flown at the pre-approach angle
            Local(layout, rows[AutolandPattern.RowBaseEnd], out var baseAlong, out var baseRight);
            Assert.AreEqual(0, baseRight, 0.01);
            double slope = Math.Atan((baseAlt - zone) / (-baseAlong - 87.5)) * MathHelper.rad2deg;
            Assert.AreEqual(AutolandPattern.DefaultPreApproachDeg, slope, 0.01);
            Assert.AreEqual(4.5, AutolandPattern.DefaultPreApproachDeg);
            Assert.AreEqual(-baseAlong, layout.BaseDistanceM, 0.01);
        }

        [TestMethod]
        public void TheLandingZoneStartsAtTheBoxsNearEndAndLandIsInItsMiddle()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            Local(layout, layout.Rows[AutolandPattern.RowZoneStart], out var along, out var right);
            Assert.AreEqual(-87.5, along, 0.01);
            Assert.AreEqual(0, right, 0.01);

            Assert.IsTrue(layout.BoxContains(Land));
            Assert.IsTrue(layout.BoxContains(Land.newpos(Heading, 85)));
            Assert.IsFalse(layout.BoxContains(Land.newpos(Heading, 90)));
            Assert.IsTrue(layout.BoxContains(Land.newpos(Heading + 90, 12)));
            Assert.IsFalse(layout.BoxContains(Land.newpos(Heading + 90, 13)));
        }

        [TestMethod]
        public void TheLoiterTouchesDownwindAbeamTheBoxsFarEnd()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());
            var rows = layout.Rows;

            Local(layout, rows[AutolandPattern.RowDownwindStart], out var startAlong, out var startRight);
            Local(layout, rows[AutolandPattern.RowLoiterToAlt], out var loiterAlong, out var loiterRight);
            Local(layout, rows[AutolandPattern.RowDownwindEnd], out var endAlong, out var endRight);

            // downwind is on the right of the landing direction
            Assert.AreEqual(170, startRight, 0.01);
            Assert.AreEqual(170, endRight, 0.01);
            Assert.AreEqual(87.5, startAlong, 0.01);

            // the loiter center is a radius in from the downwind start, so the clockwise loiter leaves along downwind
            Assert.AreEqual(87.5, loiterAlong, 0.01);
            Assert.AreEqual(110, loiterRight, 0.01);
            Assert.AreEqual(-layout.BaseDistanceM, endAlong, 0.01);

            // every loiter row is at the loiter center
            for (int i = AutolandPattern.RowLandStart; i <= AutolandPattern.RowLoiterTime; i++)
            {
                Assert.AreEqual(rows[AutolandPattern.RowLoiterToAlt].lat, rows[i].lat);
                Assert.AreEqual(rows[AutolandPattern.RowLoiterToAlt].lng, rows[i].lng);
            }
        }

        [TestMethod]
        public void ASequenceIsRecognizedWithItsInputs()
        {
            var p = Inputs();
            p.HoldAltM = 80;
            p.PreApproachDeg = 5.5;
            p.FinalApproachDeg = 2.5;
            p.LoiterTimeS = 45;
            p.DownwindOffsetM = 220;
            p.BoxLengthM = 200;
            var layout = AutolandPattern.Generate(Land, p);

            var found = AutolandPattern.FindAll(Mission(layout, 3, 2), _ => 30);

            Assert.AreEqual(1, found.Count);
            Assert.AreEqual(3, found[0].Key);
            var read = found[0].Value.Params;
            Assert.AreEqual(Heading, read.HeadingDeg, 0.01);
            Assert.AreEqual(80, read.HoldAltM, 0.01);
            Assert.AreEqual(5.5, read.PreApproachDeg, 0.01);
            Assert.AreEqual(2.5, read.FinalApproachDeg, 0.01);
            Assert.AreEqual(45, read.LoiterTimeS, 0.01);
            Assert.AreEqual(220, read.DownwindOffsetM, 0.01);
            Assert.AreEqual(200, read.BoxLengthM, 0.01);
            Assert.AreEqual(30, read.BoxWidthM);
            Assert.AreEqual(60, read.LoiterRadiusM, 0.01);
            Assert.AreEqual(Land.Lat, found[0].Value.Land.Lat, 1e-9);
            Assert.AreEqual(Land.Lng, found[0].Value.Land.Lng, 1e-9);
        }

        [TestMethod]
        public void ASequenceReadBackAndLaidOutAgainStaysPut()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());
            var rows = layout.Rows;

            for (int i = 0; i < 20; i++)
            {
                var again = AutolandPattern.TryRecognize(rows, 0, 25);
                Assert.IsNotNull(again);
                rows = AutolandPattern.Generate(again.Land, again.Params).Rows;
            }

            for (int i = 0; i < AutolandPattern.RowCount; i++)
            {
                Assert.AreEqual(layout.Rows[i].lat, rows[i].lat, 1e-7);
                Assert.AreEqual(layout.Rows[i].lng, rows[i].lng, 1e-7);
                Assert.AreEqual(layout.Rows[i].alt, rows[i].alt, 0.02);
            }
        }

        [TestMethod]
        public void TwoSequencesAreBothFound()
        {
            var first = AutolandPattern.Generate(Land, Inputs());
            var other = Inputs();
            other.HeadingDeg = 144;
            var second = AutolandPattern.Generate(Land.newpos(0, 2000), other);

            var mission = first.Rows.Concat(second.Rows).ToList();
            var found = AutolandPattern.FindAll(mission);

            CollectionAssert.AreEqual(new[] {0, 8}, found.Select(f => f.Key).ToArray());
            Assert.AreEqual(144, found[1].Value.Params.HeadingDeg, 0.01);
        }

        [TestMethod]
        public void ARowEditedByHandLeavesPlainRows()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            var moved = layout.Rows.ToList();
            var row = moved[AutolandPattern.RowDownwindEnd];
            row.alt += 5;
            moved[AutolandPattern.RowDownwindEnd] = row;
            Assert.AreEqual(0, AutolandPattern.FindAll(moved).Count);

            var shifted = layout.Rows.ToList();
            var downwind = shifted[AutolandPattern.RowDownwindStart];
            var to = new PointLatLngAlt(downwind.lat, downwind.lng, 0).newpos(0, 10);
            downwind.lat = to.Lat;
            downwind.lng = to.Lng;
            shifted[AutolandPattern.RowDownwindStart] = downwind;
            Assert.AreEqual(0, AutolandPattern.FindAll(shifted).Count);

            var other = layout.Rows.ToList();
            var land = other[AutolandPattern.RowLand];
            land.id = (ushort) MAVLink.MAV_CMD.WAYPOINT;
            other[AutolandPattern.RowLand] = land;
            Assert.AreEqual(0, AutolandPattern.FindAll(other).Count);
        }

        [TestMethod]
        public void TheExampleFileIsNotTakenForASequence()
        {
            // example_autoland.waypoints has the sequence's commands but not its geometry (all loiter params 0)
            var land = new PointLatLngAlt(34.17504480, -118.48154070, 0);
            Locationwp Row(MAVLink.MAV_CMD cmd, double lat, double lng, double alt) => new Locationwp
                {id = (ushort) cmd, lat = lat, lng = lng, alt = (float) alt, frame = 3};
            var rows = new List<Locationwp>
            {
                Row(MAVLink.MAV_CMD.DO_LAND_START, 34.17722563, -118.48193193, 50),
                Row(MAVLink.MAV_CMD.LOITER_TO_ALT, 34.17721662, -118.48193177, 50),
                Row(MAVLink.MAV_CMD.LOITER_TIME, 34.17720760, -118.48193162, 50),
                Row(MAVLink.MAV_CMD.WAYPOINT, 34.17612770, -118.48018880, 50),
                Row(MAVLink.MAV_CMD.WAYPOINT, 34.17289670, -118.47748520, 35),
                Row(MAVLink.MAV_CMD.WAYPOINT, 34.17215110, -118.47905160, 35),
                Row(MAVLink.MAV_CMD.WAYPOINT, 34.17435690, -118.48093990, 5),
                Row(MAVLink.MAV_CMD.LAND, land.Lat, land.Lng, 0),
            };

            Assert.AreEqual(0, AutolandPattern.FindAll(rows).Count);
        }

        [TestMethod]
        public void InputsOutOfRangeAreClampedAndSaidSo()
        {
            var p = Inputs();
            p.DownwindOffsetM = 20;
            p.HoldAltM = 2;
            p.PreApproachDeg = 0;
            p.BoxLengthM = 10;
            var layout = AutolandPattern.Generate(Land, p);

            Assert.AreEqual(60, layout.Params.DownwindOffsetM);
            Assert.AreEqual(40, layout.Params.BoxLengthM);
            Assert.AreEqual(AutolandPattern.MinAngleDeg, layout.Params.PreApproachDeg);
            Assert.IsTrue(layout.Params.HoldAltM >= layout.ZoneAltM + AutolandPattern.MinDescentM);
            Assert.AreEqual(4, layout.Warnings.Count);
        }

        [TestMethod]
        public void DraggingTheHandles()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            var moved = AutolandPattern.Drag(layout, AutolandHandle.Move, Land.newpos(0, 100), out var land);
            Assert.AreEqual(100, Land.GetDistance(land), 0.5);
            Assert.AreEqual(Heading, moved.HeadingDeg, 1e-9);

            // dragging the base end round to due south of LAND lands heading north
            var turned = AutolandPattern.Drag(layout, AutolandHandle.Rotate, Land.newpos(180, 400), out land);
            Assert.AreEqual(0, Math.Min(turned.HeadingDeg, 360 - turned.HeadingDeg), 0.01);
            Assert.AreEqual(Land.Lat, land.Lat);

            var corner = AutolandPattern.Drag(layout, AutolandHandle.Corner,
                AutolandPattern.FromLocal(Land, Heading, -100, 20, 0), out land);
            Assert.AreEqual(200, corner.BoxLengthM, 0.01);
            Assert.AreEqual(40, corner.BoxWidthM, 0.01);

            var offset = AutolandPattern.Drag(layout, AutolandHandle.Offset,
                AutolandPattern.FromLocal(Land, Heading, -200, 250, 0), out land);
            Assert.AreEqual(250, offset.DownwindOffsetM, 0.01);

            var tooClose = AutolandPattern.Drag(layout, AutolandHandle.Offset,
                AutolandPattern.FromLocal(Land, Heading, -200, -50, 0), out land);
            Assert.AreEqual(60, tooClose.DownwindOffsetM, 0.01);
        }

        [TestMethod]
        public void TheBaseLegClearsTheObstacleHeightByTheMargin()
        {
            foreach (var obstacle in new[] {0.0, 25, 60})
            {
                var p = Inputs();
                p.HoldAltM = AutolandPattern.HoldForObstacle(p, obstacle);
                var layout = AutolandPattern.Generate(Land, p);

                Assert.AreEqual(obstacle + AutolandPattern.ObstacleMarginM, layout.BaseAltM, 1e-9);
                Assert.AreEqual(obstacle, layout.ObstacleHeightM, 1e-9);
                Assert.AreEqual(0, layout.Warnings.Count);
            }

            // the default obstacles give about the example's heights: base 35 m, hold 50 m
            var defaults = Inputs();
            double hold = AutolandPattern.HoldForObstacle(defaults, AutolandPattern.DefaultObstacleHeightM);
            Assert.AreEqual(50.2, hold, 0.1);
        }

        [TestMethod]
        public void TheObstacleHeightIsShownAtTheMiddleOfTheBaseLeg()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            layout.ToLocal(layout.BaseMiddle, out var along, out var right);
            Assert.AreEqual(-layout.BaseDistanceM, along, 0.01);
            Assert.AreEqual(85, right, 0.01);
            Assert.AreEqual(layout.BaseAltM, layout.BaseMiddle.Alt, 1e-9);
        }

        [TestMethod]
        public void ThreeAltitudePointsRunEvenlyDownThePreApproach()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());

            Assert.AreEqual(3, layout.ApproachPoints.Count);
            double half = 87.5;
            for (int i = 0; i < 3; i++)
            {
                double f = (i + 1) / 4.0;
                var point = layout.ApproachPoints[i];
                layout.ToLocal(point, out var along, out var right);

                Assert.AreEqual(-layout.BaseDistanceM + (layout.BaseDistanceM - half) * f, along, 0.01);
                Assert.AreEqual(0, right, 0.01);
                Assert.AreEqual(layout.BaseAltM + (layout.ZoneAltM - layout.BaseAltM) * f, point.Alt, 1e-9);
            }

            // they are drawn, not flown: the rows are still the eight
            Assert.AreEqual(AutolandPattern.RowCount, layout.Rows.Count);

            // their tags go to the left of the final approach line, away from downwind; the obstacle's before base
            layout.ToLocal(layout.Outside(layout.ApproachPoints[1], false), out var tagAlong, out var tagRight);
            Assert.AreEqual(-50, tagRight, 0.01);
            layout.ToLocal(layout.Outside(layout.BaseMiddle, true), out tagAlong, out tagRight);
            Assert.AreEqual(-layout.BaseDistanceM - 50, tagAlong, 0.01);
        }

        [TestMethod]
        public void SteepAnglesAreWarnedAboutButKept()
        {
            var gentle = AutolandPattern.Generate(Land, Inputs());
            Assert.AreEqual(0, gentle.Advisories.Count);

            var p = Inputs();
            p.PreApproachDeg = 6;
            p.FinalApproachDeg = 3;
            Assert.AreEqual(0, AutolandPattern.Generate(Land, p).Advisories.Count);

            // the rounding an angle read back from the rows carries is not steep
            p.PreApproachDeg = 6.04;
            p.FinalApproachDeg = 3.04;
            Assert.AreEqual(0, AutolandPattern.Generate(Land, p).Advisories.Count);

            p.PreApproachDeg = 6.5;
            p.FinalApproachDeg = 3.5;
            var steep = AutolandPattern.Generate(Land, p);

            Assert.IsTrue(steep.PreApproachSteep);
            Assert.IsTrue(steep.FinalApproachSteep);
            Assert.AreEqual(2, steep.Advisories.Count);
            Assert.AreEqual(0, steep.Warnings.Count);
            Assert.AreEqual(6.5, steep.Params.PreApproachDeg);

            // a steep sequence is still a sequence when read back
            var read = AutolandPattern.FindAll(steep.Rows);
            Assert.AreEqual(1, read.Count);
            Assert.AreEqual(6.5, read[0].Value.Params.PreApproachDeg, 0.01);
            Assert.AreEqual(3.5, read[0].Value.Params.FinalApproachDeg, 0.01);
        }

        [TestMethod]
        public void AConvertedSequenceIsLeftAsPlainWaypoints()
        {
            var first = AutolandPattern.Generate(Land, Inputs());
            var other = Inputs();
            other.HeadingDeg = 144;
            var second = AutolandPattern.Generate(Land.newpos(0, 2000), other);
            var mission = first.Rows.Concat(second.Rows).ToList();

            var converted = new List<string> {AutolandPattern.Fingerprint(mission, 0)};
            var found = AutolandPattern.FindAll(mission, null, converted);

            // only the other sequence is still an autoland; the converted one's rows are untouched
            CollectionAssert.AreEqual(new[] {8}, found.Select(f => f.Key).ToArray());
            Assert.AreEqual(16, mission.Count);
        }

        [TestMethod]
        public void AConvertedSequenceStaysConvertedThroughAFileOrTheVehicle()
        {
            var layout = AutolandPattern.Generate(Land, Inputs());
            var converted = new List<string> {AutolandPattern.Fingerprint(layout.Rows, 0)};

            // a .waypoints file keeps 8 decimals; the vehicle keeps 1e-7 degrees
            foreach (var scale in new[] {1e8, 1e7})
            {
                var rounded = layout.Rows.Select(r =>
                {
                    r.lat = Math.Round(r.lat * scale) / scale;
                    r.lng = Math.Round(r.lng * scale) / scale;
                    return r;
                }).ToList();
                Assert.AreEqual(0, AutolandPattern.FindAll(rounded, null, converted).Count);
            }

            // moved a meter, it's another sequence
            var moved = AutolandPattern.Generate(Land.newpos(90, 1), Inputs());
            Assert.AreEqual(1, AutolandPattern.FindAll(moved.Rows, null, converted).Count);
            Assert.IsFalse(AutolandPattern.SameFingerprint(converted[0], "not a fingerprint"));
        }

        [TestMethod]
        public void ANewSequenceLandsAlongTheTakeoff()
        {
            var home = new PointLatLngAlt(34.17, -118.48, 0);
            var cmds = new List<Locationwp>
            {
                new Locationwp {id = (ushort) MAVLink.MAV_CMD.TAKEOFF},
                Waypoint(home.newpos(75, 10), 50),
                Waypoint(home.newpos(75, 300), 50),
            };

            Assert.AreEqual(75, AutolandPattern.TakeoffHeading(home, cmds), 0.01);
            Assert.AreEqual(0, AutolandPattern.TakeoffHeading(home, new List<Locationwp>()));
            Assert.AreEqual(0, AutolandPattern.TakeoffHeading(null, cmds));
        }
    }
}
