using System;
using System.Collections.Generic;
using System.Linq;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// The FLY screen's saved tab list ("tabcontrolactions", tab names joined by ';') only shows the tabs it names,
    /// so a tab added to the application has to be added to a list saved before it existed.
    /// </summary>
    public static class TabListMigration
    {
        /// <summary>
        /// The saved list with the tab added after another (at the end if that one isn't shown). An empty list shows
        /// every tab already and is returned as it is, as is a list that has the tab.
        /// </summary>
        public static string AddTab(string saved, string tab, string after)
        {
            if (string.IsNullOrEmpty(saved))
                return saved;

            var tabs = saved.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (tabs.Count == 0 || tabs.Contains(tab))
                return saved;

            var at = tabs.IndexOf(after);
            tabs.Insert(at < 0 ? tabs.Count : at + 1, tab);

            // the list is saved with a ';' after every name
            return string.Join(";", tabs) + ";";
        }
    }
}
