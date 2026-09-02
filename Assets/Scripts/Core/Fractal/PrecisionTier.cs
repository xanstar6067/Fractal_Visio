using System;

namespace FractalVisio.Core
{
    /// <summary>
    /// Arithmetic a fractal can be evaluated in. A definition declares what it implements, and the
    /// presenter picks the cheapest tier that still resolves the current scale.
    /// </summary>
    [Flags]
    public enum PrecisionTier
    {
        None = 0,

        /// <summary>GPU fp32 shader. Fast, but gives up somewhere around 1e-4.</summary>
        Float = 1 << 0,

        /// <summary>CPU fp64. The everyday path down to roughly 1e-13.</summary>
        Double = 1 << 1,

        /// <summary>CPU double-double, about 30 decimal digits. Deep zoom only - it is slow.</summary>
        DoubleDouble = 1 << 2
    }
}
