using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// Lays groups out in equal-width columns, putting each in turn under the shortest column, so boxes of different
    /// heights pack without gaps. Groups tagged <see cref="FullWidth"/> go under all the columns, as wide as they are
    /// together. The groups size their own height (AutoSize); their width is pinned to the column's.
    /// </summary>
    public class GroupColumnsPanel : Panel
    {
        public static readonly object FullWidth = new object();

        /// <summary>The narrowest a column may be: fewer columns are used before any is narrower.</summary>
        public int MinColumnWidth { get; set; } = 400;

        /// <summary>The widest a column may be, the groups keeping to the left beyond it; 0 for no limit.</summary>
        public int MaxColumnWidth { get; set; }

        /// <summary>The most columns, however wide the panel; 0 for as many as fit.</summary>
        public int MaxColumns { get; set; }

        /// <summary>The space between the columns, and between the groups in a column.</summary>
        public int Gap { get; set; } = 10;

        /// <summary>The size the groups take, with the padding, as of the last layout.</summary>
        public Size ContentSize { get; private set; }

        /// <summary>The width of a column, as of the last layout.</summary>
        public int ColumnWidth { get; private set; }

        /// <summary>
        /// Raised in a layout when the column width changes, before the groups are measured, so content that wraps
        /// (a label's MaximumSize) can be fitted to the new width: a group can't measure wrapping text itself.
        /// </summary>
        public event EventHandler ColumnWidthChanged;

        protected override void OnLayout(LayoutEventArgs levent)
        {
            var visible = Controls.Cast<Control>().Where(c => c.Visible).ToList();
            var groups = visible.Where(c => c.Tag != FullWidth).ToList();
            var wide = visible.Where(c => c.Tag == FullWidth).ToList();
            int width = ClientSize.Width - Padding.Horizontal;
            if (visible.Count == 0 || width <= 0)
            {
                ContentSize = new Size(Padding.Horizontal, Padding.Vertical);
                base.OnLayout(levent);
                return;
            }

            int columns = Math.Max(1, Math.Min(groups.Count, (width + Gap) / (MinColumnWidth + Gap)));
            if (MaxColumns > 0)
                columns = Math.Min(columns, MaxColumns);
            int columnWidth = Math.Max(MinColumnWidth, (width - (columns - 1) * Gap) / columns);
            if (MaxColumnWidth > 0)
                columnWidth = Math.Min(columnWidth, Math.Max(MinColumnWidth, MaxColumnWidth));
            int fullWidth = columns * columnWidth + (columns - 1) * Gap;

            if (columnWidth != ColumnWidth)
            {
                ColumnWidth = columnWidth;
                ColumnWidthChanged?.Invoke(this, EventArgs.Empty);
            }

            // the groups size their height to their content; pin their width to the column's (or all of them)
            SuspendLayout();
            foreach (var group in visible)
            {
                int groupWidth = wide.Contains(group) ? fullWidth : columnWidth;
                group.MinimumSize = new Size(groupWidth, 0);
                group.MaximumSize = new Size(groupWidth, 0);
            }
            ResumeLayout(false);

            base.OnLayout(levent);

            var bottoms = new int[columns];
            var origin = DisplayRectangle.Location;
            SuspendLayout();
            foreach (var group in groups)
            {
                int column = Array.IndexOf(bottoms, bottoms.Min());
                group.Location = new Point(origin.X + column * (columnWidth + Gap), origin.Y + bottoms[column]);
                bottoms[column] += group.Height + Gap;
            }

            int bottom = bottoms.Max();
            foreach (var group in wide)
            {
                group.Location = new Point(origin.X, origin.Y + bottom);
                bottom += group.Height + Gap;
            }
            ResumeLayout(false);

            ContentSize = new Size(Padding.Horizontal + fullWidth, Padding.Vertical + Math.Max(0, bottom - Gap));

            // the scroll range follows the groups' new places
            AdjustFormScrollbars(AutoScroll);
        }
    }
}
