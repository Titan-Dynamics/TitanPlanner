using DotSpatial.Projections;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using log4net;
using MissionPlanner.ArduPilot;
using MissionPlanner.ArduPilot.Mavlink;
using MissionPlanner.Comms;
using MissionPlanner.Controls;
using MissionPlanner.GCSViews;
using MissionPlanner.GCSViews.ConfigurationView;
using MissionPlanner.GeoRef;
using MissionPlanner.Log;
using MissionPlanner.Maps;
using MissionPlanner.Swarm;
using MissionPlanner.Utilities;
using MissionPlanner.Warnings;
using resedit;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Serialization;
using DotSpatial.Data;
using Microsoft.Scripting.Utils;
using static MissionPlanner.Utilities.Firmware;
using Formatting = Newtonsoft.Json.Formatting;
using ILog = log4net.ILog;

namespace MissionPlanner
{
    public partial class temp : Form
    {
        private static readonly ILog log =
            LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        private static MAVLinkSerialPort comport;

        private static TcpListener listener;

        private static TcpClient client;

        public temp()
        {
            InitializeComponent();

            but_lrhdashboard.Click += (s, e) => LRHDashboard.ShowWindow();
            BuildLayout();

            Tracking.AddPage(
                MethodBase.GetCurrentMethod().DeclaringType.ToString(),
                MethodBase.GetCurrentMethod().Name);
        }

        private static temp open;

        // the fork's, not in the designer
        private readonly MyButton but_lrhdashboard = new MyButton();

        /// <summary>
        /// Shows the Tools window, or brings it forward if it is open already.
        /// </summary>
        public static void ShowWindow()
        {
            if (open != null && !open.IsDisposed)
            {
                if (open.WindowState == FormWindowState.Minimized)
                    open.WindowState = FormWindowState.Normal;
                open.Activate();
                return;
            }

            var form = new temp();
            form.FormClosed += (s, e) =>
            {
                if (open == form)
                    open = null;
            };
            open = form;
            ThemeManager.ApplyThemeTo(form);
            form.Show();
        }

        #region Layout

        private const int ButtonWidth = 160;
        private const int ButtonHeight = 24;
        private const int ButtonGap = 10;
        private const int SectionPadding = 8;

        private class ToolRow
        {
            public MyButton Button;
            public Label Description;
            public string SearchText;
        }

        private class ToolSection
        {
            public GroupBox Box;
            public TableLayoutPanel Table;
            public readonly List<ToolRow> Rows = new List<ToolRow>();
        }

        private readonly List<ToolSection> sections = new List<ToolSection>();
        private readonly List<Label> dimLabels = new List<Label>();
        private Panel header;
        private GroupColumnsPanel columns;
        private PlaceholderTextBox search;
        private Label count;
        private Label noMatch;
        private Panel sensorPane;
        private Panel sensorLine;
        private int toolCount;

        /// <summary>
        /// Every tool: the section it is listed in, the name on its button and what it does, in the order shown. A
        /// button the designer has and this doesn't (one added upstream) is listed under Other, with its own text and
        /// the description beside it.
        /// </summary>
        private (string Section, MyButton Button, string Title, string Description)[] Catalog()
        {
            return new[]
            {
                ("Vehicle", but_lrhdashboard, "LRH Dashboard",
                    KeyboardShortcuts.Label("Power, range and link over 60 s", typeof(MainV2),
                        nameof(MainV2.ShortcutLRHDashboard))),
                ("Vehicle", but_reboot, "Reboot Vehicle", "Reboot the autopilot"),
                ("Vehicle", but_disablearmswitch, "Toggle Safety Switch", "Press the safety switch remotely"),
                ("Vehicle", BUT_QNH, "Set QNH", "Set the barometer's ground pressure"),
                ("Vehicle", but_acbarohight, "Adjust Baro Height", "Shift the baro altitude reference"),
                ("Vehicle", but_messageinterval, "Message Intervals", "Set the rate of each MAVLink message"),
                ("Vehicle", but_signkey, "MAVLink Signing", "Set up MAVLink 2 signing keys"),
                ("Vehicle", but_paramrestore, "Param Restore", "Restore params, enabling features first"),
                ("Vehicle", BUT_forcecal_accel, "Force Accel Cal", "Mark accels calibrated after a restore"),
                ("Vehicle", BUT_forcecal_mag, "Force Compass Cal", "Mark compasses calibrated after a restore"),
                ("Vehicle", but_proximity, "Proximity", "Show what the proximity sensors see"),
                ("Vehicle", but_optflowcalib, "Optical Flow Image", "Show a PX4Flow sensor's camera image"),

                ("Firmware & Hardware", but_blupdate, "Bootloader Upgrade", "Update the bootloader (can brick it)"),
                ("Firmware & Hardware", but_dfumode, "DFU Mode", "Reboot into DFU mode for USB flashing"),
                ("Firmware & Hardware", but_apjtool, "APJ Tool", "Embed default params in an .apj file"),
                ("Firmware & Hardware", but_getfw, "Download All Firmware", "Save all current firmware to History"),
                ("Firmware & Hardware", but_hwids, "Decode Device IDs", "Decode sensor IDs like COMPASS_DEV_ID"),
                ("Firmware & Hardware", but_driverclean, "Clean Drivers", "Remove the installed autopilot drivers"),

                ("Logs", butlogindex, "Log Index", "Browse a folder of telemetry logs"),
                ("Logs", BUT_sorttlogs, "Sort TLogs", "Sort tlogs by vehicle type and sysid"),
                ("Logs", but_sortlogs, "Re-sort All Logs", "Re-sort every log in the log folder"),
                ("Logs", but_maplogs, "Map Logs", "Save a map image of each log"),
                ("Logs", myButton1, "Split DataFlash Log", "Split a DataFlash log into pieces"),
                ("Logs", but_anonlog, "Anonymise Log", "Copy a log with positions moved (beta)"),
                ("Logs", but_logdlscp, "Download Logs over SSH", "Download logs from APSync by SCP"),
                ("Logs", but_remotedflogger, "Remote DataFlash Log", "Stream the DataFlash log to this PC"),
                ("Logs", BUT_magfit2, "Mag Offsets from Log", "Work out compass offsets from a log"),
                ("Logs", but_gpsinj, "Extract RTCM", "Extract RTCM corrections from a tlog"),
                ("Logs", but_dashware, "DashWare Export", "Make a DashWare file from a .bin log"),
                ("Logs", BUT_georefimage, "Geotag Images", "Geotag photos from a flight log"),

                ("Maps & Terrain", but_GDAL, "Custom GDAL Imagery", "Load GeoTIFFs as imagery and elevation"),
                ("Maps & Terrain", BUT_geinjection, "Import Map Tiles", "Import Z/Y/X tile images as imagery"),
                ("Maps & Terrain", BUT_clearcustommaps, "Clear Custom Imagery", "Delete custom imagery from the cache"),
                ("Maps & Terrain", but_agemapdata, "Age Map Data", "Delete tiles older than 30 days"),
                ("Maps & Terrain", but_dem, "Loaded DEMs", "List the elevation models loaded"),
                ("Maps & Terrain", BUT_shptopoly, "Shapefile to Polygon", "Convert a .shp file to a polygon file"),
                ("Maps & Terrain", but_3dmap, "3D Map Window", "Open the 3D map in a window of its own"),

                ("Outputs & Links", BUT_outputMavlink, "MAVLink Mirror", "Forward MAVLink to another port"),
                ("Outputs & Links", BUT_outputnmea, "NMEA Output", "Send the vehicle's position as NMEA"),
                ("Outputs & Links", BUT_outputMD, "MicroDrone Output", "Send the position in MicroDrone format"),
                ("Outputs & Links", BUT_CoT, "Cursor on Target", "Send the position as Cursor-on-Target"),
                ("Outputs & Links", but_mavserialport, "GPS Serial Passthrough", "Pass TCP port 500 to the GPS port"),
                ("Outputs & Links", but_sitl_comb, "SITL Stream Combiner", "Merge SITL vehicles onto one link"),
                ("Outputs & Links", BUT_follow_me, "Follow Me", "Have the vehicle follow an NMEA GPS"),
                ("Outputs & Links", BUT_movingbase, "Moving Base", "Show your NMEA GPS position on the map"),

                ("Swarm", BUT_swarm, "Formation", "Fly several vehicles in formation"),
                ("Swarm", BUT_followleader, "Follow the Leader", "Vehicles follow a leader's path"),
                ("Swarm", but_followswarm, "Follow Swarm", "Swarm control with a waypoint leader"),
                ("Swarm", but_trimble, "Sequence Swarm", "Edit a swarm sequence layout"),

                ("Video", but_osdvideo, "OSD Video", "Overlay the HUD on a recorded video"),
                ("Video", myButton_vlc, "VLC Stream", "Play a stream in VLC (prefer GStreamer)"),

                ("Planner", button3, "Warning Manager", "Set up custom spoken warnings"),
                ("Planner", but_ManageCMDList, "Mission Command List", "Choose the PLAN screen's commands"),
                ("Planner", BUT_paramgen, "Update Param Info", "Download the latest param descriptions"),
                ("Planner", BUT_lang_edit, "Translation Editor", "Edit the planner's translations"),

                ("Developer", but_mavinspector, "MAVLink Inspector", "Inspect every MAVLink message"),
                ("Developer", but_hexmavlink, "Decode Hex Packet", "Decode a packet from a hex string"),
                ("Developer", but_packetbytes, "Decode Packet Bytes", "Decode a packet from a list of bytes"),
                ("Developer", BUT_fft, "FFT", "Analyse vibration frequencies in a log"),
                ("Developer", but_td, "Fetch threads.txt", "Download @SYS/threads.txt by MAVFTP"),
                ("Developer", but_structtest, "Struct Speed Test", "Time the MAVLink struct conversions"),
                ("Developer", but_gimbaltest, "Gimbal Point Test", "Run the gimbal pointing calculation"),
                ("Developer", but_armandtakeoff, "Arm and Takeoff", "Copter: arm, Guided, take off to 10 m"),
                ("Developer", but_lockup, "Lockup Autopilot", "Hang the autopilot (crashes in flight)"),
            };
        }

