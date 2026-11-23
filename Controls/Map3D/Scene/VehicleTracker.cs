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

        /// <summary>The attitude as reported, in degrees: for a hovering tailsitter, its hover frame's.</summary>
        public float Roll, Pitch, Yaw;
        public double NavBearing, GpsHeading;

        /// <summary>
        /// The model's attitude in degrees: the model turned NoseUp nose up about its wings, then by these. It is Roll,
        /// Pitch and Yaw, but for a tailsitter its airframe's.
        /// </summary>
        public float ModelRoll, ModelPitch, ModelYaw, NoseUp;

        // A tailsitter's airframe is smoothed as Euler angles in a frame turned _modelFrame degrees nose up from it, moved
        // by 90° whenever the airframe's pitch in it passes the limit, so they never near straight up or down, where roll
        // and yaw can't be told apart. The model is drawn turned back by NoseUp, which eases to _modelFrame: when the
        // airframe is taken to have turned without the attitude showing it, the model turns there.
        const double ModelFrameLimit = 60;
        const double NoseUpEaseSeconds = 0.3;
        readonly TailsitterFrame _tailsitter = new TailsitterFrame();
        readonly SimpleKalmanFilter _modelRoll = new SimpleKalmanFilter(0.015, 2.0);
        readonly SimpleKalmanFilter _modelPitch = new SimpleKalmanFilter(0.015, 2.0);
        readonly SimpleKalmanFilter _modelYaw = new SimpleKalmanFilter(0.015, 2.5);
        readonly double[] _rotation = new double[9];
        bool _modelInitialized, _drawnAsReported;
        double _modelFrame, _noseUp;
        DateTime _lastModelUpdate;

        /// <summary>Forget filter state so the next update jumps straight to the vehicle.</summary>
        public void Reset()
        {
            _initialized = false;
            _modelInitialized = false;
            _drawnAsReported = false;
        }

        /// <summary>
        /// Takes this frame's pose. With smooth off it is the telemetry as it last arrived, unfiltered and not carried
        /// along between updates. Disarmed, the position is never eased either, so a first fix or the GPS settling
        /// on the ground puts the vehicle (and the camera following it) straight there instead of gliding across
        /// the map; armed, a fix more than <see cref="JumpDistance"/> away is jumped to as well. For a tailsitter, tailsitter
        /// is the frame its state says the attitude is in and hoverAngle the hover frame's angle nose down from the airframe.
        /// </summary>
        public void Update(WorldFrame frame, PointLatLngAlt center, DateTime centerTime, Vector3 velocity,
            Vector3 attitude, double groundHeight, double rawNavBearing, double rawGpsHeading, bool smooth, bool armed,
            TailsitterFrame.Expect? tailsitter = null, double hoverAngle = 90)
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
                UpdateModel(attitude, false, tailsitter, hoverAngle);
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
            UpdateModel(attitude, true, tailsitter, hoverAngle);
        }

        void UpdateModel(Vector3 attitude, bool smooth, TailsitterFrame.Expect? tailsitter, double hoverAngle)
        {
            if (tailsitter == null)
            {
                _tailsitter.Clear();
                _modelInitialized = false;
                _drawnAsReported = true;
                ModelRoll = Roll;
                ModelPitch = Pitch;
                ModelYaw = Yaw;
                NoseUp = 0;
                return;
            }

            var now = DateTime.Now;
            double turned = _tailsitter.Update(tailsitter.Value, hoverAngle, attitude.X, attitude.Y, attitude.Z, now);
            // the airframe is the report turned this far nose up
            double reportedFrame = _tailsitter.Hover ? hoverAngle : 0;

            if (!smooth)
            {
                ModelRoll = (float)attitude.X;
                ModelPitch = (float)attitude.Y;
                ModelYaw = (float)attitude.Z;
                NoseUp = (float)reportedFrame;
                _modelInitialized = false;
                _drawnAsReported = false;
                return;
            }

            double roll, pitch, yaw;
            if (!_modelInitialized)
            {
                _modelFrame = reportedFrame;
                ReadAirframe(attitude, reportedFrame, out roll, out pitch, out yaw);
                if (Math.Abs(pitch) > ModelFrameLimit)
                {
                    _modelFrame += pitch > 0 ? 90 : -90;
                    ReadAirframe(attitude, reportedFrame, out roll, out pitch, out yaw);
                }

                _modelRoll.Reset(roll);
                _modelPitch.Reset(pitch);
                _modelYaw.Reset(yaw);
                // drawn as reported until now (before the vehicle was known to be a tailsitter), the model turns from
                // there to the airframe; otherwise it starts there
                _noseUp = _drawnAsReported ? _modelFrame - reportedFrame : _modelFrame;
                _drawnAsReported = false;
                _modelInitialized = true;
                _lastModelUpdate = now;
            }
            else
            {
                // the airframe taken to have turned: it is smoothed on from where it was, and the model turns to it
                _modelFrame += turned;
                ReadAirframe(attitude, reportedFrame, out roll, out pitch, out yaw);
                if (Math.Abs(pitch) > ModelFrameLimit)
                {
                    TurnModelFrame(pitch > 0 ? 90 : -90);
                    ReadAirframe(attitude, reportedFrame, out roll, out pitch, out yaw);
                }
            }

            ModelRoll = (float)_modelRoll.UpdateAngle(roll);
            ModelPitch = (float)_modelPitch.UpdateAngle(pitch);
            ModelYaw = (float)_modelYaw.UpdateAngle(yaw);

            double dt = Math.Max(0, Math.Min(0.5, (now - _lastModelUpdate).TotalSeconds));
            _lastModelUpdate = now;
            _noseUp += (_modelFrame - _noseUp) * (1 - Math.Exp(-dt / NoseUpEaseSeconds));

            // whole turns of the frame (a tailsitter looping) leave the model where it is
            if (Math.Abs(_modelFrame) >= 360)
            {
                double turns = 360 * Math.Truncate(_modelFrame / 360);
                _modelFrame -= turns;
                _noseUp -= turns;
            }

            NoseUp = (float)_noseUp;
        }

        // the airframe's attitude in the model's frame: the report turned nose up into the airframe, then back by the frame
        void ReadAirframe(Vector3 attitude, double reportedFrame, out double roll, out double pitch, out double yaw)
        {
            EulerRotation.FromEuler(attitude.X, attitude.Y, attitude.Z, _rotation);
            EulerRotation.PitchedUp(_rotation, reportedFrame - _modelFrame, _rotation);
            EulerRotation.ToEuler(_rotation, out roll, out pitch, out yaw);
        }

        // moves the model's frame step degrees nose up and the smoothed angles into it, keeping their rates, so the model
        // is drawn where it was
        void TurnModelFrame(double step)
        {
            double roll = _modelRoll.Value, pitch = _modelPitch.Value, yaw = _modelYaw.Value;
            EulerRotation.FromEuler(roll, pitch, yaw, _rotation);
            EulerRotation.PitchedUp(_rotation, -step, _rotation);
            double newRoll, newPitch, newYaw;
            EulerRotation.ToEuler(_rotation, out newRoll, out newPitch, out newYaw);
            _modelRoll.Shift(Wrap180(newRoll - roll));
            _modelPitch.Shift(Wrap180(newPitch - pitch));
            _modelYaw.Shift(Wrap180(newYaw - yaw));
            _modelFrame += step;
            _noseUp += step;
        }

        static double Wrap180(double angle)
        {
            angle %= 360;
            if (angle > 180)
                angle -= 360;
            else if (angle <= -180)
                angle += 360;
            return angle;
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
