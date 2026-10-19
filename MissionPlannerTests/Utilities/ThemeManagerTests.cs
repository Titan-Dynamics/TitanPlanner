using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class ThemeManagerTests
    {
        [DllImport("user32.dll")]
        static extern uint GetGuiResources(IntPtr process, uint flags);

        const uint GdiObjects = 0;
        const uint UserObjects = 1;

        static readonly FieldInfo IconFile = typeof(Program).GetField("IconFile");

        // The test project sees System.Drawing twice (the real one and the MissionPlanner.Drawing shim), so the
        // bitmap is made from the assembly Program.IconFile's type comes from.
        static object NewBitmap(int size)
        {
            var bitmap = IconFile.FieldType.Assembly.GetType("System.Drawing.Bitmap");
            return Activator.CreateInstance(bitmap, size, size);
        }

        // Issue #31: every form themed made a new icon handle from Program.IconFile and never freed it, so a form
        // themed on a timer ran the process out of GDI handles.
        [TestMethod]
        public void FormIconIsMadeOnceWithoutLeakingHandles()
        {
            var previous = IconFile.GetValue(null);
            try
            {
                IconFile.SetValue(null, NewBitmap(32));
                var first = ThemeManager.FormIcon;
                Assert.IsNotNull(first);

                var process = Process.GetCurrentProcess().Handle;
                uint gdiBefore = GetGuiResources(process, GdiObjects);
                uint userBefore = GetGuiResources(process, UserObjects);

                for (int i = 0; i < 200; i++)
                    Assert.AreSame(first, ThemeManager.FormIcon);

                Assert.AreEqual(gdiBefore, GetGuiResources(process, GdiObjects));
                Assert.AreEqual(userBefore, GetGuiResources(process, UserObjects));
            }
            finally
            {
                IconFile.SetValue(null, previous);
            }
        }

        [TestMethod]
        public void FormIconFollowsANewIconFile()
        {
            var previous = IconFile.GetValue(null);
            try
            {
                IconFile.SetValue(null, NewBitmap(32));
                var first = ThemeManager.FormIcon;
                IconFile.SetValue(null, NewBitmap(16));
                Assert.AreNotSame(first, ThemeManager.FormIcon);
            }
            finally
            {
                IconFile.SetValue(null, previous);
            }
        }
    }
}
