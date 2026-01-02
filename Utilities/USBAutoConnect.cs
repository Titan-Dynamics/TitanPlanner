using log4net;
using System;
using System.Reflection;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Handles automatic connection to ArduPilot/MAVLink USB devices when plugged in.
    /// </summary>
    public class USBAutoConnect
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        /// <summary>
        /// Delay in milliseconds to wait for USB device enumeration after plug-in.
        /// This allows the bootloader to pass and the device to fully enumerate.
        /// </summary>
        public int EnumerationDelayMs { get; set; } = 6000;

        private bool _enabled = false;
        private int _connectInProgress = 0;

        // Callbacks to MainV2
        private readonly Func<bool> _isConnected;
        private readonly Func<bool> _shouldBlock;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedSubscribe;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedUnsubscribe;
        private readonly Action _connect;

        /// <summary>
        /// Creates a new USB auto-connect handler.
        /// </summary>
        /// <param name="isConnected">Returns true if already connected to a device</param>
        /// <param name="shouldBlock">Returns true if auto-connect should be blocked (e.g., firmware install screen)</param>
        /// <param name="deviceChangedSubscribe">Action to subscribe to DeviceChanged event</param>
        /// <param name="deviceChangedUnsubscribe">Action to unsubscribe from DeviceChanged event</param>
        /// <param name="connect">Action to initiate connection (sets AUTO and connects)</param>
        public USBAutoConnect(
            Func<bool> isConnected,
            Func<bool> shouldBlock,
            Action<MainV2.WMDeviceChangeEventHandler> deviceChangedSubscribe,
            Action<MainV2.WMDeviceChangeEventHandler> deviceChangedUnsubscribe,
            Action connect)
        {
            _isConnected = isConnected;
            _shouldBlock = shouldBlock;
            _deviceChangedSubscribe = deviceChangedSubscribe;
            _deviceChangedUnsubscribe = deviceChangedUnsubscribe;
            _connect = connect;
        }

        /// <summary>
        /// Gets or sets whether auto-connect is enabled.
        /// </summary>
        public bool Enabled
        {
            get => _enabled;
            set => SetEnabled(value);
        }

        /// <summary>
        /// Enables or disables auto-connect for USB ArduPilot/MAVLink devices.
        /// </summary>
        public void SetEnabled(bool enabled)
        {
            if (enabled && !_enabled)
            {
                _deviceChangedSubscribe(OnUSBDeviceChanged);
                CheckForExistingDevice();
            }
            else if (!enabled && _enabled)
            {
                _deviceChangedUnsubscribe(OnUSBDeviceChanged);
            }

            _enabled = enabled;
        }

        /// <summary>
        /// Resets the connection-in-progress flag. Call this on disconnect.
        /// </summary>
        public void ResetState()
        {
            _connectInProgress = 0;
        }

        /// <summary>
        /// Checks for already-connected ArduPilot devices on startup.
        /// </summary>
        private void CheckForExistingDevice()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_isConnected())
                        return;

                    if (_shouldBlock())
                        return;

                    // Check if any ArduPilot device is connected
                    if (FindArduPilotPort() != null)
                    {
                        _connect();
                    }
                }
                catch (Exception ex)
                {
                    log.Error("Auto-connect startup error: " + ex.Message);
                }
            });
        }

        /// <summary>
        /// Handles USB device arrival for auto-connect.
        /// </summary>
        private void OnUSBDeviceChanged(MainV2.WM_DEVICECHANGE_enum cause)
        {
            if (cause != MainV2.WM_DEVICECHANGE_enum.DBT_DEVICEARRIVAL)
                return;

            if (_isConnected())
                return;

            if (_shouldBlock())
                return;

            if (System.Threading.Interlocked.CompareExchange(ref _connectInProgress, 1, 0) != 0)
                return;

            // Check if an ArduPilot device was plugged in
            if (FindArduPilotPort() == null)
            {
                _connectInProgress = 0;
                return;
            }

            // Wait for device to fully enumerate, then connect
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Threading.Thread.Sleep(EnumerationDelayMs);

                    if (_shouldBlock() || _isConnected())
                        return;

                    _connect();
                }
                catch (Exception ex)
                {
                    log.Error("Auto-connect error: " + ex.Message);
                }
                finally
                {
                    _connectInProgress = 0;
                }
            });
        }

        /// <summary>
        /// Finds the first connected ArduPilot device port.
        /// </summary>
        public static string FindArduPilotPort()
        {
            var deviceList = Win32DeviceMgmt.GetAllCOMPorts();
            foreach (var device in deviceList)
            {
                if (!string.IsNullOrEmpty(device.name) && IsArduPilotUSBDevice(device.hardwareid))
                {
                    return device.name;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if a USB hardware ID matches known ArduPilot/MAVLink devices.
        /// </summary>
        public static bool IsArduPilotUSBDevice(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return false;

            var hid = hardwareId.ToUpperInvariant();

            return hid.Contains("VID_1209") ||  // ArduPilot ChibiOS
                   hid.Contains("VID_0483") ||  // STM32 ChibiOS
                   hid.Contains("VID_2DAE") ||  // Hex/ProfiCNC
                   hid.Contains("VID_3162") ||  // Holybro
                   hid.Contains("VID_26AC") ||  // 3DR/PX4
                   hid.Contains("VID_27AC") ||  // CubePilot
                   hid.Contains("VID_2341") ||  // Arduino (legacy APM)
                   hid.Contains("VID_1FC9");    // NXP
        }
    }
}
