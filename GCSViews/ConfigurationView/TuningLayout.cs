using MissionPlanner.Controls;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// Lays out a tuning page (ConfigArduplane, ConfigArducopter, ConfigArdurover), which the designer builds as group
    /// boxes at fixed places: its groups packed into up to three columns, each rebuilt as rows of a name and its value
    /// from where the designer put them, and the controls outside any group in groups of their own; its actions in a slim
    /// bar under the groups, which stays at the bottom while they scroll. <see cref="ShowPresent"/> then hides the
    /// parameters the vehicle doesn't have, and the groups it leaves empty.
    /// </summary>
    internal class TuningLayout
    {
        private const int MinInputWidth = 90;
        private const int MaxInputWidth = 150;
        private const int ActionWidth = 80;
        private const int ActionHeight = 20;

        /// <summary>Controls outside the designer's groups, put in a group of their own: each row a name and its input.</summary>
        public class Extra
        {
            public readonly string Title;
            public readonly (string Name, Control Input)[] Rows;

            public Extra(string title, params (string Name, Control Input)[] rows)
            {
                Title = title;
                Rows = rows;
            }
        }

        private class Group
        {
            public GroupBox Box;
            public TableLayoutPanel Table;
            public readonly List<(Label Name, Control Input)> Rows = new List<(Label, Control)>();
        }

        private readonly UserControl page;
        private readonly List<Group> groups = new List<Group>();
        private GroupColumnsPanel body;
        private Panel footer;
        private bool arranging;

        private TuningLayout(UserControl page)
        {
            this.page = page;
        }

        /// <summary>
        /// Lays the page out. The actions go at the left of the bottom bar in the order given, the options after them.
        /// </summary>
        public static TuningLayout Apply(UserControl page, Control[] actions, Control[] options, params Extra[] extras)
        {
            var layout = new TuningLayout(page);
            layout.Build(actions, options ?? new Control[0], extras);
            return layout;
        }

        /// <summary>The write button in red, as the parameter list has it: it changes the vehicle.</summary>
        public static void StyleWrite(MyButton button)
        {
            button.IgnoreTheme = true;
            button.BGGradTop = Color.FromArgb(170, 45, 45);
            button.BGGradBot = Color.FromArgb(220, 90, 90);
            button.Outline = Color.FromArgb(140, 30, 30);
            button.ForeColor = Color.White;
            button.TextColor = Color.White;
            button.TextColorNotEnabled = Color.FromArgb(180, 180, 180);
        }

        /// <summary>Every control on a page, at any depth.</summary>
        public static IEnumerable<Control> Descendants(Control parent)
        {
            foreach (Control child in parent.Controls)
            {
                yield return child;
                foreach (var grandchild in Descendants(child))
                    yield return grandchild;
            }
        }

        private static bool IsName(Control control) => control is Label;

        /// <summary>
        /// Pairs controls into rows from where the designer put them: those whose middles are within a few pixels of
        /// each other are a line, read left to right, each name with the input after it. A line may hold several pairs.
        /// </summary>
        private static List<(Label Name, Control Input)> Pair(IEnumerable<Control> controls)
        {
            int Middle(Control c) => c.Top + c.Height / 2;

            var lines = new List<List<Control>>();
            foreach (var control in controls.OrderBy(Middle).ThenBy(c => c.Left))
            {
                if (lines.Count == 0 || Math.Abs(Middle(control) - Middle(lines[lines.Count - 1][0])) > 10)
                    lines.Add(new List<Control>());
                lines[lines.Count - 1].Add(control);
            }

            var rows = new List<(Label, Control)>();
            foreach (var line in lines)
            {
                Label name = null;
                foreach (var control in line.OrderBy(c => c.Left))
                {
                    if (IsName(control))
                    {
                        if (name != null)
                            rows.Add((name, null));
                        name = (Label) control;
                    }
                    else
                    {
                        rows.Add((name, control));
                        name = null;
                    }
                }

                if (name != null)
                    rows.Add((name, null));
            }

            return rows;
        }

        private void Build(Control[] actions, Control[] options, Extra[] extras)
        {
            page.SuspendLayout();

            // where everything was, read before the font changes and scales it
            var all = page.Controls.Cast<Control>().ToList();
            var planned = new List<(Point At, GroupBox Box, string Title, List<(Label, Control)> Rows, int InputWidth)>();

            int InputWidth(IEnumerable<Control> inputs)
            {
                var widths = inputs.Where(c => c != null && !(c is CheckBox)).Select(c => c.Width).ToList();
                return Math.Max(MinInputWidth, Math.Min(MaxInputWidth, widths.Count == 0 ? 0 : widths.Max()));
            }

            foreach (var box in all.OfType<GroupBox>())
            {
                var rows = Pair(box.Controls.Cast<Control>());
                planned.Add((box.Location, box, box.Text, rows, InputWidth(rows.Select(r => r.Item2))));
            }

            // outside the groups: those the page groups by name, the names beside them dropped, and the rest under Other
            var claimed = new HashSet<Control>(extras.SelectMany(x => x.Rows.Select(r => r.Input)));
            var loose = all.Where(c => !(c is GroupBox) && !actions.Contains(c) && !options.Contains(c)).ToList();
            var other = new List<(Label, Control)>();
            foreach (var row in Pair(loose))
            {
                if (row.Input != null && claimed.Contains(row.Input))
                {
                    row.Name?.Dispose();
                    continue;
                }

                other.Add(row);
            }

            foreach (var extra in extras)
            {
                var at = extra.Rows.Select(r => r.Input.Location).OrderBy(p => p.Y).ThenBy(p => p.X).First();
                var rows = extra.Rows.Select(r => (new Label {Text = r.Name}, r.Input)).ToList();
                planned.Add((at, null, extra.Title, rows, InputWidth(extra.Rows.Select(r => r.Input))));
            }

            if (other.Count > 0)
            {
                var at = other.Select(r => (r.Item2 ?? r.Item1).Location).OrderBy(p => p.Y).ThenBy(p => p.X).First();
                planned.Add((at, null, "Other", other, InputWidth(other.Select(r => r.Item2))));
            }

            page.Controls.Clear();
            page.AutoScroll = false;
            page.Font = new Font("Segoe UI", 9F);
            var titleFont = new Font(page.Font, FontStyle.Bold);

            body = new GroupColumnsPanel
            {
                AutoScroll = true,
                Padding = new Padding(8, 6, 8, 8),
                MinColumnWidth = 240,
                // three columns stop at about half a full-screen window, not stretched across it
                MaxColumnWidth = 320,
                MaxColumns = 3,
                Gap = 8
            };
            // in the designer's reading order: along its rows, then down
            foreach (var plan in planned.OrderBy(p => p.At.Y).ThenBy(p => p.At.X))
            {
                var group = new Group {Box = plan.Box ?? new GroupBox {Text = plan.Title}};
                group.Box.SuspendLayout();
                group.Box.Controls.Clear();
                group.Box.AutoSize = true;
                group.Box.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                group.Box.Dock = DockStyle.None;
                group.Box.Padding = new Padding(8, 4, 8, 6);
                group.Box.Margin = new Padding(0);
                group.Box.Font = titleFont;

                group.Table = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    Margin = new Padding(0),
                    Font = page.Font
                };
                group.Table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
                group.Table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, plan.InputWidth));

                int row = 0;
                foreach (var (name, input) in plan.Rows)
                {
                    var label = name ?? new Label {Text = input?.Name ?? ""};
                    label.AutoSize = false;
                    label.Dock = DockStyle.Fill;
                    label.TextAlign = ContentAlignment.MiddleLeft;
                    label.AutoEllipsis = true;
                    label.Margin = new Padding(0, 2, 6, 2);

                    if (input == null)
                    {
                        // a note on its own
                        label.AutoEllipsis = false;
                        label.AutoSize = true;
                        group.Table.Controls.Add(label, 0, row);
                        group.Table.SetColumnSpan(label, 2);
                    }
                    else if (input is CheckBox)
                    {
                        input.Margin = new Padding(0, 2, 0, 2);
                        group.Table.Controls.Add(input, 0, row);
                        group.Table.SetColumnSpan(input, 2);
                        label.Dispose();
                        label = null;
                    }
                    else
                    {
                        input.Dock = DockStyle.Fill;
                        input.Margin = new Padding(0, 2, 0, 2);
                        group.Table.Controls.Add(label, 0, row);
                        group.Table.Controls.Add(input, 1, row);
                    }

                    if (label != null || input != null)
                        group.Rows.Add((label, input));
                    row++;
                }

                group.Box.Controls.Add(group.Table);
                group.Box.ResumeLayout(false);
                body.Controls.Add(group.Box);
                groups.Add(group);
            }

            // a slim bar under the groups, out of the scrolling (Arrange): the page is often long, and the groups get
            // the height
            footer = new Panel {Height = ActionHeight + 8, Padding = new Padding(8, 4, 8, 4)};
            var bar = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                WrapContents = false,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };
            foreach (var action in actions)
            {
                action.Dock = DockStyle.None;
                action.Size = new Size(Math.Max(ActionWidth, TextRenderer.MeasureText(action.Text, page.Font).Width + 24),
                    ActionHeight);
                action.Padding = new Padding(0);
                action.Margin = new Padding(0, 0, 6, 0);
                bar.Controls.Add(action);
            }

            foreach (var option in options)
            {
                option.Dock = DockStyle.None;
                // centred on the buttons
                option.Margin = new Padding(10, Math.Max(0, (ActionHeight - option.PreferredSize.Height) / 2), 6, 0);
                bar.Controls.Add(option);
            }

            footer.Controls.Add(bar);

            page.Controls.Add(body);
            page.Controls.Add(footer);
            page.Layout += (s, e) => Arrange();

            page.ResumeLayout(true);
        }

        /// <summary>
        /// The groups at the top, as tall as they are, and the bar under them, as wide as they are: when they are longer
        /// than the page, they scroll in what the bar leaves and the bar stays at the bottom.
        /// </summary>
        private void Arrange()
        {
            if (arranging)
                return;
            arranging = true;
            try
            {
                var client = page.ClientSize;
                int room = Math.Max(0, client.Height - footer.Height);

                // laid out in all the room first, which shows a scroll bar if they need one; then as tall as they are
                body.SetBounds(0, 0, client.Width, room);
                // a pixel spare, so rounding never shows a scroll bar they don't need
                int height = Math.Min(body.ContentSize.Height + 1, room);
                if (height != body.Height)
                    body.Height = height;

                int width = body.ContentSize.Width > 0 ? Math.Min(client.Width, body.ContentSize.Width) : client.Width;
                footer.SetBounds(0, height, width, footer.Height);
            }
            finally
            {
                arranging = false;
            }
        }

        private static bool Present(Control input, MAVLink.MAVLinkParamList param)
        {
            switch (input)
            {
                case MavlinkNumericUpDown number:
                    return number.ParamName != null && param.ContainsKey(number.ParamName);
                case MavlinkComboBox choice:
                    return choice.ParamName != null && param.ContainsKey(choice.ParamName);
                default:
                    return true;
            }
        }

        /// <summary>
        /// Shows the rows whose parameter the vehicle has, and the groups with any. Call after the page's setup, which
        /// picks each input's parameter from the names it may have.
        /// </summary>
        public void ShowPresent(MAVLink.MAVLinkParamList param)
        {
            foreach (var group in groups)
            {
                bool any = false;
                group.Table.SuspendLayout();
                foreach (var (name, input) in group.Rows)
                {
                    bool present = input == null || Present(input, param);
                    if (name != null)
                        name.Visible = present;
                    if (input != null)
                        input.Visible = present;
                    any |= present;
                }

                group.Table.ResumeLayout(false);
                group.Box.Visible = any;
            }

            page.PerformLayout();
        }
    }
}
