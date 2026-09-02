using System;

namespace FractalVisio.Core
{
    public enum FractalParameterKind
    {
        Double,
        Int,
        Bool
    }

    /// <summary>
    /// One knob a fractal exposes. The settings screen builds its controls from these, so a new
    /// fractal gets working UI without anyone editing the UI.
    /// </summary>
    public readonly struct FractalParameterDescriptor
    {
        public FractalParameterDescriptor(
            string key,
            string label,
            double defaultValue,
            double minimum,
            double maximum,
            FractalParameterKind kind = FractalParameterKind.Double,
            bool logarithmic = false)
        {
            Key = key;
            Label = label;
            Default = defaultValue;
            Minimum = minimum;
            Maximum = maximum;
            Kind = kind;
            Logarithmic = logarithmic;
        }

        /// <summary>Stable key. Saved state stores parameters by this, never by index.</summary>
        public string Key { get; }

        public string Label { get; }
        public double Default { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public FractalParameterKind Kind { get; }

        /// <summary>Hint for the UI: this reads better on a logarithmic slider.</summary>
        public bool Logarithmic { get; }

        public double Clamp(double value)
        {
            if (double.IsNaN(value))
            {
                return Default;
            }

            var clamped = Math.Clamp(value, Minimum, Maximum);
            return Kind switch
            {
                FractalParameterKind.Int => Math.Round(clamped),
                FractalParameterKind.Bool => clamped >= 0.5d ? 1d : 0d,
                _ => clamped
            };
        }
    }
}
