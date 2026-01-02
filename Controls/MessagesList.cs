using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using MissionPlanner.Utilities;
using static MAVLink;

namespace MissionPlanner.Controls
{
    public class MessagesList : UserControl
    {
        private Panel containerPanel;
        private VScrollBar scrollBar;
        private ContextMenuStrip contextMenu;
        private List<(DateTime time, string message, byte severity)> messages = new List<(DateTime, string, byte)>();
        private int itemHeight = 26;
        private int lastMessageCount = 0;
        private bool autoScroll = true;
        private Font displayFont;
        private int selectedIndex = -1;
        private int hoverIndex = -1;

        public MessagesList()
        {
            InitializeComponent();
            displayFont = new Font("Segoe UI", 9.5f, FontStyle.Regular);
        }

        private void InitializeComponent()
        {
            this.containerPanel = new DoubleBufferedPanel();
            this.scrollBar = new VScrollBar();
            this.contextMenu = new ContextMenuStrip();
            this.SuspendLayout();

            // Context menu for copy
            var copyItem = new ToolStripMenuItem("Copy Message");
            copyItem.Click += CopyItem_Click;
            var copyAllItem = new ToolStripMenuItem("Copy All Messages");
            copyAllItem.Click += CopyAllItem_Click;
            this.contextMenu.Items.Add(copyItem);
            this.contextMenu.Items.Add(copyAllItem);

            // scrollBar - add first so it docks on right
            this.scrollBar.Dock = DockStyle.Right;
            this.scrollBar.Name = "scrollBar";
            this.scrollBar.Scroll += ScrollBar_Scroll;

            // containerPanel
            this.containerPanel.Dock = DockStyle.Fill;
            this.containerPanel.Name = "containerPanel";
            this.containerPanel.Paint += ContainerPanel_Paint;
            this.containerPanel.MouseWheel += ContainerPanel_MouseWheel;
            this.containerPanel.MouseDown += ContainerPanel_MouseDown;
            this.containerPanel.MouseEnter += ContainerPanel_MouseEnter;
            this.containerPanel.MouseMove += ContainerPanel_MouseMove;
            this.containerPanel.MouseLeave += ContainerPanel_MouseLeave;
            this.containerPanel.ContextMenuStrip = this.contextMenu;

            // MessagesList - add scrollbar first, then panel
            this.Controls.Add(this.scrollBar);
            this.Controls.Add(this.containerPanel);
            this.Name = "MessagesList";
            this.Size = new Size(400, 300);
            this.ResumeLayout(false);

            ApplyTheme();
        }

        public void ApplyTheme()
        {
            try
            {
                this.BackColor = ThemeManager.BGColor;
                this.containerPanel.BackColor = ThemeManager.BGColor;
                this.scrollBar.BackColor = ThemeManager.ControlBGColor;
            }
            catch
            {
                // Fallback colors if theme not initialized
                this.BackColor = Color.FromArgb(30, 30, 30);
                this.containerPanel.BackColor = Color.FromArgb(30, 30, 30);
            }
            containerPanel.Invalidate();
        }

        private void ContainerPanel_MouseEnter(object sender, EventArgs e)
        {
            // Focus the panel to receive mouse wheel events
            if (!containerPanel.Focused)
                containerPanel.Focus();
        }

        private void ContainerPanel_MouseLeave(object sender, EventArgs e)
        {
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                containerPanel.Invalidate();
            }
        }

        private void ContainerPanel_MouseMove(object sender, MouseEventArgs e)
        {
            int scrollOffset = scrollBar.Enabled ? scrollBar.Value : 0;
            int newHoverIndex = scrollOffset + (e.Y / itemHeight);

            if (newHoverIndex >= messages.Count)
                newHoverIndex = -1;

            if (newHoverIndex != hoverIndex)
            {
                hoverIndex = newHoverIndex;
                containerPanel.Invalidate();
            }
        }

        private void ContainerPanel_MouseDown(object sender, MouseEventArgs e)
        {
            containerPanel.Focus();

            int scrollOffset = scrollBar.Enabled ? scrollBar.Value : 0;
            int clickedIndex = scrollOffset + (e.Y / itemHeight);

            if (clickedIndex < messages.Count)
            {
                selectedIndex = clickedIndex;
                containerPanel.Invalidate();
            }
        }

        private void CopyItem_Click(object sender, EventArgs e)
        {
            if (selectedIndex >= 0 && selectedIndex < messages.Count)
            {
                var msg = messages[selectedIndex];
                string text = $"[{msg.time:HH:mm:ss}] [{GetSeverityText(msg.severity)}] {msg.message}";
                try
                {
                    Clipboard.SetText(text);
                }
                catch { }
            }
        }

        private void CopyAllItem_Click(object sender, EventArgs e)
        {
            if (messages.Count == 0)
                return;

            var sb = new System.Text.StringBuilder();
            foreach (var msg in messages)
            {
                sb.AppendLine($"[{msg.time:HH:mm:ss}] [{GetSeverityText(msg.severity)}] {msg.message}");
            }
            try
            {
                Clipboard.SetText(sb.ToString());
            }
            catch { }
        }

