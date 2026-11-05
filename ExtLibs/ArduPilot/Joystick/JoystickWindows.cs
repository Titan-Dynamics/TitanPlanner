using System;
using System.Collections.Generic;
using System.Linq;
using MissionPlanner.Utilities;
using SharpDX.DirectInput;

namespace MissionPlanner.Joystick
{
    public class JoystickWindows: JoystickBase, IDisposable
    {
        private SharpDX.DirectInput.Joystick joystick;

        public JoystickWindows(Func<MAVLinkInterface> func) : base(func)
        {
         
        }

        public override void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// Implement reccomended best practice dispose pattern
        /// http://msdn.microsoft.com/en-us/library/b1yfkh5e%28v=vs.110%29.aspx
        /// </summary>
        /// <param name="disposing"></param>
        virtual protected void Dispose(bool disposing)
        {
            try
            {
                //not sure if this is a problem from the finalizer?
                if (disposing && joystick != null && joystick.Properties != null)
                    joystick.Unacquire();
            }
            catch
            {
            }

            try
            {
                if (disposing && joystick != null)
                    joystick.Dispose();
            }
            catch
            {
            }

            //tell gc not to call finalize, this object will be GC'd quicker now.
            GC.SuppressFinalize(this);
        }

        public override bool AcquireJoystick(string name)
        {
            joystick = getJoyStickByNameInternal(name);

            if (joystick == null)
                return false;

            joystick.Acquire();

            joystick.Poll();

            return true;
        }

        public override int getNumberPOV()
        {
            return joystick.Capabilities.PovCount;
        }

        public override bool IsJoystickValid()
        {
            return joystick != null && !joystick.IsDisposed;
        }

        /// <summary>
        /// The axes, sliders and first hat DirectInput reports for the device, with its names for them ("X Axis",
        /// "Throttle", "Rudder"...).
        /// </summary>
        public override List<KeyValuePair<joystickaxis, string>> GetAxes()
        {
            var axes = new List<KeyValuePair<joystickaxis, string>>();
            try
            {
                var sliders = 0;
                foreach (var obj in joystick.GetObjects(DeviceObjectTypeFlags.AbsoluteAxis |
                                                        DeviceObjectTypeFlags.PointOfViewController))
                {
                    joystickaxis axis;
                    if (obj.ObjectType == ObjectGuid.XAxis)
                        axis = joystickaxis.X;
                    else if (obj.ObjectType == ObjectGuid.YAxis)
                        axis = joystickaxis.Y;
                    else if (obj.ObjectType == ObjectGuid.ZAxis)
                        axis = joystickaxis.Z;
                    else if (obj.ObjectType == ObjectGuid.RxAxis)
                        axis = joystickaxis.Rx;
                    else if (obj.ObjectType == ObjectGuid.RyAxis)
                        axis = joystickaxis.Ry;
                    else if (obj.ObjectType == ObjectGuid.RzAxis)
                        axis = joystickaxis.Rz;
                    else if (obj.ObjectType == ObjectGuid.Slider)
                        axis = sliders++ == 0 ? joystickaxis.Slider1 : joystickaxis.Slider2;
                    else if (obj.ObjectType == ObjectGuid.PovController)
                        axis = joystickaxis.Hatud1;
                    else
                        continue;

                    // the state holds two sliders and reads the first hat
                    if (axes.Any(a => a.Key == axis))
                        continue;

                    axes.Add(new KeyValuePair<joystickaxis, string>(axis, obj.Name.TrimUnPrintable()));
                }
            }
            catch
            {
            } // not acquired

            return axes.Count > 0 ? axes : base.GetAxes();
        }

        public override IMyJoystickState GetCurrentState()
        {
            joystick.Poll();
            return new WindowsJoystickState(joystick.GetCurrentState());
        }

        public override void UnAcquireJoyStick()
        {
            if (joystick == null)
                return;
            joystick.Unacquire();
        }

        public override int getNumButtons()
        {
            if (joystick == null)
                return 0;
            return joystick.Capabilities.ButtonCount;
        }

        internal new static IList<DeviceInstance> getDevices()
        {
            return new DirectInput().GetDevices(DeviceClass.GameControl, DeviceEnumerationFlags.AttachedOnly);
        }
        internal static SharpDX.DirectInput.Joystick getJoyStickByNameInternal(string name)
        {
            var joysticklist = getDevices();

            foreach (DeviceInstance device in joysticklist)
            {
                if (device.ProductName.TrimUnPrintable() == name)
                {
                    return new SharpDX.DirectInput.Joystick(new DirectInput(), device.InstanceGuid);
                }
            }

            return null;
        }

        public new static JoystickBase getJoyStickByName(string name)
        {
            var joysticklist = getDevices();

            foreach (var device in joysticklist)
            {
                if (device.ProductName.TrimUnPrintable() == name)
                {
                    var js = new JoystickWindows(() => null);
                    js.AcquireJoystick(name);
                    return js;
                }
            }

            return null;
        }
    }
}