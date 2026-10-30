using org.mariuszgromada.math.mxparser.mathcollection;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Xml.Serialization;

namespace MissionPlanner.Utilities
{
    [Serializable]
    public enum DisplayNames
    {
        Basic,
        Advanced,
        Custom
    }

    public enum SeverityLevel
    {
        Emergency,
        Alert,
        Critical,
        Error,
        Warning,
        Notice,
        Info,
        Debug
    }

    [Serializable]
    public class DisplayView
    {
        public bool displayRTKInject { get; set; } = true;
        public bool displayGPSOrder { get; set; } = true;
        public bool displayHWIDs { get; set; } = true;
        public bool displayADSB { get; set; } = true;
        public DisplayNames displayName { get; set; }

        //MainV2 buttons
        public Boolean displaySimulation { get; set; } = false;
        public Boolean displayTerminal { get; set; } = true;
        public Boolean displayDonate { get; set; } = true;
        public Boolean displayHelp { get; set; } = true;

        //flight Data view
        public Boolean displayAnenometer { get; set; } = true;
        public Boolean displayQuickTab { get; set; } = true;
        public Boolean displayPreFlightTab { get; set; } = true;
        public Boolean displayAdvActionsTab { get; set; } = false;
        public Boolean displaySimpleActionsTab { get; set; } = true;
        public Boolean displayGaugesTab { get; set; } = false;
        public Boolean displayStatusTab { get; set; } = false;
        public Boolean displayScriptsTab { get; set; } = false;
        public Boolean displayTelemetryTab { get; set; } = true;
        public Boolean displayDataflashTab { get; set; } = true;
        public Boolean displayMessagesTab { get; set; } = true;
        public Boolean displayTransponderTab { get; set; } = true;
        public Boolean displayPayloadTab { get; set; } = true;
        public Boolean displayParamsTab { get; set; } = true;
        public Boolean displayVideoTab { get; set; } = true;
        public Boolean displayTuningTab { get; set; } = true;
        public Boolean displayInspectorTab { get; set; } = true;

        //flight plan
        public Boolean displayRallyPointsMenu { get; set; } = true;
        public Boolean displayGeoFenceMenu { get; set; } = true;
        public Boolean displaySplineCircleAutoWp { get; set; } = true;
        public Boolean displayTextAutoWp { get; set; } = true;
        public Boolean displayCircleSurveyAutoWp { get; set; } = true;
        public Boolean displayPoiMenu { get; set; } = true;
        public Boolean displayTrackerHomeMenu { get; set; } = true;
        public Boolean displayCheckHeightBox { get; set; } = true;
        public Boolean displayPluginAutoWp { get; set; } = true;

        //initial setup
        public Boolean displayInstallFirmware { get; set; } = true;
        public Boolean displayWizard { get; set; } = true;
        public Boolean displayFrameType { get; set; } = true;
        public Boolean displayAccelCalibration { get; set; } = true;
        public Boolean displayCompassConfiguration { get; set; } = true;
        public Boolean displayRadioCalibration { get; set; } = true;
        public Boolean displayEscCalibration { get; set; } = true;
        public Boolean displayFlightModes { get; set; } = true;
        public Boolean displayFailSafe { get; set; } = true;
        public Boolean displaySikRadio { get; set; } = true;
        public Boolean displayBattMonitor { get; set; } = true;
        public Boolean displayCAN { get; set; } = true;
        public Boolean displayCompassMotorCalib { get; set; } = true;
        public Boolean displayRangeFinder { get; set; } = true;
        public Boolean displayAirSpeed { get; set; } = true;
        public Boolean displayPx4Flow { get; set; } = true;
        public Boolean displayOpticalFlow { get; set; } = true;
        public Boolean displayOsd { get; set; } = true;
        public Boolean displayCameraGimbal { get; set; } = true;
        public Boolean displayMotorTest { get; set; } = true;
        public Boolean displayBluetooth { get; set; } = true;
        public Boolean displayParachute { get; set; } = true;
        public Boolean displayEsp { get; set; } = true;
        public Boolean displayAntennaTracker { get; set; } = true;
        public Boolean displaySerialPorts { get; set; } = true;


        //config tuning
        public Boolean displayGeoFence { get; set; } = true;
        public Boolean displayBasicTuning { get; set; } = true;
        public Boolean displayExtendedTuning { get; set; } = true;
        public Boolean displayStandardParams { get; set; } = false;
        public Boolean displayAdvancedParams { get; set; } = false;
        public Boolean displayMavFTP { get; set; } = true;
        public Boolean displayFullParamList { get; set; } = true;
        public Boolean displayFullParamTree { get; set; } = true;
        public Boolean displayParamCommitButton { get; set; } = false;
        public Boolean displayBaudCMB { get; set; } = true;
        public Boolean displaySerialPortCMB { get; set; } = true;
        public Boolean standardFlightModesOnly { get; set; } = false;
        public Boolean autoHideMenuForce { get; set; } = false;
        public Boolean displayInitialParams { get; set; } = true;
        public bool isAdvancedMode { get; set; } = false;
        public Boolean displayREPL { get; set; } = true;
        public bool displayServoOutput { get; set; } = true;
        public bool displayJoystick { get; set; } = true;
        public bool displayOSD { get; set; } = true;
        public bool displayUserParam { get; set; } = true;
        public bool displayPlannerSettings { get; set; } = true;
        public bool displayFFTSetup { get; set; } = true;
        public bool displayPreFlightTabEdit { get; set; } = true;
        public bool displayPlannerLayout { get; set; } = true;

