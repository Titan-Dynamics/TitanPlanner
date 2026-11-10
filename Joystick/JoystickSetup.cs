using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;


namespace MissionPlanner.Joystick
{
    public partial class JoystickSetup : MyUserControl, IDeactivate
    {
        bool startup = true;

        private const int MaxChannels = 16;
        private const int MaxButtonRows = 16;

        // how long Detect listens for an axis and for a button, and how far (of 65535) an axis must travel to be
        // picked over a hat
        private static readonly TimeSpan AxisDetectTime = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan ButtonDetectTime = TimeSpan.FromSeconds(10);
        private const int AxisDetectThreshold = 16000;

        // every button on the page is this size, and the channel and button rows this tall
        private static readonly Size ButtonSize = new Size(80, 23);
        private const int RowHeight = 29;

        // the button rows' columns
        private const int ButtonNameX = 8;
        private const int ButtonNumberX = 58;
        private const int ButtonDetectX = 134;
        private const int ButtonBarX = 220;
        private const int ButtonActionX = 326;
        private const int ButtonSettingsX = 462;

        // the channels' outputs, as the vehicle and the HUD get them
        private static readonly FieldInfo[] OverrideFields = Enumerable.Range(1, 18)
            .Select(channel => typeof(CurrentState).GetField("rcoverridech" + channel)).ToArray();

        public JoystickSetup()
        {
            InitializeComponent();

            // the window's title (ShowUserControl takes it from Text)
            Text = "Joystick configuration";

            // the timer shows what the joystick reads once the page is built, at the send rate; it builds nothing
            timer1.Enabled = false;

            MissionPlanner.Utilities.Tracking.AddPage(this.GetType().ToString(), this.Text);
        }

        // left to right: the joystick's own inputs, the RC channels made from them, and the button assignments
        private GroupBox inputsGroup;
        private GroupBox channelsGroup;
        private GroupBox buttonsGroup;
        private Label inputsInfo;
        private Label inputsButtonsLabel;
        private ButtonGrid inputsButtons;
        private Label buttonsInfo;
        private Label[] buttonsHeadings;
        private Label rateLabel;
        private NumericUpDown rateBox;
        private readonly Dictionary<joystickaxis, InputRow> inputRows = new Dictionary<joystickaxis, InputRow>();
        private readonly List<JoystickAxis> channelRows = new List<JoystickAxis>();
        private readonly List<ButtonRow> buttonRows = new List<ButtonRow>();

        // the joystick chosen above, which the page reads whether or not it is enabled
        private JoystickBase reader;

        // what Detect is listening for, if anything
        private Detection detection;

        // refilling the joystick list, which moves its selection on the way
        private bool listing;

        // Windows' timer at 1 ms while the page is open, so it can show the joystick at the send rate
        private IDisposable fineTimer;

        // an input's row: its name, a bar of where it is, and its value
        private class InputRow
        {
            public Label Name;
            public HorizontalProgressBar2 Bar;
            public Label Value;
            public bool Hat;
        }

        // a button function's row: the joystick button that triggers it, Detect, whether that button is down, the
        // function and its settings
        private class ButtonRow
        {
            public Control[] Controls;
            public ChoiceBox Number;
            public MyButton Detect;
            public HorizontalProgressBar Bar;
        }

        // a Detect under way: the row that started it, until when it listens, how its button shows it, what it does
        // with each reading (true once it has its answer) and what it does when the time is up
        private class Detection
        {
            public object Owner;
            public DateTime Ends;
            public Action<bool> Show;
            public Func<Dictionary<joystickaxis, int>, bool[], bool> Step;
            public Action Expire;
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

