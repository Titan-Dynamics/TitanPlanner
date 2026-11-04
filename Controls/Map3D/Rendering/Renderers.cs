using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// Draws coloured lines (position + RGBA per vertex) from a reusable buffer.
    /// </summary>
    public sealed class LineRenderer : IDisposable
    {
        const int Stride = 7;
        readonly StreamBuffer _buffer = new StreamBuffer();
        GLProgram _program;

        public void Draw(VertexList vertices, PrimitiveType mode, float width, ref Matrix4 projection, ref Matrix4 modelView)
        {
            if (vertices.Count < Stride * 2)
                return;

            if (_program == null)
                _program = GLProgram.Create(Shaders.ColorVertex, Shaders.ColorFragment);

            _program.Use();
            _program.SetMatrix("Projection", ref projection);
            _program.SetMatrix("ModelView", ref modelView);
            _buffer.Upload(vertices.Data, vertices.Count);
            _program.Pointer("Position", 3, Stride, 0);
            _program.Pointer("Color", 4, Stride, 3);
            GL.LineWidth(width);
            GL.DrawArrays(mode, 0, vertices.Count / Stride);
            _program.DisablePointer("Position");
            _program.DisablePointer("Color");
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _program?.Dispose();
        }
    }

    /// <summary>
    /// Batches textured quads (markers, labels, screen overlays) into one buffer per frame.
    /// </summary>
    public sealed class QuadRenderer : IDisposable
    {
        const int Stride = 5;
        readonly StreamBuffer _buffer = new StreamBuffer();
        readonly VertexList _vertices = new VertexList();
        readonly List<int> _textures = new List<int>();
        readonly List<float> _opacities = new List<float>();
        GLProgram _program;

        public void Begin()
        {
            _vertices.Clear();
            _textures.Clear();
            _opacities.Clear();
        }

        /// <summary>
        /// Adds a quad from four corners in the order top-left, top-right, bottom-left, bottom-right,
        /// with UVs (u0,v0)-(u1,v1) running left-to-right and top-to-bottom. Opacity scales the texture's alpha.
        /// </summary>
        public void Add(int texture, Vector3d topLeft, Vector3d topRight, Vector3d bottomLeft, Vector3d bottomRight,
            float u0 = 0, float v0 = 0, float u1 = 1, float v1 = 1, float opacity = 1)
        {
            if (texture == 0)
                return;

            Corner(topLeft, u0, v0);
            Corner(bottomLeft, u0, v1);
            Corner(topRight, u1, v0);
            Corner(topRight, u1, v0);
            Corner(bottomLeft, u0, v1);
            Corner(bottomRight, u1, v1);
            _textures.Add(texture);
            _opacities.Add(opacity);
        }

        void Corner(Vector3d p, float u, float v)
        {
            _vertices.Add((float)p.X, (float)p.Y, (float)p.Z);
            _vertices.Add(u, v);
        }

        public void Draw(ref Matrix4 projection, ref Matrix4 modelView)
        {
            if (_textures.Count == 0)
                return;

            if (_program == null)
                _program = GLProgram.Create(Shaders.TexturedVertex, Shaders.TexturedFragment);

            _program.Use();
            _program.SetMatrix("Projection", ref projection);
            _program.SetMatrix("ModelView", ref modelView);
            _program.SetInt("Texture", 0);
            GL.ActiveTexture(TextureUnit.Texture0);
            _buffer.Upload(_vertices.Data, _vertices.Count);
            _program.Pointer("Position", 3, Stride, 0);
            _program.Pointer("TexCoord", 2, Stride, 3);

            float opacity = float.NaN;
            for (int i = 0; i < _textures.Count; i++)
            {
                if (_opacities[i] != opacity)
                {
                    opacity = _opacities[i];
                    _program.SetFloat("Opacity", opacity);
                }

                GL.BindTexture(TextureTarget.Texture2D, _textures[i]);
                GL.DrawArrays(PrimitiveType.Triangles, i * 6, 6);
            }

            _program.DisablePointer("Position");
            _program.DisablePointer("TexCoord");
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _program?.Dispose();
        }
    }

    /// <summary>
    /// Full-screen sky gradient that follows the true horizon; its horizon colour is also the fog colour.
    /// </summary>
    public sealed class SkyRenderer : IDisposable
    {
        readonly StreamBuffer _buffer = new StreamBuffer();
        readonly float[] _vertices = new float[4 * 5];
        GLProgram _program;

        public void Draw(Matrix4 view, Matrix4 projection, float[] topColor, float[] horizonColor)
        {
            if (_program == null)
                _program = GLProgram.Create(Shaders.SkyVertex, Shaders.SkyFragment);

            // Directions only: drop the camera translation.
            view.Row3 = new Vector4(0, 0, 0, 1);
            var inverse = Matrix4.Invert(Matrix4.Mult(view, projection));

            int o = 0;
            for (int corner = 0; corner < 4; corner++)
            {
                float x = (corner & 1) == 0 ? -1 : 1;
                float y = (corner & 2) == 0 ? -1 : 1;
                var direction = Vector4.Transform(new Vector4(x, y, 1, 1), inverse);
                _vertices[o++] = x;
                _vertices[o++] = y;
                _vertices[o++] = direction.X / direction.W;
                _vertices[o++] = direction.Y / direction.W;
                _vertices[o++] = direction.Z / direction.W;
            }

            GL.Disable(EnableCap.DepthTest);
            GL.DepthMask(false);
            _program.Use();
            _program.SetColor("TopColor", topColor[0], topColor[1], topColor[2], 1);
            _program.SetColor("HorizonColor", horizonColor[0], horizonColor[1], horizonColor[2], 1);
            _buffer.Upload(_vertices, _vertices.Length);
            _program.Pointer("Position", 2, 5, 0);
            _program.Pointer("Direction", 3, 5, 2);
            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);
            _program.DisablePointer("Position");
            _program.DisablePointer("Direction");
            GL.BindBuffer(BufferTarget.ArrayBuffer, 0);
            GL.DepthMask(true);
        }

        public void Dispose()
        {
            _buffer.Dispose();
            _program?.Dispose();
        }
    }
}
