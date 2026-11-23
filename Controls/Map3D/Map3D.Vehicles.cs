using System;
using System.Collections.Generic;
using MissionPlanner.Controls.Scene3D;
using MissionPlanner.Maps;
using MissionPlanner.Utilities;
using OpenTK;
using Vector3 = MissionPlanner.Utilities.Vector3;

namespace MissionPlanner.Controls
{
    // Every connected vehicle with a position. The selected one is followed by the camera; the others are drawn as the
    // 2D map draws them (Config > Planner's secondary display style). Each keeps its own smoothed pose and trail, so
    // selecting another puts the camera straight at it rather than easing across or joining up their trails.
    public partial class Map3D
    {
        /// <summary>
        /// A vehicle's telemetry as FlightData read it: position with altitude AMSL (the 3D map's ground plus the height
        /// above home), attitude in degrees, velocity in m/s (north, east, down).
        /// </summary>
        public sealed class VehicleFeed
        {
            public readonly MAVState Vehicle;
            public readonly PointLatLngAlt Position;
            public readonly Vector3 Attitude;
            public readonly Vector3 Velocity;
            public readonly DateTime Time = DateTime.Now;

            public VehicleFeed(MAVState vehicle, PointLatLngAlt position, Vector3 attitude, Vector3 velocity)
            {
                Vehicle = vehicle;
                Position = position;
                Attitude = attitude;
                Velocity = velocity;
            }
        }

        // A vehicle in the scene: its smoothed pose, trail and model, and the telemetry they follow.
        private sealed class VehicleView : IDisposable
        {
            public readonly MAVState Vehicle;
            public readonly STLModelLoader Loader;
            public readonly VehicleTracker Tracker = new VehicleTracker();
            public readonly VehicleOverlay Overlay;
            public VehicleFeed Feed;
            public double Ground;

            // whether it is a tailsitter and its hover frame's angle, read from its parameters every few seconds
            public bool Tailsitter;
            public double HoverAngle = 90;
            public DateTime TailsitterRead = DateTime.MinValue;

            // the mode of the last heartbeat read
            public MAVLink.MAVLinkMessage Heartbeat;
            public uint Mode;

            public VehicleView(MAVState vehicle, string stlPath)
            {
                Vehicle = vehicle;
                Loader = new STLModelLoader { CustomSTLPath = stlPath, Vehicle = vehicle };
                Loader.Preload();
                Overlay = new VehicleOverlay(Loader);
            }

            public void Dispose()
            {
                Overlay.Dispose();
            }
        }

        // Written by FlightData's update thread under _telemetryLock; the views are the render thread's.
        private List<VehicleFeed> _feeds = new List<VehicleFeed>();
        private readonly Dictionary<MAVState, VehicleView> _vehicles = new Dictionary<MAVState, VehicleView>();
        private readonly List<MAVState> _goneVehicles = new List<MAVState>();
        private VehicleView _followed;

        /// <summary>
        /// Every vehicle with a position, replacing the last set; the selected one is followed. Safe to call from any
        /// thread.
        /// </summary>
        public void SetVehicles(List<VehicleFeed> feeds)
        {
            var selected = MainV2.comPort?.MAV;
            lock (_telemetryLock)
            {
                _feeds = feeds;
                foreach (var feed in feeds)
                    if (feed.Vehicle == selected)
                        _telemetryCenter = feed.Position;
            }
        }

        /// <summary>
        /// Matches the views to the latest feeds, adding and dropping vehicles as they come and go, and returns the
        /// selected vehicle's view, or null.
        /// </summary>
        private VehicleView SyncVehicles(List<VehicleFeed> feeds)
        {
            var selected = MainV2.comPort?.MAV;
            VehicleView followed = null;
            foreach (var feed in feeds)
            {
                VehicleView view;
                if (!_vehicles.TryGetValue(feed.Vehicle, out view))
                {
                    view = new VehicleView(feed.Vehicle, _settings.StlPath);
                    _vehicles.Add(feed.Vehicle, view);
                }

                view.Feed = feed;
                if (feed.Vehicle == selected)
                    followed = view;
            }

            // every feed has a view now, so more views than feeds means some vehicles have gone
            if (_vehicles.Count > feeds.Count)
            {
                _goneVehicles.Clear();
                foreach (var vehicle in _vehicles.Keys)
                    if (!feeds.Exists(feed => feed.Vehicle == vehicle))
                        _goneVehicles.Add(vehicle);

                foreach (var vehicle in _goneVehicles)
                {
                    _vehicles[vehicle].Dispose();
                    _vehicles.Remove(vehicle);
                }
            }

            return followed;
        }

        /// <summary>Moves every vehicle to this frame's pose and records its trail.</summary>
        private void UpdateVehicles()
        {
            foreach (var view in _vehicles.Values)
            {
                var feed = view.Feed;
                var cs = view.Vehicle.cs;
                double ground;
                if (srtm.TryGetLoadedAltitude(feed.Position.Lat, feed.Position.Lng, out ground))
                    view.Ground = ground;

                view.Tracker.Update(_frame, feed.Position, feed.Time, feed.Velocity, feed.Attitude, view.Ground,
                    cs.nav_bearing, cs.groundcourse, _settings.SmoothMovement, cs.armed, TailsitterExpect(view),
                    view.HoverAngle);
                view.Overlay.RecordTrail(_frame, view.Tracker, _settings.ShowTrail, cs.armed);
            }
        }