        private void Joystick_Load(object sender, EventArgs e)
        {
            SuspendLayout();

            listing = true;
            try
            {
                foreach (var device in JoystickBase.getDevices())
                    CMB_joysticks.Items.Add(device);
            }
            catch
            {
                listing = false;
                ResumeLayout();
                CustomMessageBox.Show("Error geting joystick list: do you have the directx redist installed?");
                this.Close();
                return;
            }

            // the joystick last used if it is plugged in, else the first
            var saved = Settings.Instance["joystick_name"];
            if (!string.IsNullOrEmpty(saved) && CMB_joysticks.Items.Contains(saved))
                CMB_joysticks.SelectedItem = saved;
            else if (CMB_joysticks.Items.Count > 0)
                CMB_joysticks.SelectedIndex = 0;
            listing = false;

            try
            {
                if (Settings.Instance.ContainsKey("joy_elevons"))
                    CHK_elevons.Checked = bool.Parse(Settings.Instance["joy_elevons"].ToString());
            }
            catch
            {
            }

            label14.Text += " " + MainV2.comPort.MAV.cs.firmware.ToString();

            // how often the joystick is read and sent, for every joystick, taking effect at once
            rateLabel = new Label { AutoSize = true, Text = "Send Rate (Hz)" };
            rateBox = new NumericUpDown
            {
                Minimum = JoystickBase.MinRateHz,
                Maximum = JoystickBase.MaxRateHz,
                Increment = 5,
                Value = JoystickBase.RateHz,
                Width = 55
            };
            rateBox.ValueChanged += (s, ev) =>
            {
                JoystickBase.RateHz = (int)rateBox.Value;
                timer1.Interval = RefreshInterval;
            };
            Controls.AddRange(new Control[] { rateLabel, rateBox });

            // the groups, filled before they go on the page so it lays them out once
            inputsGroup = CreateGroup("Joystick inputs");
            inputsInfo = new Label { AutoSize = true, Location = new Point(8, 20) };
            inputsButtonsLabel = new Label { AutoSize = true, Text = "Buttons" };
            inputsButtons = new ButtonGrid();
            inputsGroup.Controls.AddRange(new Control[] { inputsInfo, inputsButtonsLabel, inputsButtons });

            channelsGroup = CreateGroup("RC channels");

            buttonsGroup = CreateGroup("Buttons");
            buttonsInfo = new Label
            {
                AutoSize = true,
                Location = new Point(ButtonNameX, 20),
                Text = "Shown once the joystick is found."
            };
            buttonsHeadings = new[]
            {
                new Label { AutoSize = true, Location = new Point(ButtonNumberX, 20), Text = "Joystick button" },
                new Label { AutoSize = true, Location = new Point(ButtonBarX, 20), Text = "Pressed" },
                new Label { AutoSize = true, Location = new Point(ButtonActionX, 20), Text = "Function" }
            };
            buttonsGroup.Controls.Add(buttonsInfo);
            buttonsGroup.Controls.AddRange(buttonsHeadings);

            // the joystick and the configuration the page edits, then the rows that show them
            OpenJoystick();
            BuildChannelRows();

            Controls.AddRange(new Control[] { inputsGroup, channelsGroup, buttonsGroup });

            // once, with everything on the page
            ThemeManager.ApplyThemeTo(this);

            ResumeLayout(true);
            LayoutPage();

            if (MainV2.joystick != null && MainV2.joystick.enabled)
                BUT_enable.Text = "Disable";

            startup = false;
            fineTimer = JoystickBase.FineTimer();
            timer1.Interval = RefreshInterval;
            timer1.Start();
        }

        // the page shows the joystick as often as it is sent
        private static int RefreshInterval => Math.Max(1, 1000 / JoystickBase.RateHz);

