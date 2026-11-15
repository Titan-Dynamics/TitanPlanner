using MissionPlanner.Utilities;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// MenuStrip for the main menu bar, whose items are buttons rather than drop down menus.
    /// </summary>
    public class MainMenuStrip : MenuStrip
    {
        public MainMenuStrip()
        {
            // the theme's accent under the pointer, in place of Windows' blue
            Renderer = new MainMenuRenderer();
        }

        protected override void OnMouseDown(MouseEventArgs mea)
        {
            // Mono's ToolStrip.OnMouseDown releases the mouse capture of a MenuStrip on every press.
            // Under XWayland a touchscreen tap is an emulated pointer, and ungrabbing in the middle of
            // it ends the emulation, so the release never arrives and the button never clicks.
            // A plain button needs no menu tracking, so keep the capture and let OnMouseUp click it.
            if (Program.MONO && RuntimeInformation.IsOSPlatform(OSPlatform.Linux) && mea.Button == MouseButtons.Left)
            {
                var item = GetItemAt(mea.Location);
                if (item != null && !(item is ToolStripDropDownItem) && !(item is ToolStripControlHost))
                    return;
            }

            base.OnMouseDown(mea);
        }
    }

    /// <summary>
    /// The main menu as Windows draws it, but for the button under the pointer: the theme's accent (the buttons'
    /// colour) at half strength over the button's own background, where Windows puts a light blue fill and a blue
    /// border. The screens (FLY, PLAN, SETUP, CONFIG, SITL) and Help and Connect are buttons; a menu has menu items.
    /// </summary>
    internal class MainMenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
        {
            if (!Highlight(e))
                base.OnRenderButtonBackground(e);
        }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (!Highlight(e))
                base.OnRenderMenuItemBackground(e);
        }

        /// <summary>Paints an item on the bar that is under the pointer or pressed; false for any other.</summary>
        private static bool Highlight(ToolStripItemRenderEventArgs e)
        {
            var item = e.Item;
            if (item.IsOnDropDown || !(item.Selected || item.Pressed))
                return false;

            var g = e.Graphics;
            var bounds = new Rectangle(Point.Empty, item.Size);

            // what the item shows without the pointer: its picture, or its colour (the screen showing has one)
            if (item.BackgroundImage != null)
            {
                // tiled, as an item's picture is unless it says otherwise
                if (item.BackgroundImageLayout == ImageLayout.Tile)
                    using (var texture = new TextureBrush(item.BackgroundImage,
                               System.Drawing.Drawing2D.WrapMode.Tile))
                        g.FillRectangle(texture, bounds);
                else
                    g.DrawImage(item.BackgroundImage, bounds);
            }
            else if (item.BackColor.A > 0)
                using (var back = new SolidBrush(item.BackColor))
                    g.FillRectangle(back, bounds);

            // a touch stronger while pressed
            using (var accent = new SolidBrush(Color.FromArgb(item.Pressed ? 170 : 128, ThemeManager.ButBG)))
                g.FillRectangle(accent, bounds);
            return true;
        }
    }
}
