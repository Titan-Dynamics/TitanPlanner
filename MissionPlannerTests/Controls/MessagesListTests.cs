using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Controls.Tests
{
    [TestClass]
    public class MessagesListTests
    {
        static readonly DateTime Start = new DateTime(2026, 10, 19, 12, 0, 0);

        static List<(DateTime time, string message, byte severity)> Messages(int from, int count)
        {
            return Enumerable.Range(from, count).Select(i => (Start.AddMilliseconds(i), "msg " + i, (byte)6)).ToList();
        }

        // MAVLinkInterface keeps the newest 1000 messages, so a full list keeps its count as it changes.
        static void AddCapped(List<(DateTime time, string message, byte severity)> source, int i)
        {
            source.Add((Start.AddMilliseconds(i), "msg " + i, 6));
            while (source.Count > 1000)
                source.RemoveAt(0);
        }

        // Issue #23: the view stopped updating once the vehicle's message list reached its 1000-message cap.
        [TestMethod]
        public void KeepsUpdatingOnceTheSourceIsFull()
        {
            using (var list = new MessagesList())
            {
                var source = Messages(0, 1000);
                list.UpdateMessages(source);
                Assert.AreEqual("msg 999", list.Shown.Last().message);

                AddCapped(source, 1000);
                list.UpdateMessages(source);

                Assert.AreEqual(1000, list.Shown.Count);
                Assert.AreEqual("msg 1000", list.Shown.Last().message);
                Assert.AreEqual("msg 1", list.Shown.First().message);
            }
        }

        [TestMethod]
        public void ScrolledUpTheSameMessagesStayInView()
        {
            using (var list = new MessagesList())
            {
                var source = Messages(0, 1000);
                list.UpdateMessages(source);

                list.TopIndex = 100;
                var reading = list.Shown[list.TopIndex].message;

                for (int i = 1000; i < 1005; i++)
                    AddCapped(source, i);
                list.UpdateMessages(source);

                Assert.AreEqual(95, list.TopIndex);
                Assert.AreEqual(reading, list.Shown[list.TopIndex].message);
            }
        }

        [TestMethod]
        public void AtTheBottomItFollowsNewMessages()
        {
            using (var list = new MessagesList())
            {
                var source = Messages(0, 1000);
                list.UpdateMessages(source);
                int bottom = list.TopIndex;

                AddCapped(source, 1000);
                list.UpdateMessages(source);

                Assert.AreEqual(bottom, list.TopIndex);
                Assert.AreEqual("msg 1000", list.Shown.Last().message);
            }
        }

        [TestMethod]
        public void ClearHidesWhatWasShownAndKeepsNewMessages()
        {
            using (var list = new MessagesList())
            {
                var source = Messages(0, 5);
                list.UpdateMessages(source);

                list.ClearMessages();
                Assert.AreEqual(0, list.Shown.Count);

                // Nothing new: still clear.
                list.UpdateMessages(source);
                Assert.AreEqual(0, list.Shown.Count);

                source.AddRange(Messages(5, 2));
                list.UpdateMessages(source);
                CollectionAssert.AreEqual(new[] { "msg 5", "msg 6" }, list.Shown.Select(m => m.message).ToArray());
            }
        }

        [TestMethod]
        public void AfterClearAFreshListIsShownInFull()
        {
            using (var list = new MessagesList())
            {
                list.UpdateMessages(Messages(0, 5));
                list.ClearMessages();

                // A new connection starts a new list without the message the clear was made at.
                list.UpdateMessages(Messages(100, 3));
                Assert.AreEqual(3, list.Shown.Count);
            }
        }

        [TestMethod]
        public void AfterClearTheCapTrimmingItAwayShowsEverything()
        {
            using (var list = new MessagesList())
            {
                var source = Messages(0, 1000);
                list.UpdateMessages(source);
                list.ClearMessages();

                for (int i = 1000; i < 2000; i++)
                    AddCapped(source, i);
                list.UpdateMessages(source);

                Assert.AreEqual(1000, list.Shown.Count);
                Assert.AreEqual("msg 1000", list.Shown.First().message);
            }
        }
    }
}
