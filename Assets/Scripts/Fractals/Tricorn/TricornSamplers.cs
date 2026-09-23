using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Tricorn (Mandelbar): z -> conj(z)^2 + c. Conjugation turns the map anti-holomorphic
    /// and gives the set its three-fold symmetry; on the real axis it is the Mandelbrot iteration.
    /// </summary>
    public readonly struct TricornSamplerD : IEscapeSamplerD
    {
        /// <summary>Bailout on the squared modulus, as for the Mandelbrot and for the same reason.</summary>
        public const double Bailout = 65536d;

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

                // conj(z)^2 = x^2 - y^2 - 2ixy
                var nextX = zx * zx - zy * zy + cx;
                zy = -2d * zx * zy + cy;
                zx = nextX;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, Bailout);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>Same iteration in double-double: the reference the perturbation sampler is checked against.</summary>
    public readonly struct TricornSamplerDD : IEscapeSamplerDD
    {
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
                if (squared > TricornSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, TricornSamplerD.Bailout);
                }

                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), -2d), cy);
                zx = nextX;
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation. With z = Z + d, conj(z)^2 - conj(Z)^2 = conj(2 Z d + d^2), so the
    /// offset takes the Mandelbrot step and is then conjugated:
    /// <c>d' = conj(2 Z d + d^2) + dc</c>. Conjugation keeps every modulus, so the ordinary
    /// modulus rebasing tests hold unchanged - unlike the folded variants, nothing flips sign
    /// component by component. No BLA: the linear part conj(2 Z d) is not a complex product.
    /// </summary>
    public readonly struct TricornPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

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
                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), -2d), cy);
                zx = nextX;
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

                // d <- conj(2 Z d + d^2) + dc
                var zr = re[referenceIndex];
                var zi = im[referenceIndex];
                var stepX = 2d * (zr * dx - zi * dy) + dx * dx - dy * dy;
                var stepY = 2d * (zr * dy + zi * dx) + 2d * dx * dy;
                dx = stepX + deltaCx;
                dy = -stepY + deltaCy;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > TricornSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, TricornSamplerD.Bailout);
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
