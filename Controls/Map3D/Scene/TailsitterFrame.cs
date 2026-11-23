using System;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Which frame a tailsitter's attitude arrives in. ArduPlane reports it in the hover frame while the tailsitter flies
    /// as a multicopter (the airframe turned nose down about its wings by 90° plus Q_TRIM_PITCH, so level while it hangs
    /// nose up) and in the airframe's own frame on the wing. The frame changes at the end of each transition, after the
    /// mode has changed, so the change is found in the attitude itself: from one report to the next it turns by the hover
    /// frame's angle about the wings while the airframe, read in the other frame, has hardly moved.
    /// </summary>
    public sealed class TailsitterFrame
    {
        /// <summary>The frame the vehicle's state says the attitude is in.</summary>
        public enum Expect
        {
            Wing,
            Hover,

            /// <summary>The mode flies both (AUTO's VTOL takeoffs and landings, QRTL's approach): the attitude decides.</summary>
            Either
        }

        // The airframe turning less than the near angle from one report to the next when read in the other frame, and
        // more than the far angle when read in the frame it was in, is the frame changing; only a real turn of more than
        // the far angle between two reports could pass for it. While the vehicle's state says the other frame, whichever
        // reading turns the airframe less is taken.
        const double NearDegrees = 35, FarDegrees = 55;

        /// <summary>
        /// How long the vehicle's state can say the other frame, with no change seen in the attitude, before the attitude
        /// is taken to be in it: longer than a transition takes.
        /// </summary>
        public const double SettleSeconds = 5;

        readonly double[] _reported = new double[9], _same = new double[9], _other = new double[9], _lastAirframe = new double[9];
        double _angle, _lastRoll, _lastPitch, _lastYaw;
        bool _seen, _disagreeing;
        DateTime _disagreeSince;

        /// <summary>Whether the attitude is taken to be in the hover frame.</summary>
        public bool Hover { get; private set; }

        /// <summary>Forget what was seen: the next report is taken in the frame the vehicle's state says.</summary>
        public void Clear()
        {
            _seen = false;
            _disagreeing = false;
            Hover = false;
        }

        /// <summary>
        /// Takes a report (degrees) and the frame the vehicle's state says; angle is the hover frame's, in degrees nose down
        /// from the airframe. Returns the degrees the airframe was turned nose up when the frame was taken to change without
        /// the attitude showing it (the state saying so for <see cref="SettleSeconds"/>), else 0.
        /// </summary>
        public double Update(Expect expect, double angle, double roll, double pitch, double yaw, DateTime now)
        {
            if (!_seen)
            {
                Hover = expect == Expect.Hover;
                _seen = true;
                _disagreeing = false;
                Remember(angle, roll, pitch, yaw);
                return 0;
            }

            if (angle != _angle)
            {
                Remember(angle, roll, pitch, yaw);
            }
            else if (roll != _lastRoll || pitch != _lastPitch || yaw != _lastYaw)
            {
                EulerRotation.FromEuler(roll, pitch, yaw, _reported);
                EulerRotation.PitchedUp(_reported, Hover ? angle : 0, _same);
                EulerRotation.PitchedUp(_reported, Hover ? 0 : angle, _other);
                double toSame = EulerRotation.Between(_lastAirframe, _same);
                double toOther = EulerRotation.Between(_lastAirframe, _other);
                bool expected = expect == (Hover ? Expect.Wing : Expect.Hover);
                if (expected ? toOther < toSame : toOther < NearDegrees && toSame > FarDegrees)
                    Hover = !Hover;
                Remember(angle, roll, pitch, yaw);
            }

            if (expect == Expect.Either || (expect == Expect.Hover) == Hover)
            {
                _disagreeing = false;
                return 0;
            }

            if (!_disagreeing)
            {
                _disagreeing = true;
                _disagreeSince = now;
                return 0;
            }

            if ((now - _disagreeSince).TotalSeconds < SettleSeconds)
                return 0;

            Hover = !Hover;
            _disagreeing = false;
            Remember(angle, roll, pitch, yaw);
            return Hover ? angle : -angle;
        }

        // the report and its airframe, in the frame now taken
        void Remember(double angle, double roll, double pitch, double yaw)
        {
            _angle = angle;
            _lastRoll = roll;
            _lastPitch = pitch;
            _lastYaw = yaw;
            EulerRotation.FromEuler(roll, pitch, yaw, _reported);
            EulerRotation.PitchedUp(_reported, Hover ? angle : 0, _lastAirframe);
        }
    }

    /// <summary>
    /// Body to north-east-down rotations from ArduPilot's Euler angles (yaw, then pitch, then roll), in degrees, as
    /// row-major 3x3 arrays.
    /// </summary>
    public static class EulerRotation
    {
        const double ToRadians = Math.PI / 180, ToDegrees = 180 / Math.PI;

        public static void FromEuler(double roll, double pitch, double yaw, double[] m)
        {
            double cr = Math.Cos(roll * ToRadians), sr = Math.Sin(roll * ToRadians);
            double cp = Math.Cos(pitch * ToRadians), sp = Math.Sin(pitch * ToRadians);
            double cy = Math.Cos(yaw * ToRadians), sy = Math.Sin(yaw * ToRadians);
            m[0] = cp * cy;
            m[1] = sr * sp * cy - cr * sy;
            m[2] = cr * sp * cy + sr * sy;
            m[3] = cp * sy;
            m[4] = sr * sp * sy + cr * cy;
            m[5] = cr * sp * sy - sr * cy;
            m[6] = -sp;
            m[7] = sr * cp;
            m[8] = cr * cp;
        }

        public static void ToEuler(double[] m, out double roll, out double pitch, out double yaw)
        {
            pitch = -Math.Asin(Math.Max(-1, Math.Min(1, m[6]))) * ToDegrees;
            roll = Math.Atan2(m[7], m[8]) * ToDegrees;
            yaw = Math.Atan2(m[3], m[0]) * ToDegrees;
        }

        /// <summary>m turned nose up by angle degrees about its own wings (m times that pitch), into result, which may be m.</summary>
        public static void PitchedUp(double[] m, double angle, double[] result)
        {
            double c = Math.Cos(angle * ToRadians), s = Math.Sin(angle * ToRadians);
            for (int row = 0; row < 9; row += 3)
            {
                double x = m[row], y = m[row + 1], z = m[row + 2];
                result[row] = x * c - z * s;
                result[row + 1] = y;
                result[row + 2] = x * s + z * c;
            }
        }

        /// <summary>The angle in degrees of the turn from one rotation to the other.</summary>
        public static double Between(double[] m, double[] n)
        {
            double trace = 0;
            for (int i = 0; i < 9; i++)
                trace += m[i] * n[i];
            return Math.Acos(Math.Max(-1, Math.Min(1, (trace - 1) / 2))) * ToDegrees;
        }
    }
}
