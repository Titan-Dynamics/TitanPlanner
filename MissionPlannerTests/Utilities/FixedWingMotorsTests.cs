using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class FixedWingMotorsTests
    {
        const int Aileron = 4, Elevator = 19;

        static string Names(List<FixedWingMotors.Motor> motors)
        {
            return string.Join(", ", motors.Select(m => m.Name + "=" + m.Servo));
        }

        [TestMethod]
        public void OneThrottleOutputIsTheMotor()
        {
            var motors = FixedWingMotors.Find(new Dictionary<int, int>
                { [1] = Aileron, [2] = Elevator, [3] = FixedWingMotors.Throttle, [4] = 21 });
            Assert.AreEqual("Motor=3", Names(motors));
        }

        [TestMethod]
        public void NoMotorOutputIsNoMotor()
        {
            Assert.AreEqual(0, FixedWingMotors.Find(new Dictionary<int, int> { [1] = Aileron, [2] = 0 }).Count);
        }

        [TestMethod]
        public void TwoThrottleOutputsAreLettered()
        {
            var motors = FixedWingMotors.Find(new Dictionary<int, int>
                { [5] = FixedWingMotors.Throttle, [1] = Aileron, [3] = FixedWingMotors.Throttle });
            Assert.AreEqual("Motor A=3, Motor B=5", Names(motors));
        }

        [TestMethod]
        public void LeftAndRightComeFirstByName()
        {
            var motors = FixedWingMotors.Find(new Dictionary<int, int>
            {
                [2] = FixedWingMotors.ThrottleRight, [3] = FixedWingMotors.ThrottleLeft, [4] = FixedWingMotors.Throttle
            });
            Assert.AreEqual("Left motor=3, Right motor=2, Motor A=4", Names(motors));

            motors = FixedWingMotors.Find(new Dictionary<int, int>
            {
                [1] = FixedWingMotors.ThrottleLeft, [2] = FixedWingMotors.ThrottleRight,
                [3] = FixedWingMotors.ThrottleLeft, [4] = FixedWingMotors.ThrottleRight
            });
            Assert.AreEqual("Left motor A=1, Left motor B=3, Right motor A=2, Right motor B=4", Names(motors));
        }

        [TestMethod]
        public void PwmRunsFromMinToMaxOrBackWhenReversed()
        {
            Assert.AreEqual(1000, FixedWingMotors.Pwm(1000, 2000, false, 0));
            Assert.AreEqual(1050, FixedWingMotors.Pwm(1000, 2000, false, 5));
            Assert.AreEqual(2000, FixedWingMotors.Pwm(1000, 2000, false, 150));
            Assert.AreEqual(2000, FixedWingMotors.Pwm(1000, 2000, true, 0));
            Assert.AreEqual(1950, FixedWingMotors.Pwm(1000, 2000, true, 5));
            Assert.AreEqual(1000, FixedWingMotors.Pwm(1000, 2000, false, -10));
        }

        [TestMethod]
        public void MovingAMotorSwapsTheOutputsFunctions()
        {
            // onto an aileron: the aileron goes to the motor's old output
            var writes = FixedWingMotors.Move(3, FixedWingMotors.Throttle, 1, Aileron);
            Assert.AreEqual(Aileron, writes[3]);
            Assert.AreEqual(FixedWingMotors.Throttle, writes[1]);

            // onto a free output: the old one is freed
            writes = FixedWingMotors.Move(3, FixedWingMotors.ThrottleLeft, 6, FixedWingMotors.None);
            Assert.AreEqual(FixedWingMotors.None, writes[3]);
            Assert.AreEqual(FixedWingMotors.ThrottleLeft, writes[6]);

            // a motor with no output only takes the target
            writes = FixedWingMotors.Move(0, FixedWingMotors.Throttle, 6, FixedWingMotors.None);
            Assert.AreEqual(1, writes.Count);
            Assert.AreEqual(FixedWingMotors.Throttle, writes[6]);
        }

        [TestMethod]
        public void TwinsAreSetLeftAndRightByWhichTurned()
        {
            var a = new FixedWingMotors.Motor { Servo = 3, Function = FixedWingMotors.Throttle };
            var b = new FixedWingMotors.Motor { Servo = 4, Function = FixedWingMotors.Throttle };

            var writes = FixedWingMotors.AssignTwin(a, b, true);
            Assert.AreEqual(FixedWingMotors.ThrottleLeft, writes[3]);
            Assert.AreEqual(FixedWingMotors.ThrottleRight, writes[4]);

            writes = FixedWingMotors.AssignTwin(a, b, false);
            Assert.AreEqual(FixedWingMotors.ThrottleRight, writes[3]);
            Assert.AreEqual(FixedWingMotors.ThrottleLeft, writes[4]);
        }

        [TestMethod]
        public void BorrowedOutputsRoundTrip()
        {
            var text = FixedWingMotors.FormatBorrowed(7, new Dictionary<int, int>
                { [3] = FixedWingMotors.ThrottleLeft, [4] = FixedWingMotors.ThrottleRight });

            uint sysid;
            Dictionary<int, int> functions;
            Assert.IsTrue(FixedWingMotors.ParseBorrowed(text, out sysid, out functions));
            Assert.AreEqual(7u, sysid);
            Assert.AreEqual(2, functions.Count);
            Assert.AreEqual(FixedWingMotors.ThrottleLeft, functions[3]);
            Assert.AreEqual(FixedWingMotors.ThrottleRight, functions[4]);

            Assert.IsFalse(FixedWingMotors.ParseBorrowed(null, out sysid, out functions));
            Assert.IsFalse(FixedWingMotors.ParseBorrowed("7:", out sysid, out functions));
            Assert.IsFalse(FixedWingMotors.ParseBorrowed("x:3=70", out sysid, out functions));
            Assert.IsFalse(FixedWingMotors.ParseBorrowed("7:3=", out sysid, out functions));
        }
    }
}
