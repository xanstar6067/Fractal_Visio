using System;
using System.Collections.Generic;

namespace FractalVisio.Core
{
    /// <summary>
    /// Values for one fractal's parameters, laid out in descriptor order. Kept as plain doubles so
    /// it costs nothing to hand to a sampler struct, and addressed by key from the outside so
    /// saved state survives a fractal gaining or reordering parameters.
    /// </summary>
    public readonly struct FractalParameterSet
    {
        private readonly IReadOnlyList<FractalParameterDescriptor> descriptors;
        private readonly double[] values;

        private FractalParameterSet(IReadOnlyList<FractalParameterDescriptor> descriptors, double[] values)
        {
            this.descriptors = descriptors;
            this.values = values;
        }

        public static FractalParameterSet Empty => new(Array.Empty<FractalParameterDescriptor>(), Array.Empty<double>());

        public static FractalParameterSet Defaults(IReadOnlyList<FractalParameterDescriptor> descriptors)
        {
            if (descriptors == null || descriptors.Count == 0)
            {
                return Empty;
            }

            var values = new double[descriptors.Count];
            for (var i = 0; i < descriptors.Count; i++)
            {
                values[i] = descriptors[i].Default;
            }

            return new FractalParameterSet(descriptors, values);
        }

        public int Count => values?.Length ?? 0;

        public IReadOnlyList<FractalParameterDescriptor> Descriptors =>
            descriptors ?? Array.Empty<FractalParameterDescriptor>();

        public double this[int index] => values != null && (uint)index < (uint)values.Length ? values[index] : 0d;

        public bool TryGet(string key, out double value)
        {
            var index = IndexOf(key);
            if (index < 0)
            {
                value = 0d;
                return false;
            }

            value = values[index];
            return true;
        }

        public double Get(string key, double fallback = 0d)
        {
            return TryGet(key, out var value) ? value : fallback;
        }

        /// <summary>Copy with one value replaced, clamped by its descriptor. The set stays immutable
        /// so a render in flight can never see a half-applied change.</summary>
        public FractalParameterSet With(string key, double value)
        {
            var index = IndexOf(key);
            if (index < 0)
            {
                return this;
            }

            var copy = (double[])values.Clone();
            copy[index] = descriptors[index].Clamp(value);
            return new FractalParameterSet(descriptors, copy);
        }

        /// <summary>
        /// Copy with every parameter <paramref name="source"/> also has taken from it - how a Julia
        /// set and the map of its C agree on a power both carry. Keys only one side has are left alone.
        /// </summary>
        public FractalParameterSet Adopt(in FractalParameterSet source)
        {
            var result = this;
            for (var i = 0; i < Count; i++)
            {
                if (source.TryGet(descriptors[i].Key, out var value))
                {
                    result = result.With(descriptors[i].Key, value);
                }
            }

            return result;
        }

        /// <summary>Same descriptors and the same values.</summary>
        public bool SameValues(in FractalParameterSet other)
        {
            if (!ReferenceEquals(descriptors, other.descriptors) || Count != other.Count)
            {
                return false;
            }

            for (var i = 0; i < Count; i++)
            {
                if (!values[i].Equals(other.values[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int IndexOf(string key)
        {
            if (descriptors == null || string.IsNullOrEmpty(key))
            {
                return -1;
            }

            for (var i = 0; i < descriptors.Count; i++)
            {
                if (string.Equals(descriptors[i].Key, key, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
