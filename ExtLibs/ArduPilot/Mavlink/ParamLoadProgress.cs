using System;
using System.Diagnostics;

namespace MissionPlanner
{
    /// <summary>How a parameter list is coming.</summary>
    public enum ParamLoadMethod
    {
        Unknown,

        /// <summary>The whole list as one file (@PARAM/param.pck) over MAVFTP.</summary>
        Ftp,

        /// <summary>PARAM_REQUEST_LIST, the vehicle sending one PARAM_VALUE per parameter.</summary>
        List
    }

    /// <summary>What a parameter download is doing at the moment.</summary>
    public enum ParamLoadStage
    {
        Starting,
        FtpOpening,
        FtpDownloading,

        /// <summary>Asked for the list; counting the parameters in.</summary>
        Listing,
        Done
    }

    /// <summary>
    /// A parameter download as it goes, for a window to show: the vehicle, how the list is coming (MAVFTP's file or
    /// one message per parameter) and how far it has got. MAVLinkInterface writes it from the download's threads; a
    /// window reads it as often as it likes.
    /// </summary>
    public class ParamLoadProgress
    {
        private readonly Stopwatch watch = Stopwatch.StartNew();

        public ParamLoadProgress(MAVState vehicle, uint sysid, byte compid)
        {
            Vehicle = vehicle;
            Sysid = sysid;
            Compid = compid;
        }

        /// <summary>The vehicle the list is for, whose banner (firmware, board, frame) fills in as it arrives.</summary>
        public MAVState Vehicle { get; }

        public uint Sysid { get; }
        public byte Compid { get; }

        /// <summary>Seconds since the download started.</summary>
        public double Elapsed => watch.Elapsed.TotalSeconds;

        public volatile ParamLoadMethod Method;
        public volatile ParamLoadStage Stage;

        /// <summary>Parameters in so far.</summary>
        public volatile int Received;

        /// <summary>Parameters the vehicle has; 0 until it says.</summary>
        public volatile int Total;

        /// <summary>How much of the MAVFTP file is in, 0 to 100; -1 before it starts.</summary>
        public volatile int FilePercent = -1;

        public void FtpRequested()
        {
            Method = ParamLoadMethod.Ftp;
            Stage = ParamLoadStage.FtpOpening;
        }

        /// <summary>MAVFTP's progress: the open's messages have no percentage (-1), the download's do.</summary>
        public void FileProgress(int percent)
        {
            if (percent < 0)
                return;
            Stage = ParamLoadStage.FtpDownloading;
            FilePercent = Math.Min(100, percent);
        }

        public void FileReceived(int count)
        {
            Total = count;
            Received = count;
            Stage = ParamLoadStage.Done;
        }

        public void ListRequested()
        {
            Method = ParamLoadMethod.List;
            Stage = ParamLoadStage.Listing;
        }

        /// <summary>A parameter in: how many are in and how many the vehicle has.</summary>
        public void Got(int received, int total)
        {
            Total = total;
            Received = received;
        }

        public void Finished(int count)
        {
            Received = count;
            Stage = ParamLoadStage.Done;
        }

        /// <summary>How far the download is, 0 to 100; -1 while there is nothing to measure it by.</summary>
        public int Percent()
        {
            switch (Stage)
            {
                case ParamLoadStage.Done:
                    return 100;
                case ParamLoadStage.FtpDownloading:
                    return FilePercent;
                case ParamLoadStage.Listing:
                    int total = Total;
                    return total > 0 ? Math.Min(100, Received * 100 / total) : -1;
                default:
                    return -1;
            }
        }

        /// <summary>A time as the window shows it: 0.4 s, 12 s, 1:05.</summary>
        public static string Duration(double seconds)
        {
            if (seconds < 0)
                seconds = 0;
            if (seconds < 10)
                return seconds.ToString("0.0") + " s";
            if (seconds < 60)
                return ((int) seconds) + " s";
            int whole = (int) seconds;
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }
    }

    /// <summary>A progress window that can show a parameter download as it goes.</summary>
    public interface IParamLoadReporter
    {
        /// <summary>Shows <paramref name="progress"/>; the first download handed over is the one shown.</summary>
        void Track(ParamLoadProgress progress);
    }
}
