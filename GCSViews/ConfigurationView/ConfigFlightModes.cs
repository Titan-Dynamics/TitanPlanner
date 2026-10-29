using MissionPlanner.ArduPilot;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    public partial class ConfigFlightModes : MyUserControl, IActivate, IDeactivate
    {
        [Flags]
        public enum SimpleMode
        {
            None = 0,
            Simple1 = 1,
            Simple2 = 2,
            Simple3 = 4,
            Simple4 = 8,
            Simple5 = 16,
            Simple6 = 32
        }

        private readonly Timer _timer = new Timer();
        private bool _modeChannelInitializing;
        // set while Activate fills the controls from the params, so only user changes are written
        private bool _loading;
        private TableLayoutPanel _userParamsPanel;
        private GroupBox _rcOptionsGroup;

        // User-defined parameters section
        public string[] UserParamOptions { get; set; } = new string[]
        {
            "RC5_OPTION",
            "RC6_OPTION",
            "RC7_OPTION",
            "RC8_OPTION",
            "RC9_OPTION",
            "RC10_OPTION",
            "RC11_OPTION",
            "RC12_OPTION"
        };

        public ConfigFlightModes()
        {
            try
            {
                InitializeComponent();

                BuildLayout();

                CB_simple1.CheckedChanged += simple_CheckedChanged;
                CB_simple2.CheckedChanged += simple_CheckedChanged;
                CB_simple3.CheckedChanged += simple_CheckedChanged;
                CB_simple4.CheckedChanged += simple_CheckedChanged;
                CB_simple5.CheckedChanged += simple_CheckedChanged;
                CB_simple6.CheckedChanged += simple_CheckedChanged;
                chk_ss1.CheckedChanged += simple_CheckedChanged;
                chk_ss2.CheckedChanged += simple_CheckedChanged;
                chk_ss3.CheckedChanged += simple_CheckedChanged;
                chk_ss4.CheckedChanged += simple_CheckedChanged;
                chk_ss5.CheckedChanged += simple_CheckedChanged;
                chk_ss6.CheckedChanged += simple_CheckedChanged;

                // these write as soon as they change, so scrolling the page over one must not change it
                foreach (var cmb in new[] { CMB_fmode1, CMB_fmode2, CMB_fmode3, CMB_fmode4, CMB_fmode5, CMB_fmode6, CMB_modeChannel })
                    cmb.MouseWheel += IgnoreWheelUnlessOpen;

                // Load user-defined params from settings
                if (Settings.Instance.ContainsKey("FlightModeUserParams"))
                    UserParamOptions = Settings.Instance["FlightModeUserParams"].Split(',');
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        /// <summary>
        /// Puts the flight mode table and the RC options in their own boxes, side by side when the page is
        /// wide enough and stacked when it is not.
        /// </summary>
        private void BuildLayout()
        {
            SuspendLayout();

            // the mode channel row also shows the channel's live PWM
            LBL_flightmodepwm.AutoSize = true;
            LBL_flightmodepwm.Anchor = AnchorStyles.Left;
            tableLayoutPanel1.Controls.Add(LBL_flightmodepwm, 2, 0);
            tableLayoutPanel1.SetColumnSpan(LBL_flightmodepwm, 3);

            // Columns fit what they hold, so the PWM ranges sit beside the modes when the Copter-only
            // simple columns are hidden. The mode dropdowns fill their cell and have no width of their own.
            tableLayoutPanel1.ColumnStyles.Clear();
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // the last row holds only the (wrapping) simple mode link
            tableLayoutPanel1.RowStyles.Clear();
            for (var row = 0; row < tableLayoutPanel1.RowCount; row++)
                tableLayoutPanel1.RowStyles.Add(row < tableLayoutPanel1.RowCount - 1
                    ? new RowStyle(SizeType.Absolute, 28)
                    : new RowStyle(SizeType.AutoSize));
            tableLayoutPanel1.SetColumnSpan(linkLabel1_ss, 2);

            tableLayoutPanel1.AutoSize = true;
            tableLayoutPanel1.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Margin = new Padding(0);

            var flightModesGroup = CreateGroupBox("Flight Modes");
            Controls.Remove(tableLayoutPanel1);
            flightModesGroup.Controls.Add(tableLayoutPanel1);

            // filled by LoadUserParams on Activate
            _rcOptionsGroup = CreateGroupBox("RC Options");

            var layoutRoot = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                Padding = new Padding(10),
                Margin = new Padding(0)
            };
            layoutRoot.Controls.Add(flightModesGroup);
            layoutRoot.Controls.Add(_rcOptionsGroup);
            Controls.Add(layoutRoot);

            ResumeLayout(true);
        }

        private static void IgnoreWheelUnlessOpen(object sender, MouseEventArgs e)
        {
            if (!((ComboBox)sender).DroppedDown && e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }

        private static GroupBox CreateGroupBox(string title)
        {
            return new GroupBox
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(10),
                Text = title,
                Margin = new Padding(5)
            };
        }

        public void Activate()
        {
            _loading = true;

            if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduPlane ||
                MainV2.comPort.MAV.cs.firmware == Firmwares.Ateryx) // APM
            {
                CB_simple1.Visible = false;
                CB_simple2.Visible = false;
                CB_simple3.Visible = false;
                CB_simple4.Visible = false;
                CB_simple5.Visible = false;
                CB_simple6.Visible = false;

                chk_ss1.Visible = false;
                chk_ss2.Visible = false;
                chk_ss3.Visible = false;
                chk_ss4.Visible = false;
                chk_ss5.Visible = false;
                chk_ss6.Visible = false;

                linkLabel1_ss.Visible = false;

                try
                {
                    updateDropDown(CMB_fmode1, "FLTMODE1");
                    updateDropDown(CMB_fmode2, "FLTMODE2");
                    updateDropDown(CMB_fmode3, "FLTMODE3");
                    updateDropDown(CMB_fmode4, "FLTMODE4");
                    updateDropDown(CMB_fmode5, "FLTMODE5");
                    updateDropDown(CMB_fmode6, "FLTMODE6");

                    CMB_fmode1.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE1"].ToString());
                    CMB_fmode2.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE2"].ToString());
                    CMB_fmode3.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE3"].ToString());
                    CMB_fmode4.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE4"].ToString());
                    CMB_fmode5.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE5"].ToString());
                    CMB_fmode6.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE6"].ToString());
                }
                catch
                {
                }
            }
            else if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduRover) // APM
            {
                CB_simple1.Visible = false;
                CB_simple2.Visible = false;
                CB_simple3.Visible = false;
                CB_simple4.Visible = false;
                CB_simple5.Visible = false;
                CB_simple6.Visible = false;

                chk_ss1.Visible = false;
                chk_ss2.Visible = false;
                chk_ss3.Visible = false;
                chk_ss4.Visible = false;
                chk_ss5.Visible = false;
                chk_ss6.Visible = false;

                linkLabel1_ss.Visible = false;

                try
                {
                    updateDropDown(CMB_fmode1, "MODE1");
                    updateDropDown(CMB_fmode2, "MODE2");
                    updateDropDown(CMB_fmode3, "MODE3");
                    updateDropDown(CMB_fmode4, "MODE4");
                    updateDropDown(CMB_fmode5, "MODE5");
                    updateDropDown(CMB_fmode6, "MODE6");

                    CMB_fmode1.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE1"].ToString());
                    CMB_fmode2.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE2"].ToString());
                    CMB_fmode3.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE3"].ToString());
                    CMB_fmode4.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE4"].ToString());
                    CMB_fmode5.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE5"].ToString());
                    CMB_fmode6.SelectedValue = int.Parse(MainV2.comPort.MAV.param["MODE6"].ToString());
                }
                catch
                {
                }
            }
            else if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2) // ac2
            {
                if (MainV2.DisplayConfiguration.standardFlightModesOnly)
                {
                    CB_simple1.Visible = false;
                    CB_simple2.Visible = false;
                    CB_simple3.Visible = false;
                    CB_simple4.Visible = false;
                    CB_simple5.Visible = false;
                    CB_simple6.Visible = false;

                    chk_ss1.Visible = false;
                    chk_ss2.Visible = false;
                    chk_ss3.Visible = false;
                    chk_ss4.Visible = false;
                    chk_ss5.Visible = false;
                    chk_ss6.Visible = false;

                    linkLabel1_ss.Visible = false;
                }
                try
                {
                    updateDropDown(CMB_fmode1, "FLTMODE1");
                    updateDropDown(CMB_fmode2, "FLTMODE2");
                    updateDropDown(CMB_fmode3, "FLTMODE3");
                    updateDropDown(CMB_fmode4, "FLTMODE4");
                    updateDropDown(CMB_fmode5, "FLTMODE5");
                    updateDropDown(CMB_fmode6, "FLTMODE6");

                    CMB_fmode1.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE1"].ToString());
                    CMB_fmode2.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE2"].ToString());
                    CMB_fmode3.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE3"].ToString());
                    CMB_fmode4.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE4"].ToString());
                    CMB_fmode5.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE5"].ToString());
                    CMB_fmode6.SelectedValue = int.Parse(MainV2.comPort.MAV.param["FLTMODE6"].ToString());
                    CMB_fmode6.Enabled = true;

                    if (MainV2.comPort.MAV.param.ContainsKey("SIMPLE"))
                    {
                        var simple = int.Parse(MainV2.comPort.MAV.param["SIMPLE"].ToString());

                        CB_simple1.Checked = ((simple >> 0 & 1) == 1);
                        CB_simple2.Checked = ((simple >> 1 & 1) == 1);
                        CB_simple3.Checked = ((simple >> 2 & 1) == 1);
                        CB_simple4.Checked = ((simple >> 3 & 1) == 1);
                        CB_simple5.Checked = ((simple >> 4 & 1) == 1);
                        CB_simple6.Checked = ((simple >> 5 & 1) == 1);
                    }

                    if (MainV2.comPort.MAV.param.ContainsKey("SUPER_SIMPLE"))
                    {
                        var simple = int.Parse(MainV2.comPort.MAV.param["SUPER_SIMPLE"].ToString());

                        chk_ss1.Checked = ((simple >> 0 & 1) == 1);
                        chk_ss2.Checked = ((simple >> 1 & 1) == 1);
                        chk_ss3.Checked = ((simple >> 2 & 1) == 1);
                        chk_ss4.Checked = ((simple >> 3 & 1) == 1);
                        chk_ss5.Checked = ((simple >> 4 & 1) == 1);
                        chk_ss6.Checked = ((simple >> 5 & 1) == 1);
                    }
                }
                catch
                {
                }
            }
            else if (MainV2.comPort.MAV.cs.firmware == Firmwares.PX4) // APM
            {
                CB_simple1.Visible = false;
                CB_simple2.Visible = false;
                CB_simple3.Visible = false;
                CB_simple4.Visible = false;
                CB_simple5.Visible = false;
                CB_simple6.Visible = false;

                chk_ss1.Visible = false;
                chk_ss2.Visible = false;
                chk_ss3.Visible = false;
                chk_ss4.Visible = false;
                chk_ss5.Visible = false;
                chk_ss6.Visible = false;

                linkLabel1_ss.Visible = false;

                try
                {
                    updateDropDown(CMB_fmode1, "COM_FLTMODE1");
                    CMB_fmode1.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE1", "PX4");
                    updateDropDown(CMB_fmode2, "COM_FLTMODE2");
                    CMB_fmode2.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE2", "PX4");
                    updateDropDown(CMB_fmode3, "COM_FLTMODE3");
                    CMB_fmode3.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE3", "PX4");
                    updateDropDown(CMB_fmode4, "COM_FLTMODE4");
                    CMB_fmode4.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE4", "PX4");
                    updateDropDown(CMB_fmode5, "COM_FLTMODE5");
                    CMB_fmode5.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE5", "PX4");
                    updateDropDown(CMB_fmode6, "COM_FLTMODE6");
                    CMB_fmode6.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("COM_FLTMODE6", "PX4");

                    CMB_fmode1.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE1"].ToString());
                    CMB_fmode2.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE2"].ToString());
                    CMB_fmode3.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE3"].ToString());
                    CMB_fmode4.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE4"].ToString());
                    CMB_fmode5.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE5"].ToString());
                    CMB_fmode6.SelectedValue = int.Parse(MainV2.comPort.MAV.param["COM_FLTMODE6"].ToString());
                }
                catch
                {
                }
            }

            _loading = false;

            _timer.Tick += timer_Tick;

            _timer.Enabled = true;
            _timer.Interval = 100;
            _timer.Start();

            SetupModeChannelSelector();
            LoadUserParams();
        }

        public void Deactivate()
        {
            _timer.Stop();
        }

        private void timer_Tick(object sender, EventArgs e)
        {
            try
            {
                MainV2.comPort.MAV.cs.UpdateCurrentSettings(currentStateBindingSource.UpdateDataSource(MainV2.comPort.MAV.cs));
            }
            catch
            {
            }

            float pwm = 0;


            if (MainV2.comPort.MAV.param.ContainsKey("FLTMODE_CH") ||
                MainV2.comPort.MAV.param.ContainsKey("MODE_CH"))
            {
                var sw = 0;
                if (MainV2.comPort.MAV.param.ContainsKey("FLTMODE_CH"))
                {
                    sw = (int)MainV2.comPort.MAV.param["FLTMODE_CH"].Value;
                }
                else
                {
                    sw = (int)MainV2.comPort.MAV.param["MODE_CH"].Value;
                }

                switch (sw)
                {
                    case 5:
                        pwm = MainV2.comPort.MAV.cs.ch5in;
                        break;
                    case 6:
                        pwm = MainV2.comPort.MAV.cs.ch6in;
                        break;
                    case 7:
                        pwm = MainV2.comPort.MAV.cs.ch7in;
                        break;
                    case 8:
                        pwm = MainV2.comPort.MAV.cs.ch8in;
                        break;
                    case 9:
                        pwm = MainV2.comPort.MAV.cs.ch9in;
                        break;
                    case 10:
                        pwm = MainV2.comPort.MAV.cs.ch10in;
                        break;
                    case 11:
                        pwm = MainV2.comPort.MAV.cs.ch11in;
                        break;
                    case 12:
                        pwm = MainV2.comPort.MAV.cs.ch12in;
                        break;
                    case 13:
                        pwm = MainV2.comPort.MAV.cs.ch13in;
                        break;
                    case 14:
                        pwm = MainV2.comPort.MAV.cs.ch14in;
                        break;
                    case 15:
                        pwm = MainV2.comPort.MAV.cs.ch15in;
                        break;
                    case 16:
                        pwm = MainV2.comPort.MAV.cs.ch16in;
                        break;
                    default:

                        break;
                }

                LBL_flightmodepwm.Text = "PWM: " + pwm;
            }

            Control[] fmodelist = { CMB_fmode1, CMB_fmode2, CMB_fmode3, CMB_fmode4, CMB_fmode5, CMB_fmode6 };

            foreach (var ctl in fmodelist)
            {
                ThemeManager.ApplyThemeTo(ctl);
            }

            var no = readSwitch(pwm);

            fmodelist[no].BackColor = ThemeManager.CurrentPPMBackground;
        }

        // from arducopter code
        private byte readSwitch(float inpwm)
        {
            var pulsewidth = (int)inpwm; // default for Arducopter

            if (pulsewidth > 1230 && pulsewidth <= 1360) return 1;
            if (pulsewidth > 1360 && pulsewidth <= 1490) return 2;
            if (pulsewidth > 1490 && pulsewidth <= 1620) return 3;
            if (pulsewidth > 1620 && pulsewidth <= 1749) return 4; // Software Manual
            if (pulsewidth >= 1750) return 5; // Hardware Manual
            return 0;
        }

        // The mode params by firmware: FLTMODEn, MODEn on Rover, COM_FLTMODEn on PX4. A dropdown writes its
        // param as soon as the user picks a mode, like the RC Options rows.
        private void WriteFlightMode(ComboBox cmb)
        {
            if (_loading || cmb.SelectedValue == null)
                return;

            int value;
            if (!int.TryParse(cmb.SelectedValue.ToString(), out value))
                return;

            var number = cmb.Name.Substring(cmb.Name.Length - 1);
            foreach (var prefix in new[] { "FLTMODE", "MODE", "COM_FLTMODE" })
            {
                var param = prefix + number;
                if (MainV2.comPort.MAV.param.ContainsKey(param))
                {
                    ParamWriteQueue.Write(param, value);
                    return;
                }
            }
        }

        // SIMPLE and SUPER_SIMPLE (Copter) hold one bit per flight mode slot
        private void simple_CheckedChanged(object sender, EventArgs e)
        {
            if (_loading || MainV2.comPort.MAV.cs.firmware != Firmwares.ArduCopter2)
                return;

            if (Array.IndexOf(new[] { CB_simple1, CB_simple2, CB_simple3, CB_simple4, CB_simple5, CB_simple6 },
                    sender) >= 0)
                WriteSimpleMask("SIMPLE", CB_simple1, CB_simple2, CB_simple3, CB_simple4, CB_simple5, CB_simple6);
            else
                WriteSimpleMask("SUPER_SIMPLE", chk_ss1, chk_ss2, chk_ss3, chk_ss4, chk_ss5, chk_ss6);
        }

        private static void WriteSimpleMask(string param, params CheckBox[] slots)
        {
            if (!MainV2.comPort.MAV.param.ContainsKey(param))
                return;

            var value = 0;
            for (var i = 0; i < slots.Length; i++)
            {
                if (slots[i].Checked)
                    value |= 1 << i;
            }

            ParamWriteQueue.Write(param, value);
        }

        private void updateDropDown(ComboBox ctl, string param)
        {
            ctl.DataSource = ArduPilot.Common.getModesList(MainV2.comPort.MAV.cs.firmware);
            ctl.DisplayMember = "Value";
            ctl.ValueMember = "Key";
        }

        private void linkLabel1_ss_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo("https://ardupilot.org/copter/docs/simpleandsuper-simple-modes.html") { UseShellExecute = true });
            }
            catch
            {
                CustomMessageBox.Show(Strings.ERROR +
                                      " https://ardupilot.org/copter/docs/simpleandsuper-simple-modes.html");
            }
        }

        private void flightmode_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (MainV2.comPort.MAV.cs.firmware == Firmwares.ArduCopter2)
            {
                var sender2 = (Control)sender;
                var currentmode = sender2.Text.ToLower();

                if (currentmode.Contains("althold") || currentmode.Contains("auto") ||
                    currentmode.Contains("autotune") || currentmode.Contains("land") ||
                    currentmode.Contains("loiter") || currentmode.Contains("ofloiter") ||
                    currentmode.Contains("poshold") || currentmode.Contains("rtl") ||
                    currentmode.Contains("sport") || currentmode.Contains("stabilize") ||
                    currentmode.Contains("flowhold") || currentmode.Contains("zigzag"))
                {
                    //CMB_fmode1
                    //CB_simple1
                    //chk_ss1

                    var number = sender2.Name.Substring(sender2.Name.Length - 1);

                    findandenableordisable("CB_simple" + number, true);
                    findandenableordisable("chk_ss" + number, true);
                }
                else
                {
                    var number = sender2.Name.Substring(sender2.Name.Length - 1);

                    findandenableordisable("CB_simple" + number, false);
                    findandenableordisable("chk_ss" + number, false);
                }
            }

            WriteFlightMode((ComboBox)sender);
        }

        private void findandenableordisable(string ctl, bool enable)
        {
            var items = Controls.Find(ctl, true);

            if (items.Length > 0)
            {
                items[0].Enabled = enable;
            }
        }

        private void SetupModeChannelSelector()
        {
            _modeChannelInitializing = true;

            CMB_modeChannel.DisplayMember = "Value";
            CMB_modeChannel.ValueMember = "Key";
            CMB_modeChannel.DataSource = null;

            var list = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<int, string>>();
            list.Add(new System.Collections.Generic.KeyValuePair<int, string>(0, "0 - Disabled"));
            for (var i = 1; i <= 16; i++)
            {
                list.Add(new System.Collections.Generic.KeyValuePair<int, string>(i, i.ToString()));
            }

            CMB_modeChannel.DataSource = list;

            var current = 0;
            if (MainV2.comPort.MAV.param.ContainsKey("FLTMODE_CH"))
                current = (int)MainV2.comPort.MAV.param["FLTMODE_CH"].Value;
            else if (MainV2.comPort.MAV.param.ContainsKey("MODE_CH"))
                current = (int)MainV2.comPort.MAV.param["MODE_CH"].Value;

            if (current < 0) current = 0;
            if (current > 16) current = 16;

            CMB_modeChannel.SelectedValue = current;

            _modeChannelInitializing = false;
        }

        private void CMB_modeChannel_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_modeChannelInitializing)
                return;

            var selected = (int)CMB_modeChannel.SelectedValue;

            try
            {
                if (MainV2.comPort.MAV.param.ContainsKey("FLTMODE_CH"))
                    MainV2.comPort.setParam(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, "FLTMODE_CH", selected);
                else if (MainV2.comPort.MAV.param.ContainsKey("MODE_CH"))
                    MainV2.comPort.setParam(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent, "MODE_CH", selected);
            }
            catch
            {
                CustomMessageBox.Show(Strings.ErrorSettingParameter, Strings.ERROR);
            }
        }

        private void LoadUserParams()
        {
            // Remove existing panel if present
            if (_userParamsPanel != null)
            {
                _rcOptionsGroup.Controls.Remove(_userParamsPanel);
                _userParamsPanel.Dispose();
            }

            // Create user params panel; the "RC Options" box is its title
            _userParamsPanel = new TableLayoutPanel
            {
                ColumnCount = 2,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0)
            };
            _userParamsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _userParamsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

            // Add the panel to its box first, before adding child controls
            _rcOptionsGroup.Controls.Add(_userParamsPanel);

            // Add Modify button
            var modifyButton = new MyButton { Text = "Modify", AutoSize = true };
            modifyButton.Click += (o, e) =>
            {
                var opts = UserParamOptions.Aggregate((a, b) => a + "\r\n" + b);
                InputBox.Show("Params", "Enter Param Names", ref opts, false, true);
                UserParamOptions = opts.Split(new[] { ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                Settings.Instance["FlightModeUserParams"] = UserParamOptions.Aggregate((a, b) => a.Trim() + "," + b.Trim());
                LoadUserParams();
            };
            _userParamsPanel.RowCount++;
            _userParamsPanel.Controls.Add(modifyButton, 0, _userParamsPanel.RowCount - 1);
            _userParamsPanel.SetColumnSpan(modifyButton, 2);

            // Add each user param
            foreach (var option in UserParamOptions)
            {
                if (!MainV2.comPort.MAV.param.ContainsKey(option))
                    continue;

                _userParamsPanel.RowCount++;
                var label = new Label { Text = option, AutoSize = true, Anchor = AnchorStyles.Left };
                _userParamsPanel.Controls.Add(label, 0, _userParamsPanel.RowCount - 1);

                var options = ParameterMetaDataRepository.GetParameterOptionsInt(option, MainV2.comPort.MAV.cs.firmware.ToString());
                if (options.Count == 0)
                {
                    double min = 0, max = 0;
                    ParameterMetaDataRepository.GetParameterRange(option, ref min, ref max, MainV2.comPort.MAV.cs.firmware.ToString());
                    var num = new MavlinkNumericUpDown();
                    num.setup((float)min, (float)max, 1, 1, option, MainV2.comPort.MAV.param);
                    _userParamsPanel.Controls.Add(num, 1, _userParamsPanel.RowCount - 1);
                }
                else
                {
                    var cmb = new MavlinkComboBox();
                    _userParamsPanel.Controls.Add(cmb, 1, _userParamsPanel.RowCount - 1);
                    cmb.setup(options, option, MainV2.comPort.MAV.param);
                }
            }

            ThemeManager.ApplyThemeTo(_userParamsPanel);
        }
    }
}
