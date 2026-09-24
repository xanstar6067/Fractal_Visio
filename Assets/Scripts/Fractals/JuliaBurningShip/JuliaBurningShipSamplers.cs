using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Burning Ship map from the pixel with c fixed: the ship's own Julia sets. Same fold and
    /// sign as <see cref="BurningShipSamplerD"/> - a C picked on the ship's map must mean the same
    /// map here.
    /// </summary>
    public readonly struct JuliaBurningShipSamplerD : IEscapeSamplerD
    {
        private readonly double constantX;
        private readonly double constantY;

        public JuliaBurningShipSamplerD(double constantX, double constantY)
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
                zy = constantY - 2d * System.Math.Abs(zx * zy);
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

    public readonly struct JuliaBurningShipSamplerDD : IEscapeSamplerDD
    {
        private readonly DoubleDouble constantX;
        private readonly DoubleDouble constantY;

        public JuliaBurningShipSamplerDD(double constantX, double constantY)
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

                BurningShipStep.Advance(ref zx, ref zy, xSquared, ySquared, constantX, constantY);
                iteration++;
            }

            return EscapeMath.Interior;
        }
    }

    /// <summary>
    /// Deep zoom for the ship's Julia sets: the folded offset of <see cref="BurningShipPerturbationSampler"/>
    /// without dc, rebased per component - as the ship needs - onto the orbit of 0, as a Julia set
    /// needs (<see cref="JuliaPerturbationSampler"/>). No BLA, like the ship.
    /// </summary>
    public readonly struct JuliaBurningShipPerturbationSampler : IPerturbationSampler
    {
        private const double ReferenceEscape = 1e18d;
        private const double GlitchToleranceSquared = 1e-6d;

        private readonly double constantX;
        private readonly double constantY;

        public JuliaBurningShipPerturbationSampler(double constantX, double constantY)
        {
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public void BuildReference(ReferenceOrbit orbit, in DoubleDouble cx, in DoubleDouble cy, int maxIterations, double maxDeltaC)
        {
            var constantReal = new DoubleDouble(constantX);
            var constantImaginary = new DoubleDouble(constantY);
            BurningShipStep.BuildOrbit(orbit, cx, cy, constantReal, constantImaginary, maxIterations, ReferenceEscape);
            BurningShipStep.BuildOrbit(
                orbit.Secondary, new DoubleDouble(0d), new DoubleDouble(0d), constantReal, constantImaginary,
                maxIterations, ReferenceEscape);
        }

        public float Sample(ReferenceOrbit orbit, double deltaX, double deltaY, int maxIterations, CancellationToken token)
        {
            var critical = orbit.Secondary;
            var re = orbit.Re;
            var im = orbit.Im;
            var length = orbit.Length;

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

                BurningShipStep.Perturb(re[referenceIndex], im[referenceIndex], ref dx, ref dy);
                referenceIndex++;
                iteration++;

                var referenceX = referenceIndex < length ? re[referenceIndex] : 0d;
                var referenceY = referenceIndex < length ? im[referenceIndex] : 0d;
                var fullX = referenceX + dx;
                var fullY = referenceY + dy;
                var magnitude = fullX * fullX + fullY * fullY;

                if (magnitude > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, magnitude, MandelbrotSamplerD.Bailout);
                }

                // Per component, for the reason given in BurningShipPerturbationSampler.
                if (referenceIndex >= length - 1 ||
                    magnitude < dx * dx + dy * dy ||
                    System.Math.Abs(fullX) < System.Math.Abs(dx) ||
                    System.Math.Abs(fullY) < System.Math.Abs(dy) ||
                    magnitude < GlitchToleranceSquared * (referenceX * referenceX + referenceY * referenceY))
                {
                    re = critical.Re;
                    im = critical.Im;
                    length = critical.Length;
                    dx = fullX;
                    dy = fullY;
                    referenceIndex = 0;
                }
            }

            return EscapeMath.Interior;
        }
    }
}
