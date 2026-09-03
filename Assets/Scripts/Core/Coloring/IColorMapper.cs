using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Turns a buffer of escape values into pixels. The renderer keeps the escape values, not the
    /// colours, so changing a palette or a colouring setting costs one pass over that buffer
    /// (milliseconds) instead of recomputing the fractal (seconds at depth).
    ///
    /// A range rather than the whole buffer, and arrays rather than spans: the caller splits the
    /// image across threads, and a <c>Span</c> cannot be captured by the lambda that would do it.
    /// One virtual call per chunk is free; one per pixel would not be.
    /// </summary>
    public interface IColorMapper
    {
        /// <summary>
        /// Map <paramref name="count"/> values starting at <paramref name="start"/>. A negative
        /// escape value means the point never escaped - see <see cref="IEscapeSamplerD"/>.
        /// </summary>
        void MapRange(
            float[] escapeValues,
            Color32[] target,
            int start,
            int count,
            PaletteData palette,
            in ColoringSettings settings);
    }
}
