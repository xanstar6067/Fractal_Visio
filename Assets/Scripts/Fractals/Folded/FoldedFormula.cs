using System;
using System.Runtime.CompilerServices;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>Which factors of a <see cref="FoldedFormula"/> are taken by absolute value.</summary>
    [Flags]
    public enum Folds
    {
        None = 0,

        /// <summary>|x| in place of x.</summary>
        X = 1,

        /// <summary>|y| in place of y.</summary>
        Y = 2,

        /// <summary>|A|: the polynomial of the real part.</summary>
        A = 4,

        /// <summary>|C|: the polynomial of the imaginary part.</summary>
        C = 8
    }

    /// <summary>
    /// One map of the "folded polynomial" family the WPF version grew to 61 members (the Kalles
    /// Fraktaler formulas: Burning Ship, Buffalo, Celtic, Mandelbar, perpendicular, partial and quasi
    /// variants of degree 2 to 5). WPF interprets each as a graph of additions, products and absolute
    /// values (<c>FoldedPolynomialProgram</c>); here every one of them is the same shape with
    /// different flags, so one sampler, one shader and one perturbation kernel draw them all.
    ///
    /// With z = x + iy, z^p splits into real factors that are even in x and y:
    /// <list type="bullet">
    /// <item>odd p (3, 5): z^p = x A + i y C, with A = Re(z^p) / x and C = Im(z^p) / y;</item>
    /// <item>even p (2, 4): z^p = A + i p x y C, with A = Re(z^p), and C = x^2 - y^2 for p = 4, 1 for p = 2.</item>
    /// </list>
    /// A family member takes some of the factors x, y, A, C by absolute value (<see cref="Folds"/>),
    /// may negate the imaginary part, and - two quasi-perpendicular odd ones - swaps which product is
    /// real. The step is then z' = (P, s Q) or, swapped, (Q, s P), where for odd p P = F(x) F(A),
    /// Q = F(y) F(C), and for even p P = F(A), Q = p F(x) F(y) F(C). Every WPF formula reduces to
    /// this: a fold of a product is the product of the folds, and a fold of z before the power is a
    /// fold of x or y. <c>docs/ARCHITECTURE.md</c> §5.12 has the table.
    ///
    /// A and C are kept as coefficients over x^4, x^2 y^2, y^4, x^2, y^2 and 1, which is also exactly
    /// what the shader receives: the CPU and the GPU evaluate the same expression.
    /// </summary>
    public readonly struct FoldedFormula
    {
        // A = a40 x^4 + a22 x^2 y^2 + a04 y^4 + a20 x^2 + a02 y^2 + a00, C likewise.
        public readonly double A40, A22, A04, A20, A02, A00;
        public readonly double C40, C22, C04, C20, C02, C00;

        public readonly bool FoldX, FoldY, FoldA, FoldC;

        /// <summary>Odd degree: P = F(x) F(A), Q = F(y) F(C). Even: P = F(A), Q = F(x) F(y) F(C).</summary>
        public readonly bool Odd;

        /// <summary>The real part is Q and the imaginary part s P.</summary>
        public readonly bool Swap;

        /// <summary>s times the even degree's factor p (1 for odd degrees): what multiplies the imaginary part.</summary>
        public readonly double ImaginaryFactor;

        /// <summary>The degree: the base of the smooth escape count.</summary>
        public readonly int Degree;

        private FoldedFormula(
            int degree, Folds folds, bool negate, bool swap,
            double a40, double a22, double a04, double a20, double a02, double a00,
            double c40, double c22, double c04, double c20, double c02, double c00)
        {
            Degree = degree;
            Odd = (degree & 1) == 1;
            FoldX = (folds & Folds.X) != 0;
            FoldY = (folds & Folds.Y) != 0;
            FoldA = (folds & Folds.A) != 0;
            FoldC = (folds & Folds.C) != 0;
            Swap = swap && Odd;
            ImaginaryFactor = (negate ? -1d : 1d) * (Odd ? 1d : degree);
            A40 = a40;
            A22 = a22;
            A04 = a04;
            A20 = a20;
            A02 = a02;
            A00 = a00;
            C40 = c40;
            C22 = c22;
            C04 = c04;
            C20 = c20;
            C02 = c02;
            C00 = c00;
        }

        /// <summary>
        /// A member of degree <paramref name="degree"/> (2 to 5) with the given factors folded;
        /// <paramref name="negate"/> flips the imaginary part's sign, <paramref name="swap"/> (odd
        /// degrees only) makes the y-product the real part.
        /// </summary>
        public static FoldedFormula Of(int degree, Folds folds, bool negate = false, bool swap = false)
        {
            switch (degree)
            {
                case 2:
                    // z^2 = (x^2 - y^2) + i 2 x y
                    return new FoldedFormula(2, folds, negate, false, 0d, 0d, 0d, 1d, -1d, 0d, 0d, 0d, 0d, 0d, 0d, 1d);
                case 3:
                    // z^3 = x (x^2 - 3y^2) + i y (3x^2 - y^2)
                    return new FoldedFormula(3, folds, negate, swap, 0d, 0d, 0d, 1d, -3d, 0d, 0d, 0d, 0d, 3d, -1d, 0d);
                case 4:
                    // z^4 = (x^4 - 6x^2y^2 + y^4) + i 4 x y (x^2 - y^2)
                    return new FoldedFormula(4, folds, negate, false, 1d, -6d, 1d, 0d, 0d, 0d, 0d, 0d, 0d, 1d, -1d, 0d);
                case 5:
                    // z^5 = x (x^4 - 10x^2y^2 + 5y^4) + i y (5x^4 - 10x^2y^2 + y^4)
                    return new FoldedFormula(5, folds, negate, swap, 1d, -10d, 5d, 0d, 0d, 0d, 5d, -10d, 1d, 0d, 0d, 0d);
                default:
                    throw new ArgumentOutOfRangeException(nameof(degree), degree, "Folded formulas have degree 2 to 5.");
            }
        }

        /// <summary>z^p with the folds, without the constant: (x, y) -> (re, im).</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Step(double x, double y, out double re, out double im)
        {
            var x2 = x * x;
            var y2 = y * y;
            var x4 = x2 * x2;
            var xy = x2 * y2;
            var y4 = y2 * y2;
            var a = A40 * x4 + A22 * xy + A04 * y4 + A20 * x2 + A02 * y2 + A00;
            var c = C40 * x4 + C22 * xy + C04 * y4 + C20 * x2 + C02 * y2 + C00;

            var fx = FoldX ? Math.Abs(x) : x;
            var fy = FoldY ? Math.Abs(y) : y;
            var fa = FoldA ? Math.Abs(a) : a;
            var fc = FoldC ? Math.Abs(c) : c;

            double p;
            double q;
            if (Odd)
            {
                p = fx * fa;
                q = fy * fc;
            }
            else
            {
                p = fa;
                q = fx * fy * fc;
            }

            if (Swap)
            {
                re = q;
                im = ImaginaryFactor * p;
            }
            else
            {
                re = p;
                im = ImaginaryFactor * q;
            }
        }

        /// <summary>
        /// The step's own derivative, <c>[[d re/dx, d re/dy], [d im/dx, d im/dy]]</c> at (x, y): what
        /// the relief multiplies the orbit's Jacobian by. A fold has no derivative on its line; either
        /// side gives a direction, which is all the relief needs.
        /// </summary>
        public void StepJacobian(double x, double y, out double s00, out double s01, out double s10, out double s11)
        {
            var x2 = x * x;
            var y2 = y * y;
            var x4 = x2 * x2;
            var xy = x2 * y2;
            var y4 = y2 * y2;
            var a = A40 * x4 + A22 * xy + A04 * y4 + A20 * x2 + A02 * y2 + A00;
            var c = C40 * x4 + C22 * xy + C04 * y4 + C20 * x2 + C02 * y2 + C00;

            var sx = FoldX && x < 0d ? -1d : 1d;
            var sy = FoldY && y < 0d ? -1d : 1d;
            var sa = FoldA && a < 0d ? -1d : 1d;
            var sc = FoldC && c < 0d ? -1d : 1d;
            var fx = sx * x;
            var fy = sy * y;
            var fa = sa * a;
            var fc = sc * c;

            // Gradients of A and C.
            var ax = sa * (4d * A40 * x * x2 + 2d * A22 * x * y2 + 2d * A20 * x);
            var ay = sa * (2d * A22 * x2 * y + 4d * A04 * y * y2 + 2d * A02 * y);
            var cx = sc * (4d * C40 * x * x2 + 2d * C22 * x * y2 + 2d * C20 * x);
            var cy = sc * (2d * C22 * x2 * y + 4d * C04 * y * y2 + 2d * C02 * y);

            double px, py, qx, qy;
            if (Odd)
            {
                // P = F(x) F(A), Q = F(y) F(C)
                px = sx * fa + fx * ax;
                py = fx * ay;
                qx = fy * cx;
                qy = sy * fc + fy * cy;
            }
            else
            {
                // P = F(A), Q = F(x) F(y) F(C)
                px = ax;
                py = ay;
                qx = sx * fy * fc + fx * fy * cx;
                qy = fx * sy * fc + fx * fy * cy;
            }

            if (Swap)
            {
                s00 = qx;
                s01 = qy;
                s10 = ImaginaryFactor * px;
                s11 = ImaginaryFactor * py;
            }
            else
            {
                s00 = px;
                s01 = py;
                s10 = ImaginaryFactor * qx;
                s11 = ImaginaryFactor * qy;
            }
        }

        /// <summary>
        /// One step of the relief derivative: J &lt;- S J + identity I, with S the step's Jacobian at
        /// z before the step. <paramref name="identity"/> is 1 when c is the pixel, 0 for a Julia set.
        /// </summary>
        public void JacobianStep(
            double x, double y, double identity, ref double j00, ref double j01, ref double j10, ref double j11)
        {
            StepJacobian(x, y, out var s00, out var s01, out var s10, out var s11);
            var n00 = s00 * j00 + s01 * j10 + identity;
            var n01 = s00 * j01 + s01 * j11;
            var n10 = s10 * j00 + s11 * j10;
            var n11 = s10 * j01 + s11 * j11 + identity;
            j00 = n00;
            j01 = n01;
            j10 = n10;
            j11 = n11;
        }

        /// <summary>The step in double-double, for reference orbits and the exact sampler.</summary>
        public void Step(in DoubleDouble x, in DoubleDouble y, out DoubleDouble re, out DoubleDouble im)
        {
            var x2 = DoubleDouble.Square(x);
            var y2 = DoubleDouble.Square(y);
            var x4 = DoubleDouble.Square(x2);
            var xy = DoubleDouble.Multiply(x2, y2);
            var y4 = DoubleDouble.Square(y2);
            var a = Polynomial(x4, xy, y4, x2, y2, A40, A22, A04, A20, A02, A00);
            var c = Polynomial(x4, xy, y4, x2, y2, C40, C22, C04, C20, C02, C00);

            var fx = FoldX ? DoubleDouble.Abs(x) : x;
            var fy = FoldY ? DoubleDouble.Abs(y) : y;
            var fa = FoldA ? DoubleDouble.Abs(a) : a;
            var fc = FoldC ? DoubleDouble.Abs(c) : c;

            DoubleDouble p;
            DoubleDouble q;
            if (Odd)
            {
                p = DoubleDouble.Multiply(fx, fa);
                q = DoubleDouble.Multiply(fy, fc);
            }
            else
            {
                p = fa;
                q = DoubleDouble.Multiply(DoubleDouble.Multiply(fx, fy), fc);
            }

            if (Swap)
            {
                re = q;
                im = DoubleDouble.Multiply(p, ImaginaryFactor);
            }
            else
            {
                re = p;
                im = DoubleDouble.Multiply(q, ImaginaryFactor);
            }
        }

        /// <summary>
        /// The perturbed step: <c>F(Z + d) - F(Z)</c> for the reference point (X, Y) and the offset
        /// (dx, dy), without the constant. Every polynomial's offset is expanded in powers of d and
        /// every product by <c>a db + b da + da db</c>, so nothing subtracts two nearly equal values;
        /// a fold goes through <see cref="Fold.Delta"/>, exact on either side of its line. Ported from
        /// the idea of WPF's <c>FoldedPolynomialProgram.Delta</c>, on the fixed shape of this struct.
        /// The reference's A and C are evaluated here from the orbit's doubles - their sign is what a
        /// fold needs, and it is wrong only within a rounding of the fold line, where the offset
        /// itself decides the side.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Perturb(double x, double y, double dx, double dy, out double nextX, out double nextY)
        {
            var x2 = x * x;
            var y2 = y * y;
            var dx2 = (2d * x + dx) * dx;
            var dy2 = (2d * y + dy) * dy;
            var x4 = x2 * x2;
            var y4 = y2 * y2;
            var xy = x2 * y2;
            var dx4 = (2d * x2 + dx2) * dx2;
            var dy4 = (2d * y2 + dy2) * dy2;
            var dxy = x2 * dy2 + y2 * dx2 + dx2 * dy2;

            var a = A40 * x4 + A22 * xy + A04 * y4 + A20 * x2 + A02 * y2 + A00;
            var da = A40 * dx4 + A22 * dxy + A04 * dy4 + A20 * dx2 + A02 * dy2;
            var c = C40 * x4 + C22 * xy + C04 * y4 + C20 * x2 + C02 * y2 + C00;
            var dc = C40 * dx4 + C22 * dxy + C04 * dy4 + C20 * dx2 + C02 * dy2;

            var fx = x;
            var dfx = dx;
            if (FoldX)
            {
                fx = Math.Abs(x);
                dfx = Fold.Delta(x, dx);
            }

            var fy = y;
            var dfy = dy;
            if (FoldY)
            {
                fy = Math.Abs(y);
                dfy = Fold.Delta(y, dy);
            }

            if (FoldA)
            {
                da = Fold.Delta(a, da);
                a = Math.Abs(a);
            }

            if (FoldC)
            {
                dc = Fold.Delta(c, dc);
                c = Math.Abs(c);
            }

            double gx, dgx, gy, dgy;
            if (Odd)
            {
                gx = fx;
                dgx = dfx;
                gy = fy;
                dgy = dfy;
            }
            else
            {
                gx = 1d;
                dgx = 0d;
                gy = fx * fy;
                dgy = fx * dfy + fy * dfx + dfx * dfy;
            }

            // P = Gx F(A), Q = Gy F(C): offsets of the products.
            var dp = gx * da + a * dgx + dgx * da;
            var dq = gy * dc + c * dgy + dgy * dc;

            if (Swap)
            {
                nextX = dq;
                nextY = ImaginaryFactor * dp;
            }
            else
            {
                nextX = dp;
                nextY = ImaginaryFactor * dq;
            }
        }

        private static DoubleDouble Polynomial(
            in DoubleDouble x4, in DoubleDouble xy, in DoubleDouble y4, in DoubleDouble x2, in DoubleDouble y2,
            double k40, double k22, double k04, double k20, double k02, double k00)
        {
            var sum = new DoubleDouble(k00);
            if (k40 != 0d)
            {
                sum = DoubleDouble.Add(sum, DoubleDouble.Multiply(x4, k40));
            }

            if (k22 != 0d)
            {
                sum = DoubleDouble.Add(sum, DoubleDouble.Multiply(xy, k22));
            }

            if (k04 != 0d)
            {
                sum = DoubleDouble.Add(sum, DoubleDouble.Multiply(y4, k04));
            }

            if (k20 != 0d)
            {
                sum = DoubleDouble.Add(sum, DoubleDouble.Multiply(x2, k20));
            }

            if (k02 != 0d)
            {
                sum = DoubleDouble.Add(sum, DoubleDouble.Multiply(y2, k02));
            }

            return sum;
        }
    }
}
