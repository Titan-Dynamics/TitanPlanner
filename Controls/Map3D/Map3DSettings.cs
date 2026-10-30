using System;
using System.Drawing;
using MissionPlanner.Utilities;

namespace MissionPlanner.Controls
{
    /// <summary>
    /// 3D map preferences, persisted in the app settings under map3d_* keys.
    /// </summary>
    public sealed class Map3DSettings
    {
        public double CameraDistance = 0.8;
        public double CameraHeight = 0.2;

        /// <summary>Degrees around the vehicle: 0 behind, 90 right, -90 left.</summary>
        public double CameraAngle;

        public float FieldOfView = 60f;

        /// <summary>Vehicle size multiplier: 1.0 is a 1 m wingspan.</summary>
        public float VehicleScale = 1.0f;

        public Color VehicleColor = Color.Red;
        public string StlPath = "";
        public bool ShowHeadingLine = true;
        public bool ShowNavBearingLine = true;
        public bool ShowGpsHeadingLine = true;
        public bool ShowTurnRadius = true;
        public bool ShowTrail;
        public bool FpvMode;

        /// <summary>
        /// Carry the vehicle and traffic along between telemetry updates and filter their pose and the trail; off,
        /// everything is drawn from the raw telemetry.
        /// </summary>
        public bool SmoothMovement = true;

        /// <summary>The top-right diagnostics panel: frame timing graph, terrain streaming, view and GPU.</summary>
        public bool StatsForNerds;

        /// <summary>The DATA tab's HUD drawn over the 3D view, without its sky and ground.</summary>
        public bool HudOverlay;
        public double WaypointMarkerSize = 60;
        public double AdsbCircleSize = 500;

        public static Map3DSettings Load()
        {
            var s = Settings.Instance;
            var settings = new Map3DSettings
            {
                CameraDistance = s.GetDouble("map3d_camera_dist", 0.8),
                CameraHeight = s.GetDouble("map3d_camera_height", 0.2),
                CameraAngle = s.GetDouble("map3d_camera_angle", 0.0),
                VehicleScale = (float)s.GetDouble("map3d_mav_scale", s.GetDouble("map3d_plane_scale", 1.0)),
                FieldOfView = (float)s.GetDouble("map3d_fov", 60),
                StlPath = s.GetString("map3d_plane_stl_path", ""),
                ShowHeadingLine = s.GetBoolean("map3d_show_heading", true),
                ShowNavBearingLine = s.GetBoolean("map3d_show_nav_bearing", true),
                ShowGpsHeadingLine = s.GetBoolean("map3d_show_gps_heading", true),
                ShowTurnRadius = s.GetBoolean("map3d_show_turn_radius", true),
                ShowTrail = s.GetBoolean("map3d_show_trail", false),
                FpvMode = s.GetBoolean("map3d_fpv_mode", false),
                SmoothMovement = s.GetBoolean("map3d_smooth_movement", true),
                StatsForNerds = s.GetBoolean("map3d_stats_for_nerds", false),
                HudOverlay = s.GetBoolean("map3d_hud_overlay", false),
                WaypointMarkerSize = s.GetDouble("map3d_waypoint_marker_size", 60),
                AdsbCircleSize = s.GetDouble("map3d_adsb_size", 500)
            };

            try
            {
                settings.VehicleColor = Color.FromArgb(s.GetInt32("map3d_mav_color",
                    s.GetInt32("map3d_plane_color", Color.Red.ToArgb())));
            }
            catch
            {
                settings.VehicleColor = Color.Red;
            }

            return settings;
        }

        public void Save()
        {
            var s = Settings.Instance;
            s["map3d_mav_scale"] = VehicleScale.ToString();
            s["map3d_fov"] = FieldOfView.ToString();
            s["map3d_waypoint_marker_size"] = WaypointMarkerSize.ToString();
            s["map3d_adsb_size"] = AdsbCircleSize.ToString();
            s["map3d_mav_color"] = VehicleColor.ToArgb().ToString();
            s["map3d_plane_stl_path"] = StlPath ?? "";
            s["map3d_show_heading"] = ShowHeadingLine.ToString();
            s["map3d_show_nav_bearing"] = ShowNavBearingLine.ToString();
            s["map3d_show_gps_heading"] = ShowGpsHeadingLine.ToString();
            s["map3d_show_turn_radius"] = ShowTurnRadius.ToString();
            s["map3d_show_trail"] = ShowTrail.ToString();
            s["map3d_fpv_mode"] = FpvMode.ToString();
            s["map3d_smooth_movement"] = SmoothMovement.ToString();
            s["map3d_stats_for_nerds"] = StatsForNerds.ToString();
            s["map3d_hud_overlay"] = HudOverlay.ToString();
            SaveCamera();
        }

        /// <summary>Camera placement is also saved on its own when the map closes, keeping mouse adjustments.</summary>
        public void SaveCamera()
        {
            var s = Settings.Instance;
            s["map3d_camera_dist"] = CameraDistance.ToString();
            s["map3d_camera_height"] = CameraHeight.ToString();
            s["map3d_camera_angle"] = CameraAngle.ToString();
            s.Save();
        }
    }
}
