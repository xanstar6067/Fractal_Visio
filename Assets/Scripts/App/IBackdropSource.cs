using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Read-only access to the image currently on screen. The UI uses it to blur what is behind a
    /// panel; nothing here lets a caller change what is rendered.
    /// </summary>
    public interface IBackdropSource
    {
        /// <summary>The texture the viewer is looking at, or null before the first render.</summary>
        Texture Texture { get; }

        /// <summary>
        /// Sub-rectangle of <see cref="Texture"/> that fills the screen. The render buffers carry an
        /// overscan margin, so this is not the whole texture.
        /// </summary>
        Rect UvRect { get; }
    }
}
