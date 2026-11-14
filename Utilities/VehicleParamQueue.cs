using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Utilities
{
    public enum ParamLoadState
    {
        /// <summary>Not known to the loader.</summary>
        None,

        /// <summary>Waiting its turn.</summary>
        Pending,

        /// <summary>Being downloaded now.</summary>
        Loading,

        Loaded,

        /// <summary>The background download failed; the download with the dialog is left to do it.</summary>
        Failed,

        /// <summary>The vehicle can't be loaded in the background (no MAVFTP).</summary>
        NeedsManual
    }

    /// <summary>
    /// The vehicles whose parameters are still to be downloaded in the background, in the order they will be, and
    /// what became of each. One per port, system id and component id. The port and vehicle are opaque here
    /// (VehicleParamLoader gives a MAVLinkInterface and a MAVState).
    /// </summary>
    public class VehicleParamQueue
    {
        /// <summary>Downloads tried before a vehicle is given up on.</summary>
        public const int MaxAttempts = 2;

        public class Entry
        {
            public object Port;
            public uint SysId;
            public byte CompId;

            /// <summary>The vehicle as it was when queued: a reconnect makes a new one under the same ids.</summary>
            public object Vehicle;

            public ParamLoadState State;
            public int Attempts;
        }

        private readonly object _lock = new object();

        // every vehicle known, the pending ones in the order they will be downloaded
        private readonly List<Entry> _entries = new List<Entry>();

        private Entry Find(object port, uint sysid, byte compid)
        {
            return _entries.FirstOrDefault(e => ReferenceEquals(e.Port, port) && e.SysId == sysid && e.CompId == compid);
        }

        /// <summary>
        /// Queues a vehicle, at the front if asked. One already known stays as it is (a pending one moves to the front
        /// if asked), unless it is a new vehicle under the same ids, which starts again. Returns its entry.
        /// </summary>
        public Entry Enqueue(object port, uint sysid, byte compid, object vehicle, bool front = false)
        {
            lock (_lock)
            {
                var entry = Find(port, sysid, compid);
                if (entry != null && !ReferenceEquals(entry.Vehicle, vehicle))
                {
                    _entries.Remove(entry);
                    entry = null;
                }

                if (entry == null)
                {
                    entry = new Entry
                    {
                        Port = port, SysId = sysid, CompId = compid, Vehicle = vehicle, State = ParamLoadState.Pending
                    };
                    if (front)
                        _entries.Insert(0, entry);
                    else
                        _entries.Add(entry);
                }
                else if (front && entry.State == ParamLoadState.Pending)
                {
                    _entries.Remove(entry);
                    _entries.Insert(0, entry);
                }

                return entry;
            }
        }

        /// <summary>
        /// Records a vehicle that something else is downloading (the connect), so it isn't queued as well.
        /// </summary>
        public Entry Track(object port, uint sysid, byte compid, object vehicle, ParamLoadState state)
        {
            lock (_lock)
            {
                var entry = Find(port, sysid, compid);
                if (entry != null)
                    _entries.Remove(entry);

                entry = new Entry {Port = port, SysId = sysid, CompId = compid, Vehicle = vehicle, State = state};
                _entries.Add(entry);
                return entry;
            }
        }

        /// <summary>The next vehicle to download, now Loading, or null.</summary>
        public Entry TryDequeue()
        {
            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e => e.State == ParamLoadState.Pending);
                if (entry != null)
                    entry.State = ParamLoadState.Loading;
                return entry;
            }
        }

        public void Complete(Entry entry)
        {
            lock (_lock)
                entry.State = ParamLoadState.Loaded;
        }

        /// <summary>A failed download goes to the back of the queue, until it has had its attempts.</summary>
        public void Fail(Entry entry)
        {
            lock (_lock)
            {
                entry.Attempts++;
                if (entry.Attempts >= MaxAttempts)
                {
                    entry.State = ParamLoadState.Failed;
                    return;
                }

                entry.State = ParamLoadState.Pending;
                if (_entries.Remove(entry))
                    _entries.Add(entry);
            }
        }

        public void NeedsManual(Entry entry)
        {
            lock (_lock)
                entry.State = ParamLoadState.NeedsManual;
        }

        /// <summary>Puts a vehicle that was being downloaded back at the front, as if it hadn't been started.</summary>
        public void Requeue(Entry entry)
        {
            lock (_lock)
            {
                if (!_entries.Remove(entry))
                    return;
                entry.State = ParamLoadState.Pending;
                _entries.Insert(0, entry);
            }
        }

        public void Remove(Entry entry)
        {
            lock (_lock)
                _entries.Remove(entry);
        }

        /// <summary>Forgets a port's vehicles (it closed).</summary>
        public void Reset(object port)
        {
            lock (_lock)
                _entries.RemoveAll(e => ReferenceEquals(e.Port, port));
        }

        /// <summary>The state of this vehicle, None if it isn't known or is a new one under the same ids.</summary>
        public ParamLoadState StateOf(object port, uint sysid, byte compid, object vehicle)
        {
            lock (_lock)
            {
                var entry = Find(port, sysid, compid);
                if (entry == null || !ReferenceEquals(entry.Vehicle, vehicle))
                    return ParamLoadState.None;
                return entry.State;
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_lock)
                    return _entries.Count(e => e.State == ParamLoadState.Pending);
            }
        }
    }
}
