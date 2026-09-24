using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// z -> z^2 + c in fp64. A struct on purpose - see <see cref="IEscapeSamplerD"/>.
    /// </summary>
    public readonly struct MandelbrotSamplerD : IEscapeSamplerD
    {
        /// <summary>
        /// Bailout on the squared modulus. Far above the 4 that decides membership, because the
        /// smooth escape count is only a good approximation once the orbit is well clear of the
        /// set; the cost is a couple of extra iterations per escaping pixel.
        /// </summary>
        public const double Bailout = 65536d;

        public float Sample(double cx, double cy, int maxIterations, CancellationToken token)
        {
            // Main cardioid and period-2 bulb: the two large interior regions, worth testing for
            // because points inside them would otherwise run the full iteration budget.
            var x = cx - 0.25d;
            var y2 = cy * cy;
            var q = x * x + y2;
            if (q * (q + x) <= 0.25d * y2 || (cx + 1d) * (cx + 1d) + y2 <= 0.0625d)
            {
                return EscapeMath.Interior;
            }

            var zx = 0d;
            var zy = 0d;
            var iteration = 0;
            var squared = 0d;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                var nextX = zx * zx - zy * zy + cx;
                zy = 2d * zx * zy + cy;
                zx = nextX;
                iteration++;

                squared = zx * zx + zy * zy;
                if (squared > Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, Bailout);
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>Same iteration in double-double, for scales fp64 can no longer separate.</summary>
    public readonly struct MandelbrotSamplerDD : IEscapeSamplerDD
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
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout);
                }

                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
                zx = nextX;
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation, with rebasing and BLA. Ported from the WPF engine's
    /// <c>DeepZoomPixel</c>, reduced to smooth colouring. See <see cref="IPerturbationSampler"/>.
    /// </summary>
    public readonly struct MandelbrotPerturbationSampler : IPerturbationSampler
    {
        /// <summary>
        /// Escape radius of the reference, squared. Far past the pixel bailout so the orbit stays
        /// usable as long as possible; pixels that outlive it are rebased.
        /// </summary>
        private const double ReferenceEscape = 1e18d;

        /// <summary>Pauldelbrot's criterion: |z|^2 below this fraction of |Z|^2 means rebase.</summary>
        private const double GlitchToleranceSquared = 1e-6d;

        public void BuildReference(ReferenceOrbit orbit, in DoubleDouble cx, in DoubleDouble cy, int maxIterations, double maxDeltaC)
        {
            QuadraticOrbit.Build(orbit, new DoubleDouble(0d), new DoubleDouble(0d), cx, cy, maxIterations, ReferenceEscape);
            orbit.BuildQuadraticBla(MandelbrotSamplerD.Bailout, maxDeltaC);
        }

        public float Sample(ReferenceOrbit orbit, double deltaCx, double deltaCy, int maxIterations, CancellationToken token)
        {
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;
            var bla = orbit.HasBla ? orbit.Bla : null;

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

                if (bla != null &&
                    bla.TryLookup(referenceIndex, dx * dx + dy * dy, maxIterations - iteration,
                        out var aX, out var aY, out var bX, out var bY, out var steps))
                {
                    // d <- A d + B dc, skipping `steps` iterations at once.
                    var skippedX = aX * dx - aY * dy + bX * deltaCx - bY * deltaCy;
                    var skippedY = aX * dy + aY * dx + bX * deltaCy + bY * deltaCx;
                    dx = skippedX;
                    dy = skippedY;
                    referenceIndex += steps;
                    iteration += steps;
                }
                else
                {
                    // d <- 2 Z d + d^2 + dc
                    var zr = re[referenceIndex];
                    var zi = im[referenceIndex];
                    var nextX = 2d * (zr * dx - zi * dy) + dx * dx - dy * dy + deltaCx;
                    var nextY = 2d * (zr * dy + zi * dx) + 2d * dx * dy + deltaCy;
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
                    // Z_0 = 0 for the Mandelbrot, so d = z - Z_0 is z itself.
                    dx = fullX - re[0];
                    dy = fullY - im[0];
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// A z^2 + c orbit in double-double, stored as doubles: the Mandelbrot reference (start 0, c the
    /// centre) and both orbits of a Julia set (start the centre or 0, c the constant).
    /// </summary>
    internal static class QuadraticOrbit
    {
        /// <summary>
        /// Fill <paramref name="orbit"/> with the orbit of (<paramref name="startX"/>,
        /// <paramref name="startY"/>) under z^2 + (<paramref name="cx"/>, <paramref name="cy"/>),
        /// until it passes <paramref name="referenceEscape"/> on the squared modulus. Z_0 and Z_1 are
        /// always kept; see <see cref="IPerturbationSampler"/>.
        /// </summary>
        public static void Build(
            ReferenceOrbit orbit,
            DoubleDouble startX, DoubleDouble startY,
            in DoubleDouble cx, in DoubleDouble cy,
            int maxIterations, double referenceEscape)
        {
            orbit.Begin(maxIterations);

            var zx = startX;
            var zy = startY;

            for (var index = 0; index <= maxIterations; index++)
            {
                var real = zx.ToDouble();
                var imaginary = zy.ToDouble();
                if (!orbit.Append(real, imaginary))
                {
                    break;
                }

                var magnitude = real * real + imaginary * imaginary;
                if (index >= 1 && !(magnitude <= referenceEscape))
                {
                    break;
                }

                var xSquared = DoubleDouble.Square(zx);
                var ySquared = DoubleDouble.Square(zy);
                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
                zx = nextX;
            }
        }
    }
}
