namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// EdgeTX Buddy, the EdgeTX radio flasher. It talks to the radio, not the autopilot, so there is nothing to disconnect
    /// first.
    /// </summary>
    public class ConfigEdgeTXBuddy : ConfigWebPage
    {
        protected override string Url => "https://buddy.edgetx.org/#/flash";
    }
}
