using MissionPlanner.Controls;
using MissionPlanner.Utilities;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MissionPlanner.GCSViews.ConfigurationView
{
    /// <summary>
    /// Motor Setup for a fixed-wing plane. Its motors are the outputs set to Throttle, Throttle Left or Throttle Right
    /// (<see cref="FixedWingMotors"/>): each row moves a motor to another output and spins it, and with two or more, its ?
    /// spins it and asks which motor turned. ArduPlane has no motor test for a plane's motors, so a spin borrows the
    /// output: its function is set to none, DO_SET_SERVO drives it for the test, and the function is set back. Each
    /// borrowed output is kept in the settings until it is set back, so one a test leaves borrowed (the app closing, the
    /// link dropping) is set back the next time the page opens on that vehicle.
    /// </summary>
    public class ConfigFixedWingMotorTest : MyUserControl, IActivate, IDeactivate
    {
        const int MaxServos = 32;
        const string BorrowedSetting = "FixedWingMotorTest_Borrowed";

        // before a spin the output is held at zero throttle this long, for an ESC that had no signal while disarmed
        const int ZeroThrottleMs = 500;

        // between the motors of a test in sequence
        const int SequenceGapMs = 500;

        // the warning is shown before the first test of each run of the app
        static bool _warned;

        readonly Label _title;
        readonly NumericUpDown _throttle, _duration;
        readonly TableLayoutPanel _table;
        readonly MyButton _testAll, _stopAll, _testSequence;
        readonly List<Row> _rows = new List<Row>();
        List<FixedWingMotors.Motor> _motors = new List<FixedWingMotors.Motor>();
        Dictionary<int, string> _functionNames = new Dictionary<int, string>();

        // the outputs borrowed now, with the functions to set back
        readonly Dictionary<int, int> _borrowed = new Dictionary<int, int>();

        CancellationTokenSource _cancel;
        bool _testing, _writing;

        sealed class Row
        {
            public FixedWingMotors.Motor Motor;
            public GroupBox Box;
            public ComboBox Output;
            public MyButton Identify, Spin;
        }

        sealed class Item
        {
            public string Text;
            public int Value;
            public override string ToString() => Text;
        }

        public ConfigFixedWingMotorTest()
        {
            AutoScroll = true;

            _title = new Label
            {
                AutoSize = true,
                Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
                Location = new Point(8, 9)
            };
            var throttleLabel = new Label { AutoSize = true, Location = new Point(8, 50), Text = "Throttle %" };
            _throttle = new NumericUpDown
            {
                Location = new Point(100, 48),
                Size = new Size(72, 22),
                Minimum = 0,
                Maximum = 100,
                Value = 5
            };
            var durationLabel = new Label { AutoSize = true, Location = new Point(193, 50), Text = "Duration (s)" };
            _duration = new NumericUpDown
            {
                Location = new Point(285, 48),
                Size = new Size(72, 22),
                Minimum = 1,
                Maximum = 30,
                Value = 2
            };
            var note = new Label { AutoSize = true, Location = new Point(374, 53), Text = "Note: Remove props!" };
            var hint = new Label
            {
                AutoSize = true,
                Location = new Point(8, 82),
                Text = "The motors are the outputs set to Throttle, Throttle Left or Throttle Right."
            };

            _table = new TableLayoutPanel { Location = new Point(8, 112), Width = 498 };

            _testAll = new MyButton { Text = "Test All Motors", Dock = DockStyle.Fill };
            _stopAll = new MyButton { Text = "Stop All Motors", Dock = DockStyle.Fill };
            _testSequence = new MyButton { Text = "Test Sequence", Dock = DockStyle.Fill };
            _testAll.Click += (sender, e) => TestAll(false);
            _testSequence.Click += (sender, e) => TestAll(true);
            _stopAll.Click += (sender, e) => StopTest();

            Controls.AddRange(new Control[]
                { _title, throttleLabel, _throttle, durationLabel, _duration, note, hint, _table });

            Disposed += (sender, e) => StopTest();
        }

        public void Activate()
        {
            if (!MainV2.comPort.BaseStream.IsOpen)
            {
                Enabled = false;
                return;
            }

            Enabled = true;
            Rebuild();
            SetBackLeftOver();
        }

        public void Deactivate()
        {
            // a test running is stopped and its outputs set back, which goes on after the page is left
            StopTest();
        }

        static MAVLink.MAVLinkParamList Param => MainV2.comPort.MAV.param;

        static int FunctionOf(int servo)
        {
            string name = $"SERVO{servo}_FUNCTION";
            return Param.ContainsKey(name) ? (int)Param[name].Value : FixedWingMotors.None;
        }

        string NameOf(int function)
        {
            string name;
            return _functionNames.TryGetValue(function, out name) ? name : "function " + function;
        }

        /// <summary>Lays the page out again from the outputs' functions.</summary>
        void Rebuild()
        {
            if (IsDisposed)
                return;

            // an output borrowed for a test counts as the motor it is
            var functions = new Dictionary<int, int>();
            for (int servo = 1; servo <= MaxServos; servo++)
            {
                int borrowed;
                if (Param.ContainsKey($"SERVO{servo}_FUNCTION"))
                    functions[servo] = _borrowed.TryGetValue(servo, out borrowed) ? borrowed : FunctionOf(servo);
            }

            _functionNames = new Dictionary<int, string>();
            try
            {
                foreach (var option in ParameterMetaDataRepository.GetParameterOptionsInt("SERVO1_FUNCTION",
                             MainV2.comPort.MAV.cs.firmware.ToString()))
                    _functionNames[option.Key] = option.Value;
            }
            catch
            {
                // no metadata: the functions are shown by number
            }

            _motors = FixedWingMotors.Find(functions);
            _title.Text = _motors.Count == 0 ? "Fixed Wing: no motor output"
                : _motors.Count == 1 ? "Fixed Wing: 1 motor"
                : $"Fixed Wing: {_motors.Count} motors";

            // with no motor, a row to choose its output
            var shown = _motors.Count > 0
                ? _motors
                : new List<FixedWingMotors.Motor>
                    { new FixedWingMotors.Motor { Name = "Motor", Servo = 0, Function = FixedWingMotors.Throttle } };
            bool several = _motors.Count > 1;

            _table.SuspendLayout();
            SuspendLayout();
            try
            {
                foreach (Control control in _table.Controls.Cast<Control>().ToList())
                {
                    _table.Controls.Remove(control);
                    if (control != _testAll && control != _stopAll && control != _testSequence)
                        control.Dispose();
                }

                _rows.Clear();
                _table.RowStyles.Clear();
                _table.ColumnStyles.Clear();
                _table.ColumnCount = 3;
                for (int column = 0; column < 3; column++)
                    _table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
                _table.RowCount = shown.Count + 1;

                for (int i = 0; i < shown.Count; i++)
                {
                    _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
                    var row = CreateRow(shown[i], functions, several);
                    _rows.Add(row);
                    row.Box.Dock = DockStyle.Fill;
                    _table.Controls.Add(row.Box, 0, i);
                    _table.SetColumnSpan(row.Box, 3);
                }

                _table.RowStyles.Add(new RowStyle(SizeType.Absolute, 50F));
                _stopAll.Text = several ? "Stop All Motors" : "Stop Motor";
                if (several)
                {
                    _table.Controls.Add(_testAll, 0, shown.Count);
                    _table.Controls.Add(_stopAll, 1, shown.Count);
                    _table.Controls.Add(_testSequence, 2, shown.Count);
                }
                else
                {
                    _table.Controls.Add(_stopAll, 0, shown.Count);
                }

                _table.Height = shown.Count * 56 + 50;
            }
            finally
            {
                _table.ResumeLayout(true);
                ResumeLayout(false);
            }

            UpdateEnabled();
            ThemeManager.ApplyThemeTo(this);
        }

        Row CreateRow(FixedWingMotors.Motor motor, Dictionary<int, int> functions, bool identify)
        {
            var box = new GroupBox { Text = motor.Name, Height = 50, Margin = new Padding(3) };
            var row = new Row { Motor = motor, Box = box };
            var inner = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Margin = new Padding(0),
                Padding = new Padding(3, 0, 3, 0)
            };
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100F));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, identify ? 50F : 0F));
            inner.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 50F));
            inner.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            var function = new Label { Text = NameOf(motor.Function), Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };

            row.Output = new ComboBox { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
            row.Output.MouseWheel += (sender, e) => ((HandledMouseEventArgs)e).Handled = true;
            row.Output.Items.Add(new Item { Text = "Not assigned", Value = 0 });
            foreach (var output in functions.OrderBy(f => f.Key))
            {
                // what else the output is set to, so moving a motor onto it is a choice made knowingly
                string text = $"Servo {output.Key}";
                if (output.Key != motor.Servo && output.Value != FixedWingMotors.None)
                    text += $" ({NameOf(output.Value)})";
                row.Output.Items.Add(new Item { Text = text, Value = output.Key });
            }

            row.Output.SelectedIndex = Math.Max(0, row.Output.Items.Cast<Item>().ToList().FindIndex(i => i.Value == motor.Servo));
            row.Output.Tag = row;
            row.Output.SelectedIndexChanged += Output_SelectedIndexChanged;

            row.Spin = new MyButton { Text = "Spin", Dock = DockStyle.Fill, Tag = row };
            row.Spin.Click += Spin_Click;

            inner.Controls.Add(function, 0, 0);
            inner.Controls.Add(row.Output, 1, 0);
            if (identify)
            {
                row.Identify = new MyButton { Text = "?", Dock = DockStyle.Fill, Tag = row };
                row.Identify.Click += Identify_Click;
                inner.Controls.Add(row.Identify, 2, 0);
            }

            inner.Controls.Add(row.Spin, 3, 0);
            box.Controls.Add(inner);
            return row;
        }

        void UpdateEnabled()
        {
            if (IsDisposed)
                return;

            bool idle = !_testing && !_writing;
            foreach (var row in _rows)
            {
                row.Output.Enabled = idle;
                row.Spin.Enabled = idle && row.Motor.Servo > 0;
                if (row.Identify != null)
                    row.Identify.Enabled = idle && row.Motor.Servo > 0;
            }

            _testAll.Enabled = idle;
            _testSequence.Enabled = idle;
            _stopAll.Enabled = true;
        }

        async void Output_SelectedIndexChanged(object sender, EventArgs e)
        {
            var combo = (ComboBox)sender;
            var row = (Row)combo.Tag;
            var motor = row.Motor;
            int target = ((Item)combo.SelectedItem).Value;
            if (target == motor.Servo || _testing || _writing)
                return;

            Dictionary<int, int> writes;
            if (target == 0)
            {
                writes = new Dictionary<int, int> { [motor.Servo] = FixedWingMotors.None };
            }
            else
            {
                int targetFunction = FunctionOf(target);
                if (targetFunction != FixedWingMotors.None && !FixedWingMotors.IsMotor(targetFunction))
                {
                    string question = motor.Servo > 0
                        ? $"Servo {target} is set to {NameOf(targetFunction)}. Swap it with Servo {motor.Servo} ({NameOf(motor.Function)})?"
                        : $"Servo {target} is set to {NameOf(targetFunction)}. Set it to {NameOf(motor.Function)} instead?";
                    if (CustomMessageBox.Show(question, "Motor Setup", MessageBoxButtons.YesNo) != (int)DialogResult.Yes)
                    {
                        RebuildLater();
                        return;
                    }
                }

                writes = FixedWingMotors.Move(motor.Servo, motor.Function, target, targetFunction);
            }

            await Write(writes);
            RebuildLater();
        }

        // after the event that asks for it: rebuilding disposes the list whose event it is
        void RebuildLater()
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            try
            {
                BeginInvoke((Action)Rebuild);
            }
            catch (InvalidOperationException)
            {
                // closing
            }
        }

        /// <summary>Writes outputs' functions, those that change; false, with the error shown, if one fails.</summary>
        async Task<bool> Write(Dictionary<int, int> writes)
        {
            _writing = true;
            UpdateEnabled();
            try
            {
                foreach (var write in writes)
                {
                    string name = $"SERVO{write.Key}_FUNCTION";
                    if (!Param.ContainsKey(name) || (int)Param[name].Value == write.Value)
                        continue;

                    try
                    {
                        if (!await MainV2.comPort.setParamAsync(MainV2.comPort.sysidcurrent,
                                (byte)MainV2.comPort.compidcurrent, name, write.Value).ConfigureAwait(true))
                            throw new Exception("not set");
                    }
                    catch (Exception ex)
                    {
                        CustomMessageBox.Show($"Failed to set {name}: " + ex.Message, Strings.ERROR);
                        return false;
                    }
                }

                return true;
            }
            finally
            {
                _writing = false;
                UpdateEnabled();
            }
        }

        void Spin_Click(object sender, EventArgs e)
        {
            var motor = ((Row)((Control)sender).Tag).Motor;
            if (motor.Servo > 0 && ConfirmTest())
                StartTest(new List<FixedWingMotors.Motor> { motor }, false);
        }

        void TestAll(bool sequence)
        {
            var motors = _motors.Where(m => m.Servo > 0).ToList();
            if (motors.Count > 0 && ConfirmTest())
                StartTest(motors, sequence);
        }

        async void Identify_Click(object sender, EventArgs e)
        {
            var motor = ((Row)((Control)sender).Tag).Motor;
            if (motor.Servo == 0 || _motors.Count < 2 || !ConfirmTest())
                return;

            var test = StartTest(new List<FixedWingMotors.Motor> { motor }, false);
            Dictionary<int, int> writes = AskWhichTurned(motor);

            // stopped and set back before the answer is written, or setting it back would undo the answer
            StopTest();
            await test;

            if (writes != null && !IsDisposed)
            {
                await Write(writes);
                RebuildLater();
            }
        }

        /// <summary>
        /// Asks which motor turned while one spins; the functions to write to make it so (with two, the one that
        /// turned is made the left or right motor), or null.
        /// </summary>
        Dictionary<int, int> AskWhichTurned(FixedWingMotors.Motor spun)
        {
            bool twin = _motors.Count == 2;
            var other = _motors.FirstOrDefault(m => m != spun);
            bool sides = twin && _motors.Any(m => m.Function == FixedWingMotors.ThrottleLeft) &&
                         _motors.Any(m => m.Function == FixedWingMotors.ThrottleRight);

            using (var dialog = new Form())
            {
                dialog.Text = "Smart Assign";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new Size(360, twin && !sides ? 190 : 140);

                var question = new Label
                {
                    Text = $"Servo {spun.Servo} was spun.\nWhich motor turned?",
                    Location = new Point(20, 15),
                    Size = new Size(320, 36)
                };

                var choice = new ComboBox
                {
                    Location = new Point(20, 56),
                    Size = new Size(320, 25),
                    DropDownStyle = ComboBoxStyle.DropDownList
                };
                if (twin)
                {
                    choice.Items.Add(new Item { Text = "Left motor", Value = 0 });
                    choice.Items.Add(new Item { Text = "Right motor", Value = 1 });
                    choice.SelectedIndex = spun.Function == FixedWingMotors.ThrottleRight ? 1 : 0;
                }
                else
                {
                    for (int i = 0; i < _motors.Count; i++)
                        choice.Items.Add(new Item { Text = $"{_motors[i].Name} (Servo {_motors[i].Servo})", Value = i });
                    choice.SelectedIndex = _motors.IndexOf(spun);
                }

                dialog.Controls.Add(question);
                dialog.Controls.Add(choice);

                int buttonsTop = 96;
                if (twin && !sides)
                {
                    // both on Throttle: the answer sets them apart
                    dialog.Controls.Add(new Label
                    {
                        Text = "This sets the two outputs to Throttle Left and Throttle Right, which also steer by " +
                               "differential thrust as RUDD_DT_GAIN sets.",
                        Location = new Point(20, 90),
                        Size = new Size(320, 44)
                    });
                    buttonsTop = 146;
                }

                var ok = new MyButton
                {
                    Text = "OK",
                    DialogResult = DialogResult.OK,
                    Location = new Point(96, buttonsTop),
                    Size = new Size(80, 30)
                };
                var cancel = new MyButton
                {
                    Text = "Cancel",
                    DialogResult = DialogResult.Cancel,
                    Location = new Point(184, buttonsTop),
                    Size = new Size(80, 30)
                };
                dialog.Controls.Add(ok);
                dialog.Controls.Add(cancel);
                dialog.AcceptButton = ok;
                dialog.CancelButton = cancel;
                ThemeManager.ApplyThemeTo(dialog);

                if (dialog.ShowDialog(this) != DialogResult.OK || choice.SelectedItem == null)
                    return null;

                int answer = ((Item)choice.SelectedItem).Value;
                if (twin)
                    return FixedWingMotors.AssignTwin(spun, other, answer == 0);

                var turned = _motors[answer];
                return turned == spun
                    ? null
                    : FixedWingMotors.Move(spun.Servo, spun.Function, turned.Servo, turned.Function);
            }
        }

        bool ConfirmTest()
        {
            if (MainV2.comPort.MAV.cs.armed)
            {
                CustomMessageBox.Show("Disarm the vehicle to test its motors.", "Motor Setup");
                return false;
            }

            if (_warned)
                return true;

            var answer = CustomMessageBox.Show(
                "ArduPlane has no motor test for a plane's motors, so this page spins one itself: it sets the motor's " +
                "output to no function, drives the output, and sets the function back when the test ends.\n\n" +
                "Remove the propellers. If the link drops during a test, the motor keeps running until the vehicle is " +
                "powered off, or the link is back and this page is opened again to set the output back.",
                "Motor Setup", MessageBoxButtons.OKCancel);
            if (answer != (int)DialogResult.OK)
                return false;

            _warned = true;
            return true;
        }

        Task StartTest(List<FixedWingMotors.Motor> motors, bool sequence)
        {
            var cancel = new CancellationTokenSource();
            _cancel = cancel;
            _testing = true;
            UpdateEnabled();
            return RunTest(motors, sequence, (int)_throttle.Value, (int)_duration.Value, cancel.Token);
        }

        void StopTest()
        {
            try
            {
                _cancel?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        async Task RunTest(List<FixedWingMotors.Motor> motors, bool sequence, int percent, int seconds,
            CancellationToken token)
        {
            try
            {
                if (!sequence)
                {
                    await Spin(motors, percent, seconds, token);
                    return;
                }

                foreach (var motor in motors)
                {
                    if (token.IsCancellationRequested)
                        break;
                    await Spin(new List<FixedWingMotors.Motor> { motor }, percent, seconds, token);
                    try
                    {
                        await Task.Delay(SequenceGapMs, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
            finally
            {
                _testing = false;
                UpdateEnabled();
                // the outputs as they are now
                RebuildLater();
            }
        }

        /// <summary>
        /// Spins motors together for the test's time, or until it is stopped: borrows their outputs, holds them at zero
        /// throttle, drives them, then sets them back.
        /// </summary>
        async Task Spin(List<FixedWingMotors.Motor> motors, int percent, int seconds, CancellationToken token)
        {
            uint sysid = MainV2.comPort.sysidcurrent;
            byte compid = (byte)MainV2.comPort.compidcurrent;
            string error = null;
            try
            {
                foreach (var motor in motors)
                {
                    // kept before the output is borrowed, so it is set back even if borrowing it is cut short
                    _borrowed[motor.Servo] = motor.Function;
                    SaveBorrowed(sysid);
                    await SetFunction(sysid, compid, motor.Servo, FixedWingMotors.None);
                }

                foreach (var motor in motors)
                    await DriveOutput(sysid, compid, motor.Servo, ZeroPwm(motor.Servo), true);
                await Task.Delay(ZeroThrottleMs, token);

                foreach (var motor in motors)
                    await DriveOutput(sysid, compid, motor.Servo, Pwm(motor.Servo, percent), true);
                await Task.Delay(seconds * 1000, token);
            }
            catch (OperationCanceledException)
            {
                // stopped
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            finally
            {
                await SetBack(sysid, compid);
            }

            // shown once the outputs are set back, not while a motor may still turn
            if (error != null)
                CustomMessageBox.Show("Motor test failed: " + error, Strings.ERROR);
        }

        /// <summary>Stops the borrowed outputs and sets their functions back; those that fail stay kept, to set back later.</summary>
        async Task SetBack(uint sysid, byte compid)
        {
            var failed = new List<string>();
            foreach (var borrowed in _borrowed.ToList())
            {
                try
                {
                    // zero throttle first, in case the function doesn't go back
                    await DriveOutput(sysid, compid, borrowed.Key, ZeroPwm(borrowed.Key), false);
                }
                catch
                {
                    // refused once the output has its function again
                }

                try
                {
                    await SetFunction(sysid, compid, borrowed.Key, borrowed.Value);
                    _borrowed.Remove(borrowed.Key);
                }
                catch
                {
                    failed.Add($"SERVO{borrowed.Key}_FUNCTION");
                }
            }

            SaveBorrowed(sysid);
            if (failed.Count > 0)
                CustomMessageBox.Show(
                    $"Couldn't set {string.Join(", ", failed)} back after the motor test. The output has no function, " +
                    "so the motor won't run in flight: reconnect and open Motor Setup to set it back.", Strings.ERROR);
        }

        /// <summary>Sets back outputs a test left borrowed on this vehicle, those still with no function.</summary>
        async void SetBackLeftOver()
        {
            uint sysid;
            Dictionary<int, int> functions;
            if (_testing || _borrowed.Count > 0 ||
                !FixedWingMotors.ParseBorrowed(Settings.Instance[BorrowedSetting], out sysid, out functions) ||
                sysid != MainV2.comPort.sysidcurrent)
                return;

            // one given a function since was set up by hand, and is left as it is
            foreach (var function in functions)
                if (FunctionOf(function.Key) == FixedWingMotors.None)
                    _borrowed[function.Key] = function.Value;

            _writing = true;
            UpdateEnabled();
            try
            {
                await SetBack(sysid, (byte)MainV2.comPort.compidcurrent);
            }
            finally
            {
                _writing = false;
            }

            Rebuild();
        }

        void SaveBorrowed(uint sysid)
        {
            if (_borrowed.Count > 0)
                Settings.Instance[BorrowedSetting] = FixedWingMotors.FormatBorrowed(sysid, _borrowed);
            else
                Settings.Instance.Remove(BorrowedSetting);

            try
            {
                Settings.Instance.Save();
            }
            catch
            {
                // kept in memory still
            }
        }

        static async Task SetFunction(uint sysid, byte compid, int servo, int function)
        {
            string name = $"SERVO{servo}_FUNCTION";
            if (!await MainV2.comPort.setParamAsync(sysid, compid, name, function).ConfigureAwait(true))
                throw new Exception($"{name} is not on the vehicle");
        }

        // the output driven to a pulse; when asked to, refused is an error
        static async Task DriveOutput(uint sysid, byte compid, int servo, int pwm, bool acknowledged)
        {
            bool accepted = await MainV2.comPort.doCommandAsync(sysid, compid, MAVLink.MAV_CMD.DO_SET_SERVO, servo, pwm,
                0, 0, 0, 0, 0, acknowledged).ConfigureAwait(true);
            if (acknowledged && !accepted)
                throw new Exception($"the autopilot refused to drive Servo {servo}");
        }

        static int Pwm(int servo, double percent)
        {
            return FixedWingMotors.Pwm(ParamInt($"SERVO{servo}_MIN", 1100), ParamInt($"SERVO{servo}_MAX", 1900),
                ParamInt($"SERVO{servo}_REVERSED", 0) != 0, percent);
        }

        static int ZeroPwm(int servo)
        {
            return Pwm(servo, 0);
        }

        static int ParamInt(string name, int fallback)
        {
            return Param.ContainsKey(name) ? (int)Param[name].Value : fallback;
        }
    }
}