        /// <summary>
        /// Lays the designer's buttons, with their handlers, out in sections that pack into columns, each with what
        /// it does beside it, under a search box; the vehicle's sensors down the right.
        /// </summary>
        private void BuildLayout()
        {
            SuspendLayout();

            var designed = tableLayoutPanel1;
            var tools = Catalog().ToList();
            var listed = new HashSet<MyButton>(tools.Select(t => t.Button));
            foreach (var button in designed.Controls.OfType<MyButton>().Where(b => !listed.Contains(b))
                         .OrderBy(b => b.Text).ToList())
            {
                var at = designed.GetPositionFromControl(button);
                var beside = designed.GetControlFromPosition(at.Column + 1, at.Row) as Label;
                tools.Add(("Other", button, button.Text, beside?.Text ?? ""));
            }

            Controls.Clear();
            AutoScroll = false;
            Font = new Font("Segoe UI", 9.5F);
            Text = "Tools";
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            Size = new Size(Math.Min(1280, area.Width - 40), Math.Min(820, area.Height - 40));
            MinimumSize = new Size(820, 520);
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            KeyDown += Tools_KeyDown;

            columns = new GroupColumnsPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                Padding = new Padding(12, 6, 12, 12),
                MinColumnWidth = 420,
                Gap = 8
            };
            columns.ColumnWidthChanged += (s, e) => FitDescriptions();
            var sectionFont = new Font(Font, FontStyle.Bold);
            foreach (var group in tools.GroupBy(t => t.Section))
            {
                var section = new ToolSection
                {
                    Box = new GroupBox
                    {
                        // a single & would underline the next letter
                        Text = group.Key.Replace("&", "&&"),
                        Font = sectionFont,
                        AutoSize = true,
                        AutoSizeMode = AutoSizeMode.GrowAndShrink,
                        Padding = new Padding(SectionPadding, 2, SectionPadding, 4),
                        Margin = new Padding(0)
                    }
                };
                var table = new TableLayoutPanel
                {
                    Dock = DockStyle.Fill,
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    Font = Font
                };
                table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

                int row = 0;
                foreach (var tool in group)
                {
                    var button = tool.Button;
                    button.Text = tool.Title;
                    button.Dock = DockStyle.None;
                    button.AutoSize = false;
                    button.Size = new Size(ButtonWidth, ButtonHeight);
                    button.Margin = new Padding(0, 2, ButtonGap, 2);
                    button.Anchor = AnchorStyles.Left;
                    var description = new Label
                    {
                        Text = tool.Description,
                        AutoSize = true,
                        Anchor = AnchorStyles.Left | AnchorStyles.Right,
                        Margin = new Padding(0, 2, 0, 2)
                    };
                    dimLabels.Add(description);
                    table.Controls.Add(button, 0, row);
                    table.Controls.Add(description, 1, row);
                    row++;

                    section.Rows.Add(new ToolRow
                    {
                        Button = button,
                        Description = description,
                        SearchText = group.Key + " " + tool.Title + " " + tool.Description
                    });
                }

                section.Table = table;
                section.Box.Controls.Add(table);
                columns.Controls.Add(section.Box);
                sections.Add(section);
            }

            // the designer's table holds only its old description labels now
            designed.Dispose();

            toolCount = tools.Count;
            noMatch = new Label { AutoSize = true, Visible = false, Margin = new Padding(0, 8, 0, 0) };
            dimLabels.Add(noMatch);
            columns.Controls.Add(noMatch);

            sensorPane = new Panel { Dock = DockStyle.Right, Width = 340, Padding = new Padding(12, 6, 8, 12) };
            var sensorTitle = new Label
            {
                Text = "Sensors",
                Dock = DockStyle.Top,
                Height = 26,
                Font = sectionFont,
                TextAlign = ContentAlignment.MiddleLeft
            };
            controlSensorsStatus1.AutoSize = false;
            controlSensorsStatus1.Dock = DockStyle.Fill;
            controlSensorsStatus1.Margin = new Padding(0);
            // fill first so the title is docked before it
            sensorPane.Controls.Add(controlSensorsStatus1);
            sensorPane.Controls.Add(sensorTitle);
            sensorLine = new Panel { Dock = DockStyle.Right, Width = 1 };

            // the search bar across the window, the count at its end
            var headerTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 2,
                RowCount = 1,
                Padding = new Padding(12, 8, 12, 8)
            };
            headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            headerTable.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header = headerTable;
            // the parameter pages' search box
            search = new PlaceholderTextBox
            {
                Height = 28,
                Font = new Font("Segoe UI", 9.75F),
                PlaceholderText = "Search tools",
                Anchor = AnchorStyles.Left | AnchorStyles.Right,
                Margin = new Padding(0)
            };
            search.DisableAutoComplete();
            search.TextChanged += (s, e) => Filter();
            count = new Label { AutoSize = true, Anchor = AnchorStyles.Right, Margin = new Padding(10, 0, 0, 0) };
            dimLabels.Add(count);
            headerTable.Controls.Add(search, 0, 0);
            headerTable.Controls.Add(count, 1, 0);

            // fill first so the panels docked to the edges are laid out before it
            Controls.Add(columns);
            Controls.Add(sensorLine);
            Controls.Add(sensorPane);
            Controls.Add(header);
            // typing goes straight to the search
            Shown += (s, e) => search.Focus();

