using MissionPlanner.Comms;
using System;
using System.Drawing;
using MissionPlanner.Utilities;
using System.Windows.Forms;

namespace MissionPlanner.Controls
{
    public partial class ConnectionControl : UserControl
    {
        public ConnectionControl()
        {
            InitializeComponent();
            this.linkLabel1.Click += (sender, e) =>
            {
                ShowLinkStats?.Invoke(this, EventArgs.Empty);
            };
            Restyle();
        }

        // the designer's width, which the menu bar was made for, and two rows of boxes in its height
        private const int DesignWidth = 238;
        private const int RowHeight = 22;
        private const int RowGap = 3;
        private bool arranging;

        // MainV2 scales its controls to its font (AutoScaleMode.Font), this one's designer widths with them, to about
        // three quarters: the boxes keep to the widths that gives
        private float scaleX = 1;

        // the links with no baud rate: those MainV2 turns the baud box off for, and the web socket
        private static readonly string[] NoBaud = {"UDP", "UDPCl", "TCP", "WS", "AUTO"};
        private bool baudShown = true;

        // the boxes, each in its flat frame
        private FlatComboFrame portFrame, baudFrame, sysidFrame;

        /// <summary>
        /// The corner's look, nothing of what it does: the boxes in flat frames in the theme (FlatComboFrame) in Segoe
        /// UI, their lists' rows taller and picked out in the accent, and two even rows (Arrange).
        /// </summary>
        private void Restyle()
        {
            var font = new Font("Segoe UI", 8.25F);
            SuspendLayout();

            foreach (var combo in new[] {cmb_Connection, cmb_Baud, cmb_sysid})
            {
                combo.Font = font;
                if (combo.DrawMode == DrawMode.Normal)
                {
                    // the port's list draws its own rows, with the ports' names
                    combo.DrawMode = DrawMode.OwnerDrawFixed;
                    combo.DrawItem += DrawListItem;
                }

                // the field as well as the list rows: two rows have to fit the menu bar
                combo.ItemHeight = 16;
            }

            portFrame = new FlatComboFrame(cmb_Connection);
            baudFrame = new FlatComboFrame(cmb_Baud);
            sysidFrame = new FlatComboFrame(cmb_sysid);
            Controls.Add(portFrame);
            Controls.Add(baudFrame);
            Controls.Add(sysidFrame);

            linkLabel1.Font = font;
            linkLabel1.LinkBehavior = LinkBehavior.HoverUnderline;
            linkLabel1.SizeChanged += (s, e) => Arrange();

            // the baud box only for a link that has a baud rate
            cmb_Connection.TextChanged += (s, e) => ShowBaudForPort();
            cmb_Connection.SelectedIndexChanged += (s, e) => ShowBaudForPort();
            ShowBaudForPort();

            MinimumSize = Size.Empty;
            Arrange();
            ResumeLayout(false);

            // the theme's colours are set by the time it shows
            Load += (s, e) => linkLabel1.LinkColor = linkLabel1.ActiveLinkColor = ThemeManager.ButBG;
        }

        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            if ((specified & BoundsSpecified.Width) != 0)
                scaleX *= factor.Width;
            base.ScaleControl(factor, specified);
        }

