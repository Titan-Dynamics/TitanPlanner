using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// A locating beacon on each vehicle other than the selected one: thin rings in the vehicles' colour that grow out
    /// from it to the size of the waypoint markers' rings, fading as they go, facing the camera as the waypoint markers
    /// do, with the vehicle's number just above it, so they can be picked out from far off.
    /// </summary>
    public sealed class VehicleMarkerOverlay : IDisposable
    {
        /// <summary>Markers this close to the camera are left out, where the vehicle is in plain view.</summary>
        const double MinCameraDistance = 30.5;

        // In pixels of the waypoint marker's 300 px image, whose ring has an outer radius of 54: the ring's texture
        // spans 60 of those either side of its centre, and the ring is 4 wide.
        const float ImageHalf = 150, Extent = 60, OuterRadius = 54, RingWidth = 4;
        const int TextureSize = 128;

        /// <summary>How long a ring takes to grow and fade, and how many are out at once, evenly spaced.</summary>
        public const double PulseSeconds = 3.0;
        public const int Pulses = 2;

        /// <summary>A ring's size, as a share of the full one, and its opacity, from its age (0 to 1).</summary>
        public static void Pulse(double age, out double scale, out float opacity)
        {
            // grows quickly at first and settles into the full size, fading all the way
            double eased = 1 - (1 - age) * (1 - age);
            scale = 0.15 + 0.85 * eased;
            opacity = (float)(1 - age);
        }

        readonly QuadRenderer _quads = new QuadRenderer();
        readonly LabelTextures _labels = new LabelTextures();
        readonly List<KeyValuePair<double, int>> _order = new List<KeyValuePair<double, int>>();
        readonly List<VehicleTracker> _vehicles = new List<VehicleTracker>();
        readonly List<string> _names = new List<string>();
        readonly double[] _ages = new double[Pulses];
        int _texture;
        int _textureColor;
        double _cameraX, _cameraY, _cameraZ, _markerHalfSize;

        /// <summary>Starts a frame's markers; markerHalfSize is the waypoint markers' half size, in metres.</summary>
        public void Begin(Color color, double cameraX, double cameraY, double cameraZ, double markerHalfSize)
        {
            if (_texture == 0 || _textureColor != color.ToArgb())
            {
                GLTextures.Delete(ref _texture);
                _texture = RingTexture(color);
                _textureColor = color.ToArgb();
            }

            _cameraX = cameraX;
            _cameraY = cameraY;
            _cameraZ = cameraZ;
            _markerHalfSize = markerHalfSize;
            _vehicles.Clear();
            _names.Clear();
        }

        public void Add(VehicleTracker vehicle, string name)
        {
            _vehicles.Add(vehicle);
            _names.Add(name);
        }

        public void Draw(ref Matrix4 projection, ref Matrix4 view)
        {
            // far to near, so nearer markers blend over farther ones
            _order.Clear();
            for (int i = 0; i < _vehicles.Count; i++)
            {
                double dx = _vehicles[i].X - _cameraX, dy = _vehicles[i].Y - _cameraY, dz = _vehicles[i].Z - _cameraZ;
                double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (distance >= MinCameraDistance)
                    _order.Add(new KeyValuePair<double, int>(distance, i));
            }

            if (_order.Count == 0)
                return;
            _order.Sort((a, b) => b.Key.CompareTo(a.Key));

            double fullHalf = _markerHalfSize * Extent / ImageHalf;
            // half the size of a waypoint's number, its lowest point a little above the vehicle
            double labelHalfSize = _markerHalfSize * 0.2;
            double labelGap = _markerHalfSize * 0.06;
            // every vehicle's rings are in step; the oldest, widest drawn first
            double cycle = DateTime.Now.TimeOfDay.TotalSeconds / PulseSeconds;
            for (int pulse = 0; pulse < Pulses; pulse++)
                _ages[pulse] = (cycle + (double)pulse / Pulses) % 1;
            Array.Sort(_ages);
            Array.Reverse(_ages);

            _quads.Begin();
            foreach (var entry in _order)
            {
                var vehicle = _vehicles[entry.Value];
                double x = vehicle.X, y = vehicle.Y, z = vehicle.Z;

                // Face the camera: the billboard's horizontal axis is perpendicular to the view direction.
                double toCamera = Math.Atan2(_cameraX - x, _cameraY - y);
                double axisX = Math.Sin(toCamera + Math.PI / 2);
                double axisY = Math.Cos(toCamera + Math.PI / 2);

                foreach (double age in _ages)
                {
                    double scale;
                    float opacity;
                    Pulse(age, out scale, out opacity);
                    double half = fullHalf * scale;
                    _quads.Add(_texture,
                        new Vector3d(x - axisX * half, y - axisY * half, z + half),
                        new Vector3d(x + axisX * half, y + axisY * half, z + half),
                        new Vector3d(x - axisX * half, y - axisY * half, z - half),
                        new Vector3d(x + axisX * half, y + axisY * half, z - half),
                        opacity: opacity);
                }

                // mirrored so the text reads correctly
                float aspect, bottom;
                int label = _labels.Get(_names[entry.Value], out aspect, out bottom);
                double across = labelHalfSize * aspect;
                double labelOffset = labelGap + bottom * labelHalfSize;
                _quads.Add(label,
                    new Vector3d(x + axisX * across, y + axisY * across, z + labelOffset + labelHalfSize),
                    new Vector3d(x - axisX * across, y - axisY * across, z + labelOffset + labelHalfSize),
                    new Vector3d(x + axisX * across, y + axisY * across, z + labelOffset - labelHalfSize),
                    new Vector3d(x - axisX * across, y - axisY * across, z + labelOffset - labelHalfSize));
            }

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            GL.Disable(EnableCap.CullFace);
            GL.DepthMask(false);
            GL.Enable(EnableCap.DepthTest);
            _quads.Draw(ref projection, ref view);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }

        static int RingTexture(Color color)
        {
            using (var bitmap = new Bitmap(TextureSize, TextureSize, System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(bitmap))
                using (var pen = new Pen(Color.FromArgb(255, color), RingWidth * TextureSize / 2 / Extent))
                {
                    // clear to the ring's colour, so the smaller mipmaps don't fringe it with another
                    g.Clear(Color.FromArgb(0, color));
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    float centre = TextureSize / 2f;
                    float radius = (OuterRadius - RingWidth / 2) * TextureSize / 2 / Extent;
                    g.DrawEllipse(pen, new RectangleF(centre - radius, centre - radius, 2 * radius, 2 * radius));
                }

                return GLTextures.CreateFromImage(bitmap, true);
            }
        }

        public void Dispose()
        {
            GLTextures.Delete(ref _texture);
            _labels.Dispose();
            _quads.Dispose();
        }
    }
}
