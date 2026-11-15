using MissionPlanner.Utilities;
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// A ComboBox in a flat frame drawn in the theme: the field, a 1 px border (the accent while the box has the focus)
    /// and a chevron that opens its list. The box is still the box, only clipped to its text: it sits in a panel the
    /// size of its text area, so none of Windows' own border or button can show, or flash under the pointer. While it
    /// is disabled its text shows dimmed on the field, not on Windows' grey.
    /// </summary>
    public class FlatComboFrame : Control
    {
        private const int Pad = 4;
        private const int ChevronWidth = 18;

        private readonly ComboBox combo;
        private readonly Panel clip;
        private readonly Label disabledText;
        private bool arranging;
        private bool again;
        private bool hot;

        public FlatComboFrame(ComboBox combo)
        {
            this.combo = combo;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
            Cursor = Cursors.Default;

            clip = new Panel {Margin = new Padding(0)};
            disabledText = new Label
            {
                Dock = DockStyle.Fill,
                AutoSize = false,
                AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(1, 0, 0, 0),
                Visible = false
            };

            Controls.Add(clip);
            combo.Parent = clip;
            clip.Controls.Add(disabledText);
            disabledText.BringToFront();

            // until whoever holds the frame sizes it
            Size = new Size(combo.Width, combo.Height);

            combo.Enter += (s, e) => Invalidate();
            combo.Leave += (s, e) => Invalidate();
            combo.HandleCreated += (s, e) => ArrangeBox();
            combo.SizeChanged += (s, e) => ArrangeBox();
            combo.LocationChanged += (s, e) => ArrangeBox();
            combo.EnabledChanged += (s, e) => ShowState();
            combo.TextChanged += (s, e) => ShowState();
            combo.BackColorChanged += (s, e) => ShowState();
            combo.ForeColorChanged += (s, e) => ShowState();
            ShowState();
        }

        /// <summary>The box this frames.</summary>
        public ComboBox Box => combo;

        private static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

        private Color Field => combo.Enabled ? combo.BackColor : Blend(combo.BackColor, Color.Black, 0.25);

        private Color TextColour => combo.Enabled ? combo.ForeColor : Blend(Field, combo.ForeColor, 0.5);

        /// <summary>The box's text area, where Windows puts its edit; an estimate until its window is made.</summary>
        private Rectangle TextArea()
        {
            if (combo.IsHandleCreated && Type.GetType("Mono.Runtime") == null)
            {
                var info = new COMBOBOXINFO {cbSize = Marshal.SizeOf(typeof(COMBOBOXINFO))};
                try
                {
                    if (GetComboBoxInfo(combo.Handle, ref info) && info.rcItem.Right > info.rcItem.Left)
                        return Rectangle.FromLTRB(info.rcItem.Left, info.rcItem.Top, info.rcItem.Right,
                            info.rcItem.Bottom);
                }
                catch (Exception)
                {
                    // not Windows: the estimate
                }
            }

            int button = SystemInformation.VerticalScrollBarWidth;
            return new Rectangle(3, 3, Math.Max(1, combo.Width - 6 - button), Math.Max(1, combo.Height - 6));
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            ArrangeBox();
        }

        private void ArrangeBox()
        {
            if (arranging)
            {
                // the box moved or resized while being placed: place it again once this is done
                again = true;
                return;
            }

            arranging = true;
            try
            {
                // twice at most: the second pass sees the box as the first left it
                for (int pass = 0; pass < 2; pass++)
                {
                    again = false;
                    PlaceBox();
                    if (!again)
                        break;
                }
            }
            finally
            {
                arranging = false;
            }
        }

        /// <summary>
        /// The clip is the frame's inside less the chevron, as tall as the box's text (no taller than the frame's
        /// inside); the box is made wider by what isn't text and placed so its text lands on the clip. Right of the
        /// text it keeps at least a drop-down button and an edge, whatever the box reports, so neither Windows' button
        /// nor the one WinForms paints for a flat box can reach into the clip.
        /// </summary>
        private void PlaceBox()
        {
            var text = TextArea();
            int width = Math.Max(10, Width - Pad - ChevronWidth);
            int height = Math.Max(1, Math.Min(text.Height, Height - 2));
            clip.SetBounds(Pad, Math.Max(1, (Height - height) / 2), width, height);
            int right = Math.Max(combo.Width - text.Right, SystemInformation.HorizontalScrollBarArrowWidth + 4);
            combo.SetBounds(-text.Left, -text.Top - (text.Height - height) / 2, text.Left + width + right, 0,
                BoundsSpecified.Location | BoundsSpecified.Width);
        }

        /// <summary>A disabled box shows its text on the field, over Windows' grey edit.</summary>
        private void ShowState()
        {
            disabledText.Visible = !combo.Enabled;
            disabledText.Text = combo.Text;
            disabledText.Font = combo.Font;
            disabledText.BackColor = Field;
            disabledText.ForeColor = TextColour;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hot = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hot = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // the frame round the text, the chevron most of all, opens the list as the box's button did
            if (e.Button != MouseButtons.Left || !combo.Enabled)
                return;
            combo.Focus();
            combo.DroppedDown = !combo.DroppedDown;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var field = new SolidBrush(Field))
                g.FillRectangle(field, ClientRectangle);

            // the chevron
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width - ChevronWidth / 2f - 2, cy = Height / 2f;
            using (var pen = new Pen(TextColour, 1.6f)
                       {StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round})
                g.DrawLines(pen, new[]
                {
                    new PointF(cx - 4, cy - 2), new PointF(cx, cy + 2), new PointF(cx + 4, cy - 2)
                });
            g.SmoothingMode = SmoothingMode.Default;

            // the border: the accent while the box has the focus, a little brighter under the pointer
            var border = combo.Enabled && combo.ContainsFocus
                ? ThemeManager.ButBG
                : Blend(Field, combo.ForeColor, combo.Enabled && hot ? 0.4 : 0.22);
            using (var pen = new Pen(border))
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct COMBOBOXINFO
        {
            public int cbSize;
            public RECT rcItem;
            public RECT rcButton;
            public int stateButton;
            public IntPtr hwndCombo;
            public IntPtr hwndItem;
            public IntPtr hwndList;
        }

        [DllImport("user32.dll")]
        private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO pcbi);
    }
}
