using System.Threading;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Simonobrot's map, z -> z^p |z|^p + c, for a whole power p of either sign, and the inversion
    /// of WPF (c's real part negated). Shared by the parameter plane and its Julia sets, and mirrored
    /// by <c>Shaders/Simonobrot.shader</c>.
    /// </summary>
    internal static class SimonobrotStep
    {
        public const int MinimumPower = -8;
        public const int MaximumPower = 8;

        public static int ClampPower(int power) => System.Math.Clamp(power, MinimumPower, MaximumPower);

        /// <summary>
        /// Degree of the map for the smooth count: |z^p |z|^p| = |z|^(2p). A map that shrinks large
        /// z (p below 1) escapes by jumps, not by growth, and gets the plain count.
        /// </summary>
        public static double SmoothDegree(int power) => power >= 1 ? 2d * power : 2d;

        /// <summary>z^p |z|^p without the constant. At z = 0 it is 0 for every p, as in WPF.</summary>
        public static void Map(double x, double y, int power, out double re, out double im)
        {
            var radiusSquared = x * x + y * y;
            if (!(radiusSquared > 0d))
            {
                re = 0d;
                im = 0d;
                return;
            }

            var n = System.Math.Abs(power);
            var radius = System.Math.Sqrt(radiusSquared);
            var zx = 1d;
            var zy = 0d;
            var scale = 1d;
            for (var k = 0; k < n; k++)
            {
                var next = zx * x - zy * y;
                zy = zx * y + zy * x;
                zx = next;
                scale *= radius;
            }

            re = zx * scale;
            im = zy * scale;
            if (power < 0)
            {
                // 1 / (z^n |z|^n)
                var squared = re * re + im * im;
                re /= squared;
                im = -im / squared;
            }
        }

        /// <summary>
        /// The map's real Jacobian at z: <c>r^p d(z^p) + z^p grad(r^p)</c>, with
        /// <c>d(z^p) = p z^(p-1)</c> and <c>grad(r^p) = p r^(p-2) (x, y)</c> - valid for either sign.
        /// </summary>
        public static void Jacobian(double x, double y, int power, out double s00, out double s01, out double s10, out double s11)
        {
            var radiusSquared = x * x + y * y;
            if (!(radiusSquared > 0d) || power == 0)
            {
                s00 = s01 = s10 = s11 = 0d;
                return;
            }

            var n = System.Math.Abs(power);
            var radius = System.Math.Sqrt(radiusSquared);

            // z^n and r^n, then z^p = z^n or 1 / z^n, r^p likewise.
            var zx = 1d;
            var zy = 0d;
            var rp = 1d;
            for (var k = 0; k < n; k++)
            {
                var next = zx * x - zy * y;
                zy = zx * y + zy * x;
                zx = next;
                rp *= radius;
            }

            if (power < 0)
            {
                var squared = zx * zx + zy * zy;
                zx /= squared;
                zy = -zy / squared;
                rp = 1d / rp;
            }

            // p z^(p-1) = p z^p conj(z) / |z|^2
            var ax = power * (zx * x + zy * y) / radiusSquared;
            var ay = power * (zy * x - zx * y) / radiusSquared;
            var g = power * rp / radiusSquared;

            s00 = rp * ax + zx * g * x;
            s01 = -rp * ay + zx * g * y;
            s10 = rp * ay + zy * g * x;
            s11 = rp * ax + zy * g * y;
        }
    }

    /// <summary>
    /// The Simonobrot in fp64, on the parameter plane or as a Julia set. No deeper arithmetic: the
    /// radial factor |z|^p is not a polynomial, and WPF has no deep kernel for it either - the
    /// session stops the zoom where fp64 runs out.
    /// </summary>
    public readonly struct SimonobrotSamplerD : IEscapeSamplerD
    {
        private readonly int power;
        private readonly double smoothDegree;
        private readonly double inversion;
        private readonly bool julia;
        private readonly double constantX;
        private readonly double constantY;

        public SimonobrotSamplerD(int power, bool inversion)
        {
            this.power = SimonobrotStep.ClampPower(power);
            smoothDegree = SimonobrotStep.SmoothDegree(this.power);
            this.inversion = inversion ? -1d : 1d;
            julia = false;
            constantX = 0d;
            constantY = 0d;
        }

        public SimonobrotSamplerD(int power, bool inversion, double constantX, double constantY)
        {
            this.power = SimonobrotStep.ClampPower(power);
            smoothDegree = SimonobrotStep.SmoothDegree(this.power);
            this.inversion = inversion ? -1d : 1d;
            julia = true;
            this.constantX = constantX;
            this.constantY = constantY;
        }

        public float Sample(double x, double y, int maxIterations, CancellationToken token)
        {
            var zx = julia ? x : 0d;
            var zy = julia ? y : 0d;
            var cx = inversion * (julia ? constantX : x);
            var cy = julia ? constantY : y;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                SimonobrotStep.Map(zx, zy, power, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, smoothDegree);
                }
            }

            return EscapeMath.Interior;
        }

        public float SampleWithSlope(
            double x, double y, int maxIterations, CancellationToken token, out double slopeX, out double slopeY)
        {
            slopeX = 0d;
            slopeY = 0d;

            var zx = julia ? x : 0d;
            var zy = julia ? y : 0d;
            var cx = inversion * (julia ? constantX : x);
            var cy = julia ? constantY : y;

            // dc/dpixel is diag(inversion, 1) on the parameter plane; a Julia set starts from I.
            var add00 = julia ? 0d : inversion;
            var add11 = julia ? 0d : 1d;
            var j00 = julia ? 1d : 0d;
            var j01 = 0d;
            var j10 = 0d;
            var j11 = j00;
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    return EscapeMath.Interior;
                }

                SimonobrotStep.Jacobian(zx, zy, power, out var s00, out var s01, out var s10, out var s11);
                var n00 = s00 * j00 + s01 * j10 + add00;
                var n01 = s00 * j01 + s01 * j11;
                var n10 = s10 * j00 + s11 * j10;
                var n11 = s10 * j01 + s11 * j11 + add11;
                j00 = n00;
                j01 = n01;
                j10 = n10;
                j11 = n11;

                SimonobrotStep.Map(zx, zy, power, out var re, out var im);
                zx = re + cx;
                zy = im + cy;
                iteration++;

                var squared = zx * zx + zy * zy;
                if (squared > MandelbrotSamplerD.Bailout)
                {
                    EscapeMath.JacobianSlope(zx, zy, j00, j01, j10, j11, out slopeX, out slopeY);
                    return EscapeMath.Smooth(iteration, squared, MandelbrotSamplerD.Bailout, smoothDegree);
                }
            }

            return EscapeMath.Interior;
        }
    }
}
