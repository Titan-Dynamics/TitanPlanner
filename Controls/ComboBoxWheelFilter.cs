#if !LIB
using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Keeps the mouse wheel from changing a closed dropdown, which Windows does by default: scrolling a page past one
    /// would change it, and some write a parameter when they change. The wheel scrolls what the dropdown is on
    /// instead. An open dropdown's list still scrolls.
    /// </summary>
    public class ComboBoxWheelFilter : IMessageFilter
    {
        private const int WM_MOUSEWHEEL = 0x020A;

        private readonly bool _passToParent;

        /// <param name="passToParent">Hands the wheel to the dropdown's parent, which needs Windows' SendMessage; without
        /// it the wheel does nothing over a dropdown.</param>
        public ComboBoxWheelFilter(bool passToParent)
        {
            _passToParent = passToParent;
        }

        public bool PreFilterMessage(ref Message m)
        {
            if (m.Msg != WM_MOUSEWHEEL)
                return false;

            // the dropdown, or the text box inside an editable one
            if (!(Control.FromChildHandle(m.HWnd) is ComboBox combo) || combo.DroppedDown)
                return false;

            // A parent that doesn't scroll passes the wheel on up to its own, as far as one that does
            if (_passToParent && combo.Parent != null && combo.Parent.IsHandleCreated)
                SendMessage(combo.Parent.Handle, m.Msg, m.WParam, m.LParam);

            return true;
        }

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
    }
}
#endif
