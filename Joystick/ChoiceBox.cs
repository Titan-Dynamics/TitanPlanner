using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Joystick
{
    /// <summary>
    /// A drop-down choice that draws itself as a flat combo box and makes a real ComboBox only while it is open. The
    /// joystick page has dozens of these, and a ComboBox each (native windows holding every item) took seconds to make.
    /// </summary>
    public class ChoiceBox : Control
    {
        private const int ArrowWidth = 16;

        private object[] items = new object[0];
        private object selectedItem;
        private ComboBox editor;

        /// <summary>Raised when the choice changes, by the user or in code, as a ComboBox's SelectedIndexChanged.</summary>
        public event EventHandler SelectedItemChanged;

        public ChoiceBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                     ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            TabStop = true;
            Size = new Size(70, 21);
        }

        public object[] Items
        {
            get { return items; }
            set
            {
                items = value ?? new object[0];
                Invalidate();
            }
        }

        public object SelectedItem
        {
            get { return selectedItem; }
            set
            {
                if (Equals(selectedItem, value))
                    return;

                selectedItem = value;
                Invalidate();
                SelectedItemChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>The chosen item's text; setting it chooses the item with that text, if there is one.</summary>
        public override string Text
        {
            get { return selectedItem?.ToString() ?? ""; }
            set
            {
                var match = items.FirstOrDefault(item =>
                    string.Equals(item?.ToString(), value, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    SelectedItem = match;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var back = ThemeManager.ControlBGColor;
            var fore = Enabled ? ThemeManager.TextColor : SystemColors.GrayText;
            g.Clear(back);

            TextRenderer.DrawText(g, Text, Font, new Rectangle(3, 0, Width - ArrowWidth - 3, Height), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

            // the drop-down arrow
            var cx = Width - ArrowWidth / 2 - 1;
            var cy = Height / 2;
            using (var brush = new SolidBrush(fore))
                g.FillPolygon(brush, new[] { new Point(cx - 4, cy - 2), new Point(cx + 4, cy - 2), new Point(cx, cy + 2) });

            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(g, new Rectangle(1, 1, Width - ArrowWidth - 1, Height - 2), fore, back);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
                Open();
        }

        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.F4 || e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter ||
                e.Alt && e.KeyCode == Keys.Down)
            {
                Open();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                // the previous or next item, as a closed ComboBox steps
                var index = Array.IndexOf(items, selectedItem) + (e.KeyCode == Keys.Down ? 1 : -1);
                if (index >= 0 && index < items.Length)
                    SelectedItem = items[index];
                e.Handled = true;
            }
        }

        /// <summary>
        /// A ComboBox over this one, dropped down with the items, gone again once it closes.
        /// </summary>
        private void Open()
        {
            if (editor != null || Parent == null || items.Length == 0 || !Enabled)
                return;

            editor = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = ThemeManager.ControlBGColor,
                ForeColor = ThemeManager.TextColor,
                Font = Font,
                Bounds = Bounds,
                MaxDropDownItems = 20
            };
            editor.Items.AddRange(items);
            editor.SelectedItem = selectedItem;

            var opened = editor;
            opened.SelectionChangeCommitted += (s, e) => SelectedItem = opened.SelectedItem;
            opened.DropDownClosed += (s, e) => BeginInvoke((Action)(() => Close(opened)));
            opened.LostFocus += (s, e) => BeginInvoke((Action)(() => Close(opened)));

            Parent.Controls.Add(opened);
            opened.BringToFront();
            opened.Focus();
            opened.DroppedDown = true;
        }

        private void Close(ComboBox opened)
        {
            if (editor != opened)
                return;

            editor = null;
            var hadFocus = opened.Focused;
            opened.Parent?.Controls.Remove(opened);
            opened.Dispose();

            if (hadFocus && !IsDisposed && CanFocus)
                Focus();
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && editor != null)
            {
                var opened = editor;
                editor = null;
                opened.Parent?.Controls.Remove(opened);
                opened.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
