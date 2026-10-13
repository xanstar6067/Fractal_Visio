using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// Every fractal the app knows, and where the gallery files it. Adding one is a line here plus
    /// its three files - or, for a member of the folded polynomial family, a line in
    /// <see cref="FoldedFamily"/>; a 3D scene is a line here plus its definition and shader. Its section and folder decide the group it appears under, and the
    /// order of the lines is the order of the gallery - sections and folders included, in order of
    /// first appearance.
    ///
    /// Section and folder names and fractal descriptions are not here: they are user-visible text
    /// and live in the locale files as <c>section.&lt;id&gt;</c>, <c>folder.&lt;id&gt;</c> and
    /// <c>fractal.&lt;id&gt;.about</c>.
    /// </summary>
    public static class FractalCatalog
    {
        /// <summary>Section ids. Stable: favourites and filters do not store them, but locale keys do.</summary>
        public static class Sections
        {
            public const string MandelbrotFamily = "mandelbrot-family";
            public const string JuliaFamily = "julia-family";
            public const string Scenes = "fractals-3d";
        }

        /// <summary>The folded family's own folders, after the classics. Declared before
        /// <see cref="Entries"/>: static fields initialise in the order they are written.</summary>
        private static readonly string[] FoldedFolders =
        {
            FoldedFamily.Folders.Perpendicular,
            FoldedFamily.Folders.Cubic,
            FoldedFamily.Folders.Quartic,
            FoldedFamily.Folders.Quintic
        };

        private static readonly CatalogEntry[] Entries = BuildEntries();

        private static readonly IFractalDefinition[] Definitions = BuildDefinitions();

        private static readonly ISceneDefinition[] SceneDefinitions = BuildScenes();

        /// <summary>The plane fractals, in gallery order.</summary>
        public static IReadOnlyList<IFractalDefinition> All => Definitions;

        /// <summary>The 3D scenes, in gallery order.</summary>
        public static IReadOnlyList<ISceneDefinition> Scenes => SceneDefinitions;

        /// <summary>The gallery: every definition with its section and folder, in display order.</summary>
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

        private static CatalogEntry[] BuildEntries()
        {
            var entries = new List<CatalogEntry>();
            var classic = FoldedFamily.Folders.Classic;

            // The Mandelbrot family: the parameter planes.
            void Plane(IFractalDefinition definition, string folder) =>
                entries.Add(new CatalogEntry(definition, Sections.MandelbrotFamily, folder));

            Plane(new MandelbrotDefinition(), classic);
            Plane(new BurningShipDefinition(), classic);
            Plane(new TricornDefinition(), classic);
            Plane(new CelticDefinition(), classic);
            Plane(new MultibrotDefinition(), classic);
            Plane(new SimonobrotDefinition(), classic);
            Plane(new HybridDefinition(), classic);
            foreach (var folder in FoldedFolders)
            {
                foreach (var member in FoldedFamily.In(folder))
                {
                    Plane(member.CreatePlane(), folder);
                }
            }

            // The Julia family: the same maps from every point, in the same order.
            void Julia(IFractalDefinition definition, string folder) =>
                entries.Add(new CatalogEntry(definition, Sections.JuliaFamily, folder));

            Julia(new JuliaDefinition(), classic);
            Julia(new JuliaBurningShipDefinition(), classic);
            Julia(FoldedFamily.JuliaTricorn(), classic);
            Julia(FoldedFamily.JuliaCeltic(), classic);
            Julia(new MultijuliaDefinition(), classic);
            Julia(new JuliaSimonobrotDefinition(), classic);
            Julia(new HybridJuliaDefinition(), classic);
            foreach (var folder in FoldedFolders)
            {
                foreach (var member in FoldedFamily.In(folder))
                {
                    Julia(member.CreateJulia(), folder);
                }
            }

            // Three dimensions: ray marched on the GPU, turned with a finger.
            void Scene(ISceneDefinition scene) => entries.Add(new CatalogEntry(scene, Sections.Scenes));

            Scene(new MandelbulbDefinition());

            return entries.ToArray();
        }

        private static IFractalDefinition[] BuildDefinitions()
        {
            var definitions = new List<IFractalDefinition>();
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Definition != null)
                {
                    definitions.Add(Entries[i].Definition);
                }
            }

            return definitions.ToArray();
        }

        private static ISceneDefinition[] BuildScenes()
        {
            var scenes = new List<ISceneDefinition>();
            for (var i = 0; i < Entries.Length; i++)
            {
                if (Entries[i].Scene != null)
                {
                    scenes.Add(Entries[i].Scene);
                }
            }

            return scenes.ToArray();
        }
    }
}
