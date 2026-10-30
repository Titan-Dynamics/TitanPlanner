using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class LocationProjectionTests
    {
        static readonly DateTime Fix = new DateTime(2026, 1, 1, 12, 0, 0);
        static readonly PointLatLngAlt Start = new PointLatLngAlt(33.6, -117.8, 100);

        [TestMethod]
        public void ClimbingRaisesTheAltitude()
        {
            // north, east, down: -2 m/s down is a 2 m/s climb
            var projected = LocationProjection.Project(Start, new Vector3(0, 0, -2), Fix, Fix.AddSeconds(1.5));

            Assert.AreEqual(103, projected.Alt, 1e-6);
            Assert.AreEqual(0, Start.GetDistance(projected), 0.01);
        }

        [TestMethod]
        public void TheGroundTrackIgnoresTheVerticalSpeed()
        {
            // 10 m/s north while descending at 10 m/s covers 10 m of ground a second, not 14
            var projected = LocationProjection.Project(Start, new Vector3(10, 0, 10), Fix, Fix.AddSeconds(1));

            Assert.AreEqual(10, Start.GetDistance(projected), 0.05);
            Assert.IsTrue(projected.Lat > Start.Lat);
            Assert.AreEqual(90, projected.Alt, 1e-6);
        }
    }
}
