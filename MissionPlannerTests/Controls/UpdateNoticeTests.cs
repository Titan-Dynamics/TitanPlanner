using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Controls.Tests
{
    /// <summary>The latest changes the update notice takes from a ChangeLog.txt (.github/scripts/release-notes.sh).</summary>
    [TestClass]
    public class UpdateNoticeTests
    {
        private const string ChangeLog = @"Development Build 1.3.9815.1

Changes since TitanPlanner 1.3.9800.1.

TitanPlanner
* First change (1111111)
* Second change (2222222)
* Third change (3333333)
* Fourth change (4444444)
* Fifth change (5555555)
* Sixth change (6666666)

Upstream Mission Planner (2 commits)
* Upstream one (aaaaaaa)
* Upstream two (bbbbbbb)

Full diff: https://github.com/Titan-Dynamics/TitanPlanner/compare/abc...def
";

        [TestMethod]
        public void TheForksNewestComeFirstWithoutTheirCommits()
        {
            CollectionAssert.AreEqual(
                new[] {"Sixth change", "Fifth change", "Fourth change", "Third change", "Second change"},
                UpdateNotice.ParseChangeLog(ChangeLog, 5));
        }

        [TestMethod]
        public void UpstreamsOnlyWhenTheForkHasNone()
        {
            var upstreamOnly = "Changes since TitanPlanner 1.3.9800.1.\n\nUpstream Mission Planner (2 commits)\n" +
                               "* Upstream one (aaaaaaa)\n* Upstream two (bbbbbbb)\n";
            CollectionAssert.AreEqual(new[] {"Upstream two", "Upstream one"},
                UpdateNotice.ParseChangeLog(upstreamOnly, 5));
        }

        [TestMethod]
        public void TheCapIsNotAChange()
        {
            var capped = "TitanPlanner\r\n* One (1111111)\r\n* Two (2222222)\r\n* ...and 12 more, see the full diff\r\n";
            CollectionAssert.AreEqual(new[] {"Two", "One"}, UpdateNotice.ParseChangeLog(capped, 5));
        }

        [TestMethod]
        public void NoChangesIsAnEmptyList()
        {
            Assert.AreEqual(0, UpdateNotice.ParseChangeLog("No new commits since TitanPlanner 1.3.9800.1.\n", 5).Count);
            Assert.AreEqual(0, UpdateNotice.ParseChangeLog(null, 5).Count);
        }
    }
}
