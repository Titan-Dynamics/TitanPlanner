using Microsoft.CodeAnalysis.CSharp.Syntax;
using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;


namespace MissionPlanner.Joystick
{
    public partial class JoystickSetup : MyUserControl, IDeactivate
    {
        bool startup = true;

        int noButtons = 0;
        private int maxaxis = 16;

        public JoystickSetup()
        {
            InitializeComponent();

            // the window's title (ShowUserControl takes it from Text)
            Text = "Joystick configuration";

            MissionPlanner.Utilities.Tracking.AddPage(this.GetType().ToString(), this.Text);
        }

        // left to right: the joystick's own inputs, the RC channels made from them, and the button assignments
        private GroupBox inputsGroup;
        private GroupBox channelsGroup;
        private GroupBox buttonsGroup;
        private Label inputsInfo;
        private Label inputsButtons;
        private Label buttonsInfo;
        private bool buttonRowsShown;

        // the live view reads the joystick on its own, enabled or not
        private JoystickBase inputsJoystick;
        private string inputsJoystickName;
        private DateTime inputsRetry;
        private readonly Dictionary<joystickaxis, InputRow> inputRows = new Dictionary<joystickaxis, InputRow>();
        private readonly Timer inputsTimer = new Timer { Interval = 50 };

        // an input's row: its name, a bar of where it is, and its value
        private class InputRow
        {
            public Label Name;
            public HorizontalProgressBar2 Bar;
            public Label Value;
            public bool Hat;
        }

        private const int InputNameWidth = 170;
        private const int InputBarWidth = 110;
        private const int InputValueWidth = 60;
        private const int InputRowHeight = 22;

        private static GroupBox CreateGroup(string title)
        {
            return new GroupBox
            {
                Text = title,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(6)
            };
        }

        /// <summary>
        /// Puts the groups side by side under the options and sizes the page (and its window) to them.
        /// </summary>
        private void LayoutGroups()
        {
            var top = Math.Max(CHK_elevons.Bottom, chk_manualcontrol.Bottom) + 8;
            inputsGroup.Location = new Point(5, top);
            channelsGroup.Location = new Point(inputsGroup.Right + 6, top);
            buttonsGroup.Location = new Point(channelsGroup.Right + 6, top);

            var right = Controls.Cast<Control>().Where(c => c.Visible).Max(c => c.Right);
            var bottom = Controls.Cast<Control>().Where(c => c.Visible).Max(c => c.Bottom);
            Size = new Size(right + 10, bottom + 10);
        }

        /// <summary>
        /// One bar per input the joystick has, named as the joystick names it, with the axis to choose for it.
        /// </summary>
        private void BuildInputRows()
        {
            foreach (var row in inputRows.Values)
            {
                foreach (var control in new Control[] { row.Name, row.Bar, row.Value })
                {
                    inputsGroup.Controls.Remove(control);
                    control.Dispose();
                }
            }

            inputRows.Clear();

            // the text first, so the rows start under however many lines it wraps to
            var rowWidth = InputNameWidth + InputBarWidth + InputValueWidth;
            inputsInfo.MaximumSize = new Size(rowWidth, 0);
            inputsInfo.Text = inputsJoystick != null
                ? "Move a control to see which input it is. Choose the name in brackets for an RC channel."
                : "Joystick not found. Plug it in, or choose it above.";

            var y = inputsInfo.Bottom + 8;
            if (inputsJoystick != null)
            {
                foreach (var axis in inputsJoystick.GetAxes())
                {
                    var row = new InputRow
                    {
                        Hat = axis.Key == joystickaxis.Hatud1 || axis.Key == joystickaxis.Hatlr2
                    };
                    row.Name = new Label
                    {
                        AutoSize = false,
                        AutoEllipsis = true,
                        Location = new Point(8, y),
                        Size = new Size(InputNameWidth, InputRowHeight),
                        TextAlign = ContentAlignment.MiddleLeft,
                        Text = axis.Value + " (" + axis.Key + ")"
                    };
                    row.Bar = new HorizontalProgressBar2
                    {
                        Location = new Point(row.Name.Right, y + 3),
                        Size = new Size(InputBarWidth, InputRowHeight - 6),
                        Minimum = 0,
                        // a hat reads its angle x100, -1 when centred
                        Maximum = row.Hat ? 36000 : 65535,
                        DrawLabel = false
                    };
                    row.Value = new Label
                    {
                        AutoSize = false,
                        Location = new Point(row.Bar.Right, y),
                        Size = new Size(InputValueWidth, InputRowHeight),
                        TextAlign = ContentAlignment.MiddleRight
                    };
                    inputsGroup.Controls.AddRange(new Control[] { row.Name, row.Bar, row.Value });
                    inputRows[axis.Key] = row;
                    y += InputRowHeight + 2;
                }
            }

            inputsButtons.Location = new Point(8, y + 6);
            inputsButtons.Text = inputsJoystick != null ? "Buttons pressed: none" : "";

            ThemeManager.ApplyThemeTo(inputsGroup);
            LayoutGroups();
        }

