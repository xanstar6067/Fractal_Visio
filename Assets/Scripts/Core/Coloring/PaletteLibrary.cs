using System.Collections.Generic;
using UnityEngine;
using Stop = FractalVisio.Core.PaletteData.ColorStop;

namespace FractalVisio.Core
{
    /// <summary>
    /// The palettes the app ships with, in code for the same reason the fractal catalog is: assets
    /// come with the editor tooling for them (stage 7), and until then a list here is the whole
    /// registry. Adding one is a single entry.
    ///
    /// Every ramp ends on the colour it starts with - see <see cref="PaletteData"/> for why.
    /// </summary>
    public static class PaletteLibrary
    {
        private static readonly PaletteData[] Palettes =
        {
            PaletteData.FromStops(
                "aurora", "Aurora",
                new Stop(0f, 0.015f, 0.025f, 0.12f),
                new Stop(0.2f, 0.04f, 0.42f, 0.95f),
                new Stop(0.4f, 0.15f, 0.95f, 0.85f),
                new Stop(0.6f, 1f, 0.78f, 0.12f),
                new Stop(0.8f, 0.9f, 0.08f, 0.24f),
                new Stop(1f, 0.015f, 0.025f, 0.12f)),

            PaletteData.FromStops(
                "ember", "Ember",
                new Stop(0f, 0.02f, 0.01f, 0.02f),
                new Stop(0.25f, 0.55f, 0.06f, 0.02f),
                new Stop(0.5f, 1f, 0.45f, 0.05f),
                new Stop(0.72f, 1f, 0.9f, 0.55f),
                new Stop(1f, 0.02f, 0.01f, 0.02f)),

            PaletteData.FromStops(
                "ice", "Ice",
                new Stop(0f, 0.01f, 0.03f, 0.08f),
                new Stop(0.3f, 0.15f, 0.4f, 0.7f),
                new Stop(0.55f, 0.55f, 0.85f, 1f),
                new Stop(0.78f, 0.97f, 0.99f, 1f),
                new Stop(1f, 0.01f, 0.03f, 0.08f)),

            PaletteData.FromStops(
                "mono", "Mono",
                new Stop(0f, 0.02f, 0.02f, 0.03f),
                new Stop(0.5f, 0.97f, 0.97f, 0.98f),
                new Stop(1f, 0.02f, 0.02f, 0.03f)),

            PaletteData.FromStops(
                "spectrum", "Spectrum",
                new Stop(0f, 0.95f, 0.15f, 0.2f),
                new Stop(0.17f, 0.95f, 0.75f, 0.15f),
                new Stop(0.34f, 0.35f, 0.9f, 0.2f),
                new Stop(0.5f, 0.15f, 0.9f, 0.85f),
                new Stop(0.67f, 0.2f, 0.4f, 0.95f),
                new Stop(0.84f, 0.75f, 0.25f, 0.9f),
                new Stop(1f, 0.95f, 0.15f, 0.2f)),

            // From the WPF version. Its ramps run dark to light and stop there; here the ring has
            // to close, so each goes out and back (see Mirrored) instead of jumping from white to
            // black once per cycle. Its "Psychedelic" was unblended in WPF too, hence bands.
            Mirrored("fire", Rgb(0, 0, 0), Rgb(139, 0, 0), Rgb(255, 0, 0), Rgb(255, 165, 0), Rgb(255, 255, 0), Rgb(255, 255, 255)),
            Mirrored("glacier", Rgb(0, 0, 0), Rgb(0, 0, 139), Rgb(0, 0, 255), Rgb(0, 255, 255), Rgb(255, 255, 255)),
            Mirrored("fire-ice", Rgb(0, 0, 0), Rgb(0, 0, 139), Rgb(0, 255, 255), Rgb(255, 255, 255), Rgb(255, 255, 0), Rgb(255, 0, 0), Rgb(139, 0, 0)),
            Mirrored("ultraviolet", Rgb(0, 0, 0), Rgb(148, 0, 211), Rgb(238, 130, 238), Rgb(255, 255, 255)),
            Ring("psychedelic", true, Rgb(255, 0, 0), Rgb(255, 255, 0), Rgb(0, 255, 0), Rgb(0, 255, 255), Rgb(0, 0, 255), Rgb(255, 0, 255)),
            Mirrored("sunset", Rgb(0, 0, 0), Rgb(25, 25, 112), Rgb(75, 0, 130), Rgb(139, 0, 139), Rgb(220, 20, 60), Rgb(255, 140, 0), Rgb(255, 215, 0), Rgb(255, 255, 255)),
            Mirrored("ocean", Rgb(0, 0, 0), Rgb(0, 20, 40), Rgb(0, 50, 80), Rgb(0, 100, 150), Rgb(0, 150, 200), Rgb(100, 200, 255), Rgb(200, 240, 255), Rgb(255, 255, 255)),
            Mirrored("gold", Rgb(0, 0, 0), Rgb(85, 65, 0), Rgb(139, 115, 0), Rgb(205, 173, 0), Rgb(255, 215, 0), Rgb(255, 235, 128), Rgb(255, 248, 220), Rgb(255, 255, 255)),
            Mirrored("copper", Rgb(0, 0, 0), Rgb(72, 61, 20), Rgb(138, 54, 15), Rgb(184, 115, 51), Rgb(205, 127, 50), Rgb(240, 147, 43), Rgb(255, 200, 124), Rgb(255, 255, 255)),
            Mirrored("lava", Rgb(0, 0, 0), Rgb(139, 0, 0), Rgb(205, 0, 0), Rgb(255, 69, 0), Rgb(255, 140, 0), Rgb(255, 215, 0), Rgb(255, 255, 224), Rgb(255, 255, 255)),
            Mirrored("neon", Rgb(0, 0, 0), Rgb(75, 0, 75), Rgb(255, 0, 255), Rgb(0, 255, 255), Rgb(0, 255, 0), Rgb(255, 255, 0), Rgb(255, 100, 255), Rgb(255, 255, 255)),
            Mirrored("rainbow", Rgb(0, 0, 0), Rgb(148, 0, 211), Rgb(75, 0, 130), Rgb(0, 0, 255), Rgb(0, 255, 0), Rgb(255, 255, 0), Rgb(255, 127, 0), Rgb(255, 0, 0), Rgb(255, 255, 255)),
            Mirrored("amethyst", Rgb(0, 0, 0), Rgb(25, 25, 112), Rgb(72, 61, 139), Rgb(123, 104, 238), Rgb(147, 112, 219), Rgb(221, 160, 221), Rgb(238, 203, 238), Rgb(255, 255, 255)),
            Mirrored("cosmos", Rgb(0, 0, 0), Rgb(25, 25, 112), Rgb(72, 61, 139), Rgb(138, 43, 226), Rgb(255, 20, 147), Rgb(255, 105, 180), Rgb(255, 182, 193), Rgb(255, 255, 255)),
            Mirrored("forest", Rgb(0, 0, 0), Rgb(0, 39, 0), Rgb(0, 69, 0), Rgb(34, 139, 34), Rgb(50, 205, 50), Rgb(124, 252, 0), Rgb(173, 255, 47), Rgb(255, 255, 255)),
            Mirrored("turquoise", Rgb(0, 0, 0), Rgb(0, 100, 100), Rgb(0, 139, 139), Rgb(72, 209, 204), Rgb(175, 238, 238), Rgb(224, 255, 255), Rgb(255, 255, 255)),
            Mirrored("sepia", Rgb(20, 10, 0), Rgb(255, 240, 192))
        };

