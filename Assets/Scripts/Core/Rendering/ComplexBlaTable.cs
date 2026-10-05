using System;

namespace FractalVisio.Core
{
    /// <summary>
    /// Bivariate linear approximation (Zhuoran) for a z^2 + c reference orbit, ported from the WPF
    /// engine's <c>MandelbrotFamilyRenderer.Bla</c>.
    ///
    /// While the offset <c>d</c> is small next to the reference point <c>Z</c>, the perturbation step
    /// <c>d' = 2 Z d + d^2 + dc</c> is nearly linear. Drop <c>d^2</c> and a step becomes
    /// <c>d' = A d + B dc</c>, and linear steps compose. The table keeps a pyramid of them - level
    /// k holds runs of 2^k iterations - so a pixel whose offset is inside a run's validity radius
    /// jumps the whole run at once. It pays off most where the picture is most expensive: points
    /// near a minibrot, whose offsets stay tiny for hundreds of iterations.
    ///
    /// Levels are stored flat and the arrays are reused between renders; see
    /// <see cref="ReferenceOrbit"/> for why nothing here allocates per request.
    /// </summary>
    public sealed class ComplexBlaTable
    {
        /// <summary>
        /// Tolerance for dropping d^2 in one step: r = tol * |A| / 2 = tol * |Z|. At 2^-52 a single
        /// skip agrees with a plain fp64 step to rounding; this is the value the WPF engine was
        /// verified with, kept rather than loosened for speed.
        /// </summary>
        private const double Tolerance = 2.220446049250313e-16;

        private const int MaximumLevels = 30;

        /// <summary>
        /// |x + iy| without squaring the parts. Radii and offsets are kept as lengths, not squared
        /// lengths, for the deep zoom: past about 1e-154 a square is below the double range, a table
        /// of squared radii turned to zeros - every skip refused - and a squared offset to zero, which
        /// would have let any skip through.
        /// </summary>
        public static double Magnitude(double x, double y)
        {
            x = Math.Abs(x);
            y = Math.Abs(y);
            var larger = Math.Max(x, y);
            if (!(larger > 0d))
            {
                return larger;
            }

            var ratio = Math.Min(x, y) / larger;
            return larger * Math.Sqrt(1d + ratio * ratio);
        }

        private double[] ax = Array.Empty<double>();
        private double[] ay = Array.Empty<double>();
        private double[] bx = Array.Empty<double>();
        private double[] by = Array.Empty<double>();
        private double[] radius = Array.Empty<double>();
        private readonly int[] levelOffset = new int[MaximumLevels];
        private readonly int[] levelCount = new int[MaximumLevels];
        private int levels;
        private int maxReferenceIndex;

