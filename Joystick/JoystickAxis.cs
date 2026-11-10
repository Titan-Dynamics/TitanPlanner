using System;
using System.Linq;
using System.Windows.Forms;

namespace MissionPlanner.Joystick
{
    /// <summary>
    /// An RC channel's row on the joystick page: the axis it takes, a Detect button, its output, expo and reverse.
    /// </summary>
    public class JoystickAxis : UserControl
    {
        public JoystickAxis()
        {
            InitializeComponent();
        }

        private Controls.MyButton BUT_detch;
        private CheckBox revCH;
        private Label label13;
        private TextBox expo_ch;
        private Controls.HorizontalProgressBar ProgressBarCH;
        private ChoiceBox CMB_CH;

        /// <summary>Asked to find the channel's axis; the page sets ChannelValue when it has.</summary>
        public Action Detect;
        public Action SetAxis;
        public Action Reverse;
        public Action Expo;

        public int ChannelNo
        {
            get;
            set;
        }

        public string Label
        {
            get { return label13.Text; }
            set { label13.Text = value; }
        }

        public Array AxisArray
        {
            get { return CMB_CH.Items; }
            set { CMB_CH.Items = value.Cast<object>().ToArray(); }
        }

        public string ChannelValue
        {
            get { return CMB_CH.Text; }
            set { CMB_CH.Text = value; }
        }

        public string ExpoValue
        {
            get { return expo_ch.Text; }
            set { expo_ch.Text = value; }
        }

        public bool ReverseValue
        {
            get { return revCH.Checked; }
            set { revCH.Checked = value; }
        }

        /// <summary>The Detect button says "Detecting..." while the page listens for the axis.</summary>
        public bool Detecting
        {
            set { BUT_detch.Text = value ? "Detecting..." : "Detect"; }
        }

        /// <summary>Where the axis, output, expo and reverse columns start, for the headings above the rows.</summary>
        public int AxisColumn => CMB_CH.Left;
        public int OutputColumn => ProgressBarCH.Left;
        public int ExpoColumn => expo_ch.Left;
        public int ReverseColumn => revCH.Left;

        /// <summary>Shows the channel's output.</summary>
        public void ShowValue(int value)
        {
            ProgressBarCH.maxline = value;
            ProgressBarCH.Value = value;
        }

        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Component Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.BUT_detch = new MissionPlanner.Controls.MyButton();
            this.revCH = new System.Windows.Forms.CheckBox();
            this.label13 = new System.Windows.Forms.Label();
            this.expo_ch = new System.Windows.Forms.TextBox();
            this.ProgressBarCH = new MissionPlanner.Controls.HorizontalProgressBar();
            this.CMB_CH = new MissionPlanner.Joystick.ChoiceBox();
            this.SuspendLayout();
            //
            // BUT_detch
            //
            this.BUT_detch.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.BUT_detch.Location = new System.Drawing.Point(141, 3);
            this.BUT_detch.Name = "BUT_detch";
            this.BUT_detch.Size = new System.Drawing.Size(80, 23);
            this.BUT_detch.TabIndex = 62;
            this.BUT_detch.Text = "Detect";
            this.BUT_detch.UseVisualStyleBackColor = true;
            this.BUT_detch.Click += new System.EventHandler(this.BUT_detch_Click);
            //
            // revCH
            //
            this.revCH.AutoSize = true;
            this.revCH.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.revCH.Location = new System.Drawing.Point(441, 8);
            this.revCH.Name = "revCH";
            this.revCH.Size = new System.Drawing.Size(15, 14);
            this.revCH.TabIndex = 61;
            this.revCH.UseVisualStyleBackColor = true;
            this.revCH.CheckedChanged += new System.EventHandler(this.revCH_CheckedChanged);
            //
            // label13
            //
            this.label13.AutoSize = true;
            this.label13.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.label13.Location = new System.Drawing.Point(2, 8);
            this.label13.Name = "label13";
            this.label13.Size = new System.Drawing.Size(22, 13);
            this.label13.TabIndex = 60;
            this.label13.Text = "RC";
            //
            // expo_ch
            //
            this.expo_ch.BorderStyle = System.Windows.Forms.BorderStyle.None;
            this.expo_ch.Location = new System.Drawing.Point(335, 8);
            this.expo_ch.Name = "expo_ch";
            this.expo_ch.Size = new System.Drawing.Size(100, 13);
            this.expo_ch.TabIndex = 59;
            this.expo_ch.Text = "0";
            this.expo_ch.TextChanged += new System.EventHandler(this.expo_ch_TextChanged);
            //
            // ProgressBarCH
            //
            this.ProgressBarCH.DrawLabel = true;
            this.ProgressBarCH.ImeMode = System.Windows.Forms.ImeMode.NoControl;
            this.ProgressBarCH.Label = null;
            this.ProgressBarCH.Location = new System.Drawing.Point(227, 3);
            this.ProgressBarCH.Maximum = 2200;
            this.ProgressBarCH.maxline = 0;
            this.ProgressBarCH.Minimum = 800;
            this.ProgressBarCH.minline = 0;
            this.ProgressBarCH.Name = "ProgressBarCH";
            this.ProgressBarCH.Size = new System.Drawing.Size(100, 23);
            this.ProgressBarCH.TabIndex = 58;
            this.ProgressBarCH.Value = 800;
            //
            // CMB_CH
            //
            this.CMB_CH.Location = new System.Drawing.Point(65, 4);
            this.CMB_CH.Name = "CMB_CH";
            this.CMB_CH.Size = new System.Drawing.Size(70, 21);
            this.CMB_CH.TabIndex = 57;
            this.CMB_CH.SelectedItemChanged += new System.EventHandler(this.CMB_CH_SelectedIndexChanged);
            //
            // JoystickAxis
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.Controls.Add(this.BUT_detch);
            this.Controls.Add(this.revCH);
            this.Controls.Add(this.label13);
            this.Controls.Add(this.expo_ch);
            this.Controls.Add(this.ProgressBarCH);
            this.Controls.Add(this.CMB_CH);
            this.Name = "JoystickAxis";
            this.Size = new System.Drawing.Size(462, 29);
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private void CMB_CH_SelectedIndexChanged(object sender, EventArgs e)
        {
            SetAxis?.Invoke();
        }

        private void BUT_detch_Click(object sender, EventArgs e)
        {
            Detect?.Invoke();
        }

        private void expo_ch_TextChanged(object sender, EventArgs e)
        {
            Expo?.Invoke();
        }

        private void revCH_CheckedChanged(object sender, EventArgs e)
        {
            Reverse?.Invoke();
        }
    }
}
