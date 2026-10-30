using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// The 3D map settings window. Values are applied and saved when it closes.
    /// </summary>
    public static class Map3DSettingsDialog
    {
        public static void Show(IWin32Window owner, Map3DSettings settings)
        {
            using (var dialog = new Form())
            {
                dialog.Text = "3D Map Settings";
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.AutoSize = true;
                dialog.AutoSizeMode = AutoSizeMode.GrowAndShrink;
                dialog.Padding = new Padding(10);

                var layout = new TableLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    ColumnCount = 2,
                    Dock = DockStyle.Fill,
                    Padding = new Padding(5)
                };
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

                int row = 0;
                const int inputWidth = 100;

                Action<string, Control> addRow = (labelText, control) =>
                {
                    var label = new Label { Text = labelText, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 6, 3, 3) };
                    control.Width = inputWidth;
                    control.Anchor = AnchorStyles.Left;
                    layout.Controls.Add(label, 0, row);
                    layout.Controls.Add(control, 1, row);
                    row++;
                };

                Action<CheckBox> addCheckbox = check =>
                {
                    check.AutoSize = true;
                    check.Anchor = AnchorStyles.Left;
                    check.Margin = new Padding(3);
                    layout.SetColumnSpan(check, 2);
                    layout.Controls.Add(check, 0, row);
                    row++;
                };

                Func<double, double, double, decimal> clamp = (value, min, max) => (decimal)Math.Max(min, Math.Min(max, value));

                var numDist = new NumericUpDown { Minimum = 0.1m, Maximum = 100, DecimalPlaces = 2, Increment = 0.05m, Value = clamp(settings.CameraDistance, 0.1, 100) };
                addRow("Camera Dist:", numDist);

                var numHeight = new NumericUpDown { Minimum = -100, Maximum = 100, DecimalPlaces = 2, Increment = 0.05m, Value = clamp(settings.CameraHeight, -100, 100) };
                addRow("Camera Height:", numHeight);

                var numFov = new NumericUpDown { Minimum = 30, Maximum = 120, Increment = 5, Value = clamp(settings.FieldOfView, 30, 120) };
                addRow("Camera FoV:", numFov);

                var numScale = new NumericUpDown { Minimum = 0.1m, Maximum = 10, DecimalPlaces = 2, Increment = 0.05m, Value = clamp(settings.VehicleScale, 0.1, 10) };
                addRow("MAV Scale (m):", numScale);

                var numMarkerSize = new NumericUpDown { Minimum = 10, Maximum = 500, Increment = 10, Value = clamp(settings.WaypointMarkerSize, 10, 500) };
                addRow("WP Marker Size:", numMarkerSize);

                var numAdsbSize = new NumericUpDown { Minimum = 50, Maximum = 2000, Increment = 50, Value = clamp(settings.AdsbCircleSize, 50, 2000) };
                addRow("ADSB Size:", numAdsbSize);

                Color selectedColor = settings.VehicleColor;
                var colorPanel = new Panel { Height = 23, BorderStyle = BorderStyle.FixedSingle, Cursor = Cursors.Hand, Tag = "IgnoreTheme", BackColor = selectedColor };
                colorPanel.Click += (s, e) =>
                {
                    using (var colorDialog = new ColorDialog { Color = selectedColor, FullOpen = true })
                    {
                        if (colorDialog.ShowDialog() == DialogResult.OK)
                        {
                            selectedColor = colorDialog.Color;
                            colorPanel.BackColor = selectedColor;
                        }
                    }
                };
                addRow("MAV Color:", colorPanel);

                string selectedStl = settings.StlPath;
                var stlButton = new MyButton { Text = string.IsNullOrEmpty(selectedStl) ? "Default" : Path.GetFileName(selectedStl), Height = 23 };
                stlButton.Click += (s, e) =>
                {
                    using (var open = new OpenFileDialog { Filter = "STL Files (*.stl)|*.stl|All Files (*.*)|*.*", Title = "Select Plane STL File" })
                    {
                        if (!string.IsNullOrEmpty(selectedStl) && File.Exists(selectedStl))
                            open.InitialDirectory = Path.GetDirectoryName(selectedStl);
                        if (open.ShowDialog() == DialogResult.OK)
                        {
                            selectedStl = open.FileName;
                            stlButton.Text = Path.GetFileName(selectedStl);
                        }
                    }
                };
                addRow("STL File:", stlButton);

                var chkHeading = new CheckBox { Text = "Heading Line (Red)", Checked = settings.ShowHeadingLine };
                addCheckbox(chkHeading);
                var chkNavBearing = new CheckBox { Text = "Nav Bearing Line (Orange)", Checked = settings.ShowNavBearingLine };
                addCheckbox(chkNavBearing);
                var chkGpsHeading = new CheckBox { Text = "GPS Heading Line (Black)", Checked = settings.ShowGpsHeadingLine };
                addCheckbox(chkGpsHeading);
                var chkTurnRadius = new CheckBox { Text = "Turn Radius Arc (Pink)", Checked = settings.ShowTurnRadius };
                addCheckbox(chkTurnRadius);
                var chkTrail = new CheckBox { Text = "Flight Path Trail", Checked = settings.ShowTrail };
                addCheckbox(chkTrail);
                var chkFpv = new CheckBox { Text = "FPV Mode (camera at aircraft)", Checked = settings.FpvMode };
                chkFpv.CheckedChanged += (s, e) =>
                {
                    numDist.Enabled = !chkFpv.Checked;
                    numHeight.Enabled = !chkFpv.Checked;
                };
                numDist.Enabled = !settings.FpvMode;
                numHeight.Enabled = !settings.FpvMode;
                addCheckbox(chkFpv);
                var chkSmooth = new CheckBox { Text = "Smooth movement", Checked = settings.SmoothMovement };
                addCheckbox(chkSmooth);
                var chkHud = new CheckBox { Text = "HUD overlay", Checked = settings.HudOverlay };
                addCheckbox(chkHud);
                var chkStats = new CheckBox { Text = "Stats for nerds", Checked = settings.StatsForNerds };
                addCheckbox(chkStats);

                var buttons = new FlowLayoutPanel
                {
                    AutoSize = true,
                    AutoSizeMode = AutoSizeMode.GrowAndShrink,
                    Anchor = AnchorStyles.None,
                    Margin = new Padding(0, 10, 0, 0)
                };

                var saveButton = new MyButton { Text = "Save", Width = 75, Margin = new Padding(10, 0, 0, 0) };
                saveButton.Click += (s, e) => dialog.Close();

                var resetButton = new MyButton { Text = "Reset", Width = 75, Margin = new Padding(10, 0, 0, 0) };
                resetButton.Click += (s, e) =>
                {
                    var defaults = new Map3DSettings();
                    numDist.Value = (decimal)defaults.CameraDistance;
                    numHeight.Value = (decimal)defaults.CameraHeight;
                    numFov.Value = (decimal)defaults.FieldOfView;
                    numScale.Value = (decimal)defaults.VehicleScale;
                    numMarkerSize.Value = (decimal)defaults.WaypointMarkerSize;
                    numAdsbSize.Value = (decimal)defaults.AdsbCircleSize;
                    selectedColor = defaults.VehicleColor;
                    colorPanel.BackColor = selectedColor;
                    selectedStl = "";
                    stlButton.Text = "Default";
                    chkHeading.Checked = defaults.ShowHeadingLine;
                    chkNavBearing.Checked = defaults.ShowNavBearingLine;
                    chkGpsHeading.Checked = defaults.ShowGpsHeadingLine;
                    chkTurnRadius.Checked = defaults.ShowTurnRadius;
                    chkTrail.Checked = defaults.ShowTrail;
                    chkFpv.Checked = defaults.FpvMode;
                    chkSmooth.Checked = defaults.SmoothMovement;
                    chkHud.Checked = defaults.HudOverlay;
                    chkStats.Checked = defaults.StatsForNerds;
                    settings.CameraAngle = 0.0;
                    dialog.Close();
                };

                buttons.Controls.Add(saveButton);
                buttons.Controls.Add(resetButton);
                layout.SetColumnSpan(buttons, 2);
                layout.Controls.Add(buttons, 0, row);
                dialog.Controls.Add(layout);

                ThemeManager.ApplyThemeTo(dialog);

                dialog.FormClosing += (s, e) =>
                {
                    settings.CameraDistance = (double)numDist.Value;
                    settings.CameraHeight = (double)numHeight.Value;
                    settings.VehicleScale = (float)numScale.Value;
                    settings.FieldOfView = (float)numFov.Value;
                    settings.WaypointMarkerSize = (double)numMarkerSize.Value;
                    settings.AdsbCircleSize = (double)numAdsbSize.Value;
                    settings.VehicleColor = selectedColor;
                    settings.StlPath = selectedStl;
                    settings.ShowHeadingLine = chkHeading.Checked;
                    settings.ShowNavBearingLine = chkNavBearing.Checked;
                    settings.ShowGpsHeadingLine = chkGpsHeading.Checked;
                    settings.ShowTurnRadius = chkTurnRadius.Checked;
                    settings.ShowTrail = chkTrail.Checked;
                    settings.FpvMode = chkFpv.Checked;
                    settings.SmoothMovement = chkSmooth.Checked;
                    settings.HudOverlay = chkHud.Checked;
                    settings.StatsForNerds = chkStats.Checked;
                    settings.Save();
                };

                dialog.ShowDialog(owner);
            }
        }
    }
}
