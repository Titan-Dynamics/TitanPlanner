using MissionPlanner.Controls;
using System.Drawing;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// The Web Tools section's own page, saying what its pages are: websites (each a <see cref="ConfigWebPage"/>) for
    /// the hardware around the autopilot.
    /// </summary>
    public class ConfigWebTools : MyUserControl, IActivate
    {
        public ConfigWebTools()
        {
            SuspendLayout();
            Controls.Add(new Label
            {
                Name = "label1",
                Location = new Point(25, 25),
                Size = new Size(246, 78),
                Text = "The following pages are websites for setting up ESCs, radios and radio backpacks."
            });
            Name = "ConfigWebTools";
            Size = new Size(350, 225);
            ResumeLayout(false);
        }

        public void Activate()
        {
        }
    }
}
