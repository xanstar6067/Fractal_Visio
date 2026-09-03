using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// One place for every colour and size in the UI.
    ///
    /// Sizes are written in <b>density-independent pixels</b> and <see cref="Px"/> converts them
    /// to the device pixels the canvas runs in, using <see cref="ScreenScale.Density"/>. A control
    /// has to be a certain number of millimetres wide to be hit with a finger, and pixel counts say
    /// nothing about millimetres - scaling by screen height, which is what this did originally,
    /// made every control a third of its size the moment the phone was held in landscape.
    ///
    /// <see cref="UserScale"/> - the INTERFACE SIZE setting - multiplies on top. It exists because
    /// a device can misreport its density badly enough that even a correct formula lands too small,
    /// which is what the first device test found; the only reliable fix for that is a knob.
    /// </summary>
    public static class UiTheme
    {
        private const float MinimumScale = 1f;
        private const float MaximumScale = 12f;

        /// <summary>
        /// Content the settings panel must be able to show without scrolling, in dp: a title, a
        /// section label and four rows. This is what stops the interface scale from growing until
        /// two options fill the screen - the one limit worth enforcing, because a panel you cannot
        /// see the shape of is worse than small text.
        /// </summary>
        private const float MinimumPanelHeight = 300f;

        private static float userScale = 1f;

        /// <summary>
        /// User multiplier on top of the device density, from the settings panel. Changing it does
        /// not rescale anything by itself - <see cref="UiRouter"/> rebuilds the interface, because
        /// the rounded-corner sprites are generated at a fixed pixel radius.
        /// </summary>
        public static float UserScale
        {
            get => userScale;
            set => userScale = Mathf.Clamp(value, 0.6f, 2.5f);
        }

        /// <summary>What one reference pixel is worth on this screen, before the user multiplier.</summary>
        public static float DeviceScale => ScreenScale.Density;

        /// <summary>
        /// Fraction of the screen's short edge the settings button may occupy. This is what keeps
        /// the interface scale tied to the screen rather than to a number the user picked in a
        /// vacuum: the same multiplier that is comfortable on a tall phone in portrait puts a
        /// button across a quarter of the screen when it is turned sideways.
        /// </summary>
        private const float ToggleShortEdgeFraction = 0.28f;

        /// <summary>Ceiling the screen imposes on the chrome, from the short edge.</summary>
        private static float ShortEdgeCeiling =>
            Mathf.Max(MinimumScale, Mathf.Min(Screen.width, Screen.height) * ToggleShortEdgeFraction / ToggleSize);

        /// <summary>
        /// Scale for chrome: the settings button, the HUD, anything that is one control with
        /// nothing inside it to overflow.
        /// </summary>
        public static float Scale =>
            Mathf.Clamp(Mathf.Min(DeviceScale * userScale, ShortEdgeCeiling), MinimumScale, MaximumScale);

        /// <summary>
        /// Scale for the settings panel, which is chrome scale bounded by the room actually left
        /// for it. The panel is the one thing here with enough content to run out of screen, and it
        /// is the reason the two scales are separate: a button can be as big as the user likes,
        /// while a list of options has to stay a list.
        /// </summary>
        public static float PanelScale =>
            Mathf.Min(Scale, Mathf.Max(MinimumScale, AvailablePanelHeight / MinimumPanelHeight));

        /// <summary>Screen height a panel may occupy: everything but the margins and the toggle.</summary>
        public static float AvailablePanelHeight =>
            Mathf.Max(64f, Screen.height - (ScreenMargin * 3f + ToggleSize) * Scale);

        /// <summary>Width a panel may occupy.</summary>
        public static float AvailablePanelWidth =>
            Mathf.Max(64f, Screen.width - Px(ScreenMargin) * 2f);

        /// <summary>Reference pixels (dp) to device pixels, at chrome scale.</summary>
        public static float Px(float referencePixels) => referencePixels * Scale;

        /// <summary>Same, rounded to a whole pixel and never below one - for radii and hairlines.</summary>
        public static int PxInt(float referencePixels) => Mathf.Max(1, Mathf.RoundToInt(Px(referencePixels)));

        /// <summary>Reference pixels to device pixels, at panel scale. Everything inside a panel.</summary>
        public static float PanelPx(float referencePixels) => referencePixels * PanelScale;

        public static int PanelPxInt(float referencePixels) => Mathf.Max(1, Mathf.RoundToInt(PanelPx(referencePixels)));

        /// <summary>
        /// A padding or an inset that may not eat its container. Sizes in dp grow without limit as
        /// the interface scale rises, while the container does not: at a large scale the paddings
        /// and the marker gutter of a row consumed the whole row and the labels showed two letters.
        /// Every inset inside a fixed-width box goes through here.
        /// </summary>
        public static float PanelInset(float containerSize, float referencePixels, float maximumFraction)
        {
            return Mathf.Min(PanelPx(referencePixels), Mathf.Max(0f, containerSize) * maximumFraction);
        }

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
        public const float PanelWidth = 320f;
        public const float SectionSpacing = 16f;
        public const float RowSpacing = 8f;

        /// <summary>
        /// Row height. 48 dp is the documented floor for a finger target; 54 is what the device
        /// test actually asked for. Everything tappable is at least this tall.
        /// </summary>
        public const float SegmentHeight = 54f;

        public const float SegmentRadius = 12f;
        public const float ToggleSize = 66f;
        public const float ScreenMargin = 18f;
        public const int TitleFontSize = 22;
        public const int LabelFontSize = 14;
        public const int SegmentFontSize = 18;

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
