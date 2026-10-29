using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class POITests
    {
        [TestMethod]
        public void NextIdStartsAtOne()
        {
            Assert.AreEqual(1, POI.NextId(new string[0]));
            Assert.AreEqual(1, POI.NextId(new[] { "Tower", "", null }));
        }

        [TestMethod]
        public void NextIdFollowsTheHighestNumber()
        {
            Assert.AreEqual(8, POI.NextId(new[] { "3", "7", "2" }));
        }

        [TestMethod]
        public void NextIdReadsTheNumberEndingAName()
        {
            Assert.AreEqual(13, POI.NextId(new[] { "POI 4", "Tower12 ", "5a" }));
        }

        [TestMethod]
        public void NextIdIgnoresNumbersTooLargeToCount()
        {
            Assert.AreEqual(3, POI.NextId(new[] { "2", "99999999999999999999" }));
        }
    }
}
