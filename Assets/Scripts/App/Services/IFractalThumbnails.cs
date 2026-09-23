using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// Preview pictures for the gallery, drawn live rather than shipped as images: every fractal
    /// that has a GPU shader can draw its own preview in a millisecond, and a drawn preview follows
    /// the user's palette.
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
    }
}
