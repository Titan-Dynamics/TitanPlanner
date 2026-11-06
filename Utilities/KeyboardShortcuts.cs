using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// Makes a method the handler of a keyboard shortcut. Its class's ProcessCmdKey hands the keys to
    /// <see cref="KeyboardShortcuts.Handle"/>, which runs the method, and the planner settings list every shortcut
    /// declared this way, so the attribute is the one place a shortcut is written down. The method takes no
    /// arguments, or the Keys pressed; one returning bool says whether it took the key (false lets the key go on).
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class KeyboardShortcutAttribute : Attribute
    {
        public KeyboardShortcutAttribute(Keys keys, string description)
        {
            Keys = keys;
            Description = description;
        }

        public Keys Keys { get; }

        public string Description { get; }
    }

    /// <summary>
    /// Where a class's keyboard shortcuts work, as the planner settings list them; lower orders come first.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class KeyboardShortcutScopeAttribute : Attribute
    {
        public KeyboardShortcutScopeAttribute(string name, int order = 100)
        {
            Name = name;
            Order = order;
        }

        public string Name { get; }

        public int Order { get; }
    }

    /// <summary>
    /// Runs the shortcuts declared with <see cref="KeyboardShortcutAttribute"/> and lists them.
    /// </summary>
    public static class KeyboardShortcuts
    {
        /// <summary>Keys that do one thing in one place, as the planner settings show them.</summary>
        public sealed class Entry
        {
            public string Scope;
            public int Order;
            public string Description;
            public List<Keys> Keys = new List<Keys>();

            public string KeysText => Keys.Count > 2
                ? Text(Keys[0]) + " … " + Text(Keys[Keys.Count - 1])
                : string.Join(", ", Keys.Select(Text));
        }

        private const BindingFlags Methods =
            BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.DeclaredOnly;

        private static readonly Dictionary<Type, Dictionary<Keys, MethodInfo>> Handlers =
            new Dictionary<Type, Dictionary<Keys, MethodInfo>>();

        private static List<Entry> _all;

        // when a shortcut with Alt was last taken, during the current press of Alt
        private static DateTime _altShortcutTaken = DateTime.MinValue;

        /// <summary>
        /// Runs the owner's shortcut for the keys, if it has one, and says whether it took them.
        /// </summary>
        public static bool Handle(Control owner, ref Message msg, Keys keyData)
        {
            // a new press of Alt, not its key repeat, starts afresh
            if ((keyData & Keys.KeyCode) == Keys.Menu && (msg.LParam.ToInt64() & (1 << 30)) == 0)
                _altShortcutTaken = DateTime.MinValue;

            if (owner == null || !HandlersOf(owner.GetType()).TryGetValue(keyData, out var method))
                return false;

            object result;
            try
            {
                result = method.Invoke(owner, method.GetParameters().Length == 1 ? new object[] { keyData } : null);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                // the handler's own exception, as it was before shortcuts were declared
                ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }

            bool taken = !(result is bool handled) || handled;
            if (taken && (keyData & Keys.Alt) != 0)
                _altShortcutTaken = DateTime.UtcNow;
            return taken;
        }

        /// <summary>
        /// Whether to drop this message, the menu activation Windows sends on releasing Alt (WM_SYSCOMMAND
        /// SC_KEYMENU). A key taken as a shortcut never reaches Windows, which so takes Alt as pressed on its own and
        /// would select the window's menu; the form calls this from its WndProc.
        /// </summary>
        public static bool SuppressMenuKey(ref Message m)
        {
            const int WM_SYSCOMMAND = 0x0112;
            const int SC_KEYMENU = 0xF100;

            // lParam is the character for a menu mnemonic, 0 for Alt on its own
            if (m.Msg != WM_SYSCOMMAND || (m.WParam.ToInt64() & 0xFFF0) != SC_KEYMENU || m.LParam != IntPtr.Zero)
                return false;

            // a minute bounds a shortcut that left no menu activation behind (another key taken by Windows)
            bool suppress = DateTime.UtcNow - _altShortcutTaken < TimeSpan.FromMinutes(1);
            _altShortcutTaken = DateTime.MinValue;
            return suppress;
        }

        /// <summary>
        /// The text with the keys of the type's shortcut method after it, as "FPV Mode (Alt-F)", or the text alone
        /// if the method has none.
        /// </summary>
        public static string Label(string text, Type type, string method)
        {
            var keys = HandlersOf(type).Where(kv => kv.Value.Name == method).Select(kv => kv.Key).ToList();
            return keys.Count == 0 ? text : text + " (" + string.Join(", ", keys.Select(Text)) + ")";
        }

        /// <summary>
        /// Every shortcut declared in the planner, by where it works and then by its keys.
        /// </summary>
        public static IReadOnlyList<Entry> All
        {
            get
            {
                if (_all == null)
                    _all = Find(typeof(KeyboardShortcuts).Assembly);
                return _all;
            }
        }

        /// <summary>
        /// The shortcuts the assembly declares, each place's keys that do the same thing in one entry.
        /// </summary>
        public static List<Entry> Find(Assembly assembly)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                types = ex.Types.Where(t => t != null).ToArray();
            }

            var entries = new List<Entry>();
            // shortcuts belong to controls, whose ProcessCmdKey runs them
            foreach (var type in types.Where(t => typeof(Control).IsAssignableFrom(t)))
            {
                var scope = type.GetCustomAttribute<KeyboardShortcutScopeAttribute>();
                foreach (var method in type.GetMethods(Methods))
                {
                    foreach (var shortcut in method.GetCustomAttributes<KeyboardShortcutAttribute>(false))
                    {
                        var name = scope?.Name ?? type.Name;
                        var entry = entries.FirstOrDefault(e => e.Scope == name && e.Description == shortcut.Description);
                        if (entry == null)
                        {
                            entry = new Entry
                            {
                                Scope = name, Order = scope?.Order ?? 100, Description = shortcut.Description
                            };
                            entries.Add(entry);
                        }

                        if (!entry.Keys.Contains(shortcut.Keys))
                            entry.Keys.Add(shortcut.Keys);
                    }
                }
            }

            return entries
                .OrderBy(e => e.Order)
                .ThenBy(e => e.Scope, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => SortKey(e.Keys[0]))
                .ToList();
        }

        /// <summary>
        /// The keys as the planner writes them: "Ctrl-Shift-R", "Alt-F", "F12", "Num +".
        /// </summary>
        public static string Text(Keys keys)
        {
            var parts = new List<string>();
            if ((keys & Keys.Control) != 0)
                parts.Add("Ctrl");
            if ((keys & Keys.Shift) != 0)
                parts.Add("Shift");
            if ((keys & Keys.Alt) != 0)
                parts.Add("Alt");
            parts.Add(KeyName(keys & Keys.KeyCode));
            return string.Join("-", parts);
        }

        private static string KeyName(Keys key)
        {
            if (key >= Keys.D0 && key <= Keys.D9)
                return ((int)(key - Keys.D0)).ToString();

            switch (key)
            {
                case Keys.Add:
                    return "Num +";
                case Keys.Subtract:
                    return "Num -";
                case Keys.Multiply:
                    return "Num *";
                case Keys.Divide:
                    return "Num /";
                case Keys.Oemplus:
                    return "+";
                case Keys.OemMinus:
                    return "-";
                default:
                    return key.ToString();
            }
        }

        // plain keys first, then Ctrl, Ctrl-Shift and Alt, each by key, with F-keys in number order
        private static long SortKey(Keys keys)
        {
            int rank = (keys & Keys.Alt) != 0 ? 3 : (keys & Keys.Shift) != 0 ? 2 : (keys & Keys.Control) != 0 ? 1 : 0;
            return ((long)rank << 32) | (uint)(keys & Keys.KeyCode);
        }

        private static Dictionary<Keys, MethodInfo> HandlersOf(Type type)
        {
            lock (Handlers)
            {
                if (!Handlers.TryGetValue(type, out var map))
                {
                    map = new Dictionary<Keys, MethodInfo>();
                    for (var t = type; t != null && t != typeof(Control); t = t.BaseType)
                    {
                        foreach (var method in t.GetMethods(Methods))
                        {
                            // a derived class's shortcut wins over its base's
                            foreach (var shortcut in method.GetCustomAttributes<KeyboardShortcutAttribute>(false))
                            {
                                if (!map.ContainsKey(shortcut.Keys))
                                    map[shortcut.Keys] = method;
                            }
                        }
                    }

                    Handlers[type] = map;
                }

                return map;
            }
        }
    }
}
