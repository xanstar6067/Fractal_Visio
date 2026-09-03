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
                    token.ThrowIfCancellationRequested();
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
                    token.ThrowIfCancellationRequested();
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
}
