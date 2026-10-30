using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using log4net;
using MissionPlanner.Controls.Scene3D;
using MissionPlanner.Utilities;
using OpenTK;
using OpenTK.Graphics;
using OpenTK.Graphics.ES20;
using Timer = System.Windows.Forms.Timer;
using Vector3 = MissionPlanner.Utilities.Vector3;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// 3D view of the vehicle and mission over streamed satellite terrain.
    /// <para>
    /// Terrain comes from <see cref="TerrainEngine"/> (Terrain/), which refines a tile quadtree to the
    /// camera's needs and loads it on worker threads. Overlays (Overlays/) draw the mission, the vehicle and
    /// ADS-B traffic; Rendering/ holds the GL helpers. This class wires them together: it owns the GL
    /// context, takes telemetry from FlightData, places the camera and runs the frame.
    /// </para>
    /// </summary>
    public partial class Map3D : GLControl, IDeactivate
    {
        private static readonly ILog log = LogManager.GetLogger(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public static Map3D instance;
        private static GraphicsMode _graphicsMode;
        private static int _oldCacheRemoved;

        private readonly Map3DSettings _settings;
        private readonly WorldFrame _frame = new WorldFrame();
        private readonly VehicleTracker _vehicle = new VehicleTracker();
        private readonly CameraRig _camera = new CameraRig();
        private readonly SelectionParams _selection = new SelectionParams();
        private readonly STLModelLoader _stlLoader = new STLModelLoader();
        private readonly AdsbOverlay _adsb = new AdsbOverlay();

        // Created with the GL context on first paint.
        private TerrainEngine _terrain;
        private SkyRenderer _sky;
        private LineRenderer _lines;
        private MissionOverlay _mission;
        private VehicleOverlay _vehicleOverlay;
        private NerdStatsOverlay _nerdStats;
        private HudOverlay _hudOverlay;
        private bool _glFailed;

        // Set as soon as disposal starts: a paint arriving afterwards must not rebuild the renderers.
        private bool _disposed;

        // Frame timing and a per-frame snapshot for "Stats for nerds".
        private readonly FrameStats _frameStats = new FrameStats(NerdStatsOverlay.GraphSamples);
        private readonly SceneStats _sceneStats = new SceneStats();
        private readonly Stopwatch _frameClock = new Stopwatch();
        private readonly Stopwatch _cpuClock = new Stopwatch();
        private int _paintErrors;

        // Telemetry written by FlightData's update thread, read once per frame.
        private readonly object _telemetryLock = new object();
        private PointLatLngAlt _telemetryCenter = new PointLatLngAlt(0, 0, 100);
        private DateTime _telemetryTime;
        private Vector3 _rpy = new Vector3();
        private Vector3 _velocity = new Vector3();

        // The position the view is centred on: the vehicle, or the 2D map when disconnected. Render thread only.
        private PointLatLngAlt _viewCenter = PointLatLngAlt.Zero;
        private PointLatLngAlt _lastMap2DPosition = PointLatLngAlt.Zero;
        private DateTime _lastMap2DPositionChange = DateTime.MinValue;
        private double _groundHeight;

        // Free-look direction while disconnected, in degrees.
        private double _freeLookYaw;
        private double _freeLookPitch;

        // Last frame's matrices, for picking.
        private Matrix4 _view = Matrix4.Identity;
        private Matrix4 _projection = Matrix4.Identity;

        private readonly SettingsButton _settingsButton = new SettingsButton();
        private Timer timer1;
        private System.ComponentModel.IContainer components;
        private bool started;
        private bool onpaintrun;

        // Idle rendering: see NeedsFrame.
        private static readonly TimeSpan ActiveLinger = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan IdleInterval = TimeSpan.FromSeconds(1);
        private DateTime _lastActivity = DateTime.UtcNow;
        private DateTime _lastFrame;
        private PointLatLng _lastSeenMap2D;

        public Map3D() : base(GetGraphicsMode())
        {
            instance = this;
            _settings = Map3DSettings.Load();
            _stlLoader.CustomSTLPath = _settings.StlPath;
            // Parsed on a worker while the GL context is created, rather than on the UI thread in the first frame.
            _stlLoader.Preload();

            InitializeComponent();
            MouseMove += OnMouseMove;
            MouseDown += OnMouseDown;
            MouseUp += OnMouseUp;
            MouseWheel += OnMouseWheel;
            MouseDoubleClick += OnMouseDoubleClick;
            MouseLeave += OnMouseLeave;
            InitializeToolTip();

            try
            {
                // Don't wait for the monitor's refresh: this control renders on the UI thread alongside the HUD.
                VSync = false;
            }
            catch
            {
            }

            RemoveOldTileCache();
        }

        private static GraphicsMode GetGraphicsMode()
        {
            if (_graphicsMode != null)
                return _graphicsMode;

            // Prefer 32-bit colour, 24-bit depth, 8-bit stencil and 4x MSAA, falling back if the driver refuses.
            try
            {
                _graphicsMode = new GraphicsMode(new ColorFormat(32), 24, 8, 4);
            }
            catch
            {
                try
                {
                    _graphicsMode = new GraphicsMode(new ColorFormat(32), 24, 8, 0);
                }
                catch
                {
                    _graphicsMode = GraphicsMode.Default;
                }
            }

            return _graphicsMode;
        }

        /// <summary>The previous terrain loader kept its own tile cache; imagery now shares the 2D map's cache.</summary>
        private static void RemoveOldTileCache()
        {
            if (Interlocked.Exchange(ref _oldCacheRemoved, 1) != 0)
                return;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "MissionPlanner", "Map3DCache");
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                }
                catch (Exception ex)
                {
                    log.Info("Could not remove the old 3D map cache: " + ex.Message);
                }
            });
        }

        public List<Locationwp> WPs { get; set; }

        /// <summary>The vehicle (or map) position the view follows. Safe to set from any thread.</summary>
        public PointLatLngAlt LocationCenter
        {
            get
            {
                lock (_telemetryLock)
                    return new PointLatLngAlt(_telemetryCenter);
            }
            set
            {
                lock (_telemetryLock)
                {
                    _telemetryCenter = new PointLatLngAlt(value.Lat, value.Lng, value.Alt);
                    _telemetryTime = DateTime.Now;
                }
            }
        }

        /// <summary>Vehicle roll, pitch and yaw in degrees. Safe to set from any thread.</summary>
        public Vector3 rpy
        {
            get
            {
                lock (_telemetryLock)
                    return new Vector3(_rpy.X, _rpy.Y, _rpy.Z);
            }
            set
            {
                lock (_telemetryLock)
                    _rpy = new Vector3((float)Math.Round(value.X, 2), (float)Math.Round(value.Y, 2), (float)Math.Round(value.Z, 2));
            }
        }

        /// <summary>Vehicle velocity in m/s (north, east, down), used to extrapolate between updates.</summary>
        public Vector3 Velocity
        {
            get
            {
                lock (_telemetryLock)
                    return _velocity;
            }
            set
            {
                lock (_telemetryLock)
                    _velocity = new Vector3(value.X, value.Y, value.Z);
            }
        }

        /// <summary>True when a vehicle is connected and reporting a real position.</summary>
        private static bool IsVehicleConnected
        {
            get
            {
                if (MainV2.comPort?.BaseStream?.IsOpen != true)
                    return false;
                var loc = MainV2.comPort?.MAV?.cs?.Location;
                return loc != null && (loc.Lat != 0 || loc.Lng != 0);
            }
        }

        public void Deactivate()
        {
            timer1?.Stop();
            started = false;
            // Keep the GPU tile cache for when the view comes back, but stop loading for it.
            _terrain?.Pause();
        }

        private volatile bool _suspended;

        /// <summary>False while the map is hidden (<see cref="Suspend"/>); telemetry feeds can skip their work then.</summary>
        public bool IsActive
        {
            get { return !_suspended; }
        }

        /// <summary>
        /// The map was hidden: stops drawing and gives the terrain back (GPU tiles, loader threads, SRTM cells), but
        /// keeps the control and its GL context. Creating the context took the driver about 1.5 s on the UI thread
        /// every time the map was shown.
        /// </summary>
        public void Suspend()
        {
            _suspended = true;
            Deactivate();
            try
            {
                _settings.SaveCamera();
            }
            catch
            {
            }

            ReleaseTerrain();
        }

        /// <summary>Shown again after <see cref="Suspend"/>; the terrain is rebuilt on the next frame.</summary>
        public void Resume()
        {
            _suspended = false;
            // The time it was hidden is not a frame.
            _frameClock.Reset();
            Activate();
        }

        public void Activate()
        {
            if (!started)
            {
                timer1?.Start();
                started = true;
            }

            // Jump to the current pose instead of easing in from stale filter state.
            _vehicle.Reset();
            MarkActive();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            // Hidden (Suspend): a stray paint must not restart the frame timer or rebuild the terrain.
            if (_disposed || _suspended)
                return;

            if (!started)
                timer1.Start();
            started = true;
            onpaintrun = true;

            try
            {
                base.OnPaint(e);
            }
            catch
            {
                return;
            }

            Utilities.Extensions.ProtectReentry(doPaint);
        }

        public void doPaint()
        {
            if (_disposed || IsDisposed || DesignMode || _glFailed || Context == null || Width <= 0 || Height <= 0)
                return;

            try
            {
                if (!Context.IsCurrent)
                    MakeCurrent();

                EnsureRenderers();

                double interval = _frameClock.IsRunning ? _frameClock.Elapsed.TotalMilliseconds : 0;
                _frameClock.Restart();
                _cpuClock.Restart();
                _lastFrame = DateTime.UtcNow;
                RenderFrame();
                _sceneStats.CpuMs = _cpuClock.Elapsed.TotalMilliseconds;
                // Only while the panel is open (it keeps the frame rate up); idle one-second frames aren't stutter.
                if (interval > 0 && _settings.StatsForNerds)
                    _frameStats.Add(interval, _sceneStats.CpuMs);

                // Outside RenderFrame so it's there even before the view has a position.
                _settingsButton.Draw(Width, Height);
                SwapBuffers();
            }
            catch (GraphicsContextException)
            {
                // The context is not ready yet; try again next paint.
            }
            catch (Exception ex)
            {
                // A persistent failure (e.g. a driver rejecting a shader) would otherwise log every frame.
                if (_paintErrors++ < 5 || _paintErrors % 1000 == 0)
                    log.Error(ex);
                if (_terrain == null)
                    _glFailed = true;
            }
        }

        private void EnsureRenderers()
        {
            if (_sky == null)
            {
                _sky = new SkyRenderer();
                _lines = new LineRenderer();
                _mission = new MissionOverlay();
                _vehicleOverlay = new VehicleOverlay(_stlLoader);
                _nerdStats = new NerdStatsOverlay();
                _hudOverlay = new HudOverlay();

                try
                {
                    _sceneStats.Renderer = GL.GetString(StringName.Renderer) ?? "";
                    _sceneStats.GLVersion = GL.GetString(StringName.Version) ?? "";
                }
                catch
                {
                }
            }

            // Released while the map is hidden (Suspend) and made again on the first frame after.
            if (_terrain == null)
                _terrain = new TerrainEngine(_frame, new GMapImagerySource(GoogleSatelliteMapProvider.Instance));
        }

        private void RenderFrame()
        {
            PointLatLngAlt center;
            DateTime centerTime;
            Vector3 attitude, velocity;
            lock (_telemetryLock)
            {
                center = _telemetryCenter;
                centerTime = _telemetryTime;
                attitude = _rpy;
                velocity = _velocity;
            }

            float[] top, horizon;
            SkyColors(out top, out horizon);
            GL.Viewport(0, 0, Width, Height);
            GL.ClearColor(horizon[0], horizon[1], horizon[2], 1);
            // glClear honours the depth write mask, which a failed frame could have left off.
            GL.DepthMask(true);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            bool connected = IsVehicleConnected;
            _viewCenter = connected ? center : FollowMap2D(center);
            if (_viewCenter.Lat == 0 && _viewCenter.Lng == 0)
                return;

            _frame.Update(_viewCenter.Lat, _viewCenter.Lng);
            UpdateGroundHeight(_viewCenter);

            var cs = MainV2.comPort?.MAV?.cs;
            if (connected)
            {
                _vehicle.Update(_frame, _viewCenter, centerTime, velocity, attitude, _groundHeight,
                    cs?.nav_bearing ?? 0, cs?.groundcourse ?? 0);
                _vehicleOverlay.RecordTrail(_frame, _vehicle, _settings.ShowTrail, cs?.armed == true);

                // In FPV the eye stays a metre above the ground under the vehicle, or a landed vehicle would look
                // out from level with the terrain mesh, half of the view under it.
                if (_settings.FpvMode)
                    _camera.FirstPerson(_vehicle, _groundHeight + 1);
                else
                    _camera.Chase(_vehicle, _settings.CameraDistance, _settings.CameraHeight, _settings.CameraAngle);
            }
            else
            {
                // Free-look from 100 m above the 2D map's centre, starting from the last known heading.
                _vehicle.Reset();
                double x, y;
                _frame.ToLocal(_viewCenter.Lat, _viewCenter.Lng, out x, out y);
                _camera.Look(x, y, _groundHeight + 100, _vehicle.Yaw + _freeLookYaw, _freeLookPitch);
            }

            double heightAboveGround = Math.Max(1, _camera.Z - _groundHeight);
            double viewRadius = ViewRadius(heightAboveGround);
            float fov = (float)(_settings.FieldOfView * Math.PI / 180);
            float aspect = (float)Width / Height;

            // Terrain gets a near plane scaled to altitude for depth precision; overlays draw in a second pass
            // with a close near plane after clearing depth, so they are never hidden inside the terrain.
            float terrainNear = (float)Math.Max(0.25, Math.Min(25, heightAboveGround * 0.05));
            float far = (float)(viewRadius * 1.05);
            var terrainProjection = Matrix4.CreatePerspectiveFieldOfView(fov, aspect, terrainNear, far);
            var overlayProjection = Matrix4.CreatePerspectiveFieldOfView(fov, aspect, 0.1f, far);
            _view = _camera.ViewMatrix();
            _projection = overlayProjection;

            UpdateTerrain(terrainProjection, viewRadius, fov);

            GL.Disable(EnableCap.CullFace);
            GL.Disable(EnableCap.Blend);

            _sky.Draw(_view, terrainProjection, top, horizon);

            // Pass 1: terrain, and ADS-B depth-tested against it.
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(true);
            GL.DepthFunc(DepthFunction.Lequal);
            _terrain.Draw(ref terrainProjection, ref _view, viewRadius, horizon);

            var ownPosition = connected ? (cs?.Location ?? _viewCenter) : _viewCenter;
            _adsb.Draw(_lines, _frame, ownPosition, _settings.AdsbCircleSize, _camera.X, _camera.Y, _camera.Z,
                _settings.FieldOfView, Width, Height, ref terrainProjection, ref _view);

            // Pass 2: mission and vehicle.
            GL.Clear(ClearBufferMask.DepthBufferBit);
            _mission.DrawPlanLine(_lines, _frame, ref overlayProjection, ref _view);
            _mission.DrawMarkers(_frame, _viewCenter, _camera.X, _camera.Y, _settings.WaypointMarkerSize,
                ref overlayProjection, ref _view);

            if (connected)
            {
                if (!_settings.FpvMode)
                {
                    var target = _mission.NavigationTarget();
                    double targetAltitude = target != null ? _mission.DrawAltitude(target) : 0;
                    _vehicleOverlay.DrawIndicators(_lines, _settings, _vehicle, target, targetAltitude, _frame,
                        ref overlayProjection, ref _view);
                }

                if (_settings.ShowTrail)
                    _vehicleOverlay.DrawTrail(_lines, _frame, _vehicle, cs?.armed == true, ref overlayProjection, ref _view);

                if (!_settings.FpvMode)
                    _vehicleOverlay.DrawModel(_vehicle, _settings.VehicleScale, _settings.VehicleColor,
                        ref overlayProjection, ref _view);
            }

            // under the stats panel, which stays readable over it
            if (_settings.HudOverlay)
                _hudOverlay.Draw(GCSViews.FlightData.myhud, Width, Height);

            if (_settings.StatsForNerds)
            {
                UpdateSceneStats(connected, viewRadius);
                _nerdStats.Draw(_lines, Width, Height, _frameStats, _terrain.Stats, _sceneStats);
            }
        }

        private void UpdateSceneStats(bool connected, double viewRadius)
        {
            var s = _sceneStats;
            double fx, fy, fz;
            _camera.Forward(out fx, out fy, out fz);
            float fogStart, fogEnd;
            TerrainEngine.FogRange(viewRadius, out fogStart, out fogEnd);

            s.Connected = connected;
            s.Mode = !connected ? "Free look" : _settings.FpvMode ? "FPV" : "Chase";
            s.Heading =(Math.Atan2(fx, fy) * 180 / Math.PI + 360) % 360;
            s.Pitch = Math.Asin(Math.Max(-1, Math.Min(1, fz))) * 180 / Math.PI;
            s.ViewRadius = viewRadius;
            s.FogStart = fogStart;
            s.FogEnd = fogEnd;
            s.FovDegrees = _settings.FieldOfView;
            s.MaxZoom = _selection.MaxZoom;
            s.UtmZone = _frame.Zone;
            s.OriginMoves = Math.Max(0, _frame.Version - 1);
        }

        private void UpdateTerrain(Matrix4 terrainProjection, double viewRadius, float fov)
        {
            var cameraPosition = _frame.ToLatLng(_camera.X, _camera.Y, _camera.Z);
            double fx, fy, fz;
            _camera.Forward(out fx, out fy, out fz);

            _selection.CameraX = _camera.X;
            _selection.CameraY = _camera.Y;
            _selection.CameraZ = _camera.Z;
            _selection.ForwardX = fx;
            _selection.ForwardY = fy;
            _selection.ForwardZ = fz;
            _selection.CameraLat = cameraPosition.Lat;
            _selection.CameraLng = cameraPosition.Lng;
            _selection.Frustum = Frustum.FromViewProjection(Matrix4.Mult(_view, terrainProjection));
            _selection.ViewRadius = viewRadius;
            _selection.ViewportHeight = Height;
            _selection.FovY = fov;
            _selection.ReferenceHeight = _groundHeight;

            var timer = Stopwatch.StartNew();
            _terrain.Update(_selection);
            _sceneStats.TerrainMs = timer.Elapsed.TotalMilliseconds;
        }

        /// <summary>
        /// How far terrain is drawn: a little past the horizon for the camera's height, between 30 and 150 km.
        /// </summary>
        public static double ViewRadius(double heightAboveGround)
        {
            double horizon = 3570 * Math.Sqrt(Math.Max(1, heightAboveGround));
            return Math.Max(30000, Math.Min(150000, horizon * 1.15));
        }

        /// <summary>Keeps the last known ground height until the cell's data is in memory; never blocks the UI thread.</summary>
        private void UpdateGroundHeight(PointLatLngAlt position)
        {
            double ground;
            if (srtm.TryGetLoadedAltitude(position.Lat, position.Lng, out ground))
                _groundHeight = ground;
        }

        /// <summary>
        /// While disconnected the view follows the 2D map: immediately after a big jump, otherwise once it has
        /// settled for a second so panning the map doesn't thrash the terrain.
        /// </summary>
        private PointLatLngAlt FollowMap2D(PointLatLngAlt telemetryCenter)
        {
            var current = _viewCenter;
            if (current.Lat == 0 && current.Lng == 0)
                current = telemetryCenter;

            PointLatLng? map2D = null;
            try
            {
                map2D = GCSViews.FlightData.instance?.gMapControl1?.Position;
            }
            catch
            {
            }

            if (map2D == null || (map2D.Value.Lat == 0 && map2D.Value.Lng == 0))
                return current;

            var mapCenter = new PointLatLngAlt(map2D.Value.Lat, map2D.Value.Lng, 0);
            if (current.Lat == 0 && current.Lng == 0)
            {
                _lastMap2DPosition = mapCenter;
                _lastMap2DPositionChange = DateTime.Now;
                return mapCenter;
            }

            bool bigJump = current.GetDistance(mapCenter) > 1000;
            if ((_lastMap2DPosition.Lat == 0 && _lastMap2DPosition.Lng == 0) || _lastMap2DPosition.GetDistance(mapCenter) > 30)
            {
                _lastMap2DPosition = mapCenter;
                _lastMap2DPositionChange = DateTime.Now;
            }

            bool settled = (DateTime.Now - _lastMap2DPositionChange).TotalMilliseconds >= 1000;
            if (current.GetDistance(_lastMap2DPosition) > 30 && (bigJump || settled))
                return new PointLatLngAlt(_lastMap2DPosition);

            return current;
        }

        /// <summary>
        /// Sky colours from the theme, lightened to match the HUD. The horizon colour doubles as the fog colour,
        /// so distant terrain fades seamlessly into the sky.
        /// </summary>
        private static void SkyColors(out float[] top, out float[] horizon)
        {
            Color skyTop = ThemeManager.HudSkyTop.A > 0 ? ThemeManager.HudSkyTop : Color.Blue;
            Color skyBottom = ThemeManager.HudSkyBot.A > 0 ? ThemeManager.HudSkyBot : Color.LightBlue;
            top = Lighten(skyTop);
            horizon = Lighten(skyBottom);
        }

        private static float[] Lighten(Color c)
        {
            return new[] { c.R / 255f * 0.5f + 0.5f, c.G / 255f * 0.5f + 0.5f, c.B / 255f * 0.5f + 0.5f };
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _disposed = true;
                try
                {
                    // Keeps the camera placement the user ended with, including mouse adjustments.
                    _settings.SaveCamera();
                }
                catch
                {
                }

                timer1?.Stop();
                timer1?.Dispose();
                _adsbToolTip?.Dispose();
                _settingsToolTip?.Dispose();
                DisposeRenderers();

                if (instance == this)
                    instance = null;
            }

            base.Dispose(disposing);
        }

        private void DisposeRenderers()
        {
            ReleaseTerrain();

            if (_sky == null)
                return;

            MakeContextCurrent();
            try
            {
                _sky.Dispose();
                _lines.Dispose();
                _mission.Dispose();
                _vehicleOverlay.Dispose();
                _nerdStats.Dispose();
                _hudOverlay.Dispose();
                _settingsButton.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Map3D cleanup: " + ex.Message);
            }

            _sky = null;
        }

        private void MakeContextCurrent()
        {
            try
            {
                if (IsHandleCreated && Context != null && !Context.IsCurrent)
                    MakeCurrent();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Gives back the terrain: its GPU tiles and loader threads now, and once those threads have exited the SRTM
        /// cells, followed by a compacting GC so the memory goes back to Windows.
        /// </summary>
        private void ReleaseTerrain()
        {
            if (_terrain == null)
                return;

            MakeContextCurrent();

            // The engine stops its loader threads even if GL cleanup fails.
            var terrain = _terrain;
            _terrain = null;
            terrain.Dispose();

            // The terrain's elevation cells (26 MB each) would otherwise stay in memory after the map closes. A
            // worker may still be loading one, so release them once the workers have exited, off the UI thread.
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    terrain.WaitForLoaderExit(30000);
                    ElevationSampler.ReleaseCells();

                    // The cells and tile buffers sit on the large object heap, which is only collected and
                    // compacted by a full GC; do one now so the memory goes back to Windows when the map closes.
                    System.Runtime.GCSettings.LargeObjectHeapCompactionMode =
                        System.Runtime.GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect();
                }
                catch (Exception ex)
                {
                    log.Error(ex);
                }
            });
        }

        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.SuspendLayout();
            //
            // timer1
            //
            this.timer1.Interval = 12;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            //
            // Map3D
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.Margin = new System.Windows.Forms.Padding(5, 5, 5, 5);
            this.Name = "Map3D";
            this.Size = new System.Drawing.Size(853, 591);
            this.Resize += new System.EventHandler(this.test_Resize);
            this.ResumeLayout(false);
        }

        private void OpenSettings()
        {
            Map3DSettingsDialog.Show(this, _settings);
            _stlLoader.CustomSTLPath = _settings.StlPath;
            MarkActive();
            Invalidate();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            if (onpaintrun && IsHandleCreated && !IsDisposed && !Disposing && NeedsFrame())
            {
                Invalidate();
                onpaintrun = false;
            }
        }

        private void test_Resize(object sender, EventArgs e)
        {
            MarkActive();
            Invalidate();
        }

        /// <summary>Keeps the full frame rate for a moment, e.g. after input.</summary>
        private void MarkActive()
        {
            _lastActivity = DateTime.UtcNow;
        }

        /// <summary>
        /// Full frame rate while anything changes: a connected vehicle, terrain streaming, spinning markers,
        /// ADS-B, the stats panel, recent input or the 2D map moving. Otherwise one frame a second, which still
        /// picks up anything missed (such as elevation data arriving).
        /// </summary>
        private bool NeedsFrame()
        {
            var now = DateTime.UtcNow;
            if (Map2DMoved())
                _lastActivity = now;

            bool animating = IsVehicleConnected || _settings.StatsForNerds || _terrain == null || _terrain.IsBusy ||
                             (_mission != null && _mission.MarkersDrawn > 0) || _adsb.ScreenPositions.Count > 0;
            return animating || now - _lastActivity < ActiveLinger || now - _lastFrame >= IdleInterval;
        }

        private bool Map2DMoved()
        {
            try
            {
                var position = GCSViews.FlightData.instance?.gMapControl1?.Position;
                if (position == null || position.Value == _lastSeenMap2D)
                    return false;
                _lastSeenMap2D = position.Value;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
