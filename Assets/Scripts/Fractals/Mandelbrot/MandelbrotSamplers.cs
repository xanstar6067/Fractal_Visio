using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// z -> z^2 + c in fp64. A struct on purpose - see <see cref="IEscapeSamplerD"/>.
    /// </summary>
    public readonly struct MandelbrotSamplerD : IEscapeSamplerD
    {
        public int Sample(double cx, double cy, int maxIterations, CancellationToken token)
        {
            // Main cardioid and period-2 bulb: the two large interior regions, worth testing for
            // because points inside them would otherwise run the full iteration budget.
            var x = cx - 0.25d;
            var y2 = cy * cy;
            var q = x * x + y2;
            if (q * (q + x) <= 0.25d * y2 || (cx + 1d) * (cx + 1d) + y2 <= 0.0625d)
            {
                return maxIterations;
            }

            var zx = 0d;
            var zy = 0d;
            var iteration = 0;
            while (iteration < maxIterations && zx * zx + zy * zy <= 4d)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                var nextX = zx * zx - zy * zy + cx;
                zy = 2d * zx * zy + cy;
                zx = nextX;
                iteration++;
            }

            return iteration;
        }
    }

    /// <summary>Same iteration in double-double, for scales fp64 can no longer separate.</summary>
    public readonly struct MandelbrotSamplerDD : IEscapeSamplerDD
    {
        public int Sample(in DoubleDouble cx, in DoubleDouble cy, int maxIterations, CancellationToken token)
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
                if (DoubleDouble.Add(xSquared, ySquared).ToDouble() > 4d)
                {
                    break;
                }

                var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
                zx = nextX;
                iteration++;
            }

            return iteration;
        }
    }
}
