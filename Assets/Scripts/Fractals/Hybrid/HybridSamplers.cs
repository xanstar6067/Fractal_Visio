using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// A hybrid iterated in fp64: step k of the orbit is the formula at phase k of the
    /// <see cref="HybridCycle"/>, each followed by + c and its own escape test, as in WPF. Both
    /// forms, like <see cref="FoldedSamplerD"/>: the parameter plane (z_0 = 0, c = the pixel) or a
    /// Julia set (z_0 = the pixel, c fixed).
    ///
    /// The smooth count uses the degree of the step that escaped. That is what keeps it continuous
    /// across a band boundary even when the steps' degrees differ: a pixel that escapes one step
    /// later had, one step earlier, |z| just under the bailout, and the step it then takes is the one
    /// whose degree measures how far past the bailout it lands.
    /// </summary>
    public readonly struct HybridSamplerD : IEscapeSamplerD
    {
        private readonly FoldedFormula[] steps;
        private readonly bool julia;
        private readonly double constantX;
        private readonly double constantY;

        public HybridSamplerD(HybridCycle cycle)
        {
            steps = cycle.Steps;
            julia = false;
            constantX = 0d;
            constantY = 0d;
        }

        public HybridSamplerD(HybridCycle cycle, double constantX, double constantY)
        {
            steps = cycle.Steps;
            julia = true;
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public float Sample(double x, double y, int maxIterations, CancellationToken token)
        {
            var zx = julia ? x : 0d;
            var zy = julia ? y : 0d;
            var cx = julia ? constantX : x;
            var cy = julia ? constantY : y;
            var count = steps.Length;
            var phase = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                ref readonly var step = ref steps[phase];
                step.Step(zx, zy, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;
                phase = phase + 1 == count ? 0 : phase + 1;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, step.Degree);
                }
            }

            return EscapeMath.Interior;
        }

        public float SampleWithSlope(
            double x, double y, int maxIterations, CancellationToken token, out double slopeX, out double slopeY)
        {
            slopeX = 0d;
            slopeY = 0d;

            var zx = julia ? x : 0d;
            var zy = julia ? y : 0d;
            var cx = julia ? constantX : x;
            var cy = julia ? constantY : y;

            var identity = julia ? 0d : 1d;
            var j00 = julia ? 1d : 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = j00;
            var count = steps.Length;
            var phase = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                ref readonly var step = ref steps[phase];
                step.JacobianStep(zx, zy, identity, ref j00, ref j01, ref j10, ref j11);
                step.Step(zx, zy, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;
                phase = phase + 1 == count ? 0 : phase + 1;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(zx, zy, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, step.Degree);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>The same iteration in double-double: the exact reference the perturbation sampler is checked against.</summary>
    public readonly struct HybridSamplerDD : IEscapeSamplerDD
    {
        private readonly FoldedFormula[] steps;
        private readonly bool julia;
        private readonly DoubleDouble constantX;
        private readonly DoubleDouble constantY;

        public HybridSamplerDD(HybridCycle cycle)
        {
            steps = cycle.Steps;
            julia = false;
            constantX = default;
            constantY = default;
        }

        public HybridSamplerDD(HybridCycle cycle, double constantX, double constantY)
        {
            steps = cycle.Steps;
            julia = true;
            this.constantX = new DoubleDouble(constantX);
            this.constantY = new DoubleDouble(constantY);
        }

        public float Sample(in DoubleDouble x, in DoubleDouble y, int maxIterations, CancellationToken token)
        {
            var zx = julia ? x : new DoubleDouble(0d);
            var zy = julia ? y : new DoubleDouble(0d);
            var cx = julia ? constantX : x;
            var cy = julia ? constantY : y;
            var count = steps.Length;
            var phase = 0;
            var degree = steps[count - 1].Degree;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 63) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                // Tested before the step, unlike the fp64 samplers: compare with one more iteration.
                var squared = DoubleDouble.Add(DoubleDouble.Square(zx), DoubleDouble.Square(zy)).ToDouble();
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, degree);
                }

                ref readonly var step = ref steps[phase];
                step.Step(zx, zy, out var re, out var im);
                zx = DoubleDouble.Add(re, cx);
                zy = DoubleDouble.Add(im, cy);
                degree = step.Degree;
                iteration++;
                phase = phase + 1 == count ? 0 : phase + 1;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation for a hybrid, on the parameter plane or as a Julia set. The offset
    /// steps through each phase's <see cref="FoldedFormula.Perturb"/> and rebases per component, as
    /// <see cref="FoldedPerturbationSampler"/> does - with one difference that matters.
    ///
    /// A rebase moves the pixel onto an orbit that starts at 0 (<c>d = z</c>, exact). For a single
    /// formula there is one such orbit; for a hybrid there is one per phase of the cycle, because the
    /// next step of the pixel is the formula of <i>its</i> phase, and an orbit of 0 that started with
    /// another formula is a different orbit. So the reference keeps one beside it per phase
    /// (<see cref="ReferenceOrbit.Extra"/>; on the parameter plane phase 0's is the reference
    /// itself), and a pixel rebases onto the one of its phase. WPF rebased onto a single step of the
    /// same phase whose value is not 0, which cost the pixel its digits; this is why the cycle is
    /// capped at <see cref="HybridSequence.MaximumCycle"/> steps.
    /// </summary>
    public readonly struct HybridPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly HybridCycle cycle;
        private readonly FoldedFormula[] steps;
        private readonly bool julia;
        private readonly double constantX;
        private readonly double constantY;

        public HybridPerturbationSampler(HybridCycle cycle)
        {
            this.cycle = cycle;
            steps = cycle.Steps;
            julia = false;
            constantX = 0d;
            constantY = 0d;
        }

        public HybridPerturbationSampler(HybridCycle cycle, double constantX, double constantY)
        {
            this.cycle = cycle;
            steps = cycle.Steps;
            julia = true;
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public void BuildReference(
            ReferenceOrbit orbit, in HighPrecision centerX, in HighPrecision centerY, int maxIterations, double maxDeltaC,
            CancellationToken token)
        {
            // Double-double is as deep as these go (PrecisionTier.Arbitrary is not declared).
            var x = DoubleDouble.FromHighPrecision(centerX);
            var y = DoubleDouble.FromHighPrecision(centerY);
            var zero = new DoubleDouble(0d);

            if (!julia)
            {
                // The reference starts at 0 in phase 0: it is phase 0's rebase orbit as well.
                BuildOrbit(orbit, zero, zero, x, y, 0, maxIterations, token);
                for (var phase = 1; phase < steps.Length && !token.IsCancellationRequested; phase++)
                {
                    BuildOrbit(orbit.Extra(phase), zero, zero, x, y, phase, maxIterations, token);
                }

                return;
            }

            var cx = new DoubleDouble(constantX);
            var cy = new DoubleDouble(constantY);
            BuildOrbit(orbit, x, y, cx, cy, 0, maxIterations, token);

            // The orbits of 0 are the same for every view of this set; rebuilt only when the cycle or
            // C changed, or they are shorter than the budget asks.
            for (var phase = 0; phase < steps.Length && !token.IsCancellationRequested; phase++)
            {
                var critical = orbit.Extra(phase);
                if (critical.BuiltFor is CriticalKey key && key.Matches(cycle, constantX, constantY) &&
                    critical.BuiltIterations >= maxIterations)
                {
                    continue;
                }

                critical.BuiltFor = null;
                BuildOrbit(critical, zero, zero, cx, cy, phase, maxIterations, token);
                if (!token.IsCancellationRequested)
                {
                    critical.BuiltFor = new CriticalKey(cycle, constantX, constantY);
                    critical.BuiltIterations = maxIterations;
                }
            }
        }

        private void BuildOrbit(
            ReferenceOrbit orbit, DoubleDouble zx, DoubleDouble zy, in DoubleDouble cx, in DoubleDouble cy,
            int phase, int maxIterations, CancellationToken token)
        {
            orbit.Begin(maxIterations);
            var count = steps.Length;

            for (var index = 0; index <= maxIterations; index++)
            {
                if ((index & 255) == 255 && token.IsCancellationRequested)
                {
                    return;
                }

                var real = zx.ToDouble();
                var imaginary = zy.ToDouble();
                if (!orbit.Append(real, imaginary))
                {
                    break;
                }

                var magnitude = real * real + imaginary * imaginary;
                if (index >= 1 && !(magnitude <= ReferenceEscape))
                {
                    break;
                }

                steps[phase].Step(zx, zy, out var re, out var im);
                zx = DoubleDouble.Add(re, cx);
                zy = DoubleDouble.Add(im, cy);
                phase = phase + 1 == count ? 0 : phase + 1;
            }
        }

        /// <summary>The orbit a pixel whose next step is <paramref name="phase"/> rebases onto.</summary>
        private ReferenceOrbit RebaseOrbit(ReferenceOrbit orbit, int phase)
        {
            return !julia && phase == 0 ? orbit : orbit.Extra(phase);
        }

        public float Sample(ReferenceOrbit orbit, double deltaX, double deltaY, int maxIterations, CancellationToken token)
        {
            var addX = julia ? 0d : deltaX;
            var addY = julia ? 0d : deltaY;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;
            var count = steps.Length;

            var dx = julia ? deltaX : 0d;
            var dy = julia ? deltaY : 0d;
            var referenceIndex = 0;
            var phase = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 1023) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                ref readonly var step = ref steps[phase];
                step.Perturb(re[referenceIndex], im[referenceIndex], dx, dy, out var nextX, out var nextY);
                dx = nextX + addX;
                dy = nextY + addY;
                referenceIndex++;
                iteration++;
                phase = phase + 1 == count ? 0 : phase + 1;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout, step.Degree);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    var target = RebaseOrbit(orbit, phase);
                    re = target.Re;
                    im = target.Im;
                    length = target.Length;
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }

        public float SampleWithSlope(
            ReferenceOrbit orbit, double deltaX, double deltaY, int maxIterations, CancellationToken token,
            out double slopeX, out double slopeY)
        {
            slopeX = 0d;
            slopeY = 0d;

            var addX = julia ? 0d : deltaX;
            var addY = julia ? 0d : deltaY;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;
            var count = steps.Length;

            var dx = julia ? deltaX : 0d;
            var dy = julia ? deltaY : 0d;
            var identity = julia ? 0d : 1d;
            var j00 = julia ? 1d : 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = j00;
            var referenceIndex = 0;
            var phase = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 1023) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                ref readonly var step = ref steps[phase];
                var zr = re[referenceIndex];
                var zi = im[referenceIndex];
                step.JacobianStep(zr + dx, zi + dy, identity, ref j00, ref j01, ref j10, ref j11);

                step.Perturb(zr, zi, dx, dy, out var nextX, out var nextY);
                dx = nextX + addX;
                dy = nextY + addY;
                referenceIndex++;
                iteration++;
                phase = phase + 1 == count ? 0 : phase + 1;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(fullX, fullY, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout, step.Degree);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    var target = RebaseOrbit(orbit, phase);
                    re = target.Re;
                    im = target.Im;
                    length = target.Length;
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }

        /// <summary>What a Julia set's orbits of 0 were built for: the cycle (cached per sequence) and C.</summary>
        private sealed class CriticalKey
        {
            private readonly HybridCycle cycle;
            private readonly double constantX;
            private readonly double constantY;

            public CriticalKey(HybridCycle cycle, double constantX, double constantY)
            {
                this.cycle = cycle;
                this.constantX = constantX;
                this.constantY = constantY;
            }

            public bool Matches(HybridCycle other, double x, double y)
            {
                return cycle.SameSteps(other) && constantX == x && constantY == y;
            }
        }
    }
}
