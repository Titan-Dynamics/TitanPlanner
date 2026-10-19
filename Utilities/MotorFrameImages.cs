using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using log4net;

namespace MissionPlanner.Utilities
{
    /// <summary>
    /// The motor-order diagrams for each copter frame class and type, from the ArduPilot documentation. A copy of each
    /// ships next to the exe (MotorFrames) so the Motor Setup page shows them without internet; a per-user cache holds
    /// newer copies, written over from ardupilot.org whenever it can be reached.
    /// </summary>
    public static class MotorFrameImages
    {
        private static readonly ILog log = LogManager.GetLogger(typeof(MotorFrameImages));

        public const string BaseUrl = "https://ardupilot.org/copter/_images/";

        /// <summary>Cached copies younger than this are not downloaded again.</summary>
        public static readonly TimeSpan RefreshAge = TimeSpan.FromDays(7);

        private static readonly Dictionary<(int frameClass, int frameType), string> FileNames =
            new Dictionary<(int, int), string>
            {
                // QUAD (Class 1)
                { (1, 0), "m_01_00_quad_plus.svg" },
                { (1, 1), "m_01_01_quad_x.svg" },
                { (1, 2), "m_01_02_quad_v.svg" },
                { (1, 3), "m_01_03_quad_h.svg" },
                { (1, 4), "m_01_04_quad_v_tail.svg" },
                { (1, 5), "m_01_05_quad_a_tail.svg" },
                { (1, 6), "m_01_06_quad_plus_rev.svg" },
                { (1, 12), "m_01_12_quad_x_bf.svg" },
                { (1, 13), "m_01_13_quad_x_dji.svg" },
                { (1, 14), "m_01_14_quad_x_cw.svg" },
                { (1, 16), "m_01_16_quad_plus_nyt.svg" },
                { (1, 17), "m_01_17_quad_x_nyt.svg" },
                { (1, 18), "m_01_18_quad_x_bf_rev.svg" },
                { (1, 19), "m_01_19_quad_y4a.svg" },
                // HEXA (Class 2)
                { (2, 0), "m_02_00_hexa_plus.svg" },
                { (2, 1), "m_02_01_hexa_x.svg" },
                { (2, 3), "m_02_03_hexa_h.svg" },
                { (2, 13), "m_02_13_hexa_x_dji.svg" },
                { (2, 14), "m_02_14_hexa_x_cw.svg" },
                // OCTA (Class 3)
                { (3, 0), "m_03_00_octo_plus.svg" },
                { (3, 1), "m_03_01_octo_x.svg" },
                { (3, 2), "m_03_02_octo_v.svg" },
                { (3, 3), "m_03_03_octo_h.svg" },
                { (3, 13), "m_03_13_octo_x_dji.svg" },
                { (3, 14), "m_03_14_octo_x_cw.svg" },
                { (3, 15), "m_03_15_octo_i.svg" },
                // OCTAQUAD (Class 4)
                { (4, 0), "m_04_00_octo_quad_plus.svg" },
                { (4, 1), "m_04_01_octo_quad_x.svg" },
                { (4, 2), "m_04_02_octo_quad_v.svg" },
                { (4, 3), "m_04_03_octo_quad_h.svg" },
                { (4, 12), "m_04_12_octo_quad_x_bf.svg" },
                { (4, 14), "m_04_14_octo_quad_x_cw.svg" },
                { (4, 18), "m_04_18_octo_quad_x_bf_rev.svg" },
                // Y6 (Class 5)
                { (5, 0), "m_05_00_y6_a.svg" },
                { (5, 10), "m_05_10_y6_b.svg" },
                { (5, 11), "m_05_11_y6_f.svg" },
                // TRI (Class 7)
                { (7, 0), "m_07_00_tricopter.svg" },
                { (7, 6), "m_07_06_tricopter_pitch_rev.svg" },
                // BICOPTER (Class 10)
                { (10, 0), "m_10_00_bicopter.svg" },
                // DODECAHEXA (Class 12)
                { (12, 0), "m_12_00_dodecahexa_plus.svg" },
                { (12, 1), "m_12_01_dodecahexa_x.svg" },
                // DECA (Class 14)
                { (14, 0), "m_14_00_deca_plus.svg" },
                { (14, 1), "m_14_01_deca_x_and_cw_x.svg" },
            };

