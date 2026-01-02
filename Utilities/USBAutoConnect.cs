using log4net;
using MissionPlanner.ArduPilot;
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
        public int EnumerationDelayMs { get; set; } = 6600;

        private bool _enabled = false;
        private int _connectInProgress = 0;

        // Callbacks to MainV2
        private readonly Func<bool> _isConnected;
        private readonly Func<bool> _shouldBlock;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedSubscribe;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedUnsubscribe;
        private readonly Action _refreshPortList;
        private readonly Action<string> _connect;

        /// <summary>
        /// Creates a new USB auto-connect handler.
        /// </summary>
        /// <param name="isConnected">Returns true if already connected to a device</param>
        /// <param name="shouldBlock">Returns true if auto-connect should be blocked (e.g., firmware install screen)</param>
        /// <param name="deviceChangedSubscribe">Action to subscribe to DeviceChanged event</param>
        /// <param name="deviceChangedUnsubscribe">Action to unsubscribe from DeviceChanged event</param>
        /// <param name="refreshPortList">Action to refresh the serial port list</param>
        /// <param name="connect">Action to select port and initiate connection</param>
        public USBAutoConnect(
            Func<bool> isConnected,
            Func<bool> shouldBlock,
            Action<MainV2.WMDeviceChangeEventHandler> deviceChangedSubscribe,
            Action<MainV2.WMDeviceChangeEventHandler> deviceChangedUnsubscribe,
            Action refreshPortList,
            Action<string> connect)
        {
            _isConnected = isConnected;
            _shouldBlock = shouldBlock;
            _deviceChangedSubscribe = deviceChangedSubscribe;
            _deviceChangedUnsubscribe = deviceChangedUnsubscribe;
            _refreshPortList = refreshPortList;
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

                    var port = FindArduPilotPort();
                    if (port != null)
                    {
                        _connect(port);
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

            // Quick check that an ArduPilot device was plugged in
            if (FindArduPilotPort() == null)
            {
                _connectInProgress = 0;
                return;
            }

            // Wait for device to fully enumerate (e.g., Cube re-enumerates from COM6 to COM7/COM8)
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Threading.Thread.Sleep(EnumerationDelayMs);

                    if (_shouldBlock() || _isConnected())
                        return;

                    // Refresh port list after device has finished re-enumerating
                    _refreshPortList();

                    // Detect the port AFTER the delay when device has finished re-enumerating
                    var port = FindArduPilotPort();
                    if (port == null)
                    {
                        log.Warn("Auto-connect: No ArduPilot port found after enumeration delay");
                        return;
                    }

                    _connect(port);
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
        /// Finds the best connected ArduPilot device port.
        /// For composite USB devices (like CubeOrange), prefers the MAVLink interface.
        /// </summary>
        public static string FindArduPilotPort()
        {
            var deviceList = Win32DeviceMgmt.GetAllCOMPorts();
            string fallbackPort = null;

            foreach (var device in deviceList)
            {
                if (string.IsNullOrEmpty(device.name) || !IsArduPilotUSBDevice(device.hardwareid))
                    continue;

                // Prefer port with "Mavlink" in description (e.g., CubeOrange MAVLink interface)
                if (!string.IsNullOrEmpty(device.description) &&
                    device.description.IndexOf("mavlink", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device.name;
                }

                // Prefer MI_00 (interface 0) which is typically MAVLink on composite devices
                if (!string.IsNullOrEmpty(device.hardwareid) &&
                    device.hardwareid.Contains("MI_00"))
                {
                    return device.name;
                }

                // Keep first matching port as fallback
                if (fallbackPort == null)
                    fallbackPort = device.name;
            }

            return fallbackPort;
        }

        /// <summary>
        /// Checks if a USB hardware ID matches known ArduPilot/MAVLink devices.
        /// Uses the same VID/PID patterns as BoardDetect for consistency.
        /// </summary>
        public static bool IsArduPilotUSBDevice(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return false;

            var hid = hardwareId.ToUpperInvariant();

            // ChibiOS-based boards (most common modern ArduPilot devices)
            if (hid.Contains("VID_0483&PID_5740") ||  // STM32 ChibiOS
                hid.Contains("VID_1209&PID_5740"))    // ArduPilot ChibiOS
                return true;

            // Manufacturer VIDs (Hex, Holybro, CubePilot)
            if (hid.Contains("VID_2DAE") ||  // Hex/ProfiCNC
                hid.Contains("VID_3162") ||  // Holybro
                hid.Contains("VID_27AC"))    // CubePilot/VRBrain
                return true;

            // 3DR/PX4 devices
            if (hid.Contains("VID_26AC&PID_0010") ||  // PX4 FMU
                hid.Contains("VID_26AC&PID_0011") ||  // PX4v2
                hid.Contains("VID_26AC&PID_0012") ||  // Pixracer
                hid.Contains("VID_26AC&PID_0013") ||  // Pixhawk 3 Pro
                hid.Contains("VID_26AC&PID_0016") ||  // PX4RL
                hid.Contains("VID_26AC&PID_0021") ||  // PX4v3 X2.1
                hid.Contains("VID_26AC&PID_0032") ||  // CUAVv5/fmuv5
                hid.Contains("VID_26AC&PID_0001"))    // PX4v2 bootloader
                return true;

            // NXP
            if (hid.Contains("VID_1FC9&PID_001C"))  // NXP FMUK66
                return true;

            // Arduino (legacy APM boards)
            if (hid.Contains("VID_2341&PID_0010"))  // Arduino Mega 2560
                return true;

            return false;
        }
    }
}
