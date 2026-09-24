using System.Collections.Generic;

namespace FractalVisio.Core
{
    /// <summary>
    /// Implemented by a fractal definition two of whose parameters are one point on another
    /// fractal's plane. A Julia set's constant C is a point of the Mandelbrot set - every C inside it
    /// gives a connected Julia set, every C outside a dust - so the natural way to choose C is to
    /// point at the Mandelbrot set. The UI draws that other fractal as a map and lets the user place
    /// the point on it; this interface is all it needs: which fractal to draw, which two parameters
    /// the point writes, what part of the plane the map starts on, and a few well-known points.
    ///
    /// Neither side references the other's type: the map fractal is found by id in the catalog,
    /// like everything else the UI shows.
    /// </summary>
    public interface IParameterPlane
    {
        /// <summary><see cref="IFractalDefinition.Id"/> of the fractal drawn as the map, e.g. "mandelbrot".</summary>
        string PlaneFractalId { get; }

        /// <summary>Parameter holding the point's real part. Its descriptor's range bounds the map.</summary>
        string RealKey { get; }

        /// <summary>Parameter holding the point's imaginary part.</summary>
        string ImaginaryKey { get; }

        /// <summary>
        /// The part of the plane the map shows at first - the whole set, tightly. The map fits it
        /// inside its own shape, so it is a rectangle rather than a view: a wide map and a tall one
        /// both show all of it.
        /// </summary>
        PlaneRect PlaneBounds { get; }

        /// <summary>
        /// Points worth starting from, in display order. Their names are locale keys,
        /// <c>fractal.&lt;id&gt;.preset.&lt;preset id&gt;</c>, falling back to the preset id.
        /// </summary>
        IReadOnlyList<PlanePreset> Presets { get; }
    }

    /// <summary>An axis-aligned rectangle of the complex plane.</summary>
    public readonly struct PlaneRect
    {
        public PlaneRect(double minX, double maxX, double minY, double maxY)
        {
            MinX = System.Math.Min(minX, maxX);
            MaxX = System.Math.Max(minX, maxX);
            MinY = System.Math.Min(minY, maxY);
            MaxY = System.Math.Max(minY, maxY);
        }

        public double MinX { get; }
        public double MaxX { get; }
        public double MinY { get; }
        public double MaxY { get; }

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public double CenterX => (MinX + MaxX) * 0.5d;
        public double CenterY => (MinY + MaxY) * 0.5d;
    }

    /// <summary>A named point of a <see cref="IParameterPlane"/>: a Julia constant with a reputation.</summary>
    public readonly struct PlanePreset
    {
        public PlanePreset(string id, double real, double imaginary)
        {
            Id = id;
            Real = real;
            Imaginary = imaginary;
        }

        /// <summary>Stable key for the locale name. Never rename a shipped one.</summary>
        public string Id { get; }

        public double Real { get; }
        public double Imaginary { get; }
    }
}