        /// <summary>Raised on a worker thread when a refresh changes a frame's diagram.</summary>
        public static event Action<int, int> Updated;

        private static readonly object refreshLock = new object();
        private static Task refresh;

        public static string ShippedDirectory => Path.Combine(Settings.GetRunningDirectory(), "MotorFrames");

        public static string CacheDirectory => Path.Combine(Settings.GetUserDataDirectory(), "MotorFrames");

        public static IEnumerable<(int frameClass, int frameType)> Frames => FileNames.Keys;

        public static bool TryGetFileName(int frameClass, int frameType, out string fileName)
        {
            return FileNames.TryGetValue((frameClass, frameType), out fileName);
        }

        /// <summary>The newest local copy of a frame's diagram (cached, else shipped), or null when there is none.</summary>
        public static string Load(int frameClass, int frameType)
        {
            string fileName;
            return TryGetFileName(frameClass, frameType, out fileName) ? LoadLocal(fileName) : null;
        }

        private static string LoadLocal(string fileName)
        {
            foreach (var directory in new[] { CacheDirectory, ShippedDirectory })
            {
                try
                {
                    var path = Path.Combine(directory, fileName);
                    if (File.Exists(path))
                        return File.ReadAllText(path);
                }
                catch (Exception ex)
                {
                    log.Debug(ex);
                }
            }

            return null;
        }

        /// <summary><see cref="RefreshAsync"/> on a worker thread, once per session.</summary>
        public static void RefreshInBackground(int frameClass, int frameType)
        {
            lock (refreshLock)
            {
                if (refresh != null)
                    return;
                refresh = Task.Run(() => RefreshAsync(frameClass, frameType));
            }
        }

        /// <summary>
        /// Downloads the diagrams whose cached copy is missing or older than <see cref="RefreshAge"/>, the given frame
        /// first, writing over the cache. Stops at the first request that fails to connect, so offline it gives up
        /// straight away.
        /// </summary>
        public static async Task RefreshAsync(int frameClass, int frameType)
        {
            var first = (frameClass, frameType);
            try
            {
                Directory.CreateDirectory(CacheDirectory);
                foreach (var frame in FileNames.Keys.OrderBy(f => f.Equals(first) ? 0 : 1).ToList())
                {
                    var fileName = FileNames[frame];
                    var path = Path.Combine(CacheDirectory, fileName);
                    if (File.Exists(path) && DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < RefreshAge)
                        continue;

                    Download.HTTPResult result;
                    try
                    {
                        result = await Download.GetAsyncWithStatus(BaseUrl + fileName, null, TimeSpan.FromSeconds(15))
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        log.Info("Motor frame diagrams not refreshed (no connection?): " + ex.Message);
                        return;
                    }

                    if (result.status != HttpStatusCode.OK || result.content == null || !result.content.Contains("<svg"))
                    {
                        log.Info("Motor frame diagram " + fileName + " not refreshed: " + result.status);
                        continue;
                    }

                    var previous = LoadLocal(fileName);

                    // Written whole and then moved over, so the page never reads a half-written file.
                    var temp = path + ".tmp";
                    File.WriteAllText(temp, result.content);
                    if (File.Exists(path))
                        File.Delete(path);
                    File.Move(temp, path);

                    if (previous != result.content)
                        Updated?.Invoke(frame.frameClass, frame.frameType);
                }
            }
            catch (Exception ex)
            {
                log.Error(ex);
            }
        }
    }
}
