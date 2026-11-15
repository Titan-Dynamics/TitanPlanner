using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Tells of an update without stopping anything: a window in the middle of the main one, hard to miss, but not
    /// modal, so a launch's parameter download and the rest carry on under it. It gives the version on offer, its
    /// latest changes, and Update now or Not now.
    /// </summary>
    public class UpdateNotice : Form
    {
        private const int TextWidth = 440;

        private static UpdateNotice open;

        private readonly List<Label> dimLabels = new List<Label>();
        private readonly LinkLabel allChanges;
        private readonly MyButton notNow;

        /// <summary>
        /// Shows the notice, replacing one already open. <paramref name="update"/> runs on Update now.
        /// </summary>
        public static void ShowFor(bool dev, Version current, Version available, IList<string> changes,
            string allChangesUrl, Action update)
        {
            open?.Close();
            var notice = new UpdateNotice(dev, current, available, changes, allChangesUrl, update);
            notice.FormClosed += (s, e) =>
            {
                if (open == notice)
                    open = null;
            };
            open = notice;
            ThemeManager.ApplyThemeTo(notice);
            // owned, so it stays over the main window, but not modal
            notice.Show(MainV2.instance);
        }

        /// <summary>
        /// The newest of the changes a ChangeLog.txt lists, newest first, without their commit ids. The file
        /// (.github/scripts/release-notes.sh) has the fork's commits as "* subject (sha)" bullets, oldest first, then
        /// upstream's in a section of their own: the fork's are the ones that tell what changed, upstream's are used
        /// only when there are none.
        /// </summary>
        public static List<string> ParseChangeLog(string text, int count)
        {
            var sections = new List<List<string>>();
            List<string> section = null;
            foreach (var raw in (text ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                if (!line.StartsWith("* "))
                {
                    section = null;
                    continue;
                }

                if (section == null)
                {
                    section = new List<string>();
                    sections.Add(section);
                }

                // the cap's "...and 12 more" isn't a change
                if (line.StartsWith("* ...and "))
                    continue;
                section.Add(Regex.Replace(line.Substring(2), @"\s*\([0-9a-f]{7,40}\)\s*$", ""));
            }

            var changes = sections.FirstOrDefault(s => s.Count > 0) ?? new List<string>();
            return Enumerable.Reverse(changes).Take(count).ToList();
        }

        private UpdateNotice(bool dev, Version current, Version available, IList<string> changes,
            string allChangesUrl, Action update)
        {
            Text = "Update available";
            Font = new Font("Segoe UI", 9.5F);
            // a standard frame: a tool window's small caption draws its close box oddly with the dark title bar
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            KeyPreview = true;
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape)
                    Close();
            };

            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1,
                Padding = new Padding(18, 14, 18, 14),
                Dock = DockStyle.Fill
            };

            Label Line(string text, Font font, Padding margin, bool dim = false)
            {
                var label = new Label
                {
                    Text = text,
                    Font = font,
                    AutoSize = true,
                    MaximumSize = new Size(TextWidth, 0),
                    Margin = margin
                };
                if (dim)
                    dimLabels.Add(label);
                table.Controls.Add(label);
                return label;
            }

            var title = dev
                ? "A new Dev build is available"
                : available != null
                    ? "TitanPlanner " + available + " is available"
                    : "A new version of TitanPlanner is available";
            Line(title, new Font("Segoe UI", 12F, FontStyle.Bold), new Padding(0, 0, 0, 4));

            var running = "You're running " + current + ", built " + MissionPlanner.Utilities.Update.BuildDate(current).ToString("d MMM yyyy") + ".";
            var detail = dev && available != null ? "Dev build " + available + " is ready. " + running : running;
            Line(detail + " Updating closes TitanPlanner, installs the new version and starts it again.", Font,
                new Padding(0, 0, 0, 12), dim: true);

            if (changes != null && changes.Count > 0)
            {
                Line(changes.Count == 1 ? "Latest change" : "Latest changes", new Font("Segoe UI", 9.5F, FontStyle.Bold),
                    new Padding(0, 0, 0, 4));
                foreach (var change in changes)
                    Line("•  " + change, Font, new Padding(6, 0, 0, 3));
            }

            allChanges = new LinkLabel
            {
                Text = "See all changes",
                AutoSize = true,
                Margin = new Padding(0, 8, 0, 14)
            };
            allChanges.LinkClicked += (s, e) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(allChangesUrl) {UseShellExecute = true});
                }
                catch
                {
                    // no browser to open it in
                }
            };
            if (!string.IsNullOrEmpty(allChangesUrl))
                table.Controls.Add(allChanges);

            var buttons = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            var updateNow = new MyButton {Text = "Update now", Size = new Size(120, 30), Margin = new Padding(8, 0, 0, 0)};
            updateNow.Click += (s, e) =>
            {
                Close();
                update?.Invoke();
            };
            notNow = new MyButton {Text = "Not now", Size = new Size(100, 30), Margin = new Padding(0)};
            notNow.Click += (s, e) => Close();
            buttons.Controls.Add(updateNow);
            buttons.Controls.Add(notNow);
            table.Controls.Add(buttons);

            Controls.Add(table);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // the theme is applied by now
            var dim = Color.FromArgb(
                BackColor.R + (ForeColor.R - BackColor.R) * 60 / 100,
                BackColor.G + (ForeColor.G - BackColor.G) * 60 / 100,
                BackColor.B + (ForeColor.B - BackColor.B) * 60 / 100);
            foreach (var label in dimLabels)
                label.ForeColor = dim;
            allChanges.LinkColor = allChanges.ActiveLinkColor = allChanges.VisitedLinkColor = ThemeManager.ButBG;
            // the way out, quieter than Update now
            ThemeManager.ApplyPlainButtonTheme(notNow);

            // in the middle of the main window, where it can't be missed (Show takes no CenterParent)
            var owner = Owner;
            var host = owner != null && owner.WindowState != FormWindowState.Minimized
                ? owner.Bounds
                : Screen.FromPoint(Cursor.Position).WorkingArea;
            Location = new Point(host.Left + (host.Width - Width) / 2, host.Top + (host.Height - Height) / 2);
        }
    }
}
