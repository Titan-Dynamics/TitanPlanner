using System;
using System.Diagnostics;
using System.IO;
using MissionPlanner.Log;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// The log folder, where logs and HUD recordings are saved.
    /// </summary>
    public static class LogFolder
    {
        /// <summary>
        /// Opens the log folder, making it first if it isn't there, or says why it can't.
        /// </summary>
        public static void Open()
        {
            try
            {
                Directory.CreateDirectory(Settings.Instance.LogDir);
                Process.Start(Settings.Instance.LogDir);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(
                    string.Format(LogStrings.LogDirectoryError, Settings.Instance.LogDir) + "\r\n" + ex.Message,
                    Strings.ERROR);
            }
        }
    }
}
