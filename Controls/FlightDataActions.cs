using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    public partial class FlightDataActions : UserControl
    {
        private const int MinGroupBoxWidth = 350;
        private const int GroupBoxHeight = 154;

        public FlightDataActions()
        {
            InitializeComponent();
            CreateAuxRows();
            CreateServoRows();
            scrollPanel.Resize += ScrollPanel_Resize;
        }

        private void ScrollPanel_Resize(object sender, EventArgs e)
        {
            UpdateGroupBoxWidths();
        }

        private void UpdateGroupBoxWidths()
        {
            if (scrollPanel == null) return;

            // Account for vertical scrollbar if visible
            int scrollBarWidth = scrollPanel.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
            int availableWidth = scrollPanel.ClientSize.Width - scrollBarWidth - mainFlowLayout.Padding.Horizontal;

            // FlowLayoutPanel default margin is 3 on each side = 6 total per control
            int marginPerBox = 6;

            // Calculate how many boxes fit per row at minimum width
            int boxesPerRow = Math.Max(1, availableWidth / (MinGroupBoxWidth + marginPerBox));
            if (boxesPerRow > 4) boxesPerRow = 4;

            // Calculate the width for each box to fill the row
            int boxWidth = (availableWidth / boxesPerRow) - marginPerBox;
            boxWidth = Math.Max(boxWidth, MinGroupBoxWidth);

            grpCommand.Width = boxWidth;
            grpCommand.Height = GroupBoxHeight;
            grpSetpoints.Width = boxWidth;
            grpSetpoints.Height = GroupBoxHeight;
            grpTools.Width = boxWidth;
            grpTools.Height = GroupBoxHeight;
            grpAux.Width = boxWidth;
            grpAux.Height = GroupBoxHeight;
            grpPayload.Width = boxWidth;
            grpPayload.Height = GroupBoxHeight;

            // Servos and Relays share the width of the row of boxes above them, or take a row each when only one
            // box fits, as tall as their rows wrapped to that width
            int rowWidth = boxesPerRow * (boxWidth + marginPerBox) - marginPerBox;
            bool sideBySide = boxesPerRow > 1;
            int width = sideBySide ? (rowWidth - marginPerBox) / 2 : rowWidth;
            int servosHeight = FitToRows(grpServos, flowServos, width);
            int relaysHeight = FitToRows(grpRelays, flowRelays, width);
            if (sideBySide)
                grpServos.Height = grpRelays.Height = Math.Max(servosHeight, relaysHeight);
        }

        // Sizes the box to the width and to the height of its rows wrapped to it, and returns that height
        private static int FitToRows(GroupBox box, FlowLayoutPanel rows, int width)
        {
            box.Width = width;
            var inner = box.DisplayRectangle;
            box.Height += rows.GetPreferredSize(new Size(inner.Width, 0)).Height - inner.Height;
            return box.Height;
        }

        #region Aux functions

        // DO_AUX_FUNCTION switch positions
        private static readonly (string Text, int Position)[] AuxPositions = { ("Low", 0), ("Mid", 1), ("High", 2) };

        private readonly List<(ComboBox Function, MyButton[] Buttons)> _auxRows =
            new List<(ComboBox Function, MyButton[] Buttons)>();

        // the vehicle type the function lists were made for: the functions differ between vehicle types
        private string _auxFunctionsFirmware;

        /// <summary>
        /// Rows that each run an aux function (any RCx_OPTION) on the vehicle at a switch position, as an RC switch
        /// assigned to it would. The function a row runs is kept in the settings (AuxN_desc).
        /// </summary>
        private void CreateAuxRows()
        {
            tableAux.SuspendLayout();
            for (int row = 0; row < tableAux.RowCount; row++)
            {
                // sized and styled from the Control box, which the designer scaled
                var function = new ComboBox
                {
                    Anchor = cmbAction.Anchor,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    DropDownWidth = 250,
                    Font = cmbAction.Font,
                    FormattingEnabled = true,
                    ItemHeight = cmbAction.ItemHeight,
                    Margin = cmbAction.Margin,
                };
                // a vehicle of another type connected since the lists were made
                function.DropDown += (sender, e) => UpdateAuxFunctions();
                tableAux.Controls.Add(function, 0, row);

                var buttons = new MyButton[AuxPositions.Length];
                for (int i = 0; i < AuxPositions.Length; i++)
                {
                    int index = row, position = AuxPositions[i].Position;
                    buttons[i] = new MyButton
                    {
                        Anchor = btnDoAction.Anchor,
                        Enabled = false,
                        Height = btnDoAction.Height,
                        Margin = btnDoAction.Margin,
                        Text = AuxPositions[i].Text,
                        TextColorNotEnabled = btnDoAction.TextColorNotEnabled,
                        UseVisualStyleBackColor = true,
                    };
                    buttons[i].Click += (sender, e) => RunAuxFunction(index, position);
                    tableAux.Controls.Add(buttons[i], i + 1, row);
                }

                _auxRows.Add((function, buttons));
            }

            tableAux.ResumeLayout(false);

            // the lists are made when the box is first shown
            grpAux.VisibleChanged += (sender, e) =>
            {
                if (grpAux.Visible)
                    UpdateAuxFunctions();
            };
        }

        /// <summary>
        /// Fills the function lists for the vehicle type, the first time and again when the type changes.
        /// </summary>
        private void UpdateAuxFunctions()
        {
            string firmware;
            try
            {
                firmware = MainV2.comPort.MAV.cs.firmware.ToString();
            }
            catch
            {
                return;
            }

            if (firmware == _auxFunctionsFirmware)
                return;
            _auxFunctionsFirmware = firmware;

            for (int row = 0; row < _auxRows.Count; row++)
            {
                var function = _auxRows[row].Function;
                function.SelectedValueChanged -= AuxFunction_SelectedValueChanged;

                // each row its own list: rows bound to the same one would share a selection
                function.DisplayMember = "Value";
                function.ValueMember = "Key";
                function.DataSource = ParameterMetaDataRepository.GetParameterOptionsInt("RC6_OPTION", firmware);
                if (int.TryParse(Settings.Instance["Aux" + row + "_desc"], out var saved))
                    function.SelectedValue = saved;

                function.SelectedValueChanged += AuxFunction_SelectedValueChanged;
                UpdateAuxButtons(row);
            }
        }

        private void AuxFunction_SelectedValueChanged(object sender, EventArgs e)
        {
            int row = _auxRows.FindIndex(r => r.Function == sender);
            if (row < 0 || !(_auxRows[row].Function.SelectedValue is int function))
                return;

            Settings.Instance["Aux" + row + "_desc"] = function.ToString();
            UpdateAuxButtons(row);
        }

        // a row on Do Nothing (0) has nothing to run
        private void UpdateAuxButtons(int row)
        {
            bool set = _auxRows[row].Function.SelectedValue is int function && function != 0;
            foreach (var button in _auxRows[row].Buttons)
                button.Enabled = set;
        }

        private void RunAuxFunction(int row, int position)
        {
            if (!(_auxRows[row].Function.SelectedValue is int function))
                return;

            try
            {
                if (!MainV2.comPort.doCommand(MainV2.comPort.sysidcurrent, (byte)MainV2.comPort.compidcurrent,
                        MAVLink.MAV_CMD.DO_AUX_FUNCTION, function, position, 0, 0, 0, 0, 0))
                    CustomMessageBox.Show(Strings.CommandFailed, Strings.ERROR);
            }
            catch (Exception ex)
            {
                CustomMessageBox.Show(Strings.CommandFailed + ex, Strings.ERROR);
            }
        }

        #endregion

        /// <summary>
        /// The servo and relay rows. Each numbers itself in the order made, servos 5 to 16 and relays 1 to 16, which
        /// their saved names and PWM values are kept under.
        /// </summary>
        private void CreateServoRows()
        {
            flowServos.SuspendLayout();
            for (int i = 0; i < 12; i++)
                flowServos.Controls.Add(new ServoOptions());
            flowServos.ResumeLayout(false);

            flowRelays.SuspendLayout();
            for (int i = 0; i < 16; i++)
            {
                // narrowed to its buttons and name, so more fit on a line
                var relay = new RelayOptions();
                relay.Width = relay.Controls.Cast<Control>().Max(c => c.Right) + relay.Margin.Right;
                flowRelays.Controls.Add(relay);
            }
            flowRelays.ResumeLayout(false);

            // Servos and Relays start a row of their own (the Payload box between is never shown)
            mainFlowLayout.SetFlowBreak(grpAux, true);
        }

        public void Initialize()
        {
            ThemeManager.ApplyThemeTo(this);
            UpdateGroupBoxWidths();
        }

        #region Exposed Controls - for FlightData.cs to wire up existing event handlers

        // Command group
        public ComboBox CMB_action => cmbAction;
        public MyButton BUTactiondo => btnDoAction;
        public ComboBox CMB_setwp => cmbWaypoint;
        public MyButton BUT_setwp => btnSetWP;
        public ComboBox CMB_modes => cmbModes;
        public MyButton BUT_setmode => btnSetMode;
        public ComboBox CMB_mountmode => cmbMount;
        public MyButton BUT_mountmode => btnSetMount;
        public MyButton BUTrestartmission => btnRestartMission;
        public MyButton BUT_resumemis => btnResumeMission;

        // Setpoints group
        public ModifyandSet modifyandSetSpeed => modSetSpeed;
        public ModifyandSet modifyandSetAlt => modSetAlt;
        public ModifyandSet modifyandSetLoiterRad => modSetLoiterRad;
        public MyButton BUT_Homealt => btnHomeAlt;

        // Tools group
        public MyButton BUT_RAWSensor => btnRawSensor;
        public MyButton BUT_joystick => btnJoystick;
        public MyButton BUT_SendMSG => btnMessage;
        public MyButton BUT_clear_track => btnClearTrack;
        public MyButton BUT_Reboot => btnReboot;
        public MyButton BUT_abortland => btnAbortLanding;

        // Payload group
        public TrackBar TrackBarPitch => trackBarPitch;
        public TrackBar TrackBarRoll => trackBarRoll;
        public TrackBar TrackBarYaw => trackBarYaw;
        public TextBox TXT_gimbalPitchPos => txtPitchPos;
        public TextBox TXT_gimbalRollPos => txtRollPos;
        public TextBox TXT_gimbalYawPos => txtYawPos;
        public MyButton BUT_resetGimbalPos => btnResetGimbal;
        public MyButton BUT_GimbalVideo => btnGimbalVideo;

        #endregion
    }
}
