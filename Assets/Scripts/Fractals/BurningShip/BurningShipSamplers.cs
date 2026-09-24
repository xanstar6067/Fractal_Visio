using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// z -> (|Re z| - i|Im z|)^2 + c: x' = x^2 - y^2 + cx, y' = -2|xy| + cy. The absolute values
    /// break the symmetry that lets the Mandelbrot interior tests work, so there is no cheap
    /// early-out here.
    ///
    /// The minus is the WPF engine's orientation, and the picture everyone knows: with the
    /// imaginary axis pointing up, the ship stands on its keel with the masts above. The textbook
    /// (|x| + i|y|)^2 + c is the same set mirrored top to bottom. Views saved before 2026-09-24 used
    /// that one; <see cref="StateCodec.Upgrade"/> mirrors them on load.
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
                zy = cy - 2d * System.Math.Abs(zx * zy);
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

                BurningShipStep.Advance(ref zx, ref zy, xSquared, ySquared, cx, cy);
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom by perturbation. The fold w = (|Re z|, -|Im z|) is not complex-analytic, so the
    /// offset is folded component by component (<see cref="Fold.Delta"/>) and then squared as
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
            BurningShipStep.BuildOrbit(orbit, new DoubleDouble(0d), new DoubleDouble(0d), cx, cy, maxIterations, ReferenceEscape);
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

                BurningShipStep.Perturb(re[referenceIndex], im[referenceIndex], ref dx, ref dy);
                dx += deltaCx;
                dy += deltaCy;
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
                    // Z_0 = 0, so d = z - Z_0 is z itself.
                    dx = fullX;
                    dy = fullY;
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// The Burning Ship step in its three forms - double-double, the reference orbit, and the
    /// perturbed offset - shared with the ship's Julia sets, which iterate the same map with a
    /// fixed c. One place for the fold's sign, which is the one thing that must never differ
    /// between them.
    /// </summary>
    internal static class BurningShipStep
    {
        /// <summary>z &lt;- (|x| - i|y|)^2 + c in double-double, given x^2 and y^2 already squared.</summary>
        public static void Advance(
            ref DoubleDouble zx, ref DoubleDouble zy,
            in DoubleDouble xSquared, in DoubleDouble ySquared,
            in DoubleDouble cx, in DoubleDouble cy)
        {
            var product = DoubleDouble.Abs(DoubleDouble.Multiply(zx, zy));
            zx = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
            zy = DoubleDouble.Subtract(cy, DoubleDouble.Multiply(product, 2d));
        }

        /// <summary>
        /// Fill <paramref name="orbit"/> with the orbit of (<paramref name="startX"/>,
        /// <paramref name="startY"/>) under the map with constant (<paramref name="cx"/>,
        /// <paramref name="cy"/>). Z_0 and Z_1 are always kept; see <see cref="IPerturbationSampler"/>.
        /// </summary>
        public static void BuildOrbit(
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

                Advance(ref zx, ref zy, DoubleDouble.Square(zx), DoubleDouble.Square(zy), cx, cy);
            }
        }

        /// <summary>
        /// d &lt;- fold(Z + d)^2 - fold(Z)^2 for the reference point (<paramref name="zr"/>,
        /// <paramref name="zi"/>), without the constant: the Mandelbrot form adds dc, a Julia set
        /// adds nothing.
        /// </summary>
        public static void Perturb(double zr, double zi, ref double dx, ref double dy)
        {
            // Folded reference W = (|Zr|, -|Zi|) and folded offset fold(Z + d) - fold(Z).
            var wr = System.Math.Abs(zr);
            var wi = -System.Math.Abs(zi);
            var wdx = Fold.Delta(zr, dx);
            var wdy = -Fold.Delta(zi, dy);

            // 2 W w_d + w_d^2
            var nextX = 2d * (wr * wdx - wi * wdy) + wdx * wdx - wdy * wdy;
            dy = 2d * (wr * wdy + wi * wdx) + 2d * wdx * wdy;
            dx = nextX;
        }
    }
}
