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

        // Layout. The explorer has a gallery button in the top-left corner and a toolbar along the
        // bottom - down the right edge in landscape - and a panel opens beside the toolbar: a sheet
        // above it in portrait, a column left of it in landscape. Everything stays inside the
        // screen's safe area, clear of cut-outs and rounded corners.

        /// <summary>
        /// Landscape puts the toolbar down the right edge instead of along the bottom: a phone on
        /// its side has little height, and a bottom bar plus a panel above it would leave none.
        /// </summary>
        public static bool ToolbarVertical => Screen.width > Screen.height;

        public static float SafeLeft => Mathf.Max(0f, Screen.safeArea.xMin);

        public static float SafeRight => Mathf.Max(0f, Screen.width - Screen.safeArea.xMax);

        public static float SafeBottom => Mathf.Max(0f, Screen.safeArea.yMin);

        public static float SafeTop => Mathf.Max(0f, Screen.height - Screen.safeArea.yMax);

        /// <summary>Height of the top bar - the gallery button - in device pixels. A full touch target.</summary>
        public static float TopBarHeight => Px(SegmentHeight);

        /// <summary>
        /// Toolbar depth across its long side: its height along the bottom, its width down the
        /// side. Only the length along the bar gives way when space runs out; this does not.
        /// </summary>
        public static float ToolbarThickness =>
            (ToolbarVertical ? Px(ToolbarItemWidth) : Px(ToolbarItemHeight)) + Px(ToolbarPadding) * 2f;

        /// <summary>
        /// Size of one toolbar button for <paramref name="count"/> buttons: full size when they fit,
        /// shorter along the bar when they do not - a large interface scale on a narrow phone must
        /// squeeze the bar, not push buttons off the screen.
        /// </summary>
        public static Vector2 ToolbarItemSize(int count)
        {
            var width = Px(ToolbarItemWidth);
            var height = Px(ToolbarItemHeight);
            var margin = Px(ScreenMargin);
            var padding = Px(ToolbarPadding);
            count = Mathf.Max(1, count);

            if (ToolbarVertical)
            {
                var room = Screen.height - SafeTop - SafeBottom - margin * 3f - TopBarHeight - padding * 2f;
                height = Mathf.Min(height, Mathf.Max(1f, room) / count);
            }
            else
            {
                var room = Screen.width - SafeLeft - SafeRight - margin * 2f - padding * 2f;
                width = Mathf.Min(width, Mathf.Max(1f, room) / count);
            }

            return new Vector2(width, height);
        }

        /// <summary>
        /// Put a panel of the given size beside the toolbar: centred above it in portrait, left of
        /// it in the bottom-right corner in landscape. Both leave the top bar uncovered.
        /// </summary>
        public static void DockPanel(RectTransform panel, float width, float height)
        {
            var margin = Px(ScreenMargin);
            if (ToolbarVertical)
            {
                UiFactory.Anchor(
                    panel,
                    new Vector2(1f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(-(SafeRight + margin * 2f + ToolbarThickness), SafeBottom + margin),
                    new Vector2(width, height));
                return;
            }

            UiFactory.Anchor(
                panel,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2((SafeLeft - SafeRight) * 0.5f, SafeBottom + margin * 2f + ToolbarThickness),
                new Vector2(width, height));
        }

        /// <summary>
        /// Screen height a panel may occupy: the safe area less the top bar, the toolbar when it runs
        /// along the bottom, and the margins between them.
        /// </summary>
        public static float AvailablePanelHeight
        {
            get
            {
                var margin = ScreenMargin * Scale;
                var usable = Screen.height - SafeTop - SafeBottom;
                var reserved = TopBarHeight + (ToolbarVertical ? margin * 3f : margin * 4f + ToolbarThickness);
                return Mathf.Max(64f, usable - reserved);
            }
        }

        /// <summary>Width a panel may occupy: the safe area less the margins, and the toolbar when it runs down the side.</summary>
        public static float AvailablePanelWidth
        {
            get
            {
                var margin = Px(ScreenMargin);
                var usable = Screen.width - SafeLeft - SafeRight;
                var reserved = ToolbarVertical ? margin * 3f + ToolbarThickness : margin * 2f;
                return Mathf.Max(64f, usable - reserved);
            }
        }

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

        /// <summary>
        /// The gallery covers the whole screen, so it is darker than a panel: at panel strength a
        /// bright band of the picture behind came through every card and label at once.
        /// </summary>
        public static readonly Color GalleryTint = new(0.03f, 0.035f, 0.06f, 0.84f);

        /// <summary>A gallery card: recessed into the glass, like an option row.</summary>
        public static readonly Color CardFill = new(0f, 0f, 0f, 0.3f);

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

        /// <summary>
        /// Reference size of a chrome button. The toolbar buttons are drawn at their own size below;
        /// this stays as the yardstick the chrome scale is bounded by (<see cref="ShortEdgeCeiling"/>).
        /// </summary>
        public const float ToggleSize = 66f;

        public const float ScreenMargin = 18f;

        /// <summary>One toolbar button: an icon over a one-word label. Wider than a bare icon button, because of the label.</summary>
        public const float ToolbarItemWidth = 68f;
        public const float ToolbarItemHeight = 60f;
        public const float ToolbarPadding = 4f;
        public const float ToolbarRadius = 22f;
        public const int ToolbarLabelFontSize = 11;

        public const int TitleFontSize = 22;
        public const int LabelFontSize = 14;
        public const int SegmentFontSize = 18;

        // Gallery
        /// <summary>Narrowest a gallery card may get before the grid drops a column: two columns on a phone held upright.</summary>
        public const float CardMinimumWidth = 150f;
        public const int CardMaximumColumns = 6;
        public const float CardRadius = 16f;
        public const float CardSpacing = 12f;
        public const int CardTitleFontSize = 16;
        public const int CardDetailFontSize = 12;
        public const float ChipHeight = 48f;

        /// <summary>The favourite star's gold: warm enough to read as "marked" against any palette.</summary>
        public static readonly Color Favorite = new(1f, 0.8f, 0.3f, 1f);

        /// <summary>Behind text laid over a picture: dark enough to read white on a white band.</summary>
        public static readonly Color Scrim = new(0f, 0f, 0f, 0.55f);

        public static Font Font => Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
    }
}
