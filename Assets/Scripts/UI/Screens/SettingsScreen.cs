using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The settings panel: a scrolling column of <see cref="SettingsSection"/>s. Adding a setting
    /// is one section plus the two lines that read and write it on the session - the layout, the
    /// scrolling and the touch targets are already handled.
    ///
    /// It lists fractals from <c>AppServices.Catalog</c> and palettes from
    /// <see cref="PaletteLibrary"/>, both as Core types: the UI never references the Fractals
    /// assembly, so a new fractal or palette appears here on its own.
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        /// <summary>Render resolution as a fraction of the screen; 0 means the device profile decides.</summary>
        private static readonly float[] ResolutionScales = { 0f, 0.5f, 0.75f, 1f };

        private static readonly string[] ResolutionNames = { "Auto", "50%", "75%", "100%" };

        /// <summary>
        /// Colouring as three presets rather than two switches. They are not independent in
        /// practice - nobody wants unsmoothed logarithmic - and a preset row is one tap where two
        /// toggles are two.
        /// </summary>
        private static readonly (bool Smooth, ColoringMode Mode)[] ColoringPresets =
        {
            (false, ColoringMode.Linear),
            (true, ColoringMode.Linear),
            (true, ColoringMode.Logarithmic)
        };

        private static readonly string[] ColoringNames = { "Bands", "Smooth", "Smooth log" };

        // Centred on 1.0 - the density figure the screen reports - with room either side. Sizes
        // rather than adjectives: "Large" means nothing without knowing the base, XS to XXL is a
        // ladder, and none of it will need translating when stage 13 arrives.
        private static readonly float[] InterfaceScales = { 0.8f, 1f, 1.2f, 1.45f, 1.75f, 2.1f };

        private static readonly string[] InterfaceNames = { "XS", "S", "M", "L", "XL", "XXL" };

        /// <summary>Most columns to split into. Past three the rows get too narrow to read.</summary>
        private const int MaximumColumns = 3;

        private readonly List<IFractalDefinition> fractals = new();

        private SettingsSection fractalSection;
        private SettingsSection paletteSection;
        private SettingsSection coloringSection;
        private SettingsSection resolutionSection;
        private SettingsSection interfaceSection;

        protected override void OnBuild(Transform parent)
        {
            fractals.Clear();
            for (var i = 0; i < Services.Catalog.Count; i++)
            {
                fractals.Add(Services.Catalog[i]);
            }

            var palettes = PaletteLibrary.All;

            var fractalNames = new string[fractals.Count];
            for (var i = 0; i < fractals.Count; i++)
            {
                fractalNames[i] = fractals[i].DisplayName;
            }

            var paletteNames = new string[palettes.Count];
            for (var i = 0; i < palettes.Count; i++)
            {
                paletteNames[i] = palettes[i].DisplayName;
            }

            var specs = new[]
            {
                new SectionSpec("FRACTAL", fractalNames, SelectFractal),
                new SectionSpec("PALETTE", paletteNames, SelectPalette),
                new SectionSpec("COLOURING", ColoringNames, SelectColoring),
                new SectionSpec("RESOLUTION", ResolutionNames, SelectResolution),
                new SectionSpec("INTERFACE SIZE", InterfaceNames, SelectInterfaceScale)
            };

            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var availableWidth = UiTheme.AvailablePanelWidth;
            var availableHeight = UiTheme.AvailablePanelHeight;

            // Columns are how the panel uses a wide screen. One natural column is what a phone in
            // portrait has room for; a tablet or a desktop window fits two or three, which turns a
            // long scroll into something readable at a glance.
            var naturalWidth = UiTheme.PanelPx(UiTheme.PanelWidth);
            var columns = Mathf.Clamp(Mathf.FloorToInt(availableWidth / naturalWidth), 1, MaximumColumns);
            var width = Mathf.Min(columns * naturalWidth, availableWidth);

            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var sectionGap = Mathf.Min(UiTheme.PanelPx(UiTheme.SectionSpacing), availableHeight * 0.04f);
            var titleHeight = UiTheme.PanelPx(28f);
            var columnWidth = (width - padding * 2f - padding * (columns - 1)) / columns;

            // Shortest column first, so five sections of different lengths end up balanced instead
            // of one column running off the bottom while the next is half empty.
            var cursors = new float[columns];
            var top = -(padding + titleHeight + sectionGap);
            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            var plan = new int[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                var column = ShortestColumn(cursors);
                plan[i] = column;
                cursors[column] -= SettingsSection.MeasureHeight(specs[i].Options.Count) + sectionGap;
            }

            var tallest = 0f;
            for (var i = 0; i < columns; i++)
            {
                tallest = Mathf.Max(tallest, -cursors[i]);
            }

            var contentHeight = tallest - sectionGap + padding;
            var height = Mathf.Min(contentHeight, availableHeight);

            Panel = GlassPanel.Create(
                "SettingsPanel",
                parent,
                UiTheme.PanelRadius,
                UiTheme.PanelTint,
                UiTheme.PanelBorder);

            UiFactory.Anchor(
                Panel.Root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-margin, margin * 2f + UiTheme.Px(UiTheme.ToggleSize)),
                new Vector2(width, height));

            var content = BuildScroll(contentHeight, height);

            var title = UiFactory.CreateText(
                "Title", content, "Settings", UiTheme.TitleFontSize, UiTheme.Text,
                TextAnchor.UpperLeft, fitToRect: true, panelScale: true);
            Place(title.rectTransform, padding, -padding, width - padding * 2f, titleHeight);
            title.fontStyle = FontStyle.Bold;

            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            var built = new SettingsSection[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                var column = plan[i];
                var x = padding + column * (columnWidth + padding);
                built[i] = SettingsSection.Create(
                    content, specs[i].Label, specs[i].Options, specs[i].OnSelect, x, cursors[column], columnWidth);
                cursors[column] -= built[i].Height + sectionGap;
            }

            fractalSection = built[0];
            paletteSection = built[1];
            coloringSection = built[2];
            resolutionSection = built[3];
            interfaceSection = built[4];

            RefreshSelection();
        }

        private static int ShortestColumn(float[] cursors)
        {
            var best = 0;
            for (var i = 1; i < cursors.Length; i++)
            {
                if (cursors[i] > cursors[best])
                {
                    best = i;
                }
            }

            return best;
        }

        private readonly struct SectionSpec
        {
            public SectionSpec(string label, IReadOnlyList<string> options, System.Action<int> onSelect)
            {
                Label = label;
                Options = options;
                OnSelect = onSelect;
            }

            public string Label { get; }
            public IReadOnlyList<string> Options { get; }
            public System.Action<int> OnSelect { get; }
        }

        protected override void OnTick()
        {
            RefreshSelection();
        }

        /// <summary>
        /// Wrap the panel content in a scroll view. The viewport is the glass panel's own content
        /// rectangle, which is already inside its rounded mask - so the list is clipped by the same
        /// shape that draws the panel, with no second mask to keep in sync.
        /// </summary>
        private RectTransform BuildScroll(float contentHeight, float viewportHeight)
        {
            var content = UiFactory.CreateRect("ScrollContent", Panel.Content);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight);

            // Something has to catch the drag in the gaps between rows, or a finger that starts
            // between two options scrolls nothing.
            var dragArea = UiFactory.CreateImage("DragArea", content, null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(dragArea.rectTransform);
            dragArea.raycastTarget = true;

            var scroll = Panel.Content.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = Panel.Content;
            scroll.horizontal = false;
            scroll.vertical = contentHeight > viewportHeight + 1f;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = UiTheme.PanelPx(28f);

            return content;
        }

        private void SelectFractal(int index)
        {
            if (index >= 0 && index < fractals.Count)
            {
                Services.Session.SetDefinition(fractals[index]);
            }
        }

        private void SelectPalette(int index)
        {
            var palettes = PaletteLibrary.All;
            if (index >= 0 && index < palettes.Count)
            {
                Services.Session.SetPalette(palettes[index]);
            }
        }

        private void SelectColoring(int index)
        {
            if (index < 0 || index >= ColoringPresets.Length)
            {
                return;
            }

            var settings = Services.Session.Coloring;
            settings.Smooth = ColoringPresets[index].Smooth;
            settings.Mode = ColoringPresets[index].Mode;
            Services.Session.SetColoring(settings);
        }

        private void SelectResolution(int index)
        {
            if (index >= 0 && index < ResolutionScales.Length)
            {
                Services.Session.SetRenderScale(ResolutionScales[index]);
            }
        }

        private void SelectInterfaceScale(int index)
        {
            if (index < 0 || index >= InterfaceScales.Length)
            {
                return;
            }

            var settings = Services.Session.Interface;
            settings.Scale = InterfaceScales[index];
            Services.Session.SetInterface(settings);
        }

        private void RefreshSelection()
        {
            var session = Services.Session;

            fractalSection?.SetSelected(fractals.IndexOf(session.Definition));
            paletteSection?.SetSelected(PaletteLibrary.IndexOf(session.Palette));
            coloringSection?.SetSelected(ColoringIndex(session.Coloring));
            resolutionSection?.SetSelected(NearestIndex(ResolutionScales, session.Quality.RenderScale));
            interfaceSection?.SetSelected(NearestIndex(InterfaceScales, session.Interface.Scale));
        }

        private static int ColoringIndex(in ColoringSettings settings)
        {
            for (var i = 0; i < ColoringPresets.Length; i++)
            {
                if (ColoringPresets[i].Smooth == settings.Smooth && ColoringPresets[i].Mode == settings.Mode)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int NearestIndex(float[] values, float target)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var distance = Mathf.Abs(values[i] - target);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = i;
            }

            return bestDistance <= 0.01f ? best : -1;
        }

        private static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }
    }
}
