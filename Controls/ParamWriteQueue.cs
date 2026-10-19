using log4net;
using System;
using System.Collections.Generic;
using System.Reflection;
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

        /// <summary>
        /// Queue a write to the current vehicle. A failure is reported to the user; onDone then runs on the UI
        /// thread with whether the vehicle confirmed the value.
        /// </summary>
        public static void Write(string paramName, double value, Action<bool> onDone = null)
        {
            var port = MainV2.comPort;
            var sysid = port.sysidcurrent;
            var compid = (byte)port.compidcurrent;
            var key = sysid + ":" + compid + ":" + paramName;

            lock (locker)
            {
                if (pending.TryGetValue(key, out var queued))
                {
                    if (onDone != null)
                        queued.callbacks.Add(onDone);
                    pending[key] = (value, queued.callbacks);
                    return;
                }

                var callbacks = new List<Action<bool>>();
                if (onDone != null)
                    callbacks.Add(onDone);
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
