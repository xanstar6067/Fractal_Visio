using System;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Lightweight high-precision wrapper used by the View layer.
    /// Uses decimal for deterministic value storage and explicit double conversions when needed.
    /// </summary>
    [Serializable]
    public readonly struct HighPrecision : IComparable<HighPrecision>, IEquatable<HighPrecision>
    {
        [SerializeField] private readonly decimal value;

        public HighPrecision(decimal value)
        {
            this.value = value;
        }

        public decimal AsDecimal => value;
        public double AsDouble => (double)value;

        public static HighPrecision Zero => new(0m);
        public static HighPrecision One => new(1m);

        public static HighPrecision FromDouble(double source)
        {
            if (double.IsNaN(source) || double.IsInfinity(source))
            {
                return Zero;
            }

            const double max = (double)decimal.MaxValue;
            const double min = (double)decimal.MinValue;

            if (source > max)
            {
                return new HighPrecision(decimal.MaxValue);
            }

            if (source < min)
            {
                return new HighPrecision(decimal.MinValue);
            }

            return new HighPrecision((decimal)source);
        }

        public int CompareTo(HighPrecision other) => value.CompareTo(other.value);
        public bool Equals(HighPrecision other) => value == other.value;
        public override bool Equals(object obj) => obj is HighPrecision other && Equals(other);
        public override int GetHashCode() => value.GetHashCode();
        public override string ToString() => value.ToString("G29");

        public static HighPrecision operator +(HighPrecision a, HighPrecision b) => new(a.value + b.value);
        public static HighPrecision operator -(HighPrecision a, HighPrecision b) => new(a.value - b.value);
        public static HighPrecision operator *(HighPrecision a, HighPrecision b) => new(a.value * b.value);
        public static HighPrecision operator /(HighPrecision a, HighPrecision b) => new(a.value / b.value);
        public static HighPrecision operator -(HighPrecision a) => new(-a.value);

        public static bool operator >(HighPrecision a, HighPrecision b) => a.value > b.value;
        public static bool operator <(HighPrecision a, HighPrecision b) => a.value < b.value;
        public static bool operator >=(HighPrecision a, HighPrecision b) => a.value >= b.value;
        public static bool operator <=(HighPrecision a, HighPrecision b) => a.value <= b.value;

        public static implicit operator HighPrecision(decimal source) => new(source);
        public static explicit operator decimal(HighPrecision source) => source.value;
        public static explicit operator double(HighPrecision source) => source.AsDouble;
    }
}
