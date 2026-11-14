using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// A FLY screen tab list saved before the Swarm tab existed.
    /// </summary>
    [TestClass]
    public class TabListMigrationTests
    {
        [TestMethod]
        public void TheTabGoesAfterTheOneNamed()
        {
            Assert.AreEqual("tabQuick;tabParams;tabSwarm;tabStatus;",
                TabListMigration.AddTab("tabQuick;tabParams;tabStatus;", "tabSwarm", "tabParams"));
        }

        [TestMethod]
        public void TheTabGoesLastWhenTheOneNamedIsNotShown()
        {
            Assert.AreEqual("tabQuick;tabStatus;tabSwarm;",
                TabListMigration.AddTab("tabQuick;tabStatus;", "tabSwarm", "tabParams"));
        }

        [TestMethod]
        public void AListWithTheTabIsLeftAlone()
        {
            Assert.AreEqual("tabSwarm;tabQuick;",
                TabListMigration.AddTab("tabSwarm;tabQuick;", "tabSwarm", "tabParams"));
        }

        [TestMethod]
        public void NoSavedListShowsEveryTabAlready()
        {
            Assert.IsNull(TabListMigration.AddTab(null, "tabSwarm", "tabParams"));
            Assert.AreEqual("", TabListMigration.AddTab("", "tabSwarm", "tabParams"));
        }
    }
}
