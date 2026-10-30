using System;

namespace MissionPlanner.Controls.Scene3D
{
    /// <summary>
    /// The pure side of the 3D map's clouds: a tileable cloud-density texture, the layers it is drawn in, and how
    /// high they sit. The layers stay well above the vehicle: the lowest at least <see cref="ClearanceAboveVehicle"/>
    /// over it, and never lower than <see cref="MinimumAboveGround"/> over the ground.
    /// </summary>
    public static class CloudField
    {
        public const double ClearanceAboveVehicle = 1000;
        public const double MinimumAboveGround = 1500;

        /// <summary>One sheet of cloud, from the lowest up.</summary>
        public sealed class Layer
        {
            /// <summary>Metres above the lowest layer.</summary>
            public double Height;

            /// <summary>Size in metres of one tile of the texture, east-west and north-south (a stretched one makes streaks).</summary>
            public double ScaleX, ScaleY;

            /// <summary>The finer second sampling of the texture, as a fraction of the scale.</summary>
            public double DetailScale;

            /// <summary>
            /// Density below which the sky shows through at full cloud density (100); higher is less cloud. Lower
            /// densities raise it, see <see cref="Coverages"/>.
            /// </summary>
            public float Coverage;

            /// <summary>How soft the cloud edges are, in density.</summary>
            public float Softness;

            public float Opacity;

            /// <summary>Drift in m/s, east and north.</summary>
            public double WindX, WindY;
        }

        /// <summary>Cumulus, then patchy mid-level cloud, then thin high streaks.</summary>
        public static readonly Layer[] Layers =
        {
            new Layer { Height = 0, ScaleX = 14000, ScaleY = 14000, DetailScale = 0.29, Coverage = 0.451f, Softness = 0.11f, Opacity = 0.93f, WindX = 7, WindY = 2 },
            new Layer { Height = 1800, ScaleX = 22000, ScaleY = 22000, DetailScale = 0.33, Coverage = 0.500f, Softness = 0.16f, Opacity = 0.62f, WindX = 11, WindY = -3 },
            new Layer { Height = 5000, ScaleX = 52000, ScaleY = 13000, DetailScale = 0.21, Coverage = 0.494f, Softness = 0.22f, Opacity = 0.42f, WindX = 19, WindY = 6 }
        };

        /// <summary>
        /// The texture is also sampled at this fraction of a layer's scale (so many times larger) to vary the
        /// coverage across the sky, giving cloudy and clear stretches instead of an even, repeating carpet.
        /// </summary>
        public const double WeatherScale = 0.17;

        /// <summary>How far the weather moves a layer's coverage either way.</summary>
        public const float WeatherStrength = 0.12f;

        /// <summary>The weight of the finer detail sampling in the density, the shape taking the rest.</summary>
        public const float DetailWeight = 0.25f;

        /// <summary>
        /// How fast the detail and the weather drift, as multiples of the wind: the detail a little faster so the
        /// clouds change shape as they go, the weather slower than the clouds in it.
        /// </summary>
        public const double DetailDrift = 1.4, WeatherDrift = 0.5;

        /// <summary>Where a layer starts and finishes thinning out, as fractions of the terrain's view radius.</summary>
        public const double FadeStart = 0.35, FadeEnd = 0.85;

        /// <summary>Cloud colour where it is thin, and where thickest as seen from below.</summary>
        public static readonly float[] LitColor = { 0.98f, 0.98f, 0.99f };
        public static readonly float[] ShadeColor = { 0.64f, 0.67f, 0.72f };

        /// <summary>The texture's size in pixels (a power of two, so it can repeat) and the seed of its noise.</summary>
        public const int TextureSize = 256, TextureSeed = 1729;

        // the noise: octaves layered from the coarsest, which repeats this many times across the texture, each
        // finer octave at this share of the one before
        const int Octaves = 5;
        const int BasePeriod = 4;
        const double Persistence = 0.5;

        /// <summary>Cloud density from none to full, and the default.</summary>
        public const int MaxDensity = 100, DefaultDensity = 50;

        // the share of sky a coverage gives is measured on a grid of this many points a side across one period of
        // the weather (which repeats every 1 / WeatherScale tiles), and the coverage for a share found in this many
        // halvings
        const int CoverGrid = 120;
        const int CoverSearchSteps = 30;

        /// <summary>
        /// Each layer's coverage threshold for a cloud density from 0 to <see cref="MaxDensity"/>: the share of sky
        /// the layer covers at full density, scaled by density / MaxDensity. How much sky a threshold leaves clear
        /// depends on the noise, so it is measured on the texture with the shader's own sampling.
        /// </summary>
        public static float[] Coverages(float[] texture, int size, int density)
        {
            var coverages = new float[Layers.Length];
            for (int i = 0; i < Layers.Length; i++)
            {
                var samples = Samples(Layers[i], texture, size);
                double target = Cover(Layers[i], samples, Layers[i].Coverage) * Math.Max(0, density) / MaxDensity;

                // more coverage, less cloud: halve the range to the threshold that covers the target share
                double lo = -1, hi = 2;
                for (int step = 0; step < CoverSearchSteps; step++)
                {
                    double mid = (lo + hi) / 2;
                    if (Cover(Layers[i], samples, mid) > target)
                        lo = mid;
                    else
                        hi = mid;
                }

                coverages[i] = (float)((lo + hi) / 2);
            }

            return coverages;
        }

        /// <summary>The share of sky a layer covers with a coverage threshold, measured on the texture.</summary>
        public static double Cover(Layer layer, float[] texture, int size, double coverage)
        {
            return Cover(layer, Samples(layer, texture, size), coverage);
        }

