using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// Every fractal the app knows. Adding one is a line here plus its three files.
    ///
    /// Deliberately plain C# for now: nothing yet chooses a fractal at runtime, and a
    /// ScriptableObject asset per definition only starts paying for itself once the menu exists.
    /// The interface the rest of the app sees (<see cref="IFractalDefinition"/>) does not change
    /// when that swap happens.
    /// </summary>
    public static class FractalCatalog
    {
        private static readonly IFractalDefinition[] Definitions =
        {
            new MandelbrotDefinition()
        };

        public static IReadOnlyList<IFractalDefinition> All => Definitions;

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
    }
}
