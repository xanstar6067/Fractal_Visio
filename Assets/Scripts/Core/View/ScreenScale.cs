using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// How big a physical thing is on this screen, in device pixels.
    ///
    /// Everything the user touches or reads has to be sized in millimetres, not in pixels: a
    /// 56-pixel button is comfortable on a laptop and invisible on a phone. The unit here is the
    /// Android convention - one dp is one pixel at 160 dpi - and <see cref="Dp"/> converts.
    ///
    /// <b>Do not trust <see cref="Screen.dpi"/> alone.</b> On Android it is whatever the vendor put
    /// in the panel metadata; it comes back as zero, as a nominal bucket, or simply wrong often
    /// enough that a UI sized purely from it can end up unusable on real hardware - which is
    /// exactly what happened here. So the density is the larger of what the device claims and what
    /// its resolution implies: the second value is a floor no bad metadata can push through.
    ///
    /// Lives in Core because both the interface (<c>UiTheme</c>) and the gesture layer need it, and
    /// those two assemblies cannot see each other.
    /// </summary>
    public static class ScreenScale
    {
        /// <summary>Density the dp unit is defined at.</summary>
        private const float ReferenceDpi = 160f;

        /// <summary>Outside this range a reported dpi is metadata noise, not a measurement.</summary>
        private const float MinimumBelievableDpi = 50f;
        private const float MaximumBelievableDpi = 900f;

        /// <summary>
        /// Short edge, in dp, a handheld screen is assumed to be. Phones land between roughly 320
        /// and 420 dp regardless of their pixel count, so dividing the short edge by this is a
        /// sound floor for the density even when the device says nothing useful.
        /// </summary>
        private const float HandheldShortEdgeDp = 380f;

        private const float MinimumDensity = 1f;
        private const float MaximumDensity = 8f;

        /// <summary>Device pixels per dp.</summary>
        public static float Density
        {
            get
            {
                var reported = 0f;
                var dpi = Screen.dpi;
                if (dpi >= MinimumBelievableDpi && dpi <= MaximumBelievableDpi)
                {
                    reported = dpi / ReferenceDpi;
                }

                var implied = 0f;
                if (Application.isMobilePlatform)
                {
                    var shortEdge = Mathf.Min(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                    implied = shortEdge / HandheldShortEdgeDp;
                }

                return Mathf.Clamp(Mathf.Max(reported, implied), MinimumDensity, MaximumDensity);
            }
        }

        /// <summary>Density-independent pixels to device pixels.</summary>
        public static float Dp(float value) => value * Density;
    }
}
