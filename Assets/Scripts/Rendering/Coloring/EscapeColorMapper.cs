using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// The default escape-value colouring: wrap the count onto the palette and hand interior points
    /// their own colour. Stateless, so one instance serves every renderer and every thread.
    /// </summary>
    public sealed class EscapeColorMapper : IColorMapper
    {
        public void MapRange(
            float[] escapeValues,
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
                target[i] = new Color32(
                    LerpByte(a.r, b.r, t),
                    LerpByte(a.g, b.g, t),
                    LerpByte(a.b, b.b, t),
                    255);
            }
        }

        private static byte LerpByte(byte a, byte b, float t)
        {
            return (byte)(a + (b - a) * t + 0.5f);
        }
    }
}
