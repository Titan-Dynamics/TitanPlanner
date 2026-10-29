using System.Drawing;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Colours the 3D map's own UI takes from the app theme.
    /// </summary>
    public static class ThemeColors
    {
        /// <summary>The theme's button colour, as used by the Settings dialog's buttons.</summary>
        public static Color Accent
        {
            get { return MapButton.Accent; }
        }
    }
}
