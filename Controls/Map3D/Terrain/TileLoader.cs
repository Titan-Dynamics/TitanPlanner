using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace MissionPlanner.Controls.Scene3D
{
    public struct ImageryResult
    {
        public TileLoadOutcome Outcome;

        /// <summary>BGRA, top row first.</summary>
        public byte[] Pixels;

        public int Width;
        public int Height;
        public string Error;
    }

    public interface ITileImagerySource
    {
        /// <summary>Called on loader threads; must be thread-safe.</summary>
        ImageryResult Fetch(TileId id);
    }

    /// <summary>
    /// Loads terrain tiles on a pool of worker threads. The render thread replaces the wanted set every frame
    /// with <see cref="SetRequests"/>; workers always take the best-ranked request next, and requests that
    /// drop out of the wanted set are discarded before they start. Each job fetches imagery, samples
    /// elevation and builds the mesh, so the render thread only has to upload finished results.
    /// </summary>
    public sealed class TileLoader : IDisposable
    {
        public const int DefaultWorkerCount = 8;

        readonly object _lock = new object();
        readonly Dictionary<TileId, TileRequest> _pending = new Dictionary<TileId, TileRequest>();
        readonly HashSet<TileId> _inFlight = new HashSet<TileId>();

        /// <summary>Finished but not yet collected by the render thread; not re-queued meanwhile.</summary>
        readonly HashSet<TileId> _done = new HashSet<TileId>();

        readonly ConcurrentQueue<TileLoadResult> _results = new ConcurrentQueue<TileLoadResult>();
        readonly List<Thread> _workers = new List<Thread>();
        readonly ITileImagerySource _imagery;
        readonly Func<double[], double[], float[], DemStatus> _sampleElevation;
        volatile bool _stop;

        public TileLoader(ITileImagerySource imagery, int workerCount = DefaultWorkerCount,
            Func<double[], double[], float[], DemStatus> sampleElevation = null)
        {
            _imagery = imagery;
            _sampleElevation = sampleElevation ?? ElevationSampler.Sample;

            for (int i = 0; i < workerCount; i++)
            {
                var thread = new Thread(WorkerLoop)
                {
                    IsBackground = true,
                    Name = "Map3D tile loader " + i,
                    Priority = ThreadPriority.BelowNormal
                };
                _workers.Add(thread);
                thread.Start();
            }
        }

        public int PendingCount
        {
            get
            {
                lock (_lock)
                    return _pending.Count;
            }
        }

        public int InFlightCount
        {
            get
            {
                lock (_lock)
                    return _inFlight.Count;
            }
        }

        public int WorkerCount
        {
            get { return _workers.Count; }
        }

        /// <summary>Finished tiles waiting for <see cref="TryGetResult"/>.</summary>
        public bool HasResults
        {
            get { return !_results.IsEmpty; }
        }

        /// <summary>Replaces everything not yet started with these requests.</summary>
        public void SetRequests(IList<TileRequest> requests)
        {
            lock (_lock)
            {
                _pending.Clear();
                foreach (var request in requests)
                {
                    if (_inFlight.Contains(request.Id) || _done.Contains(request.Id))
                        continue;
                    _pending[request.Id] = request;
                }

                if (_pending.Count > 0)
                    Monitor.PulseAll(_lock);
            }
        }

        public bool TryGetResult(out TileLoadResult result)
        {
            if (!_results.TryDequeue(out result))
                return false;

            lock (_lock)
                _done.Remove(result.Id);
            return true;
        }

        void WorkerLoop()
        {
            while (!_stop)
            {
                TileRequest job = null;
                lock (_lock)
                {
                    while (!_stop && _pending.Count == 0)
                        Monitor.Wait(_lock, 1000);
                    if (_stop)
                        return;

                    foreach (var candidate in _pending.Values)
                    {
                        if (job == null || candidate.Priority < job.Priority)
                            job = candidate;
                    }

                    _pending.Remove(job.Id);
                    _inFlight.Add(job.Id);
                }

                TileLoadResult result;
                try
                {
                    result = Process(job);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Map3D tile " + job.Id + " failed: " + ex);
                    result = new TileLoadResult
                    {
                        Id = job.Id, Kind = job.Kind, ZoneVersion = job.ZoneVersion,
                        Outcome = TileLoadOutcome.Failed, Error = ex.Message
                    };
                }

                lock (_lock)
                {
                    _inFlight.Remove(job.Id);
                    _done.Add(job.Id);
                }

                // Nobody collects results after Dispose; don't hold on to the mesh and pixels.
                if (!_stop)
                    _results.Enqueue(result);
            }
        }

        TileLoadResult Process(TileRequest job)
        {
            var result = new TileLoadResult { Id = job.Id, Kind = job.Kind, ZoneVersion = job.ZoneVersion };
            var timer = Stopwatch.StartNew();

            if (job.Kind == TileJobKind.Full)
            {
                var image = _imagery.Fetch(job.Id);
                result.FetchMs = timer.Elapsed.TotalMilliseconds;
                timer.Restart();
                if (image.Outcome != TileLoadOutcome.Loaded)
                {
                    result.Outcome = image.Outcome;
                    result.Error = image.Error;
                    return result;
                }

                result.Pixels = image.Pixels;
                result.Width = image.Width;
                result.Height = image.Height;
            }

            double[] rowLats, colLngs;
            TileMeshBuilder.GridLatLngs(job.Id, job.GridN, out rowLats, out colLngs);
            var heights = new float[(job.GridN + 1) * (job.GridN + 1)];

            bool fallback = _sampleElevation(rowLats, colLngs, heights) != DemStatus.Ready;
            if (fallback)
            {
                if (job.Kind == TileJobKind.Remesh)
                {
                    result.Outcome = TileLoadOutcome.StillPending;
                    return result;
                }

                if (job.FallbackHeights != null)
                {
                    ElevationSampler.SampleFromAncestor(job.Id, job.GridN, job.FallbackSource, job.FallbackHeights,
                        job.FallbackGridN, heights);
                }
                else
                {
                    for (int k = 0; k < heights.Length; k++)
                        heights[k] = job.FallbackHeight;
                }
            }

            result.Mesh = TileMeshBuilder.Build(job.Id, job.GridN, rowLats, colLngs, heights, job.Zone);
            result.HeightsAreFallback = fallback;
            result.Outcome = TileLoadOutcome.Loaded;
            result.BuildMs = timer.Elapsed.TotalMilliseconds;
            return result;
        }

        /// <summary>
        /// Stops the workers. They are background threads that exit after their current job (which may be an
        /// HTTP request), so this does not wait for them.
        /// </summary>
        public void Dispose()
        {
            _stop = true;
            lock (_lock)
            {
                _pending.Clear();
                Monitor.PulseAll(_lock);
            }

            DrainResults();
        }

        void DrainResults()
        {
            TileLoadResult discarded;
            while (_results.TryDequeue(out discarded))
            {
            }
        }

        /// <summary>
        /// After <see cref="Dispose"/>, waits up to timeoutMs for the workers to finish their last job. Returns
        /// false if any is still running. Blocks, so not for the UI thread.
        /// </summary>
        public bool WaitForWorkers(int timeoutMs)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            bool exited = true;
            foreach (var worker in _workers)
            {
                var remaining = deadline - DateTime.UtcNow;
                if (remaining < TimeSpan.Zero || !worker.Join(remaining))
                {
                    exited = false;
                    break;
                }
            }

            // A worker can finish between the stop check and the enqueue.
            DrainResults();
            return exited;
        }
    }
}
