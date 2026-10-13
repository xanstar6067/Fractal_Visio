using System;
using System.Collections.Generic;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Reading and editing a <see cref="IStepSequence"/> on the session, for the fractal panel's rows
    /// and the step sheet. Every edit is one session change (<see cref="FractalSession.SetParameters"/>),
    /// so a swap of two rows never renders or saves half done.
    /// </summary>
    internal static class StepSequenceEdit
    {
        public static int Rows(IStepSequence sequence, in FractalParameterSet parameters) =>
            Math.Clamp((int)Math.Round(parameters.Get(sequence.RowsKey, 1d)), 1, sequence.MaximumRows);

        public static int Formula(IStepSequence sequence, in FractalParameterSet parameters, int row) =>
            Math.Clamp((int)Math.Round(parameters.Get(sequence.FormulaKey(row))), 0, Math.Max(0, sequence.Choices.Count - 1));

        public static int Power(IStepSequence sequence, in FractalParameterSet parameters, int row) =>
            (int)Math.Round(parameters.Get(sequence.PowerKey(row), 2d));

        public static int Repeats(IStepSequence sequence, in FractalParameterSet parameters, int row) =>
            Math.Max(1, (int)Math.Round(parameters.Get(sequence.RepeatsKey(row), 1d)));

        /// <summary>Steps in one turn: the rows' repeats added up.</summary>
        public static int CycleLength(IStepSequence sequence, in FractalParameterSet parameters)
        {
            var total = 0;
            var rows = Rows(sequence, parameters);
            for (var row = 0; row < rows; row++)
            {
                total += Repeats(sequence, parameters, row);
            }

            return total;
        }

        public static bool CanAddRow(IStepSequence sequence, in FractalParameterSet parameters) =>
            Rows(sequence, parameters) < sequence.MaximumRows && CycleLength(sequence, parameters) < sequence.MaximumCycle;

        /// <summary>Every value of the sequence, rows past the count included: what Undo puts back.</summary>
        public static ParameterValue[] Snapshot(IStepSequence sequence, in FractalParameterSet parameters)
        {
            var values = new List<ParameterValue> { new(sequence.RowsKey, parameters.Get(sequence.RowsKey, 1d)) };
            for (var row = 0; row < sequence.MaximumRows; row++)
            {
                values.Add(new ParameterValue(sequence.FormulaKey(row), parameters.Get(sequence.FormulaKey(row))));
                values.Add(new ParameterValue(sequence.PowerKey(row), parameters.Get(sequence.PowerKey(row), 2d)));
                values.Add(new ParameterValue(sequence.RepeatsKey(row), parameters.Get(sequence.RepeatsKey(row), 1d)));
            }

            return values.ToArray();
        }

        public static void SetPower(FractalSession session, IStepSequence sequence, int row, int power)
        {
            session.SetParameter(sequence.PowerKey(row), power);
        }

        public static void SetFormula(FractalSession session, IStepSequence sequence, int row, int choice)
        {
            session.SetParameter(sequence.FormulaKey(row), choice);
        }

        /// <summary>One more or one fewer repeat, within 1 and what the cycle has room for.</summary>
        public static void ChangeRepeats(FractalSession session, IStepSequence sequence, int row, int delta)
        {
            var parameters = session.Parameters;
            var repeats = Repeats(sequence, parameters, row);
            var room = sequence.MaximumCycle - CycleLength(sequence, parameters);
            var next = Math.Max(1, repeats + Math.Min(delta, room));
            if (next != repeats)
            {
                session.SetParameter(sequence.RepeatsKey(row), next);
            }
        }

        /// <summary>A new last row: the first formula (the Mandelbrot set), squared, once.</summary>
        public static void AddRow(FractalSession session, IStepSequence sequence)
        {
            var parameters = session.Parameters;
            if (!CanAddRow(sequence, parameters))
            {
                return;
            }

            var row = Rows(sequence, parameters);
            session.SetParameters(new[]
            {
                new ParameterValue(sequence.RowsKey, row + 1),
                new ParameterValue(sequence.FormulaKey(row), 0d),
                new ParameterValue(sequence.PowerKey(row), 2d),
                new ParameterValue(sequence.RepeatsKey(row), 1d)
            });
        }

        /// <summary>Remove a row; the ones below move up. Returns what Undo needs, or null if nothing changed.</summary>
        public static ParameterValue[] RemoveRow(FractalSession session, IStepSequence sequence, int row)
        {
            var parameters = session.Parameters;
            var rows = Rows(sequence, parameters);
            if (rows <= 1 || row < 0 || row >= rows)
            {
                return null;
            }

            var before = Snapshot(sequence, parameters);
            var values = new List<ParameterValue> { new(sequence.RowsKey, rows - 1) };
            for (var i = row; i < rows - 1; i++)
            {
                CopyRow(sequence, parameters, i + 1, i, values);
            }

            session.SetParameters(values);
            return before;
        }

        /// <summary>Swap a row with its neighbour above (-1) or below (+1).</summary>
        public static void MoveRow(FractalSession session, IStepSequence sequence, int row, int direction)
        {
            var parameters = session.Parameters;
            var other = row + direction;
            if (other < 0 || other >= Rows(sequence, parameters) || row < 0)
            {
                return;
            }

            var values = new List<ParameterValue>();
            CopyRow(sequence, parameters, row, other, values);
            CopyRow(sequence, parameters, other, row, values);
            session.SetParameters(values);
        }

        private static void CopyRow(IStepSequence sequence, in FractalParameterSet parameters, int from, int to, List<ParameterValue> values)
        {
            values.Add(new ParameterValue(sequence.FormulaKey(to), parameters.Get(sequence.FormulaKey(from))));
            values.Add(new ParameterValue(sequence.PowerKey(to), parameters.Get(sequence.PowerKey(from), 2d)));
            values.Add(new ParameterValue(sequence.RepeatsKey(to), parameters.Get(sequence.RepeatsKey(from), 1d)));
        }

        /// <summary>"Mandelbrot": <c>fractal.&lt;id&gt;</c>, else the formula's own name.</summary>
        public static string ChoiceName(IStringCatalog strings, in StepChoice choice) =>
            strings.GetOr("fractal." + choice.Id, choice.Name);
    }
}
