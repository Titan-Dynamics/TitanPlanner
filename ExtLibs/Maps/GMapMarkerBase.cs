using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using GMap.NET;
using GMap.NET.WindowsForms;
using MissionPlanner.Utilities;

namespace MissionPlanner.Maps
{
    [Serializable]
    public class GMapMarkerBase: GMapMarker
    {
        public static bool DisplayCOGSetting = true;
        public static bool DisplayHeadingSetting = true;
        public static bool DisplayNavBearingSetting = true;
        public static bool DisplayRadiusSetting = true;
        public static bool DisplayTargetSetting = true;
        public static int length = 500;
        public static InactiveDisplayStyleEnum InactiveDisplayStyle = InactiveDisplayStyleEnum.Transparent;

        /// <summary>How opaque a vehicle other than the selected one is drawn with the Transparent style.</summary>
        public const float InactiveOpacity = 0.5f;
        protected static readonly int InactiveAlpha = (int)Math.Round(255 * InactiveOpacity);

        // Custom aircraft icon support
        private static Bitmap _customIcon;
        public static Bitmap CustomIcon => _customIcon;
        public static bool HasCustomIcon => _customIcon != null;

        public static bool LoadCustomIcon()
        {
            try
            {
                var customIconPath = Settings.Instance["custom_aircraft_icon"];
                if (!string.IsNullOrEmpty(customIconPath) && File.Exists(customIconPath))
                {
                    _customIcon?.Dispose();

                    // Load and resize to 70x70 while maintaining aspect ratio and centering
                    using (var originalImage = Image.FromFile(customIconPath))
                    {
                        var resizedImage = new Bitmap(70, 70);
                        using (var graphics = Graphics.FromImage(resizedImage))
                        {
                            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                            // Calculate scale to fit within 70x70 while maintaining aspect ratio
                            float scale = Math.Min(70f / originalImage.Width, 70f / originalImage.Height);
                            int newWidth = (int)(originalImage.Width * scale);
                            int newHeight = (int)(originalImage.Height * scale);

                            // Center the image
                            int x = (70 - newWidth) / 2;
                            int y = (70 - newHeight) / 2;

                            graphics.Clear(Color.Transparent);
                            graphics.DrawImage(originalImage, x, y, newWidth, newHeight);
                        }
                        _customIcon = resizedImage;
                    }
                    return true;
                }
                return false;
            }
            catch
            {
                _customIcon = null;
                return false;
            }
        }

        public static void ClearCustomIcon()
        {
            _customIcon?.Dispose();
            _customIcon = null;
        }

        static GMapMarkerBase()
        {
            // Load custom icon on startup if configured
            LoadCustomIcon();
        }
        
        // Instance variables
        public bool IsActive = true;
        protected bool IsHidden => InactiveDisplayStyle == InactiveDisplayStyleEnum.Hidden && !IsActive;
        protected bool IsTransparent => InactiveDisplayStyle == InactiveDisplayStyleEnum.Transparent && !IsActive;
        protected bool DisplayCOG => DisplayCOGSetting && !IsTransparent;
        protected bool DisplayHeading => DisplayHeadingSetting && !IsTransparent;
        protected bool DisplayNavBearing => DisplayNavBearingSetting && !IsTransparent;
        protected bool DisplayRadius => DisplayRadiusSetting && !IsTransparent;
        protected bool DisplayTarget => DisplayTargetSetting && !IsTransparent;
        
        public GMapMarkerBase(PointLatLng pos):base(pos)
        {
        }

        public enum InactiveDisplayStyleEnum
        {
            Normal,
            Transparent,
            Hidden
        }
    }
}
