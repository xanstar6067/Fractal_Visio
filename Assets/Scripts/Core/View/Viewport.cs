using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Geometry of a render target. Replaces direct <c>Screen.width/height</c> reads inside view
    /// math, so the same code drives the live screen, an off-screen capture buffer of any size,
    /// and a buffer that deliberately covers more than the viewer sees.
    ///
    /// A viewport separates two sizes: the buffer (<see cref="Width"/> x <see cref="Height"/>)
    /// and the part of it the viewer actually sees (<see cref="VisibleWidth"/> x
    /// <see cref="VisibleHeight"/>). The difference is overscan: real computed pixels kept in
    /// reserve around the image. A pan or a zoom-out reveals those instead of a stretched edge,
    /// which is what removes the smeared bars during a gesture.
    /// </summary>
    public readonly struct Viewport
    {
        /// <summary>Viewport with no overscan: the buffer is exactly what is seen.</summary>
        public Viewport(int width, int height)
        {
            Width = Mathf.Max(1, width);
            Height = Mathf.Max(1, height);
            VisibleWidth = Width;
            VisibleHeight = Height;
        }

        public Viewport(int visibleWidth, int visibleHeight, int bufferWidth, int bufferHeight)
        {
            VisibleWidth = Mathf.Max(1, visibleWidth);
            VisibleHeight = Mathf.Max(1, visibleHeight);
            Width = Mathf.Max(VisibleWidth, bufferWidth);
            Height = Mathf.Max(VisibleHeight, bufferHeight);
        }

        /// <summary>Buffer width in pixels, margins included.</summary>
        public int Width { get; }

        /// <summary>Buffer height in pixels, margins included.</summary>
        public int Height { get; }

        public int VisibleWidth { get; }
        public int VisibleHeight { get; }

        /// <summary>Aspect of the buffer. Renderers map their normalised coordinates by this.</summary>
        public double Aspect => (double)Width / Height;

        public bool HasOverscan => Width != VisibleWidth || Height != VisibleHeight;

        /// <summary>How much wider than the visible area the buffer is, per axis.</summary>
        public double FieldScaleX => (double)Width / VisibleWidth;

        public double FieldScaleY => (double)Height / VisibleHeight;

        /// <summary>Margin beyond one edge as a fraction of the visible size, averaged over both axes.</summary>
        public float Overscan => (float)((FieldScaleX + FieldScaleY) * 0.5d - 1d) * 0.5f;

        /// <summary>Sub-rectangle of the buffer the viewer sees. Feed this to <c>RawImage.uvRect</c>.</summary>
        public Rect VisibleUvRect
        {
            get
            {
                var spanX = (float)(1d / FieldScaleX);
                var spanY = (float)(1d / FieldScaleY);
                return new Rect((1f - spanX) * 0.5f, (1f - spanY) * 0.5f, spanX, spanY);
            }
        }

        /// <summary>The visible area as a pixel rectangle inside the buffer, centred in it.</summary>
        public RectInt VisibleRect => new(
            (Width - VisibleWidth) / 2,
            (Height - VisibleHeight) / 2,
            VisibleWidth,
            VisibleHeight);

        /// <summary>The visible part of this viewport, as a viewport of its own (no overscan).</summary>
        public Viewport Visible => new(VisibleWidth, VisibleHeight);
    }
}
