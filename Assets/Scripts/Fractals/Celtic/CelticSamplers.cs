using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Celtic Mandelbrot: z -> |Re(z^2)| + i Im(z^2) + c. The fold acts on the square, not on z,
    /// which bends the Mandelbrot's bulbs into knotwork.
    /// </summary>
    public readonly struct CelticSamplerD : IEscapeSamplerD
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

                var nextX = System.Math.Abs(zx * zx - zy * zy) + cx;
                zy = 2d * zx * zy + cy;
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

        public float SampleWithSlope(
            double cx, double cy, int maxIterations, CancellationToken token, out double slopeX, out double slopeY)
        {
            slopeX = 0d;
            slopeY = 0d;

            var zx = 0d;
            var zy = 0d;
            var j00 = 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = 0d;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                // The fold is on Re(z^2): its row of the Jacobian turns with the sign of x^2 - y^2.
                EscapeMath.QuadraticJacobianStep(
                    zx, zy, JacobianSign(zx, zy), 1d, 1d, ref j00, ref j01, ref j10, ref j11);

                var nextX = System.Math.Abs(zx * zx - zy * zy) + cx;
                zy = 2d * zx * zy + cy;
                zx = nextX;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > Bailout)
                {
                    EscapeMath.JacobianSlope(zx, zy, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, squared, Bailout);
                }
            }

            return EscapeMath.Interior;
        }

        /// <summary>Sign of Re(z^2) = x^2 - y^2: how |Re(z^2)| turns the real row of the Jacobian.</summary>
        internal static double JacobianSign(double x, double y) => x * x - y * y >= 0d ? 1d : -1d;
    }

    /// <summary>Same iteration in double-double: the reference the perturbation sampler is checked against.</summary>
    public readonly struct CelticSamplerDD : IEscapeSamplerDD
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
                if (squared > CelticSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, CelticSamplerD.Bailout);
                }

                var nextX = DoubleDouble.Add(DoubleDouble.Abs(DoubleDouble.Subtract(xSquared, ySquared)), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
                zx = nextX;
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation, ported from the WPF engine's <c>DeepZoomPixelReflected</c>
    /// (Celtic branch). The offset of the square, <c>du = 2(Zr dr - Zi di) + dr^2 - di^2</c> and
    /// <c>dv = 2(Zr di + Zi dr) + 2 dr di</c>, is taken first; only its real part goes through the
    /// fold, <see cref="Fold.Delta"/> of <c>U = Re(Z^2)</c>, which is exact on either side of a
    /// sign flip - so the usual modulus rebasing is enough, as in WPF.
    ///
    /// Checked against double-double at 1e-13..1e-21 on grids around boundary points: no
    /// difference on ordinary boundaries. The fold also makes regions of bounded chaotic orbits,
    /// and there plain fp64 already disagrees with double-double at 1e-4 - the result is decided
    /// by rounding noise in any arithmetic. A per-component rebase test like the Burning Ship's
    /// (when |Re z^2| is below its offset) was tried and changed nothing measurable there.
    /// </summary>
    public readonly struct CelticPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        public void BuildReference(
            ReferenceOrbit orbit, in HighPrecision centerX, in HighPrecision centerY, int maxIterations, double maxDeltaC,
            CancellationToken token)
        {
            // Double-double is as deep as this fractal goes (PrecisionTier.Arbitrary is not declared).
            var cx = DoubleDouble.FromHighPrecision(centerX);
            var cy = DoubleDouble.FromHighPrecision(centerY);
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
                var nextX = DoubleDouble.Add(DoubleDouble.Abs(DoubleDouble.Subtract(xSquared, ySquared)), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
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

                var zr = re[referenceIndex];
                var zi = im[referenceIndex];
                var deltaU = 2d * (zr * dx - zi * dy) + dx * dx - dy * dy;
                var deltaV = 2d * (zr * dy + zi * dx) + 2d * dx * dy;
                dx = Fold.Delta(zr * zr - zi * zi, deltaU) + deltaCx;
                dy = deltaV + deltaCy;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > CelticSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, CelticSamplerD.Bailout);
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

        public float SampleWithSlope(
            ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token,
            out double slopeX, out double slopeY)
        {
            slopeX = 0d;
            slopeY = 0d;

            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;

            var dx = 0d;
            var dy = 0d;
            var j00 = 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = 0d;
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
                EscapeMath.QuadraticJacobianStep(
                    fx, fy, CelticSamplerD.JacobianSign(fx, fy), 1d, 1d, ref j00, ref j01, ref j10, ref j11);

                var deltaU = 2d * (zr * dx - zi * dy) + dx * dx - dy * dy;
                var deltaV = 2d * (zr * dy + zi * dx) + 2d * dx * dy;
                dx = Fold.Delta(zr * zr - zi * zi, deltaU) + deltaCx;
                dy = deltaV + deltaCy;
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > CelticSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(fullX, fullY, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, magnitude, CelticSamplerD.Bailout);
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
