using System;

namespace FractalVisio.Core
{
    /// <summary>
    /// How fast the view is currently zooming out, smoothed, and what field of view a render should
    /// therefore cover.
    ///
    /// Zoom-out is the one motion a finished frame cannot follow: the viewer asks to see more than
    /// was ever computed. The answer used by the reference app is to render a wider field than the
    /// screen while the gesture runs and accept the coarser pixels, rather than to render more
    /// pixels. Sizing that field from the measured rate is what makes it right for both a slow
    /// scroll and a fast pinch; a constant margin can only ever be right for one of them.
    ///
    /// Pure arithmetic on purpose: the caller supplies the timestep, so the same code drives the
    /// live screen and any future replay or capture path.
    /// </summary>
    public struct ViewMotion
    {
        /// <summary>Time constant of the rate estimate. Short enough to react inside one gesture.</summary>
        private const double SmoothingSeconds = 0.18d;

        /// <summary>A frame longer than this is a hitch or a resume; it says nothing about speed.</summary>
        private const double MaximumStepSeconds = 0.25d;

        private double previousScale;
        private double rate;
        private bool primed;

        /// <summary>Smoothed d(ln scale)/dt, clamped to the zoom-out direction. Zero when settled.</summary>
        public readonly double ZoomOutRate => Math.Max(0d, rate);

        public void Reset()
        {
            previousScale = 0d;
            rate = 0d;
            primed = false;
        }

        public void Sample(double scale, double deltaSeconds)
        {
            if (!(scale > 0d) || double.IsNaN(scale))
            {
                return;
            }

            if (!primed)
            {
                previousScale = scale;
                primed = true;
                return;
            }

            if (!(deltaSeconds > 0d))
            {
                return;
            }

            var step = Math.Min(deltaSeconds, MaximumStepSeconds);
            var instant = Math.Log(scale / previousScale) / step;
            var alpha = 1d - Math.Exp(-step / SmoothingSeconds);
            rate += (instant - rate) * alpha;
            previousScale = scale;
        }

        /// <summary>
        /// Field of view a render should cover, as a multiple of the visible area: how much wider
        /// the view will have become by the time the frame lands, floored at
        /// <paramref name="baseFactor"/> and capped at <paramref name="maximumFactor"/>.
        /// </summary>
        public readonly double FieldFactor(double lookaheadSeconds, double baseFactor, double maximumFactor)
        {
            var floor = Math.Max(1d, baseFactor);
            var ceiling = Math.Max(floor, maximumFactor);
            var predicted = floor * Math.Exp(ZoomOutRate * Math.Max(0d, lookaheadSeconds));
            return Math.Min(ceiling, predicted);
        }
    }
}
