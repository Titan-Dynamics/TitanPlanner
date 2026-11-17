using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.ArduPilot.Tests
{
    /// <summary>What the parameter download window shows: how far it has got, and the time.</summary>
    [TestClass]
    public class ParamLoadProgressTests
    {
        [TestMethod]
        public void NoPercentageUntilThereIsSomethingToMeasure()
        {
            var p = new ParamLoadProgress(null, 1, 1);
            Assert.AreEqual(-1, p.Percent());
            p.FtpRequested();
            Assert.AreEqual(-1, p.Percent());
            p.FileProgress(-1);
            Assert.AreEqual(ParamLoadStage.FtpOpening, p.Stage);
            p.FileProgress(40);
            Assert.AreEqual(ParamLoadStage.FtpDownloading, p.Stage);
            Assert.AreEqual(40, p.Percent());
            p.FileReceived(1000);
            Assert.AreEqual(100, p.Percent());
            Assert.AreEqual(1000, p.Received);
        }

        [TestMethod]
        public void TheStreamedListCountsTheParametersIn()
        {
            var p = new ParamLoadProgress(null, 1, 1);
            p.ListRequested();
            Assert.AreEqual(ParamLoadMethod.List, p.Method);
            Assert.AreEqual(-1, p.Percent());
            p.Got(250, 1000);
            Assert.AreEqual(25, p.Percent());
            p.Finished(1000);
            Assert.AreEqual(100, p.Percent());
        }

        [TestMethod]
        public void Durations()
        {
            Assert.AreEqual((0.4).ToString("0.0") + " s", ParamLoadProgress.Duration(0.42));
            Assert.AreEqual("12 s", ParamLoadProgress.Duration(12.7));
            Assert.AreEqual("1:05", ParamLoadProgress.Duration(65.2));
        }
    }
}
