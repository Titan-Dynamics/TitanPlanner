using System;
using System.Diagnostics;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// The flight data HUD over the 3D view. The HUD itself draws it: an off-screen copy of the DATA tab's HUD
    /// (HUD.CreateOverlay) renders with GDI+ on a transparent background, without its sky and ground, and the
    /// picture is drawn over the scene as a texture. It is redrawn at most RefreshMs apart, not every frame.
    /// </summary>
    public sealed class HudOverlay : IDisposable
    {
        const int RefreshMs = 33;

        readonly QuadRenderer _quad = new QuadRenderer();
        readonly Stopwatch _sinceRefresh = new Stopwatch();
        HUD _hud;
        int _texture;
        int _width;
        int _height;

        public void Draw(HUD source, int viewportWidth, int viewportHeight)
        {
            if (source == null || viewportWidth <= 0 || viewportHeight <= 0)
                return;

            if (_hud == null)
                _hud = HUD.CreateOverlay(source);

            if (_texture == 0 || viewportWidth != _width || viewportHeight != _height ||
                _sinceRefresh.ElapsedMilliseconds >= RefreshMs)
            {
                var picture = _hud.RenderOverlay(viewportWidth, viewportHeight);
                GLTextures.Delete(ref _texture);
                _texture = GLTextures.CreateFromImage(picture, false);
                _width = viewportWidth;
                _height = viewportHeight;
                _sinceRefresh.Restart();
            }

            var projection = Matrix4.CreateOrthographicOffCenter(0, viewportWidth, viewportHeight, 0, -1, 1);
            var identity = Matrix4.Identity;

            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactorSrc.SrcAlpha, BlendingFactorDest.OneMinusSrcAlpha);

            _quad.Begin();
            _quad.Add(_texture, new Vector3d(0, 0, 0), new Vector3d(_width, 0, 0),
                new Vector3d(0, _height, 0), new Vector3d(_width, _height, 0));
            _quad.Draw(ref projection, ref identity);

            GL.DepthMask(true);
            GL.Enable(EnableCap.DepthTest);
        }

        public void Dispose()
        {
            GLTextures.Delete(ref _texture);
            _quad.Dispose();
            _hud?.Dispose();
            _hud = null;
        }
    }
}
