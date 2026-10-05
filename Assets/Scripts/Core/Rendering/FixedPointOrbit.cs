using System;
using System.Numerics;
using System.Threading;

namespace FractalVisio.Core
{
    /// <summary>
    /// A z^2 + c reference orbit in arbitrary-precision fixed point, for depths double-double
    /// (~32 digits) cannot hold. The port of the WPF engine's BigFloat reference orbit, reshaped for
    /// what .NET Standard 2.1 has: no UInt128, no inline arrays, no BitOperations.
    ///
    /// Fixed point rather than floating, because an orbit is bounded: up to the reference escape
    /// (|Z|^2 &lt;= 1e18) every value fits two 32-bit integer limbs, so the only question is how many
    /// fraction limbs - and the answer is the depth. Each number is a sign and a magnitude of
    /// <c>n</c> limbs, least significant first, the low <c>n - 2</c> of them fraction. A step is three
    /// products of n-limb numbers into one scratch buffer and a few signed additions; nothing is
    /// allocated per step, which matters on Unity's non-moving collector far more than the
    /// arithmetic does.
    ///
    /// The orbit is only ever read back as doubles (<see cref="ReferenceOrbit"/>), so the precision
    /// it needs is the precision of its <i>start</i>: enough fraction bits to resolve the pixel
    /// spacing, plus the bits the iteration loses along the way (WPF: log2(1/spacing) +
    /// 2 log2(iterations) + 48).
    /// </summary>
    public static class FixedPointOrbit
    {
        /// <summary>Fraction bits for a render whose pixels lie within <paramref name="maxDeltaC"/> of the reference.</summary>
        public static int FractionBitsFor(double maxDeltaC, int maxIterations)
        {
            var depthBits = maxDeltaC > 0d ? -Math.Log(maxDeltaC, 2d) : 0d;
            var iterationBits = Math.Log(Math.Max(2, maxIterations), 2d);
            var bits = (int)Math.Ceiling(Math.Max(0d, depthBits) + 2d * iterationBits + 64d);
            return Math.Max(128, (bits + 31) / 32 * 32);
        }

        /// <summary>
        /// Fill <paramref name="orbit"/> with the orbit of (<paramref name="startX"/>,
        /// <paramref name="startY"/>) under z^2 + (<paramref name="cx"/>, <paramref name="cy"/>) until
        /// it passes <paramref name="referenceEscape"/> on the squared modulus. Z_0 and Z_1 are always
        /// kept (see <see cref="IPerturbationSampler"/>). Returns false if cancelled part-way.
        /// </summary>
        public static bool BuildQuadratic(
            ReferenceOrbit orbit,
            in HighPrecision startX, in HighPrecision startY,
            in HighPrecision cx, in HighPrecision cy,
            int maxIterations, double referenceEscape, int fractionBits,
            CancellationToken token)
        {
            orbit.Begin(maxIterations);

            var fractionLimbs = (fractionBits + 31) / 32;
            var n = fractionLimbs + 2;
            var x = new Number(n);
            var y = new Number(n);
            var c0 = new Number(n);
            var c1 = new Number(n);
            var xx = new Number(n);
            var yy = new Number(n);
            var xy = new Number(n);
            var scratch = new uint[2 * n + 1];

            x.Set(startX, fractionLimbs);
            y.Set(startY, fractionLimbs);
            c0.Set(cx, fractionLimbs);
            c1.Set(cy, fractionLimbs);

            for (var index = 0; index <= maxIterations; index++)
            {
                if ((index & 255) == 0 && token.IsCancellationRequested)
                {
                    return false;
                }

                var real = x.ToDouble(fractionLimbs);
                var imaginary = y.ToDouble(fractionLimbs);
                if (!orbit.Append(real, imaginary))
                {
                    break;
                }

                var magnitude = real * real + imaginary * imaginary;
                if (index >= 1 && !(magnitude <= referenceEscape))
                {
                    break;
                }

                // x' = x^2 - y^2 + cx, y' = 2xy + cy
                Multiply(x, x, xx, scratch, fractionLimbs);
                Multiply(y, y, yy, scratch, fractionLimbs);
                Multiply(x, y, xy, scratch, fractionLimbs);
                xy.ShiftLeftOne();

                Add(xx, yy, -1, x);
                Add(x, c0, 1, x);
                Add(xy, c1, 1, y);
            }

            return true;
        }

        /// <summary>A signed fixed-point number: magnitude limbs, least significant first.</summary>
        private sealed class Number
        {
            public readonly uint[] Limbs;
            public int Sign; // -1, 0 or 1

            public Number(int length)
            {
                Limbs = new uint[length];
            }

