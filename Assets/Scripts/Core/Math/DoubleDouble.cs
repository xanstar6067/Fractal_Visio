using System;
using System.Runtime.CompilerServices;

namespace FractalVisio.Core
{
    /// <summary>
    /// An allocation-free double-double value. The unevaluated hi + lo pair carries
    /// roughly 30 decimal digits without allocating or relying on System.Decimal
    /// inside jobs.
    /// </summary>
    [Serializable]
    public readonly struct DoubleDouble
    {
        private const double Splitter = 134217729d; // 2^27 + 1

        public readonly double Hi;
        public readonly double Lo;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public DoubleDouble(double hi, double lo = 0d)
        {
            Hi = hi;
            Lo = lo;
        }

        public static DoubleDouble FromDecimal(decimal value)
        {
            var hi = (double)value;
            var remainder = value - (decimal)hi;
            return Normalize(hi, (double)remainder);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Add(DoubleDouble a, DoubleDouble b)
        {
            TwoSum(a.Hi, b.Hi, out var sum, out var error);
            error += a.Lo + b.Lo;
            return QuickNormalize(sum, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Add(DoubleDouble a, double b)
        {
            TwoSum(a.Hi, b, out var sum, out var error);
            error += a.Lo;
            return QuickNormalize(sum, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Subtract(DoubleDouble a, DoubleDouble b)
        {
            return Add(a, new DoubleDouble(-b.Hi, -b.Lo));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Multiply(DoubleDouble a, DoubleDouble b)
        {
            TwoProduct(a.Hi, b.Hi, out var product, out var error);
            error += (a.Hi * b.Lo) + (a.Lo * b.Hi);
            return QuickNormalize(product, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Multiply(DoubleDouble a, double b)
        {
            TwoProduct(a.Hi, b, out var product, out var error);
            error += a.Lo * b;
            return QuickNormalize(product, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static DoubleDouble Square(DoubleDouble value)
        {
            TwoProduct(value.Hi, value.Hi, out var product, out var error);
            error += 2d * value.Hi * value.Lo;
            return QuickNormalize(product, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble() => Hi + Lo;

        private static DoubleDouble Normalize(double hi, double lo)
        {
            TwoSum(hi, lo, out var sum, out var error);
            return new DoubleDouble(sum, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static DoubleDouble QuickNormalize(double hi, double lo)
        {
            var sum = hi + lo;
            var error = lo - (sum - hi);
            return new DoubleDouble(sum, error);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TwoSum(double a, double b, out double sum, out double error)
        {
            sum = a + b;
            var bVirtual = sum - a;
            error = (a - (sum - bVirtual)) + (b - bVirtual);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void TwoProduct(double a, double b, out double product, out double error)
        {
            product = a * b;

            var splitA = Splitter * a;
            var aHigh = splitA - (splitA - a);
            var aLow = a - aHigh;

            var splitB = Splitter * b;
            var bHigh = splitB - (splitB - b);
            var bLow = b - bHigh;

            error = ((aHigh * bHigh - product) + aHigh * bLow + aLow * bHigh) + aLow * bLow;
        }
    }
}