        private void ContainerPanel_MouseWheel(object sender, MouseEventArgs e)
        {
            if (!scrollBar.Enabled)
                return;

            int delta = e.Delta > 0 ? -3 : 3;
            int newValue = scrollBar.Value + delta;
            newValue = Math.Max(scrollBar.Minimum, Math.Min(scrollBar.Maximum - scrollBar.LargeChange + 1, newValue));

            if (newValue != scrollBar.Value)
            {
                scrollBar.Value = newValue;
                autoScroll = (scrollBar.Value >= scrollBar.Maximum - scrollBar.LargeChange);
                containerPanel.Invalidate();
            }
        }

        private void ScrollBar_Scroll(object sender, ScrollEventArgs e)
        {
            autoScroll = (scrollBar.Value >= scrollBar.Maximum - scrollBar.LargeChange);
            containerPanel.Invalidate();
        }

        public void UpdateMessages(List<(DateTime time, string message, byte severity)> newMessages)
        {
            if (newMessages == null)
                return;

            if (newMessages.Count == lastMessageCount)
                return;

            messages = new List<(DateTime, string, byte)>(newMessages);
            lastMessageCount = messages.Count;

            UpdateScrollBar();

            if (autoScroll && messages.Count > 0)
            {
                int maxScroll = Math.Max(0, scrollBar.Maximum - scrollBar.LargeChange + 1);
                if (scrollBar.Value != maxScroll)
                    scrollBar.Value = maxScroll;
            }

            containerPanel.Invalidate();
        }

        private void UpdateScrollBar()
        {
            int visibleItems = Math.Max(1, containerPanel.Height / itemHeight);

            if (messages.Count <= visibleItems)
            {
                scrollBar.Enabled = false;
                scrollBar.Value = 0;
                scrollBar.Maximum = 0;
            }
            else
            {
                scrollBar.Enabled = true;
                scrollBar.Minimum = 0;
                scrollBar.Maximum = messages.Count - 1;
                scrollBar.LargeChange = Math.Max(1, visibleItems);
                scrollBar.SmallChange = 1;
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateScrollBar();
            containerPanel.Invalidate();
        }

        private void ContainerPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(containerPanel.BackColor);

            if (messages.Count == 0)
            {
                // Draw placeholder text using theme color
                Color placeholderColor;
                try { placeholderColor = ThemeManager.TextColor; }
                catch { placeholderColor = Color.Gray; }

                using (var brush = new SolidBrush(Color.FromArgb(128, placeholderColor)))
                using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                {
                    g.DrawString("No messages", displayFont, brush, containerPanel.ClientRectangle, sf);
                }
                return;
            }

            int visibleItems = (containerPanel.Height / itemHeight) + 2;
            int scrollOffset = scrollBar.Enabled ? scrollBar.Value : 0;

            // Draw messages from oldest (top) to newest (bottom)
            int y = 0;
            for (int i = 0; i < visibleItems && scrollOffset + i < messages.Count; i++)
            {
                int msgIndex = scrollOffset + i;
                if (msgIndex >= messages.Count)
                    break;

                var msg = messages[msgIndex];
                bool isSelected = (msgIndex == selectedIndex);
                bool isHovered = (msgIndex == hoverIndex);
                DrawMessageRow(g, msg, y, msgIndex, isSelected, isHovered);
                y += itemHeight;

                if (y > containerPanel.Height)
                    break;
            }
        }

        private void DrawMessageRow(Graphics g, (DateTime time, string message, byte severity) msg, int y, int index, bool isSelected, bool isHovered)
        {
            Color bgColor = GetSeverityBackgroundColor(msg.severity);
            Color textColor = GetSeverityTextColor(msg.severity);
            string severityText = GetSeverityText(msg.severity);

            // Get theme colors for separator and timestamp
            Color separatorColor, timestampColor;
            try
            {
                separatorColor = ThemeManager.ControlBGColor;
                timestampColor = ThemeManager.TextColor;
            }
            catch
            {
                separatorColor = Color.FromArgb(50, 50, 50);
                timestampColor = Color.FromArgb(160, 160, 160);
            }

            // Adjust background for selection/hover
            if (isSelected)
            {
                try { bgColor = Color.FromArgb(80, ThemeManager.BannerColor2); }
                catch { bgColor = Color.FromArgb(60, 80, 100); }
            }
            else if (isHovered)
            {
                bgColor = Color.FromArgb(
                    Math.Min(255, bgColor.R + 15),
                    Math.Min(255, bgColor.G + 15),
                    Math.Min(255, bgColor.B + 15));
            }

            // Draw background
            using (var brush = new SolidBrush(bgColor))
            {
                g.FillRectangle(brush, 0, y, containerPanel.Width, itemHeight - 1);
            }

            // Draw separator line
            using (var pen = new Pen(Color.FromArgb(60, separatorColor)))
            {
                g.DrawLine(pen, 0, y + itemHeight - 1, containerPanel.Width, y + itemHeight - 1);
            }

            int padding = 4;
            int textY = y + (itemHeight - displayFont.Height) / 2;

            // Draw timestamp
            string timeStr = msg.time.ToString("HH:mm:ss");
            using (var brush = new SolidBrush(Color.FromArgb(160, timestampColor)))
            {
                g.DrawString(timeStr, displayFont, brush, padding, textY);
            }

            // Draw severity badge
            int severityX = 70;
            Color badgeColor = GetSeverityBadgeColor(msg.severity);
            using (var brush = new SolidBrush(badgeColor))
            {
                var badgeRect = new Rectangle(severityX, y + 4, 50, itemHeight - 8);
                g.FillRectangle(brush, badgeRect);
            }
            using (var brush = new SolidBrush(Color.White))
            using (var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
            {
                var badgeRect = new RectangleF(severityX, y, 50, itemHeight);
                using (var smallFont = new Font(displayFont.FontFamily, displayFont.Size * 0.8f, FontStyle.Bold))
                {
                    g.DrawString(severityText, smallFont, brush, badgeRect, sf);
                }
            }

            // Draw message
            int messageX = 130;
            using (var brush = new SolidBrush(textColor))
            {
                var messageRect = new RectangleF(messageX, textY, containerPanel.Width - messageX - padding, displayFont.Height);
                using (var sf = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap })
                {
                    g.DrawString(msg.message, displayFont, brush, messageRect, sf);
                }
            }
        }

