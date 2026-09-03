using System.Collections.Generic;
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
                new Stop(1f, 0.95f, 0.15f, 0.2f))
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
    }
}