        /// <summary>
        /// The top row (the joystick, its buttons and the loaded configuration, with the options at the right end), the
        /// groups side by side under it, and the page (and its window) sized to them.
        /// </summary>
        private void LayoutPage()
        {
            var rowCenter = CMB_joysticks.Top + CMB_joysticks.Height / 2;
            int CenterTop(Control control) => rowCenter - control.Height / 2;

            label5.Top = CenterTop(label5);
            var x = CMB_joysticks.Right + 6;
            foreach (var button in new Control[] { BUT_enable, BUT_save, but_export, but_import })
            {
                button.Bounds = new Rectangle(new Point(x, rowCenter - ButtonSize.Height / 2), ButtonSize);
                x = button.Right + 6;
            }

            label14.AutoSize = true;
            label14.Location = new Point(x + 6, CenterTop(label14));

            var top = Math.Max(CMB_joysticks.Bottom, BUT_enable.Bottom) + 8;
            inputsGroup.Location = new Point(5, top);
            channelsGroup.Location = new Point(inputsGroup.Right + 6, top);
            buttonsGroup.Location = new Point(channelsGroup.Right + 6, top);

            // the options at the right end, in this order, each with the gap after it (the rate's label sits next to
            // its box)
            var options = new (Control Control, int Gap)[]
            {
                (CHK_elevons, 12), (chk_manualcontrol, 12), (rateLabel, 4), (rateBox, 0)
            };
            var optionsWidth = options.Sum(option => option.Control.Width + option.Gap);
            var right = Math.Max(buttonsGroup.Right, label14.Right + 24 + optionsWidth);
            var optionX = right - optionsWidth;
            foreach (var option in options)
            {
                option.Control.Location = new Point(optionX, CenterTop(option.Control));
                optionX = option.Control.Right + option.Gap;
            }

            var bottom = new Control[] { inputsGroup, channelsGroup, buttonsGroup }.Max(group => group.Bottom);
            Size = new Size(right + 10, bottom + 10);
        }

        /// <summary>
        /// Opens the joystick chosen above, for the page to read whether or not it is enabled, with the configuration
        /// the page edits on MainV2.joystick (the enabled joystick, or one the page opens), and lays out the inputs and
        /// button rows the joystick has.
        /// </summary>
        private void OpenJoystick()
        {
            CancelDetection();
            CloseReader();

            var name = CMB_joysticks.Text;
            if (!string.IsNullOrEmpty(name))
            {
                try
                {
                    reader = JoystickBase.getJoyStickByName(name);
                }
                catch
                {
                    reader = null;
                }
            }

            if (MainV2.joystick == null || !MainV2.joystick.enabled)
                OpenConfigJoystick();

            BuildInputRows();
            BuildButtonRows();
        }

        /// <summary>
        /// The configuration the page edits while the joystick isn't enabled: the vehicle's, from its files, on the
        /// joystick chosen above.
        /// </summary>
        private void OpenConfigJoystick()
        {
            try
            {
                MainV2.joystick?.UnAcquireJoyStick();
            }
            catch
            {
            }

            var joy = JoystickBase.Create(() => MainV2.comPort);
            for (int a = 1; a <= MaxChannels; a++)
            {
                var config = joy.getChannel(a);
                joy.setChannel(a, config.axis, config.reverse, config.expo);
            }

            joy.elevons = CHK_elevons.Checked;
            joy.manual_control = chk_manualcontrol.Checked;
            joy.name = CMB_joysticks.Text;
            try
            {
                joy.AcquireJoystick(joy.name);
            }
            catch
            {
                // not plugged in: it still holds the configuration
            }

            MainV2.joystick = joy;
        }

        private void CloseReader()
        {
            try
            {
                reader?.Dispose();
            }
            catch
            {
            }

            reader = null;
        }

        /// <summary>
        /// One bar per input the joystick has, named as the joystick names it, with the axis to choose for it, and its
        /// buttons.
        /// </summary>
        private void BuildInputRows()
        {
            inputsGroup.SuspendLayout();

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
            inputsInfo.Text = reader != null
                ? "Move a control to see which input it is. Choose the name in brackets for an RC channel."
                : "Joystick not found. Plug it in, then choose it above.";

            var y = inputsInfo.Top + inputsInfo.PreferredSize.Height + 8;
            var axes = new List<KeyValuePair<joystickaxis, string>>();
            try
            {
                if (reader != null)
                    axes = reader.GetAxes();
            }
            catch
            {
            }

            foreach (var axis in axes)
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

            // the buttons, numbered as the button rows number them
            var buttons = 0;
            try
            {
                buttons = reader?.getNumButtons() ?? 0;
            }
            catch
            {
            }

            inputsButtonsLabel.Visible = inputsButtons.Visible = buttons > 0;
            inputsButtonsLabel.Location = new Point(8, y + 6);
            inputsButtons.Location = new Point(8, inputsButtonsLabel.Top + inputsButtonsLabel.PreferredHeight + 4);
            inputsButtons.Width = rowWidth;
            inputsButtons.Count = buttons;

            inputsGroup.ResumeLayout(false);
        }