        public static IReadOnlyList<PaletteData> All => Palettes;

        public static PaletteData Default => Palettes[0];

        public static PaletteData Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < Palettes.Length; i++)
            {
                if (Palettes[i].Id == id)
                {
                    return Palettes[i];
                }
            }

            return null;
        }

        public static int IndexOf(PaletteData palette)
        {
            for (var i = 0; i < Palettes.Length; i++)
            {
                if (ReferenceEquals(Palettes[i], palette))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// A ramp that runs through <paramref name="colors"/> and back again, so the ring closes on
        /// the colour it starts with without a jump. Named by its id through the locales.
        /// </summary>
        private static PaletteData Mirrored(string id, params Color32[] colors)
        {
            var last = colors.Length - 1;
            var stops = new Stop[last * 2 + 1];
            for (var i = 0; i <= last; i++)
            {
                stops[i] = new Stop(0.5f * i / last, colors[i]);
                stops[last * 2 - i] = new Stop(1f - 0.5f * i / last, colors[i]);
            }

            return PaletteData.FromStops(id, id, stops);
        }

        /// <summary>Colours evenly round the ring, closing on the first.</summary>
        private static PaletteData Ring(string id, bool bands, params Color32[] colors)
        {
            var stops = new Stop[colors.Length + 1];
            for (var i = 0; i < colors.Length; i++)
            {
                stops[i] = new Stop(i / (float)colors.Length, colors[i]);
            }

            stops[colors.Length] = new Stop(1f, colors[0]);
            return PaletteData.FromStops(id, id, bands, stops);
        }

        private static Color32 Rgb(byte r, byte g, byte b) => new(r, g, b, 255);
    }
}