        public bool lockQuickView { get; set; } = false;

        public DisplayView()
        {
            // default to basic.
            //also when a new field is added/created this defines the template for missing options
            displayName = DisplayNames.Basic;

            //MainV2 buttons
            displaySimulation = false;
            displayDonate = true;
            displayHelp = true;

            //flight Data view
            displayAnenometer = true;
            displayQuickTab = true;
            displayPreFlightTab = true;
            displayAdvActionsTab = false;
            displaySimpleActionsTab = true;
            displayGaugesTab = false;
            displayStatusTab = false;
            displayScriptsTab = false;
            displayTelemetryTab = true;
            displayDataflashTab = true;
            displayMessagesTab = true;
            displayTransponderTab = true;
            displayPayloadTab = true;
            displayParamsTab = true;
            displayVideoTab = true;
            displayTuningTab = true;
            displayInspectorTab = true;

            //flight plan
            displayRallyPointsMenu = true;
            displayGeoFenceMenu = true;
            displaySplineCircleAutoWp = true;
            displayTextAutoWp = true;
            displayCircleSurveyAutoWp = true;
            displayPoiMenu = true;
            displayTrackerHomeMenu = true;
            displayCheckHeightBox = true;
            displayPluginAutoWp = true;

            //initial setup
            displayInstallFirmware = true;
            displayInitialParams = true;
            displayWizard = true;
            displayFrameType = true;
            displayAccelCalibration = true;
            displayCompassConfiguration = true;
            displayRadioCalibration = true;
            displayServoOutput = true;
            displayEscCalibration = true;
            displayFlightModes = true;
            displayFailSafe = true;
            displaySikRadio = true;
            displayBattMonitor = true;
            displayCAN = true;
            displayCompassMotorCalib = true;
            displayRangeFinder = true;
            displayAirSpeed = true;
            displayPx4Flow = true;
            displayOpticalFlow = true;
            displayOsd = true;
            displayCameraGimbal = true;
            displayMotorTest = true;
            displayBluetooth = true;
            displayParachute = true;
            displayEsp = true;
            displayAntennaTracker = true;
            displayRTKInject = true;
            displayJoystick = true;
            displaySerialPorts = true;
            displayREPL = true;
            displayTerminal = true;

            //config tuning
            displayGeoFence = true;
            displayBasicTuning = true;
            displayExtendedTuning = true;
            displayStandardParams = false;
            displayAdvancedParams = false;
            displayMavFTP = true;
            displayFullParamList = true;
            displayFullParamTree = true;
            displayParamCommitButton = false;
            displayBaudCMB = true;
            standardFlightModesOnly = false;
            displaySerialPortCMB = true;
            autoHideMenuForce = false;
            displayOSD = true;
            isAdvancedMode = false;
        }
    }
    public static class DisplayViewExtensions
    {
        public static bool TryParse(string value, out DisplayView result)
        {
            result = new DisplayView();
            var serializer = new XmlSerializer(result.GetType());

            using (TextReader reader = new StringReader(value))
            {
                if (!value.StartsWith("{"))
                {
                    try
                    {
                        result = (DisplayView) serializer.Deserialize(reader);
                        return true;
                    }
                    catch (Exception)
                    {
                        return false;
                    }
                }

                try
                {
                    result = value.FromJSON<DisplayView>();
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }
        public static string ConvertToString(this DisplayView v)
        {
            return v.ToJSON();

            XmlSerializer xmlSerializer = new XmlSerializer(v.GetType());
            using (StringWriter textWriter = new StringWriter())
            {
                xmlSerializer.Serialize(textWriter, v);
                return textWriter.ToString();
            }
        }
        public static DisplayView Basic(this DisplayView v)
        {
            return new DisplayView()
            {
                displayName = DisplayNames.Basic,
                //MainV2 buttons
                displaySimulation = true,
                displayDonate = true,
                displayHelp = true,

                //flight Data view
                displayAnenometer = true,
                displayQuickTab = true,
                displayPreFlightTab = true,
                displayAdvActionsTab = false,
                displaySimpleActionsTab = true,
                displayGaugesTab = false,
                displayStatusTab = false,
                displayScriptsTab = false,
                displayTelemetryTab = true,
                displayDataflashTab = true,
                displayMessagesTab = true,
                displayTransponderTab = true,
                displayPayloadTab = true,
                displayParamsTab = true,
                displayVideoTab = true,
                displayTuningTab = true,
                displayInspectorTab = true,

                //flight plan
                displayRallyPointsMenu = true,
                displayGeoFenceMenu = true,
                displaySplineCircleAutoWp = true,
                displayTextAutoWp = true,
                displayCircleSurveyAutoWp = true,
                displayPoiMenu = true,
                displayTrackerHomeMenu = true,
                displayCheckHeightBox = true,
                displayPluginAutoWp = true,

                //initial setup
                displayInstallFirmware = true,
                displayWizard = true,
                displayFrameType = true,
                displayAccelCalibration = true,
                displayCompassConfiguration = true,
                displayRadioCalibration = true,
                displayServoOutput = true,
                displayEscCalibration = true,
                displayFlightModes = true,
                displayFailSafe = true,
                displaySikRadio = true,
                displayBattMonitor = true,
                displayCAN = true,
                displayCompassMotorCalib = true,
                displayRangeFinder = true,
                displayAirSpeed = true,
                displayPx4Flow = true,
                displayOpticalFlow = true,
                displayOsd = true,
                displayCameraGimbal = true,
                displayMotorTest = true,
                displayBluetooth = true,
                displayParachute = true,
                displayEsp = true,
                displayAntennaTracker = true,
                displayRTKInject = true,
                displayJoystick = true,
                displayREPL = true,
                displayTerminal = false,

                //config tuning
                displayGeoFence = true,
                displayBasicTuning = true,
                displayExtendedTuning = true,
                displayStandardParams = false,
                displayAdvancedParams = false,
                displayMavFTP = true,
                displayFullParamList = true,
                displayFullParamTree = true,
                displayParamCommitButton = false,
                displayBaudCMB = true,
                displaySerialPortCMB = true,
                displayOSD = true,
                standardFlightModesOnly = false,
                autoHideMenuForce = false,
                isAdvancedMode = false
            };
        }
        public static DisplayView Advanced(this DisplayView v)
        {
            return new DisplayView()
            {
                displayName = DisplayNames.Advanced,
                //MainV2 buttons
                displaySimulation = true,
                displayDonate = true,
                displayHelp = true,

                //flight Data view
                displayAnenometer = true,
                displayQuickTab = true,
                displayPreFlightTab = true,
                displayAdvActionsTab = true,
                displaySimpleActionsTab = false,
                displayGaugesTab = false,
                displayStatusTab = true,
                displayScriptsTab = true,
                displayTelemetryTab = true,
                displayDataflashTab = true,
                displayMessagesTab = true,
                displayTransponderTab = true,
                displayPayloadTab = true,
                displayParamsTab = true,
                displayVideoTab = true,
                displayTuningTab = true,
                displayInspectorTab = true,

                //flight plan
                displayRallyPointsMenu = true,
                displayGeoFenceMenu = true,
                displaySplineCircleAutoWp = true,
                displayTextAutoWp = true,
                displayCircleSurveyAutoWp = true,
                displayPoiMenu = true,
                displayTrackerHomeMenu = true,
                displayCheckHeightBox = true,
                displayPluginAutoWp = true,

                //initial setup
                displayInstallFirmware = true,
                displayWizard = true,
                displayFrameType = true,
                displayAccelCalibration = true,
                displayCompassConfiguration = true,
                displayRadioCalibration = true,
                displayServoOutput = true,
                displayEscCalibration = true,
                displayFlightModes = true,
                displayFailSafe = true,
                displaySikRadio = true,
                displayBattMonitor = true,
                displayCAN = true,
                displayCompassMotorCalib = true,
                displayRangeFinder = true,
                displayAirSpeed = true,
                displayPx4Flow = true,
                displayOpticalFlow = true,
                displayOsd = true,
                displayCameraGimbal = true,
                displayMotorTest = true,
                displayBluetooth = true,
                displayParachute = true,
                displayEsp = true,
                displayAntennaTracker = true,
                displayRTKInject = true,
                displayJoystick = true,
                displayREPL = true,
                displayTerminal = true,

                //config tuning
                displayGeoFence = true,
                displayBasicTuning = true,
                displayExtendedTuning = true,
                displayStandardParams = false,
                displayAdvancedParams = false,
                displayMavFTP = true,
                displayFullParamList = true,
                displayFullParamTree = true,
                displayParamCommitButton = false,
                displayBaudCMB = true,
                displaySerialPortCMB = true,
                standardFlightModesOnly = false,
                displayOSD = true,
                autoHideMenuForce = false,
                isAdvancedMode = true
            };
        }

        public static string custompath = Settings.GetRunningDirectory() + Path.DirectorySeparatorChar + "custom.displayview";

        public static DisplayView Custom(this DisplayView v)
        {
            var result = new DisplayView().Advanced();

            if (File.Exists(custompath) && TryParse(File.ReadAllText(custompath), out result))
            {
                result.displayName = DisplayNames.Custom;
                return result;
            }

            return result;
        }
    }
}