        /// <summary>
        /// A row per RC channel, from the configuration the page edits, under its column headings.
        /// </summary>
        private void BuildChannelRows()
        {
            channelsGroup.SuspendLayout();

            var y = 20 + label8.PreferredHeight + 4;
            var axes = Enum.GetValues(typeof(joystickaxis));
            for (int a = 1; a <= MaxChannels; a++)
            {
                var config = MainV2.joystick.getChannel(a);

                var ax = new JoystickAxis()
                {
                    ChannelNo = a,
                    Label = "RC " + a,
                    AxisArray = axes,
                    ChannelValue = config.axis.ToString(),
                    ExpoValue = config.expo.ToString(),
                    ReverseValue = config.reverse,
                    Location = new Point(6, y),
                    Name = "axis" + a
                };

                ax.Detect = () => DetectAxis(ax);
                ax.Reverse = () => MainV2.joystick?.setReverse(ax.ChannelNo, ax.ReverseValue);
                ax.SetAxis = () => MainV2.joystick?.setAxis(ax.ChannelNo,
                    (joystickaxis)Enum.Parse(typeof(joystickaxis), ax.ChannelValue));
                ax.Expo = () =>
                {
                    if (int.TryParse(ax.ExpoValue, out int expoValue))
                    {
                        MainV2.joystick?.setExpo(ax.ChannelNo, expoValue);
                    }
                };

                channelsGroup.Controls.Add(ax);
                channelRows.Add(ax);

                y += ax.Height;
            }

            // the headings over their columns
            var first = channelRows[0];
            foreach (var (heading, column) in new[]
                     {
                         (label8, first.AxisColumn), (label7, first.OutputColumn), (label6, first.ExpoColumn),
                         (label9, first.ReverseColumn)
                     })
            {
                heading.Parent = channelsGroup;
                heading.Location = new Point(first.Left + column, 20);
            }

            channelsGroup.ResumeLayout(false);
        }

        /// <summary>
        /// A row per button function the joystick has buttons for (up to MaxButtonRows), from the configuration the
        /// page edits.
        /// </summary>
        private void BuildButtonRows()
        {
            buttonsGroup.SuspendLayout();

            foreach (var row in buttonRows)
            {
                foreach (var control in row.Controls)
                {
                    buttonsGroup.Controls.Remove(control);
                    control.Dispose();
                }
            }

            buttonRows.Clear();

            var buttons = 0;
            try
            {
                buttons = reader?.getNumButtons() ?? 0;
            }
            catch
            {
            }

            var count = Math.Min(MaxButtonRows, buttons);
            buttonsInfo.Visible = count == 0;
            foreach (var heading in buttonsHeadings)
                heading.Visible = count > 0;

            var y = 20 + label8.PreferredHeight + 4;
            for (int f = 0; f < count; f++)
                buttonRows.Add(BuildButtonRow(f, y + f * RowHeight, buttons));

            buttonsGroup.ResumeLayout(false);
        }

