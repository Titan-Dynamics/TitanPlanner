using log4net;
using MissionPlanner.ArduPilot;
using System;
using System.Reflection;

namespace MissionPlanner.Utilities
{
    public class USBAutoConnect
    {
        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        public int EnumerationDelayMs { get; set; } = 6000;

        private bool _enabled = false;
        private int _connectInProgress = 0;

        private readonly Func<bool> _isConnected;
        private readonly Func<bool> _shouldBlock;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedSubscribe;
        private readonly Action<MainV2.WMDeviceChangeEventHandler> _deviceChangedUnsubscribe;
        private readonly Action _refreshPortList;
        private readonly Action<string> _connect;

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

        public bool Enabled
        {
            get => _enabled;
            set => SetEnabled(value);
        }

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

        public void ResetState()
        {
            _connectInProgress = 0;
        }

        private void CheckForExistingDevice()
        {
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (_isConnected() || _shouldBlock())
                        return;

                    var port = FindArduPilotPort();
                    if (port != null)
                        _connect(port);
                }
                catch (Exception ex)
                {
                    log.Error("Auto-connect startup error: " + ex.Message);
                }
            });
        }

        private void OnUSBDeviceChanged(MainV2.WM_DEVICECHANGE_enum cause)
        {
            if (cause != MainV2.WM_DEVICECHANGE_enum.DBT_DEVICEARRIVAL)
                return;

            if (_isConnected() || _shouldBlock())
                return;

            if (System.Threading.Interlocked.CompareExchange(ref _connectInProgress, 1, 0) != 0)
                return;

            if (FindArduPilotPort() == null)
            {
                _connectInProgress = 0;
                return;
            }

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    System.Threading.Thread.Sleep(EnumerationDelayMs);

                    if (_shouldBlock() || _isConnected())
                        return;

                    _refreshPortList();

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

        public static string FindArduPilotPort()
        {
            var deviceList = Win32DeviceMgmt.GetAllCOMPorts();
            string fallbackPort = null;

            foreach (var device in deviceList)
            {
                if (string.IsNullOrEmpty(device.name) || !IsArduPilotUSBDevice(device.hardwareid))
                    continue;

                if (!string.IsNullOrEmpty(device.hardwareid) && device.hardwareid.Contains("MI_00"))
                    return device.name;

                if (!string.IsNullOrEmpty(device.description) &&
                    device.description.IndexOf("mavlink", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return device.name;
                }

                if (fallbackPort == null)
                    fallbackPort = device.name;
            }

            return fallbackPort;
        }

        public static bool IsArduPilotUSBDevice(string hardwareId)
        {
            if (string.IsNullOrEmpty(hardwareId))
                return false;

            var hid = hardwareId.ToUpperInvariant();

            return hid.Contains("VID_0483&PID_5740") ||  // STM32 ChibiOS
                   hid.Contains("VID_1209") ||           // ArduPilot (pid.codes)
                   hid.Contains("VID_2DAE") ||           // Hex/ProfiCNC
                   hid.Contains("VID_3162") ||           // Holybro
                   hid.Contains("VID_26AC") ||           // 3DR/PX4
                   hid.Contains("VID_27AC") ||           // CubePilot/VRBrain
                   hid.Contains("VID_1FC9") ||           // NXP
                   hid.Contains("VID_2341");             // Arduino
        }
    }
}
