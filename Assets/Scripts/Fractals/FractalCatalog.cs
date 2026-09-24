using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// Every fractal the app knows, and where the gallery files it. Adding one is a line here plus
    /// its three files; its section decides the group it appears under, and the order of the lines
    /// is the order of the gallery - sections included, in order of first appearance.
    ///
    /// Section names and fractal descriptions are not here: they are user-visible text and live in
    /// the locale files as <c>section.&lt;id&gt;</c> and <c>fractal.&lt;id&gt;.about</c>.
    /// </summary>
    public static class FractalCatalog
    {
        /// <summary>Section ids. Stable: favourites and filters do not store them, but locale keys do.</summary>
        public static class Sections
        {
            public const string MandelbrotFamily = "mandelbrot-family";
            public const string JuliaFamily = "julia-family";
        }

        private static readonly CatalogEntry[] Entries =
        {
            new(new MandelbrotDefinition(), Sections.MandelbrotFamily),
            new(new BurningShipDefinition(), Sections.MandelbrotFamily),
            new(new TricornDefinition(), Sections.MandelbrotFamily),
            new(new CelticDefinition(), Sections.MandelbrotFamily),
            new(new MultibrotDefinition(), Sections.MandelbrotFamily),
            new(new JuliaDefinition(), Sections.JuliaFamily),
            new(new JuliaBurningShipDefinition(), Sections.JuliaFamily)
        };

        private static readonly IFractalDefinition[] Definitions = BuildDefinitions();

        public static IReadOnlyList<IFractalDefinition> All => Definitions;

        /// <summary>The gallery: every definition with its section, in display order.</summary>
        public static IReadOnlyList<CatalogEntry> Gallery => Entries;

        public static IFractalDefinition Default => Definitions[0];

        public static IFractalDefinition Find(string id)
        {
            for (var i = 0; i < Definitions.Length; i++)
            {
                if (string.Equals(Definitions[i].Id, id, StringComparison.Ordinal))
                {
                    return Definitions[i];
                }
            }

            return null;
        }

        private static IFractalDefinition[] BuildDefinitions()
        {
            var definitions = new IFractalDefinition[Entries.Length];
            for (var i = 0; i < Entries.Length; i++)
            {
                definitions[i] = Entries[i].Definition;
            }

            return definitions;
        }
    }
}
