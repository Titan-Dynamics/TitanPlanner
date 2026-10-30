using System;
using System.Diagnostics;
using System.Reflection;
using log4net;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Layers of cloud well above the vehicle (see <see cref="CloudField"/>), drifting with the wind. Each layer is one
    /// quad around the camera, coloured from a tiling density texture in the fragment shader; the texture is placed
    /// by absolute UTM position, so the clouds stay put over the ground as the scene's origin moves. Drawn after the
    /// terrain, depth-tested against it but not writing depth, highest layer first.
    /// </summary>
    public sealed class CloudRenderer : IDisposable
    {
        // seconds the layers take to settle at a new height, so they don't jump with the ground under the vehicle
        const double HeightEase = 3;

        // the longest step, in seconds, the height eases by in one frame, so a pause doesn't snap it
        const double MaxStep = 1;

        static readonly ILog log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

        readonly StreamBuffer _buffer = new StreamBuffer();
        readonly float[] _vertices = new float[4 * 3];
        readonly Stopwatch _clock = Stopwatch.StartNew();
        GLProgram _program;
        int _texture;
        double _base = double.NaN;
        double _lastTime;

        // the texture's density values, and the layers' coverage thresholds for the density they were worked out for
        float[] _textureDensity;
        float[] _coverages;
        int _coveragesDensity = -1;

        // a driver that rejects the shader leaves the sky clear rather than failing every frame
        bool _failed;

        /// <summary>Draws the layers at a cloud density from 1 to <see cref="CloudField.MaxDensity"/>.</summary>
        public void Draw(WorldFrame frame, double cameraX, double cameraY, double cameraZ, double groundHeight,
            double vehicleAltitude, double viewRadius, float[] fogColor, int density, ref Matrix4 projection, ref Matrix4 view)
        {
            if (_failed || density <= 0)
                return;

            if (_program == null)
            {
                try
                {
                    _program = GLProgram.Create(Shaders.CloudVertex, Shaders.CloudFragment);
                }
                catch (Exception ex)
                {
                    _failed = true;
                    log.Error(ex);
                    return;
                }
            }

            if (_texture == 0)
                CreateTexture();
            if (density != _coveragesDensity)
            {
                _coverages = CloudField.Coverages(_textureDensity, CloudField.TextureSize, density);
                _coveragesDensity = density;
            }

            double now = _clock.Elapsed.TotalSeconds;
            double dt = Math.Min(MaxStep, now - _lastTime);
            _lastTime = now;
            double target = CloudField.BaseAltitude(groundHeight, vehicleAltitude);
            _base = double.IsNaN(_base) ? target : _base + (target - _base) * (1 - Math.Exp(-dt / HeightEase));

            float fogStart, fogEnd;
            TerrainEngine.FogRange(viewRadius, out fogStart, out fogEnd);
            double fadeEnd = viewRadius * CloudField.FadeEnd;

            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
            GL.DepthMask(false);

            _program.Use();
            _program.SetMatrix("Projection", ref projection);
            _program.SetMatrix("ModelView", ref view);
            _program.SetInt("Texture", 0);
            _program.SetVector3("Camera", (float)cameraX, (float)cameraY, (float)cameraZ);
            _program.SetFloat("FadeStart", (float)(viewRadius * CloudField.FadeStart));
            _program.SetFloat("FadeEnd", (float)fadeEnd);
            _program.SetFloat("FogStart", fogStart);
            _program.SetFloat("FogEnd", fogEnd);
            _program.SetColor("FogColor", fogColor[0], fogColor[1], fogColor[2], 1);
            var lit = CloudField.LitColor;
            var shade = CloudField.ShadeColor;
            _program.SetVector3("LitColor", lit[0], lit[1], lit[2]);
            _program.SetVector3("ShadeColor", shade[0], shade[1], shade[2]);
            GL.ActiveTexture(TextureUnit.Texture0);
            GL.BindTexture(TextureTarget.Texture2D, _texture);

            // farthest (highest) first, so nearer layers blend over them
            for (int i = CloudField.Layers.Length - 1; i >= 0; i--)
            {
                var layer = CloudField.Layers[i];
                float z = (float)(_base + layer.Height);
                if (z < cameraZ)
                    continue;

                double scaleX = 1 / layer.ScaleX, scaleY = 1 / layer.ScaleY;
                double detail = 1 / layer.DetailScale;

                // the scene origin's place in the texture for each sampling, carried back by the wind
                double driftX = layer.WindX * now, driftY = layer.WindY * now;
                double detailDriftX = driftX * CloudField.DetailDrift, detailDriftY = driftY * CloudField.DetailDrift;
                double weatherDriftX = driftX * CloudField.WeatherDrift, weatherDriftY = driftY * CloudField.WeatherDrift;
                _program.SetVector2("Scale", (float)scaleX, (float)scaleY);
                _program.SetVector2("Offset",
                    Wrap((frame.OriginEasting - driftX) * scaleX), Wrap((frame.OriginNorthing - driftY) * scaleY));
                _program.SetVector2("DetailOffset",
                    Wrap((frame.OriginEasting - detailDriftX) * scaleX * detail),
                    Wrap((frame.OriginNorthing - detailDriftY) * scaleY * detail));
                _program.SetFloat("DetailFactor", (float)detail);
                _program.SetFloat("DetailWeight", CloudField.DetailWeight);
                _program.SetFloat("WeatherFactor", (float)CloudField.WeatherScale);
                _program.SetVector2("WeatherOffset",
                    Wrap((frame.OriginEasting - weatherDriftX) * scaleX * CloudField.WeatherScale),
                    Wrap((frame.OriginNorthing - weatherDriftY) * scaleY * CloudField.WeatherScale));
                _program.SetFloat("WeatherStrength", CloudField.WeatherStrength);
                _program.SetFloat("Coverage", _coverages[i]);
                _program.SetFloat("Softness", layer.Softness);
                _program.SetFloat("Opacity", layer.Opacity);

                Quad(cameraX, cameraY, z, fadeEnd);
                _buffer.Upload(_vertices, _vertices.Length);
                _program.Pointer("Position", 3, 3, 0);
                GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            }

            _program.DisablePointer("Position");
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DepthMask(true);
            GL.Disable(EnableCap.Blend);
        }

        // a square of the given half size around the camera at height z
        void Quad(double x, double y, float z, double half)
        {
            int o = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                _vertices[o++] = (float)(x + ((corner & 1) == 0 ? -half : half));
                _vertices[o++] = (float)(y + ((corner & 2) == 0 ? -half : half));
                _vertices[o++] = z;
            }
        }

        // the fractional part, for texture coordinates that wrap
        static float Wrap(double value)
        {
            return (float)(value - Math.Floor(value));
        }

        void CreateTexture()
        {
            var pixels = CloudField.Texture(CloudField.TextureSize, CloudField.TextureSeed, out _textureDensity);
            _texture = GLTextures.CreateFromBgra(pixels, CloudField.TextureSize, CloudField.TextureSize, true);
            if (_texture == 0)
                return;

            GL.BindTexture(TextureTarget.Texture2D, _texture);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
        }

        public void Dispose()
        {
            GLTextures.Delete(ref _texture);
            _buffer.Dispose();
            _program?.Dispose();
        }
    }
}
