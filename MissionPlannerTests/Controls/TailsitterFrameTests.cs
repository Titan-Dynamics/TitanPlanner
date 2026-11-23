using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.Scene3D.Tests
{
    [TestClass]
    public class TailsitterFrameTests
    {
        static readonly DateTime Start = new DateTime(2026, 1, 1);

        // the report of an airframe pitched airframePitch, in the hover frame (angle nose down from it) or its own
        static double Reported(double airframePitch, bool hover, double angle = 90)
        {
            return hover ? airframePitch - angle : airframePitch;
        }

        [TestMethod]
        public void EulerRoundTripsAndPitchingUpNinetyPutsTheNoseUp()
        {
            var m = new double[9];
            EulerRotation.FromEuler(20, -35, 140, m);
            double roll, pitch, yaw;
            EulerRotation.ToEuler(m, out roll, out pitch, out yaw);
            Assert.AreEqual(20, roll, 1e-9);
            Assert.AreEqual(-35, pitch, 1e-9);
            Assert.AreEqual(140, yaw, 1e-9);

            EulerRotation.FromEuler(0, 0, 0, m);
            EulerRotation.PitchedUp(m, 90, m);
            // the nose (the first column) points up, north-east-down
            Assert.AreEqual(0, m[0], 1e-9);
            Assert.AreEqual(0, m[3], 1e-9);
            Assert.AreEqual(-1, m[6], 1e-9);

            var n = new double[9];
            EulerRotation.FromEuler(0, 0, 0, n);
            Assert.AreEqual(90, EulerRotation.Between(m, n), 1e-6);
        }

        [TestMethod]
        public void FirstTakesTheFrameItsStateSays()
        {
            var frame = new TailsitterFrame();
            frame.Update(TailsitterFrame.Expect.Hover, 90, 0, 0, 0, Start);
            Assert.IsTrue(frame.Hover);

            frame.Clear();
            frame.Update(TailsitterFrame.Expect.Wing, 90, 0, 0, 0, Start);
            Assert.IsFalse(frame.Hover);

            frame.Clear();
            frame.Update(TailsitterFrame.Expect.Either, 90, 0, 0, 0, Start);
            Assert.IsFalse(frame.Hover);
        }

        [TestMethod]
        public void TheHoverFrameIsFoundWhereTheTransitionIntoHoverEnds()
        {
            foreach (double angle in new[] { 90.0, 93.5 })
            {
                var frame = new TailsitterFrame();
                var now = Start;
                // level on the wing, then a Q mode: still reported on the wing while the nose comes up
                for (double airframe = 0; airframe <= 50; airframe += 5)
                {
                    frame.Update(airframe == 0 ? TailsitterFrame.Expect.Wing : TailsitterFrame.Expect.Hover, angle, 0,
                        Reported(airframe, false), 0, now);
                    Assert.IsFalse(frame.Hover, "on the wing at " + airframe);
                    now = now.AddMilliseconds(100);
                }

                // the transition done, the same airframe moving on is reported in the hover frame
                for (double airframe = 55; airframe <= 90; airframe += 5)
                {
                    frame.Update(TailsitterFrame.Expect.Hover, angle, 0, Reported(airframe, true, angle), 0, now);
                    Assert.IsTrue(frame.Hover, "hovering at " + airframe);
                    now = now.AddMilliseconds(100);
                }
            }
        }

        [TestMethod]
        public void TheWingFrameIsFoundWhereTheTransitionOutOfHoverEnds()
        {
            var frame = new TailsitterFrame();
            var now = Start;
            // a wing mode: still reported in the hover frame while the nose comes down
            for (double airframe = 90; airframe >= 50; airframe -= 5)
            {
                frame.Update(airframe == 90 ? TailsitterFrame.Expect.Hover : TailsitterFrame.Expect.Wing, 90, 3,
                    Reported(airframe, true), 250, now);
                Assert.IsTrue(frame.Hover, "hovering at " + airframe);
                now = now.AddMilliseconds(100);
            }

            for (double airframe = 45; airframe >= 0; airframe -= 5)
            {
                frame.Update(TailsitterFrame.Expect.Wing, 90, 3, Reported(airframe, false), 250, now);
                Assert.IsFalse(frame.Hover, "on the wing at " + airframe);
                now = now.AddMilliseconds(100);
            }
        }

        [TestMethod]
        public void AChangeIsFoundBeforeTheModeShowsIt()
        {
            // disarmed on its tail in a wing mode, switched to QHOVER: the report changes frame straight away, the
            // heartbeat with the mode comes up to a second later
            var frame = new TailsitterFrame();
            frame.Update(TailsitterFrame.Expect.Wing, 90, 0, 88, 40, Start);
            Assert.IsFalse(frame.Hover);

            frame.Update(TailsitterFrame.Expect.Wing, 90, 0, Reported(88, true), 40, Start.AddMilliseconds(100));
            Assert.IsTrue(frame.Hover);

            frame.Update(TailsitterFrame.Expect.Hover, 90, 0, Reported(88, true), 40, Start.AddMilliseconds(900));
            Assert.IsTrue(frame.Hover);
        }

        [TestMethod]
        public void ManeuversAreNotTakenForAChange()
        {
            var random = new Random(3);
            foreach (var expect in new[] { TailsitterFrame.Expect.Hover, TailsitterFrame.Expect.Either })
            {
                var frame = new TailsitterFrame();
                var now = Start;
                double roll = 0, pitch = 0, yaw = 0;
                frame.Update(TailsitterFrame.Expect.Hover, 90, roll, pitch, yaw, now);
                for (int i = 0; i < 2000; i++)
                {
                    // up to 15° a report on each axis
                    roll = Math.Max(-45, Math.Min(45, roll + (random.NextDouble() - 0.5) * 30));
                    pitch = Math.Max(-45, Math.Min(45, pitch + (random.NextDouble() - 0.5) * 30));
                    yaw = (yaw + (random.NextDouble() - 0.5) * 30 + 360) % 360;
                    now = now.AddMilliseconds(100);
                    frame.Update(expect, 90, roll, pitch, yaw, now);
                    Assert.IsTrue(frame.Hover, expect + " at report " + i);
                }
            }
        }

        [TestMethod]
        public void TheStateIsTakenWhenNoChangeIsSeenInTime()
        {
            var frame = new TailsitterFrame();
            frame.Update(TailsitterFrame.Expect.Wing, 90, 0, 0, 0, Start);

            double turned = frame.Update(TailsitterFrame.Expect.Hover, 90, 0, 0, 0, Start.AddSeconds(1));
            Assert.AreEqual(0, turned);
            turned = frame.Update(TailsitterFrame.Expect.Hover, 90, 0, 0, 0,
                Start.AddSeconds(1 + TailsitterFrame.SettleSeconds - 0.1));
            Assert.AreEqual(0, turned);
            Assert.IsFalse(frame.Hover);

            turned = frame.Update(TailsitterFrame.Expect.Hover, 92, 0, 0, 0,
                Start.AddSeconds(1 + TailsitterFrame.SettleSeconds));
            Assert.IsTrue(frame.Hover);
            Assert.AreEqual(92, turned);
        }

        [TestMethod]
        public void AModeThatFliesBothNeverSettles()
        {
            var frame = new TailsitterFrame();
            frame.Update(TailsitterFrame.Expect.Wing, 90, 0, 0, 0, Start);
            for (int i = 1; i < 60; i++)
                Assert.AreEqual(0, frame.Update(TailsitterFrame.Expect.Either, 90, 0, 0, 0, Start.AddSeconds(i)));
            Assert.IsFalse(frame.Hover);
        }
    }

    [TestClass]
    public class TailsitterModelTests
    {
        static readonly Vector3 Still = new Vector3(0, 0, 0);

        static WorldFrame Frame()
        {
            var frame = new WorldFrame();
            frame.Update(40.0, -105.0);
            return frame;
        }

        static readonly PointLatLngAlt Here = new PointLatLngAlt(40.0, -105.0, 1700);

        static void Update(VehicleTracker tracker, WorldFrame frame, double roll, double pitch, double yaw, bool smooth,
            TailsitterFrame.Expect? expect)
        {
            tracker.Update(frame, Here, DateTime.Now, Still, new Vector3(roll, pitch, yaw), 1600, 0, 0, smooth, true,
                expect);
        }

        // the model as drawn: turned NoseUp nose up, then by its Euler angles
        static double[] Model(VehicleTracker tracker)
        {
            var m = new double[9];
            EulerRotation.FromEuler(tracker.ModelRoll, tracker.ModelPitch, tracker.ModelYaw, m);
            EulerRotation.PitchedUp(m, tracker.NoseUp, m);
            return m;
        }

        static double[] Airframe(double roll, double pitch, double yaw, double noseUp = 0)
        {
            var m = new double[9];
            EulerRotation.FromEuler(roll, pitch, yaw, m);
            EulerRotation.PitchedUp(m, noseUp, m);
            return m;
        }

        [TestMethod]
        public void AnythingElseIsDrawnAsReported()
        {
            var tracker = new VehicleTracker();
            Update(tracker, Frame(), 10, 20, 30, false, null);
            Assert.AreEqual(0, tracker.NoseUp);
            Assert.AreEqual(10, tracker.ModelRoll, 1e-4);
            Assert.AreEqual(20, tracker.ModelPitch, 1e-4);
            Assert.AreEqual(30, tracker.ModelYaw, 1e-4);
        }

        [TestMethod]
        public void HoveringTheModelIsNoseUp()
        {
            foreach (bool smooth in new[] { false, true })
            {
                var tracker = new VehicleTracker();
                Update(tracker, Frame(), 5, -3, 120, smooth, TailsitterFrame.Expect.Hover);
                Assert.AreEqual(0, EulerRotation.Between(Model(tracker), Airframe(5, -3, 120, 90)), 0.01,
                    "smooth " + smooth);
            }
        }

        [TestMethod]
        public void TheModelTurnsWithTheAirframeThroughTheChangeOfFrame()
        {
            var frame = Frame();
            var tracker = new VehicleTracker();
            Update(tracker, frame, 0, 0, 45, false, TailsitterFrame.Expect.Wing);
            for (double airframe = 5; airframe <= 90; airframe += 5)
            {
                // a Q mode, reported on the wing until 50°, then in the hover frame
                double reported = airframe <= 50 ? airframe : airframe - 90;
                Update(tracker, frame, 0, reported, 45, false, TailsitterFrame.Expect.Hover);
                Assert.AreEqual(0, EulerRotation.Between(Model(tracker), Airframe(0, airframe, 45)), 0.01,
                    "at " + airframe);
            }
        }

        [TestMethod]
        public void SmoothedTheModelHoldsStillThroughTheChangeOfFrame()
        {
            // on its tail, switched to a Q mode: the report changes frame, the airframe doesn't move
            var frame = Frame();
            var tracker = new VehicleTracker();
            for (int i = 0; i < 10; i++)
            {
                Update(tracker, frame, 0, 88, 40, true, TailsitterFrame.Expect.Wing);
                Assert.AreEqual(0, EulerRotation.Between(Model(tracker), Airframe(0, 88, 40)), 0.5, "on the wing " + i);
            }

            for (int i = 0; i < 10; i++)
            {
                Update(tracker, frame, 0, -2, 40, true, TailsitterFrame.Expect.Hover);
                Assert.AreEqual(0, EulerRotation.Between(Model(tracker), Airframe(0, 88, 40)), 0.5, "hovering " + i);
            }
        }

        [TestMethod]
        public void SmoothedPastTheFrameLimitTheModelEasesOnWithoutAJump()
        {
            // the smoothed angles move into a frame nearer the airframe's with the model drawn where it was
            var frame = Frame();
            var tracker = new VehicleTracker();
            Update(tracker, frame, 0, 0, 0, true, TailsitterFrame.Expect.Wing);
            var before = Model(tracker);
            Update(tracker, frame, 0, 70, 0, true, TailsitterFrame.Expect.Wing);
            var after = Model(tracker);
            var airframe = Airframe(0, 70, 0);

            double gap = EulerRotation.Between(before, airframe);
            Assert.IsTrue(EulerRotation.Between(before, after) < gap, "moved less than the whole way");
            Assert.IsTrue(EulerRotation.Between(after, airframe) < gap, "moved towards the airframe");
        }

        [TestMethod]
        public void OnItsTailOnTheWingRollAndYawHoldStill()
        {
            // near straight up the reported roll and yaw trade places from one report to the next for one airframe
            var frame = Frame();
            var tracker = new VehicleTracker();
            for (int i = 0; i < 40; i++)
            {
                bool odd = i % 2 == 1;
                Update(tracker, frame, odd ? 40 : 0, 89, odd ? 70 : 30, true, TailsitterFrame.Expect.Wing);
                Assert.AreEqual(0, EulerRotation.Between(Model(tracker), Airframe(0, 89, 30)), 2, "at report " + i);
            }
        }

        [TestMethod]
        public void KnownLateAsATailsitterTheModelTurnsNoseUpOverTime()
        {
            var frame = Frame();
            var tracker = new VehicleTracker();
            Update(tracker, frame, 0, 0, 0, true, null);
            Update(tracker, frame, 0, 0, 0, true, TailsitterFrame.Expect.Hover);
            Assert.IsTrue(tracker.NoseUp < 30, "starts as it was drawn");

            var until = DateTime.Now.AddSeconds(3);
            while (DateTime.Now < until && tracker.NoseUp < 89.5)
            {
                System.Threading.Thread.Sleep(20);
                Update(tracker, frame, 0, 0, 0, true, TailsitterFrame.Expect.Hover);
            }

            Assert.AreEqual(90, tracker.NoseUp, 0.5);
        }
    }
}
