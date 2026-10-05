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
        DoubleDouble = 1 << 2,

        /// <summary>
        /// CPU perturbation: one reference orbit in double-double, every pixel iterates only its
        /// offset from it in fp64. Covers the depths of <see cref="DoubleDouble"/> at roughly fp64
        /// cost. A definition that declares it takes deep renders through
        /// <see cref="ICpuPassHost.RunPerturbed{T}"/> instead of its double-double sampler.
        /// </summary>
        Perturbation = 1 << 3,

        /// <summary>
        /// The perturbation reference may take any number of digits: below double-double's reach it
        /// is built in fixed point (<see cref="FixedPointOrbit"/>), and the zoom goes on to
        /// <c>RenderQuality.ArbitraryMinimumScale</c> (1e-280) instead of stopping near 1e-24. A
        /// definition declares it only if its <see cref="IPerturbationSampler.BuildReference"/>
        /// builds an orbit at the precision the depth asks for, rather than converting the centre to
        /// double-double.
        /// </summary>
        Arbitrary = 1 << 4
    }
}
