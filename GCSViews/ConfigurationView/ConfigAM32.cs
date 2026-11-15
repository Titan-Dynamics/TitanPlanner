namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>The AM32 ESC configurator.</summary>
    public class ConfigAM32 : ConfigWebPage
    {
        protected override string Url => "https://am32.ca/configurator";

        protected override void BeforeFirstLoad()
        {
            Common.MessageShowAgain("AM32 Configurator",
                "Make sure to disconnect from Mission Planner before using AM32 configurator.");
        }
    }
}