        private Color GetSeverityBackgroundColor(byte severity)
        {
            switch ((MAV_SEVERITY)severity)
            {
                case MAV_SEVERITY.EMERGENCY:
                case MAV_SEVERITY.ALERT:
                case MAV_SEVERITY.CRITICAL:
                    return Color.FromArgb(60, 20, 20);
                case MAV_SEVERITY.ERROR:
                    return Color.FromArgb(50, 25, 25);
                case MAV_SEVERITY.WARNING:
                    return Color.FromArgb(50, 45, 20);
                case MAV_SEVERITY.NOTICE:
                    return Color.FromArgb(30, 40, 50);
                case MAV_SEVERITY.INFO:
                    return Color.FromArgb(35, 35, 35);
                case MAV_SEVERITY.DEBUG:
                    return Color.FromArgb(30, 30, 40);
                default:
                    return Color.FromArgb(35, 35, 35);
            }
        }

        private Color GetSeverityTextColor(byte severity)
        {
            switch ((MAV_SEVERITY)severity)
            {
                case MAV_SEVERITY.EMERGENCY:
                case MAV_SEVERITY.ALERT:
                case MAV_SEVERITY.CRITICAL:
                    return Color.FromArgb(255, 120, 120);
                case MAV_SEVERITY.ERROR:
                    return Color.FromArgb(255, 150, 150);
                case MAV_SEVERITY.WARNING:
                    return Color.FromArgb(255, 230, 130);
                case MAV_SEVERITY.NOTICE:
                    return Color.FromArgb(130, 200, 255);
                case MAV_SEVERITY.INFO:
                    return Color.FromArgb(230, 230, 230);
                case MAV_SEVERITY.DEBUG:
                    return Color.FromArgb(170, 170, 200);
                default:
                    return Color.FromArgb(230, 230, 230);
            }
        }

        private Color GetSeverityBadgeColor(byte severity)
        {
            switch ((MAV_SEVERITY)severity)
            {
                case MAV_SEVERITY.EMERGENCY:
                    return Color.FromArgb(180, 0, 0);
                case MAV_SEVERITY.ALERT:
                    return Color.FromArgb(200, 50, 0);
                case MAV_SEVERITY.CRITICAL:
                    return Color.FromArgb(180, 30, 30);
                case MAV_SEVERITY.ERROR:
                    return Color.FromArgb(160, 50, 50);
                case MAV_SEVERITY.WARNING:
                    return Color.FromArgb(180, 140, 0);
                case MAV_SEVERITY.NOTICE:
                    return Color.FromArgb(0, 100, 150);
                case MAV_SEVERITY.INFO:
                    return Color.FromArgb(70, 70, 70);
                case MAV_SEVERITY.DEBUG:
                    return Color.FromArgb(80, 80, 100);
                default:
                    return Color.FromArgb(70, 70, 70);
            }
        }

        private string GetSeverityText(byte severity)
        {
            switch ((MAV_SEVERITY)severity)
            {
                case MAV_SEVERITY.EMERGENCY:
                    return "EMERG";
                case MAV_SEVERITY.ALERT:
                    return "ALERT";
                case MAV_SEVERITY.CRITICAL:
                    return "CRIT";
                case MAV_SEVERITY.ERROR:
                    return "ERROR";
                case MAV_SEVERITY.WARNING:
                    return "WARN";
                case MAV_SEVERITY.NOTICE:
                    return "NOTICE";
                case MAV_SEVERITY.INFO:
                    return "INFO";
                case MAV_SEVERITY.DEBUG:
                    return "DEBUG";
                default:
                    return "INFO";
            }
        }

        // Double buffered panel to prevent flickering
        private class DoubleBufferedPanel : Panel
        {
            public DoubleBufferedPanel()
            {
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
                this.UpdateStyles();
            }
        }
    }
}