        private void CloseInputs()
        {
            try
            {
                inputsJoystick?.Dispose();
            }
            catch
            {
            }

            inputsJoystick = null;
        }

        private void inputsTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // (re)open on a new choice of joystick, and every couple of seconds while it isn't found
                if (inputsJoystickName != CMB_joysticks.Text ||
                    inputsJoystick == null && DateTime.Now > inputsRetry)
                {
                    var changed = inputsJoystickName != CMB_joysticks.Text;
                    CloseInputs();
                    inputsJoystickName = CMB_joysticks.Text;
                    inputsJoystick = JoystickBase.getJoyStickByName(inputsJoystickName);
                    inputsRetry = DateTime.Now.AddSeconds(2);
                    if (changed || inputsJoystick != null)
                        BuildInputRows();
                }

                if (inputsJoystick == null)
                    return;

                var state = inputsJoystick.GetCurrentState();
                var values = JoystickBase.ReadAxes(state);
                foreach (var row in inputRows)
                {
                    if (!values.TryGetValue(row.Key, out var value))
                        continue;

                    row.Value.Bar.Value = value;
                    row.Value.Value.Text = !row.Value.Hat ? value.ToString()
                        : value < 0 ? "centred" : value / 100 + "°";
                }

                // numbered as the button dropdowns number them
                var pressed = (state.GetButtons() ?? new bool[0])
                    .Select((down, number) => down ? number.ToString() : null)
                    .Where(number => number != null)
                    .ToList();
                inputsButtons.Text = "Buttons pressed: " + (pressed.Count > 0 ? string.Join(", ", pressed) : "none");
            }
            catch
            {
                // unplugged: show it as not found and look for it again
                CloseInputs();
                inputsRetry = DateTime.Now.AddSeconds(2);
                BuildInputRows();
            }
        }

        private void Joystick_Load(object sender, EventArgs e)
        {
            try
            {
                var joysticklist = JoystickBase.getDevices();

                foreach (var device in joysticklist)
                {
                    CMB_joysticks.Items.Add(device);
                }
            }
            catch
            {
                CustomMessageBox.Show("Error geting joystick list: do you have the directx redist installed?");
                this.Close();
                return;
            }

            if (CMB_joysticks.Items.Count > 0 && CMB_joysticks.SelectedIndex == -1)
                CMB_joysticks.SelectedIndex = 0;

            try
            {
                if (Settings.Instance.ContainsKey("joystick_name") && Settings.Instance["joystick_name"].ToString() != "")
                    CMB_joysticks.Text = Settings.Instance["joystick_name"].ToString();
            }
            catch
            {
            }

            try
            {
                if (Settings.Instance.ContainsKey("joy_elevons"))
                    CHK_elevons.Checked = bool.Parse(Settings.Instance["joy_elevons"].ToString());
            }
            catch
            {
            } // IF 1 DOESNT EXIST NONE WILL

            var tempjoystick = JoystickBase.Create(() => MainV2.comPort);

            label14.Text += " " + MainV2.comPort.MAV.cs.firmware.ToString();

            this.SuspendLayout();

            // options under the joystick row, then the groups
            CHK_elevons.Location = new Point(CMB_joysticks.Left, BUT_enable.Bottom + 8);
            chk_manualcontrol.Location = new Point(CHK_elevons.Right + 20, CHK_elevons.Top);

            inputsGroup = CreateGroup("Joystick inputs");
            inputsInfo = new Label { AutoSize = true, Location = new Point(8, 20) };
            inputsButtons = new Label { AutoSize = true };
            inputsGroup.Controls.Add(inputsInfo);
            inputsGroup.Controls.Add(inputsButtons);
            Controls.Add(inputsGroup);

            // the channel rows under their column headings
            channelsGroup = CreateGroup("RC channels");
            var headingTop = 20;
            foreach (var heading in new Control[] { label8, label7, label6, label9 })
            {
                heading.Parent = channelsGroup;
                heading.Location = new Point(heading.Left + 6, headingTop);
            }
            Controls.Add(channelsGroup);

            buttonsGroup = CreateGroup("Buttons");
            buttonsInfo = new Label
            {
                AutoSize = true,
                Location = new Point(8, 20),
                Text = "Shown once the joystick is read."
            };
            buttonsGroup.Controls.Add(buttonsInfo);
            Controls.Add(buttonsGroup);

            var y = label8.Bottom + 4;

            for (int a = 1; a <= maxaxis; a++)
            {
                var config = tempjoystick.getChannel(a);

                var ax = new JoystickAxis()
                {
                    ChannelNo = a,
                    Label = "RC " + a,
                    AxisArray = (Enum.GetValues(typeof(joystickaxis))),
                    ChannelValue = config.axis.ToString(),
                    ExpoValue = config.expo.ToString(),
                    ReverseValue = config.reverse,
                    Location = new Point(6, y),
                    Name = "axis" + a
                };

                ax.Detect = () => JoystickBase.getMovingAxis(CMB_joysticks.Text, 16000).ToString();
                ax.Reverse = () => MainV2.joystick?.setReverse(ax.ChannelNo, ax.ReverseValue);
                ax.SetAxis = () => MainV2.joystick?.setAxis(ax.ChannelNo,
                    (joystickaxis)Enum.Parse(typeof(joystickaxis), ax.ChannelValue));
                ax.GetValue = () =>
                {
                    return (short)MainV2.comPort.MAV.cs.GetType().GetField("rcoverridech" + ax.ChannelNo)
                        .GetValue(MainV2.comPort.MAV.cs);
                };
                ax.Expo = () =>
                {
                    if (int.TryParse(ax.ExpoValue, out int expoValue))
                    {
                        MainV2.joystick?.setExpo(ax.ChannelNo, expoValue);
                    }
                };

                channelsGroup.Controls.Add(ax);

                y += ax.Height;
            }

            BuildInputRows();

            this.ResumeLayout();

            // again with the groups sized to what they now hold
            LayoutGroups();

            // the window was themed before these groups were made
            ThemeManager.ApplyThemeTo(this);

            if (MainV2.joystick != null && MainV2.joystick.enabled)
                BUT_enable.Text = "Disable";

            // read the joystick for the channels and buttons whether or not it's enabled
            if (CMB_joysticks.Items.Count > 0)
                timer1.Start();

            inputsTimer.Tick += inputsTimer_Tick;
            inputsTimer.Start();

            startup = false;
        }

        int[] getButtonNumbers()
        {
            int[] temp = new int[128];
            temp[0] = -1;
            for (int a = 0; a < temp.Length - 1; a++)
            {
                temp[a + 1] = a;
            }
            return temp;
        }

        private void BUT_enable_Click(object sender, EventArgs e)
        {
            if (MainV2.joystick == null || MainV2.joystick.enabled == false)
            {
                try
                {
                    if (MainV2.joystick != null)
                        MainV2.joystick.UnAcquireJoyStick();
                }
                catch
                {
                }

                // all config is loaded from the xmls
                var joy = JoystickBase.Create(() => MainV2.comPort);

                joy.elevons = CHK_elevons.Checked;

                //show error message if a joystick is not connected when Enable is clicked
                if (!joy.start(CMB_joysticks.Text))
                {
                    CustomMessageBox.Show("Please Connect a Joystick", "No Joystick");
                    joy.Dispose();
                    return;
                }

                Settings.Instance["joystick_name"] = CMB_joysticks.Text;

                MainV2.joystick = joy;
                MainV2.joystick.enabled = true;

                BUT_enable.Text = "Disable";

                //timer1.Start();
            }
            else
            {
                MainV2.joystick.enabled = false;

                MainV2.joystick.clearRCOverride();

                MainV2.joystick = null;


                //timer1.Stop();

                BUT_enable.Text = "Enable";
            }
        }

        private void BUT_save_Click(object sender, EventArgs e)
        {
            if (MainV2.joystick == null)
            {
                CustomMessageBox.Show("Please select a joystick");
                return;
            }
            MainV2.joystick.saveconfig();

            Settings.Instance["joy_elevons"] = CHK_elevons.Checked.ToString();
        }

        /// <summary>
        /// The rows assigning the joystick's buttons, once it is being read (enabled, or read for this page).
        /// </summary>
        private void ShowButtonRows(JoystickBase joy)
        {
            if (buttonRowsShown || joy == null)
                return;

            noButtons = Math.Min(16, joy.getNumButtons());
            if (noButtons == 0)
                return;

            buttonRowsShown = true;

            buttonsGroup.SuspendLayout();
            buttonsGroup.Controls.Remove(buttonsInfo);

            var rowHeight = Controls.Find("axis" + 1, true).FirstOrDefault()?.Height ?? 28;
            for (int f = 0; f < noButtons; f++)
            {
                string name = (f).ToString();

                doButtontoUI(name, 8, 20 + f * rowHeight);

                var config = joy.getButton(f);

                joy.setButton(f, config);
            }

            buttonsGroup.ResumeLayout();

            ThemeManager.ApplyThemeTo(buttonsGroup);

            LayoutGroups();
        }

        private void timer1_Tick(object sender, EventArgs e)
        {
            try
            {
                if (MainV2.joystick != null && MainV2.joystick.enabled)
                    ShowButtonRows(MainV2.joystick);

                if (MainV2.joystick == null || MainV2.joystick.enabled == false)
                {
                    //Console.WriteLine(DateTime.Now.Millisecond + " start ");
                    var joy = MainV2.joystick;
                    if (joy == null)
                    {
                        joy = JoystickBase.Create(() => MainV2.comPort);
                        for (int a = 1; a <= maxaxis; a++)
                        {
                            var config = joy.getChannel(a);

                            joy.setChannel(a, config.axis, config.reverse, config.expo);
                        }

                        joy.elevons = CHK_elevons.Checked;

                        joy.AcquireJoystick(CMB_joysticks.Text);

                        joy.name = CMB_joysticks.Text;

                        MainV2.joystick = joy;

                        CMB_joysticks.SelectedIndex = CMB_joysticks.Items.IndexOf(joy.name);
                    }

                    ShowButtonRows(MainV2.joystick);

                    MainV2.joystick.elevons = CHK_elevons.Checked;

                    MainV2.comPort.MAV.cs.rcoverridech1 = joy.getValueForChannel(1);
                    MainV2.comPort.MAV.cs.rcoverridech2 = joy.getValueForChannel(2);
                    MainV2.comPort.MAV.cs.rcoverridech3 = joy.getValueForChannel(3);
                    MainV2.comPort.MAV.cs.rcoverridech4 = joy.getValueForChannel(4);
                    MainV2.comPort.MAV.cs.rcoverridech5 = joy.getValueForChannel(5);
                    MainV2.comPort.MAV.cs.rcoverridech6 = joy.getValueForChannel(6);
                    MainV2.comPort.MAV.cs.rcoverridech7 = joy.getValueForChannel(7);
                    MainV2.comPort.MAV.cs.rcoverridech8 = joy.getValueForChannel(8);
                    MainV2.comPort.MAV.cs.rcoverridech9 = joy.getValueForChannel(9);
                    MainV2.comPort.MAV.cs.rcoverridech10 = joy.getValueForChannel(10);
                    MainV2.comPort.MAV.cs.rcoverridech11 = joy.getValueForChannel(11);
                    MainV2.comPort.MAV.cs.rcoverridech12 = joy.getValueForChannel(12);
                    MainV2.comPort.MAV.cs.rcoverridech13 = joy.getValueForChannel(13);
                    MainV2.comPort.MAV.cs.rcoverridech14 = joy.getValueForChannel(14);
                    MainV2.comPort.MAV.cs.rcoverridech15 = joy.getValueForChannel(15);
                    MainV2.comPort.MAV.cs.rcoverridech16 = joy.getValueForChannel(16);
                    MainV2.comPort.MAV.cs.rcoverridech17 = joy.getValueForChannel(17);
                    MainV2.comPort.MAV.cs.rcoverridech18 = joy.getValueForChannel(18);

                    //Console.WriteLine(DateTime.Now.Millisecond + " end ");
                }
            }
            catch (SharpDX.SharpDXException ex)
            {
                ex.ToString();
                if (MainV2.joystick != null && MainV2.joystick.enabled == true)
                {
                    BUT_enable_Click(null, null);
                }

                if (ex.Message.Contains("DIERR_NOTACQUIRED"))
                    MainV2.joystick = null;
            }
            catch
            {
                
            }

            try
            {
                for (int f = 0; f < noButtons; f++)
                {
                    string name = (f).ToString();

                    var items = buttonsGroup.Controls.Find("hbar" + name, false);

                    if (items.Length > 0)
                        ((HorizontalProgressBar)items[0]).Value =
                            MainV2.joystick.isButtonPressed(f) ? 100 : 0;
                }
            }
            catch
            {
            } // this is for buttons - silent fail
        }

        private void CMB_joysticks_Click(object sender, EventArgs e)
        {
            CMB_joysticks.Items.Clear();

            var joysticklist = JoystickBase.getDevices();

            foreach (var device in joysticklist)
            {
                CMB_joysticks.Items.Add(device);
            }

            if (CMB_joysticks.Items.Count > 0 && CMB_joysticks.SelectedIndex == -1)
                CMB_joysticks.SelectedIndex = 0;
        }

        private void cmbbutton_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (startup)
                return;

            string name = ((ComboBox)sender).Name.Replace("cmbbutton", "");

            MainV2.joystick.changeButton((int.Parse(name)), int.Parse(((ComboBox)sender).Text));
        }

        private void BUT_detbutton_Click(object sender, EventArgs e)
        {
            string name = ((MyButton)sender).Name.Replace("mybut", "");

            ComboBox cmb = (ComboBox)(buttonsGroup.Controls.Find("cmbbutton" + name, false)[0]);
            cmb.Text = JoystickBase.getPressedButton(CMB_joysticks.Text).ToString();
        }

        void doButtontoUI(string name, int x, int y)
        {
            MyLabel butlabel = new MyLabel();
            ComboBox butnumberlist = new ComboBox();
            Controls.MyButton but_detect = new Controls.MyButton();
            HorizontalProgressBar hbar = new HorizontalProgressBar();
            ComboBox cmbaction = new ComboBox();
            Controls.MyButton but_settings = new Controls.MyButton();

            if (MainV2.joystick == null)
            {
                butlabel.Dispose();
                butnumberlist.Dispose();
                but_detect.Dispose();
                hbar.Dispose();
                cmbaction.Dispose();
                but_settings.Dispose();
                return;
            }

            var config = MainV2.joystick.getButton(int.Parse(name));


            butlabel.Location = new Point(x, y);
            butlabel.Size = new Size(47, 13);
            butlabel.Text = "But " + (int.Parse(name) + 1);

            butnumberlist.Location = new Point(butlabel.Right, y);
            butnumberlist.Size = new Size(70, 21);
            //butnumberlist.DataSource = getButtonNumbers();

            butnumberlist.Items.AddRange(getButtonNumbers().Select(item => item.ToString()).ToArray());
            butnumberlist.SelectedIndex = 0;


            butnumberlist.DropDownStyle = ComboBoxStyle.DropDownList;
            butnumberlist.Name = "cmbbutton" + name;

            //butnumberlist.SelectedItem = "-1";
            butnumberlist.SelectedItem = config.buttonno.ToString();

            //if (Settings.Instance["butno" + name] != null)
            //    butnumberlist.Text = (Settings.Instance["butno" + name].ToString());
            //if (config.buttonno != -1)

            //butnumberlist.Text = config.buttonno.ToString();

            butnumberlist.SelectedIndexChanged += new EventHandler(cmbbutton_SelectedIndexChanged);

            but_detect.Location = new Point(butnumberlist.Right, y);
            //but_detect.Size = BUT_detch1.Size;
            but_detect.Text = "Detect";
            but_detect.AutoSize = true;

            but_detect.Name = "mybut" + name;
            but_detect.Click += new EventHandler(BUT_detbutton_Click);

            hbar.Location = new Point(but_detect.Right, y);
            hbar.Size = new Size(100, 21);
            hbar.Name = "hbar" + name;

            cmbaction.Location = new Point(hbar.Right + 5, y);
            cmbaction.Size = new Size(100, 21);

            //cmbaction.DataSource = Enum.GetNames(typeof(buttonfunction));
            cmbaction.Items.AddRange(Enum.GetNames(typeof(buttonfunction)));


            //Common.getModesList(MainV2.comPort.MAV.cs);
            //cmbaction.ValueMember = "Key";
            //cmbaction.DisplayMember = "Value";
            cmbaction.Tag = name;
            cmbaction.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbaction.Name = "cmbaction" + name;
            //if (Settings.Instance["butaction" + name] != null)
            //  cmbaction.Text = Settings.Instance["butaction" + name].ToString();
            //if (config.function != buttonfunction.ChangeMode)
            cmbaction.Text = config.function.ToString();
            cmbaction.SelectedIndexChanged += cmbaction_SelectedIndexChanged;

            but_settings.Location = new Point(cmbaction.Right + 5, y);
            //but_settings.Size = BUT_detch1.Size;
            but_settings.Text = "Settings";
            but_settings.Name = "butsettings" + name;
            but_settings.Click += but_settings_Click;
            but_settings.Tag = cmbaction;

            // do this here so putting in text works
            buttonsGroup.Controls.AddRange(new Control[] { butlabel, butnumberlist, but_detect, hbar, cmbaction, but_settings });
        }

        void cmbaction_SelectedIndexChanged(object sender, EventArgs e)
        {
            int num = int.Parse(((Control)sender).Tag.ToString());
            var config = MainV2.joystick.getButton(num);
            config.function =
                (buttonfunction)Enum.Parse(typeof(buttonfunction), ((Control)sender).Text);
            MainV2.joystick.setButton(num, config);
        }

        void but_settings_Click(object sender, EventArgs e)
        {
            var cmb = ((Control)sender).Tag as ComboBox;

            switch ((buttonfunction)Enum.Parse(typeof(buttonfunction), cmb.SelectedItem.ToString()))
            {
                case buttonfunction.ChangeMode:
                    new Joy_ChangeMode((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Mount_Mode:
                    new Joy_Mount_Mode((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Do_Repeat_Relay:
                    new Joy_Do_Repeat_Relay((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Do_Repeat_Servo:
                    new Joy_Do_Repeat_Servo((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Do_Set_Relay:
                    new Joy_Do_Set_Relay((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Do_Set_Servo:
                    new Joy_Do_Set_Servo((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Button_axis0:
                    new Joy_Button_axis((string)cmb.Tag).ShowDialog();
                    break;
                case buttonfunction.Button_axis1:
                    new Joy_Button_axis((string)cmb.Tag).ShowDialog();
                    break;
                default:
                    CustomMessageBox.Show("No settings to set", "No settings");
                    break;
            }
        }

        private void CMB_joysticks_SelectedIndexChanged(object sender, EventArgs e)
        {
            try
            {
                if (MainV2.joystick != null && MainV2.joystick.enabled == false)
                    MainV2.joystick.UnAcquireJoyStick();
            }
            catch
            {
            }
        }

       
        private void JoystickSetup_FormClosed(object sender, FormClosedEventArgs e)
        {
            timer1.Stop();
            inputsTimer.Stop();
            CloseInputs();

            if (MainV2.joystick != null && MainV2.joystick.enabled == false)
            {
                MainV2.joystick.UnAcquireJoyStick();
                MainV2.joystick = null;
            }
        }

        private void CHK_elevons_CheckedChanged(object sender, EventArgs e)
        {
            if (MainV2.joystick == null)
            {
                return;
            }
            MainV2.joystick.elevons = CHK_elevons.Checked;
        }

        private void chk_manualcontrol_CheckedChanged(object sender, EventArgs e)
        {
            MainV2.joystick.manual_control = chk_manualcontrol.Checked;
        }

        public void Deactivate()
        {
            timer1.Stop();
            inputsTimer.Stop();
            CloseInputs();

            if (MainV2.joystick != null && MainV2.joystick.enabled == false)
            {
                MainV2.joystick.UnAcquireJoyStick();
                MainV2.joystick = null;
            }
        }

        private void but_export_Click(object sender, EventArgs e)
        {
            SaveFileDialog sfd = new SaveFileDialog();
            sfd.Filter = "Joystick config files (*.joycfg)|*.joycfg|All files (*.*)|*.*";
            if (sfd.ShowDialog() == DialogResult.OK)
            {
                MainV2.joystick.saveconfig();
                MainV2.joystick.ExportConfig(sfd.FileName);
            }
        }

        private void but_import_Click(object sender, EventArgs e)
        {
            if (CustomMessageBox.Show("NOTE: this will replace any existing joystick configuration.\nPlease make sure you have saved your current configuration if needed.", "Import Joystick Config", MessageBoxButtons.OKCancel) == (int)DialogResult.OK)
            {
                OpenFileDialog ofd = new OpenFileDialog();
                ofd.Filter = "Joystick config files (*.joycfg)|*.joycfg|All files (*.*)|*.*";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    MainV2.joystick.ImportConfig(ofd.FileName);
                    MainV2.joystick.loadconfig();
                    CustomMessageBox.Show("Please reopen joystick for changes to take effect");
                    this.BeginInvoke((Action)delegate ()
                    {
                        this.Close();
                        ((Form)this.Parent).Close();
                    });
                }
            }
        }
    }
}