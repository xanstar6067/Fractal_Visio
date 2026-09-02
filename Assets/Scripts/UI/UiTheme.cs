using UnityEngine;

namespace FractalVisio.UI
{
    /// <summary>
    /// One place for every colour and size in the UI. Sizes are expressed in reference pixels and
    /// scaled by screen height, because the canvas runs in raw device pixels - a 56 pixel button is
    /// a speck on a 2400 pixel tall phone.
    /// </summary>
    public static class UiTheme
    {
        private const float ReferenceHeight = 800f;

        public static float Scale => Mathf.Max(1f, Screen.height / ReferenceHeight);

        /// <summary>Reference pixels to device pixels.</summary>
        public static float Px(float referencePixels) => referencePixels * Scale;

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
        public static readonly Color SegmentHover = new(0f, 0f, 0f, 0.3f);
        public static readonly Color SegmentSelected = new(0.42f, 0.76f, 1f, 0.32f);

        // Metrics, in reference pixels
        public const float PanelRadius = 18f;
        public const float PanelPadding = 18f;
        public const float PanelWidth = 312f;
        public const float RowSpacing = 14f;
        public const float SegmentHeight = 42f;
        public const float SegmentRadius = 12f;
        public const float ToggleSize = 52f;
        public const float ScreenMargin = 18f;
        public const int TitleFontSize = 18;
        public const int LabelFontSize = 12;
        public const int SegmentFontSize = 15;

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