        private ButtonRow BuildButtonRow(int index, int y, int joystickButtons)
        {
            var joy = MainV2.joystick;
            var config = joy.getButton(index);
            joy.setButton(index, config);

            var row = new ButtonRow();

            var label = new Label
            {
                AutoSize = false,
                Location = new Point(ButtonNameX, y),
                Size = new Size(ButtonNumberX - ButtonNameX, RowHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "But " + (index + 1)
            };

            // none (-1), or one of the joystick's buttons (and the one set, should it have fewer)
            var last = Math.Max(joystickButtons - 1, config.buttonno);
            row.Number = new ChoiceBox
            {
                Location = new Point(ButtonNumberX, y + 4),
                Size = new Size(ButtonDetectX - 6 - ButtonNumberX, 21),
                Items = Enumerable.Range(-1, last + 2).Select(number => (object)number.ToString()).ToArray()
            };
            row.Number.Text = config.buttonno.ToString();
            if (row.Number.SelectedItem == null)
                row.Number.Text = "-1";
            row.Number.SelectedItemChanged += (s, e) =>
                MainV2.joystick?.changeButton(index, int.Parse(row.Number.Text));

            row.Detect = new MyButton
            {
                Location = new Point(ButtonDetectX, y + 3),
                Size = ButtonSize,
                Text = "Detect"
            };
            row.Detect.Click += (s, e) => DetectButton(row);

            row.Bar = new HorizontalProgressBar
            {
                Location = new Point(ButtonBarX, y + 4),
                Size = new Size(ButtonActionX - 6 - ButtonBarX, 21)
            };

            var action = new ChoiceBox
            {
                Location = new Point(ButtonActionX, y + 4),
                Size = new Size(ButtonSettingsX - 6 - ButtonActionX, 21),
                Tag = index.ToString(),
                Name = "cmbaction" + index,
                Items = Enum.GetNames(typeof(buttonfunction)).Cast<object>().ToArray()
            };
            action.Text = config.function.ToString();
            action.SelectedItemChanged += cmbaction_SelectedIndexChanged;

            var settings = new MyButton
            {
                Location = new Point(ButtonSettingsX, y + 3),
                Size = ButtonSize,
                Text = "Settings",
                Tag = action
            };
            settings.Click += but_settings_Click;

            row.Controls = new Control[] { label, row.Number, row.Detect, row.Bar, action, settings };
            buttonsGroup.Controls.AddRange(row.Controls);
            return row;
        }

        #region Detect

        /// <summary>
        /// Listens for AxisDetectTime and gives the channel the axis that moved most. A second click stops it.
        /// </summary>
        private void DetectAxis(JoystickAxis ax)
        {
            if (StopDetection(ax) || reader == null)
                return;

            // only the axes the joystick has, so one it doesn't have can't be picked
            HashSet<joystickaxis> axes;
            try
            {
                axes = new HashSet<joystickaxis>(reader.GetAxes().Select(a => a.Key));
            }
            catch
            {
                return;
            }

            var min = new Dictionary<joystickaxis, int>();
            var max = new Dictionary<joystickaxis, int>();
            StartDetection(new Detection
            {
                Owner = ax,
                Ends = DateTime.Now + AxisDetectTime,
                Show = detecting => ax.Detecting = detecting,
                Step = (values, buttons) =>
                {
                    foreach (var value in values)
                    {
                        if (!axes.Contains(value.Key))
                            continue;
                        min[value.Key] = min.TryGetValue(value.Key, out var low) ? Math.Min(low, value.Value) : value.Value;
                        max[value.Key] = max.TryGetValue(value.Key, out var high) ? Math.Max(high, value.Value) : value.Value;
                    }

                    // it listens for the whole time
                    return false;
                },
                Expire = () =>
                {
                    var axis = JoystickBase.MostMovedAxis(min, max, AxisDetectThreshold);
                    if (axis != joystickaxis.None)
                        ax.ChannelValue = axis.ToString();
                }
            });
        }

        /// <summary>
        /// Listens until a joystick button is pressed, for up to ButtonDetectTime, and gives the row that button. A
        /// second click stops it.
        /// </summary>
        private void DetectButton(ButtonRow row)
        {
            if (StopDetection(row) || reader == null)
                return;

            // buttons already down when it starts count once they are let go and pressed again
            HashSet<int> held = null;
            StartDetection(new Detection
            {
                Owner = row,
                Ends = DateTime.Now + ButtonDetectTime,
                Show = detecting => row.Detect.Text = detecting ? "Detecting..." : "Detect",
                Step = (values, buttons) =>
                {
                    if (held == null)
                    {
                        held = new HashSet<int>(Enumerable.Range(0, buttons.Length).Where(b => buttons[b]));
                        return false;
                    }

                    for (int b = 0; b < buttons.Length; b++)
                    {
                        if (!buttons[b])
                        {
                            held.Remove(b);
                        }
                        else if (!held.Contains(b))
                        {
                            row.Number.Text = b.ToString();
                            return true;
                        }
                    }

                    return false;
                }
            });
        }

        private void StartDetection(Detection start)
        {
            CancelDetection();
            detection = start;
            start.Show(true);
        }

        // stops the detection the row started, if it has one
        private bool StopDetection(object owner)
        {
            if (detection == null || detection.Owner != owner)
                return false;

            CancelDetection();
            return true;
        }

        private void CancelDetection()
        {
            var stopped = detection;
            detection = null;
            stopped?.Show(false);
        }

        // a reading for the detection under way: it ends when it has its answer or its time is up
        private void StepDetection(Dictionary<joystickaxis, int> values, bool[] buttons)
        {
            var current = detection;
            if (current == null)
                return;

            if (current.Step(values, buttons))
            {
                CancelDetection();
            }
            else if (DateTime.Now >= current.Ends)
            {
                CancelDetection();
                current.Expire?.Invoke();
            }
        }

        #endregion

        #region Live readings

        private void timer1_Tick(object sender, EventArgs e)
        {
            ReadJoystick();
            ShowChannels();
        }

        /// <summary>
        /// The chosen joystick's inputs and buttons, the button rows' pressed bars, and the detection under way.
        /// </summary>
        private void ReadJoystick()
        {
            if (reader == null)
                return;

            IMyJoystickState state;
            try
            {
                state = reader.GetCurrentState();
            }
            catch
            {
                state = null;
            }

            if (state == null)
            {
                // unplugged: shown as not found until it is chosen again
                JoystickLost();
                return;
            }

            var values = JoystickBase.ReadAxes(state);
            var buttons = state.GetButtons() ?? new bool[0];

            foreach (var row in inputRows)
            {
                if (!values.TryGetValue(row.Key, out var value))
                    continue;

                row.Value.Bar.Value = value;
                row.Value.Value.Text = !row.Value.Hat ? value.ToString()
                    : value < 0 ? "centred" : value / 100 + "°";
            }

            inputsButtons.ShowPressed(buttons);

            // each row's bar: whether the button chosen for it is down (-1 for none)
            foreach (var row in buttonRows)
            {
                int.TryParse(row.Number.Text, out var number);
                row.Bar.Value = number >= 0 && number < buttons.Length && buttons[number] ? 100 : 0;
            }

            StepDetection(values, buttons);
        }

        /// <summary>
        /// The RC channels' outputs. Enabled, the joystick's own loop works them out; otherwise the page does, from the
        /// configuration it edits.
        /// </summary>
        private void ShowChannels()
        {
            var cs = MainV2.comPort.MAV.cs;
            var joy = MainV2.joystick;
            try
            {
                if (joy != null && !joy.enabled && joy.IsJoystickValid())
                {
                    joy.elevons = CHK_elevons.Checked;
                    for (int channel = 1; channel <= OverrideFields.Length; channel++)
                        OverrideFields[channel - 1].SetValue(cs, joy.getValueForChannel(channel));
                }
            }
            catch
            {
                // unplugged: the reader shows it
            }

            foreach (var row in channelRows)
                row.ShowValue((short)OverrideFields[row.ChannelNo - 1].GetValue(cs));
        }

        private void JoystickLost()
        {
            SuspendLayout();
            CancelDetection();
            CloseReader();
            BuildInputRows();
            BuildButtonRows();
            ThemeManager.ApplyThemeTo(inputsGroup);
            ThemeManager.ApplyThemeTo(buttonsGroup);
            ResumeLayout(true);
            LayoutPage();
        }

        #endregion

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
                    // the page's own again, to edit
                    OpenConfigJoystick();
                    return;
                }

