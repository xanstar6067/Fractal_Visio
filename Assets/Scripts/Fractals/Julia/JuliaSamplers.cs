using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// z -> z^2 + c from the pixel, with c fixed: the Julia set of c. The Mandelbrot iteration with
    /// the roles swapped - there the pixel is c and the start is 0. A struct on purpose - see
    /// <see cref="IEscapeSamplerD"/>.
    /// </summary>
    public readonly struct JuliaSamplerD : IEscapeSamplerD
    {
        private readonly double constantX;
        private readonly double constantY;

        public JuliaSamplerD(double constantX, double constantY)
        {
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public float Sample(double x, double y, int maxIterations, CancellationToken token)
        {
            var zx = x;
            var zy = y;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                var nextX = zx * zx - zy * zy + constantX;
                zy = 2d * zx * zy + constantY;
                zx = nextX;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>Same iteration in double-double: the exact reference the perturbation sampler is checked against.</summary>
    public readonly struct JuliaSamplerDD : IEscapeSamplerDD
    {
        private readonly DoubleDouble constantX;
        private readonly DoubleDouble constantY;

        public JuliaSamplerDD(double constantX, double constantY)
        {
            this.constantX = new DoubleDouble(constantX);
            this.constantY = new DoubleDouble(constantY);
        }

        public float Sample(in DoubleDouble x, in DoubleDouble y, int maxIterations, CancellationToken token)
        {
            var zx = x;
            var zy = y;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 63) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                var xSquared = DoubleDouble.Square(zx);
                var ySquared = DoubleDouble.Square(zy);
                var squared = DoubleDouble.Add(xSquared, ySquared).ToDouble();
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout);
                }

                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), constantX);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), constantY);
                zx = nextX;
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation, ported from the WPF engine's <c>DeepZoomPixel</c> for Julia sets:
    /// the reference is the centre of the view, and a pixel starts from its offset from it,
    /// <c>d_0 = z_0 - Z_0</c>; after that the step is the Mandelbrot one without the <c>dc</c> term,
    /// <c>d' = 2 Z d + d^2</c>, because c is the same for every pixel.
    ///
    /// Rebasing is where it differs from the WPF engine. The WPF engine restarted the reference with
    /// <c>d = z - Z_0</c>; here a pixel moves onto a second orbit, that of the critical point 0
    /// (<see cref="ReferenceOrbit.Secondary"/>), with <c>d = z</c> exactly. For the Mandelbrot set
    /// the two are the same thing, since its reference starts at 0. For a Julia set, <c>Z_0</c> is
    /// wherever the view is, so <c>z - Z_0</c> near the critical point is a difference of two
    /// unrelated numbers and loses the digits rebasing is meant to keep. The orbit of 0 also
    /// converges to the same attracting cycle as every interior pixel, so interior pixels settle into
    /// following it with shrinking offsets - and its BLA table then skips most of their iterations,
    /// which is what makes the black inside of a Julia set cheap.
    /// </summary>
    public readonly struct JuliaPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly double constantX;
        private readonly double constantY;

        public JuliaPerturbationSampler(double constantX, double constantY)
        {
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public void BuildReference(ReferenceOrbit orbit, in DoubleDouble cx, in DoubleDouble cy, int maxIterations, double maxDeltaC)
        {
            var constantReal = new DoubleDouble(constantX);
            var constantImaginary = new DoubleDouble(constantY);

            // Pixels differ only in where they start, never in c: no dc term, so the BLA radii owe
            // nothing to one.
            QuadraticOrbit.Build(orbit, cx, cy, constantReal, constantImaginary, maxIterations, ReferenceEscape);
            orbit.BuildQuadraticBla(MandelbrotSamplerD.Bailout, 0d);

            var critical = orbit.Secondary;
            QuadraticOrbit.Build(
                critical, new DoubleDouble(0d), new DoubleDouble(0d), constantReal, constantImaginary,
                maxIterations, ReferenceEscape);
            critical.BuildQuadraticBla(MandelbrotSamplerD.Bailout, 0d);
        }

        public float Sample(ReferenceOrbit orbit, double deltaX, double deltaY, int maxIterations, CancellationToken token)
        {
            var critical = orbit.Secondary;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;
            var bla = orbit.HasBla ? orbit.Bla : null;

            var dx = deltaX;
            var dy = deltaY;
            var referenceIndex = 0;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 1023) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                if (bla != null &&
                    bla.TryLookup(referenceIndex, dx * dx + dy * dy, maxIterations - iteration,
                        out var aX, out var aY, out _, out _, out var steps))
                {
                    // d <- A d, skipping `steps` iterations at once.
                    var skippedX = aX * dx - aY * dy;
                    var skippedY = aX * dy + aY * dx;
                    dx = skippedX;
                    dy = skippedY;
                    referenceIndex += steps;
                    iteration += steps;
                }
                else
                {
                    // d <- 2 Z d + d^2
                    var zr = re[referenceIndex];
                    var zi = im[referenceIndex];
                    var nextX = 2d * (zr * dx - zi * dy) + dx * dx - dy * dy;
                    var nextY = 2d * (zr * dy + zi * dx) + 2d * dx * dy;
                    dx = nextX;
                    dy = nextY;
                    referenceIndex++;
                    iteration++;
                }

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    // Onto the orbit of the critical point. It starts at 0, so d = z exactly.
                    re = critical.Re;
                    im = critical.Im;
                    length = critical.Length;
                    bla = critical.HasBla ? critical.Bla : null;
                    dx = fullX;
                    dy = fullY;
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }
}
