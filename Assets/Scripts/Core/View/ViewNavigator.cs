using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Pure view arithmetic: pixels in, <see cref="ViewState"/> out. Holds no Unity objects and
    /// never reads <c>Screen</c> - the caller supplies the <see cref="Viewport"/>, so the same
    /// code serves the live screen, an off-screen capture and future tools such as bookmarks.
    /// </summary>
    public static class ViewNavigator
    {
        /// <summary>Fractal-plane point under a pixel of the given viewport.</summary>
        public static (decimal x, decimal y) ScreenToFractal(in ViewState view, in Viewport viewport, Vector2 point)
        {
            var aspect = viewport.Aspect;
            var nx = ((double)point.x / viewport.Width - 0.5d) * aspect;
            var ny = (double)point.y / viewport.Height - 0.5d;
            var (rx, ry) = Rotate(nx, ny, view.rotation);

            var scale = view.scale.AsDecimal;
            return (
                view.x.AsDecimal + (decimal)rx * scale,
                view.y.AsDecimal + (decimal)ry * scale);
        }

        /// <summary>Drag by a screen-space delta, turned into view space by the current rotation.</summary>
        public static void Pan(ref ViewState view, in Viewport viewport, Vector2 deltaPixels)
        {
            var aspect = viewport.Aspect;
            var ndx = -(double)deltaPixels.x / viewport.Width * aspect;
            var ndy = -(double)deltaPixels.y / viewport.Height;
            var (rx, ry) = Rotate(ndx, ndy, view.rotation);

            var scale = view.scale.AsDecimal;
            view.x = new HighPrecision(view.x.AsDecimal + (decimal)rx * scale);
            view.y = new HighPrecision(view.y.AsDecimal + (decimal)ry * scale);
        }

        /// <summary>
        /// Two-finger transform: zoom and rotate about the pinch pivot, then slide the centre so
        /// the fractal point that was under the pivot is under it again.
        /// </summary>
        public static void PinchZoomRotate(
            ref ViewState view,
            in Viewport viewport,
            Vector2 previousPivot,
            Vector2 currentPivot,
            float rawZoomRatio,
            float zoomSpeed,
            float rotationDelta,
            double minimumScale,
            double maximumScale)
        {
            var safeRatio = Mathf.Max(0.01f, rawZoomRatio);
            var zoomRatio = Math.Pow(safeRatio, zoomSpeed);

            // Fractal point under the pivot before the transform.
            var anchor = ScreenToFractal(view, viewport, previousPivot);

            var newScale = Math.Clamp(view.scale.AsDouble / zoomRatio, minimumScale, maximumScale);
            view.scale = HighPrecision.FromDouble(newScale);
            view.rotation += rotationDelta;

            // With a rotation-aware mapping, re-anchoring also turns the view about the pivot.
            var moved = ScreenToFractal(view, viewport, currentPivot);
            view.x = new HighPrecision(view.x.AsDecimal + anchor.x - moved.x);
            view.y = new HighPrecision(view.y.AsDecimal + anchor.y - moved.y);
        }

        /// <summary>
        /// The view a buffer must draw so that its visible sub-rectangle shows exactly
        /// <paramref name="view"/>. A viewport with overscan covers a wider field, so the span
        /// grows by its vertical field scale; the horizontal side follows from the buffer aspect.
        /// The widening is done in decimal so deep-zoom precision survives it.
        /// </summary>
        public static ViewState ForViewport(in ViewState view, in Viewport viewport)
        {
            if (!viewport.HasOverscan)
            {
                return view;
            }

            var widened = view;
            widened.scale = new HighPrecision(view.scale.AsDecimal * (decimal)viewport.FieldScaleY);
            return widened;
        }

        private static (double x, double y) Rotate(double x, double y, double radians)
        {
            var cos = Math.Cos(radians);
            var sin = Math.Sin(radians);
            return (x * cos - y * sin, x * sin + y * cos);
        }
    }
}
