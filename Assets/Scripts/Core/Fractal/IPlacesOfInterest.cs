using System;
using System.Collections.Generic;

namespace FractalVisio.Core
{
    /// <summary>
    /// Implemented by a definition that knows places worth seeing - the WPF version's points of
    /// interest. Beside <see cref="IFractalDefinition"/>, like <see cref="IParameterPlane"/>: a
    /// fractal without places does not implement it, and the UI shows no list.
    /// </summary>
    public interface IPlacesOfInterest
    {
        /// <summary>
        /// In display order. Names are locale keys <c>fractal.&lt;id&gt;.place.&lt;place id&gt;</c>,
        /// falling back to the place id.
        /// </summary>
        IReadOnlyList<PlaceOfInterest> Places { get; }
    }

    /// <summary>
    /// A place: a centre and a height framed the way a default view is - for a screen at least as
    /// wide as it is tall, widened by the session on an upright one - plus the parameters it needs,
    /// such as a Multibrot's power. Coordinates are decimal, like every saved view: a place deep
    /// in carries more digits than a double holds.
    /// </summary>
    public readonly struct PlaceOfInterest
    {
        public PlaceOfInterest(string id, decimal x, decimal y, decimal height, params ParameterValue[] parameters)
        {
            Id = id;
            X = x;
            Y = y;
            Height = height;
            Parameters = parameters ?? Array.Empty<ParameterValue>();
        }

        /// <summary>Stable key for the locale name. Never rename a shipped one.</summary>
        public string Id { get; }

        public decimal X { get; }
        public decimal Y { get; }

        /// <summary>Plane height to show on a wide screen.</summary>
        public decimal Height { get; }

        public IReadOnlyList<ParameterValue> Parameters { get; }

        /// <summary>The place as a view, for <c>FractalSession.SetFramedView</c>.</summary>
        public ViewState Framing => new()
        {
            x = new HighPrecision(X),
            y = new HighPrecision(Y),
            scale = new HighPrecision(Height),
            rotation = 0d
        };
    }

    /// <summary>A parameter value by key, as saved state stores it.</summary>
    public readonly struct ParameterValue
    {
        public ParameterValue(string key, double value)
        {
            Key = key;
            Value = value;
        }

        public string Key { get; }
        public double Value { get; }
    }
}
