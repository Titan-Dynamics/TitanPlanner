using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace MissionPlanner.Utilities.Tests
{
    [TestClass]
    public class MotorFrameImagesTests
    {
        string userData;
        string previousUserData;

        [TestInitialize]
        public void UseTemporaryUserData()
        {
            previousUserData = Settings.CustomUserDataDirectory;
            userData = Path.Combine(Path.GetTempPath(), "MotorFrameImagesTests-" + Guid.NewGuid().ToString("N"));
            Settings.CustomUserDataDirectory = userData;
        }

        [TestCleanup]
        public void RestoreUserData()
        {
            Settings.CustomUserDataDirectory = previousUserData;
            try
            {
                Directory.Delete(userData, true);
            }
            catch (IOException)
            {
            }
        }

        static string RepoMotorFrames()
        {
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "MotorFrames");
                if (Directory.Exists(candidate) && File.Exists(Path.Combine(dir.FullName, "MissionPlanner.csproj")))
                    return candidate;
            }

            Assert.Inconclusive("repository MotorFrames folder not found");
            return null;
        }

        // Air-gapped setups only have the shipped copies.
        [TestMethod]
        public void EveryFrameShipsADiagram()
        {
            var shipped = RepoMotorFrames();
            foreach (var frame in MotorFrameImages.Frames)
            {
                string fileName;
                Assert.IsTrue(MotorFrameImages.TryGetFileName(frame.frameClass, frame.frameType, out fileName));
                var path = Path.Combine(shipped, fileName);
                Assert.IsTrue(File.Exists(path), fileName + " is not shipped");
                StringAssert.Contains(File.ReadAllText(path), "<svg");
            }
        }

        [TestMethod]
        public void ACachedCopyIsPreferred()
        {
            Directory.CreateDirectory(MotorFrameImages.CacheDirectory);
            string fileName;
            MotorFrameImages.TryGetFileName(1, 1, out fileName);
            File.WriteAllText(Path.Combine(MotorFrameImages.CacheDirectory, fileName), "<svg>cached</svg>");

            Assert.AreEqual("<svg>cached</svg>", MotorFrameImages.Load(1, 1));
        }

        [TestMethod]
        public void UnknownFramesHaveNoDiagram()
        {
            string fileName;
            Assert.IsFalse(MotorFrameImages.TryGetFileName(99, 99, out fileName));
            Assert.IsNull(MotorFrameImages.Load(99, 99));
        }

        // Hits ardupilot.org, like the other download tests.
        [TestMethod]
        public void RefreshWritesTheCacheFromArduPilotOrg()
        {
            MotorFrameImages.RefreshAsync(1, 1).GetAwaiter().GetResult();

            var cached = Directory.GetFiles(MotorFrameImages.CacheDirectory, "*.svg");
            Assert.AreEqual(MotorFrameImages.Frames.Count(), cached.Length);
            Assert.IsTrue(cached.All(f => File.ReadAllText(f).Contains("<svg")));
            Assert.IsFalse(Directory.GetFiles(MotorFrameImages.CacheDirectory, "*.tmp").Any());
        }
    }
}
