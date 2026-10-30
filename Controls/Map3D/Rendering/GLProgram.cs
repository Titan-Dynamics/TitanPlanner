using System;
using System.Collections.Generic;
using OpenTK;
using OpenTK.Graphics.ES20;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// A linked shader program with cached attribute and uniform locations.
    /// </summary>
    public sealed class GLProgram : IDisposable
    {
        readonly Dictionary<string, int> _attributes = new Dictionary<string, int>();
        readonly Dictionary<string, int> _uniforms = new Dictionary<string, int>();

        public int Id { get; private set; }

        GLProgram(int id)
        {
            Id = id;
        }

        /// <summary>
        /// Compiles and links. Shaders use GLSL 1.10 / ES 2.0 syntax so they run on every desktop driver and under Mono.
        /// </summary>
        public static GLProgram Create(string vertexSource, string fragmentSource)
        {
            int vs = Compile(ShaderType.VertexShader, vertexSource);
            int fs = Compile(ShaderType.FragmentShader, fragmentSource);

            int program = GL.CreateProgram();
            GL.AttachShader(program, vs);
            GL.AttachShader(program, fs);
            GL.LinkProgram(program);

            int status;
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out status);
            GL.DetachShader(program, vs);
            GL.DetachShader(program, fs);
            GL.DeleteShader(vs);
            GL.DeleteShader(fs);
            if (status != (int)All.True)
            {
                string log = GL.GetProgramInfoLog(program);
                GL.DeleteProgram(program);
                throw new Exception("Map3D shader link failed: " + log);
            }

            return new GLProgram(program);
        }

        static int Compile(ShaderType type, string source)
        {
            int shader = GL.CreateShader(type);
            GL.ShaderSource(shader, source);
            GL.CompileShader(shader);
            int status;
            GL.GetShader(shader, ShaderParameter.CompileStatus, out status);
            if (status != (int)All.True)
            {
                string log = GL.GetShaderInfoLog(shader);
                GL.DeleteShader(shader);
                throw new Exception("Map3D " + type + " compile failed: " + log);
            }

            return shader;
        }

        public void Use()
        {
            GL.UseProgram(Id);
        }

        public int Attrib(string name)
        {
            int location;
            if (!_attributes.TryGetValue(name, out location))
            {
                location = GL.GetAttribLocation(Id, name);
                _attributes[name] = location;
            }

            return location;
        }

        public int Uniform(string name)
        {
            int location;
            if (!_uniforms.TryGetValue(name, out location))
            {
                location = GL.GetUniformLocation(Id, name);
                _uniforms[name] = location;
            }

            return location;
        }

        public void SetMatrix(string name, ref Matrix4 value)
        {
            GL.UniformMatrix4(Uniform(name), 1, false, ref value.Row0.X);
        }

        public void SetFloat(string name, float value)
        {
            GL.Uniform1(Uniform(name), value);
        }

        public void SetInt(string name, int value)
        {
            GL.Uniform1(Uniform(name), value);
        }

        public void SetVector2(string name, float x, float y)
        {
            GL.Uniform2(Uniform(name), x, y);
        }

        public void SetVector3(string name, float x, float y, float z)
        {
            GL.Uniform3(Uniform(name), x, y, z);
        }

        public void SetColor(string name, float r, float g, float b, float a)
        {
            GL.Uniform4(Uniform(name), r, g, b, a);
        }

        /// <summary>Points an attribute at the currently bound array buffer (offsets and stride in floats).</summary>
        public void Pointer(string name, int size, int strideFloats, int offsetFloats)
        {
            int location = Attrib(name);
            if (location < 0)
                return;
            GL.EnableVertexAttribArray(location);
            GL.VertexAttribPointer(location, size, VertexAttribPointerType.Float, false, strideFloats * sizeof(float),
                (IntPtr)(offsetFloats * sizeof(float)));
        }

        public void DisablePointer(string name)
        {
            int location = Attrib(name);
            if (location >= 0)
                GL.DisableVertexAttribArray(location);
        }

        public void Dispose()
        {
            if (Id != 0)
            {
                GL.DeleteProgram(Id);
                Id = 0;
            }
        }
    }
}
