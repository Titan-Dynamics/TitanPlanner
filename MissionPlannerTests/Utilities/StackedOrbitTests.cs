using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// Three aircraft from a field at 300 m, flying at 80, 150 and 110 m above it, sent to a stack with its floor at
    /// 100 m and 30 m between the slots.
    /// </summary>
    [TestClass]
    public class StackedOrbitTests
    {
        const double Field = 300;
        const double Floor = 100;
        const double Sep = 30;

        const MAVLink.MAV_FRAME Relative = MAVLink.MAV_FRAME.GLOBAL_RELATIVE_ALT;
        const MAVLink.MAV_FRAME Absolute = MAVLink.MAV_FRAME.GLOBAL;
        const MAVLink.MAV_FRAME Terrain = MAVLink.MAV_FRAME.GLOBAL_TERRAIN_ALT;

        static StackedOrbit.Aircraft Plane(uint sysid, double aboveField, double home = Field)
        {
            return new StackedOrbit.Aircraft
            {
                Name = "Aircraft " + sysid, SysId = sysid, AmslM = home + aboveField, HomeAmslM = home
            };
        }

        static StackedOrbit.Aircraft[] Three()
        {
            return new[] {Plane(1, 80), Plane(2, 150), Plane(3, 110)};
        }

        static uint[] Order(StackedOrbit.Plan plan)
        {
            return plan.Slots.Select(s => s.Aircraft.SysId).ToArray();
        }

        [TestMethod]
        public void TheLowestAircraftTakesTheFloor()
        {
            var plan = StackedOrbit.Build(Three(), Relative, Floor, Sep);

            Assert.IsTrue(plan.Valid);
            CollectionAssert.AreEqual(new uint[] {1, 3, 2}, Order(plan));
            CollectionAssert.AreEqual(new[] {100.0, 130.0, 160.0}, plan.Slots.Select(s => s.TargetM).ToArray());
            CollectionAssert.AreEqual(new[] {400.0, 430.0, 460.0}, plan.Slots.Select(s => s.TargetAmslM).ToArray());
        }

        [TestMethod]
        public void AircraftAtTheSameHeightGoBySystemId()
        {
            var plan = StackedOrbit.Build(new[] {Plane(7, 90), Plane(4, 90), Plane(5, 60)}, Relative, Floor, Sep);

            CollectionAssert.AreEqual(new uint[] {5, 4, 7}, Order(plan));
        }

        [TestMethod]
        public void AStackSentAgainKeepsItsSlots()
        {
            var first = StackedOrbit.Build(Three(), Relative, Floor, Sep);
            var settled = first.Slots.Select(s => Plane(s.Aircraft.SysId, s.TargetM)).Reverse();

            var again = StackedOrbit.Build(settled, Relative, Floor, Sep);

            CollectionAssert.AreEqual(Order(first), Order(again));
        }

        [TestMethod]
        public void AbsoluteCommandsHeightsAboveSeaLevel()
        {
            var plan = StackedOrbit.Build(Three(), Absolute, 450, Sep);

            Assert.IsTrue(plan.Valid);
            CollectionAssert.AreEqual(new[] {450.0, 480.0, 510.0}, plan.Slots.Select(s => s.TargetM).ToArray());
            CollectionAssert.AreEqual(new[] {450.0, 480.0, 510.0}, plan.Slots.Select(s => s.TargetAmslM).ToArray());
        }

        [TestMethod]
        public void AnAbsoluteFloorUnderTheFieldIsRefused()
        {
            // a height above home, typed with Absolute selected
            var plan = StackedOrbit.Build(Three(), Absolute, Floor, Sep);

            Assert.IsFalse(plan.Valid);
        }

        [TestMethod]
        public void AnAbsoluteFloorUnderTheGroundAtTheCentreIsRefused()
        {
            Assert.IsFalse(StackedOrbit.Build(Three(), Absolute, 450, Sep, 500).Valid);
            Assert.IsTrue(StackedOrbit.Build(Three(), Absolute, 450, Sep, 320).Valid);
        }

        [TestMethod]
        public void TerrainCountsFromTheGroundAtTheCentre()
        {
            var plan = StackedOrbit.Build(Three(), Terrain, Floor, Sep, 350);

            Assert.IsTrue(plan.Valid);
            CollectionAssert.AreEqual(new[] {450.0, 480.0, 510.0}, plan.Slots.Select(s => s.TargetAmslM).ToArray());

            // not known: the slots are still set, their height above sea level isn't
            var unknown = StackedOrbit.Build(Three(), Terrain, Floor, Sep);
            Assert.IsTrue(unknown.Valid);
            Assert.IsTrue(unknown.Slots.All(s => double.IsNaN(s.TargetAmslM)));
            CollectionAssert.AreEqual(new uint[] {1, 3, 2}, Order(unknown));
        }

        [TestMethod]
        public void ZeroSeparationOrFloorIsRefused()
        {
            Assert.IsFalse(StackedOrbit.Build(Three(), Relative, Floor, 0).Valid);
            Assert.IsFalse(StackedOrbit.Build(Three(), Relative, Floor, -10).Valid);
            Assert.IsFalse(StackedOrbit.Build(Three(), Relative, 0, Sep).Valid);
            Assert.IsFalse(StackedOrbit.Build(Three(), Terrain, 0, Sep, 350).Valid);
            Assert.IsFalse(StackedOrbit.Build(new StackedOrbit.Aircraft[0], Relative, Floor, Sep).Valid);
        }

        [TestMethod]
        public void RelativeWithHomesAtDifferentHeightsWarnsThenRefuses()
        {
            // the upper aircraft's home is 10 m lower: 20 m between them, not 30
            var closer = StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 150, Field - 10)}, Relative, Floor, Sep);
            Assert.IsTrue(closer.Valid);
            Assert.AreEqual(1, closer.Warnings.Count);
            Assert.AreEqual(20, closer.Slots[1].TargetAmslM - closer.Slots[0].TargetAmslM);

            // 20 m lower: 10 m between them, under half the separation
            Assert.IsFalse(StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 150, Field - 20)}, Relative, Floor, Sep)
                .Valid);

            // 40 m lower: the upper slot would be under the lower one
            Assert.IsFalse(StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 150, Field - 40)}, Relative, Floor, Sep)
                .Valid);

            // a home a little off, as two GPS on one field are, says nothing
            var same = StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 150, Field - 2)}, Relative, Floor, Sep);
            Assert.IsTrue(same.Valid);
            Assert.AreEqual(0, same.Warnings.Count);

            // and Absolute doesn't depend on the homes
            Assert.IsTrue(StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 150, Field - 40)}, Absolute, 450, Sep)
                .Valid);
        }

        [TestMethod]
        public void ClimbersGoFromTheTopThenDescendersFromTheBottom()
        {
            // floor 100, slots 100/130/160/190: the aircraft at 80 and 120 climb, those at 170 and 250 descend
            var plan = StackedOrbit.Build(new[] {Plane(1, 80), Plane(2, 120), Plane(3, 170), Plane(4, 250)}, Relative,
                Floor, Sep);

            CollectionAssert.AreEqual(new uint[] {1, 2, 3, 4}, Order(plan));
            CollectionAssert.AreEqual(new uint[] {2, 1, 3, 4},
                StackedOrbit.SendOrder(plan).Select(s => s.Aircraft.SysId).ToArray());
        }

        [TestMethod]
        public void EveryAircraftIsSentOnce()
        {
            var plan = StackedOrbit.Build(Three(), Terrain, Floor, Sep);

            CollectionAssert.AreEquivalent(new uint[] {1, 2, 3},
                StackedOrbit.SendOrder(plan).Select(s => s.Aircraft.SysId).ToArray());
        }
    }
}