            Filter();
            ResumeLayout(true);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);

            // the theme has been applied by now: the secondary colours come from it
            var dim = Blend(BackColor, ForeColor, 0.55);
            foreach (var label in dimLabels)
                label.ForeColor = dim;
            sensorLine.BackColor = Blend(BackColor, ForeColor, 0.18);
            header.BackColor = Blend(BackColor, ForeColor, 0.04);
            sensorPane.BackColor = Blend(BackColor, ForeColor, 0.025);
            controlSensorsStatus1.BackColor = sensorPane.BackColor;
            controlSensorsStatus1.ForeColor = ForeColor;
            // as the parameter pages have it
            search.BackColor = ThemeManager.BGColorTextBox;
            search.ForeColor = ThemeManager.TextColor;
            search.PlaceholderColor = Color.Gray;
        }

        private static Color Blend(Color from, Color to, double amount)
        {
            return Color.FromArgb(
                (int) (from.R + (to.R - from.R) * amount),
                (int) (from.G + (to.G - from.G) * amount),
                (int) (from.B + (to.B - from.B) * amount));
        }

        /// <summary>
        /// Wraps the descriptions to what a column leaves beside the buttons (the section's padding, the button and
        /// the gap after it), so each section is measured at its wrapped height.
        /// </summary>
        private void FitDescriptions()
        {
            int width = Math.Max(120, columns.ColumnWidth - 2 * SectionPadding - ButtonWidth - ButtonGap - 4);
            foreach (var section in sections)
            {
                section.Table.SuspendLayout();
                foreach (var row in section.Rows)
                    row.Description.MaximumSize = new Size(width, 0);
                section.Table.ResumeLayout(false);
            }
        }

        /// <summary>
        /// Shows the tools whose section, name or description hold every word searched for, and the sections with
        /// any.
        /// </summary>
        private void Filter()
        {
            var words = search.Text.Split(new[] {' '}, StringSplitOptions.RemoveEmptyEntries);
            int shown = 0;

            columns.SuspendLayout();
            foreach (var section in sections)
            {
                bool any = false;
                foreach (var row in section.Rows)
                {
                    bool match = words.All(w => row.SearchText.IndexOf(w, StringComparison.OrdinalIgnoreCase) >= 0);
                    row.Button.Visible = match;
                    row.Description.Visible = match;
                    any |= match;
                    if (match)
                        shown++;
                }

                section.Box.Visible = any;
            }

            noMatch.Text = "No tools match \"" + search.Text.Trim() + "\".";
            noMatch.Visible = shown == 0;
            columns.ResumeLayout(true);
            columns.AutoScrollPosition = Point.Empty;

            count.Text = words.Length == 0 ? toolCount + " tools" : shown + " of " + toolCount + " tools";
        }

        private void Tools_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                // the first clears the search, the next closes the window
                if (search.Text.Length > 0)
                    search.Text = "";
                else
                    Close();
                e.Handled = e.SuppressKeyPress = true;
            }
            else if (e.KeyData == (Keys.Control | Keys.F))
            {
                search.Focus();
                search.SelectionStart = 0;
                search.SelectionLength = search.Text.Length;
                e.Handled = e.SuppressKeyPress = true;
            }
        }

        #endregion

        //private static Factory factory; 
        //private static VideoPlayer player; 
        //private static Renderer renderer; 
        //private Media media; 


        private void temp_Load(object sender, EventArgs e)
        {
        }

        private void BUT_geinjection_Click(object sender, EventArgs e)
        {
            var MainMap = new GMapControl();

            MainMap.MapProvider = GoogleSatelliteMapProvider.Instance;

            MainMap.CacheLocation = Settings.GetDataDirectory() +
                                    "gmapcache" + Path.DirectorySeparatorChar;

            var fbd = new FolderBrowserDialog();

            try
            {
                fbd.SelectedPath = @"C:\Users\hog\Documents\albany 2011\New folder";
            }
            catch
            {
            }

            if (fbd.ShowDialog() != DialogResult.OK)
                return;

            if (fbd.SelectedPath != "")
            {
                var files = Directory.GetFiles(fbd.SelectedPath, "*.jpg", SearchOption.AllDirectories);
                var files1 = Directory.GetFiles(fbd.SelectedPath, "*.png", SearchOption.AllDirectories);

                var origlength = files.Length;
                Array.Resize(ref files, origlength + files1.Length);
                Array.Copy(files1, 0, files, origlength, files1.Length);

                foreach (var file in files)
                {
                    log.Info(DateTime.Now.Millisecond + " Doing " + file);
                    var reg = new Regex(@"Z([0-9]+)\\([0-9]+)\\([0-9]+)");

                    var mat = reg.Match(file);

                    if (mat.Success == false)
                        continue;

                    var temp = 1 << int.Parse(mat.Groups[1].Value);

                    var pnt = new GPoint(int.Parse(mat.Groups[3].Value), int.Parse(mat.Groups[2].Value));

                    BUT_geinjection.Text = file;
                    BUT_geinjection.Refresh();

                    //MainMap.Projection.

                    var tile = new MemoryStream();

                    var Img = Image.FromFile(file);
                    Img.Save(tile, ImageFormat.Jpeg);

                    tile.Seek(0, SeekOrigin.Begin);
                    log.Info(pnt.X + " " + pnt.Y);

                    Application.DoEvents();

                    GMaps.Instance.PrimaryCache.PutImageToCache(tile.ToArray(), Custom.Instance.DbId, pnt,
                        int.Parse(mat.Groups[1].Value));

                    // Application.DoEvents();
                }
            }

            MainMap.Dispose();
        }

        private void BUT_clearcustommaps_Click(object sender, EventArgs e)
        {
            var MainMap = new GMapControl();
            MainMap.MapProvider = GoogleSatelliteMapProvider.Instance;

            var removed = MainMap.Manager.PrimaryCache.DeleteOlderThan(DateTime.Now, Custom.Instance.DbId);

            CustomMessageBox.Show("Removed " + removed + " images");

            log.InfoFormat("Removed {0} images", removed);

            MainMap.Dispose();
        }

        private void BUT_lang_edit_Click(object sender, EventArgs e)
        {
            new Form1().Show();
        }

        private void BUT_georefimage_Click(object sender, EventArgs e)
        {
            new Georefimage().Show();
        }

        private void BUT_follow_me_Click(object sender, EventArgs e)
        {
            var si = new FollowMe();
            ThemeManager.ApplyThemeTo(si);
            si.Show();
        }

        private void BUT_paramgen_Click(object sender, EventArgs e)
        {
            /*
            if(MissionPlanner.Utilities.Update.dobeta)
                ParameterMetaDataParser.GetParameterInformation(ConfigurationManager.AppSettings["ParameterLocationsBleeding"]);
            else
                ParameterMetaDataParser.GetParameterInformation(ConfigurationManager.AppSettings["ParameterLocations"]);
                */
            // scan latest, and append older
            ParameterMetaDataParser.GetParameterInformation(ConfigurationManager.AppSettings["ParameterLocationsBleeding"] + ";" + ConfigurationManager.AppSettings["ParameterLocations"]);

            ParameterMetaDataRepositoryAPM.Reload();
        }

        private void myButton1_Click(object sender, EventArgs e)
        {
            new SerialOutputMD().Show();
        }

        private void but_osdvideo_Click(object sender, EventArgs e)
        {
            new OSDVideo().Show();
        }

        private void BUT_swarm_Click(object sender, EventArgs e)
        {
            new FormationControl().Show();
        }

        private void BUT_outputMavlink_Click(object sender, EventArgs e)
        {
            new SerialOutputPass().Show();
        }

        private void BUT_outputnmea_Click(object sender, EventArgs e)
        {
            new SerialOutputNMEA().Show();
        }

        private void BUT_followleader_Click(object sender, EventArgs e)
        {
            new FollowPathControl().Show();
        }

        private void BUT_sorttlogs_Click(object sender, EventArgs e)
        {
            var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = Settings.Instance.LogDir;

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                try
                {
                    LogSort.SortLogs(Directory.GetFiles(fbd.SelectedPath, "*.tlog"));
                }
                catch
                {
                }
            }
        }

        private void BUT_movingbase_Click(object sender, EventArgs e)
        {
            var si = new MovingBase();
            ThemeManager.ApplyThemeTo(si);
            si.Show();
        }

        private void but_getfw_Click(object sender, EventArgs e)
        {
            var basedir = Settings.GetDataDirectory() + "History";

            Directory.CreateDirectory(basedir);

            var fw = new Firmware();

            var list = fw.getFWList();

            var options = new optionsObject();
            options.softwares = list;

            var members = typeof(software).GetFields();

            XmlWriterSettings settings = new XmlWriterSettings();
            settings.IndentChars = "    ";
            settings.Indent = true;
            settings.Encoding = Encoding.ASCII;

            using (var xmlwriter = XmlWriter.Create(basedir + Path.DirectorySeparatorChar + @"firmware2.xml", settings))
            {
                for (int a = 0; a < options.softwares.Count; a++)
                {
                    Loading.ShowLoading(((a - 1) / (float)list.Count) * 100.0 + "% " + options.softwares[a].name, this);

                    List<Task<bool>> tasklist = new List<Task<bool>>();

                    foreach (var field in members)
                    {
                        if (field.Name.ToLower().Contains("url"))
                        {
                            var url = field.GetValue(options.softwares[a]).ToString();

                            if (String.IsNullOrEmpty(url))
                                continue;

                            field.SetValue(options.softwares[a], new Uri(url).LocalPath.TrimStart('/', '\\'));

                            var task = Download.getFilefromNetAsync(url, basedir + new Uri(url).LocalPath);
                            tasklist.Add(task);
                        }
                    }

                    //Task.WaitAll(tasklist.ToArray());
                }

                XmlSerializer xms = new XmlSerializer(typeof(optionsObject), new Type[] { typeof(software) });

                xms.Serialize(xmlwriter, options);
            }
            Loading.Close();
        }


        private void button3_Click(object sender, EventArgs e)
        {
            var frm = new WarningsManager();

            frm.Show();
        }

        private void but_mavserialport_Click(object sender, EventArgs e)
        {
            if (comport != null)
            {
                comport.Close();
                if (client != null && client.Connected)
                {
                    client.Close();
                }

                if (listener != null)
                {
                    listener.Stop();
                }
                return;
            }

            try
            {
                comport = new MAVLinkSerialPort(MainV2.comPort, MAVLink.SERIAL_CONTROL_DEV.GPS1);

                if (listener != null)
                {
                    listener.Stop();
                    listener = null;
                }

                listener = new TcpListener(IPAddress.Any, 500);

                listener.Start();

                listener.BeginAcceptTcpClient(DoAcceptTcpClientCallback, listener);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
            }
        }

        private void DoAcceptTcpClientCallback(IAsyncResult ar)
        {
            try
            {
                // Get the listener that handles the client request.
                var listener = (TcpListener)ar.AsyncState;

                listener.BeginAcceptTcpClient(DoAcceptTcpClientCallback, listener);

                if (client != null && client.Connected)
                    return;

                // End the operation and display the received data on  
                // the console.
                using (
                    client = listener.EndAcceptTcpClient(ar))
                {
                    var stream = client.GetStream();

                    if (!comport.IsOpen)
                        comport.Open();

                    while (client.Connected && comport.IsOpen)
                    {
                        while (stream.DataAvailable)
                        {
                            var data = new byte[4096];
                            try
                            {
                                var len = stream.Read(data, 0, data.Length);

                                comport.Write(data, 0, len);
                            }
                            catch
                            {
                            }
                        }

                        while (comport.BytesToRead > 0)
                        {
                            var data = new byte[4096];
                            try
                            {
                                var len = comport.Read(data, 0, data.Length);

                                stream.Write(data, 0, len);
                            }
                            catch
                            {
                            }
                        }

                        Thread.Sleep(1);
                    }
                }
            }
            catch
            {
            }
        }

        private async void BUT_magfit2_Click(object sender, EventArgs e)
        {
            await MagCalib.ProcessLog(0).ConfigureAwait(false);
        }

        private void BUT_shptopoly_Click(object sender, EventArgs e)
        {
            using (var fd = new OpenFileDialog())
            {
                fd.Filter = "Shape file|*.shp";
                var result = fd.ShowDialog();
                var file = fd.FileName;

                var pStart = new ProjectionInfo();
                var pESRIEnd = KnownCoordinateSystems.Geographic.World.WGS1984;
                var reproject = false;

                if (File.Exists(file))
                {
                    var prjfile = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar +
                                  Path.GetFileNameWithoutExtension(file) + ".prj";
                    if (File.Exists(prjfile))
                    {
                        using (
                            var re =
                                File.OpenText(Path.GetDirectoryName(file) + Path.DirectorySeparatorChar +
                                              Path.GetFileNameWithoutExtension(file) + ".prj"))
                        {
                            pStart.ParseEsriString(re.ReadLine());

                            reproject = true;
                        }
                    }

                    var fs = FeatureSet.Open(file);

                    fs.FillAttributes();

                    var rows = fs.NumRows();

                    var dtOriginal = fs.DataTable;
                    for (var row = 0; row < dtOriginal.Rows.Count; row++)
                    {
                        var original = dtOriginal.Rows[row].ItemArray;
                    }

                    foreach (DataColumn col in dtOriginal.Columns)
                    {
                        Console.WriteLine(col.ColumnName + " " + col.DataType);
                    }

                    var a = 1;

                    var path = Path.GetDirectoryName(file);

                    foreach (var feature in fs.Features)
                    {
                        var sb = new StringBuilder();

                        sb.Append("#Shap to Poly - Mission Planner\r\n");
                        foreach (var point in feature.Geometry.Coordinates)
                        {
                            if (reproject)
                            {
                                double[] xyarray = { point.X, point.Y };
                                double[] zarray = { point.Z };

                                Reproject.ReprojectPoints(xyarray, zarray, pStart, pESRIEnd, 0, 1);

                                point.X = xyarray[0];
                                point.Y = xyarray[1];
                                point.Z = zarray[0];
                            }

                            sb.Append(point.Y.ToString(CultureInfo.InvariantCulture) + "\t" +
                                      point.X.ToString(CultureInfo.InvariantCulture) + "\r\n");
                        }

                        log.Info("writting poly to " + path + Path.DirectorySeparatorChar + "poly-" + a + ".poly");
                        File.WriteAllText(path + Path.DirectorySeparatorChar + "poly-" + a + ".poly", sb.ToString());

                        a++;
                    }
                }
            }
        }

        private void but_gimbaltest_Click(object sender, EventArgs e)
        {
            if (MainV2.comPort.BaseStream.IsOpen)
            {
                GimbalPoint.ProjectPoint(MainV2.comPort);
            }
            else
                CustomMessageBox.Show(Strings.PleaseConnect, Strings.ERROR);
        }

        private string[] GetFiles(string path, string pattern)
        {
            var files = new List<string>();
            var directories = new string[] { };

            try
            {
                files.AddRange(Directory.GetFiles(path, pattern, SearchOption.TopDirectoryOnly));
                directories = Directory.GetDirectories(path);
            }
            catch (UnauthorizedAccessException) { }

            foreach (var directory in directories)
                try
                {
                    files.AddRange(GetFiles(directory, pattern));
                }
                catch (UnauthorizedAccessException) { }

            return files.ToArray();
        }

        private void but_maplogs_Click(object sender, EventArgs e)
        {
            var fbd = new FolderBrowserDialog();
            fbd.SelectedPath = Settings.Instance.LogDir;

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                LogMap.MapLogs(GetFiles(fbd.SelectedPath, "*.tlog"));
                LogMap.MapLogs(GetFiles(fbd.SelectedPath, "*.bin"));
                LogMap.MapLogs(GetFiles(fbd.SelectedPath, "*.log"));
            }
        }

        private void butlogindex_Click(object sender, EventArgs e)
        {
            var form = new LogIndex();

            form.Show();
        }

        private void but_structtest_Click(object sender, EventArgs e)
        {
            var array = new byte[100];

            for (var l = 0; l < array.Length; l++)
            {
                array[l] = (byte)l;
            }

            var a = 0;
            var start = DateTime.MinValue;
            var end = DateTime.MinValue;


            start = DateTime.Now;
            for (a = 0; a < 1000000; a++)
            {
                var obj = (object)new MAVLink.mavlink_heartbeat_t();
                MavlinkUtil.ByteArrayToStructure(array, ref obj, 6, 5);
            }
            end = DateTime.Now;
            Console.WriteLine("ByteArrayToStructure " + (end - start).TotalMilliseconds);
            start = DateTime.Now;
            for (a = 0; a < 1000000; a++)
            {
                var ans1 = MavlinkUtil.ByteArrayToStructureT<MAVLink.mavlink_heartbeat_t>(array, 6);
            }
            end = DateTime.Now;
            Console.WriteLine("ByteArrayToStructureT<> " + (end - start).TotalMilliseconds);
            start = DateTime.Now;
            for (a = 0; a < 1000000; a++)
            {
                var ans2 = MavlinkUtil.ReadUsingPointer<MAVLink.mavlink_heartbeat_t>(array, 6);
            }
            end = DateTime.Now;
            Console.WriteLine("ReadUsingPointer " + (end - start).TotalMilliseconds);
            start = DateTime.Now;

            for (a = 0; a < 1000000; a++)
            {
                var ans3 = MavlinkUtil.ByteArrayToStructureGC<MAVLink.mavlink_heartbeat_t>(array, 6);
            }
            end = DateTime.Now;
            Console.WriteLine("ByteArrayToStructureGC<T> " + (end - start).TotalMilliseconds);

            start = DateTime.Now;
            for (a = 0; a < 1000000; a++)
            {
                var ans4 = MavlinkUtil.ByteArrayToStructureGC(array, typeof(MAVLink.mavlink_heartbeat_t), 6, 5);
            }
            end = DateTime.Now;
            Console.WriteLine("ByteArrayToStructureGC " + (end - start).TotalMilliseconds);

            start = DateTime.Now;
            for (a = 0; a < 1000000; a++)
            {
                var ans4 = MavlinkUtil.ByteArrayToStructureGCArray(array, typeof(MAVLink.mavlink_heartbeat_t), 6, 5);
            }
            end = DateTime.Now;
            Console.WriteLine("ByteArrayToStructureGCArray " + (end - start).TotalMilliseconds);


            
        }

        private void but_armandtakeoff_Click(object sender, EventArgs e)
        {
            try
            {

                MainV2.comPort.setMode("Stabilize");

                if (MainV2.comPort.doARM(true))
                {
                    MainV2.comPort.setMode("GUIDED");

                    Thread.Sleep(300);

                    MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, MAVLink.MAV_CMD.TAKEOFF, 0, 0, 0, 0, 0, 0, 10);
                }
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.ToString());
            }
        }

        private void but_sitl_comb_Click(object sender, EventArgs e)
        {
            StreamCombiner.Connect += (mav, localsysid) =>
            {
                MainV2.instance.doConnect(mav, "preset",
                    localsysid.ToString());

                MainV2.Comports.Add(mav);
            };

            StreamCombiner.Start();
        }

        private void but_injectgps_Click(object sender, EventArgs e)
        {
            new ConfigSerialInjectGPS().Show();
        }

        private void BUT_fft_Click(object sender, EventArgs e)
        {
            var fft = new fftui();

            fft.Show();
        }


        private void but_reboot_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("Are you sure?", "", MessageBoxButtons.YesNo) == (int)DialogResult.Yes)
                MainV2.comPort.doReboot(false, true);
        }

        private void BUT_QNH_Click(object sender, EventArgs e)
        {
            var paramname = MainV2.comPort.MAV.param.ContainsKey("GND_ABS_PRESS") ? "GND_ABS_PRESS" : "BARO1_GND_PRESS";

            var currentQNH = MainV2.comPort.GetParam(paramname).ToString();

            if (InputBox.Show("QNH", "Enter the QNH in pascals (103040 = 1030.4 hPa)", ref currentQNH) ==
                DialogResult.OK)
            {
                var newQNH = double.Parse(currentQNH);

                MainV2.comPort.setParam(MainV2.comPort.sysidcurrent, (byte) MainV2.comPort.compidcurrent,
                    paramname, newQNH);
            }
        }

        private void but_trimble_Click(object sender, EventArgs e)
        {
            new Swarm.Sequence.LayoutEditor().Show();
        }

        private void myButton_vlc_Click(object sender, EventArgs e)
        {
            var render = new vlcrender();

            var url = render.playurl;
            if (InputBox.Show("enter url", "enter url", ref url) == DialogResult.OK)
            {
                render.playurl = url;
                try
                {
                    render.Start();
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
                }
            }
        }



        private void but_agemapdata_Click(object sender, EventArgs e)
        {
            var removed = ((PureImageCache)MyImageCache.Instance).DeleteOlderThan(DateTime.Now.AddDays(-30),
                FlightData.instance.gMapControl1.MapProvider.DbId);

            CustomMessageBox.Show("Removed " + removed + " images");

            log.InfoFormat("Removed {0} images", removed);
        }

        private void myButton1_Click_2(object sender, EventArgs e)
        {
            
            OpenFileDialog openFileDialog1 = new OpenFileDialog();
            openFileDialog1.Filter = "Log Files|*.log;*.bin;*.BIN;*.LOG";
            openFileDialog1.FilterIndex = 2;
            openFileDialog1.Multiselect = true;
            openFileDialog1.InitialDirectory =  Settings.Instance.LogDir;
            if (openFileDialog1.ShowDialog() == DialogResult.OK)
            {
                int a = 10;
                InputBox.Show("How Many", "Enter how many pieces to split into", ref a);
                new DFLogBuffer(openFileDialog1.FileName).SplitLog(a);
            }            
        }

        private void but_signkey_Click(object sender, EventArgs e)
        {
            var auth = new AuthKeys();

            auth.Show();
        }

        private void but_optflowcalib_Click(object sender, EventArgs e)
        {
            var test = new Form();
            var imagebox = new PictureBox();
            imagebox.Dock = DockStyle.Fill;
            imagebox.SizeMode = PictureBoxSizeMode.Zoom;
            test.Controls.Add(imagebox);

            test.Show();

            var flow = new OpticalFlow(MainV2.comPort, MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent);

            // disable on close form
            test.Closed += (o, args) =>
            {
                flow.CalibrationMode(false);
                flow.Close();
            };

            // enable calibration mode
            flow.CalibrationMode(true);

            // setup bitmap to screen
            flow.newImage += (s, eh) => imagebox.Image = (Image)eh.Image.Clone();
        }

        private async void but_gpsinj_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "tlog|*.tlog";
            ofd.ShowDialog();

            SaveFileDialog sfd = new SaveFileDialog();
            sfd.FileName = "output.dat";
            var result = sfd.ShowDialog();

            if (ofd.CheckFileExists && result == DialogResult.OK)
            {
                using (var st = sfd.OpenFile())
                {
                    using (MAVLinkInterface mine = new MAVLinkInterface(ofd.OpenFile()))
                    {
                        mine.logreadmode = true;

                        while (mine.logplaybackfile.BaseStream.Position < mine.logplaybackfile.BaseStream.Length)
                        {
                            MAVLink.MAVLinkMessage packet = await mine.readPacketAsync().ConfigureAwait(true);

                            if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.GPS_INJECT_DATA)
                            {
                                var item = packet.ToStructure<MAVLink.mavlink_gps_inject_data_t>();
                                st.Write(item.data, 0, item.len);
                            }
                            else if (packet.msgid == (uint)MAVLink.MAVLINK_MSG_ID.GPS_RTCM_DATA)
                            {
                                var item = packet.ToStructure<MAVLink.mavlink_gps_rtcm_data_t>();
                                st.Write(item.data, 0, item.len);
                            }
                        }
                    }
                }
            }
        }

        private void but_followswarm_Click(object sender, EventArgs e)
        {
            new Swarm.WaypointLeader.WPControl().Show();
        }

        private void myButton3_Click(object sender, EventArgs e)
        {
            but_GDAL_Click(sender, e);
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case 0x86: // WM_NCACTIVATE
                    var child = Control.FromHandle(m.LParam);

                    if (child is Form)
                    {
                        ThemeManager.ApplyThemeTo(child);
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        private void but_GDAL_Click(object sender, EventArgs e)
        {
            FolderBrowserDialog fbd = new FolderBrowserDialog();
            if (Directory.Exists(Settings.Instance["GDALImageDir"]))
                fbd.SelectedPath = Settings.Instance["GDALImageDir"];

            if (fbd.ShowDialog() == DialogResult.OK)
            {
                if (Directory.Exists(fbd.SelectedPath))
                {
                    Settings.Instance["GDALImageDir"] = fbd.SelectedPath;
                    Utilities.GDAL.OnProgress += GDAL_OnProgress;
                    Utilities.GDAL.ScanDirectory(fbd.SelectedPath);
                    DTED.OnProgress += GDAL_OnProgress;
                    DTED.AddCustomDirectory(fbd.SelectedPath);

                    Loading.Close();
                }
            }
        }

        private void GDAL_OnProgress(double percent, string message)
        {
            Loading.ShowLoading((percent).ToString("0.0%") + " " + message, this);
        }

        private void but_sortlogs_Click(object sender, EventArgs e)
        {
            MissionPlanner.Log.LogSort.SortLogs(Directory.GetFiles(Settings.Instance.LogDir, "*.tlog",
                SearchOption.AllDirectories), Settings.Instance.LogDir);
            MissionPlanner.Log.LogSort.SortLogs(Directory.GetFiles(Settings.Instance.LogDir, "*.bin",
                SearchOption.AllDirectories), Settings.Instance.LogDir);
            MissionPlanner.Log.LogSort.SortLogs(Directory.GetFiles(Settings.Instance.LogDir, "*.log",
                SearchOption.AllDirectories), Settings.Instance.LogDir);
            MissionPlanner.Log.LogSort.SortLogs(Directory.GetFiles(Settings.Instance.LogDir, "*.rlog",
             SearchOption.AllDirectories), Settings.Instance.LogDir);
        }

        private void but_logdlscp_Click(object sender, EventArgs e)
        {
            LogDownloadscp form = new LogDownloadscp();
            form.Show();
        }

        private void but_td_Click(object sender, EventArgs e)
        {
            string path = "@SYS/threads.txt";
            if (InputBox.Show("path", "path", ref path) == DialogResult.OK)
            {
                if (MainV2.comPort.BaseStream.IsOpen)
                {
                    var mavftp = new MAVFtp(MainV2.comPort, MainV2.comPort.sysidcurrent,
                        (byte) MainV2.comPort.compidcurrent);
                    var st = mavftp.GetFile(path, new CancellationTokenSource(5000), true);
                    var output = Path.Combine(Settings.GetUserDataDirectory(), Path.GetFileName(path));
                    File.WriteAllBytes(output, st.ToArray());
                }
            }
        }

        private void but_dem_Click(object sender, EventArgs e)
        {
            UserControl ctl = new UserControl() { Width = 1100, Height = 600, AutoSize = true };

            FlowLayoutPanel flp = new FlowLayoutPanel() { Dock = DockStyle.Fill, AutoScroll = true };
            var lbl2 = new Label() { Text = "Click on line to zoom to it", AutoSize = true };
            flp.Controls.Add(lbl2);
            string line = "";

            foreach (var item in GeoTiff.index)
            {
                //log.InfoFormat("Start Point ({0},{1},{2}) --> ({3},{4},{5})", item.i, item.j, item.k, item.x, item.y, item.z);

                line = String.Format("{0} = {1} = {2}*{3} {4} {8}\r\n", item.FileName, item.Area, item.width, item.height, item.bits,
                    item.xscale, item.yscale, item.zscale, item.srcProjection?.Name ?? item.srcProjection?.Transform?.Name);

                var lbl = new Label() { Text = line, AutoSize = true};
                lbl.Click += (o, args) =>
                {
                    FlightData.instance.gMapControl1.SetZoomToFitRect(item.Area);
                    FlightPlanner.instance.MainMap.SetZoomToFitRect(item.Area);
                };
                flp.Controls.Add(lbl);
            }

            ctl.Controls.Add(flp);
            var butt = new MyButton() { Text = "Open DEM Dir", Dock = DockStyle.Top };
            butt.Click += (a, ev) =>
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(srtm.datadirectory) { UseShellExecute = true });
            };
            ctl.Controls.Add(butt);

            ctl.ShowUserControl();
        }

        private void but_gsttest_Click(object sender, EventArgs e)
        {
            System.Threading.ThreadPool.QueueUserWorkItem((a) =>
            {
                //GStreamer.test();
            });
        }

        private void but_proximity_Click(object sender, EventArgs e)
        {
            new ProximityControl(MainV2.comPort.MAV).Show();
        }

        private void but_dashware_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Filter = "bin|*.bin;*.BIN";
            ofd.ShowDialog();

            if (ofd.CheckFileExists)
            {
                string options = "GPS;ATT;NTUN;CTUN;MODE;BAT";
                InputBox.Show("DashWare Types", "Enter Messages you want eg PARM;NTUN;CTUN", ref options);

                var split = options.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

                DashWare.Create(ofd.FileName, ofd.FileName + ".csv", split.Length > 0 ? split.ToList() : null);
            }
        }

        private void but_mavinspector_Click(object sender, EventArgs e)
        {
            new MAVLinkInspector(MainV2.comPort).Show();
        }

        private void BUT_driverclean_Click(object sender, EventArgs e)
        {
            CleanDrivers.Clean();
        }

        private void but_blupdate_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("Are you sure you want to upgrade the bootloader? This can brick your board",
                "BL Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == (int) DialogResult.Yes)
                if (CustomMessageBox.Show(
                    "Are you sure you want to upgrade the bootloader? This can brick your board, Please allow 5 mins for this process",
                    "BL Update", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == (int) DialogResult.Yes)
                    try
                    {
                        if (MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent,
                            (byte) MainV2.comPort.compidcurrent, MAVLink.MAV_CMD.FLASH_BOOTLOADER, 0, 0, 0, 0, 290876,
                            0, 0))
                        {
                            CustomMessageBox.Show("Upgraded bootloader");
                        }
                        else
                        {
                            CustomMessageBox.Show("Failed to upgrade bootloader");
                        }
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
                    }
        }

        private void but_3dmap_Click(object sender, EventArgs e)
        {
            var ogl = new Map3D();
            var frm = ogl.ShowUserControl();
        }

        private void but_anonlog_Click(object sender, EventArgs e)
        {
            CustomMessageBox.Show("This is beta, please confirm the output file");
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Filter = "tlog or bin/log|*.tlog;*.bin;*.log";

                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    var ext = Path.GetExtension(ofd.FileName).ToLower();
                    if (ext == ".bin")
                        ext = ".log";
                    using (SaveFileDialog sfd = new SaveFileDialog())
                    {
                        sfd.DefaultExt = ext;
                        sfd.FileName = Path.GetFileNameWithoutExtension(ofd.FileName) + "-anon" + ext;
                        if (sfd.ShowDialog() == DialogResult.OK)
                        {
                            Privacy.anonymise(ofd.FileName, sfd.FileName);
                        }
                    }
                }
            }
        }

        private void but_messageinterval_Click(object sender, EventArgs e)
        {
            var form = new Form();
            FlowLayoutPanel flp = new FlowLayoutPanel();
            flp.Dock = DockStyle.Fill;
            flp.AutoSize = true;
            ComboBox cmb = new ComboBox();
            cmb.DataSource = Enum.GetNames(typeof(MAVLink.MAVLINK_MSG_ID)).ToSortedList((s, s1) => s.CompareTo(s1));
            cmb.Width += 50;
            Button but = new Button();
            but.Text = "Set";
            ComboBox cmbrate = new ComboBox();
            cmbrate.DataSource = Enumerable.Range(0, 200).ToList();

            but.Click += (o, args) =>
            {
                var rate = double.Parse(cmbrate.Text.ToString());
                var value = Enum.Parse(typeof(MAVLink.MAVLINK_MSG_ID), cmb.Text.ToString());
                float rateratio;
                if (rate <= 0)
                    rateratio = (float)rate;
                else
                    rateratio = 1.0f / (float) rate * 1000000.0f;
                try
                {
                    MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte) MainV2.comPort.compidcurrent,
                        MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL, (float) (int) value, rateratio
                        , 0, 0, 0, 0, 0);
                }
                catch (Exception ex)
                {
                    CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
                }
            };

            Button but2 = new Button();
            but2.Text = "Set All";
            but2.Click += (o, args) =>
            {
                var rate = double.Parse(cmbrate.Text.ToString());
                float rateratio;
                if (rate <= 0)
                    rateratio = (float)rate;
                else
                    rateratio = 1.0f / (float)rate * 1000000.0f;
                ((IList)cmb.DataSource).ForEach(a =>
               {
                   var value = Enum.Parse(typeof(MAVLink.MAVLINK_MSG_ID), a.ToString());
                   try
                   {
                       MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte) MainV2.comPort.compidcurrent,
                           MAVLink.MAV_CMD.SET_MESSAGE_INTERVAL, (float) (int) value,
                           rateratio, 0, 0, 0, 0, 0, false);
                   }
                   catch (Exception ex)
                   {
                       CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
                   }
               });
            };

            //cmb.SelectedIndexChanged += (o, args) => { MAVLink.MAVLINK_MSG_ID.GET_MESSAGE_INTERVAL };

            form.Controls.Add(flp);

            flp.Controls.Add(cmb);
            flp.Controls.Add(cmbrate);
            flp.Controls.Add(but);

            flp.Controls.Add(but2);

            form.Show(this);
        }

        private void BUT_xplane_Click(object sender, EventArgs e)
        {

        }

        private void but_disablearmswitch_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("Are you sure?", "", MessageBoxButtons.YesNo) == (int)DialogResult.Yes)
            {   
                var target_system = MainV2.comPort.sysidcurrent;
                if (target_system == 0) {
                    log.Info("Not toggling safety on sysid 0");
                    return;
                }
                var custom_mode = (MainV2.comPort.MAV.cs.sensors_enabled.motor_control && MainV2.comPort.MAV.cs.sensors_enabled.seen) ? 1u : 0u;
                var mode = new MAVLink.mavlink_set_mode_t() { custom_mode = custom_mode, target_system = (byte)(target_system )};
                MainV2.comPort.setMode(mode, MAVLink.MAV_MODE_FLAG.SAFETY_ARMED);
            }
        }

        private void but_hwids_Click(object sender, EventArgs e)
        {
            string value = "0";
            if (InputBox.Show("hwid", "Enter the ID number", ref value, false, true) == DialogResult.OK)
            {
                StringBuilder sb = new StringBuilder();
                var items = value.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                foreach (var item in items)
                {
                    var items2 = item.Split(new char[] { ' ', '\t' });

                    foreach (var item2 in items2)
                    {
                        uint uintvalue = 0;

                        if (uint.TryParse(item2, out uintvalue))
                        {
                            Device.DeviceStructure test1 = new Device.DeviceStructure(uintvalue);

                            sb.AppendLine(item.Replace('\t', ' ') + " = " + test1.ToString());

                        }
                    }
                }

                CustomMessageBox.Show(sb.ToString());
            }
        }

        private void but_packetbytes_Click(object sender, EventArgs e)
        {
            string input = "";
            InputBox.Show("input", "enter the hex byte data", ref input, false, true);

            var ishex = input.Contains("0x") || input.ToLower().Any(a => a >= 'a' && a <= 'f');

            var split = input.Replace("0x", ",").Split(new char[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var buffer = split.Select(a => ishex ? Convert.ToByte(a, 16) : (byte)Convert.ToInt32(a, 10));

            MAVLink.MavlinkParse parse = new MAVLink.MavlinkParse();

            var packet = parse.ReadPacket(new MemoryStream(buffer.ToArray()));

            CustomMessageBox.Show(packet?.ToString() +
                                  "\n" + packet.ToJSON(Formatting.Indented));
        }

        private void but_acbarohight_Click(object sender, EventArgs e)
        {
            var paramname = MainV2.comPort.MAV.param.ContainsKey("GND_ABS_PRESS") ? "GND_ABS_PRESS" : "BARO1_GND_PRESS";

            var currentQNH = MainV2.comPort.GetParam(paramname).ToString();
            //338.6388 pa => 100' = 30.48m
            CustomMessageBox.Show("use at your own risk!!!");

            NumericUpDown mavlinkNumericUpDown = new NumericUpDown();
            mavlinkNumericUpDown.Minimum = -100;
            mavlinkNumericUpDown.Maximum = 100;

            mavlinkNumericUpDown.Padding = new Padding(20);
            mavlinkNumericUpDown.ValueChanged += (o, args) =>
                {
                    MainV2.comPort.setParam(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, paramname, (float)(double.Parse(currentQNH) + (double)mavlinkNumericUpDown.Value * 11.1));
                };

            mavlinkNumericUpDown.ShowUserControl();
        }

        private async void But_stayoutest_Click(object sender, EventArgs e)
        {
            var list = new List<Locationwp>();
            var tl = (MainV2.comPort.MAV.cs.Location.gps_offset(-20, -25));
            var tr = (MainV2.comPort.MAV.cs.Location.gps_offset(50, -25));
            var br = (MainV2.comPort.MAV.cs.Location.gps_offset(50, 15));
            var bl = (MainV2.comPort.MAV.cs.Location.gps_offset(-20, 15));

            var cmd = MAVLink.MAV_CMD.FENCE_POLYGON_VERTEX_INCLUSION;

            //MAVLink.mavlink_mission_request_int_t req = new MAVLink.mavlink_mission_request_int_t();

            var frame = (byte)MAVLink.MAV_FRAME.GLOBAL_INT;
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tl.Lat, lng = tl.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tr.Lat, lng = tr.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = br.Lat, lng = br.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = bl.Lat, lng = bl.Lng });

            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tl.Lat, lng = tl.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tr.Lat, lng = tr.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = br.Lat, lng = br.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = bl.Lat, lng = bl.Lng });

            tl = (MainV2.comPort.MAV.cs.Location.gps_offset(-40, -45));
            tr = (MainV2.comPort.MAV.cs.Location.gps_offset(20, -45));
            br = (MainV2.comPort.MAV.cs.Location.gps_offset(20, 20));
            bl = (MainV2.comPort.MAV.cs.Location.gps_offset(-40, 20));

            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tl.Lat, lng = tl.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = tr.Lat, lng = tr.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = br.Lat, lng = br.Lng });
            list.Add(new Locationwp() { alt = 0, frame = frame, id = (ushort)cmd, p1 = 4, lat = bl.Lat, lng = bl.Lng });

            //mav_mission.upload(MainV2.comPort, MAVLink.MAV_MISSION_TYPE.FENCE, list);

            var mission = await mav_mission.download(MainV2.comPort, MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_MISSION_TYPE.MISSION).ConfigureAwait(true);
            var fence = await mav_mission.download(MainV2.comPort, MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_MISSION_TYPE.FENCE).ConfigureAwait(true);
            var rally = await mav_mission.download(MainV2.comPort, MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, MAVLink.MAV_MISSION_TYPE.RALLY).ConfigureAwait(true);
        }

        private void but_lockup_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("Lockup the autopilot??? this can cause a CRASH!!!!!!",
                    "Lockup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == (int)DialogResult.Yes)
                if (CustomMessageBox.Show("Lockup the autopilot??? this can cause a CRASH!!!!!!",
                        "Lockup", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == (int)DialogResult.Yes)
                    MainV2.comPort.doCommand(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid,
                        MAVLink.MAV_CMD.PREFLIGHT_REBOOT_SHUTDOWN,
                        42, 24, 71, 93, 0, 0, 0, false);
        }

        private void but_hexmavlink_Click(object sender, EventArgs e)
        {
            string input = "";
            InputBox.Show("", "enter the hex value 'fd0500001a0c1'", ref input);

            var packet = StringToByteArray(input);

            var mavpacket = new MAVLink.MavlinkParse().ReadPacket(new MemoryStream(packet));

            CustomMessageBox.Show(mavpacket.ToJSON(Formatting.Indented));
        }

        public static byte[] StringToByteArray(string hex)
        {
            return Enumerable.Range(0, hex.Length)
                .Where(x => x % 2 == 0)
                .Select(x => Convert.ToByte(hex.Substring(x, 2), 16))
                .ToArray();
        }

        private void but_remotedflogger_Click(object sender, EventArgs e)
        {
            RemoteLog.StartRemoteLog(MainV2.comPort, MainV2.comPort.sysidcurrent,
                (byte) MainV2.comPort.compidcurrent);
        }

        private void but_paramrestore_Click(object sender, EventArgs e)
        {
            CustomMessageBox.Show("This process make take a some time");

            using (var ofd = new OpenFileDialog
            {
                AddExtension = true,
                DefaultExt = ".param",
                RestoreDirectory = true,
                Filter = ParamFile.FileMask
            })
            {
                var dr = ofd.ShowDialog();

                if (dr == DialogResult.OK)
                {
                    var param2 = ParamFile.loadParamFile(ofd.FileName);

                    ProgressReporterDialogue prd = new ProgressReporterDialogue();

                    prd.DoWork += dialogue =>
                    {
                        List<string> fails = new List<string>();
                        var set = 0;
                        var alreadyset = 0;
                        dialogue.UpdateProgressAndStatus(-1, "Get All by Name");
                        // prefeed
                        foreach (var d in param2)
                        {
                            MainV2.comPort.GetParam(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, d.Key,
                                requireresponce: false);
                        }

                        dialogue.UpdateProgressAndStatus(-1, "Set Enable's");
                        // enables
                        foreach (var d in param2.Where(a=>a.Key.ToLower().Contains("enable")))
                        {
                            try
                            {
                                MainV2.comPort.setParam(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, d.Key,
                                    d.Value);
                            }
                            catch
                            {

                            }
                        }

                        foreach (var d in param2)
                        {
                            dialogue.UpdateProgressAndStatus(-1, "Set " + d.Key);
                            if (dialogue.doWorkArgs.CancelRequested)
                            {
                                dialogue.doWorkArgs.CancelAcknowledged = true;
                                return;
                            }

                            try
                            {
                                if (MainV2.comPort.MAV.param.ContainsKey(d.Key) &&
                                    MainV2.comPort.MAV.param[d.Key].Value == d.Value)
                                {
                                    alreadyset++;
                                    continue;
                                }

                                MainV2.comPort.GetParam(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, d.Key);
                                
                                if (d.Key.ToLower().Contains("_id"))
                                    MainV2.comPort.setParam(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, d.Key,
                                        0,
                                        true);

                                MainV2.comPort.setParam(MainV2.comPort.MAV.sysid, MainV2.comPort.MAV.compid, d.Key,
                                    d.Value,
                                    true);

                                set++;
                            }
                            catch
                            {
                                fails.Add(d.Key);
                            }
                        }

                        if (fails.Count > 0)
                            CustomMessageBox.Show("Set " + set + " params \nAlready Set " 
                                                  + alreadyset + " params \nFailed to set " 
                                                  + fails.Aggregate((a, b) => a + "\n" + b));
                        else
                            CustomMessageBox.Show("Set " + set + " params \nAlready Set "
                                                  + alreadyset + " params");

                    };

                    prd.RunBackgroundOperationAsync();
                }
            }
        }

        private void BUT_CoT_Click(object sender, EventArgs e)
        {
            new SerialOutputCoT().Show();
        }

        private void but_ManageCMDList_Click(object sender, EventArgs e)
        {
            var CMDList = new MavCommandSelection();
            CMDList.Show();
        }

        private void but_signfw_Click(object sender, EventArgs e)
        {
            OpenFileDialog ofd = new OpenFileDialog();
            ofd.Title = "APJ File";
            ofd.Filter = "*.apj|*.apj";
            if (ofd.ShowDialog() == DialogResult.OK)
            {
                OpenFileDialog ofd2 = new OpenFileDialog();
                ofd2.Title = "Param File";
                ofd2.Filter = "*.param|*.param|*.parm|*.parm";
                if (ofd2.ShowDialog() == DialogResult.OK)
                {
                    apj_tool.Process(ofd.FileName, ofd2.FileName);

                    CustomMessageBox.Show("The new APJ has been saved with the source APJ");
                }
            }
        }

        private void but_dfumode_Click(object sender, EventArgs e)
        {
            MainV2.comPort.doDFUBoot(MainV2.comPort.sysidcurrent, (byte) MainV2.comPort.compidcurrent);
        }

        // Perform a force calibration for accelerometers when restoring parameters to a board after a param wipe,
        // to mark the parameters as calibrated.
        private void BUT_forcecal_accel_Click(object sender, EventArgs e)
        {
            // Send MAV_CMD_PREFLIGHT_CALIBRATION with param5=76 (magic number)
            try
            {
                MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent,
                    MAVLink.MAV_CMD.PREFLIGHT_CALIBRATION, 0, 0, 0, 0, 76, 0, 0, true);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
            }
        }

        // Perform a force calibration for compasses when restoring parameters to a board after a param wipe,
        // to mark the parameters as calibrated.
        private void BUT_forcecal_mag_Click(object sender, EventArgs e)
        {
            // Send MAV_CMD_PREFLIGHT_CALIBRATION with param2=76 (magic number)
            try
            {
                MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent,
                    MAVLink.MAV_CMD.PREFLIGHT_CALIBRATION, 0, 76, 0, 0, 0, 0, 0, true);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(ex.ToString(), Strings.ERROR);
            }
        }
    }
}
