namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// The web page of an ExpressLRS or TitanLRS radio's backpack, at 10.0.0.1 when this computer is on the radio's
    /// telemetry WiFi.
    /// </summary>
    public class ConfigLRSBackpack : ConfigWebPage
    {
        protected override string Url => "http://10.0.0.1/";

        protected override string UnreachableMessage =>
            "This page is only useful when connected to MAVLink telemetry through an ExpressLRS or TitanLRS radio's " +
            "backpack WiFi.";
    }
}
