using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// z -> (|Re z| + i|Im z|)^2 + c. The absolute values break the symmetry that lets the
    /// Mandelbrot interior tests work, so there is no cheap early-out here.
    /// </summary>
    public readonly struct BurningShipSamplerD : IEscapeSamplerD
    {
        private readonly double bailout;

        public BurningShipSamplerD(double bailout)
        {
            this.bailout = bailout;
        }

        public float Sample(double cx, double cy, int maxIterations, CancellationToken token)
        {
            var zx = 0d;
            var zy = 0d;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                var nextX = zx * zx - zy * zy + cx;
                zy = 2d * System.Math.Abs(zx * zy) + cy;
                zx = nextX;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, bailout);
                }
            }

            return EscapeMath.Interior;
        }
    }

    public readonly struct BurningShipSamplerDD : IEscapeSamplerDD
    {
        private readonly double bailout;

        public BurningShipSamplerDD(double bailout)
        {
            this.bailout = bailout;
        }

        public float Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token)
        {
            var zx = new DoubleDouble(0d);
            var zy = new DoubleDouble(0d);
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
                if (squared > bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, bailout);
                }

                var product = DoubleDouble.Multiply(zx, zy);
                zx = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Abs(product), 2d), cy);
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation. The fold w = (|Re z|, |Im z|) is not complex-analytic, so the
    /// offset is folded component by component (<see cref="FoldedDelta"/>) and then squared as
    /// usual - ported from the WPF engine's <c>DeepZoomPixelReflected</c>. No BLA: its linear part
    /// here is a real 2x2 map rather than a complex number, and the WPF engine needed a separate
    /// table for it. Rebasing alone already takes this to fp64 cost per iteration.
    /// </summary>
    public readonly struct BurningShipPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly double bailout;

        public BurningShipPerturbationSampler(double bailout)
        {
            this.bailout = bailout;
        }

        public void BuildReference(ReferenceOrbit orbit, in DoubleDouble cx, in DoubleDouble cy, int maxIterations, double maxDeltaC)
        {
            orbit.Begin(maxIterations);

            var zx = new DoubleDouble(0d);
            var zy = new DoubleDouble(0d);

            for (var index = 0; index <= maxIterations; index++)
            {
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

                var xSquared = DoubleDouble.Square(zx);
                var ySquared = DoubleDouble.Square(zy);
                var product = DoubleDouble.Multiply(zx, zy);
                zx = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Abs(product), 2d), cy);
            }
        }

        public float Sample(ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token)
        {
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;

            var dx = 0d;
            var dy = 0d;
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

                // Folded reference W and folded offset: fold(Z + d) - fold(Z).
                var wr = System.Math.Abs(zr);
                var wi = System.Math.Abs(zi);
                var wdx = FoldedDelta(zr, dx);
                var wdy = FoldedDelta(zi, dy);

                // d <- 2 W d_w + d_w^2 + dc
                dx = 2d * (wr * wdx - wi * wdy) + wdx * wdx - wdy * wdy + deltaCx;
                dy = 2d * (wr * wdy + wi * wdx) + 2d * wdx * wdy + deltaCy;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, bailout);
                }

                // The fold works on each component separately, so the cancellation rebasing guards
                // against has to be caught per component too: a real part near zero while the
                // offset's real part is not glitches the next fold even when |z| as a whole is
                // large. Measured against double-double at 1e-21..1e-23 on a boundary point, the
                // modulus test alone left 5-50% of the frame wrong; this leaves none.
                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }

        /// <summary>
        /// |Z + d| - |Z| without catastrophic cancellation. While d has not flipped the sign of the
        /// component (the usual case deep in) this is exactly +-d; on a flip it is the reflected
        /// expression, and d is then comparable to Z, so the pixel rebases right after.
        /// </summary>
        private static double FoldedDelta(double reference, double delta)
        {
            if (reference > 0d)
            {
                return delta > -reference ? delta : -(delta + 2d * reference);
            }

            if (reference < 0d)
            {
                return delta < -reference ? -delta : delta + 2d * reference;
            }

            return System.Math.Abs(delta);
        }
    }
}