        // ArduPlane's modes that always hover (QSTABILIZE, QHOVER, QLOITER, QLAND, QAUTOTUNE, QACRO), and those that hover
        // for some of what they fly (AUTO, RTL, AVOID_ADSB and GUIDED with their VTOL legs, QRTL with its approach on the
        // wing); the rest fly on the wing
        private static readonly HashSet<uint> HoverModes = new HashSet<uint> { 17, 18, 19, 20, 22, 23 };
        private static readonly HashSet<uint> MixedModes = new HashSet<uint> { 10, 11, 14, 15, 21 };

        /// <summary>
        /// For a tailsitter (Q_TAILSIT_ENABLE set), the frame its state says its attitude is in: its VTOL state while
        /// it sends one, else its mode. Null for anything else.
        /// </summary>
        private static TailsitterFrame.Expect? TailsitterExpect(VehicleView view)
        {
            var mav = view.Vehicle;
            var now = DateTime.Now;
            if (now - view.TailsitterRead > TimeSpan.FromSeconds(3) || now < view.TailsitterRead)
            {
                view.TailsitterRead = now;
                view.Tailsitter = ReadTailsitter(mav, out view.HoverAngle);
            }

            if (!view.Tailsitter)
                return null;

            var heartbeat = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.HEARTBEAT);
            if (heartbeat == null)
                return TailsitterFrame.Expect.Either;

            // EXTENDED_SYS_STATE isn't streamed by default, but when it is, its VTOL state says the frame exactly:
            // the hover frame from the end of the transition into hover to the end of the one out of it
            var state = mav.getPacketLast((uint)MAVLink.MAVLINK_MSG_ID.EXTENDED_SYS_STATE);
            if (state != null && (heartbeat.rxtime - state.rxtime).TotalSeconds < 3)
            {
                switch ((MAVLink.MAV_VTOL_STATE)mav.cs.vtol_state)
                {
                    case MAVLink.MAV_VTOL_STATE.MC:
                    case MAVLink.MAV_VTOL_STATE.TRANSITION_TO_FW:
                        return TailsitterFrame.Expect.Hover;
                    case MAVLink.MAV_VTOL_STATE.FW:
                    case MAVLink.MAV_VTOL_STATE.TRANSITION_TO_MC:
                        return TailsitterFrame.Expect.Wing;
                }
            }

            if (!ReferenceEquals(heartbeat, view.Heartbeat))
            {
                view.Heartbeat = heartbeat;
                view.Mode = heartbeat.ToStructure<MAVLink.mavlink_heartbeat_t>().custom_mode;
            }

            if (HoverModes.Contains(view.Mode))
                return TailsitterFrame.Expect.Hover;
            return MixedModes.Contains(view.Mode) ? TailsitterFrame.Expect.Either : TailsitterFrame.Expect.Wing;
        }

        // a quadplane with Q_TAILSIT_ENABLE set; its attitude's hover frame is 90° plus Q_TRIM_PITCH nose down from it
        private static bool ReadTailsitter(MAVState mav, out double hoverAngle)
        {
            hoverAngle = 90;
            if (!ArduPilot.Common.IsQuadPlane(mav))
                return false;

            try
            {
                var enable = mav.param["Q_TAILSIT_ENABLE"];
                if (enable == null || enable.Value <= 0)
                    return false;

                var trim = mav.param["Q_TRIM_PITCH"];
                if (trim != null)
                    hoverAngle = 90 + trim.Value;
                return true;
            }
            catch
            {
                // the parameters being reloaded
                return false;
            }
        }

        /// <summary>
        /// The vehicles' models: the selected one solid (not in FPV, where the camera is inside it), then the others
        /// with the secondary display style's opacity, so faded ones in front of it blend over it, each in a ring the
        /// size of the waypoint markers with its system id above.
        /// </summary>
        private void DrawVehicles(ref Matrix4 projection)
        {
            if (_followed != null && !_settings.FpvMode && _vehicles.ContainsValue(_followed))
                _followed.Overlay.DrawModel(_followed.Tracker, _settings.VehicleScale, _settings.VehicleColor, 1,
                    ref projection, ref _view);

            float opacity = InactiveOpacity();
            if (opacity <= 0)
                return;

            _vehicleMarkers.Begin(_settings.VehicleColor, _camera.X, _camera.Y, _camera.Z, _settings.WaypointMarkerSize);
            foreach (var view in _vehicles.Values)
            {
                if (view == _followed)
                    continue;
                view.Overlay.DrawModel(view.Tracker, _settings.VehicleScale, _settings.VehicleColor, opacity,
                    ref projection, ref _view);
                _vehicleMarkers.Add(view.Tracker, view.Vehicle.sysid.ToString());
            }

            _vehicleMarkers.Draw(ref projection, ref _view);
        }

        private static float InactiveOpacity()
        {
            switch (GMapMarkerBase.InactiveDisplayStyle)
            {
                case GMapMarkerBase.InactiveDisplayStyleEnum.Hidden:
                    return 0;
                case GMapMarkerBase.InactiveDisplayStyleEnum.Transparent:
                    return GMapMarkerBase.InactiveOpacity;
                default:
                    return 1;
            }
        }

        /// <summary>Jump to the current poses instead of easing in from stale filter state.</summary>
        private void ResetVehicles()
        {
            foreach (var view in _vehicles.Values)
                view.Tracker.Reset();
        }

        private void DisposeVehicles()
        {
            foreach (var view in _vehicles.Values)
                view.Dispose();
            _vehicles.Clear();
            _followed = null;
        }
    }
}
