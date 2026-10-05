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
    /// <c>|z|^2 &lt; 1e-6 |Z|^2</c> (Pauldelbrot), move the pixel onto an orbit that starts at the
    /// critical point 0: <c>d = z</c>, index 0. For a Mandelbrot-type set that orbit is the reference
    /// itself (<c>Z_0 = 0</c>). A Julia set's reference starts at the centre of the view instead, so
    /// it keeps the orbit of 0 beside it in <see cref="ReferenceOrbit.Secondary"/> and rebases onto
    /// that. Either way this alone removes glitches without extra reference points.</item>
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
        /// point; a sampler that builds a <see cref="ComplexBlaTable"/> needs it, and it also says
        /// how many digits the orbit needs (<see cref="FixedPointOrbit.FractionBitsFor"/>). A sampler limited to
        /// double-double converts the point with <see cref="DoubleDouble.FromHighPrecision"/>.
        /// Returning early on <paramref name="token"/> is allowed; the renderer then discards the orbit.
        /// </summary>
        void BuildReference(
            ReferenceOrbit orbit, in HighPrecision cx, in HighPrecision cy, int maxIterations, double maxDeltaC,
            CancellationToken token);

        /// <summary>Escape value for the pixel at offset (<paramref name="deltaCx"/>, <paramref name="deltaCy"/>) from the reference.</summary>
        float Sample(ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token);

        /// <summary>
        /// <see cref="Sample"/> with the relief slope, as <see cref="IEscapeSamplerD.SampleWithSlope"/>.
        /// The derivative is of the pixel's full orbit, iterated in fp64 from the full z = Z + d (it
        /// needs a direction, not digits); a BLA skip moves it as <c>dz' = A dz + B</c>, the
        /// derivative of the skip itself, and a rebase leaves it alone, since z does not change.
        /// </summary>
        float SampleWithSlope(
            ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token,
            out double slopeX, out double slopeY);
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
        private ReferenceOrbit secondary;

        /// <summary>
        /// A second orbit a sampler may keep beside this one, reused the same way - a Julia set's
        /// orbit of the critical point, which its pixels rebase onto. Created on first use, from
        /// <see cref="IPerturbationSampler.BuildReference"/>: that runs before any pixel is sampled,
        /// so the samplers only ever read it.
        /// </summary>
        public ReferenceOrbit Secondary => secondary ??= new ReferenceOrbit();

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
            blaEscapeSquared = escapeSquared;
            BlaMaxDeltaC = maxDeltaC;
        }

        private double blaEscapeSquared;

        /// <summary>The |dc| bound the BLA table was built for; 0 when the orbit takes no dc (a Julia set).</summary>
        public double BlaMaxDeltaC { get; private set; }

        /// <summary>
        /// Rebuild the BLA table if a render reusing this orbit reaches further from it than the
        /// table was built for: its radii shrink with |dc|, so a table built for a smaller |dc|
        /// would let skips run where they are no longer valid. The orbit itself stays.
        /// </summary>
        public void EnsureBlaCovers(double maxDeltaC)
        {
            if (HasBla && BlaMaxDeltaC > 0d && maxDeltaC > BlaMaxDeltaC)
            {
                BuildQuadraticBla(blaEscapeSquared, maxDeltaC);
            }
        }

        // ---- What the renderer needs to reuse an orbit across requests (see FractalCpuRenderer).

        /// <summary>The point this orbit belongs to: pixel offsets are measured from here.</summary>
        public HighPrecision CenterX { get; set; }

        public HighPrecision CenterY { get; set; }

        /// <summary>Scale of the view it was built for: its digits are sized for that depth.</summary>
        public double BuiltScale { get; set; }

        /// <summary>Iteration budget it was built with.</summary>
        public int BuiltIterations { get; set; }

        /// <summary>Fractal and parameters it was built for; null while it is not usable.</summary>
        public object BuiltFor { get; set; }
    }
}
