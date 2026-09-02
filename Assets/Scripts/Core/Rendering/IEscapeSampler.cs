using System.Threading;

namespace FractalVisio.Core
{
    /// <summary>
    /// The per-pixel escape-time kernel in fp64. Implement it on a <b>struct</b>: the renderer only
    /// ever calls it through a generic type parameter constrained to
    /// <c>struct, IEscapeSamplerD</c>, so the JIT compiles a private copy of the whole pass loop
    /// with this body inlined. Implement it on a class and every pixel becomes an interface call
    /// that cannot be inlined - the same loop then runs several times slower.
    /// </summary>
    public interface IEscapeSamplerD
    {
        /// <returns>Iterations until escape, or <paramref name="maxIterations"/> if it never did.</returns>
        int Sample(double cx, double cy, int maxIterations, CancellationToken token);
    }

    /// <summary>Same contract in double-double (~30 digits) for the deep-zoom path.</summary>
    public interface IEscapeSamplerDD
    {
        int Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token);
    }
}
