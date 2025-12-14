using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// A TextBox wrapper that supports placeholder text and vertical centering.
    /// Uses a Panel container with a borderless TextBox inside.
    /// </summary>
    public class PlaceholderTextBox : Panel
    {
        private TextBox _textBox;
        private string _placeholderText = "";
        private Color _placeholderColor = Color.Gray;
        private Color _borderColor = Color.Gray;
        private bool _showingPlaceholder = true;

        [Category("Appearance")]
        [Description("The placeholder text to display when the textbox is empty.")]
        public string PlaceholderText
        {
            get { return _placeholderText; }
            set
            {
                _placeholderText = value;
                Invalidate();
            }
        }

        [Category("Appearance")]
        [Description("The color of the placeholder text.")]
        public Color PlaceholderColor
        {
            get { return _placeholderColor; }
            set
            {
                _placeholderColor = value;
                Invalidate();
            }
        }

        [Browsable(true)]
        [DesignerSerializationVisibility(DesignerSerializationVisibility.Visible)]
        public override string Text
        {
            get { return _textBox?.Text ?? ""; }
            set
            {
                if (_textBox != null)
                    _textBox.Text = value;
            }
        }

        public override Font Font
        {
            get { return base.Font; }
            set
            {
                base.Font = value;
                if (_textBox != null)
                {
                    _textBox.Font = value;
                    CenterTextBox();
                }
            }
        }

        public new event EventHandler TextChanged;

        public PlaceholderTextBox()
        {
            // Set up the panel
            this.Height = 28;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);

            // Create inner textbox
            _textBox = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Multiline = false,
                Visible = false  // Start hidden, show when focused or has text
            };

            _textBox.TextChanged += (s, e) =>
            {
                UpdatePlaceholderVisibility();
                TextChanged?.Invoke(this, e);
            };

            _textBox.GotFocus += (s, e) =>
            {
                _textBox.Visible = true;
                _showingPlaceholder = false;
                Invalidate();
            };

            _textBox.LostFocus += (s, e) =>
            {
                UpdatePlaceholderVisibility();
                Invalidate();
            };

            _textBox.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };

            this.Controls.Add(_textBox);

            // Apply theme
            ApplyTheme();
        }

        private void UpdatePlaceholderVisibility()
        {
            bool hasText = !string.IsNullOrEmpty(_textBox.Text);
            bool hasFocus = _textBox.Focused;

            _showingPlaceholder = !hasText && !hasFocus;
            _textBox.Visible = hasText || hasFocus;
            Invalidate();
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            CenterTextBox();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            CenterTextBox();
        }

        private void CenterTextBox()
        {
            if (_textBox == null)
                return;

            int textHeight = _textBox.PreferredHeight;
            int y = (this.ClientSize.Height - textHeight) / 2;
            _textBox.SetBounds(4, y, this.ClientSize.Width - 8, textHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;

            // Draw border
            using (var pen = new Pen(_borderColor, 1))
            {
                g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }

            // Draw placeholder if showing
            if (_showingPlaceholder && !string.IsNullOrEmpty(_placeholderText))
            {
                TextFormatFlags flags = TextFormatFlags.VerticalCenter |
                                        TextFormatFlags.Left |
                                        TextFormatFlags.EndEllipsis |
                                        TextFormatFlags.NoPadding;

                Rectangle rect = new Rectangle(4, 0, Width - 8, Height);
                TextRenderer.DrawText(g, _placeholderText, Font, rect, _placeholderColor, BackColor, flags);
            }
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            _textBox.Visible = true;
            _textBox.Focus();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            _textBox.Visible = true;
            _textBox.Focus();
        }

        /// <summary>
        /// Sets up autocomplete with a custom list of suggestions.
        /// </summary>
        public void SetAutoCompleteSource(IEnumerable<string> items)
        {
            var collection = new AutoCompleteStringCollection();
            collection.AddRange(items.ToArray());

            _textBox.AutoCompleteMode = AutoCompleteMode.Suggest;
            _textBox.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _textBox.AutoCompleteCustomSource = collection;
        }

        /// <summary>
        /// Disables autocomplete
        /// </summary>
        public void DisableAutoComplete()
        {
            _textBox.AutoCompleteMode = AutoCompleteMode.None;
            _textBox.AutoCompleteSource = AutoCompleteSource.None;
        }

        /// <summary>
        /// Applies theme colors
        /// </summary>
        public void ApplyTheme()
        {
            this.BackColor = ThemeManager.ControlBGColor;
            _textBox.BackColor = ThemeManager.ControlBGColor;
            _textBox.ForeColor = ThemeManager.TextColor;
            _placeholderColor = Color.FromArgb(128, ThemeManager.TextColor);
            _borderColor = Color.FromArgb(80, ThemeManager.TextColor);
            Invalidate();
        }

        public new void Focus()
        {
            _textBox.Visible = true;
            _textBox.Focus();
        }

        public new bool Focused => _textBox?.Focused ?? false;

        public int SelectionStart
        {
            get => _textBox?.SelectionStart ?? 0;
            set { if (_textBox != null) _textBox.SelectionStart = value; }
        }

        public int SelectionLength
        {
            get => _textBox?.SelectionLength ?? 0;
            set { if (_textBox != null) _textBox.SelectionLength = value; }
        }
    }
}
