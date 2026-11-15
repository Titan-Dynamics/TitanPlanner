namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// The TitanLRS web flasher. It talks to the radio, not the autopilot, so there is nothing to disconnect first.
    /// </summary>
    public class ConfigTitanLRS : ConfigWebPage
    {
        protected override string Url => "https://titan-dynamics.github.io/TitanLRS-Web-Flasher/";
    }
}
