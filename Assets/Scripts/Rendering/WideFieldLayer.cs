using System;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// A small, coarse render of a far wider field than the screen, kept behind the sharp frame.
    ///
    /// It exists for the one motion a finished frame cannot follow. A pan or a zoom-in asks to see
    /// area the last frame already contains, so placing that frame is enough. A zoom-out asks to
    /// see area that was never computed, and no margin around a single frame is wide enough for a
    /// gesture that can double the field in a few hundred milliseconds. Something correct has to
    /// already be there, and that is this: a backdrop covering several times the screen, refreshed
    /// only when the viewer is about to leave it.
    ///
    /// It is cheap because it is deliberately bad: a few hundred pixels on the long edge, a couple
    /// of workers, and it only re-renders on the rare occasions the policy below asks for it.
    /// </summary>
    public sealed class WideFieldLayer : IDisposable
    {
        /// <summary>
        /// Refresh once the viewer is within this much of the edge of the coverage, as a fraction
        /// of the display. Negative, so the render starts before anything uncovered is on screen.
        /// </summary>
        private const float RefreshOverhang = -0.12f;

        /// <summary>Refresh at once, running render or not, past this much uncovered display.</summary>
        private const float UrgentOverhang = 0.02f;

        /// <summary>
        /// How far the backdrop's own scale may drift from the requested field before it is worth
        /// redoing. Wide bounds on purpose: re-rendering it on every zoom step would defeat the
        /// point of having it.
        /// </summary>
        private const double ScaleDriftLow = 0.35d;
        private const double ScaleDriftHigh = 6d;

        private const double MinimumRefreshSeconds = 0.35d;

        /// <summary>
        /// Floor on restarts even when the viewer has already left the coverage. Without it a fast
        /// zoom-out restarts the render every frame and no pass ever lands, which is the one
        /// outcome worse than a stale backdrop.
        /// </summary>
        private const double UrgentRefreshSeconds = 0.15d;

        private readonly FractalCpuRenderer renderer;

        private Texture2D texture;
        private Viewport viewport;
        private double lastRequestTime = double.NegativeInfinity;
        private double requestedFieldFactor = 1d;

        public WideFieldLayer(Gradient gradient, int maximumWorkers)
        {
            renderer = new FractalCpuRenderer(gradient, Mathf.Max(1, maximumWorkers));
        }

        /// <summary>The backdrop image, or null before the first pass lands.</summary>
        public Texture2D Texture => renderer.HasPublished ? texture : null;

        public bool HasFrame => renderer.HasPublished && texture != null;

        public ViewState FrameView => renderer.PublishedView;

        public double FrameAspect => renderer.PublishedAspect;

        public bool IsBusy => renderer.IsBusy;

        /// <summary>Resize the backdrop buffer. Drops whatever it held; the next tick re-renders.</summary>
        public void Resize(in Viewport value)
        {
            if (texture != null && viewport.Width == value.Width && viewport.Height == value.Height)
            {
                return;
            }

            renderer.CompletePendingWork();
            DestroyTexture();

            viewport = value;
            texture = new Texture2D(viewport.Width, viewport.Height, TextureFormat.RGBA32, false, false)
            {
                name = "Fractal Wide Field",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[viewport.Width * viewport.Height];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = FractalCpuRenderer.InteriorColor;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            lastRequestTime = double.NegativeInfinity;
        }

        /// <summary>What the backdrop is a picture of stopped being true: fractal, parameters, palette.</summary>
        public void Discard()
        {
            renderer.DiscardPublished();
            lastRequestTime = double.NegativeInfinity;
        }

        /// <summary>Stop and forget everything. Used when the CPU path is not the active backend.</summary>
        public void Suspend()
        {
            renderer.Invalidate();
        }

        /// <summary>
        /// Decide whether the backdrop still covers <paramref name="view"/> and re-render it if not.
        /// Call once per frame while the CPU path is active.
        /// </summary>
        public void Tick(
            IFractalDefinition definition,
            in FractalParameterSet parameters,
            in ViewState view,
            double fieldFactor,
            int iterations,
            bool extendedPrecision,
            double displayAspect)
        {
            renderer.Update();

            if (texture == null || definition == null)
            {
                return;
            }

            if (!ShouldRefresh(view, fieldFactor, displayAspect))
            {
                return;
            }

            var wideView = view;
            wideView.scale = new HighPrecision(view.scale.AsDecimal * (decimal)Math.Max(1d, fieldFactor));

            requestedFieldFactor = Math.Max(1d, fieldFactor);
            lastRequestTime = Time.realtimeSinceStartupAsDouble;

            renderer.Request(
                texture,
                viewport,
                definition,
                parameters,
                wideView,
                iterations,
                extendedPrecision,
                false);
        }

        public void Dispose()
        {
            renderer.Dispose();
            DestroyTexture();
        }

        private bool ShouldRefresh(in ViewState view, double fieldFactor, double displayAspect)
        {
            if (!renderer.HasPublished)
            {
                // Nothing to show yet. One request in flight is enough; do not restart it.
                return !renderer.IsBusy;
            }

            var placement = FramePlacement.Resolve(renderer.PublishedView, renderer.PublishedAspect, view, displayAspect);
            if (!placement.IsValid)
            {
                return !renderer.IsBusy;
            }

            var sinceRequest = Time.realtimeSinceStartupAsDouble - lastRequestTime;

            if (placement.Overhang >= UrgentOverhang)
            {
                return sinceRequest >= UrgentRefreshSeconds;
            }

            if (renderer.IsBusy)
            {
                return false;
            }

            if (sinceRequest < MinimumRefreshSeconds)
            {
                return false;
            }

            if (placement.Overhang >= RefreshOverhang)
            {
                return true;
            }

            // The backdrop is still covering, but the view may have zoomed far enough that its
            // pixels are meaningless (too coarse) or that it is barely wider than the screen.
            var currentScale = view.scale.AsDouble;
            var frameScale = renderer.PublishedView.scale.AsDouble;
            if (!(currentScale > 0d) || !(frameScale > 0d))
            {
                return true;
            }

            var drift = frameScale / currentScale / Math.Max(1d, requestedFieldFactor);
            return drift < ScaleDriftLow || drift > ScaleDriftHigh;
        }

        private void DestroyTexture()
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
