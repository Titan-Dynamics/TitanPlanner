using log4net;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Writes parameters off the UI thread, one at a time in the order they were queued, so a page stays usable
    /// while the vehicle confirms each write. A value queued again before its write starts replaces the pending
    /// one, so stepping through a list only writes where it stopped.
    /// </summary>
    public static class ParamWriteQueue
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static readonly object locker = new object();
        private static Task tail = Task.CompletedTask;

        private static readonly Dictionary<string, (double value, List<Action<bool>> callbacks)> pending =
            new Dictionary<string, (double, List<Action<bool>>)>();

        private class Delayed
        {
            public MAVLinkInterface Port;
            public uint Sysid;
            public byte Compid;
            public string ParamName;
            public double Value;
            public readonly List<Action<bool>> Callbacks = new List<Action<bool>>();
        }

        // writes held back by a delay; every new delayed write restarts the one timer
        private static readonly Dictionary<string, Delayed> delayed = new Dictionary<string, Delayed>();
        private static Timer delayTimer;

        /// <summary>
        /// Queue a write to the current vehicle. A failure is reported to the user; onDone then runs on the UI
        /// thread with whether the vehicle confirmed the value. With delayMs, the write waits until no delayed
        /// write has been queued for that long, then goes out with the others held back.
        /// </summary>
        public static void Write(string paramName, double value, Action<bool> onDone = null, int delayMs = 0)
        {
            var port = MainV2.comPort;
            var sysid = port.sysidcurrent;
            var compid = (byte)port.compidcurrent;

            if (delayMs <= 0)
            {
                Enqueue(port, sysid, compid, paramName, value, onDone == null ? null : new[] { onDone });
                return;
            }

            var key = sysid + ":" + compid + ":" + paramName;
            lock (locker)
            {
                if (!delayed.TryGetValue(key, out var item))
                {
                    item = new Delayed { Port = port, Sysid = sysid, Compid = compid, ParamName = paramName };
                    delayed[key] = item;
                }

                item.Value = value;
                if (onDone != null)
                    item.Callbacks.Add(onDone);

                if (delayTimer == null)
                    delayTimer = new Timer(_ => Flush());
                delayTimer.Change(delayMs, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Send the delayed writes now, e.g. when the page that made them closes.
        /// </summary>
        public static void Flush()
        {
            List<Delayed> items;
            lock (locker)
            {
                delayTimer?.Change(Timeout.Infinite, Timeout.Infinite);
                items = delayed.Values.ToList();
                delayed.Clear();
            }

            foreach (var item in items)
                Enqueue(item.Port, item.Sysid, item.Compid, item.ParamName, item.Value, item.Callbacks);
        }

        private static void Enqueue(MAVLinkInterface port, uint sysid, byte compid, string paramName, double value,
            IEnumerable<Action<bool>> onDone)
        {
            var key = sysid + ":" + compid + ":" + paramName;

            lock (locker)
            {
                if (pending.TryGetValue(key, out var queued))
                {
                    if (onDone != null)
                        queued.callbacks.AddRange(onDone);
                    pending[key] = (value, queued.callbacks);
                    return;
                }

                var callbacks = new List<Action<bool>>();
                if (onDone != null)
                    callbacks.AddRange(onDone);
                pending[key] = (value, callbacks);

                tail = tail.ContinueWith(_ => Run(port, sysid, compid, paramName, key), TaskScheduler.Default);
            }
        }

        private static void Run(MAVLinkInterface port, uint sysid, byte compid, string paramName, string key)
        {
            (double value, List<Action<bool>> callbacks) item;
            lock (locker)
            {
                item = pending[key];
                pending.Remove(key);
            }

            bool ok;
            try
            {
                ok = port.setParamAsync(sysid, compid, paramName, item.value).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                log.Error(ex);
                ok = false;
            }

            var main = MainV2.instance;
            if (main == null || main.IsDisposed || !main.IsHandleCreated)
                return;

            main.BeginInvoke((Action)(() =>
            {
                if (!ok)
                    CustomMessageBox.Show(String.Format(Strings.ErrorSetValueFailed, paramName), Strings.ERROR);

                foreach (var callback in item.callbacks)
                {
                    try
                    {
                        callback(ok);
                    }
                    catch (Exception ex)
                    {
                        log.Error(ex);
                    }
                }
            }));
        }
    }
}