        // the shader's density and weather at the grid's points, as pairs
        static double[] Samples(Layer layer, float[] texture, int size)
        {
            var samples = new double[CoverGrid * CoverGrid * 2];
            double span = 1 / WeatherScale;
            double detail = 1 / layer.DetailScale;
            int o = 0;
            for (int y = 0; y < CoverGrid; y++)
            {
                for (int x = 0; x < CoverGrid; x++)
                {
                    double u = (x + 0.5) / CoverGrid * span, v = (y + 0.5) / CoverGrid * span;
                    double shape = Sample(texture, size, u, v);
                    samples[o++] = shape + (Sample(texture, size, u * detail, v * detail) - shape) * DetailWeight;
                    samples[o++] = Sample(texture, size, u * WeatherScale, v * WeatherScale);
                }
            }

            return samples;
        }

        // the mean of the shader's cloud term over the samples
        static double Cover(Layer layer, double[] samples, double coverage)
        {
            double sum = 0;
            for (int i = 0; i < samples.Length; i += 2)
            {
                double c = coverage + (0.5 - samples[i + 1]) * 2 * WeatherStrength;
                sum += SmoothStep(c, c + layer.Softness, samples[i]);
            }

            return sum / (samples.Length / 2);
        }

        // the texture as the GPU samples it: wrapping, bilinear between texel centres
        static double Sample(float[] texture, int size, double u, double v)
        {
            double x = (u - Math.Floor(u)) * size - 0.5, y = (v - Math.Floor(v)) * size - 0.5;
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double top = Texel(texture, size, x0, y0) + (Texel(texture, size, x0 + 1, y0) - Texel(texture, size, x0, y0)) * fx;
            double bottom = Texel(texture, size, x0, y0 + 1) +
                            (Texel(texture, size, x0 + 1, y0 + 1) - Texel(texture, size, x0, y0 + 1)) * fx;
            return top + (bottom - top) * fy;
        }

        static double Texel(float[] texture, int size, int x, int y)
        {
            return texture[((y % size + size) % size) * size + (x % size + size) % size];
        }

        static double SmoothStep(double edge0, double edge1, double x)
        {
            double t = Math.Max(0, Math.Min(1, (x - edge0) / (edge1 - edge0)));
            return t * t * (3 - 2 * t);
        }

        /// <summary>The lowest layer's altitude AMSL for the ground and the vehicle (or camera) altitude below it.</summary>
        public static double BaseAltitude(double groundHeight, double vehicleAltitude)
        {
            return Math.Max(groundHeight + MinimumAboveGround, vehicleAltitude + ClearanceAboveVehicle);
        }

        /// <summary>
        /// A size x size cloud density texture, 0 to 1, that tiles seamlessly: fractal gradient noise, every octave
        /// periodic over the texture. BGRA with the density in every channel, for <see cref="GLTextures"/>.
        /// </summary>
        public static byte[] Texture(int size, int seed, out float[] density)
        {
            var gradients = Gradients(seed);
            density = new float[size * size];

            double min = double.MaxValue, max = double.MinValue;
            var raw = new double[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double sum = 0, amplitude = 1;
                    int period = BasePeriod;
                    for (int o = 0; o < Octaves; o++)
                    {
                        sum += amplitude * Noise((double)x / size * period, (double)y / size * period, period, gradients, o);
                        amplitude *= Persistence;
                        period *= 2;
                    }

                    raw[y * size + x] = sum;
                    min = Math.Min(min, sum);
                    max = Math.Max(max, sum);
                }
            }

            var pixels = new byte[size * size * 4];
            for (int i = 0; i < raw.Length; i++)
            {
                float value = (float)((raw[i] - min) / Math.Max(1e-9, max - min));
                density[i] = value;
                byte b = (byte)Math.Round(value * 255);
                pixels[i * 4] = b;
                pixels[i * 4 + 1] = b;
                pixels[i * 4 + 2] = b;
                pixels[i * 4 + 3] = b;
            }

            return pixels;
        }

        // gradient noise on a lattice that repeats every period cells, so the texture wraps
        static double Noise(double x, double y, int period, double[] gradients, int octave)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            double fx = x - x0, fy = y - y0;
            double u = Fade(fx), v = Fade(fy);

            double n00 = Dot(gradients, Hash(x0, y0, period, octave), fx, fy);
            double n10 = Dot(gradients, Hash(x0 + 1, y0, period, octave), fx - 1, fy);
            double n01 = Dot(gradients, Hash(x0, y0 + 1, period, octave), fx, fy - 1);
            double n11 = Dot(gradients, Hash(x0 + 1, y0 + 1, period, octave), fx - 1, fy - 1);

            double nx0 = n00 + (n10 - n00) * u;
            double nx1 = n01 + (n11 - n01) * u;
            return nx0 + (nx1 - nx0) * v;
        }

        static double Fade(double t)
        {
            return t * t * t * (t * (t * 6 - 15) + 10);
        }

        static int Hash(int x, int y, int period, int octave)
        {
            x = ((x % period) + period) % period;
            y = ((y % period) + period) % period;
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + octave * 1274126177;
                h = (h ^ (h >> 13)) * 1274126177;
                return (h ^ (h >> 16)) & 255;
            }
        }

        static double Dot(double[] gradients, int index, double x, double y)
        {
            return gradients[index * 2] * x + gradients[index * 2 + 1] * y;
        }

        static double[] Gradients(int seed)
        {
            var random = new Random(seed);
            var gradients = new double[256 * 2];
            for (int i = 0; i < 256; i++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                gradients[i * 2] = Math.Cos(angle);
                gradients[i * 2 + 1] = Math.Sin(angle);
            }

            return gradients;
        }
    }
}
