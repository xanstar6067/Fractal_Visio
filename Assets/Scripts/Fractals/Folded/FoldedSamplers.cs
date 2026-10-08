using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// A <see cref="FoldedFormula"/> iterated in fp64, either way round: on the parameter plane
    /// (z_0 = 0, c = the pixel) or as a Julia set (z_0 = the pixel, c fixed). One struct for both:
    /// the choice is a field read once per pixel, and both forms share every line of the loop.
    /// </summary>
    public readonly struct FoldedSamplerD : IEscapeSamplerD
    {
        private readonly FoldedFormula formula;
        private readonly bool julia;
        private readonly double constantX;
        private readonly double constantY;

        public FoldedSamplerD(in FoldedFormula formula)
        {
            this.formula = formula;
            julia = false;
            constantX = 0d;
            constantY = 0d;
        }

        public FoldedSamplerD(in FoldedFormula formula, double constantX, double constantY)
        {
            this.formula = formula;
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
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                formula.Step(zx, zy, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, formula.Degree);
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

            // With respect to c (J_0 = 0, plus I per step) or to the start (J_0 = I).
            var identity = julia ? 0d : 1d;
            var j00 = julia ? 1d : 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = j00;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                formula.JacobianStep(zx, zy, identity, ref j00, ref j01, ref j10, ref j11);
                formula.Step(zx, zy, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(zx, zy, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, formula.Degree);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>The same iteration in double-double: the exact reference the perturbation sampler is checked against.</summary>
    public readonly struct FoldedSamplerDD : IEscapeSamplerDD
    {
        private readonly FoldedFormula formula;
        private readonly bool julia;
        private readonly DoubleDouble constantX;
        private readonly DoubleDouble constantY;

        public FoldedSamplerDD(in FoldedFormula formula)
        {
            this.formula = formula;
            julia = false;
            constantX = default;
            constantY = default;
        }

        public FoldedSamplerDD(in FoldedFormula formula, double constantX, double constantY)
        {
            this.formula = formula;
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
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, formula.Degree);
                }

                formula.Step(zx, zy, out var re, out var im);
                zx = DoubleDouble.Add(re, cx);
                zy = DoubleDouble.Add(im, cy);
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation for every <see cref="FoldedFormula"/>, on the parameter plane or as
    /// a Julia set. The offset steps through <see cref="FoldedFormula.Perturb"/>; rebasing is per
    /// component, as for the Burning Ship - a fold acts on x and y separately, so a real part near
    /// zero next to a larger real offset glitches the next fold even when |z| is large. A Julia set
    /// rebases onto the orbit of 0 at the same C (<see cref="ReferenceOrbit.Secondary"/>), as
    /// <see cref="JuliaBurningShipPerturbationSampler"/> does. No BLA: the linear part of a folded
    /// step is a real 2x2 map, and like the ship these run at fp64 cost through rebasing alone.
    /// </summary>
    public readonly struct FoldedPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly FoldedFormula formula;
        private readonly bool julia;
        private readonly double constantX;
        private readonly double constantY;

        public FoldedPerturbationSampler(in FoldedFormula formula)
        {
            this.formula = formula;
            julia = false;
            constantX = 0d;
            constantY = 0d;
        }

        public FoldedPerturbationSampler(in FoldedFormula formula, double constantX, double constantY)
        {
            this.formula = formula;
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
                BuildOrbit(orbit, zero, zero, x, y, maxIterations, token);
                return;
            }

            var cx = new DoubleDouble(constantX);
            var cy = new DoubleDouble(constantY);
            BuildOrbit(orbit, x, y, cx, cy, maxIterations, token);
            BuildOrbit(orbit.Secondary, zero, zero, cx, cy, maxIterations, token);
        }

        private void BuildOrbit(
            ReferenceOrbit orbit, DoubleDouble zx, DoubleDouble zy, in DoubleDouble cx, in DoubleDouble cy,
            int maxIterations, CancellationToken token)
        {
            orbit.Begin(maxIterations);

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

                formula.Step(zx, zy, out var re, out var im);
                zx = DoubleDouble.Add(re, cx);
                zy = DoubleDouble.Add(im, cy);
            }
        }

        public float Sample(ReferenceOrbit orbit, double deltaX, double deltaY, int maxIterations, CancellationToken token)
        {
            // Parameter plane: d_0 = 0 and dc is added every step. Julia set: d_0 is the offset and
            // nothing is added.
            var rebaseOrbit = julia ? orbit.Secondary : orbit;
            var addX = julia ? 0d : deltaX;
            var addY = julia ? 0d : deltaY;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;

            var dx = julia ? deltaX : 0d;
            var dy = julia ? deltaY : 0d;
            var referenceIndex = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 1023) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                formula.Perturb(re[referenceIndex], im[referenceIndex], dx, dy, out var nextX, out var nextY);
                dx = nextX + addX;
                dy = nextY + addY;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout, formula.Degree);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    re = rebaseOrbit.Re;
                    im = rebaseOrbit.Im;
                    length = rebaseOrbit.Length;
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

            var rebaseOrbit = julia ? orbit.Secondary : orbit;
            var addX = julia ? 0d : deltaX;
            var addY = julia ? 0d : deltaY;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;

            var dx = julia ? deltaX : 0d;
            var dy = julia ? deltaY : 0d;
            var identity = julia ? 0d : 1d;
            var j00 = julia ? 1d : 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = j00;
            var referenceIndex = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 1023) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                var zr = re[referenceIndex];
                var zi = im[referenceIndex];
                formula.JacobianStep(zr + dx, zi + dy, identity, ref j00, ref j01, ref j10, ref j11);

                formula.Perturb(zr, zi, dx, dy, out var nextX, out var nextY);
                dx = nextX + addX;
                dy = nextY + addY;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(fullX, fullY, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout, formula.Degree);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    re = rebaseOrbit.Re;
                    im = rebaseOrbit.Im;
                    length = rebaseOrbit.Length;
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }
}
