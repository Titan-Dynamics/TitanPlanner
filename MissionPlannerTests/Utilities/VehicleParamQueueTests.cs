using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    /// <summary>
    /// The order the vehicles of a link have their parameters downloaded in, and what a switch, a failure and a
    /// reconnect do to it.
    /// </summary>
    [TestClass]
    public class VehicleParamQueueTests
    {
        readonly object port = new object();
        readonly object one = new object();
        readonly object two = new object();
        readonly object three = new object();

        VehicleParamQueue Three()
        {
            var queue = new VehicleParamQueue();
            queue.Enqueue(port, 1, 1, one);
            queue.Enqueue(port, 2, 1, two);
            queue.Enqueue(port, 3, 1, three);
            return queue;
        }

        [TestMethod]
        public void VehiclesAreDownloadedInTheOrderQueued()
        {
            var queue = Three();

            Assert.AreSame(one, queue.TryDequeue().Vehicle);
            Assert.AreSame(two, queue.TryDequeue().Vehicle);
            Assert.AreSame(three, queue.TryDequeue().Vehicle);
            Assert.IsNull(queue.TryDequeue());
        }

        [TestMethod]
        public void AVehicleIsOnlyQueuedOnce()
        {
            var queue = Three();
            queue.Enqueue(port, 2, 1, two);

            Assert.AreEqual(3, queue.PendingCount);
        }

        [TestMethod]
        public void TheVehicleSwitchedToGoesFirst()
        {
            var queue = Three();
            queue.Enqueue(port, 3, 1, three, true);

            Assert.AreSame(three, queue.TryDequeue().Vehicle);
            Assert.AreSame(one, queue.TryDequeue().Vehicle);
        }

        [TestMethod]
        public void AVehicleBeingDownloadedIsLeftAlone()
        {
            var queue = Three();
            var loading = queue.TryDequeue();

            Assert.AreSame(loading, queue.Enqueue(port, 1, 1, one, true));
            Assert.AreEqual(ParamLoadState.Loading, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(two, queue.TryDequeue().Vehicle);
        }

        [TestMethod]
        public void ALoadedVehicleIsNotQueuedAgain()
        {
            var queue = Three();
            queue.Complete(queue.TryDequeue());
            queue.Enqueue(port, 1, 1, one, true);

            Assert.AreEqual(ParamLoadState.Loaded, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(two, queue.TryDequeue().Vehicle);
        }

        [TestMethod]
        public void AFailedDownloadIsTriedOnceMoreAfterTheOthers()
        {
            var queue = Three();
            queue.Fail(queue.TryDequeue());

            Assert.AreEqual(ParamLoadState.Pending, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(two, queue.TryDequeue().Vehicle);
            Assert.AreSame(three, queue.TryDequeue().Vehicle);

            var again = queue.TryDequeue();
            Assert.AreSame(one, again.Vehicle);
            queue.Fail(again);

            Assert.AreEqual(ParamLoadState.Failed, queue.StateOf(port, 1, 1, one));
            Assert.IsNull(queue.TryDequeue());
        }

        [TestMethod]
        public void AnInterruptedDownloadGoesBackToTheFront()
        {
            var queue = Three();
            queue.Requeue(queue.TryDequeue());

            Assert.AreEqual(ParamLoadState.Pending, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(one, queue.TryDequeue().Vehicle);
        }

        [TestMethod]
        public void AVehicleWithoutMavftpIsLeftForTheSwitch()
        {
            var queue = Three();
            queue.NeedsManual(queue.TryDequeue());

            Assert.AreEqual(ParamLoadState.NeedsManual, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(two, queue.TryDequeue().Vehicle);
        }

        [TestMethod]
        public void AReconnectedVehicleStartsAgain()
        {
            var queue = Three();
            queue.Complete(queue.TryDequeue());

            // the same ids, a new vehicle
            var reconnected = new object();
            Assert.AreEqual(ParamLoadState.None, queue.StateOf(port, 1, 1, reconnected));

            queue.Enqueue(port, 1, 1, reconnected);
            Assert.AreEqual(ParamLoadState.Pending, queue.StateOf(port, 1, 1, reconnected));
            Assert.AreEqual(ParamLoadState.None, queue.StateOf(port, 1, 1, one));
        }

        [TestMethod]
        public void AClosedPortForgetsItsVehicles()
        {
            var queue = Three();
            var other = new object();
            var elsewhere = new object();
            queue.Enqueue(other, 1, 1, elsewhere);

            queue.Reset(port);

            Assert.AreEqual(ParamLoadState.None, queue.StateOf(port, 1, 1, one));
            Assert.AreSame(elsewhere, queue.TryDequeue().Vehicle);
            Assert.IsNull(queue.TryDequeue());
        }

        [TestMethod]
        public void AVehicleTheConnectDownloadsIsNotQueued()
        {
            var queue = new VehicleParamQueue();
            queue.Track(port, 1, 1, one, ParamLoadState.Loading);
            queue.Enqueue(port, 1, 1, one);

            Assert.AreEqual(ParamLoadState.Loading, queue.StateOf(port, 1, 1, one));
            Assert.IsNull(queue.TryDequeue());
        }
    }
}
