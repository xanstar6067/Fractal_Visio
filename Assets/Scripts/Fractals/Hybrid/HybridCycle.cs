using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// One turn of a hybrid's iteration: every step in order, each row's formula as many times as
    /// it repeats - "Mandelbrot x2, Burning Ship" is (M, M, S), then again from the start. Reduced to
    /// its shortest period, so (M, S, M, S) is (M, S) and "Burning Ship x3" is the Burning Ship: the
    /// deep zoom keeps one reference orbit per step of the turn, and a turn twice as long as it
    /// needs to be costs twice the orbits for the same picture.
    ///
    /// Built from the parameters (<see cref="HybridSequence"/>) and cached: the shader binding asks
    /// for it every frame, the CPU passes on every request.
    /// </summary>
    public sealed class HybridCycle
    {
        private static volatile HybridCycle last;

        // The sequence's parameter values this cycle was built from, by position.
        private readonly double[] key;

        // (formula, power) of every step of one turn: what the cycle computes.
        private readonly int[] signature;

        private HybridCycle(double[] key, FoldedFormula[] steps, int[] signature)
        {
            this.key = key;
            Steps = steps;
            this.signature = signature;
        }

        /// <summary>The steps of one turn, in order; never empty.</summary>
        public FoldedFormula[] Steps { get; }

        public int Length => Steps.Length;

        /// <summary>Whether <paramref name="other"/> computes the same steps - two parameter sets can describe one cycle.</summary>
        public bool SameSteps(HybridCycle other)
        {
            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (other == null || other.signature.Length != signature.Length)
            {
                return false;
            }

            for (var i = 0; i < signature.Length; i++)
            {
                if (signature[i] != other.signature[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>The cycle <paramref name="parameters"/> describe. The sequence's values lead every hybrid's parameter list.</summary>
        public static HybridCycle Of(in FractalParameterSet parameters)
        {
            var cached = last;
            if (cached != null && cached.Matches(parameters))
            {
                return cached;
            }

            var built = Build(parameters);
            last = built;
            return built;
        }

        private bool Matches(in FractalParameterSet parameters)
        {
            if (parameters.Count < key.Length)
            {
                return false;
            }

            for (var i = 0; i < key.Length; i++)
            {
                if (!parameters[i].Equals(key[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static HybridCycle Build(in FractalParameterSet parameters)
        {
            var key = new double[Math.Min(HybridSequence.ValueCount, parameters.Count)];
            for (var i = 0; i < key.Length; i++)
            {
                key[i] = parameters[i];
            }

            // Each step as (formula, its real power), so two steps compare equal exactly when they compute the same.
            var choices = new List<int>();
            var powers = new List<int>();
            var rows = Math.Clamp((int)Math.Round(Value(key, 0, 1d)), 1, HybridSequence.MaximumRows);
            for (var row = 0; row < rows && choices.Count < HybridSequence.MaximumCycle; row++)
            {
                var choice = Math.Clamp((int)Math.Round(Value(key, HybridSequence.FormulaIndex(row), 0d)), 0, HybridSteps.Count - 1);
                var power = HybridSteps.EffectivePower(choice, (int)Math.Round(Value(key, HybridSequence.PowerIndex(row), 2d)));
                var repeats = Math.Clamp(
                    (int)Math.Round(Value(key, HybridSequence.RepeatsIndex(row), 1d)), 1, HybridSequence.MaximumRepeats);
                for (var r = 0; r < repeats && choices.Count < HybridSequence.MaximumCycle; r++)
                {
                    choices.Add(choice);
                    powers.Add(power);
                }
            }

            var period = ShortestPeriod(choices, powers);
            var steps = new FoldedFormula[period];
            var signature = new int[period * 2];
            for (var i = 0; i < period; i++)
            {
                steps[i] = HybridSteps.Formula(choices[i], powers[i]);
                signature[i * 2] = choices[i];
                signature[i * 2 + 1] = powers[i];
            }

            return new HybridCycle(key, steps, signature);
        }

        private static double Value(double[] values, int index, double fallback)
        {
            return index < values.Length ? values[index] : fallback;
        }

        /// <summary>The smallest d dividing the length such that the sequence repeats every d steps.</summary>
        private static int ShortestPeriod(List<int> choices, List<int> powers)
        {
            var length = choices.Count;
            for (var period = 1; period < length; period++)
            {
                if (length % period != 0)
                {
                    continue;
                }

                var repeats = true;
                for (var i = period; i < length && repeats; i++)
                {
                    repeats = choices[i] == choices[i - period] && powers[i] == powers[i - period];
                }

                if (repeats)
                {
                    return period;
                }
            }

            return Math.Max(1, length);
        }
    }

    /// <summary>
    /// A hybrid's sequence as parameters (<see cref="IStepSequence"/>): a row count, then for every
    /// possible row its formula, power and repeats. They come first in both hybrids' parameter lists,
    /// in this order - <see cref="HybridCycle"/> reads them by position - and their keys are the same
    /// in both, so a C map and "Julia set of the centre" carry the sequence over by key.
    /// </summary>
    public static class HybridSequence
    {
        public const int MaximumRows = 8;

        /// <summary>
        /// Steps in one turn. The deep zoom keeps a reference orbit per step (see
        /// <see cref="HybridPerturbationSampler"/>), so this bounds its memory and the time to build
        /// them; WPF allowed 256, and its deep zoom did not rebase a hybrid exactly.
        /// </summary>
        public const int MaximumCycle = 16;

        public const int MaximumRepeats = 16;

        public const string RowsKey = "rows";

        /// <summary>How many parameters the sequence takes at the front of the list.</summary>
        public const int ValueCount = 1 + 3 * MaximumRows;

        private static readonly string[] FormulaKeys = Keys("f");
        private static readonly string[] PowerKeys = Keys("p");
        private static readonly string[] RepeatsKeys = Keys("r");

        public static string FormulaKey(int row) => FormulaKeys[row];

        public static string PowerKey(int row) => PowerKeys[row];

        public static string RepeatsKey(int row) => RepeatsKeys[row];

        public static int FormulaIndex(int row) => 1 + 3 * row;

        public static int PowerIndex(int row) => 2 + 3 * row;

        public static int RepeatsIndex(int row) => 3 + 3 * row;

        /// <summary>
        /// The sequence's descriptors, WPF's default first: Mandelbrot twice, then the Burning Ship.
        /// Rows past the count are ignored; a row added is reset (see the fractal panel's editor).
        /// </summary>
        public static List<FractalParameterDescriptor> Descriptors()
        {
            var mandelbrot = HybridSteps.IndexOf("mandelbrot");
            var ship = HybridSteps.IndexOf("burning-ship");
            var list = new List<FractalParameterDescriptor>
            {
                new(RowsKey, "Rows", 2d, 1d, MaximumRows, FractalParameterKind.Int)
            };

            for (var row = 0; row < MaximumRows; row++)
            {
                var formula = row == 1 ? ship : mandelbrot;
                list.Add(new FractalParameterDescriptor(
                    FormulaKeys[row], "Formula", formula, 0d, HybridSteps.Count - 1, FractalParameterKind.Int));
                list.Add(new FractalParameterDescriptor(
                    PowerKeys[row], "Power", 2d, HybridSteps.MinimumPower, HybridSteps.MaximumPower, FractalParameterKind.Int));
                list.Add(new FractalParameterDescriptor(
                    RepeatsKeys[row], "Repeats", row == 0 ? 2d : 1d, 1d, MaximumRepeats, FractalParameterKind.Int));
            }

            return list;
        }

        /// <summary>A whole sequence as parameter values: rows of (formula id, power, repeats). For places.</summary>
        public static ParameterValue[] Values(params (string Formula, int Power, int Repeats)[] rows)
        {
            var values = new List<ParameterValue> { new(RowsKey, rows.Length) };
            for (var row = 0; row < rows.Length; row++)
            {
                var index = HybridSteps.IndexOf(rows[row].Formula);
                if (index < 0)
                {
                    throw new ArgumentException("No hybrid step " + rows[row].Formula);
                }

                values.Add(new ParameterValue(FormulaKeys[row], index));
                values.Add(new ParameterValue(PowerKeys[row], rows[row].Power));
                values.Add(new ParameterValue(RepeatsKeys[row], rows[row].Repeats));
            }

            return values.ToArray();
        }

        private static string[] Keys(string prefix)
        {
            var keys = new string[MaximumRows];
            for (var i = 0; i < keys.Length; i++)
            {
                keys[i] = prefix + i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return keys;
        }
    }
}
