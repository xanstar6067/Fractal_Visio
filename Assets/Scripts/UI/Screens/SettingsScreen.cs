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

        private static readonly float[] InterfaceScales = { 0.85f, 1f, 1.25f, 1.5f };

        private static readonly string[] InterfaceNames = { "Compact", "Normal", "Large", "Huge" };

        private readonly List<IFractalDefinition> fractals = new();

        private SettingsSection fractalSection;
        private SettingsSection paletteSection;
        private SettingsSection coloringSection;
        private SettingsSection resolutionSection;
        private SettingsSection interfaceSection;

        protected override void OnBuild(Transform parent)
        {
            var padding = UiTheme.Px(UiTheme.PanelPadding);
            var sectionGap = UiTheme.Px(UiTheme.SectionSpacing);
            var titleHeight = UiTheme.Px(28f);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);

            fractals.Clear();
            for (var i = 0; i < Services.Catalog.Count; i++)
            {
                fractals.Add(Services.Catalog[i]);
            }

            var palettes = PaletteLibrary.All;

            var contentHeight = padding * 2f + titleHeight + sectionGap +
                                SettingsSection.MeasureHeight(fractals.Count) + sectionGap +
                                SettingsSection.MeasureHeight(palettes.Count) + sectionGap +
                                SettingsSection.MeasureHeight(ColoringNames.Length) + sectionGap +
                                SettingsSection.MeasureHeight(ResolutionNames.Length) + sectionGap +
                                SettingsSection.MeasureHeight(InterfaceNames.Length);

            // The panel sits above the toggle and never runs off the top of the screen; whatever
            // does not fit scrolls. On a phone held sideways that is most of it.
            var width = Mathf.Min(UiTheme.Px(UiTheme.PanelWidth), Screen.width - margin * 2f);
            var maxHeight = Mathf.Max(UiTheme.Px(160f), Screen.height - (margin * 3f + UiTheme.Px(UiTheme.ToggleSize)));
            var height = Mathf.Min(contentHeight, maxHeight);

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
            var rowWidth = width - padding * 2f;
            var cursor = -padding;

            var title = UiFactory.CreateText(
                "Title", content, "Settings", UiTheme.TitleFontSize, UiTheme.Text, TextAnchor.UpperLeft);
            Place(title.rectTransform, padding, cursor, rowWidth, titleHeight);
            title.fontStyle = FontStyle.Bold;
            cursor -= titleHeight + sectionGap;

            var fractalNames = new string[fractals.Count];
            for (var i = 0; i < fractals.Count; i++)
            {
                fractalNames[i] = fractals[i].DisplayName;
            }

            fractalSection = SettingsSection.Create(
                content, "FRACTAL", fractalNames, SelectFractal, padding, cursor, rowWidth);
            cursor -= fractalSection.Height + sectionGap;

            var paletteNames = new string[palettes.Count];
            for (var i = 0; i < palettes.Count; i++)
            {
                paletteNames[i] = palettes[i].DisplayName;
            }

            paletteSection = SettingsSection.Create(
                content, "PALETTE", paletteNames, SelectPalette, padding, cursor, rowWidth);
            cursor -= paletteSection.Height + sectionGap;

            coloringSection = SettingsSection.Create(
                content, "COLOURING", ColoringNames, SelectColoring, padding, cursor, rowWidth);
            cursor -= coloringSection.Height + sectionGap;

            resolutionSection = SettingsSection.Create(
                content, "RESOLUTION", ResolutionNames, SelectResolution, padding, cursor, rowWidth);
            cursor -= resolutionSection.Height + sectionGap;

            interfaceSection = SettingsSection.Create(
                content, "INTERFACE SIZE", InterfaceNames, SelectInterfaceScale, padding, cursor, rowWidth);

            RefreshSelection();
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
            scroll.scrollSensitivity = UiTheme.Px(28f);

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
