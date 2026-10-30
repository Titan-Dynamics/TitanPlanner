namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// GLSL sources. Written for GLSL 1.10 / ES 2.0 (no #version) to match the legacy contexts GLControl
    /// creates on Windows, Linux and macOS.
    /// </summary>
    public static class Shaders
    {
        const string FloatPrecision = @"
#ifdef GL_ES
precision highp float;
#endif
";

        /// <summary>Textured terrain with distance fog that fades into the sky's horizon colour.</summary>
        public const string TerrainVertex = @"
attribute vec3 Position;
attribute vec2 TexCoord;
uniform mat4 ModelView;
uniform mat4 Projection;
varying vec2 vTexCoord;
varying float vDistance;
void main(void) {
    vec4 viewPos = ModelView * vec4(Position, 1.0);
    vDistance = length(viewPos.xyz);
    vTexCoord = TexCoord;
    gl_Position = Projection * viewPos;
}
";

        public const string TerrainFragment = FloatPrecision + @"
uniform sampler2D Texture;
uniform float FogStart;
uniform float FogEnd;
uniform vec4 FogColor;
varying vec2 vTexCoord;
varying float vDistance;
void main(void) {
    vec3 color = texture2D(Texture, vTexCoord).rgb;
    float fog = smoothstep(FogStart, FogEnd, vDistance);
    gl_FragColor = vec4(mix(color, FogColor.rgb, fog), 1.0);
}
";

        /// <summary>Per-vertex coloured geometry: lines, ADS-B circles.</summary>
        public const string ColorVertex = @"
attribute vec3 Position;
attribute vec4 Color;
uniform mat4 ModelView;
uniform mat4 Projection;
varying vec4 vColor;
void main(void) {
    vColor = Color;
    gl_Position = Projection * ModelView * vec4(Position, 1.0);
}
";

        public const string ColorFragment = FloatPrecision + @"
varying vec4 vColor;
void main(void) {
    gl_FragColor = vColor;
}
";

        /// <summary>Textured quads: waypoint markers, labels and screen overlays.</summary>
        public const string TexturedVertex = @"
attribute vec3 Position;
attribute vec2 TexCoord;
uniform mat4 ModelView;
uniform mat4 Projection;
varying vec2 vTexCoord;
void main(void) {
    vTexCoord = TexCoord;
    gl_Position = Projection * ModelView * vec4(Position, 1.0);
}
";

        public const string TexturedFragment = FloatPrecision + @"
uniform sampler2D Texture;
varying vec2 vTexCoord;
void main(void) {
    gl_FragColor = texture2D(Texture, vTexCoord);
}
";

        /// <summary>The vehicle model, lit in world space so the shading stays put as the vehicle turns.</summary>
        public const string LitVertex = @"
attribute vec3 Position;
attribute vec3 Normal;
uniform mat4 Model;
uniform mat4 ModelView;
uniform mat4 Projection;
uniform vec3 LightDirection;
uniform vec4 BaseColor;
varying vec4 vColor;
void main(void) {
    vec3 n = normalize((Model * vec4(Normal, 0.0)).xyz);
    float light = min(1.0, 0.3 + 0.7 * max(dot(n, LightDirection), 0.0));
    vColor = vec4(BaseColor.rgb * light, BaseColor.a);
    gl_Position = Projection * ModelView * vec4(Position, 1.0);
}
";

        public const string LitFragment = ColorFragment;

        /// <summary>
        /// Full-screen sky. Each corner carries its world view direction, so the gradient follows the real
        /// horizon through any pitch or roll.
        /// </summary>
        public const string SkyVertex = @"
attribute vec2 Position;
attribute vec3 Direction;
varying vec3 vDirection;
void main(void) {
    vDirection = Direction;
    gl_Position = vec4(Position, 0.0, 1.0);
}
";

        public const string SkyFragment = FloatPrecision + @"
uniform vec4 TopColor;
uniform vec4 HorizonColor;
varying vec3 vDirection;
void main(void) {
    float elevation = normalize(vDirection).z;
    gl_FragColor = mix(HorizonColor, TopColor, smoothstep(0.0, 0.6, elevation));
}
";

        /// <summary>A sheet of cloud: a horizontal quad in scene metres around the camera.</summary>
        public const string CloudVertex = @"
attribute vec3 Position;
uniform mat4 ModelView;
uniform mat4 Projection;
varying vec3 vPosition;
void main(void) {
    vPosition = Position;
    gl_Position = Projection * ModelView * vec4(Position, 1.0);
}
";

        /// <summary>
        /// Cloud from a tiling density texture sampled three times: the shape, finer detail (each drifting with the
        /// wind), and a much larger weather pattern that moves the coverage, so the sky has cloudy and clear stretches
        /// instead of a repeating carpet. Cut off below the coverage so sky shows between clouds, darker where
        /// thicker as seen from below, thinned out with distance and fogged into the horizon colour like the terrain.
        /// </summary>
        public const string CloudFragment = FloatPrecision + @"
uniform sampler2D Texture;
uniform vec2 Scale;
uniform vec2 Offset;
uniform float DetailFactor;
uniform vec2 DetailOffset;
uniform float DetailWeight;
uniform float WeatherFactor;
uniform vec2 WeatherOffset;
uniform float WeatherStrength;
uniform float Coverage;
uniform float Softness;
uniform float Opacity;
uniform vec3 Camera;
uniform float FadeStart;
uniform float FadeEnd;
uniform float FogStart;
uniform float FogEnd;
uniform vec4 FogColor;
uniform vec3 LitColor;
uniform vec3 ShadeColor;
varying vec3 vPosition;
// fragments fainter than this are dropped rather than blended
const float MinAlpha = 0.004;
void main(void) {
    vec2 uv = vPosition.xy * Scale;
    float density = mix(texture2D(Texture, uv + Offset).r, texture2D(Texture, uv * DetailFactor + DetailOffset).r, DetailWeight);
    float weather = texture2D(Texture, uv * WeatherFactor + WeatherOffset).r;
    float coverage = Coverage + (0.5 - weather) * 2.0 * WeatherStrength;
    float cloud = smoothstep(coverage, coverage + Softness, density);
    float across = length(vPosition.xy - Camera.xy);
    float alpha = cloud * Opacity * (1.0 - smoothstep(FadeStart, FadeEnd, across));
    if (alpha < MinAlpha)
        discard;
    vec3 color = mix(LitColor, ShadeColor, smoothstep(coverage, 1.0, density));
    float fog = smoothstep(FogStart, FogEnd, length(vPosition - Camera));
    gl_FragColor = vec4(mix(color, FogColor.rgb, fog), alpha);
}
";
    }
}
