using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The formulas a hybrid's step can take - WPF's <c>HybridFormulaSettings.HybridFormulaChoices</c>:
    /// the ten base maps of degree 2, which a row may raise to any power 2 to 5, and the 48 named
    /// formulas of fixed degree. Every one is a <see cref="FoldedFormula"/>, so one step of any hybrid
    /// is a step of the folded engine.
    ///
    /// <b>The order is saved</b>: a row stores its formula as an index into this list. Append, never
    /// insert or reorder.
    /// </summary>
    public static class HybridSteps
    {
        public const int MinimumPower = 2;
        public const int MaximumPower = 5;

        private static readonly Entry[] Table = BuildTable();

        private static readonly StepChoice[] ChoiceList = BuildChoices();

        public static IReadOnlyList<StepChoice> Choices => ChoiceList;

        public static int Count => Table.Length;

        /// <summary>The index of the formula <paramref name="id"/>, or -1.</summary>
        public static int IndexOf(string id)
        {
            for (var i = 0; i < Table.Length; i++)
            {
                if (Table[i].Choice.Id == id)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// The step of formula <paramref name="choice"/> at <paramref name="power"/>; a formula of fixed
        /// degree ignores the power. Out-of-range values are clamped: they come from saved files.
        /// </summary>
        public static FoldedFormula Formula(int choice, int power)
        {
            var entry = Table[Math.Clamp(choice, 0, Table.Length - 1)];
            if (entry.Choice.FixedPower > 0)
            {
                return entry.Fixed;
            }

            power = Math.Clamp(power, MinimumPower, MaximumPower);
            var folds = entry.Folds;

            // "Celtic": the real part by absolute value. For an odd power it is x A, so the fold is of
            // both factors; for an even one it is A alone.
            if (entry.Celtic)
            {
                folds |= (power & 1) == 1 ? Folds.X | Folds.A : Folds.A;
            }

            return FoldedFormula.Of(power, folds, entry.Negate);
        }

        /// <summary>The power a step of <paramref name="choice"/> really has: its own if fixed, else the row's.</summary>
        public static int EffectivePower(int choice, int power)
        {
            var fixedPower = Table[Math.Clamp(choice, 0, Table.Length - 1)].Choice.FixedPower;
            return fixedPower > 0 ? fixedPower : Math.Clamp(power, MinimumPower, MaximumPower);
        }

        private static StepChoice[] BuildChoices()
        {
            var choices = new StepChoice[Table.Length];
            for (var i = 0; i < choices.Length; i++)
            {
                choices[i] = Table[i].Choice;
            }

            return choices;
        }

        private static Entry[] BuildTable()
        {
            var classic = FoldedFamily.Folders.Classic;
            var perpendicular = FoldedFamily.Folders.Perpendicular;
            var entries = new List<Entry>
            {
                // ---- Any power: (fx + i s fy)^p, the real part folded for the Celtic ones. Their
                // degree-2 forms are the classic fractals and the perpendicular members, written the
                // same way in FoldedFamily; the Burning Ship is ours, masts up: (|x| - i|y|)^p.
                Variable("mandelbrot", "Mandelbrot", classic, Folds.None, false, false),
                Variable("burning-ship", "Burning Ship", classic, Folds.X | Folds.Y, true, false),
                Variable("tricorn", "Tricorn", classic, Folds.None, true, false),

                // WPF's classic Buffalo, (|x| + i|y|)^p: a mirror of the ship as a fractal of its own,
                // but not as a step - the next step sees the other half of the plane.
                Variable("buffalo", "Buffalo", classic, Folds.X | Folds.Y, false, false),
                Variable("celtic", "Celtic", classic, Folds.None, false, true),
                Variable("perpendicular-mandelbrot", "Perpendicular Mandelbrot", perpendicular, Folds.X, true, false),
                Variable("perpendicular-burning-ship", "Perpendicular Burning Ship", perpendicular, Folds.Y, true, false),
                Variable("perpendicular-celtic", "Perpendicular Celtic", perpendicular, Folds.X, true, true),
                Variable("perpendicular-buffalo", "Perpendicular Buffalo", perpendicular, Folds.Y, true, true),
                Variable("celtic-mandelbar", "Celtic Mandelbar", perpendicular, Folds.None, true, true)
            };

            // ---- Fixed degree: the named formulas, as their own fractals draw them.
            foreach (var id in FixedIds())
            {
                entries.Add(Fixed(id));
            }

            return entries.ToArray();
        }

        // Append only, like the table: the index is what a saved row holds. A method, not a field: it
        // is read while Table initialises, and static fields initialise in the order they are written.
        private static string[] FixedIds() => new[]
        {
            "cubic-quasi-burning-ship",
            "cubic-flying-squirrel",
            "cubic-burning-ship",
            "cubic-buffalo",
            "cubic-celtic",
            "cubic-mandelbar",
            "cubic-partial-burning-ship-real",
            "cubic-partial-burning-ship-imag",
            "cubic-quasi-perpendicular",
            "cubic-celtic-quasi-perpendicular",
            "cubic-quasi-perpendicular-burning-ship",
            "cubic-quasi-perpendicular-buffalo",
            "quartic-burning-ship",
            "quartic-buffalo",
            "quartic-celtic",
            "quartic-mandelbar",
            "quartic-partial-burning-ship-imag",
            "quartic-partial-burning-ship-real",
            "quartic-partial-burning-ship-real-mandelbar",
            "quartic-celtic-partial-burning-ship-imag",
            "quartic-celtic-partial-burning-ship-real",
            "quartic-celtic-partial-burning-ship-real-mandelbar",
            "quartic-buffalo-partial-imag",
            "quartic-celtic-mandelbar",
            "quartic-false-quasi-perpendicular",
            "quartic-false-quasi-heart",
            "quartic-celtic-false-quasi-perpendicular",
            "quartic-celtic-false-quasi-heart",
            "quartic-imag-quasi",
            "quartic-real-quasi-perpendicular",
            "quartic-real-quasi-heart",
            "quartic-celtic-imag-quasi",
            "quartic-celtic-real-quasi-perpendicular",
            "quartic-celtic-real-quasi-heart",
            "quintic-burning-ship",
            "quintic-buffalo",
            "quintic-celtic",
            "quintic-mandelbar",
            "quintic-partial-burning-ship-real",
            "quintic-partial-burning-ship-real-mandelbar",
            "quintic-celtic-mandelbar",
            "quintic-quasi-burning-ship",
            "quintic-quasi-perpendicular",
            "quintic-quasi-heart",
            "quintic-quasi-perpendicular-burning-ship",
            "quintic-quasi-perpendicular-buffalo",
            "quintic-celtic-quasi-perpendicular",
            "quintic-celtic-quasi-heart"
        };

        private static Entry Variable(string id, string name, string folder, Folds folds, bool negate, bool celtic)
        {
            return new Entry(new StepChoice(id, name, folder, 0), folds, negate, celtic, default);
        }

        private static Entry Fixed(string id)
        {
            for (var i = 0; i < FoldedFamily.Members.Count; i++)
            {
                var member = FoldedFamily.Members[i];
                if (member.Id == id)
                {
                    return new Entry(
                        new StepChoice(id, member.Name, member.Folder, member.Formula.Degree),
                        Folds.None, false, false, member.Formula);
                }
            }

            throw new InvalidOperationException("No folded formula " + id + " for a hybrid step.");
        }

        private readonly struct Entry
        {
            public Entry(in StepChoice choice, Folds folds, bool negate, bool celtic, in FoldedFormula fixedFormula)
            {
                Choice = choice;
                Folds = folds;
                Negate = negate;
                Celtic = celtic;
                Fixed = fixedFormula;
            }

            public StepChoice Choice { get; }

            /// <summary>Folds of x and y before the power, for a formula of any power.</summary>
            public Folds Folds { get; }

            public bool Negate { get; }

            public bool Celtic { get; }

            public FoldedFormula Fixed { get; }
        }
    }
}
