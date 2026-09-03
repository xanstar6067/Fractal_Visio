using UnityEngine;

namespace FractalVisio.UI
{
    /// <summary>
    /// One place for every colour and size in the UI.
    ///
    /// Sizes are written in <b>density-independent pixels</b> - the Android convention, where one
    /// unit is one pixel at 160 dpi - and <see cref="Px"/> converts them to the device pixels the
    /// canvas actually runs in. The scale therefore comes from <see cref="Screen.dpi"/>, not from
    /// the resolution: a control has to be a certain number of millimetres wide to be hit with a
    /// finger, and pixel counts say nothing about millimetres. Scaling by screen height, which is
    /// what this did before, made every control shrink to a third of its size the moment the phone
    /// was held in landscape.
    ///
    /// Because `Screen.dpi` is unreliable on some Android devices (it can be zero, or the panel's
    /// nominal rather than actual density), the result is clamped and multiplied by
    /// <see cref="UserScale"/>, which the interface section of the settings panel drives. That
    /// setting is the escape hatch for a device that lies about itself.
    /// </summary>
    public static class UiTheme
    {
        /// <summary>Density the reference pixels are quoted at.</summary>
        private const float ReferenceDpi = 160f;

        /// <summary>Plausible range for a reported dpi. Outside it, the value is not believed.</summary>
        private const float MinimumBelievableDpi = 50f;
        private const float MaximumBelievableDpi = 900f;

        private const float MinimumScale = 1f;
        private const float MaximumScale = 6f;

        /// <summary>Short edge, in dp, a phone is assumed to be when the dpi cannot be trusted.</summary>
        private const float FallbackShortEdgeDp = 360f;

        private static float userScale = 1f;

        /// <summary>
        /// User multiplier on top of the device scale, from the settings panel. Changing it does
        /// not rescale anything by itself - <see cref="UiRouter"/> rebuilds the interface, because
        /// the rounded-corner sprites are generated at a fixed pixel radius.
        /// </summary>
        public static float UserScale
        {
            get => userScale;
            set => userScale = Mathf.Clamp(value, 0.6f, 2f);
        }

        /// <summary>What one reference pixel is worth on this screen, before the user multiplier.</summary>
        public static float DeviceScale
        {
            get
            {
                var dpi = Screen.dpi;
                if (dpi >= MinimumBelievableDpi && dpi <= MaximumBelievableDpi)
                {
                    return dpi / ReferenceDpi;
                }

                if (!Application.isMobilePlatform)
                {
                    return 1f;
                }

                var shortEdge = Mathf.Min(Mathf.Max(1, Screen.width), Mathf.Max(1, Screen.height));
                return shortEdge / FallbackShortEdgeDp;
            }
        }

        public static float Scale => Mathf.Clamp(DeviceScale * userScale, MinimumScale, MaximumScale);

        /// <summary>Reference pixels (dp) to device pixels.</summary>
        public static float Px(float referencePixels) => referencePixels * Scale;

        /// <summary>Same, rounded to a whole pixel and never below one - for radii and hairlines.</summary>
        public static int PxInt(float referencePixels) => Mathf.Max(1, Mathf.RoundToInt(Px(referencePixels)));

        // Glass. The tint has to be opaque enough that white text stays readable over the brightest
        // part of a fractal - at 0.6 the panel went pale green over a yellow band and the labels
        // disappeared. It still takes the colour of whatever is behind it, just darker.
        public static readonly Color PanelTint = new(0.035f, 0.045f, 0.075f, 0.76f);
        public static readonly Color PanelBorder = new(1f, 1f, 1f, 0.18f);
        public static readonly Color ButtonTint = new(0.035f, 0.045f, 0.075f, 0.7f);
        public static readonly Color ButtonBorder = new(1f, 1f, 1f, 0.2f);

        /// <summary>Hairline along the inside of the top edge, the way light catches real glass.</summary>
        public static readonly Color Highlight = new(1f, 1f, 1f, 0.1f);

        // Content
        public static readonly Color Text = new(0.94f, 0.95f, 0.98f, 1f);
        public static readonly Color TextMuted = new(0.72f, 0.75f, 0.82f, 1f);
        public static readonly Color Accent = new(0.42f, 0.76f, 1f, 1f);
        // Rows darken the glass rather than lightening it: a white overlay reads as a bright patch
        // over a yellow band and disappears over a dark one, while a black one recesses on both.
        public static readonly Color SegmentIdle = new(0f, 0f, 0f, 0.22f);
        public static readonly Color SegmentSelected = new(0.42f, 0.76f, 1f, 0.32f);

        // Metrics, in reference pixels (dp)
        public const float PanelRadius = 18f;
        public const float PanelPadding = 16f;
        public const float PanelWidth = 300f;
        public const float SectionSpacing = 16f;
        public const float RowSpacing = 8f;

        /// <summary>
        /// Row height. 48 dp is the smallest target a finger hits reliably; everything tappable in
        /// this UI is at least this tall, and nothing should be added below it.
        /// </summary>
        public const float SegmentHeight = 48f;

        public const float SegmentRadius = 12f;
        public const float ToggleSize = 56f;
        public const float ScreenMargin = 16f;
        public const int TitleFontSize = 19;
        public const int LabelFontSize = 13;
        public const int SegmentFontSize = 16;

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
