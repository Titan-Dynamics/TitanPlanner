using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using DirectShowLib;
using MissionPlanner.Utilities;
using WebCamService;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The DATA screen's Video tab. One source at a time plays behind the HUD: a capture device, an RTSP or MJPEG
    /// stream, or a GStreamer pipeline. The tab starts and stops it, says how it is playing, and holds the HUD overlay,
    /// OSD color and HUD recording.
    /// </summary>
    public class FlightPlannerVideoOptions : UserControl
    {
        private enum Source
        {
            Camera,
            Rtsp,
            Mjpeg,
            Pipeline,
        }

        private static readonly Dictionary<Source, string> SourceNames = new Dictionary<Source, string>
        {
            { Source.Camera, "Capture device" },
            { Source.Rtsp, "RTSP stream" },
            { Source.Mjpeg, "MJPEG stream" },
            { Source.Pipeline, "GStreamer pipeline" },
        };

        private const string RtspPipeline =
            "rtspsrc location={0} udp-reconnect=1 timeout=0 do-retransmission=false ! application/x-rtp ! decodebin3 ! queue max-size-buffers=1 leaky=2 ! videoconvert ! video/x-raw,format=BGRA ! appsink name=outsink sync=false";

        private const string DefaultPipeline =
            "videotestsrc ! video/x-raw, width=1280, height=720, framerate=30/1 ! videoconvert ! video/x-raw,format=BGRA ! appsink name=outsink";

        private const string HereLinkUrl = "rtsp://{0}:8554/fpv_stream";
        private const string DefaultHereLinkIp = "192.168.43.1";

        // a playing source without a frame for this long reads as having no video
        private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(3);

        // the dropdowns, text boxes and buttons beside them are all this tall: a dropdown in this font
        private static readonly Font InputFont = new Font("Microsoft Sans Serif", 9.75F);
        private const int MinBoxWidth = 350;
        private const int BoxMargin = 6;
        // above and below each control, so neighbouring rows have twice this between them
        private const int RowGap = 5;
        // the status and the Start/Stop button across the top
        private const int TopHeight = 34;
        // the corners of the status and the Start/Stop and Record buttons
        private const int CornerRadius = 6;

        private int _rowHeight;
        private FlowLayoutPanel _boxes;
        private GroupBox grpSource;
        private TableLayoutPanel tableSource;
        private GroupBox grpDisplay;
        private TableLayoutPanel tableDisplay;
        private GroupBox grpRecording;
        private TableLayoutPanel tableRecording;

        private ComboBox cmbSource;
        private Label lblDevice;
        private ComboBox cmbDevice;
        private MyButton btnRefreshDevices;
        private Label lblFormat;
        private ComboBox cmbFormat;
        private Label lblRtspUrl;
        private TextBox txtRtspUrl;
        private InputHost hostRtspUrl;
        private MyButton btnHereLink;
        private Label lblMjpegUrl;
        private TextBox txtMjpegUrl;
        private InputHost hostMjpegUrl;
        private Label lblPipeline;
        private TextBox txtPipeline;
        private InputHost hostPipeline;
        private Label lblPipelineHint;
        private TableLayoutPanel _top;
        private StatusIndicator _status;
        private StartStopButton btnStartStop;
        private CheckBox chkHudShow;
        private Label lblOsdColor;
        private ComboBox cmbOsdColor;
        private StartStopButton btnRecord;
        private MyButton btnOpenFolder;
        private Label lblRecord;

        private readonly System.Windows.Forms.Timer _statusTimer = new System.Windows.Forms.Timer { Interval = 500 };

        // the sources in the dropdown, in its order (no capture devices under Mono)
        private readonly List<Source> _sources = new List<Source>();

        private bool startup = true;
        private bool _devicesListed;

        // what the tab started and when, and why nothing is playing when that wasn't the Stop button
        private Source? _started;
        private DateTime _startedAt;
        private string _error;
        private bool _wasPlaying;

        // the latest frame from whichever source is playing, written on that source's thread
        private long _lastFrameTicks;
        private volatile int _frameWidth;
        private volatile int _frameHeight;

        // the capture device whose frames are counted
        private Capture _watchedCam;

        public FlightPlannerVideoOptions()
        {
            InitializeComponent();

            foreach (var source in SourceNames.Keys)
            {
                if (source == Source.Camera && MainV2.MONO)
                    continue;
                _sources.Add(source);
                cmbSource.Items.Add(SourceNames[source]);
            }

            cmbSource.SelectedIndexChanged += CmbSource_SelectedIndexChanged;
            cmbDevice.SelectedIndexChanged += CmbDevice_SelectedIndexChanged;
            btnRefreshDevices.Click += (sender, e) => ListDevices();
            btnHereLink.Click += BtnHereLink_Click;
            btnStartStop.Click += BtnStartStop_Click;
            chkHudShow.CheckedChanged += ChkHudShow_CheckedChanged;
            cmbOsdColor.DrawItem += CmbOsdColor_DrawItem;
            cmbOsdColor.SelectedIndexChanged += CmbOsdColor_SelectedIndexChanged;
            btnRecord.Click += BtnRecord_Click;
            btnOpenFolder.Click += BtnOpenFolder_Click;
            _statusTimer.Tick += (sender, e) => UpdateStatus();
            VisibleChanged += FlightPlannerVideoOptions_VisibleChanged;

            // the streams' frames, to say how they are playing
            GCSViews.FlightData.hudGStreamer.OnNewImage += (sender, image) =>
            {
                if (image != null)
                    NoteFrame(image.Width, image.Height);
            };
            CaptureMJPEG.onNewImage += (sender, image) =>
            {
                if (image != null)
                    NoteFrame(image.Width, image.Height);
            };
        }

        private void InitializeComponent()
        {
            SuspendLayout();

            cmbSource = NewComboBox();
            _rowHeight = cmbSource.PreferredHeight;

            // Video Source: the source, its settings, and starting it
            tableSource = NewTable(3);
            AddRow(tableSource, NewLabel("Source"), cmbSource);

            lblDevice = NewLabel("Device");
            cmbDevice = NewComboBox();
            btnRefreshDevices = NewButton("Refresh", 100);
            AddRow(tableSource, lblDevice, cmbDevice, btnRefreshDevices);

            lblFormat = NewLabel("Format");
            cmbFormat = NewComboBox();
            AddRow(tableSource, lblFormat, cmbFormat);

            lblRtspUrl = NewLabel("URL");
            hostRtspUrl = NewInput(out txtRtspUrl, false);
            btnHereLink = NewButton("HereLink", 100);
            AddRow(tableSource, lblRtspUrl, hostRtspUrl, btnHereLink);

            lblMjpegUrl = NewLabel("URL");
            hostMjpegUrl = NewInput(out txtMjpegUrl, false);
            AddRow(tableSource, lblMjpegUrl, hostMjpegUrl);

            lblPipeline = NewLabel("Pipeline");
            hostPipeline = NewInput(out txtPipeline, true);
            AddRow(tableSource, lblPipeline, hostPipeline);

            lblPipelineHint = NewLabel("End it with ! videoconvert ! video/x-raw,format=BGRA ! appsink name=outsink");
            AddRow(tableSource, null, lblPipelineHint);

            grpSource = NewBox("Video Source", tableSource);

            // Display: what the HUD draws over the video
            chkHudShow = new CheckBox { AutoSize = true, Text = "HUD overlay", UseVisualStyleBackColor = true };
            tableDisplay = NewTable(2);
            AddRow(tableDisplay, chkHudShow);
            lblOsdColor = NewLabel("OSD Color");
            cmbOsdColor = NewComboBox();
            cmbOsdColor.DrawMode = DrawMode.OwnerDrawFixed;
            cmbOsdColor.ItemHeight = _rowHeight - 6;
            AddRow(tableDisplay, lblOsdColor, cmbOsdColor);
            grpDisplay = NewBox("Display", tableDisplay);

            // Recording: the HUD with its video, to the log folder
            btnRecord = new StartStopButton("Record HUD", "Stop Recording", recordMark: true)
            {
                Anchor = AnchorStyles.Left,
                Size = new Size(170, _rowHeight),
            };
            btnOpenFolder = NewButton("Open Folder", 120);
            tableRecording = NewTable(2);
            AddRow(tableRecording, btnRecord, btnOpenFolder);
            lblRecord = NewLabel("");
            AddRow(tableRecording, lblRecord);
            grpRecording = NewBox("Recording", tableRecording);

            // across the top: how the video is, and starting or stopping it
            _status = new StatusIndicator
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Height = TopHeight,
                Margin = new Padding(0, 0, 8, 0),
            };
            btnStartStop = new StartStopButton("Start", "Stop", recordMark: false)
            {
                Margin = Padding.Empty,
                Size = new Size(150, TopHeight),
            };
            _top = new TableLayoutPanel
            {
                ColumnCount = 2,
                Height = TopHeight,
                Margin = new Padding(BoxMargin / 2, BoxMargin / 2, BoxMargin / 2, BoxMargin),
                RowCount = 1,
            };
            _top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _top.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _top.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            _top.Controls.Add(_status, 0, 0);
            _top.Controls.Add(btnStartStop, 1, 0);

            _boxes = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Top,
                Padding = new Padding(4),
            };
            _boxes.Controls.AddRange(new Control[] { _top, grpSource, grpDisplay, grpRecording });
            _boxes.SetFlowBreak(_top, true);
            _boxes.SetFlowBreak(grpSource, true);

            AutoScroll = true;
            Controls.Add(_boxes);
            Name = "FlightPlannerVideoOptions";
            Size = new Size(760, 330);
            Resize += (sender, e) => LayoutBoxes();
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>
        /// Sizes the boxes as the Actions tab does: the status and Start/Stop across the top, Video Source a row of its
        /// own, Display and Recording side by side when two fit, each as tall as its rows.
        /// </summary>
        private void LayoutBoxes()
        {
            if (_boxes == null)
                return;

            int available = ClientSize.Width - _boxes.Padding.Horizontal;
            bool sideBySide = available >= 2 * (MinBoxWidth + BoxMargin);
            int full = Math.Max(MinBoxWidth, available - BoxMargin);
            int half = sideBySide ? available / 2 - BoxMargin : full;

            // long text wraps to the width it has
            lblPipelineHint.MaximumSize = new Size(Math.Max(100, full - 120), 0);
            lblRecord.MaximumSize = new Size(Math.Max(100, half - 30), 0);

            _boxes.SuspendLayout();
            _top.Width = full;
            FitToRows(grpSource, tableSource, full);
            int display = FitToRows(grpDisplay, tableDisplay, half);
            int recording = FitToRows(grpRecording, tableRecording, half);
            if (sideBySide)
                grpDisplay.Height = grpRecording.Height = Math.Max(display, recording);
            _boxes.ResumeLayout(true);
        }

        // sizes the box to the width and to the height of its rows at that width, and returns that height
        private static int FitToRows(GroupBox box, TableLayoutPanel rows, int width)
        {
            box.Width = width;
            var inner = box.DisplayRectangle;
            box.Height += rows.GetPreferredSize(new Size(inner.Width, 0)).Height - inner.Height;
            return box.Height;
        }

        private static GroupBox NewBox(string title, TableLayoutPanel rows)
        {
            var box = new GroupBox { Margin = new Padding(BoxMargin / 2), Padding = new Padding(6), Text = title };
            box.Controls.Add(rows);
            return box;
        }

        // a label column, a column that takes the width left, and one for a button beside it
        private static TableLayoutPanel NewTable(int columns)
        {
            var table = new TableLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = columns,
                Dock = DockStyle.Top,
            };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            if (columns > 2)
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            return table;
        }

        // a row of the table: one control a column (null leaves it empty), the last spanning the columns after it
        private static void AddRow(TableLayoutPanel table, params Control[] cells)
        {
            int row = table.RowCount++;
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            for (int column = 0; column < cells.Length; column++)
            {
                var cell = cells[column];
                if (cell == null)
                    continue;

                cell.Margin = new Padding(column == 0 ? 3 : 6, RowGap, 3, RowGap);
                table.Controls.Add(cell, column, row);
                if (column == cells.Length - 1)
                    table.SetColumnSpan(cell, table.ColumnCount - column);
            }
        }

        private static Label NewLabel(string text)
        {
            return new Label { Anchor = AnchorStyles.Left, AutoSize = true, Text = text };
        }

        private static ComboBox NewComboBox()
        {
            return new ComboBox
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Font = InputFont,
                FormattingEnabled = true,
            };
        }

        private InputHost NewInput(out TextBox box, bool multiline)
        {
            box = new TextBox { Font = InputFont, Multiline = multiline };
            if (multiline)
                box.ScrollBars = ScrollBars.Vertical;
            return new InputHost(box, multiline ? _rowHeight * 5 / 2 : _rowHeight)
            {
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
            };
        }

        private MyButton NewButton(string text, int width)
        {
            return new MyButton
            {
                Anchor = AnchorStyles.Left,
                Size = new Size(width, _rowHeight),
                Text = text,
                TextColorNotEnabled = Color.FromArgb(64, 87, 4),
                UseVisualStyleBackColor = true,
            };
        }

        /// <summary>
        /// A text box as tall as the dropdowns, its text centred. The theme takes text boxes' borders off, which leaves
        /// them only a line of text tall; this draws the box's background around it.
        /// </summary>
        private class InputHost : Panel
        {
            private const int Inset = 5;

            public readonly TextBox Box;

            public InputHost(TextBox box, int height)
            {
                Box = box;
                box.BorderStyle = BorderStyle.None;
                box.BackColorChanged += (sender, e) => Invalidate();
                Height = height;
                Controls.Add(box);
            }

            // always the text box's, whatever the theme gives the panel
            public override Color BackColor
            {
                get => Box?.BackColor ?? base.BackColor;
                set => base.BackColor = value;
            }

            protected override void OnLayout(LayoutEventArgs levent)
            {
                base.OnLayout(levent);
                if (Box.Multiline)
                    Box.Bounds = new Rectangle(Inset, Inset, Width - 2 * Inset, Height - 2 * Inset);
                else
                    Box.Bounds = new Rectangle(Inset, (Height - Box.Height) / 2, Width - 2 * Inset, Box.Height);
            }

            protected override void OnClick(EventArgs e)
            {
                base.OnClick(e);
                Box.Focus();
            }
        }

        private enum VideoState
        {
            Off,
            Waiting,
            Playing,
            Error,
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        /// <summary>
        /// How the video is: a dot in its state's color, and a line saying what is playing or why nothing is.
        /// </summary>
        private class StatusIndicator : Control
        {
            private VideoState _state = VideoState.Off;

            public StatusIndicator()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                Text = "No video started";
            }

            public void SetState(VideoState state, string text)
            {
                if (state == _state && text == Text)
                    return;

                _state = state;
                Text = text;
                Invalidate();
            }

            private static Color StateColor(VideoState state)
            {
                switch (state)
                {
                    case VideoState.Playing:
                        return Color.FromArgb(60, 190, 100);
                    case VideoState.Waiting:
                        return Color.FromArgb(235, 170, 50);
                    case VideoState.Error:
                        return Color.FromArgb(225, 75, 70);
                    default:
                        return Color.FromArgb(130, 130, 130);
                }
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent?.BackColor ?? BackColor);

                using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
                using (var fill = new SolidBrush(ThemeManager.ControlBGColor))
                    g.FillPath(fill, path);

                // the dot, with a soft ring while video plays
                var color = StateColor(_state);
                int dot = Height / 3;
                var dotBounds = new Rectangle(Height / 2 - dot / 2, (Height - dot) / 2, dot, dot);
                if (_state == VideoState.Playing)
                {
                    var ring = Rectangle.Inflate(dotBounds, dot / 3, dot / 3);
                    using (var glow = new SolidBrush(Color.FromArgb(70, color)))
                        g.FillEllipse(glow, ring);
                }

                using (var brush = new SolidBrush(color))
                    g.FillEllipse(brush, dotBounds);

                int textLeft = dotBounds.Right + Height / 3;
                TextRenderer.DrawText(g, Text, Font,
                    new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft - Height / 2), Height), ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                    TextFormatFlags.NoPadding);
            }
        }

        /// <summary>
        /// A button that starts something in green and stops it in red: a mark (play or record, a square to stop) beside
        /// its words, lighter under the pointer, darker pressed.
        /// </summary>
        private class StartStopButton : Control
        {
            private static readonly Color StartColor = Color.FromArgb(46, 160, 87);
            private static readonly Color StopColor = Color.FromArgb(205, 65, 60);

            private readonly string _startText;
            private readonly string _stopText;
            private readonly bool _recordMark;
            private bool _running;
            private bool _hover;
            private bool _pressed;

            public StartStopButton(string startText, string stopText, bool recordMark)
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
                _startText = startText;
                _stopText = stopText;
                _recordMark = recordMark;
                Cursor = Cursors.Hand;
                Font = new Font("Microsoft Sans Serif", 9.75F, FontStyle.Bold);
            }

            /// <summary>
            /// Whether what it starts is running, so that it offers to stop it.
            /// </summary>
            public bool Running
            {
                get => _running;
                set
                {
                    if (_running == value)
                        return;
                    _running = value;
                    Invalidate();
                }
            }

            protected override void OnMouseEnter(EventArgs e)
            {
                _hover = true;
                Invalidate();
                base.OnMouseEnter(e);
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                _hover = false;
                _pressed = false;
                Invalidate();
                base.OnMouseLeave(e);
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                {
                    _pressed = true;
                    Invalidate();
                }

                base.OnMouseDown(e);
            }

            protected override void OnMouseUp(MouseEventArgs e)
            {
                _pressed = false;
                Invalidate();
                base.OnMouseUp(e);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Parent?.BackColor ?? BackColor);

                var color = _running ? StopColor : StartColor;
                if (_pressed)
                    color = ControlPaint.Dark(color, 0.05f);
                else if (_hover)
                    color = ControlPaint.Light(color, 0.25f);

                using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), CornerRadius))
                using (var fill = new SolidBrush(color))
                    g.FillPath(fill, path);

                // the mark and the words, centred together
                string text = _running ? _stopText : _startText;
                var textSize = TextRenderer.MeasureText(g, text, Font, Size.Empty, TextFormatFlags.NoPadding);
                int mark = Height * 2 / 5;
                const int gap = 8;
                int x = (Width - (mark + gap + textSize.Width)) / 2;
                int y = (Height - mark) / 2;
                using (var white = new SolidBrush(Color.White))
                {
                    if (_running)
                        g.FillRectangle(white, x, y, mark, mark);
                    else if (_recordMark)
                        g.FillEllipse(white, x, y, mark, mark);
                    else
                        g.FillPolygon(white, new[]
                        {
                            new Point(x, y), new Point(x + mark, y + mark / 2), new Point(x, y + mark),
                        });
                }

                TextRenderer.DrawText(g, text, Font, new Point(x + mark + gap, (Height - textSize.Height) / 2),
                    Color.White, TextFormatFlags.NoPadding);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _statusTimer.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Activate();
        }

        public void Activate()
        {
            startup = true;

            cmbOsdColor.DataSource = Enum.GetNames(typeof(KnownColor));
            var hudcolor = Settings.Instance["hudcolor"];
            if (hudcolor != null)
            {
                var index = cmbOsdColor.Items.IndexOf(hudcolor);
                if (index >= 0)
                    cmbOsdColor.SelectedIndex = index;
            }

            txtRtspUrl.Text = Settings.Instance["rtsp_url"] ?? "";
            txtMjpegUrl.Text = Settings.Instance["mjpeg_url"] ?? "";
            txtPipeline.Text = Settings.Instance["gstreamer_url"] ?? DefaultPipeline;
            chkHudShow.Checked = Settings.Instance.GetBoolean("CHK_hudshow", GCSViews.FlightData.myhud.hudon);

            // the source last used, a capture device at first
            if (!Enum.TryParse(Settings.Instance["video_source"], out Source source) || !_sources.Contains(source))
                source = _sources[0];
            cmbSource.SelectedIndex = _sources.IndexOf(source);
            ShowSourceFields();

            startup = false;
            UpdateStatus();
        }

        private void FlightPlannerVideoOptions_VisibleChanged(object sender, EventArgs e)
        {
            if (Visible)
            {
                // the HUD overlay can be changed in the planner settings too
                chkHudShow.Checked = Settings.Instance.GetBoolean("CHK_hudshow", GCSViews.FlightData.myhud.hudon);
                UpdateStatus();
                _statusTimer.Start();
            }
            else
            {
                _statusTimer.Stop();
            }
        }

        #region Source

        private Source SelectedSource => _sources[Math.Max(0, cmbSource.SelectedIndex)];

        private void CmbSource_SelectedIndexChanged(object sender, EventArgs e)
        {
            ShowSourceFields();
            if (!startup)
                Settings.Instance["video_source"] = SelectedSource.ToString();
        }

        // only the selected source's fields show
        private void ShowSourceFields()
        {
            var source = SelectedSource;
            tableSource.SuspendLayout();
            SetVisible(source == Source.Camera, lblDevice, cmbDevice, btnRefreshDevices, lblFormat, cmbFormat);
            SetVisible(source == Source.Rtsp, lblRtspUrl, hostRtspUrl, btnHereLink);
            SetVisible(source == Source.Mjpeg, lblMjpegUrl, hostMjpegUrl);
            SetVisible(source == Source.Pipeline, lblPipeline, hostPipeline, lblPipelineHint);
            tableSource.ResumeLayout(true);
            LayoutBoxes();

            // listing the devices takes a moment, so it waits until they are wanted
            if (source == Source.Camera && !_devicesListed)
                ListDevices();
        }

        private static void SetVisible(bool visible, params Control[] controls)
        {
            foreach (var control in controls)
                control.Visible = visible;
        }

        private void BtnHereLink_Click(object sender, EventArgs e)
        {
            txtRtspUrl.Text = string.Format(HereLinkUrl, Settings.Instance["herelinkip"] ?? DefaultHereLinkIp);
        }

        private void ListDevices()
        {
            if (MainV2.MONO)
                return;

            _devicesListed = true;
            try
            {
                var capt = new Capture();
                cmbDevice.DataSource = WebCamService.Capture.getDevices();
                capt.Dispose();

                var device = Settings.Instance.GetInt32("video_device");
                if (device >= 0 && device < cmbDevice.Items.Count)
                    cmbDevice.SelectedIndex = device;
            }
            catch
            {
            }
        }

        // the device's formats, with the one last used chosen
        private void CmbDevice_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (MainV2.MONO)
                return;

            try
            {
                int hr;
                int count;
                int size;
                object o;
                IBaseFilter capFilter = null;
                ICaptureGraphBuilder2 capGraph = null;
                AMMediaType media = null;
                VideoInfoHeader v;
                VideoStreamConfigCaps c;
                var modes = new List<GCSBitmapInfo>();

                capGraph = (ICaptureGraphBuilder2)new CaptureGraphBuilder2();
                var m_FilterGraph = (IFilterGraph2)new FilterGraph();

                DsDevice[] capDevices;
                capDevices = DsDevice.GetDevicesOfCat(FilterCategory.VideoInputDevice);

                if (cmbDevice.SelectedIndex < 0 || cmbDevice.SelectedIndex >= capDevices.Length)
                    return;

                hr = m_FilterGraph.AddSourceFilterForMoniker(capDevices[cmbDevice.SelectedIndex].Mon, null,
                    "Video input", out capFilter);
                try
                {
                    DsError.ThrowExceptionForHR(hr);
                }
                catch
                {
                    return;
                }

                hr = capGraph.FindInterface(PinCategory.Capture, MediaType.Video, capFilter, typeof(IAMStreamConfig).GUID, out o);
                DsError.ThrowExceptionForHR(hr);

                var videoStreamConfig = o as IAMStreamConfig;
                if (videoStreamConfig == null)
                    return;

                hr = videoStreamConfig.GetNumberOfCapabilities(out count, out size);
                DsError.ThrowExceptionForHR(hr);
                var TaskMemPointer = Marshal.AllocCoTaskMem(size);
                for (var i = 0; i < count; i++)
                {
                    hr = videoStreamConfig.GetStreamCaps(i, out media, TaskMemPointer);
                    v = (VideoInfoHeader)Marshal.PtrToStructure(media.formatPtr, typeof(VideoInfoHeader));
                    c = (VideoStreamConfigCaps)Marshal.PtrToStructure(TaskMemPointer, typeof(VideoStreamConfigCaps));
                    modes.Add(new GCSBitmapInfo(v.BmiHeader.Width, v.BmiHeader.Height, c.MaxFrameInterval,
                        c.VideoStandard.ToString(), media));
                }
                Marshal.FreeCoTaskMem(TaskMemPointer);
                DsUtils.FreeAMMediaType(media);

                cmbFormat.DataSource = modes;

                var format = Settings.Instance.GetInt32("video_options");
                if (cmbDevice.SelectedIndex == Settings.Instance.GetInt32("video_device") && format >= 0 &&
                    format < cmbFormat.Items.Count)
                    cmbFormat.SelectedIndex = format;
            }
            catch
            {
            }
        }

        #endregion

        #region Start and stop

        private static bool IsPlaying =>
            MainV2.cam != null || GCSViews.FlightData.IsHudGStreamerRunning || CaptureMJPEG.IsRunning;

        private void BtnStartStop_Click(object sender, EventArgs e)
        {
            if (IsPlaying)
                StopVideo();
            else
                StartVideo();

            UpdateStatus();
        }

        /// <summary>
        /// Plays the selected source, stopping whatever was playing first, from any source.
        /// </summary>
        private void StartVideo()
        {
            StopVideo();

            var source = SelectedSource;
            Settings.Instance["video_source"] = source.ToString();
            GCSViews.FlightData.myhud.hudon = chkHudShow.Checked;
            _startedAt = DateTime.UtcNow;

            switch (source)
            {
                case Source.Camera:
                    StartCamera();
                    break;

                case Source.Rtsp:
                    var rtsp = WithScheme(txtRtspUrl.Text, "rtsp://");
                    if (rtsp == null)
                    {
                        _error = "Enter the stream's RTSP URL";
                        return;
                    }

                    txtRtspUrl.Text = rtsp;
                    Settings.Instance["rtsp_url"] = rtsp;
                    StartPipeline(string.Format(RtspPipeline, rtsp));
                    break;

                case Source.Mjpeg:
                    var mjpeg = WithScheme(txtMjpegUrl.Text, "http://");
                    if (mjpeg == null)
                    {
                        _error = "Enter the stream's URL";
                        return;
                    }

                    txtMjpegUrl.Text = mjpeg;
                    Settings.Instance["mjpeg_url"] = mjpeg;
                    CaptureMJPEG.URL = mjpeg;
                    CaptureMJPEG.runAsync();
                    break;

                case Source.Pipeline:
                    var pipeline = txtPipeline.Text.Trim();
                    if (pipeline == "")
                    {
                        _error = "Enter a GStreamer pipeline";
                        return;
                    }

                    Settings.Instance["gstreamer_url"] = pipeline;
                    StartPipeline(pipeline);
                    break;
            }

            if (_error == null)
                _started = source;
        }

        /// <summary>
        /// Stops whatever video is playing, from any source, and clears it from the HUD.
        /// </summary>
        private void StopVideo()
        {
            if (MainV2.cam != null)
            {
                MainV2.cam.camimage -= GCSViews.FlightData.instance.cam_camimage;
                try
                {
                    MainV2.cam.Dispose();
                }
                catch
                {
                }

                MainV2.cam = null;
            }

            GCSViews.FlightData.StopHudGStreamer();
            CaptureMJPEG.Stop();
            GCSViews.FlightData.myhud.bgimage = null;

            _started = null;
            _error = null;
            _wasPlaying = false;
        }

        // the URL with the scheme added when it was left out, or null without one
        private static string WithScheme(string text, string scheme)
        {
            var url = text.Trim();
            if (url == "")
                return null;
            return url.Contains("://") ? url : scheme + url;
        }

        private void StartPipeline(string pipeline)
        {
            GStreamer.GstLaunch = GStreamer.LookForGstreamer();
            if (!GStreamer.GstLaunchExists)
            {
                GStreamerUI.DownloadGStreamer();
                if (!GStreamer.GstLaunchExists)
                {
                    _error = "GStreamer isn't installed";
                    return;
                }
            }

            try
            {
                GCSViews.FlightData.StartHudGStreamer(pipeline);
            }
            catch (Exception ex)
            {
                _error = ex.Message;
            }
        }

        private void StartCamera()
        {
            if (!(cmbFormat.SelectedItem is GCSBitmapInfo format))
            {
                _error = "Choose a capture device and format";
                return;
            }

            try
            {
                MainV2.cam = new Capture(cmbDevice.SelectedIndex, format.Media);
                MainV2.cam.Start();
                MainV2.cam.camimage += GCSViews.FlightData.instance.cam_camimage;

                Settings.Instance["video_device"] = cmbDevice.SelectedIndex.ToString();
                Settings.Instance["video_options"] = cmbFormat.SelectedIndex.ToString();
            }
            catch (Exception ex)
            {
                // held open by another program, or gone
                if (MainV2.cam != null)
                {
                    try
                    {
                        MainV2.cam.Dispose();
                    }
                    catch
                    {
                    }

                    MainV2.cam = null;
                }

                _error = "The capture device didn't start: " + ex.Message;
            }
        }

        #endregion

        #region Status

        private void NoteFrame(int width, int height)
        {
            _frameWidth = width;
            _frameHeight = height;
            Interlocked.Exchange(ref _lastFrameTicks, DateTime.UtcNow.Ticks);
        }

        // counts the frames of whichever capture device is running, whoever started it
        private void WatchCameraFrames()
        {
            var cam = MainV2.cam;
            if (cam == _watchedCam)
                return;

            if (_watchedCam != null)
                _watchedCam.camimage -= OnCameraFrame;
            if (cam != null)
                cam.camimage += OnCameraFrame;
            _watchedCam = cam;
        }

        private void OnCameraFrame(Image image)
        {
            try
            {
                if (image != null)
                    NoteFrame(image.Width, image.Height);
            }
            catch
            {
                // the HUD drawing the same frame
            }
        }

        private void UpdateStatus()
        {
            WatchCameraFrames();

            bool playing = IsPlaying;
            // a source the tab started that stopped without the Stop button: a stream that ended, a pipeline that
            // failed
            if (_wasPlaying && !playing && _started != null && _error == null)
                _error = "The video stopped: check the source (the log has the details)";
            if (!playing)
                _started = null;
            _wasPlaying = playing;

            btnStartStop.Running = playing;
            if (playing)
            {
                var (state, text) = PlayingState();
                _status.SetState(state, PlayingName() + " · " + text);
            }
            else
            {
                _status.SetState(_error != null ? VideoState.Error : VideoState.Off, _error ?? "No video started");
            }

            bool recording = GCSViews.FlightData.instance?.IsHudRecording == true;
            btnRecord.Running = recording;
            lblRecord.Text = recording ? "Recording to the log folder" : "Saves the HUD and its video to the log folder";
        }

        private string PlayingName()
        {
            if (MainV2.cam != null)
                return SourceNames[Source.Camera];
            if (CaptureMJPEG.IsRunning)
                return SourceNames[Source.Mjpeg];
            // started here, or elsewhere (a stream found on connecting)
            return _started.HasValue ? SourceNames[_started.Value] : "GStreamer stream";
        }

        private (VideoState State, string Text) PlayingState()
        {
            long ticks = Interlocked.Read(ref _lastFrameTicks);
            if (ticks == 0 || ticks < _startedAt.Ticks)
                return (VideoState.Waiting, "connecting…");

            var silent = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);
            if (silent > FrameTimeout)
                return (VideoState.Waiting, "no video for " + (int)silent.TotalSeconds + " s");

            return (VideoState.Playing, _frameWidth + " × " + _frameHeight);
        }

        #endregion

        #region Display and recording

        private void ChkHudShow_CheckedChanged(object sender, EventArgs e)
        {
            GCSViews.FlightData.myhud.hudon = chkHudShow.Checked;
            Settings.Instance["CHK_hudshow"] = chkHudShow.Checked.ToString();
        }

        private void BtnRecord_Click(object sender, EventArgs e)
        {
            var flightData = GCSViews.FlightData.instance;
            if (flightData == null)
                return;

            if (flightData.IsHudRecording)
                flightData.StopHudRecording();
            else
                flightData.StartHudRecording();

            UpdateStatus();
        }

        // the log folder, where recordings go
        private void BtnOpenFolder_Click(object sender, EventArgs e)
        {
            LogFolder.Open();
        }

        private void CmbOsdColor_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (startup)
                return;

            if (cmbOsdColor.Text != "")
            {
                Settings.Instance["hudcolor"] = cmbOsdColor.Text;
                GCSViews.FlightData.myhud.hudcolor = Color.FromName(cmbOsdColor.Text);
            }
        }

        private void CmbOsdColor_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            var g = e.Graphics;
            var rect = e.Bounds;
            Brush brush = null;

            if ((e.State & DrawItemState.Selected) == 0)
                brush = new SolidBrush(cmbOsdColor.BackColor);
            else
                brush = SystemBrushes.Highlight;

            g.FillRectangle(brush, rect);

            brush = new SolidBrush(Color.FromName((string)cmbOsdColor.Items[e.Index]));

            g.FillRectangle(brush, rect.X + 2, rect.Y + 2, 30, rect.Height - 4);
            g.DrawRectangle(Pens.Black, rect.X + 2, rect.Y + 2, 30, rect.Height - 4);

            if ((e.State & DrawItemState.Selected) == 0)
                brush = new SolidBrush(cmbOsdColor.ForeColor);
            else
                brush = SystemBrushes.HighlightText;
            g.DrawString(cmbOsdColor.Items[e.Index].ToString(),
                cmbOsdColor.Font, brush, rect.X + 35, rect.Top + rect.Height - cmbOsdColor.Font.Height);
        }

        #endregion

        public class GCSBitmapInfo
        {
            public GCSBitmapInfo(int width, int height, long fps, string standard, AMMediaType media)
            {
                Width = width;
                Height = height;
                Fps = fps;
                Standard = standard;
                Media = media;
            }

            public int Width { get; set; }
            public int Height { get; set; }
            public long Fps { get; set; }
            public string Standard { get; set; }
            public AMMediaType Media { get; set; }

            public override string ToString()
            {
                return Width + " x " + Height + string.Format(" {0:0.00} fps ", 10000000.0 / Fps) + Standard;
            }
        }
    }
}
