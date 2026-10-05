using System;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace FractalVisio.Core
{
    /// <summary>
    /// Arbitrary-precision binary floating point for the view: centre and scale.
    /// <c>value = mantissa * 2^exponent</c>, the mantissa a <see cref="BigInteger"/> rounded to
    /// <see cref="PrecisionBits"/> significant bits and kept odd, so that one value has one
    /// representation and <see cref="Equals(HighPrecision)"/> can compare fields.
    ///
    /// It replaced <c>decimal</c> (28 digits), which stopped the zoom at about 1e-24: a centre at
    /// 1e-250 needs some 900 bits. 1152 bits reach 1e-300 with a margin of 20 digits; how deep a
    /// fractal may actually go is the renderer's business (<see cref="PrecisionTier.Arbitrary"/>),
    /// not this type's.
    ///
    /// Not a per-pixel type. Every operation allocates: gestures do a few dozen per frame, the
    /// renderer converts the centre once per request, and a reference orbit is computed in
    /// fixed-point limbs (<see cref="FixedPointOrbit"/>), never in this.
    /// </summary>
    public readonly struct HighPrecision : IComparable<HighPrecision>, IEquatable<HighPrecision>
    {
        /// <summary>Significant bits kept by every operation.</summary>
        public const int PrecisionBits = 1152;

        /// <summary>Exponents past this are not values the app can produce; they saturate.</summary>
        private const int ExponentLimit = 1 << 24;

        private readonly BigInteger mantissa;
        private readonly int exponent;

        private HighPrecision(BigInteger canonicalMantissa, int exponent)
        {
            mantissa = canonicalMantissa;
            this.exponent = exponent;
        }

        public HighPrecision(decimal value)
        {
            this = FromDecimal(value);
        }

        public static HighPrecision Zero => default;

        public static HighPrecision One => new(BigInteger.One, 0);

        public bool IsZero => mantissa.IsZero;

        public int Sign => mantissa.Sign;

        /// <summary>Nearest double: 0 below its range, infinity above it.</summary>
        public double AsDouble
        {
            get
            {
                if (mantissa.IsZero)
                {
                    return 0d;
                }

                var magnitude = BigInteger.Abs(mantissa);
                var bits = BitLength(magnitude);
                long scale = exponent;
                if (bits > 62)
                {
                    magnitude >>= bits - 62;
                    scale += bits - 62;
                }

                var value = ScaleB((double)(long)magnitude, scale);
                return mantissa.Sign < 0 ? -value : value;
            }
        }

        /// <summary>log2 |value|, approximately; negative infinity for zero.</summary>
        public double Log2()
        {
            if (mantissa.IsZero)
            {
                return double.NegativeInfinity;
            }

            var magnitude = BigInteger.Abs(mantissa);
            var bits = BitLength(magnitude);
            var top = bits > 62 ? magnitude >> (bits - 62) : magnitude;
            var shift = bits > 62 ? bits - 62 : 0;
            return Math.Log((double)(long)top, 2d) + shift + exponent;
        }

        public static HighPrecision FromInt(long value) => Create(new BigInteger(value), 0);

        public static HighPrecision FromDouble(double value)
        {
            if (value == 0d || double.IsNaN(value) || double.IsInfinity(value))
            {
                return default;
            }

            var bits = BitConverter.DoubleToInt64Bits(value);
            var field = (int)((bits >> 52) & 0x7FF);
            var fraction = bits & 0xFFFFFFFFFFFFFL;
            long magnitude;
            int binaryExponent;
            if (field == 0)
            {
                magnitude = fraction;
                binaryExponent = -1074;
            }
            else
            {
                magnitude = fraction | (1L << 52);
                binaryExponent = field - 1075;
            }

            return Create(new BigInteger(bits < 0 ? -magnitude : magnitude), binaryExponent);
        }

        public static HighPrecision FromDecimal(decimal value)
        {
            if (value == 0m)
            {
                return default;
            }

            var parts = decimal.GetBits(value);
            var scale = (parts[3] >> 16) & 0xFF;
            var negative = (parts[3] & int.MinValue) != 0;
            var magnitude = new BigInteger((uint)parts[2]);
            magnitude = (magnitude << 32) | (uint)parts[1];
            magnitude = (magnitude << 32) | (uint)parts[0];
            var numerator = Create(negative ? -magnitude : magnitude, 0);
            return scale == 0 ? numerator : numerator / Create(BigInteger.Pow(10, scale), 0);
        }

        /// <summary>
        /// Round to a multiple of 2^-<paramref name="fractionBits"/>. The session keeps a view to
        /// what its scale can use, so a centre that pans at a shallow zoom does not grow a
        /// thousand-bit tail - or a saved file a three-hundred-digit one.
        /// </summary>
        public HighPrecision Quantize(int fractionBits)
        {
            if (mantissa.IsZero || exponent >= -fractionBits)
            {
                return this;
            }

            var shift = -fractionBits - (long)exponent;
            if (shift > PrecisionBits + 64L)
            {
                return default;
            }

            var magnitude = BigInteger.Abs(mantissa);
            magnitude = (magnitude + (BigInteger.One << (int)(shift - 1))) >> (int)shift;
            return Create(mantissa.Sign < 0 ? -magnitude : magnitude, -fractionBits);
        }

        /// <summary>
        /// This value times 2^<paramref name="fractionBits"/>, as an integer (rounded): the bridge to
        /// fixed-point arithmetic.
        /// </summary>
        public BigInteger ToScaledInteger(int fractionBits)
        {
            if (mantissa.IsZero)
            {
                return BigInteger.Zero;
            }

            var shift = (long)exponent + fractionBits;
            if (shift >= 0)
            {
                return mantissa << (int)Math.Min(shift, int.MaxValue / 2);
            }

            if (-shift > PrecisionBits + 64L)
            {
                return BigInteger.Zero;
            }

            var magnitude = BigInteger.Abs(mantissa);
            magnitude = (magnitude + (BigInteger.One << (int)(-shift - 1))) >> (int)-shift;
            return mantissa.Sign < 0 ? -magnitude : magnitude;
        }

        public static HighPrecision operator +(HighPrecision a, HighPrecision b)
        {
            if (a.mantissa.IsZero)
            {
                return b;
            }

            if (b.mantissa.IsZero)
            {
                return a;
            }

            var high = a.exponent >= b.exponent ? a : b;
            var low = a.exponent >= b.exponent ? b : a;
            var difference = (long)high.exponent - low.exponent;

            // The low operand ends far below the high one's last kept bit: it cannot change it.
            var lowTop = low.exponent + (long)BitLength(BigInteger.Abs(low.mantissa));
            var highBottom = high.exponent + (long)BitLength(BigInteger.Abs(high.mantissa)) - PrecisionBits - 2;
            if (lowTop < highBottom)
            {
                return high;
            }

            return Create((high.mantissa << (int)difference) + low.mantissa, low.exponent);
        }

        public static HighPrecision operator -(HighPrecision a, HighPrecision b) => a + -b;

        public static HighPrecision operator -(HighPrecision a) => new(-a.mantissa, a.exponent);

        public static HighPrecision operator *(HighPrecision a, HighPrecision b)
        {
            if (a.mantissa.IsZero || b.mantissa.IsZero)
            {
                return default;
            }

            return Create(a.mantissa * b.mantissa, (long)a.exponent + b.exponent);
        }

        public static HighPrecision operator *(HighPrecision a, double b) => a * FromDouble(b);

        public static HighPrecision operator /(HighPrecision a, HighPrecision b)
        {
            if (b.mantissa.IsZero)
            {
                throw new DivideByZeroException();
            }

            if (a.mantissa.IsZero)
            {
                return default;
            }

            // Enough quotient bits for a full mantissa plus rounding.
            var shift = Math.Max(
                0, PrecisionBits + 2 + BitLength(BigInteger.Abs(b.mantissa)) - BitLength(BigInteger.Abs(a.mantissa)));
            var quotient = BigInteger.Divide(a.mantissa << shift, b.mantissa);
            return Create(quotient, (long)a.exponent - b.exponent - shift);
        }

        public static HighPrecision operator /(HighPrecision a, double b) => a / FromDouble(b);

        public static bool operator >(HighPrecision a, HighPrecision b) => a.CompareTo(b) > 0;
        public static bool operator <(HighPrecision a, HighPrecision b) => a.CompareTo(b) < 0;
        public static bool operator >=(HighPrecision a, HighPrecision b) => a.CompareTo(b) >= 0;
        public static bool operator <=(HighPrecision a, HighPrecision b) => a.CompareTo(b) <= 0;

        public static implicit operator HighPrecision(decimal source) => FromDecimal(source);

        public static explicit operator double(HighPrecision source) => source.AsDouble;

        public int CompareTo(HighPrecision other)
        {
            if (Sign != other.Sign)
            {
                return Sign.CompareTo(other.Sign);
            }

            return (this - other).Sign;
        }

        public bool Equals(HighPrecision other) => exponent == other.exponent && mantissa.Equals(other.mantissa);

        public override bool Equals(object obj) => obj is HighPrecision other && Equals(other);

        public override int GetHashCode() => mantissa.GetHashCode() * 397 ^ exponent;

        public override string ToString() => ToInvariantString();

        /// <summary>
        /// Invariant decimal text that parses back to the same value: as many significant digits as
        /// the mantissa holds bits for, plus two. Plain notation near 1, scientific below 1e-6 -
        /// a scale at 1e-200 written out would be two hundred zeros.
        /// </summary>
        public string ToInvariantString(int maximumSignificantDigits = int.MaxValue)
        {
            if (mantissa.IsZero)
            {
                return "0";
            }

            var magnitude = BigInteger.Abs(mantissa);
            var negative = mantissa.Sign < 0;
            string digits;
            int pointPosition; // digits before the decimal point, may be <= 0 or > digits.Length
            if (exponent >= 0)
            {
                digits = (magnitude << exponent).ToString(CultureInfo.InvariantCulture);
                pointPosition = digits.Length;
            }
            else
            {
                // m * 2^-k = m * 5^k / 10^k: exact, and the digits come out of one ToString.
                var k = -exponent;
                digits = (magnitude * BigInteger.Pow(5, k)).ToString(CultureInfo.InvariantCulture);
                pointPosition = digits.Length - k;
            }

            var significant = Math.Min(
                maximumSignificantDigits, (int)Math.Ceiling(BitLength(magnitude) * 0.30102999566398120) + 2);
            significant = Math.Max(1, significant);
            if (digits.Length > significant)
            {
                var rounded = RoundDigits(digits, significant, out var carried);
                pointPosition += carried ? 1 : 0;
                digits = rounded;
            }

            digits = digits.TrimEnd('0');
            if (digits.Length == 0)
            {
                return "0";
            }

            var builder = new StringBuilder(digits.Length + 16);
            if (negative)
            {
                builder.Append('-');
            }

            if (pointPosition <= -6 || pointPosition > 30)
            {
                builder.Append(digits[0]);
                if (digits.Length > 1)
                {
                    builder.Append('.').Append(digits, 1, digits.Length - 1);
                }

                builder.Append('e').Append((pointPosition - 1).ToString(CultureInfo.InvariantCulture));
                return builder.ToString();
            }

            if (pointPosition <= 0)
            {
                builder.Append("0.").Append('0', -pointPosition).Append(digits);
            }
            else if (pointPosition >= digits.Length)
            {
                builder.Append(digits).Append('0', pointPosition - digits.Length);
            }
            else
            {
                builder.Append(digits, 0, pointPosition).Append('.').Append(digits, pointPosition, digits.Length - pointPosition);
            }

            return builder.ToString();
        }

        /// <summary>Parse invariant decimal text, plain or with an exponent ("1.5e-120").</summary>
        public static bool TryParse(string text, out HighPrecision value)
        {
            value = default;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            text = text.Trim();
            var index = 0;
            var negative = false;
            if (text[0] == '-' || text[0] == '+')
            {
                negative = text[0] == '-';
                index = 1;
            }

            var digits = new StringBuilder(text.Length);
            var fractionDigits = 0;
            var seenPoint = false;
            var decimalExponent = 0;
            for (; index < text.Length; index++)
            {
                var c = text[index];
                if (c >= '0' && c <= '9')
                {
                    digits.Append(c);
                    if (seenPoint)
                    {
                        fractionDigits++;
                    }
                }
                else if (c == '.' && !seenPoint)
                {
                    seenPoint = true;
                }
                else if (c == 'e' || c == 'E')
                {
                    if (!int.TryParse(text.Substring(index + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out decimalExponent) ||
                        Math.Abs(decimalExponent) > 100000)
                    {
                        return false;
                    }

                    break;
                }
                else
                {
                    return false;
                }
            }

            if (digits.Length == 0)
            {
                return false;
            }

            var integer = BigInteger.Parse(digits.ToString(), CultureInfo.InvariantCulture);
            if (negative)
            {
                integer = -integer;
            }

            var power = decimalExponent - fractionDigits;
            var number = Create(integer, 0);
            value = power >= 0
                ? number * Create(BigInteger.Pow(10, power), 0)
                : number / Create(BigInteger.Pow(10, -power), 0);
            return true;
        }

        /// <summary>Round and canonicalise: at most <see cref="PrecisionBits"/> bits, odd mantissa.</summary>
        private static HighPrecision Create(BigInteger value, long binaryExponent)
        {
            if (value.IsZero)
            {
                return default;
            }

            var negative = value.Sign < 0;
            var magnitude = negative ? -value : value;
            var bits = BitLength(magnitude);
            if (bits > PrecisionBits)
            {
                var shift = bits - PrecisionBits;
                magnitude = (magnitude + (BigInteger.One << (shift - 1))) >> shift;
                binaryExponent += shift;
            }

            var trailing = TrailingZeros(magnitude);
            if (trailing > 0)
            {
                magnitude >>= trailing;
                binaryExponent += trailing;
            }

            if (binaryExponent < -ExponentLimit)
            {
                return default;
            }

            if (binaryExponent > ExponentLimit)
            {
                binaryExponent = ExponentLimit;
            }

            return new HighPrecision(negative ? -magnitude : magnitude, (int)binaryExponent);
        }

        /// <summary>Bits in a non-negative integer.</summary>
        private static int BitLength(BigInteger magnitude)
        {
            if (magnitude.IsZero)
            {
                return 0;
            }

            var bytes = magnitude.ToByteArray();
            var top = bytes.Length - 1;
            while (top > 0 && bytes[top] == 0)
            {
                top--;
            }

            var bits = 0;
            for (var b = bytes[top]; b != 0; b >>= 1)
            {
                bits++;
            }

            return top * 8 + bits;
        }

        private static int TrailingZeros(BigInteger magnitude)
        {
            if (magnitude.IsZero)
            {
                return 0;
            }

            var bytes = magnitude.ToByteArray();
            var index = 0;
            while (bytes[index] == 0)
            {
                index++;
            }

            var bits = 0;
            for (var b = bytes[index]; (b & 1) == 0; b >>= 1)
            {
                bits++;
            }

            return index * 8 + bits;
        }

        /// <summary>The first <paramref name="count"/> digits, rounded half up on the next one.</summary>
        private static string RoundDigits(string digits, int count, out bool carriedIntoNewDigit)
        {
            carriedIntoNewDigit = false;
            var kept = digits.Substring(0, count).ToCharArray();
            if (digits[count] < '5')
            {
                return new string(kept);
            }

            for (var i = count - 1; i >= 0; i--)
            {
                if (kept[i] != '9')
                {
                    kept[i]++;
                    return new string(kept);
                }

                kept[i] = '0';
            }

            carriedIntoNewDigit = true;
            return "1" + new string(kept);
        }

        /// <summary>x * 2^n without Math.ScaleB, which .NET Standard 2.1 does not have.</summary>
        public static double ScaleB(double x, long n)
        {
            while (n > 1000)
            {
                x *= Pow2(1000);
                n -= 1000;
                if (double.IsInfinity(x))
                {
                    return x;
                }
            }

            while (n < -1000)
            {
                x *= Pow2(-1000);
                n += 1000;
                if (x == 0d)
                {
                    return 0d;
                }
            }

            return x * Pow2((int)n);
        }

        /// <summary>2^n for -1022 &lt;= n &lt;= 1023, built from its bits.</summary>
        private static double Pow2(int n) => BitConverter.Int64BitsToDouble((long)(n + 1023) << 52);
    }
}
