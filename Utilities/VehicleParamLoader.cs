using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.ArduPilot.Mavlink;
using MissionPlanner.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Downloads the parameters of every vehicle on a link in the background, one vehicle at a time, so that
    /// switching to a vehicle finds them there. The connect downloads the vehicle it selects; this does the rest,
    /// and any that show up later.
    ///
    /// It only uses MAVFTP, which listens for its replies. The older download reads the port itself, and would take
    /// the acknowledgements of commands sent to other vehicles on the link, so a vehicle without MAVFTP is left to
    /// the download with the dialog, when it is switched to.
    /// </summary>
    public class VehicleParamLoader
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public static readonly VehicleParamLoader Instance = new VehicleParamLoader();

        private const string ParamFile = "@PARAM/param.pck?withdefaults=1";

        private readonly VehicleParamQueue _queue = new VehicleParamQueue();

        // ports whose connect has finished with the vehicle it selected
        private readonly HashSet<MAVLinkInterface> _ready = new HashSet<MAVLinkInterface>();

        private readonly object _lock = new object();
        private Thread _worker;
        private int _suspended;

        // the download in progress
        private CancellationTokenSource _jobCancel;
        private MAVLinkInterface _jobPort;
        private bool _jobSuspended;

        /// <summary>A vehicle's state changed. Raised on the loader's thread.</summary>
        public event Action<MAVState> StateChanged;

        private VehicleParamLoader()
        {
        }

        /// <summary>Whether the vehicle's whole parameter list is here.</summary>
        public static bool IsComplete(MAVState mav)
        {
            return mav != null && mav.param.TotalReported > 0 && mav.param.TotalReceived >= mav.param.TotalReported;
        }

        private static bool IsOpen(MAVLinkInterface port)
        {
            try
            {
                return port != null && port.BaseStream != null && port.BaseStream.IsOpen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Whether this is an autopilot to load: the list of a link also holds its cameras, gimbals, CAN nodes and
        /// other ground stations.
        /// </summary>
        public static bool Eligible(MAVLinkInterface port, MAVState mav)
        {
            if (port == null || mav == null || port.logreadmode || !IsOpen(port))
                return false;

            return mav.compid == (byte) MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1 &&
                   mav.sysid != 0 && mav.sysid != MAVLinkInterface.gcssysid &&
                   mav.aptype != MAVLink.MAV_TYPE.GCS &&
                   mav.apname != MAVLink.MAV_AUTOPILOT.INVALID &&
                   !mav.CANNode && !mav.isHighLatency;
        }

        // the vehicle is still the one on that port under its ids (a reconnect makes new ones)
        private static bool Current(MAVLinkInterface port, MAVState mav)
        {
            return IsOpen(port) && port.MAVlist.Contains(mav.sysid, mav.compid, false) &&
                   ReferenceEquals(port.MAVlist[mav.sysid, mav.compid], mav);
        }

        /// <summary>The connect has dealt with the vehicle it selected: the port's other vehicles can be loaded.</summary>
        public void PortReady(MAVLinkInterface port)
        {
            if (port == null)
                return;

            lock (_lock)
                _ready.Add(port);

            EnsureWorker();
        }

        /// <summary>
        /// The port is connecting or disconnecting: its vehicles are forgotten and nothing of it is downloaded until
        /// it is ready again. The connect downloads the vehicle it selects, and two downloads of one vehicle would
        /// reset each other's file transfer.
        /// </summary>
        public void Reset(MAVLinkInterface port)
        {
            if (port == null)
                return;

            lock (_lock)
            {
                _ready.Remove(port);
                if (_jobCancel != null && ReferenceEquals(_jobPort, port))
                    _jobCancel.Cancel();
            }

            _queue.Reset(port);
        }

        /// <summary>The connect is downloading this vehicle itself, in the background.</summary>
        public void ExternalStarted(MAVLinkInterface port, MAVState mav)
        {
            _queue.Track(port, mav.sysid, mav.compid, mav, ParamLoadState.Loading);
            OnStateChanged(mav);
        }

        public void ExternalFinished(MAVLinkInterface port, MAVState mav)
        {
            // not complete: forgotten, so the sweep queues it
            var entry = _queue.Track(port, mav.sysid, mav.compid, mav, ParamLoadState.Loaded);
            if (!IsComplete(mav))
                _queue.Remove(entry);
            OnStateChanged(mav);
        }

        /// <summary>
        /// Puts a vehicle at the front of the queue (it was switched to) and returns its state. None means it isn't
        /// one this loads.
        /// </summary>
        public ParamLoadState Prioritise(MAVLinkInterface port, uint sysid, byte compid)
        {
            if (port == null || !port.MAVlist.Contains(sysid, compid, false))
                return ParamLoadState.None;

            var mav = port.MAVlist[sysid, compid];
            if (IsComplete(mav))
                return ParamLoadState.Loaded;

            if (!Eligible(port, mav))
                return ParamLoadState.None;

            // the selection also changes during the connect, which downloads that vehicle itself
            lock (_lock)
            {
                if (!_ready.Contains(port))
                    return _queue.StateOf(port, sysid, compid, mav) == ParamLoadState.Loading
                        ? ParamLoadState.Loading
                        : ParamLoadState.None;
            }

            var entry = _queue.Enqueue(port, sysid, compid, mav, true);
            EnsureWorker();

            // loaded once, and not complete now: the list changed under it, which the dialog's download handles
            return entry.State == ParamLoadState.Loaded ? ParamLoadState.None : entry.State;
        }

        /// <summary>The state of a vehicle's parameters.</summary>
        public ParamLoadState StateOf(MAVState mav)
        {
            if (mav == null)
                return ParamLoadState.None;

            if (IsComplete(mav))
                return ParamLoadState.Loaded;

            var state = _queue.StateOf(mav.parent, mav.sysid, mav.compid, mav);
            return state == ParamLoadState.Loaded ? ParamLoadState.None : state;
        }

        /// <summary>Whether the vehicle is waiting for, or in, its background download.</summary>
        public bool IsLoading(MAVState mav)
        {
            var state = StateOf(mav);
            return state == ParamLoadState.Pending || state == ParamLoadState.Loading;
        }

        /// <summary>
        /// Holds the downloads off until disposed, stopping the one in progress (it goes back to the front of the
        /// queue): a batch of commands shouldn't share the link with a file transfer.
        /// </summary>
        public IDisposable Suspend()
        {
            lock (_lock)
            {
                _suspended++;
                if (_jobCancel != null)
                {
                    _jobSuspended = true;
                    _jobCancel.Cancel();
                }
            }

            return new Resume(this);
        }

        private class Resume : IDisposable
        {
            private VehicleParamLoader _loader;

            public Resume(VehicleParamLoader loader)
            {
                _loader = loader;
            }

            public void Dispose()
            {
                var loader = Interlocked.Exchange(ref _loader, null);
                if (loader == null)
                    return;
                lock (loader._lock)
                    loader._suspended--;
            }
        }

        private void OnStateChanged(MAVState mav)
        {
            try
            {
                StateChanged?.Invoke(mav);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }

        private void EnsureWorker()
        {
            lock (_lock)
            {
                if (_worker != null)
                    return;

                _worker = new Thread(Run) {IsBackground = true, Name = "Vehicle Param Loader"};
                _worker.Start();
            }
        }

        private void Run()
        {
            var lastSweep = DateTime.MinValue;

            while (true)
            {
                Thread.Sleep(200);

                try
                {
                    if ((DateTime.UtcNow - lastSweep).TotalSeconds >= 1)
                    {
                        Sweep();
                        lastSweep = DateTime.UtcNow;
                    }

                    lock (_lock)
                    {
                        if (_suspended > 0)
                            continue;
                    }

                    var entry = _queue.TryDequeue();
                    if (entry != null)
                        Download(entry);
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }
        }

        // queue what isn't loaded on the ready ports, and forget the ports that closed
        private void Sweep()
        {
            foreach (var port in MainV2.Comports.ToArray())
            {
                bool ready;
                lock (_lock)
                    ready = _ready.Contains(port);

                if (!ready)
                    continue;

                if (!IsOpen(port))
                {
                    lock (_lock)
                        _ready.Remove(port);
                    _queue.Reset(port);
                    continue;
                }

                if (port.logreadmode)
                    continue;

                foreach (var mav in port.MAVlist)
                {
                    if (!Eligible(port, mav) || IsComplete(mav))
                        continue;

                    if (_queue.StateOf(port, mav.sysid, mav.compid, mav) != ParamLoadState.None)
                        continue;

                    log.InfoFormat("queue params {0}:{1}", mav.sysid, mav.compid);
                    _queue.Enqueue(port, mav.sysid, mav.compid, mav);
                    OnStateChanged(mav);
                }
            }
        }

        private void Download(VehicleParamQueue.Entry entry)
        {
            var port = (MAVLinkInterface) entry.Port;
            var mav = (MAVState) entry.Vehicle;

            if (!Current(port, mav))
            {
                _queue.Remove(entry);
                return;
            }

            // something else got them (the dialog's download, a refresh)
            if (IsComplete(mav))
            {
                _queue.Complete(entry);
                OnStateChanged(mav);
                return;
            }

            // a command or a transfer has the port: wait for it
            if (port.giveComport)
            {
                _queue.Requeue(entry);
                return;
            }

            OnStateChanged(mav);

            if (!WaitForCapabilities(port, mav))
            {
                Finish(entry, mav, false);
                return;
            }

            if ((mav.cs.capabilities & (uint) MAVLink.MAV_PROTOCOL_CAPABILITY.FTP) == 0 ||
                !Settings.Instance.GetBoolean("UseMavFtpParams", true))
            {
                log.InfoFormat("params {0}:{1} no MAVFTP, left for the switch", mav.sysid, mav.compid);
                _queue.NeedsManual(entry);
                OnStateChanged(mav);
                return;
            }

            log.InfoFormat("get params {0}:{1} in the background", mav.sysid, mav.compid);

            var cancel = new CancellationTokenSource();
            lock (_lock)
            {
                // a Suspend between the dequeue and here
                if (_suspended > 0)
                {
                    _queue.Requeue(entry);
                    return;
                }

                // or the port went back to connecting
                if (!_ready.Contains(port))
                {
                    _queue.Remove(entry);
                    return;
                }

                _jobCancel = cancel;
                _jobPort = port;
                _jobSuspended = false;
            }

            MemoryStream file = null;
            bool suspended;
            try
            {
                var task = Task.Run(() => new MAVFtp(port, mav.sysid, mav.compid).GetFile(ParamFile, cancel, true, 110));

                var cancelled = DateTime.MinValue;
                while (!task.IsCompleted)
                {
                    Thread.Sleep(100);

                    if (!cancel.IsCancellationRequested && !Current(port, mav))
                        cancel.Cancel();

                    if (cancel.IsCancellationRequested)
                    {
                        if (cancelled == DateTime.MinValue)
                            cancelled = DateTime.UtcNow;
                        // a transfer that doesn't notice is left to its own timeouts
                        else if ((DateTime.UtcNow - cancelled).TotalSeconds > 10)
                            break;
                    }
                }

                if (task.Status == TaskStatus.RanToCompletion && !cancel.IsCancellationRequested)
                    file = task.Result;
                else if (task.IsFaulted)
                    log.Error(task.Exception);
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
            finally
            {
                lock (_lock)
                {
                    suspended = _jobSuspended;
                    _jobCancel = null;
                    _jobPort = null;
                    _jobSuspended = false;
                }
            }

            if (!Current(port, mav))
            {
                _queue.Remove(entry);
                return;
            }

            if (suspended)
            {
                _queue.Requeue(entry);
                OnStateChanged(mav);
                return;
            }

            MAVLink.MAVLinkParamList list = null;
            try
            {
                if (file != null && file.Length > 0)
                    list = parampck.unpack(file.ToArray());
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }

            if (list == null || list.Count == 0)
            {
                Finish(entry, mav, false);
                return;
            }

            // as MAVLinkInterface.getParamListMavftpAsync stores them
            mav.param.Clear();
            mav.param.TotalReported = list.Count;
            mav.param.AddRange(list);

            var gen = new MAVLink.MavlinkParse();
            foreach (var p in list)
            {
                mav.param_types[p.Name] = p.Type;
                try
                {
                    var value = new MAVLink.mavlink_param_value_t((float) p.Value, (ushort) list.Count, 0,
                        p.Name.MakeBytesSize(16), (byte) p.Type);
                    port.SaveToTlog(mav.sysid > 255
                        ? gen.GenerateMAVLinkPacket20(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE, value, false, mav.sysid,
                            mav.compid)
                        : gen.GenerateMAVLinkPacket10(MAVLink.MAVLINK_MSG_ID.PARAM_VALUE, value, mav.sysid,
                            mav.compid));
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            }

            log.InfoFormat("got params {0}:{1} {2}", mav.sysid, mav.compid, list.Count);
            Finish(entry, mav, true);

            // the pages showing the selected vehicle's parameters reload on this, as after a getParamList
            if (ReferenceEquals(MainV2.comPort, port) && ReferenceEquals(port.MAV, mav) && MainV2.instance != null)
                MainV2.instance.BeginInvokeIfRequired(() => port.OnParamListChanged());
        }

        private void Finish(VehicleParamQueue.Entry entry, MAVState mav, bool ok)
        {
            if (ok)
                _queue.Complete(entry);
            else
                _queue.Fail(entry);
            OnStateChanged(mav);
        }

        // whether the vehicle supports MAVFTP comes with its version, which a vehicle not selected may not have sent
        private bool WaitForCapabilities(MAVLinkInterface port, MAVState mav)
        {
            if (mav.cs.capabilities != 0)
                return true;

            try
            {
                var request = new MAVLink.mavlink_command_long_t
                {
                    target_system = (byte) mav.sysid,
                    target_component = mav.compid,
                    command = (ushort) MAVLink.MAV_CMD.REQUEST_MESSAGE,
                    param1 = (float) MAVLink.MAVLINK_MSG_ID.AUTOPILOT_VERSION
                };
                port.generatePacket((int) MAVLink.MAVLINK_MSG_ID.COMMAND_LONG, request, mav.sysid, mav.compid);
            }
            catch (Exception ex)
            {
                log.Error(ex);
                return false;
            }

            var until = DateTime.UtcNow.AddSeconds(3);
            while (mav.cs.capabilities == 0 && DateTime.UtcNow < until && Current(port, mav))
                Thread.Sleep(100);

            return mav.cs.capabilities != 0;
        }
    }
}
