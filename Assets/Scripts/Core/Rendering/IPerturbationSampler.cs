using System;
using System.Threading;

namespace FractalVisio.Core
{
    /// <summary>
    /// A deep-zoom kernel by perturbation. Implement it on a <b>struct</b>, for the same reason as
    /// <see cref="IEscapeSamplerD"/>.
    ///
    /// The idea, in two steps. Once per render, iterate the centre of the view in high precision
    /// and keep the orbit <c>Z_n</c> as plain doubles (<see cref="BuildReference"/>). Then every
    /// pixel iterates only its offset <c>d_n = z_n - Z_n</c>, which is tiny and therefore fits fp64
    /// no matter how deep the view is; for z^2 + c that is <c>d' = 2 Z d + d^2 + dc</c>. The cost per
    /// pixel stays fp64-sized at every depth, which is the whole point: double-double is 15-20x
    /// slower per iteration and made the picture's frame rate a function of how deep it was.
    ///
    /// Two rules every implementation needs, both from the WPF engine this was ported from:
    /// <list type="bullet">
    /// <item>Rebase (Zhuoran): when <c>|z| &lt; |d|</c>, or the reference ran out, or
    /// <c>|z|^2 &lt; 1e-6 |Z|^2</c> (Pauldelbrot), set <c>d = z - Z_0</c> and restart the reference
    /// at index 0. That alone removes glitches without a second reference point.</item>
    /// <item>Always keep at least <c>Z_0</c> and <c>Z_1</c> in the orbit, even when the centre
    /// escapes at once, or a rebased pixel has no reference step to take.</item>
    /// </list>
    /// Cancellation returns, it does not throw - see <see cref="IEscapeSamplerD"/>.
    /// </summary>
    public interface IPerturbationSampler
    {
        /// <summary>
        /// Fill <paramref name="orbit"/> with the reference orbit of the point (<paramref name="cx"/>,
        /// <paramref name="cy"/>), up to <paramref name="maxIterations"/> steps.
        /// <paramref name="maxDeltaC"/> bounds how far any pixel of this render lies from that
        /// point; a sampler that builds a <see cref="ComplexBlaTable"/> needs it.
        /// </summary>
        void BuildReference(ReferenceOrbit orbit, in DoubleDouble cx, in DoubleDouble cy, int maxIterations, double maxDeltaC);

        /// <summary>Escape value for the pixel at offset (<paramref name="deltaCx"/>, <paramref name="deltaCy"/>) from the reference.</summary>
        float Sample(ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token);
    }

    /// <summary>
    /// The reference orbit of one render, as doubles, plus its optional BLA table. Owned by a
    /// renderer and rebuilt in place for every request: a gesture restarts renders several times a
    /// second, and allocating a fresh orbit and table each time would hand the garbage collector a
    /// steady stream of work on the frames that most need to be smooth.
    /// </summary>
    public sealed class ReferenceOrbit
    {
        private double[] re = Array.Empty<double>();
        private double[] im = Array.Empty<double>();

        /// <summary>Real parts of <c>Z_0 .. Z_(Length-1)</c>. May be longer than <see cref="Length"/>.</summary>
        public double[] Re => re;

        public double[] Im => im;

        public int Length { get; private set; }

        /// <summary>Iteration-skipping table. Only meaningful while <see cref="HasBla"/> is set.</summary>
        public ComplexBlaTable Bla { get; } = new ComplexBlaTable();

        public bool HasBla { get; private set; }

        /// <summary>Start a new orbit of up to <paramref name="maxIterations"/> steps.</summary>
        public void Begin(int maxIterations)
        {
            var capacity = Math.Max(2, maxIterations + 1);
            if (re.Length < capacity)
            {
                re = new double[capacity];
                im = new double[capacity];
            }

            Length = 0;
            HasBla = false;
        }

        /// <summary>Append the next orbit point. Returns false once the capacity is used up.</summary>
        public bool Append(double real, double imaginary)
        {
            if (Length >= re.Length)
            {
                return false;
            }

            re[Length] = real;
            im[Length] = imaginary;
            Length++;
            return true;
        }

        /// <summary>
        /// Build the BLA table for a z^2 + c orbit. <paramref name="escapeSquared"/> is the pixel
        /// bailout on the squared modulus: no skip may jump across it.
        /// </summary>
        public void BuildQuadraticBla(double escapeSquared, double maxDeltaC)
        {
            HasBla = Bla.Build(this, escapeSquared, maxDeltaC);
        }
    }
}
