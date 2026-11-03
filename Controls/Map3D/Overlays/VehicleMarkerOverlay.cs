using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Thin rings around the vehicles other than the selected one, as big as the waypoint markers' rings and facing the
    /// camera the same way, with the vehicle's number above as waypoint numbers are, so they can be picked out from far
    /// off. The rings are in the vehicles' colour at half opacity.
    /// </summary>
    public sealed class VehicleMarkerOverlay : IDisposable
    {
        /// <summary>Markers this close to the camera are left out, as waypoint markers are near the vehicle.</summary>
        const double MinCameraDistance = 61.0;

        // In pixels of the waypoint marker's 300 px image, whose ring has an outer radius of 54: the ring's texture
        // spans 60 of those either side of its centre, and the ring is 4 wide.
        const float ImageHalf = 150, Extent = 60, OuterRadius = 54, RingWidth = 4;
        const int TextureSize = 128;
        const float RingOpacity = 0.5f;

        readonly QuadRenderer _quads = new QuadRenderer();
        readonly LabelTextures _labels = new LabelTextures();
        readonly List<KeyValuePair<double, int>> _order = new List<KeyValuePair<double, int>>();
        readonly List<VehicleTracker> _vehicles = new List<VehicleTracker>();
        readonly List<string> _names = new List<string>();
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

            double half = _markerHalfSize * Extent / ImageHalf;
            // half the size of a waypoint's number
            double labelHalfSize = _markerHalfSize * 0.2;
            double labelOffset = _markerHalfSize * 0.5;
            _quads.Begin();
            foreach (var entry in _order)
            {
                var vehicle = _vehicles[entry.Value];
                double x = vehicle.X, y = vehicle.Y, z = vehicle.Z;

                // Face the camera: the billboard's horizontal axis is perpendicular to the view direction.
                double toCamera = Math.Atan2(_cameraX - x, _cameraY - y);
                double axisX = Math.Sin(toCamera + Math.PI / 2);
                double axisY = Math.Cos(toCamera + Math.PI / 2);
                _quads.Add(_texture,
                    new Vector3d(x - axisX * half, y - axisY * half, z + half),
                    new Vector3d(x + axisX * half, y + axisY * half, z + half),
                    new Vector3d(x - axisX * half, y - axisY * half, z - half),
                    new Vector3d(x + axisX * half, y + axisY * half, z - half));

                // above the ring, as waypoint numbers sit; mirrored so the text reads correctly
                float aspect;
                int label = _labels.Get(_names[entry.Value], out aspect);
                double across = labelHalfSize * aspect;
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
                using (var pen = new Pen(Color.FromArgb((int)Math.Round(255 * RingOpacity), color), RingWidth * TextureSize / 2 / Extent))
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
