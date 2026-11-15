using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.ArduPilot.Tests
{
    [TestClass]
    public class CurrentStateTests
    {
        static CurrentState Flying(double amps, double volts, float groundspeed)
        {
            return new CurrentState {current = amps, battery_voltage = volts, groundspeed = groundspeed};
        }

        [TestMethod]
        public void EfficiencyIsTheDrawOverTheGroundSpeed()
        {
            // 10 A and 222 W at 20 m/s (72 km/h)
            var cs = Flying(10, 22.2, 20);

            Assert.AreEqual(10000 / 72.0, cs.battery_mahperkm, 1e-9);
            Assert.AreEqual(222 / 72.0, cs.battery_whperkm, 1e-6);
        }

        [TestMethod]
        public void EfficiencyFollowsTheLastValues()
        {
            var cs = Flying(10, 22.2, 20);

            cs.current = 5;
            Assert.AreEqual(5000 / 72.0, cs.battery_mahperkm, 1e-9);

            cs.groundspeed = 10;
            Assert.AreEqual(5000 / 36.0, cs.battery_mahperkm, 1e-9);
        }

        [TestMethod]
        public void EfficiencyIsZeroWhenNotMoving()
        {
            var cs = Flying(10, 22.2, 0.5f);

            Assert.AreEqual(0, cs.battery_mahperkm);
            Assert.AreEqual(0, cs.battery_whperkm);
            Assert.AreEqual(0, cs.battery_kmleft);
        }
    }
}
