using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// Small pictures drawn beside the session - the gallery's previews, the C map - drawn live
    /// rather than shipped as images: every fractal that has a GPU shader can draw its own preview in
    /// a millisecond, and a drawn preview follows the user's palette.
    /// </summary>
    public interface IFractalThumbnails
    {
        /// <summary>
        /// The preview of <paramref name="entry"/>, about <paramref name="size"/> pixels square, in the
        /// session's palette. Null until it has been drawn - previews are drawn a few per frame, so
        /// ask again next frame - and null for good when this device cannot draw that fractal on the
        /// GPU. The same texture is redrawn in place when the palette changes.
        /// </summary>
        Texture Get(in CatalogEntry entry, int size);

        /// <summary>
        /// Draw <paramref name="definition"/> at <paramref name="view"/> into
        /// <paramref name="target"/> now, in the session's palette - for a picture its owner redraws
        /// itself, like a map the user pans. The iteration budget is the session's for that scale.
        /// Returns false, drawing nothing, when this device cannot draw the fractal on the GPU.
        /// </summary>
        bool Draw(IFractalDefinition definition, in FractalParameterSet parameters, in ViewState view, RenderTexture target);

        /// <summary>
        /// Counts palette and colouring changes. A picture drawn with <see cref="Draw"/> is stale
        /// once this differs from the value it was drawn at.
        /// </summary>
        int ColoringVersion { get; }
    }
}
