using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// The default escape-value colouring: wrap the count onto the palette and hand interior points
    /// their own colour, then, with the relief on, light the result. Stateless, so one instance
    /// serves every renderer and every thread.
    /// </summary>
    public sealed class EscapeColorMapper : IColorMapper
    {
        public void MapRange(
            float[] escapeValues,
            int[] slopes,
            Color32[] target,
            int start,
            int count,
            PaletteData palette,
            in ColoringSettings settings)
        {
            if (escapeValues == null || target == null || palette == null)
            {
                return;
            }

            var end = Mathf.Min(Mathf.Min(escapeValues.Length, target.Length), start + count);
            var interior = settings.InteriorColor;
            var cycle = Mathf.Max(1f, settings.CycleLength);
            var inverseCycle = 1f / cycle;
            var inverseLogCycle = 1f / Mathf.Log(1f + cycle);
            var logarithmic = settings.Mode == ColoringMode.Logarithmic;
            var offset = settings.Offset;
            var smooth = settings.Smooth;
            var stops = palette.Count;

            var relief = settings.Relief && slopes != null && slopes.Length >= end;
            var light = relief ? ReliefLight.From(settings) : default;

            for (var i = Mathf.Max(0, start); i < end; i++)
            {
                var value = escapeValues[i];
                if (value < 0f)
                {
                    target[i] = interior;
                    continue;
                }

                // Dropping the fraction here rather than in the sampler is what makes the smooth
                // switch a remap instead of a re-render. Same for the mode: this loop is the one
                // definition of the mapping, and ColoringSettings.Position mirrors it for callers
                // that need a single value.
                var escapeCount = smooth ? value : Mathf.Floor(value);
                var position = logarithmic
                    ? Mathf.Log(1f + escapeCount) * inverseLogCycle + offset
                    : escapeCount * inverseCycle + offset;
                position -= Mathf.Floor(position);

                var scaled = position * stops;
                var first = (int)scaled;
                if (first >= stops)
                {
                    first = stops - 1;
                }

                var second = first + 1;
                if (second >= stops)
                {
                    second = 0;
                }

                var t = scaled - first;
                var a = palette[first];
                var b = palette[second];
                var red = a.r + (b.r - a.r) * t;
                var green = a.g + (b.g - a.g) * t;
                var blue = a.b + (b.b - a.b) * t;

                if (relief)
                {
                    Shade(light, slopes[i], ref red, ref green, ref blue);
                }

                target[i] = new Color32((byte)(red + 0.5f), (byte)(green + 0.5f), (byte)(blue + 0.5f), 255);
            }
        }

        /// <summary>
        /// The relief's per-pixel lighting, on channels in 0..255. Mirrors
        /// <c>FractalCommon.hlsl:FractalReliefColor</c> - keep the two in step.
        ///
        /// The surface normal tilts by <see cref="ReliefLight.Slope"/> towards the slope's direction.
        /// Diffuse light over ambient, divided by what a flat surface gets, multiplies the colour
        /// while it is below 1; above 1 it blends towards white by (i - 1) / i instead of clipping, so
        /// a lit slope turns paler, never flat white - with a low sun i reaches 2 and more, and the
        /// plain min(1, i - 1) washed whole slopes out. The highlight is Blinn-Phong, blended towards
        /// white.
        /// </summary>
        private static void Shade(in ReliefLight light, int packedSlope, ref float red, ref float green, ref float blue)
        {
            ReliefLight.UnpackSlope(packedSlope, out var slopeX, out var slopeY);
            var nx = slopeX * light.Slope;
            var ny = slopeY * light.Slope;
            var inverseLength = 1f / Mathf.Sqrt(nx * nx + ny * ny + 1f);

            var diffuse = Mathf.Max(0f, (nx * light.LightX + ny * light.LightY + light.LightZ) * inverseLength);
            var illumination = (ReliefLight.Ambient + (1f - ReliefLight.Ambient) * diffuse) * light.InverseFlat;

            if (illumination <= 1f)
            {
                red *= illumination;
                green *= illumination;
                blue *= illumination;
            }
            else
            {
                var towardsWhite = (illumination - 1f) / illumination;
                red += (255f - red) * towardsWhite;
                green += (255f - green) * towardsWhite;
                blue += (255f - blue) * towardsWhite;
            }

            if (light.Specular > 0f)
            {
                var facing = Mathf.Max(0f, (nx * light.HalfX + ny * light.HalfY + light.HalfZ) * inverseLength);
                var highlight = Mathf.Min(1f, light.Specular * Mathf.Pow(facing, light.Shininess));
                red += (255f - red) * highlight;
                green += (255f - green) * highlight;
                blue += (255f - blue) * highlight;
            }
        }
    }
}
