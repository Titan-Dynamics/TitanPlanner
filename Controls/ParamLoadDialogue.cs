using log4net;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The window over a connect and a parameter download (MAVLinkInterface's progress window). While parameters come
    /// it shows the vehicle (firmware, board, frame), how far it has got, and four numbers: parameters in of how many,
    /// how the list is coming (MAVFTP's file or streamed), the link's quality and the time taken. A failure stays on
    /// screen with what went wrong in place of the vehicle. Over a connect it is the same window: what the connect
    /// says and a bar.
    /// The work runs as it does under ProgressReporterDialogue: on a pool thread, cancelled through doWorkArgs, the
    /// window closing itself when it is done.
    /// </summary>
    public class ParamLoadDialogue : Form, IProgressReporterDialogue, IParamLoadReporter
    {
        private const int Pad = 18;
        private const int Wide = 470;
        private const int ShortHeight = 108;
        private const int TallHeight = 140;

        private static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
        private static readonly Color Bad = Color.FromArgb(0xF0, 0x34, 0x4E);

        private readonly object locker = new object();
        private readonly System.Windows.Forms.Timer timer;
        private readonly LinkLabel details;
        private readonly Font titleFont = new Font("Segoe UI", 12F, FontStyle.Bold);
        private readonly Font bodyFont = new Font("Segoe UI", 9F);
        private readonly Font smallFont = new Font("Segoe UI", 8.25F);
        private readonly Font valueFont = new Font("Segoe UI Semibold", 10F);

        private int progress = -1;
        private string status = "";
        private ParamLoadProgress load;
        private bool tall;
        private volatile Thread worker;
        private Exception workerException;
        private string error;
        private bool finished;
        private double? stopped;
        private bool mayClose;
        private DateTime cancelAsked;

        public event MissionPlanner.Utilities.DoWorkEventHandler DoWork;

        public ProgressWorkerEventArgs doWorkArgs { get; set; }

        /// <param name="parameters">
        /// Opens tall, for a parameter download; a connect opens short, and grows if it goes on to download them.
        /// </param>
        public ParamLoadDialogue(bool parameters)
        {
            doWorkArgs = new ProgressWorkerEventArgs();
            tall = parameters;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.UserPaint, true);
            AutoScaleMode = AutoScaleMode.None;
            Font = bodyFont;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            // the X cancels while the work runs, and closes once it has stopped
            ControlBox = true;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(Wide, tall ? TallHeight : ShortHeight);
            KeyPreview = true;

            details = new LinkLabel {Text = "Details...", AutoSize = true, Visible = false};
            details.LinkClicked += (s, e) => ShowDetails();
            Controls.Add(details);

            // the bar's sweep and the counts; drawing this window is cheap
            timer = new System.Windows.Forms.Timer {Interval = 50};
            timer.Tick += (s, e) => Tick();
        }

        private static Color Accent => ThemeManager.ButBG.A == 0 ? Color.FromArgb(0xFF, 0x91, 0x1B) : ThemeManager.ButBG;

        private Color Dim => Blend(BackColor, ForeColor, 0.62);

        private Color Faint => Blend(BackColor, ForeColor, 0.14);

        private static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

        /// <summary>Called at setup: runs the work on a pool thread and shows this window until it is done.</summary>
        public void RunBackgroundOperationAsync()
        {
            ThreadPool.QueueUserWorkItem(RunBackgroundOperation);
            ShowDialog(OwnerWindow());
        }

        /// <summary>
        /// The main window, which this one is kept above while it holds the input. Without an owner, Windows takes
        /// whatever window is active, none when another program has the focus (as when the USB auto-connect starts a
        /// connect), and the main window can then come up over this one, live but taking no input, with what holds it
        /// out of sight behind.
        /// </summary>
        private IWin32Window OwnerWindow()
        {
            var main = MainV2.instance;
            if (main == null || main.IsDisposed || !main.IsHandleCreated || main.InvokeRequired || !main.Visible)
                return null;
            return main;
        }

        /// <summary>Called from the work's thread.</summary>
        /// <param name="progress">progress in %, -1 means indeterminate</param>
        public void UpdateProgressAndStatus(int progress, string status)
        {
            // once a cancel is asked for, only what the cancel's clean-up says
            if (doWorkArgs.CancelRequested && !doWorkArgs.CancelAcknowledged)
                return;

            lock (locker)
            {
                this.progress = progress;
                this.status = status ?? "";
            }
        }

        public void Track(ParamLoadProgress progress)
        {
            if (progress != null)
                Interlocked.CompareExchange(ref load, progress, null);
        }

        void IProgressReporterDialogue.BeginInvoke(Delegate method)
        {
            BeginInvoke(method);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // the body says what and for which vehicle; the given title ("Getting Params 1") carries only the sysid
            if (tall)
                Text = "Parameters";

            // the theme is on by now
            details.LinkColor = details.ActiveLinkColor = details.VisitedLinkColor = Accent;
            timer.Start();
            Focus();
        }

        private void RunBackgroundOperation(object o)
        {
            worker = Thread.CurrentThread;
            try
            {
                worker.Name = "ParamLoadDialogue background thread";
            }
            catch
            {
                // fails on mono
            }

            // the window first, so what the work says has somewhere to show
            while (!IsHandleCreated && !IsDisposed)
                Thread.Sleep(50);

            // if this window isn't the active one, windows the work opens take the wrong owner
            Post(() =>
            {
                if (!Focused)
                    Focus();
            });
            Thread.Sleep(50);

            try
            {
                log.Info("DoWork");
                DoWork?.Invoke(this);
                log.Info("DoWork Done");
            }
            catch (Exception e)
            {
                if (doWorkArgs.CancelRequested && doWorkArgs.CancelAcknowledged)
                {
                    Post(CloseNow);
                    return;
                }

                Post(() => Fail(doWorkArgs.ErrorMessage, e));
                return;
            }
            finally
            {
                // the pool has the thread back: Tick's abort is for the work only
                worker = null;
            }

            if (doWorkArgs.CancelRequested && doWorkArgs.CancelAcknowledged)
            {
                Post(CloseNow);
                return;
            }

            if (!string.IsNullOrEmpty(doWorkArgs.ErrorMessage))
            {
                Post(() => Fail(doWorkArgs.ErrorMessage, null));
                return;
            }

            if (doWorkArgs.CancelRequested)
            {
                Post(() => Fail("Operation could not cancel", null));
                return;
            }

            // done: a moment at full, then away
            Post(() =>
            {
                finished = true;
                stopped = load?.Elapsed;
                Invalidate();
            });
            Thread.Sleep(load != null ? 300 : 100);
            Post(CloseNow);
        }

        private void Post(Action action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated)
                    BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // the window is gone (ObjectDisposedException is one of these)
            }
        }

        private void CloseNow()
        {
            mayClose = true;
            Close();
        }

        /// <summary>The work stopped short: what it said, kept on screen until the X.</summary>
        private void Fail(string message, Exception exception)
        {
            error = string.IsNullOrEmpty(message)
                ? "There was an unexpected error (" + exception?.Message + ")"
                : message;
            workerException = exception;
            stopped = load?.Elapsed;
            if (load != null)
                Expand();
            details.Visible = exception != null;
            PlaceDetails();
            Invalidate();
        }

        private void CancelOrClose()
        {
            if (error != null || finished)
            {
                CloseNow();
                return;
            }

            if (doWorkArgs.CancelRequested)
                return;
            cancelAsked = DateTime.UtcNow;
            doWorkArgs.CancelRequested = true;
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CancelOrClose();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // the X (or Alt+F4) while the work runs asks it to stop, and the window goes when it has. Asked again
            // straight away (a double click) it waits for the work; a few seconds on, it goes regardless
            if (!mayClose && e.CloseReason == CloseReason.UserClosing && error == null && !finished &&
                (!doWorkArgs.CancelRequested || DateTime.UtcNow - cancelAsked < TimeSpan.FromSeconds(3)))
            {
                e.Cancel = true;
                CancelOrClose();
            }

            base.OnFormClosing(e);
        }

        private void ShowDetails()
        {
            if (workerException == null)
                return;
            var message = workerException.Message + Environment.NewLine + Environment.NewLine +
                          workerException.StackTrace;
            MsgBox.CustomMessageBox.ShowTextBox(message, "Exception Details", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void Tick()
        {
            if (IsDisposed)
                return;

            // a connect that goes on to download the parameters
            if (load != null && !tall)
                Expand();

            // work that can't stop by itself, told to (as ProgressReporterDialogue does)
            var thread = worker;
            if (doWorkArgs.CancelRequested && doWorkArgs.ForceExit && thread != null && thread.IsAlive)
            {
                try
                {
                    thread.Abort();
                }
                catch
                {
                    // gone already
                }
            }

            Invalidate();
        }

        /// <summary>From the connect's height to the download's, about the same middle.</summary>
        private void Expand()
        {
            if (tall)
                return;
            tall = true;
            Text = "Parameters";
            var middle = new Point(Left + Width / 2, Top + Height / 2);
            ClientSize = new Size(Wide, TallHeight);
            Location = new Point(middle.X - Width / 2, middle.Y - Height / 2);
            PlaceDetails();
        }

        /// <summary>A failure's Details link: at the end of the error's line, or under a connect's error.</summary>
        private void PlaceDetails()
        {
            if (tall)
                details.Location = new Point(ClientSize.Width - Pad - details.Width, 42 + (18 - details.Height) / 2);
            else
                details.Location = new Point(Pad + 24, 88);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            using (var back = new SolidBrush(BackColor))
                g.FillRectangle(back, ClientRectangle);

            var current = load;
            if (current != null && tall)
            {
                PaintLoad(g, current);
                return;
            }

            int percent;
            string text;
            lock (locker)
            {
                percent = progress;
                text = status;
            }

            PaintConnect(g, percent, text);
        }

        private void PaintConnect(Graphics g, int percent, string text)
        {
            int width = ClientSize.Width - Pad * 2;
            if (error != null)
            {
                FailedMark(g, Pad, 14 + 13);
                Draw(g, error, titleFont, ForeColor, new Rectangle(Pad + 24, 14, width - 24, 72),
                    TextFormatFlags.WordBreak);
                return;
            }

            Draw(g, Text, titleFont, ForeColor, new Rectangle(Pad, 14, width, 26));
            Draw(g, doWorkArgs.CancelRequested ? "Canceling..." : text, bodyFont, Dim,
                new Rectangle(Pad, 44, width, 36), TextFormatFlags.WordBreak);
            Bar(g, new Rectangle(Pad, 88, width, 6), finished ? 100 : percent, Accent);
        }

        private void PaintLoad(Graphics g, ParamLoadProgress p)
        {
            int width = ClientSize.Width - Pad * 2;
            bool failed = error != null;
            bool done = !failed && (finished || p.Stage == ParamLoadStage.Done);
            int percent = p.Percent();

            // what this is, and how far it has got
            string title = failed ? "Parameter download failed" : done ? "Parameters loaded" : "Loading parameters";
            Draw(g, title, titleFont, ForeColor, new Rectangle(Pad, 14, width - 70, 26));
            if (percent >= 0)
                Draw(g, percent + "%", titleFont, failed ? Bad : Accent, new Rectangle(Pad + width - 70, 14, 70, 26),
                    TextFormatFlags.Right);

            // which vehicle, or what went wrong
            if (failed)
                Draw(g, error, bodyFont, Bad,
                    new Rectangle(Pad, 42, width - (details.Visible ? details.Width + 8 : 0), 18));
            else
                Draw(g, doWorkArgs.CancelRequested ? "Canceling..." : Identity(p), bodyFont, Dim,
                    new Rectangle(Pad, 42, width, 18));
            Bar(g, new Rectangle(Pad, 68, width, 6), failed && percent < 0 ? 0 : percent, failed ? Bad : Accent);

            // the numbers
            int quality = LinkQuality(p.Vehicle);
            Stat(g, 0, "Parameters",
                p.Total > 0 ? string.Format("{0:N0} / {1:N0}", p.Received, p.Total) : "—", ForeColor);
            Stat(g, 1, "Method",
                p.Method == ParamLoadMethod.Ftp ? "MAVFTP" : p.Method == ParamLoadMethod.List ? "Streamed" : "—",
                ForeColor);
            Stat(g, 2, "Link", quality >= 0 ? quality + "%" : "—", quality >= 0 && quality < 80 ? Bad : ForeColor);
            Stat(g, 3, "Time", ParamLoadProgress.Duration(stopped ?? p.Elapsed), ForeColor);
        }

        /// <summary>The firmware, board and frame as the banner gives them, and the vehicle's id.</summary>
        private static string Identity(ParamLoadProgress p)
        {
            var parts = new List<string>();
            var vehicle = p.Vehicle;
            if (vehicle != null)
            {
                var version = vehicle.VersionString;
                if (!string.IsNullOrWhiteSpace(version))
                    parts.Add(version.Trim());
                // the board's name, then its serial number
                var serial = vehicle.SerialString;
                if (!string.IsNullOrWhiteSpace(serial))
                    parts.Add(serial.Trim().Split(' ')[0]);
                var frame = vehicle.FrameString;
                if (!string.IsNullOrWhiteSpace(frame))
                    parts.Add(frame.Replace("Frame:", "").Trim());
            }

            parts.Add(p.Compid == 1 ? "Vehicle " + p.Sysid : "Vehicle " + p.Sysid + ", component " + p.Compid);
            return string.Join("  ·  ", parts);
        }

        /// <summary>The share of the vehicle's recent packets that arrived, as the link quality; -1 too soon to say.</summary>
        private static int LinkQuality(MAVState vehicle)
        {
            if (vehicle == null)
                return -1;
            float lost = Math.Max(0, vehicle.packetslost), arrived = Math.Max(0, vehicle.packetsnotlost);
            if (lost + arrived < 10)
                return -1;
            return (int) Math.Round(100 * arrived / (lost + arrived));
        }

        private void Stat(Graphics g, int column, string label, string value, Color color)
        {
            int width = (ClientSize.Width - Pad * 2) / 4;
            int x = Pad + column * width;
            Draw(g, label, smallFont, Dim, new Rectangle(x, 88, width - 8, 15));
            Draw(g, value, valueFont, color, new Rectangle(x, 104, width - 8, 20));
        }

        private static void Draw(Graphics g, string text, Font font, Color color, Rectangle bounds,
            TextFormatFlags flags = TextFormatFlags.Left)
        {
            flags |= TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            if ((flags & TextFormatFlags.WordBreak) == 0)
                flags |= TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, text ?? "", font, bounds, color, flags);
        }

        /// <summary>The bar: filled to <paramref name="percent"/>, or with a piece sweeping across while it is -1.</summary>
        private void Bar(Graphics g, Rectangle bounds, int percent, Color fill)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var track = Pill(bounds))
            using (var faint = new SolidBrush(Faint))
            using (var brush = new SolidBrush(fill))
            {
                g.FillPath(faint, track);
                if (percent >= 0)
                {
                    int width = (int) Math.Round(bounds.Width * Math.Min(100, percent) / 100.0);
                    if (width > 0)
                        using (var part = Pill(new Rectangle(bounds.X, bounds.Y, Math.Max(width, bounds.Height),
                                   bounds.Height)))
                            g.FillPath(brush, part);
                }
                else
                {
                    // eased, so it slows at the ends as it turns round
                    double t = (Environment.TickCount & int.MaxValue) % 1600 / 1600.0;
                    t = t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);
                    int piece = bounds.Width / 4;
                    int x = bounds.X - piece + (int) ((bounds.Width + piece) * t);
                    g.SetClip(track, CombineMode.Intersect);
                    using (var part = Pill(new Rectangle(x, bounds.Y, piece, bounds.Height)))
                        g.FillPath(brush, part);
                }
            }

            g.Restore(state);
        }

        private static GraphicsPath Pill(Rectangle r)
        {
            var path = new GraphicsPath();
            int d = r.Height;
            if (r.Width <= d)
            {
                path.AddEllipse(r);
                return path;
            }

            path.AddArc(r.X, r.Y, d, d, 90, 180);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 180);
            path.CloseFigure();
            return path;
        }

        /// <summary>A failed connect's mark: an exclamation in a red circle.</summary>
        private static void FailedMark(Graphics g, int x, int middle)
        {
            var state = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var fill = new SolidBrush(Bad))
                g.FillEllipse(fill, x, middle - 7, 14, 14);
            using (var pen = new Pen(Color.White, 1.8f) {StartCap = LineCap.Round, EndCap = LineCap.Round})
                g.DrawLine(pen, x + 7f, middle - 3.6f, x + 7f, middle + 0.6f);
            using (var dot = new SolidBrush(Color.White))
                g.FillEllipse(dot, x + 6f, middle + 2.2f, 2f, 2f);
            g.Restore(state);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                timer.Dispose();
                titleFont.Dispose();
                smallFont.Dispose();
                valueFont.Dispose();
            }

            base.Dispose(disposing);

            // the window's own font, so after the window
            if (disposing)
                bodyFont.Dispose();
        }
    }
}