            public void Set(in HighPrecision value, int fractionLimbs)
            {
                Array.Clear(Limbs, 0, Limbs.Length);
                var scaled = value.ToScaledInteger(fractionLimbs * 32);
                Sign = scaled.Sign;
                if (Sign == 0)
                {
                    return;
                }

                var bytes = BigInteger.Abs(scaled).ToByteArray();
                for (var i = 0; i < bytes.Length && i / 4 < Limbs.Length; i++)
                {
                    Limbs[i / 4] |= (uint)bytes[i] << (8 * (i % 4));
                }
            }

            public void ShiftLeftOne()
            {
                uint carry = 0;
                for (var i = 0; i < Limbs.Length; i++)
                {
                    var limb = Limbs[i];
                    Limbs[i] = (limb << 1) | carry;
                    carry = limb >> 31;
                }
            }

            public double ToDouble(int fractionLimbs)
            {
                if (Sign == 0)
                {
                    return 0d;
                }

                var top = Limbs.Length - 1;
                while (top > 0 && Limbs[top] == 0)
                {
                    top--;
                }

                // Three limbs are 96 bits: more than a double keeps.
                var value = 0d;
                for (var i = top; i >= 0 && i >= top - 2; i--)
                {
                    value += HighPrecision.ScaleB(Limbs[i], 32L * (i - fractionLimbs));
                }

                return Sign < 0 ? -value : value;
            }
        }

        /// <summary>result = a * b, truncated to the fixed-point grid. result may not alias a or b.</summary>
        private static void Multiply(Number a, Number b, Number result, uint[] scratch, int fractionLimbs)
        {
            var n = a.Limbs.Length;
            if (a.Sign == 0 || b.Sign == 0)
            {
                Array.Clear(result.Limbs, 0, n);
                result.Sign = 0;
                return;
            }

            Array.Clear(scratch, 0, scratch.Length);
            var aLimbs = a.Limbs;
            var bLimbs = b.Limbs;

            var aTop = n - 1;
            while (aTop > 0 && aLimbs[aTop] == 0)
            {
                aTop--;
            }

            var bTop = n - 1;
            while (bTop > 0 && bLimbs[bTop] == 0)
            {
                bTop--;
            }

            for (var i = 0; i <= aTop; i++)
            {
                ulong ai = aLimbs[i];
                if (ai == 0)
                {
                    continue;
                }

                ulong carry = 0;
                var k = i;
                for (var j = 0; j <= bTop; j++, k++)
                {
                    var product = ai * bLimbs[j] + scratch[k] + carry;
                    scratch[k] = (uint)product;
                    carry = product >> 32;
                }

                while (carry != 0 && k < scratch.Length)
                {
                    var sum = (ulong)scratch[k] + carry;
                    scratch[k] = (uint)sum;
                    carry = sum >> 32;
                    k++;
                }
            }

            var any = false;
            for (var i = 0; i < n; i++)
            {
                var limb = scratch[i + fractionLimbs];
                result.Limbs[i] = limb;
                any |= limb != 0;
            }

            result.Sign = any ? a.Sign * b.Sign : 0;
        }

        /// <summary>result = a + bSign * b. result may alias a.</summary>
        private static void Add(Number a, Number b, int bSign, Number result)
        {
            var n = a.Limbs.Length;
            var signB = b.Sign * bSign;
            if (signB == 0)
            {
                Copy(a, result);
                return;
            }

            if (a.Sign == 0)
            {
                Copy(b, result);
                result.Sign = signB;
                return;
            }

            if (a.Sign == signB)
            {
                ulong carry = 0;
                for (var i = 0; i < n; i++)
                {
                    var sum = (ulong)a.Limbs[i] + b.Limbs[i] + carry;
                    result.Limbs[i] = (uint)sum;
                    carry = sum >> 32;
                }

                result.Sign = signB;
                return;
            }

            // Opposite signs: subtract the smaller magnitude from the larger.
            var comparison = CompareMagnitude(a.Limbs, b.Limbs);
            if (comparison == 0)
            {
                Array.Clear(result.Limbs, 0, n);
                result.Sign = 0;
                return;
            }

            var larger = comparison > 0 ? a.Limbs : b.Limbs;
            var smaller = comparison > 0 ? b.Limbs : a.Limbs;
            var sign = comparison > 0 ? a.Sign : signB;
            long borrow = 0;
            for (var i = 0; i < n; i++)
            {
                var difference = (long)larger[i] - smaller[i] - borrow;
                borrow = difference < 0 ? 1 : 0;
                result.Limbs[i] = (uint)(difference + (borrow << 32));
            }

            result.Sign = sign;
        }

        private static void Copy(Number source, Number target)
        {
            if (!ReferenceEquals(source, target))
            {
                Array.Copy(source.Limbs, target.Limbs, source.Limbs.Length);
                target.Sign = source.Sign;
            }
        }

        private static int CompareMagnitude(uint[] a, uint[] b)
        {
            for (var i = a.Length - 1; i >= 0; i--)
            {
                if (a[i] != b[i])
                {
                    return a[i] > b[i] ? 1 : -1;
                }
            }

            return 0;
        }
    }
}