                Settings.Instance["joystick_name"] = CMB_joysticks.Text;

                MainV2.joystick = joy;
                MainV2.joystick.enabled = true;

                BUT_enable.Text = "Disable";
            }
            else
            {
                MainV2.joystick.enabled = false;

                MainV2.joystick.clearRCOverride();

                MainV2.joystick = null;

                BUT_enable.Text = "Enable";

                // the page keeps a configuration to edit
                OpenConfigJoystick();
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

        private void CMB_joysticks_Click(object sender, EventArgs e)
        {
            // the joysticks plugged in now, keeping the one chosen
            var chosen = CMB_joysticks.Text;

            listing = true;
            CMB_joysticks.Items.Clear();
            try
            {
                foreach (var device in JoystickBase.getDevices())
                    CMB_joysticks.Items.Add(device);
            }
            catch
            {
            }

            if (CMB_joysticks.Items.Contains(chosen))
                CMB_joysticks.SelectedItem = chosen;
            else if (CMB_joysticks.Items.Count > 0)
                CMB_joysticks.SelectedIndex = 0;
            listing = false;

            // another joystick, or the chosen one back after it was unplugged
            if (CMB_joysticks.Text != chosen || reader == null)
                JoystickChosen();
        }

        private void CMB_joysticks_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (startup || listing)
                return;

            JoystickChosen();
        }

