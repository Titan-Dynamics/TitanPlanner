using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.ArduPilot;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public partial class DisplayThisForm : Form
    {
        private readonly Func<string, bool> _isChecked;
        private readonly EventHandler _checkChangedHandler;
        private readonly MouseEventHandler _mouseDownHandler;

        private PlaceholderTextBox _searchBox;
        private DoubleBufferedPanel _contentPanel;
        private List<DisplayItem> _allItems;
        private List<DisplayItem> _filteredItems;
        private Timer _updateTimer;
        private Font _boldFont;
        private Font _normalFont;
        private Font _headerFont;

        private int _rowHeight = 18;
        private int _padding = 10;
        private int _checkboxSize = 14;
        private int _checkboxMargin = 4;
        private int _nameWidth = 140;
        private int _valueWidth = 80;
        private int _columnGap = 20;

        private DisplayItem _hoveredItem = null;

        public DisplayThisForm(
            Func<string, bool> isChecked,
            EventHandler checkChangedHandler,
            MouseEventHandler mouseDownHandler = null)
        {
            _isChecked = isChecked;
            _checkChangedHandler = checkChangedHandler;
            _mouseDownHandler = mouseDownHandler;

            _allItems = new List<DisplayItem>();
            _filteredItems = new List<DisplayItem>();

            InitializeComponent();
            SetupControls();
        }

        private void SetupControls()
        {
            // Search box at top
            _searchBox = new PlaceholderTextBox();
            _searchBox.Dock = DockStyle.Top;
            _searchBox.Font = new Font(this.Font.FontFamily, 10);
            _searchBox.PlaceholderText = "Search";
            _searchBox.PlaceholderColor = Color.Gray;
            _searchBox.TextChanged += SearchBox_TextChanged;
            _searchBox.MinimumSize = new Size(0, 28);
            _searchBox.Height = 28;

            // Content panel
            _contentPanel = new DoubleBufferedPanel();
            _contentPanel.Dock = DockStyle.Fill;
            _contentPanel.AutoScroll = true;
            _contentPanel.Paint += ContentPanel_Paint;
            _contentPanel.MouseClick += ContentPanel_MouseClick;
            _contentPanel.MouseMove += ContentPanel_MouseMove;
            _contentPanel.MouseLeave += ContentPanel_MouseLeave;

            // Add controls - order matters for docking
            this.Controls.Add(_contentPanel);
            this.Controls.Add(_searchBox);

            _updateTimer = new Timer();
            _updateTimer.Interval = 200;
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        private void DisplayThisForm_Load(object sender, EventArgs e)
        {
            // Set form size to 70% of screen
            var screen = Screen.FromControl(this);
            this.Width = (int)(screen.WorkingArea.Width * 0.7);
            this.Height = (int)(screen.WorkingArea.Height * 0.7);
            this.CenterToScreen();

            BuildItemList();
            _updateTimer.Start();

            ThemeManager.ApplyThemeTo(this);
            ApplyTheme();
        }

        private void BuildItemList()
        {
            _allItems.Clear();

            object thisBoxed = MainV2.comPort.MAV.cs;
            Type test = thisBoxed.GetType();

            foreach (var field in test.GetProperties())
            {
                object fieldValue = field.GetValue(thisBoxed, null);
                if (fieldValue == null)
                    continue;

                if (!fieldValue.IsNumber())
                    continue;

                string group = CurrentState.GetGroupText(field.Name);
                if (string.IsNullOrEmpty(group)) group = "Other";

                string displayName = field.Name;
                if (field.Name.Contains("customfield"))
                {
                    if (CurrentState.custom_field_names.ContainsKey(field.Name))
                    {
                        displayName = CurrentState.custom_field_names[field.Name];
                    }
                    else
                    {
                        continue; // Skip unnamed custom fields
                    }
                }

                _allItems.Add(new DisplayItem
                {
                    Name = field.Name,
                    DisplayName = displayName,
                    Group = group,
                    Value = fieldValue?.ToString() ?? "",
                    IsChecked = _isChecked(field.Name)
                });
            }

            // Sort by group, then by display name
            _allItems = _allItems.OrderBy(f => f.Group).ThenBy(f => f.DisplayName).ToList();
            _filteredItems = _allItems;

            // Set up autocomplete
            _searchBox.SetAutoCompleteSource(_allItems.Select(x => x.DisplayName).ToList());

            UpdateScrollSize();
            _contentPanel.Invalidate();
        }

        private void UpdateScrollSize()
        {
            if (_contentPanel == null || _filteredItems == null)
                return;

            if (_filteredItems.Count == 0)
            {
                _contentPanel.AutoScrollMinSize = Size.Empty;
                return;
            }

            int availableHeight = _contentPanel.ClientSize.Height;
            int headerHeight = _rowHeight + 4;

            // Count groups and items
            var groups = _filteredItems.GroupBy(x => x.Group).ToList();
            int totalRows = _filteredItems.Count + groups.Count; // items + group headers

            int rowsPerColumn = Math.Max(1, (availableHeight - _padding * 2) / _rowHeight);
            int columnWidth = _checkboxSize + _checkboxMargin + _nameWidth + _valueWidth + _columnGap;
            int numColumns = (int)Math.Ceiling((double)totalRows / rowsPerColumn);

            int totalWidth = _padding + (numColumns * columnWidth) + _padding;
            _contentPanel.AutoScrollMinSize = new Size(totalWidth, 0);
        }

        private void ContentPanel_Paint(object sender, PaintEventArgs e)
        {
            if (_filteredItems.Count == 0)
                return;

            if (_boldFont == null || _boldFont.FontFamily != this.Font.FontFamily)
            {
                _boldFont = new Font(this.Font.FontFamily, this.Font.Size, FontStyle.Bold);
                _normalFont = new Font(this.Font.FontFamily, this.Font.Size, FontStyle.Regular);
                _headerFont = new Font(this.Font.FontFamily, this.Font.Size + 1, FontStyle.Bold);
            }

            var g = e.Graphics;
            g.TranslateTransform(_contentPanel.AutoScrollPosition.X, _contentPanel.AutoScrollPosition.Y);
            g.Clear(_contentPanel.BackColor);

            int availableHeight = _contentPanel.ClientSize.Height;
            int rowsPerColumn = Math.Max(1, (availableHeight - _padding * 2) / _rowHeight);
            int columnWidth = _checkboxSize + _checkboxMargin + _nameWidth + _valueWidth + _columnGap;

            int x = _padding;
            int y = _padding;
            int rowInColumn = 0;

            using (var headerBrush = new SolidBrush(ThemeManager.BannerColor2))
            using (var nameBrush = new SolidBrush(_contentPanel.ForeColor))
            using (var valueBrush = new SolidBrush(Color.Gray))
            using (var checkPen = new Pen(_contentPanel.ForeColor, 1))
            using (var checkFillBrush = new SolidBrush(Color.Green))
            using (var hoverBrush = new SolidBrush(Color.FromArgb(40, 128, 128, 128)))
            {
                string currentGroup = null;

                foreach (var item in _filteredItems)
                {
                    // Draw group header if group changed
                    if (item.Group != currentGroup)
                    {
                        currentGroup = item.Group;

                        // Check if we need to wrap to next column
                        if (rowInColumn > 0 && rowInColumn >= rowsPerColumn - 1)
                        {
                            x += columnWidth;
                            y = _padding;
                            rowInColumn = 0;
                        }

                        // Draw header
                        g.DrawString(currentGroup, _headerFont, headerBrush, x, y);
                        y += _rowHeight;
                        rowInColumn++;

                        // Check column wrap again after header
                        if (rowInColumn >= rowsPerColumn)
                        {
                            x += columnWidth;
                            y = _padding;
                            rowInColumn = 0;
                        }
                    }

                    // Store item bounds for hit testing
                    item.Bounds = new Rectangle(x, y, columnWidth - _columnGap, _rowHeight);

                    // Draw hover highlight
                    if (item == _hoveredItem)
                    {
                        g.FillRectangle(hoverBrush, item.Bounds);
                    }

                    // Draw checkbox
                    var checkRect = new Rectangle(x, y + (_rowHeight - _checkboxSize) / 2, _checkboxSize, _checkboxSize);
                    g.DrawRectangle(checkPen, checkRect);
                    if (item.IsChecked)
                    {
                        var innerRect = new Rectangle(checkRect.X + 2, checkRect.Y + 2, checkRect.Width - 4, checkRect.Height - 4);
                        g.FillRectangle(checkFillBrush, innerRect);
                    }

                    // Draw name
                    int textX = x + _checkboxSize + _checkboxMargin;
                    g.DrawString(item.DisplayName, _normalFont, nameBrush, textX, y);

                    // Draw value
                    g.DrawString(item.Value, _normalFont, valueBrush, textX + _nameWidth, y);

                    y += _rowHeight;
                    rowInColumn++;

                    // Column wrap
                    if (rowInColumn >= rowsPerColumn)
                    {
                        x += columnWidth;
                        y = _padding;
                        rowInColumn = 0;
                    }
                }
            }
        }

        private void ContentPanel_MouseClick(object sender, MouseEventArgs e)
        {
            var item = GetItemAtPoint(e.Location);
            if (item != null)
            {
                item.IsChecked = !item.IsChecked;

                // Create a fake checkbox to pass to the handler
                var fakeCheckbox = new CheckBox
                {
                    Name = item.Name,
                    Text = item.DisplayName,
                    Checked = item.IsChecked,
                    Tag = "custom"
                };

                if (e.Button == MouseButtons.Right && _mouseDownHandler != null)
                {
                    _mouseDownHandler(fakeCheckbox, e);
                }

                _checkChangedHandler?.Invoke(fakeCheckbox, EventArgs.Empty);
                fakeCheckbox.Dispose();

                _contentPanel.Invalidate();
            }
        }

        private void ContentPanel_MouseMove(object sender, MouseEventArgs e)
        {
            var item = GetItemAtPoint(e.Location);
            if (item != _hoveredItem)
            {
                _hoveredItem = item;
                _contentPanel.Cursor = item != null ? Cursors.Hand : Cursors.Default;
                _contentPanel.Invalidate();
            }
        }

        private void ContentPanel_MouseLeave(object sender, EventArgs e)
        {
            if (_hoveredItem != null)
            {
                _hoveredItem = null;
                _contentPanel.Invalidate();
            }
        }

        private DisplayItem GetItemAtPoint(Point pt)
        {
            // Adjust for scroll position
            pt.X -= _contentPanel.AutoScrollPosition.X;
            pt.Y -= _contentPanel.AutoScrollPosition.Y;

            foreach (var item in _filteredItems)
            {
                if (item.Bounds.Contains(pt))
                    return item;
            }
            return null;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (!this.Visible)
                return;

            try
            {
                var cs = MainV2.comPort.MAV.cs;
                bool changed = false;

                foreach (var item in _allItems)
                {
                    var prop = typeof(CurrentState).GetProperty(item.Name);
                    if (prop != null)
                    {
                        var val = prop.GetValue(cs)?.ToString() ?? "";
                        if (item.Value != val)
                        {
                            item.Value = val;
                            changed = true;
                        }
                    }
                }

                if (changed)
                {
                    _contentPanel.Invalidate();
                }
            }
            catch
            {
                // Ignore errors during update
            }
        }

        private void SearchBox_TextChanged(object sender, EventArgs e)
        {
            var searchText = _searchBox.Text.Trim().ToLower();

            if (string.IsNullOrEmpty(searchText))
            {
                _filteredItems = _allItems;
            }
            else
            {
                _filteredItems = _allItems.Where(x =>
                    x.DisplayName.ToLower().Contains(searchText) ||
                    x.Name.ToLower().Contains(searchText) ||
                    x.Group.ToLower().Contains(searchText)).ToList();
            }

            UpdateScrollSize();
            _contentPanel.Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollSize();
            _contentPanel?.Invalidate();
        }

        private void ApplyTheme()
        {
            this.BackColor = ThemeManager.BGColor;
            _searchBox?.ApplyTheme();
            if (_contentPanel != null)
            {
                _contentPanel.BackColor = ThemeManager.BGColor;
                _contentPanel.ForeColor = ThemeManager.TextColor;
                _contentPanel.Invalidate();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _updateTimer?.Stop();
            _updateTimer?.Dispose();
            _boldFont?.Dispose();
            _normalFont?.Dispose();
            _headerFont?.Dispose();
            base.OnFormClosing(e);
        }

        private class DisplayItem
        {
            public string Name { get; set; }
            public string DisplayName { get; set; }
            public string Group { get; set; }
            public string Value { get; set; }
            public bool IsChecked { get; set; }
            public Rectangle Bounds { get; set; }
        }

        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint |
                              ControlStyles.UserPaint |
                              ControlStyles.OptimizedDoubleBuffer, true);
                this.UpdateStyles();
            }
        }
    }
}
