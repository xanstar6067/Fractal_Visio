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
    ///
    /// <b>Cancellation returns, it does not throw.</b> Check the token every few hundred iterations
    /// and return any value once it is set; the renderer discards samples of a cancelled pass. A
    /// throw is the expensive way to say the same thing, and a gesture cancels renders several
    /// times a second.
    /// </summary>
    public interface IEscapeSamplerD
    {
        /// <returns>
        /// The continuous escape count, or a <b>negative</b> value if the point never escaped
        /// within <paramref name="maxIterations"/>. Negative is the interior marker the whole
        /// colouring path keys on - do not return <paramref name="maxIterations"/> for it.
        /// </returns>
        float Sample(double cx, double cy, int maxIterations, CancellationToken token);

        /// <summary>
        /// <see cref="Sample"/>, also returning the direction in which the escape potential rises
        /// at this point - the relief's slope (<see cref="ReliefLight"/>) - in plane coordinates,
        /// not normalised; (0, 0) for the interior. Computed from the orbit's derivative, which the
        /// plain <see cref="Sample"/> does not carry: a second loop rather than a flag, so the
        /// picture without relief pays nothing for it. <see cref="EscapeMath.HolomorphicSlope"/> and
        /// <see cref="EscapeMath.JacobianSlope"/> turn the final z and derivative into the slope.
        /// </summary>
        float SampleWithSlope(double cx, double cy, int maxIterations, CancellationToken token, out double slopeX, out double slopeY);
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
        /// Relief slope at an escaped point of a complex-analytic map, from the final z and its
        /// derivative dz (with respect to c, or to the starting point for a Julia set): the gradient
        /// of log|z| is conj(dz / z), which points the same way as z conj(dz).
        /// </summary>
        public static void HolomorphicSlope(double zx, double zy, double dx, double dy, out double slopeX, out double slopeY)
        {
            slopeX = zx * dx + zy * dy;
            slopeY = zy * dx - zx * dy;
        }

        /// <summary>
        /// Relief slope for a map that is not complex-analytic (a fold, a conjugate), from the final z
        /// and its real Jacobian: <c>j00 = dx/dcx, j01 = dx/dcy, j10 = dy/dcx, j11 = dy/dcy</c>. The
        /// gradient of log|z| is J^T z / |z|^2.
        /// </summary>
        public static void JacobianSlope(
            double zx, double zy, double j00, double j01, double j10, double j11, out double slopeX, out double slopeY)
        {
            slopeX = j00 * zx + j10 * zy;
            slopeY = j01 * zx + j11 * zy;
        }

        /// <summary>
        /// One step of a quadratic map's Jacobian, for every member of the z^2 family at once. The
        /// step's own derivative is <c>[[2x a, -2y a], [2y b, 2x b]]</c>: a = b = 1 is z^2 itself,
        /// b = -1 the Tricorn's conj(z)^2, b = -sign(xy) the Burning Ship's fold, a = sign(x^2 - y^2)
        /// the Celtic one. <paramref name="identity"/> is 1 when c varies with the pixel (the
        /// Mandelbrot form: J' = Df J + I) and 0 for a Julia set. (x, y) is z before the step.
        /// </summary>
        public static void QuadraticJacobianStep(
            double x, double y, double a, double b, double identity,
            ref double j00, ref double j01, ref double j10, ref double j11)
        {
            var x2 = 2d * x;
            var y2 = 2d * y;
            var n00 = a * (x2 * j00 - y2 * j10) + identity;
            var n01 = a * (x2 * j01 - y2 * j11);
            var n10 = b * (y2 * j00 + x2 * j10);
            var n11 = b * (y2 * j01 + x2 * j11) + identity;
            j00 = n00;
            j01 = n01;
            j10 = n10;
            j11 = n11;
        }

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

        /// <summary>
        /// Same for a map of degree <paramref name="power"/> (z^p + c): the count grows by one each
        /// time log|z| is multiplied by p, so the logarithm is taken to base p. Using base 2 for a
        /// cubic map leaves a visible step at every iteration boundary.
        /// </summary>
        public static float Smooth(int iteration, double squaredModulus, double bailout, double power)
        {
            if (!(power > 1d) || power == 2d)
            {
                return Smooth(iteration, squaredModulus, bailout);
            }

            if (!(squaredModulus > 1d) || !(bailout > 1d) || double.IsInfinity(squaredModulus))
            {
                return iteration + 1;
            }

            var ratio = System.Math.Log(squaredModulus) / System.Math.Log(bailout);
            if (!(ratio > 0d))
            {
                return iteration + 1;
            }

            return (float)(iteration + 1 - System.Math.Log(ratio) / System.Math.Log(power));
        }
    }
}
