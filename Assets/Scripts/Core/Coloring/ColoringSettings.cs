using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>How the escape count is spread across the palette before it wraps.</summary>
    public enum ColoringMode
    {
        /// <summary>Constant iterations per sweep. Even, and nearly featureless far from the set.</summary>
        Linear = 0,

        /// <summary>
        /// Sweeps compress as the count grows. The exterior of a zoomed-out view spans only a
        /// handful of iterations, so a linear ramp gives all of it one colour; a logarithmic one
        /// spends most of the palette exactly there. Both agree at zero and at one full sweep.
        /// </summary>
        Logarithmic = 1
    }

    /// <summary>
    /// How an escape value becomes a colour. Everything here is a remap of an existing render, not
    /// a reason to recompute one - that separation is what makes palette editing usable at a depth
    /// where a full render takes seconds.
    /// </summary>
    public struct ColoringSettings
    {
        /// <summary>
        /// Interior colour before anything overrides it - also what an unrendered buffer and an
        /// uncovered composite pixel are cleared to, so those never flash a different black.
        /// </summary>
        public static readonly Color32 DefaultInteriorColor = new(3, 5, 12, 255);

        /// <summary>
        /// Use the fractional part of the escape count. Off, the image bands at every integer
        /// iteration; on, the bands become a continuous ramp. Kept as a switch because the banding
        /// is a legitimate look and because it is the obvious first suspect if a new fractal's
        /// smooth value turns out to be wrong.
        /// </summary>
        public bool Smooth;

        /// <summary>How the count is spread across the palette. See <see cref="ColoringMode"/>.</summary>
        public ColoringMode Mode;

        /// <summary>Iterations per full sweep of the palette.</summary>
        public float CycleLength;

        /// <summary>Rotation of the palette, 0..1.</summary>
        public float Offset;

        /// <summary>Colour for points that never escaped within the budget.</summary>
        public Color32 InteriorColor;

        /// <summary>
        /// Palette position for an escape count, before the wrap. Shared so the CPU mapper is the
        /// single definition and the shader has something exact to mirror.
        /// </summary>
        public readonly float Position(float escapeCount)
        {
            var count = Smooth ? escapeCount : Mathf.Floor(escapeCount);
            var cycle = Mathf.Max(1f, CycleLength);
            var normalized = Mode == ColoringMode.Logarithmic
                ? Mathf.Log(1f + Mathf.Max(0f, count)) / Mathf.Log(1f + cycle)
                : count / cycle;
            return normalized + Offset;
        }

        public static ColoringSettings Default => new ColoringSettings
        {
            Smooth = true,
            // Logarithmic by default: a zoomed-out view is almost entirely low counts, and that is
            // the first thing anyone sees when the app opens.
            Mode = ColoringMode.Logarithmic,
            // Matches the hard-coded 0.021 the renderer used before palettes existed, so switching
            // a palette on does not also change the scale of the banding.
            CycleLength = 48f,
            Offset = 0f,
            InteriorColor = DefaultInteriorColor
        }.Sanitized();

        public ColoringSettings Sanitized()
        {
            var result = this;
            result.CycleLength = Mathf.Clamp(result.CycleLength, 1f, 4096f);
            result.Offset = result.Offset - Mathf.Floor(result.Offset);
            return result;
        }

        public bool Equals(in ColoringSettings other)
        {
            return Smooth == other.Smooth &&
                   Mode == other.Mode &&
                   Mathf.Approximately(CycleLength, other.CycleLength) &&
                   Mathf.Approximately(Offset, other.Offset) &&
                   InteriorColor.r == other.InteriorColor.r &&
                   InteriorColor.g == other.InteriorColor.g &&
                   InteriorColor.b == other.InteriorColor.b;
        }
    }
}
