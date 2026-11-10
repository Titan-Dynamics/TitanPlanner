using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// A survey at 120 over ground rising from 1418 m to 1604 m with home at 1432 m, as reported: the camera's height
    /// above the ground under each shot depends on the frame the waypoints are given.
    /// </summary>
    [TestClass]
    public class MissionAltitudeTests
    {
        const double Home = 1432;
        const double Alt = 120;
        const double Low = 1418;
        const double High = 1604;

        static double Agl(MAVLink.MAV_FRAME frame, double ground, double alt = Alt)
        {
            return MissionAltitude.ToAsl(alt, frame, Home, ground) - ground;
        }

        [TestMethod]
        public void TerrainHoldsTheHeightOverRisingGround()
        {
            Assert.AreEqual(Low + Alt, MissionAltitude.ToAsl(Alt, MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT, Home, Low));
            Assert.AreEqual(Alt, Agl(MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT, Low));
            Assert.AreEqual(Alt, Agl(MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT, High));
            Assert.AreEqual(Alt, Agl(MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT, High));
        }

        [TestMethod]
        public void RelativeFliesLevelAboveHome()
        {
            Assert.AreEqual(Home + Alt, MissionAltitude.ToAsl(Alt, MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, Home, High));
            Assert.AreEqual(134, Agl(MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, Low));
            // under the ground at the high end
            Assert.AreEqual(-52, Agl(MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT, High));
        }

        [TestMethod]
        public void AbsoluteIsAboveSeaLevel()
        {
            Assert.AreEqual(1700, MissionAltitude.ToAsl(1700, MAVLink.MAV_FRAME.GLOBAL, Home, High));
            Assert.AreEqual(96, Agl(MAVLink.MAV_FRAME.GLOBAL_INT, High, 1700));
        }

        [TestMethod]
        public void TerrainWithoutTheGroundCountsFromHome()
        {
            Assert.AreEqual(Home + Alt,
                MissionAltitude.ToAsl(Alt, MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT, Home, double.NaN));
        }

        [TestMethod]
        public void OnlyTheTerrainFramesFollowTheGround()
        {
            Assert.IsTrue(MissionAltitude.FollowsTerrain(MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT));
            Assert.IsTrue(MissionAltitude.FollowsTerrain(MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT_INT));
            Assert.IsFalse(MissionAltitude.FollowsTerrain(MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT));
            Assert.IsFalse(MissionAltitude.FollowsTerrain(MAVLink.MAV_FRAME.GLOBAL));
        }
    }
}
