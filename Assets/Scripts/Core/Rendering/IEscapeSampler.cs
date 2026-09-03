using System.Threading;

namespace FractalVisio.Core
{
    /// <summary>
    /// The per-pixel escape-time kernel in fp64. Implement it on a <b>struct</b>: the renderer only
    /// ever calls it through a generic type parameter constrained to
    /// <c>struct, IEscapeSamplerD</c>, so the JIT compiles a private copy of the whole pass loop
    /// with this body inlined. Implement it on a class and every pixel becomes an interface call
    /// that cannot be inlined - the same loop then runs several times slower.
    ///
    /// <b>The return value is a continuous escape count, not an iteration index.</b> A sampler that
    /// escapes should return the fractional count - the iteration it escaped on, plus how far past
    /// the bailout it went - because that fraction is the whole difference between concentric
    /// bands and a smooth ramp. The standard form for a power-2 map, with bailout on the squared
    /// modulus:
    /// <code>
    ///   nu = i + 1 - log2( log(|z|^2) / log(bailout) )
    /// </code>
    /// A bailout of 4 makes that approximation poor; use a few hundred instead. The extra
    /// iterations it costs are a handful per escaping pixel.
    /// </summary>
    public interface IEscapeSamplerD
    {
        /// <returns>
        /// The continuous escape count, or a <b>negative</b> value if the point never escaped
        /// within <paramref name="maxIterations"/>. Negative is the interior marker the whole
        /// colouring path keys on - do not return <paramref name="maxIterations"/> for it.
        /// </returns>
        float Sample(double cx, double cy, int maxIterations, CancellationToken token);
    }

    /// <summary>Same contract in double-double (~30 digits) for the deep-zoom path.</summary>
    public interface IEscapeSamplerDD
    {
        float Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token);
    }

    /// <summary>Shared helpers for writing a sampler. Not a hot path - one call per escaped pixel.</summary>
    public static class EscapeMath
    {
        /// <summary>Marker for "did not escape". Any negative value works; this is the canonical one.</summary>
        public const float Interior = -1f;

        /// <summary>
        /// Continuous escape count for a power-2 map that left <paramref name="bailout"/> (a bound
        /// on the squared modulus) at iteration <paramref name="iteration"/> with squared modulus
        /// <paramref name="squaredModulus"/>.
        /// </summary>
        public static float Smooth(int iteration, double squaredModulus, double bailout)
        {
            // log2 of a ratio of logs: 1 at the moment of escape, 2 one iteration later - which is
            // exactly what makes the value continuous across the iteration boundary.
            if (!(squaredModulus > 1d) || !(bailout > 1d) || double.IsInfinity(squaredModulus))
            {
                return iteration + 1;
            }

            var ratio = System.Math.Log(squaredModulus) / System.Math.Log(bailout);
            if (!(ratio > 0d))
            {
                return iteration + 1;
            }

            return (float)(iteration + 1 - System.Math.Log(ratio, 2d));
        }
    }
}
