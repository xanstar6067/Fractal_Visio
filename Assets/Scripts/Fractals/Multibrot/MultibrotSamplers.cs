using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Multibrot: z -> z^p + c for a whole power p. The power is a field rather than a type
    /// parameter; one integer loop per iteration costs nothing next to the multiplications it runs.
    /// </summary>
    public readonly struct MultibrotSamplerD : IEscapeSamplerD
    {
        /// <summary>Bailout on the squared modulus, as for the Mandelbrot and for the same reason.</summary>
        public const double Bailout = 65536d;

        private readonly int power;

        public MultibrotSamplerD(int power)
        {
            this.power = MultibrotDefinition.ClampPower(power);
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

                var px = zx;
                var py = zy;
                for (var k = 1; k < power; k++)
                {
                    var next = px * zx - py * zy;
                    py = px * zy + py * zx;
                    px = next;
                }

                zx = px + cx;
                zy = py + cy;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, Bailout, power);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>Same iteration in double-double: the reference the perturbation sampler is checked against.</summary>
    public readonly struct MultibrotSamplerDD : IEscapeSamplerDD
    {
        private readonly int power;

        public MultibrotSamplerDD(int power)
        {
            this.power = MultibrotDefinition.ClampPower(power);
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

                var squared = DoubleDouble.Add(DoubleDouble.Square(zx), DoubleDouble.Square(zy)).ToDouble();
                if (squared > MultibrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MultibrotSamplerD.Bailout, power);
                }

                MultibrotDefinition.Power(zx, zy, power, out var px, out var py);
                zx = DoubleDouble.Add(px, cx);
                zy = DoubleDouble.Add(py, cy);
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation. With z = Z + d the offset steps as
    /// <c>d' = z^p - Z^p + dc = d * T + dc</c>, where <c>T = sum z^j Z^(p-1-j)</c> over
    /// j = 0..p-1. T is summed directly from the pixel's z = Z + d: its terms all point the same
    /// way while d is small, so it carries no cancellation, and its relative error of one rounding
    /// survives the multiplication by d - which is all perturbation needs. Where that stops being
    /// true (z small next to d) the usual rebasing takes over.
    /// </summary>
    public readonly struct MultibrotPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly int power;

        public MultibrotPerturbationSampler(int power)
        {
            this.power = MultibrotDefinition.ClampPower(power);
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

                MultibrotDefinition.Power(zx, zy, power, out var px, out var py);
                zx = DoubleDouble.Add(px, cx);
                zy = DoubleDouble.Add(py, cy);
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
                var fx = zr + dx;
                var fy = zi + dy;

                // T_m = Z^m + z T_(m-1), T_0 = 1: after p-1 steps T = sum z^j Z^(p-1-j).
                var tx = 1d;
                var ty = 0d;
                var wx = 1d;
                var wy = 0d;
                for (var m = 1; m < power; m++)
                {
                    var nextWx = wx * zr - wy * zi;
                    wy = wx * zi + wy * zr;
                    wx = nextWx;

                    var productX = fx * tx - fy * ty;
                    var productY = fx * ty + fy * tx;
                    tx = wx + productX;
                    ty = wy + productY;
                }

                var nextX = dx * tx - dy * ty + deltaCx;
                var nextY = dx * ty + dy * tx + deltaCy;
                dx = nextX;
                dy = nextY;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MultibrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, MultibrotSamplerD.Bailout, power);
                }

                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }
}
