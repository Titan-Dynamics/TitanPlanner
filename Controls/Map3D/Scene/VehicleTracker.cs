using System;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Smooths the vehicle's pose between telemetry updates: dead-reckons from the last fix using velocity,
    /// then runs Kalman filters on position, altitude, attitude and the heading indicators. Position is filtered
    /// in absolute UTM so moving the scene origin never makes the vehicle slide.
    /// </summary>
    public sealed class VehicleTracker
    {
        // Lower q = smoother, higher r = trust measurements less.
        readonly SimpleKalmanFilter _east = new SimpleKalmanFilter(0.02, 1.5);
        readonly SimpleKalmanFilter _north = new SimpleKalmanFilter(0.02, 1.5);
        readonly SimpleKalmanFilter _altitude = new SimpleKalmanFilter(0.02, 1.5);
        readonly SimpleKalmanFilter _roll = new SimpleKalmanFilter(0.015, 2.0);
        readonly SimpleKalmanFilter _pitch = new SimpleKalmanFilter(0.015, 2.0);
        readonly SimpleKalmanFilter _yaw = new SimpleKalmanFilter(0.015, 2.5);
        readonly SimpleKalmanFilter _navBearing = new SimpleKalmanFilter(0.015, 2.0);
        readonly SimpleKalmanFilter _gpsHeading = new SimpleKalmanFilter(0.015, 2.0);
        bool _initialized;
        int _zoneVersion = -1;

        // a fix this far (metres) from where the vehicle is drawn is jumped to rather than eased to
        const double JumpDistance = 100;

        /// <summary>Vehicle position in local scene coordinates (z is AMSL).</summary>
        public double X, Y, Z;

        public float Roll, Pitch, Yaw;
        public double NavBearing, GpsHeading;

        /// <summary>Forget filter state so the next update jumps straight to the vehicle.</summary>
        public void Reset()
        {
            _initialized = false;
        }

        /// <summary>
        /// Takes this frame's pose. With smooth off it is the telemetry as it last arrived, unfiltered and not carried
        /// along between updates. Disarmed, the position is never eased either, so a first fix or the GPS settling
        /// on the ground puts the vehicle (and the camera following it) straight there instead of gliding across
        /// the map; armed, a fix more than <see cref="JumpDistance"/> away is jumped to as well.
        /// </summary>
        public void Update(WorldFrame frame, PointLatLngAlt center, DateTime centerTime, Vector3 velocity,
            Vector3 attitude, double groundHeight, double rawNavBearing, double rawGpsHeading, bool smooth, bool armed)
        {
            if (!smooth)
            {
                double rawEast, rawNorth;
                frame.ToAbsolute(center.Lat, center.Lng, out rawEast, out rawNorth);
                X = rawEast - frame.OriginEasting;
                Y = rawNorth - frame.OriginNorthing;
                Z = Math.Max(center.Alt, groundHeight + 1);
                Roll = (float)attitude.X;
                Pitch = (float)attitude.Y;
                Yaw = (float)attitude.Z;
                NavBearing = rawNavBearing;
                GpsHeading = rawGpsHeading;

                // smoothing switched back on starts from here, not from where it left off
                _initialized = false;
                return;
            }

            var projected = LocationProjection.Project(center, velocity, centerTime, DateTime.Now);
            double east, north;
            frame.ToAbsolute(projected.Lat, projected.Lng, out east, out north);

            if (!_initialized || frame.ZoneVersion != _zoneVersion)
            {
                _east.Reset(east);
                _north.Reset(north);
                _altitude.Reset(projected.Alt);
                _roll.Reset(attitude.X);
                _pitch.Reset(attitude.Y);
                _yaw.Reset(attitude.Z);
                _navBearing.Reset(rawNavBearing);
                _gpsHeading.Reset(rawGpsHeading);
                _initialized = true;
                _zoneVersion = frame.ZoneVersion;
            }

            double jumpEast = east - _east.Value, jumpNorth = north - _north.Value;
            if (!armed || jumpEast * jumpEast + jumpNorth * jumpNorth > JumpDistance * JumpDistance)
            {
                _east.Reset(east);
                _north.Reset(north);
                _altitude.Reset(projected.Alt);
            }

            X = _east.Update(east) - frame.OriginEasting;
            Y = _north.Update(north) - frame.OriginNorthing;
            // kept a metre above the ground, which the reported altitude often reads under on the ground
            Z = Math.Max(_altitude.Update(projected.Alt), groundHeight + 1);

            Roll = (float)_roll.UpdateAngle(attitude.X);
            Pitch = (float)_pitch.UpdateAngle(attitude.Y);
            Yaw = (float)_yaw.UpdateAngle(attitude.Z);
            NavBearing = _navBearing.UpdateAngle(rawNavBearing);
            GpsHeading = _gpsHeading.UpdateAngle(rawGpsHeading);
        }
    }

    /// <summary>
    /// Where the camera sits and looks: orbiting behind the vehicle, from the cockpit (FPV), or free-look
    /// from a fixed point when no vehicle is connected.
    /// </summary>
    public sealed class CameraRig
    {
        public double X, Y, Z;
        public double LookX, LookY, LookZ;

        /// <summary>Camera roll in degrees (FPV only).</summary>
        public float Roll;

        /// <summary>Behind the vehicle at distance/height, rotated by angle (degrees) around it.</summary>
        public void Chase(VehicleTracker vehicle, double distance, double height, double angle)
        {
            double heading = (vehicle.Yaw + angle + 180) * Math.PI / 180;
            X = vehicle.X + Math.Sin(heading) * distance;
            Y = vehicle.Y + Math.Cos(heading) * distance;
            Z = vehicle.Z + height;
            LookX = vehicle.X;
            LookY = vehicle.Y;
            LookZ = vehicle.Z;
            Roll = 0;
        }

        /// <summary>From the vehicle, but no lower than minZ: on the ground the vehicle sits level with the terrain.</summary>
        public void FirstPerson(VehicleTracker vehicle, double minZ)
        {
            Look(vehicle.X, vehicle.Y, Math.Max(vehicle.Z, minZ), vehicle.Yaw, vehicle.Pitch);
            Roll = vehicle.Roll;
        }

        /// <summary>From a point, looking along yaw/pitch in degrees (pitch positive up).</summary>
        public void Look(double x, double y, double z, double yaw, double pitch)
        {
            X = x;
            Y = y;
            Z = z;
            double yawRad = yaw * Math.PI / 180;
            double pitchRad = pitch * Math.PI / 180;
            const double lookDistance = 100;
            LookX = x + Math.Sin(yawRad) * Math.Cos(pitchRad) * lookDistance;
            LookY = y + Math.Cos(yawRad) * Math.Cos(pitchRad) * lookDistance;
            LookZ = z + Math.Sin(pitchRad) * lookDistance;
            Roll = 0;
        }

        public OpenTK.Matrix4 ViewMatrix()
        {
            var view = OpenTK.Matrix4.LookAt((float)X, (float)Y, (float)Z, (float)LookX, (float)LookY, (float)LookZ, 0, 0, 1);
            if (Roll != 0)
                view = OpenTK.Matrix4.Mult(view, OpenTK.Matrix4.CreateRotationZ(Roll * (float)Math.PI / 180));
            return view;
        }

        /// <summary>Unit view direction.</summary>
        public void Forward(out double x, out double y, out double z)
        {
            x = LookX - X;
            y = LookY - Y;
            z = LookZ - Z;
            double length = Math.Sqrt(x * x + y * y + z * z);
            if (length < 1e-9)
            {
                x = 0;
                y = 1;
                z = 0;
                return;
            }

            x /= length;
            y /= length;
            z /= length;
        }
    }
}
