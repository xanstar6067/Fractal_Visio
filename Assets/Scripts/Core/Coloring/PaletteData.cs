using System;
using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// A colour ramp, baked to a fixed number of stops and sampled cyclically.
    ///
    /// Cyclic is the operative word: escape-time colouring maps an unbounded iteration count onto
    /// the ramp by wrapping, so a palette whose two ends do not meet shows a hard seam every time
    /// the count crosses a cycle boundary. Every built-in palette therefore starts and ends on the
    /// same colour, and <see cref="Sample"/> interpolates from the last stop back to the first.
    ///
    /// Plain data with no Unity object behind it, so the same instance serves the CPU mapper and
    /// the GPU palette texture. Stage 7 wraps it in a <c>PaletteAsset</c> for user-authored ramps;
    /// this type is what the asset will hold.
    /// </summary>
    public sealed class PaletteData
    {
        public const int Resolution = 256;

        private readonly Color32[] colors;
        private readonly ColorStop[] stops;

        public PaletteData(string id, string displayName, Color32[] colors)
            : this(id, displayName, colors, Array.Empty<ColorStop>())
        {
        }

        private PaletteData(string id, string displayName, Color32[] colors, ColorStop[] stops, bool bands = false)
        {
            Bands = bands;
            Id = id ?? string.Empty;
            DisplayName = displayName ?? Id;
            this.colors = colors is { Length: > 0 } ? colors : new[] { new Color32(255, 255, 255, 255) };
            this.stops = stops ?? Array.Empty<ColorStop>();
        }

        /// <summary>Stable key, used by saved state. Never localise it.</summary>
        public string Id { get; }

        public string DisplayName { get; }

        /// <summary>
        /// The gradient stops the palette was baked from, empty for one built from raw colours.
        /// Kept because a palette is edited and saved as its stops, never as 256 baked colours.
        /// </summary>
        public IReadOnlyList<ColorStop> Stops => stops;

        /// <summary>
        /// Each stop's colour holds until the next stop instead of blending into it: hard bands rather
        /// than a ramp. Baked into the colours like everything else, so neither renderer knows about it.
        /// </summary>
        public bool Bands { get; }

        public int Count => colors.Length;

        public Color32 this[int index] => colors[(int)((uint)index % (uint)colors.Length)];

        /// <summary>The baked stops, for building a GPU palette texture.</summary>
        public ReadOnlySpan<Color32> Colors => colors;

        /// <summary>Sample at <paramref name="t"/> in 0..1, wrapping, linear between stops.</summary>
        public Color32 Sample(float t)
        {
            var wrapped = t - Mathf.Floor(t);
            var scaled = wrapped * colors.Length;
            var first = (int)scaled;
            if (first >= colors.Length)
            {
                first = colors.Length - 1;
            }

            var second = first + 1;
            if (second >= colors.Length)
            {
                second = 0;
            }

            return Lerp(colors[first], colors[second], scaled - first);
        }

        /// <summary>
        /// Bake a palette from gradient stops. Positions are 0..1 and must ascend; the ramp wraps
        /// from the last stop to the first, so give both ends the same colour unless a seam is
        /// wanted.
        /// </summary>
        public static PaletteData FromStops(string id, string displayName, params ColorStop[] stops) =>
            FromStops(id, displayName, false, stops);

        /// <param name="bands">See <see cref="Bands"/>.</param>
        public static PaletteData FromStops(string id, string displayName, bool bands, params ColorStop[] stops)
        {
            var colors = new Color32[Resolution];
            if (stops == null || stops.Length == 0)
            {
                for (var i = 0; i < Resolution; i++)
                {
                    colors[i] = new Color32(255, 255, 255, 255);
                }

                return new PaletteData(id, displayName, colors);
            }

            var sorted = (ColorStop[])stops.Clone();
            Array.Sort(sorted, (a, b) => a.Position.CompareTo(b.Position));

            for (var i = 0; i < Resolution; i++)
            {
                colors[i] = EvaluateStops(sorted, i / (float)Resolution, bands);
            }

            return new PaletteData(id, displayName, colors, sorted, bands);
        }

        private static Color32 EvaluateStops(ColorStop[] stops, float t, bool bands)
        {
            if (t <= stops[0].Position)
            {
                return stops[0].Color;
            }

            for (var i = 1; i < stops.Length; i++)
            {
                if (t > stops[i].Position)
                {
                    continue;
                }

                if (bands)
                {
                    return t < stops[i].Position ? stops[i - 1].Color : stops[i].Color;
                }

                var span = Mathf.Max(1e-5f, stops[i].Position - stops[i - 1].Position);
                return Lerp(stops[i - 1].Color, stops[i].Color, (t - stops[i - 1].Position) / span);
            }

            return stops[stops.Length - 1].Color;
        }

        private static Color32 Lerp(Color32 a, Color32 b, float t)
        {
            var clamped = Mathf.Clamp01(t);
            return new Color32(
                (byte)(a.r + (b.r - a.r) * clamped + 0.5f),
                (byte)(a.g + (b.g - a.g) * clamped + 0.5f),
                (byte)(a.b + (b.b - a.b) * clamped + 0.5f),
                255);
        }

        public readonly struct ColorStop
        {
            public ColorStop(float position, float r, float g, float b)
            {
                Position = Mathf.Clamp01(position);
                Color = new Color32(
                    (byte)Mathf.Clamp(Mathf.RoundToInt(r * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(g * 255f), 0, 255),
                    (byte)Mathf.Clamp(Mathf.RoundToInt(b * 255f), 0, 255),
                    255);
            }

            public ColorStop(float position, Color32 color)
            {
                Position = Mathf.Clamp01(position);
                Color = new Color32(color.r, color.g, color.b, 255);
            }

            public float Position { get; }
            public Color32 Color { get; }
        }
    }
}
