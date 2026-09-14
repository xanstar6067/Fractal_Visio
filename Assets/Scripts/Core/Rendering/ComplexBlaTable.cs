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

        private double[] ax = Array.Empty<double>();
        private double[] ay = Array.Empty<double>();
        private double[] bx = Array.Empty<double>();
        private double[] by = Array.Empty<double>();
        private double[] r2 = Array.Empty<double>();
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
                if (magnitude > escapeSquared || next > escapeSquared)
                {
                    r2[n] = 0d;
                }
                else
                {
                    var r = Tolerance * Math.Sqrt(magnitude);
                    r2[n] = r * r;
                }
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
                        r2[target] = r2[left];
                        continue;
                    }

                    double xAx = ax[left], xAy = ay[left], xBx = bx[left], xBy = by[left], xR2 = r2[left];
                    double yAx = ax[right], yAy = ay[right], yBx = bx[right], yBy = by[right], yR2 = r2[right];

                    // A_z = A_y A_x ; B_z = A_y B_x + B_y  (complex)
                    var zAx = yAx * xAx - yAy * xAy;
                    var zAy = yAx * xAy + yAy * xAx;
                    var zBx = yAx * xBx - yAy * xBy + yBx;
                    var zBy = yAx * xBy + yAy * xBx + yBy;

                    double zR2;
                    if (xR2 <= 0d || yR2 <= 0d ||
                        double.IsNaN(zAx) || double.IsInfinity(zAx) ||
                        double.IsNaN(zAy) || double.IsInfinity(zAy) ||
                        double.IsNaN(zBx) || double.IsInfinity(zBx) ||
                        double.IsNaN(zBy) || double.IsInfinity(zBy))
                    {
                        zR2 = 0d;
                    }
                    else
                    {
                        // |d_in| <= r_x and |A_x d_in + B_x dc| <= r_y
                        //   =>  r_z = min(r_x, max(0, (r_y - |B_x| dcmax) / |A_x|))
                        var xA = Math.Sqrt(xAx * xAx + xAy * xAy);
                        var xB = Math.Sqrt(xBx * xBx + xBy * xBy);
                        var rx = Math.Sqrt(xR2);
                        var ry = Math.Sqrt(yR2);
                        var bound = xA > 0d ? (ry - xB * maxDeltaC) / xA : 0d;
                        var rz = Math.Min(rx, Math.Max(0d, bound));
                        zR2 = rz * rz;
                    }

                    ax[target] = zAx;
                    ay[target] = zAy;
                    bx[target] = zBx;
                    by[target] = zBy;
                    r2[target] = zR2;
                }
            }

            maxReferenceIndex = length - 1;
            return true;
        }

        /// <summary>
        /// Longest valid skip from <paramref name="referenceIndex"/> for an offset of squared size
        /// <paramref name="deltaMagnitudeSquared"/>, no longer than <paramref name="iterationBudget"/>.
        /// False when only a single step would apply - that is cheaper taken for real.
        /// </summary>
        public bool TryLookup(
            int referenceIndex,
            double deltaMagnitudeSquared,
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
                var radiusSquared = r2[index];
                if (radiusSquared <= 0d || deltaMagnitudeSquared >= radiusSquared ||
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
            r2 = new double[total];
        }
    }
}