        private void JoystickChosen()
        {
            SuspendLayout();
            OpenJoystick();
            ThemeManager.ApplyThemeTo(inputsGroup);
            ThemeManager.ApplyThemeTo(buttonsGroup);
            ResumeLayout(true);
            LayoutPage();
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
            var cmb = ((Control)sender).Tag as ChoiceBox;

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

        private void JoystickSetup_FormClosed(object sender, FormClosedEventArgs e)
        {
            ClosePage();
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
            if (MainV2.joystick != null)
                MainV2.joystick.manual_control = chk_manualcontrol.Checked;
        }

        public void Deactivate()
        {
            ClosePage();
        }

        private void ClosePage()
        {
            timer1.Stop();
            fineTimer?.Dispose();
            fineTimer = null;
            detection = null;
            CloseReader();

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

        /// <summary>
        /// The joystick's buttons as numbered cells, lit while pressed.
        /// </summary>
        private sealed class ButtonGrid : Control
        {
            private const int CellWidth = 26;
            private const int CellHeight = 20;
            private const int Gap = 3;

            private bool[] pressed = new bool[0];

            public ButtonGrid()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                         ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }

            public int Count
            {
                get { return pressed.Length; }
                set
                {
                    pressed = new bool[Math.Max(0, value)];
                    Height = HeightFor(Width);
                    Invalidate();
                }
            }

            private int Columns => ColumnsFor(Width);

            private static int ColumnsFor(int width) => Math.Max(1, (width + Gap) / (CellWidth + Gap));

            private int HeightFor(int width)
            {
                var rows = (pressed.Length + ColumnsFor(width) - 1) / ColumnsFor(width);
                return Math.Max(0, rows * (CellHeight + Gap) - Gap);
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);

                // as tall as its rows of cells at this width
                var height = HeightFor(Width);
                if (Height != height)
                    Height = height;
            }

            public void ShowPressed(bool[] buttons)
            {
                var changed = false;
                for (int i = 0; i < pressed.Length; i++)
                {
                    var down = i < buttons.Length && buttons[i];
                    if (pressed[i] == down)
                        continue;

                    pressed[i] = down;
                    changed = true;
                }

                if (changed)
                    Invalidate();
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);

                // lit as the input bars fill
                var lit = ThemeManager.HorizontalPBValueColor.IsEmpty
                    ? Color.FromArgb(148, 193, 31)
                    : ThemeManager.HorizontalPBValueColor;

                var columns = Columns;
                using (var offBrush = new SolidBrush(ThemeManager.ControlBGColor))
                using (var litBrush = new SolidBrush(lit))
                {
                    for (int i = 0; i < pressed.Length; i++)
                    {
                        var cell = new Rectangle(i % columns * (CellWidth + Gap), i / columns * (CellHeight + Gap),
                            CellWidth, CellHeight);
                        e.Graphics.FillRectangle(pressed[i] ? litBrush : offBrush, cell);
                        TextRenderer.DrawText(e.Graphics, i.ToString(), Font, cell,
                            pressed[i] ? Color.Black : ThemeManager.TextColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                            TextFormatFlags.NoPadding);
                    }
                }
            }
        }
    }
}