        /// <summary>
        /// Build from <paramref name="orbit"/>. <paramref name="maxDeltaC"/> is an upper bound on
        /// |dc| over the render. Returns false when the orbit is too short to be worth a table.
        /// </summary>
        public bool Build(ReferenceOrbit orbit, double escapeSquared, double maxDeltaC)
        {
            var length = orbit.Length;
            if (length < 4)
            {
                levels = 0;
                return false;
            }

            var re = orbit.Re;
            var im = orbit.Im;

            var level0 = length - 1;
            var total = 0;
            levels = 0;
            for (var count = level0; ; count = (count + 1) >> 1)
            {
                levelOffset[levels] = total;
                levelCount[levels] = count;
                total += count;
                levels++;
                if (count <= 1 || levels >= MaximumLevels)
                {
                    break;
                }
            }

            EnsureCapacity(total);

            // Level 0: single steps n -> n+1 with A = 2 Z_n, B = 1.
            for (var n = 0; n < level0; n++)
            {
                var zr = re[n];
                var zi = im[n];
                ax[n] = 2d * zr;
                ay[n] = 2d * zi;
                bx[n] = 1d;
                by[n] = 0d;

                var magnitude = zr * zr + zi * zi;
                var next = re[n + 1] * re[n + 1] + im[n + 1] * im[n + 1];

                // A skip may not jump over the pixel's escape: that step has to be taken for real.
                radius[n] = magnitude > escapeSquared || next > escapeSquared ? 0d : Tolerance * Magnitude(zr, zi);
            }

            // Higher levels: merge neighbours, x applied first and then y.
            for (var k = 1; k < levels; k++)
            {
                var previousOffset = levelOffset[k - 1];
                var previousCount = levelCount[k - 1];
                var offset = levelOffset[k];
                var count = levelCount[k];

                for (var j = 0; j < count; j++)
                {
                    var left = previousOffset + 2 * j;
                    var right = left + 1;
                    var target = offset + j;

                    if (2 * j + 1 >= previousCount)
                    {
                        ax[target] = ax[left];
                        ay[target] = ay[left];
                        bx[target] = bx[left];
                        by[target] = by[left];
                        radius[target] = radius[left];
                        continue;
                    }

                    double xAx = ax[left], xAy = ay[left], xBx = bx[left], xBy = by[left], rx = radius[left];
                    double yAx = ax[right], yAy = ay[right], yBx = bx[right], yBy = by[right], ry = radius[right];

                    // A_z = A_y A_x ; B_z = A_y B_x + B_y  (complex)
                    var zAx = yAx * xAx - yAy * xAy;
                    var zAy = yAx * xAy + yAy * xAx;
                    var zBx = yAx * xBx - yAy * xBy + yBx;
                    var zBy = yAx * xBy + yAy * xBx + yBy;

                    double rz;
                    if (rx <= 0d || ry <= 0d ||
                        double.IsNaN(zAx) || double.IsInfinity(zAx) ||
                        double.IsNaN(zAy) || double.IsInfinity(zAy) ||
                        double.IsNaN(zBx) || double.IsInfinity(zBx) ||
                        double.IsNaN(zBy) || double.IsInfinity(zBy))
                    {
                        rz = 0d;
                    }
                    else
                    {
                        // |d_in| <= r_x and |A_x d_in + B_x dc| <= r_y
                        //   =>  r_z = min(r_x, max(0, (r_y - |B_x| dcmax) / |A_x|))
                        var xA = Magnitude(xAx, xAy);
                        var xB = Magnitude(xBx, xBy);
                        var bound = xA > 0d ? (ry - xB * maxDeltaC) / xA : 0d;
                        rz = Math.Min(rx, Math.Max(0d, bound));
                    }

                    ax[target] = zAx;
                    ay[target] = zAy;
                    bx[target] = zBx;
                    by[target] = zBy;
                    radius[target] = rz;
                }
            }

            maxReferenceIndex = length - 1;
            return true;
        }

        /// <summary>
        /// Longest valid skip from <paramref name="referenceIndex"/> for an offset of size
        /// <paramref name="deltaMagnitude"/> (<see cref="Magnitude"/>), no longer than
        /// <paramref name="iterationBudget"/>.
        /// False when only a single step would apply - that is cheaper taken for real.
        /// </summary>
        public bool TryLookup(
            int referenceIndex,
            double deltaMagnitude,
            int iterationBudget,
            out double aX,
            out double aY,
            out double bX,
            out double bY,
            out int steps)
        {
            aX = 1d;
            aY = 0d;
            bX = 0d;
            bY = 0d;
            steps = 0;

            for (var k = 0; k < levels; k++)
            {
                var span = 1 << k;
                if ((referenceIndex & (span - 1)) != 0)
                {
                    break; // not aligned to a run of this length
                }

                var j = referenceIndex >> k;
                if (j >= levelCount[k])
                {
                    break;
                }

                var index = levelOffset[k] + j;
                var limit = radius[index];
                if (limit <= 0d || deltaMagnitude >= limit ||
                    span > iterationBudget || referenceIndex + span > maxReferenceIndex)
                {
                    break;
                }

                aX = ax[index];
                aY = ay[index];
                bX = bx[index];
                bY = by[index];
                steps = span;
            }

            return steps >= 2;
        }

        private void EnsureCapacity(int total)
        {
            if (ax.Length >= total)
            {
                return;
            }

            ax = new double[total];
            ay = new double[total];
            bx = new double[total];
            by = new double[total];
            radius = new double[total];
        }
    }
}