        // after a scale too: the boxes are scaled after this control, and lay out once they have been
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            Arrange();
        }

        // the boxes half as wide again as the designer's, for the names in them
        private const float Widen = 1.5f;

        private int Scaled(int designed)
        {
            return (int) Math.Round(designed * Widen * scaleX);
        }

        /// <summary>
        /// The port and baud over Stats and the vehicle, at the designer's widths as MainV2 scales them, right edges
        /// aligned; Stats centred on the vehicle's box, which takes the rest of its row.
        /// </summary>
        private void Arrange()
        {
            if (arranging)
                return;
            arranging = true;
            try
            {
                if (portFrame == null)
                    return;

                int width = Scaled(DesignWidth);
                if (baudShown)
                {
                    portFrame.SetBounds(0, 0, Scaled(121), RowHeight);
                    baudFrame.SetBounds(Scaled(127), 0, width - Scaled(127), RowHeight);
                }
                else
                {
                    // the port across the row
                    portFrame.SetBounds(0, 0, width, RowHeight);
                }

                int second = RowHeight + RowGap;
                linkLabel1.Location = new Point(2, second + (RowHeight - linkLabel1.Height) / 2);
                int left = Math.Min(linkLabel1.Right + 4, width / 2);
                sysidFrame.SetBounds(left, second, width - left, RowHeight);

                Size = new Size(width, second + RowHeight);
            }
            finally
            {
                arranging = false;
            }
        }

        /// <summary>
        /// Shows the baud box only for a link with a baud rate: not UDP, TCP, the web socket or AUTO. Only whether it
        /// shows: what it holds, and whether MainV2 has it enabled, stay as they are.
        /// </summary>
        private void ShowBaudForPort()
        {
            var port = cmb_Connection.Text;
            bool baud = !Array.Exists(NoBaud, name => string.Equals(name, port, StringComparison.OrdinalIgnoreCase));
            if (baud == baudShown)
                return;
            baudShown = baud;
            if (baudFrame != null)
                baudFrame.Visible = baud;
            Arrange();
        }

        /// <summary>A list's row: the box's colours, the one under the pointer picked out in the accent.</summary>
        private static void DrawRow(ComboBox combo, DrawItemEventArgs e, string text)
        {
            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected &&
                            (e.State & DrawItemState.ComboBoxEdit) != DrawItemState.ComboBoxEdit;
            var back = combo.BackColor;
            var accent = ThemeManager.ButBG;
            var fill = selected
                ? Color.FromArgb(back.R + (accent.R - back.R) * 45 / 100, back.G + (accent.G - back.G) * 45 / 100,
                    back.B + (accent.B - back.B) * 45 / 100)
                : back;
            using (var brush = new SolidBrush(fill))
                e.Graphics.FillRectangle(brush, e.Bounds);

            var bounds = new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 8, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, text, e.Font, bounds, combo.ForeColor,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix);
        }

        private void DrawListItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;
            var combo = (ComboBox) sender;
            // as the box shows it (the vehicles' through their Format)
            DrawRow(combo, e, combo.GetItemText(combo.Items[e.Index]));
        }

        public event EventHandler ShowLinkStats;

        public ComboBox CMB_baudrate
        {
            get { return this.cmb_Baud; }
        }

        public ComboBox CMB_serialport
        {
            get { return this.cmb_Connection; }
        }


        /// <summary>
        /// Called from the main form - set whether we are connected or not currently.
        /// UI will be updated accordingly
        /// </summary>
        /// <param name="isConnected">Whether we are connected</param>
        public void IsConnected(bool isConnected)
        {
            this.linkLabel1.Visible = isConnected;
            cmb_Baud.Enabled = !isConnected;
            cmb_Connection.Enabled = !isConnected;

            UpdateSysIDS();
        }

        private void ConnectionControl_MouseClick(object sender, MouseEventArgs e)
        {

        }

        private void cmb_Connection_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            ComboBox combo = sender as ComboBox;

            string text = combo.Items[e.Index].ToString();
            if (!MainV2.MONO)
            {
                // the name MainV2 looked up as it read the ports: a lookup here (WMI, up to a second a port) would
                // hold the list up as it draws
                text = text + " " + SerialPort.GetCachedNiceName(text);
            }

            DrawRow(combo, e, text);
        }

        public void UpdateSysIDS()
        {
            cmb_sysid.SelectedIndexChanged -= CMB_sysid_SelectedIndexChanged;

            var oldidx = cmb_sysid.SelectedIndex;

            cmb_sysid.Items.Clear();

            int selectidx = -1;

            foreach (var port in MainV2.Comports.ToArray())
            {
                var list = port.MAVlist.GetRawIDS();

                foreach (ulong item in list)
                {
                    var temp = new port_sysid() { compid = (byte)item, sysid = (uint)(item >> 8), port = port };

                    // exclude GCS's from the list
                    if (temp.compid == (int)MAVLink.MAV_COMPONENT.MAV_COMP_ID_MISSIONPLANNER)
                        continue;

                    var idx = cmb_sysid.Items.Add(temp);

                    if (temp.port == MainV2.comPort && temp.sysid == MainV2.comPort.sysidcurrent && temp.compid == MainV2.comPort.compidcurrent)
                    {
                        selectidx = idx;
                    }
                }
            }

            if (/*oldidx == -1 && */ selectidx != -1)
            {
                cmb_sysid.SelectedIndex = selectidx;
            }

            cmb_sysid.SelectedIndexChanged += CMB_sysid_SelectedIndexChanged;
        }

        internal struct port_sysid
        {
            internal MAVLinkInterface port;
            internal uint sysid;
            internal int compid;
        }

        private void CMB_sysid_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (cmb_sysid.SelectedItem == null)
                return;

            var temp = (port_sysid)cmb_sysid.SelectedItem;

            foreach (var port in MainV2.Comports)
            {
                if (port == temp.port)
                {
                    MainV2.comPort = port;
                    MainV2.comPort.sysidcurrent = temp.sysid;
                    MainV2.comPort.compidcurrent = temp.compid;

                    // the vehicles of a link are loaded in the background after the connect: one that is waiting
                    // goes to the front of that queue, and only one it can't load gets the download with the dialog
                    // Ctrl skips the parameters, the background download as well
                    var loading = Control.ModifierKeys == Keys.Control
                        ? (VehicleParamLoader.IsComplete(MainV2.comPort.MAV) ? ParamLoadState.Loaded : ParamLoadState.None)
                        : VehicleParamLoader.Instance.Prioritise(MainV2.comPort, temp.sysid, (byte) temp.compid);

                    if (loading == ParamLoadState.Loaded)
                    {
                        // what shows this vehicle's parameters reloads on this, as after a download
                        MainV2.comPort.OnParamListChanged();
                    }
                    else if ((loading == ParamLoadState.NeedsManual || loading == ParamLoadState.Failed ||
                              (loading == ParamLoadState.None &&
                               MainV2.comPort.MAV.param.TotalReceived < MainV2.comPort.MAV.param.TotalReported)) &&
                        /*MainV2.comPort.MAV.compid == (byte)MAVLink.MAV_COMPONENT.MAV_COMP_ID_AUTOPILOT1 && */
                        !(Control.ModifierKeys == Keys.Control))
                        MainV2.comPort.getParamList();

                    MainV2.View.Reload();
                }
            }
        }

        private void cmb_sysid_Format(object sender, ListControlConvertEventArgs e)
        {
            var temp = (port_sysid)e.Value;
            MAVLink.MAV_COMPONENT compid = (MAVLink.MAV_COMPONENT)temp.compid;
            string mavComponentHeader = "MAV_COMP_ID_";
            string mavComponentString = null;

            foreach (var port in MainV2.Comports)
            {
                if (port == temp.port)
                {
                    if (compid == (MAVLink.MAV_COMPONENT)1)
                    {
                        //use Autopilot type as displaystring instead of "FCS1"
                        mavComponentString = port.MAVlist[temp.sysid, temp.compid].aptype.ToString();
                    }
                    else
                    {
                        //use name from enum if it exists, use the component ID otherwise
                        mavComponentString = compid.ToString();
                        if (mavComponentString.Length > mavComponentHeader.Length)
                        {
                            //remove "MAV_COMP_ID_" header
                            mavComponentString = mavComponentString.Remove(0, mavComponentHeader.Length);
                        }

                        if (temp.port.MAVlist[temp.sysid, temp.compid].CANNode)
                            mavComponentString =
                                temp.compid + " " + temp.port.MAVlist[temp.sysid, temp.compid].VersionString;
                    }
                    e.Value = temp.port.BaseStream.PortName + "-" + temp.sysid + "-" + mavComponentString.Replace("_", " ");
                }
            }
        }
    }
}