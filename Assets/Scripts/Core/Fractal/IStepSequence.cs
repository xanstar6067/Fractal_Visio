using System.Collections.Generic;

namespace FractalVisio.Core
{
    /// <summary>
    /// Implemented by a definition whose parameters hold a sequence of steps the iteration cycles
    /// through - a hybrid: two Mandelbrot steps, then a Burning Ship one, then again. Beside
    /// <see cref="IFractalDefinition"/>, like <see cref="IParameterPlane"/>; the UI shows the sequence
    /// as rows to edit instead of one slider per parameter.
    ///
    /// The sequence is ordinary parameters - a row count and, per row, a formula, a power and a
    /// number of repeats - so it is saved, bookmarked and handed to a C map by key like any other
    /// parameter, and nothing outside the definition knows how a step is computed.
    /// </summary>
    public interface IStepSequence
    {
        /// <summary>Most rows. Every row up to this has its three parameters.</summary>
        int MaximumRows { get; }

        /// <summary>Most steps in one turn of the cycle: the sum of the rows' repeats.</summary>
        int MaximumCycle { get; }

        /// <summary>Parameter holding how many rows are in use.</summary>
        string RowsKey { get; }

        /// <summary>Parameter holding a row's formula: an index into <see cref="Choices"/>.</summary>
        string FormulaKey(int row);

        /// <summary>Parameter holding a row's power, where its formula lets one be chosen.</summary>
        string PowerKey(int row);

        /// <summary>Parameter holding how many times in a row the step is taken.</summary>
        string RepeatsKey(int row);

        /// <summary>The formulas a row can take, in a fixed order: saved states store the index.</summary>
        IReadOnlyList<StepChoice> Choices { get; }
    }

    /// <summary>One formula a step of a <see cref="IStepSequence"/> can take.</summary>
    public readonly struct StepChoice
    {
        public StepChoice(string id, string name, string folder, int fixedPower)
        {
            Id = id;
            Name = name;
            Folder = folder;
            FixedPower = fixedPower;
        }

        /// <summary>Its name is <c>fractal.&lt;id&gt;</c> in the locales, as the fractal of that formula's.</summary>
        public string Id { get; }

        /// <summary>The name when a locale has none.</summary>
        public string Name { get; }

        /// <summary>Gallery folder it belongs with (<c>folder.&lt;id&gt;</c>): how a long list is grouped.</summary>
        public string Folder { get; }

        /// <summary>The formula's own power, or 0 when a row chooses one.</summary>
        public int FixedPower { get; }
    }
}
