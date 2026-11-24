using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// A fixed-wing plane's motors, as its servo outputs set them: each output set to Throttle, Throttle Left or Throttle
    /// Right drives one. ArduPlane has no motor test for them (MAV_CMD_DO_MOTOR_TEST is the quadplane's), so the Motor
    /// Setup page spins one by borrowing its output: the output's function is set to none, which lets DO_SET_SERVO drive
    /// it, and then set back.
    /// </summary>
    public static class FixedWingMotors
    {
        // SERVOn_FUNCTION values
        public const int None = 0, Throttle = 70, ThrottleLeft = 73, ThrottleRight = 74;

        public static bool IsMotor(int function)
        {
            return function == Throttle || function == ThrottleLeft || function == ThrottleRight;
        }

        /// <summary>A motor: its name on the page, its output (1-based, 0 for none) and the output's function.</summary>
        public sealed class Motor
        {
            public string Name;
            public int Servo;
            public int Function;
        }

        /// <summary>
        /// The motors of the outputs' functions (output number to function): the left ones first, then the right ones,
        /// then those on Throttle, each in output order. One is "Motor"; with more, Throttle Left and Throttle Right
        /// are the left and right motors and the rest are lettered.
        /// </summary>
        public static List<Motor> Find(IDictionary<int, int> functions)
        {
            var outputs = functions.Where(f => IsMotor(f.Value)).OrderBy(f => Rank(f.Value)).ThenBy(f => f.Key).ToList();
            var motors = new List<Motor>();
            if (outputs.Count == 1)
            {
                motors.Add(new Motor { Name = "Motor", Servo = outputs[0].Key, Function = outputs[0].Value });
                return motors;
            }

            foreach (var group in outputs.GroupBy(f => f.Value))
            {
                var side = group.Key == ThrottleLeft ? "Left motor" : group.Key == ThrottleRight ? "Right motor" : "Motor";
                bool lettered = group.Count() > 1 || group.Key == Throttle;
                char letter = 'A';
                foreach (var output in group)
                    motors.Add(new Motor
                    {
                        Name = lettered ? side + " " + letter++ : side,
                        Servo = output.Key,
                        Function = output.Value
                    });
            }

            return motors;
        }

        static int Rank(int function)
        {
            return function == ThrottleLeft ? 0 : function == ThrottleRight ? 1 : 2;
        }

        /// <summary>The output's pulse for a throttle in percent, between its SERVOn_MIN and SERVOn_MAX.</summary>
        public static int Pwm(int min, int max, bool reversed, double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            double travel = (max - min) * percent / 100;
            return (int)Math.Round(reversed ? max - travel : min + travel);
        }

        /// <summary>
        /// The functions to write to move a motor (on servo, 0 for none, with function) to target: the target's function
        /// goes to the motor's output and the motor's to the target, so nothing is lost.
        /// </summary>
        public static Dictionary<int, int> Move(int servo, int function, int target, int targetFunction)
        {
            var writes = new Dictionary<int, int>();
            if (servo > 0 && servo != target)
                writes[servo] = targetFunction;
            writes[target] = function;
            return writes;
        }

        /// <summary>The functions to write for two motors when the one that spun is the left (or right) one.</summary>
        public static Dictionary<int, int> AssignTwin(Motor spun, Motor other, bool spunIsLeft)
        {
            return new Dictionary<int, int>
            {
                [spun.Servo] = spunIsLeft ? ThrottleLeft : ThrottleRight,
                [other.Servo] = spunIsLeft ? ThrottleRight : ThrottleLeft
            };
        }

        /// <summary>A vehicle's borrowed outputs and the functions to set back, as kept in the settings.</summary>
        public static string FormatBorrowed(uint sysid, IDictionary<int, int> functions)
        {
            return sysid.ToString(CultureInfo.InvariantCulture) + ":" +
                   string.Join(";", functions.Select(f => f.Key.ToString(CultureInfo.InvariantCulture) + "=" +
                                                          f.Value.ToString(CultureInfo.InvariantCulture)));
        }

        public static bool ParseBorrowed(string text, out uint sysid, out Dictionary<int, int> functions)
        {
            sysid = 0;
            functions = new Dictionary<int, int>();
            if (string.IsNullOrEmpty(text))
                return false;

            int colon = text.IndexOf(':');
            if (colon < 0 || !uint.TryParse(text.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out sysid))
                return false;

            foreach (var pair in text.Substring(colon + 1).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split('=');
                int servo, function;
                if (parts.Length != 2 ||
                    !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out servo) ||
                    !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out function))
                    return false;
                functions[servo] = function;
            }

            return functions.Count > 0;
        }
    }
}
