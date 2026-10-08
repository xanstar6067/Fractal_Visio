using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The folded polynomial family as data: every member the WPF version has (its
    /// <c>FoldedFormulaCatalog</c>, the perpendicular four, Celtic Mandelbar and the two cubic
    /// Kalles Fraktaler ones), each a parameter plane and a Julia set. Names are the formulas'
    /// own - the Kalles Fraktaler names, kept in English in WPF's Russian interface too - and are the
    /// fallback when a locale has no <c>fractal.&lt;id&gt;</c>. Default views, Julia constants and
    /// places are WPF's.
    ///
    /// <see cref="FractalCatalog"/> files the members by <see cref="Member.Folder"/>; adding one is a
    /// line here.
    /// </summary>
    public static class FoldedFamily
    {
        /// <summary>Gallery folders the members are filed under (<c>folder.&lt;id&gt;</c> in the locales).</summary>
        public static class Folders
        {
            public const string Classic = "classic";
            public const string Perpendicular = "perpendicular";
            public const string Cubic = "cubic";
            public const string Quartic = "quartic";
            public const string Quintic = "quintic";
        }

        public sealed class Member
        {
            public Member(
                string id, string name, string folder, in FoldedFormula formula, in ViewState view,
                PlanePreset[] juliaPresets, PlaceOfInterest[] places)
            {
                Id = id;
                Name = name;
                Folder = folder;
                Formula = formula;
                View = view;
                JuliaPresets = juliaPresets;
                Places = places ?? Array.Empty<PlaceOfInterest>();
            }

            /// <summary>Id of the parameter plane; the Julia set's is "julia-" before it.</summary>
            public string Id { get; }

            public string Name { get; }

            public string Folder { get; }

            public FoldedFormula Formula { get; }

            /// <summary>The parameter plane's default view.</summary>
            public ViewState View { get; }

            /// <summary>The Julia set's constants: the first is its default.</summary>
            public PlanePreset[] JuliaPresets { get; }

            public PlaceOfInterest[] Places { get; }

            public string JuliaId => "julia-" + Id;

            public FoldedDefinition CreatePlane() => new(Id, Name, Formula, View, Places);

            public FoldedJuliaDefinition CreateJulia() => JuliaOf(JuliaId, Name + " Julia", Formula, Id, View, JuliaPresets);
        }

        /// <summary>
        /// The Julia sets of the two hand-written members, whose parameter planes keep their own
        /// samplers and shaders (<see cref="TricornDefinition"/>, <see cref="CelticDefinition"/>):
        /// the same maps, drawn here by the folded engine.
        /// </summary>
        public static FoldedJuliaDefinition JuliaTricorn() => JuliaOf(
            "julia-tricorn", "Tricorn Julia", FoldedFormula.Of(2, Folds.None, negate: true), "tricorn",
            View(-0.5m, 0m, 3.8m),
            // WPF's two constants lie well outside the Tricorn - sparse dust, a poor first picture.
            // The first is just outside its edge instead, where the dust is dense lace.
            new PlanePreset("lace-cloud", 0.29909d, 0.40186d),
            new PlanePreset("conjugate-lace", -0.1d, 0.65d),
            new PlanePreset("triple-branches", -0.4d, 0.2d));

        public static FoldedJuliaDefinition JuliaCeltic() => JuliaOf(
            "julia-celtic", "Celtic Julia", FoldedFormula.Of(2, Folds.A), "celtic",
            View(-0.5m, 0m, 3m),
            new PlanePreset("celtic-loops", -0.75d, 0.12d),
            new PlanePreset("celtic-antenna", -1.2d, 0.05d));

        public static IReadOnlyList<Member> Members { get; } = new[]
        {
            // ---- Degree 2. WPF's classic Buffalo, (|x| + i|y|)^2 + c, is not here: it is the Burning
            // Ship upside down (docs/ROADMAP-WPF.md, block 2), and its Julia sets the ship's mirrored.
            Plane("perpendicular-mandelbrot", "Perpendicular Mandelbrot", Folders.Perpendicular, 2, Folds.X,
                View(-0.4m, 0m, 3m), Julia(new("reflected-lace", -0.4d, 0.2d), new("branched-form", -1.2d, 0.05d)),
                BorderCloseup(), negate: true),
            Plane("perpendicular-burning-ship", "Perpendicular Burning Ship", Folders.Perpendicular, 2, Folds.Y,
                View(-0.4m, 0m, 3m),
                Julia(new("ruby-lace", -0.49893d, 0.07073d), new("reflected-lace", -0.4d, 0.3d), new("branched-form", -0.6d, 0.5d)),
                BorderCloseup(), negate: true),
            Plane("perpendicular-celtic", "Perpendicular Celtic", Folders.Perpendicular, 2, Folds.A | Folds.X,
                View(-0.4m, 0m, 3m), Julia(new("reflected-lace", -0.6d, 0.2d), new("branched-form", -1.2d, 0.05d)),
                BorderCloseup(), negate: true),
            Plane("perpendicular-buffalo", "Perpendicular Buffalo", Folders.Perpendicular, 2, Folds.A | Folds.Y,
                View(-0.4m, 0m, 3m), Julia(new("reflected-lace", -0.6d, 0.3d), new("branched-form", -0.8d, 0.4d)),
                BorderCloseup(), negate: true),
            Plane("celtic-mandelbar", "Celtic Mandelbar", Folders.Perpendicular, 2, Folds.A,
                View(-0.4m, 0m, 3m), Julia(new("celtic-reflected-lace", -0.75d, 0.12d), new("celtic-branches", -1.2d, 0.05d)),
                BorderCloseup(), negate: true),

            // ---- Degree 3.
            Plane("cubic-quasi-burning-ship", "Cubic Quasi Burning Ship", Folders.Cubic, 3, Folds.X | Folds.Y | Folds.C,
                View(0m, 0.25m, 3m), Julia(new("cubic-ship-lace", -0.1d, 0.85d), new("connected-cubic", -0.2d, 0.6d)),
                new[] { WpfPlace.At("cubic-folds", 0m, 1.1m, 4m) }, negate: true),
            Plane("cubic-flying-squirrel", "Cubic Flying Squirrel", Folders.Cubic, 3, Folds.Y | Folds.C,
                View(0m, -0.25m, 3m), Julia(new("cubic-wings", -0.1d, -0.85d), new("connected-wings", -0.2d, -0.6d)),
                new[] { WpfPlace.At("wings-closeup", 0m, -1.1m, 4m) }),
            Kf("cubic-burning-ship", "Cubic Burning Ship", 3, Folds.X | Folds.Y, -0.425d, -0.5525d, 0.5525d, 0.425d),
            Kf("cubic-buffalo", "Cubic Buffalo", 3, Folds.X | Folds.Y | Folds.A | Folds.C, -0.2975d, -1.275d, -1.275d, -0.2975d),
            Kf("cubic-celtic", "Cubic Celtic", 3, Folds.X | Folds.A, -0.595d, 0.6375d, -0.595d, -0.6375d),
            Kf("cubic-mandelbar", "Cubic Mandelbar", 3, Folds.None, -0.5593d, -0.7191d, -0.25d, -0.35d, negate: true),
            Kf("cubic-partial-burning-ship-real", "Cubic Partial Burning Ship Real", 3, Folds.X, 0.5525d, -0.2975d, 0.5525d, 0.2975d),
            Kf("cubic-partial-burning-ship-imag", "Cubic Partial Burning Ship Imag", 3, Folds.Y, -0.4675d, 0.0425d, 0.4675d, 0.0425d),
            Kf("cubic-quasi-perpendicular", "Cubic Quasi Perpendicular", 3, Folds.X | Folds.C, 0.68d, 0.8925d, 0.68d, -0.8925d, negate: true),
            Kf("cubic-celtic-quasi-perpendicular", "Cubic Celtic Quasi Perpendicular", 3, Folds.X | Folds.A | Folds.C,
                -0.8075d, 0.85d, 0.0425d, 0.85d, negate: true),
            Kf("cubic-quasi-perpendicular-burning-ship", "Cubic Quasi Perpendicular Burning Ship", 3, Folds.Y | Folds.A,
                -0.51d, 0.2975d, -0.9775d, -0.2125d, negate: true, swap: true),
            Kf("cubic-quasi-perpendicular-buffalo", "Cubic Quasi Perpendicular Buffalo", 3, Folds.Y | Folds.A | Folds.C,
                -0.255d, 0.68d, -0.5525d, 0.2975d, negate: true, swap: true),

            // ---- Degree 4.
            Kf("quartic-burning-ship", "Quartic Burning Ship", 4, Folds.X | Folds.Y, -0.7225d, -0.425d, 0.5525d, -0.2975d),
            Kf("quartic-buffalo", "Quartic Buffalo", 4, Folds.X | Folds.Y | Folds.A | Folds.C, -0.595d, -1.105d, -1.02d, -0.595d),
            Kf("quartic-celtic", "Quartic Celtic", 4, Folds.A, -0.935d, -0.1275d, -0.935d, -0.1275d),
            Kf("quartic-mandelbar", "Quartic Mandelbar", 4, Folds.None, -0.255d, -0.5525d, -0.255d, 0.5525d, negate: true),
            Kf("quartic-partial-burning-ship-imag", "Quartic Partial Burning Ship Imag", 4, Folds.Y, -0.255d, -0.5525d, 0.5525d, -0.2975d),
            Kf("quartic-partial-burning-ship-real", "Quartic Partial Burning Ship Real", 4, Folds.X, 0.6375d, -0.2975d, 0.6375d, 0.2975d),
            Kf("quartic-partial-burning-ship-real-mandelbar", "Quartic Partial Burning Ship Real Mandelbar", 4, Folds.X,
                -0.4395d, -0.4794d, -0.25d, -0.35d, negate: true),
            Kf("quartic-celtic-partial-burning-ship-imag", "Quartic Celtic Partial Burning Ship Imag", 4, Folds.A | Folds.Y,
                -0.935d, -0.085d, -0.255d, 0.5525d),
            Kf("quartic-celtic-partial-burning-ship-real", "Quartic Celtic Partial Burning Ship Real", 4, Folds.A | Folds.X,
                -0.935d, 0.085d, -0.6375d, -0.935d),
            Kf("quartic-celtic-partial-burning-ship-real-mandelbar", "Quartic Celtic Partial Burning Ship Real Mandelbar", 4,
                Folds.A | Folds.X, 0.5525d, -0.3825d, 0.5525d, -0.3825d, negate: true),
            Kf("quartic-buffalo-partial-imag", "Quartic Buffalo Partial Imag", 4, Folds.X | Folds.Y | Folds.C, 0.595d, -0.765d, -0.17d, -0.9775d),
            Kf("quartic-celtic-mandelbar", "Quartic Celtic Mandelbar", 4, Folds.A, -0.935d, 0.085d, -0.85d, -0.17d, negate: true),
            Kf("quartic-false-quasi-perpendicular", "Quartic False Quasi Perpendicular", 4, Folds.C, 0.6375d, 0.6375d, 0.6375d, -0.6375d, negate: true),
            Kf("quartic-false-quasi-heart", "Quartic False Quasi Heart", 4, Folds.C, -0.3825d, -0.8925d, -0.3825d, 0.8925d),
            Kf("quartic-celtic-false-quasi-perpendicular", "Quartic Celtic False Quasi Perpendicular", 4, Folds.A | Folds.C,
                -0.8075d, -0.255d, -0.8075d, -0.255d, negate: true),
            Kf("quartic-celtic-false-quasi-heart", "Quartic Celtic False Quasi Heart", 4, Folds.A | Folds.C, -0.765d, 0.6375d, -0.765d, -0.6375d),
            Kf("quartic-imag-quasi", "Quartic Imag Quasi", 4, Folds.Y | Folds.C, 0.6375d, -0.6375d, -0.255d, 0.6375d),
            Kf("quartic-real-quasi-perpendicular", "Quartic Real Quasi Perpendicular", 4, Folds.X | Folds.C, 0.34d, -0.8075d, 0.34d, 0.8075d, negate: true),
            Kf("quartic-real-quasi-heart", "Quartic Real Quasi Heart", 4, Folds.X | Folds.C, -0.6375d, -0.085d, -0.255d, -0.425d),
            Kf("quartic-celtic-imag-quasi", "Quartic Celtic Imag Quasi", 4, Folds.A | Folds.Y | Folds.C, -0.765d, 0.6375d, -0.8075d, -0.255d),
            Kf("quartic-celtic-real-quasi-perpendicular", "Quartic Celtic Real Quasi Perpendicular", 4, Folds.A | Folds.X | Folds.C,
                -0.425d, 0.9775d, -0.425d, -0.9775d, negate: true),
            Kf("quartic-celtic-real-quasi-heart", "Quartic Celtic Real Quasi Heart", 4, Folds.A | Folds.X | Folds.C, -0.7225d, 0.34d, -0.7225d, -0.34d),

            // ---- Degree 5.
            Kf("quintic-burning-ship", "Quintic Burning Ship", 5, Folds.X | Folds.Y, 0.6375d, -0.2975d, -0.2975d, 0.6375d),
            Kf("quintic-buffalo", "Quintic Buffalo", 5, Folds.X | Folds.Y | Folds.A | Folds.C, -0.425d, -0.9775d, -0.9775d, -0.425d),
            Kf("quintic-celtic", "Quintic Celtic", 5, Folds.X | Folds.A, -0.255d, -0.595d, -0.2975d, 0.6375d),
            Kf("quintic-mandelbar", "Quintic Mandelbar", 5, Folds.None, -0.5525d, -0.3825d, 0.5525d, -0.3825d, negate: true),
            Kf("quintic-partial-burning-ship-real", "Quintic Partial Burning Ship Real", 5, Folds.X, -0.3825d, -0.5525d, -0.3825d, 0.5525d),
            Kf("quintic-partial-burning-ship-real-mandelbar", "Quintic Partial Burning Ship Real Mandelbar", 5, Folds.X,
                0.51d, -0.3825d, 0.51d, 0.3825d, negate: true),
            Kf("quintic-celtic-mandelbar", "Quintic Celtic Mandelbar", 5, Folds.X | Folds.A, -0.51d, -0.3825d, -0.51d, 0.3825d, negate: true),
            Kf("quintic-quasi-burning-ship", "Quintic Quasi Burning Ship", 5, Folds.X | Folds.Y | Folds.C, -0.3825d, 0.51d, -0.51d, 0.765d, negate: true),
            Kf("quintic-quasi-perpendicular", "Quintic Quasi Perpendicular", 5, Folds.X | Folds.C, -0.3825d, -0.51d, -0.3825d, 0.51d, negate: true),
            Kf("quintic-quasi-heart", "Quintic Quasi Heart", 5, Folds.X | Folds.C, 0.68d, -0.425d, 0.68d, 0.425d),
            Kf("quintic-quasi-perpendicular-burning-ship", "Quintic Quasi Perpendicular Burning Ship", 5, Folds.Y | Folds.A,
                -0.425d, -0.68d, -0.8075d, -0.595d, negate: true, swap: true),
            Kf("quintic-quasi-perpendicular-buffalo", "Quintic Quasi Perpendicular Buffalo", 5, Folds.X | Folds.Y | Folds.A | Folds.C,
                0.085d, 0.8925d, -0.8925d, -0.085d, negate: true, swap: true),
            Kf("quintic-celtic-quasi-perpendicular", "Quintic Celtic Quasi Perpendicular", 5, Folds.X | Folds.A | Folds.C,
                -0.2125d, 1.02d, 0.51d, -0.425d, negate: true),
            Kf("quintic-celtic-quasi-heart", "Quintic Celtic Quasi Heart", 5, Folds.X | Folds.A | Folds.C, -0.765d, -0.2975d, -0.51d, -0.3825d)
        };

        /// <summary>The members filed under <paramref name="folder"/>, in catalog order.</summary>
        public static IEnumerable<Member> In(string folder)
        {
            for (var i = 0; i < Members.Count; i++)
            {
                if (Members[i].Folder == folder)
                {
                    yield return Members[i];
                }
            }
        }

        private static Member Plane(
            string id, string name, string folder, int degree, Folds folds, in ViewState view,
            PlanePreset[] juliaPresets, PlaceOfInterest[] places, bool negate = false, bool swap = false)
        {
            return new Member(id, name, folder, FoldedFormula.Of(degree, folds, negate, swap), view, juliaPresets, places);
        }

        /// <summary>
        /// One of the WPF <c>FoldedFormulaCatalog</c> formulas: the whole set centred, 3 high, and its
        /// Julia set's default and alternative C - WPF's two presets.
        /// </summary>
        private static Member Kf(
            string id, string name, int degree, Folds folds, double juliaReal, double juliaImaginary,
            double alternativeReal, double alternativeImaginary, bool negate = false, bool swap = false)
        {
            var folder = degree switch
            {
                3 => Folders.Cubic,
                4 => Folders.Quartic,
                _ => Folders.Quintic
            };

            var presets = juliaReal == alternativeReal && juliaImaginary == alternativeImaginary
                ? Julia(new PlanePreset("default", juliaReal, juliaImaginary))
                : Julia(
                    new PlanePreset("default", juliaReal, juliaImaginary),
                    new PlanePreset("alternative", alternativeReal, alternativeImaginary));
            return Plane(id, name, folder, degree, folds, View(0m, 0m, 3m), presets, Array.Empty<PlaceOfInterest>(), negate, swap);
        }

        private static PlanePreset[] Julia(params PlanePreset[] presets) => presets;

        private static PlaceOfInterest[] BorderCloseup() => new[] { WpfPlace.At("border-closeup", -1.4m, 0.04m, 12m) };

        private static ViewState View(decimal x, decimal y, decimal height) => new()
        {
            x = x,
            y = y,
            scale = height,
            rotation = 0d,
            iterations = 128
        };

        /// <summary>
        /// A Julia set of <paramref name="formula"/> whose C is picked on <paramref name="planeId"/>.
        /// The map starts on the plane's default view, as wide as WPF's 4:3 canvas.
        /// </summary>
        private static FoldedJuliaDefinition JuliaOf(
            string id, string name, in FoldedFormula formula, string planeId, in ViewState planeView, params PlanePreset[] presets)
        {
            var height = planeView.scale.AsDouble;
            var centerX = planeView.x.AsDouble;
            var centerY = planeView.y.AsDouble;
            var bounds = new PlaneRect(
                centerX - height * 2d / 3d, centerX + height * 2d / 3d, centerY - height * 0.5d, centerY + height * 0.5d);
            return new FoldedJuliaDefinition(
                id, name, formula, planeId, bounds, presets[0].Real, presets[0].Imaginary, presets);
        }
    }
}
