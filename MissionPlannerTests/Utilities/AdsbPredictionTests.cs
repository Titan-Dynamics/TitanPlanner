using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class AdsbPredictionTests
    {
        static readonly DateTime Measured = new DateTime(2026, 10, 18, 12, 0, 0);

        // 100 m/s due east at 1000 m, measured at Measured.
        static adsb.PointLatLngAltHdg Plane()
        {
            return new adsb.PointLatLngAltHdg(34.0, -118.0, 1000, 90, 100 * 100, "A1B2C3", Measured.AddSeconds(2))
            {
                PositionTime = Measured
            };
        }

        [TestMethod]
        public void MovesAlongItsTrackAtItsGroundSpeed()
        {
            var plane = Plane();
            var predicted = plane.Predict(Measured.AddSeconds(2));

            Assert.AreEqual(200, plane.GetDistance(predicted), 1);
            Assert.AreEqual(90, plane.GetBearing(predicted), 0.5);
            Assert.AreEqual(1000, predicted.Alt, 1e-6);
        }

        [TestMethod]
        public void ClimbsAtItsVerticalRate()
        {
            var plane = Plane();
            plane.VerticalSpeed = 5 * 100; // 5 m/s
            Assert.AreEqual(1015, plane.Predict(Measured.AddSeconds(3)).Alt, 1e-6);
        }

        [TestMethod]
        public void StopsCarryingForwardAfterTheLimit()
        {
            var plane = Plane();
            var predicted = plane.Predict(Measured.AddSeconds(60));
            // newpos and GetDistance use slightly different earth radii (0.1%).
            Assert.AreEqual(100 * adsb.PointLatLngAltHdg.MaxPredictSeconds, plane.GetDistance(predicted), 2);
        }

        [TestMethod]
        public void StaysPutWithoutAUsableTrackOrSpeed()
        {
            var noTrack = Plane();
            noTrack.TrackKnown = false;
            var grounded = Plane();
            grounded.IsOnGround = true;
            var stopped = Plane();
            stopped.Speed = 0;

            foreach (var plane in new[] { noTrack, grounded, stopped })
                Assert.AreEqual(0, plane.GetDistance(plane.Predict(Measured.AddSeconds(5))), 1e-6);
        }

        [TestMethod]
        public void NeverMovesBackwards()
        {
            var plane = Plane();
            Assert.AreEqual(0, plane.GetDistance(plane.Predict(Measured.AddSeconds(-1))), 1e-6);
        }

        [TestMethod]
        public void WithoutAPositionTimeUsesTheTimeReceived()
        {
            var plane = Plane();
            plane.PositionTime = default(DateTime);
            // Time is Measured + 2 s, so one second later it has moved one second's worth.
            Assert.AreEqual(100, plane.GetDistance(plane.Predict(Measured.AddSeconds(3))), 1);
        }

        [TestMethod]
        public void OnlyThePreviousDefaultServerIsMigrated()
        {
            Assert.AreEqual(adsb.DefaultServer, adsb.MigrateServer("https://api.adsb.lol"));
            Assert.AreEqual(adsb.DefaultServer, adsb.MigrateServer("https://api.adsb.lol/"));
            Assert.AreEqual("https://example.com/adsb", adsb.MigrateServer("https://example.com/adsb"));
            Assert.AreEqual("192.168.1.10", adsb.MigrateServer("192.168.1.10"));
            Assert.IsNull(adsb.MigrateServer(null));
        }
    }
}
