using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The settings panel. Today it holds one setting - which fractal is on screen - and it is
    /// built as a list of option rows precisely so the next ones (palette, colouring, quality) drop
    /// in as more sections without a rewrite.
    ///
    /// It lists fractals from <c>AppServices.Catalog</c>, which hands them over as Core interfaces:
    /// the UI never references the Fractals assembly, so a new fractal appears here on its own.
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        private readonly List<Option> options = new();

        protected override void OnBuild(Transform parent)
        {
            var catalog = Services.Catalog;
            var count = Mathf.Max(1, catalog.Count);

            var padding = UiTheme.Px(UiTheme.PanelPadding);
            var rowHeight = UiTheme.Px(UiTheme.SegmentHeight);
            var gap = UiTheme.Px(8f);
            var titleHeight = UiTheme.Px(26f);
            var labelHeight = UiTheme.Px(20f);

            var width = UiTheme.Px(UiTheme.PanelWidth);
            var height = padding * 2f + titleHeight + gap + labelHeight + gap +
                         count * rowHeight + (count - 1) * gap;

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
                new Vector2(
                    -UiTheme.Px(UiTheme.ScreenMargin),
                    UiTheme.Px(UiTheme.ScreenMargin * 2f + UiTheme.ToggleSize)),
                new Vector2(width, height));

            var cursor = -padding;

            var title = UiFactory.CreateText(
                "Title", Panel.Content, "Settings", UiTheme.TitleFontSize, UiTheme.Text, TextAnchor.UpperLeft);
            PlaceRow(title.rectTransform, padding, cursor, width - padding * 2f, titleHeight);
            title.fontStyle = FontStyle.Bold;
            cursor -= titleHeight + gap;

            var label = UiFactory.CreateText(
                "FractalLabel", Panel.Content, "FRACTAL", UiTheme.LabelFontSize, UiTheme.TextMuted, TextAnchor.UpperLeft);
            PlaceRow(label.rectTransform, padding, cursor, width - padding * 2f, labelHeight);
            cursor -= labelHeight + gap;

            for (var i = 0; i < catalog.Count; i++)
            {
                var option = BuildOption(catalog[i], padding, cursor, width - padding * 2f, rowHeight);
                options.Add(option);
                cursor -= rowHeight + gap;
            }

            RefreshSelection();
        }

        protected override void OnTick()
        {
            RefreshSelection();
        }

        private Option BuildOption(IFractalDefinition definition, float x, float y, float width, float height)
        {
            var radius = Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(UiTheme.SegmentRadius)));

            var background = UiFactory.CreateImage(
                "Option_" + definition.Id, Panel.Content, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
            background.raycastTarget = true;
            PlaceRow(background.rectTransform, x, y, width, height);

            var name = UiFactory.CreateText(
                "Name", background.transform, definition.DisplayName, UiTheme.SegmentFontSize, UiTheme.Text, TextAnchor.MiddleLeft);
            UiFactory.Stretch(name.rectTransform);
            name.rectTransform.offsetMin = new Vector2(UiTheme.Px(14f), 0f);
            name.rectTransform.offsetMax = new Vector2(-UiTheme.Px(40f), 0f);

            // A dot rather than a tick: no glyph to depend on, and it reads at a glance.
            var marker = UiFactory.CreateImage(
                "Marker", background.transform, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(5f)))), UiTheme.Accent);
            UiFactory.Anchor(
                marker.rectTransform,
                new Vector2(1f, 0.5f),
                new Vector2(1f, 0.5f),
                new Vector2(-UiTheme.Px(14f), 0f),
                new Vector2(UiTheme.Px(10f), UiTheme.Px(10f)));

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1.1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1.2f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.5f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            var captured = definition;
            button.onClick.AddListener(() => Services.Session.SetDefinition(captured));

            return new Option(definition, background, marker);
        }

        private void RefreshSelection()
        {
            var active = Services.Session.Definition;
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var selected = ReferenceEquals(option.Definition, active);
                option.Background.color = selected ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
                option.Marker.enabled = selected;
            }
        }

        private static void PlaceRow(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private readonly struct Option
        {
            public Option(IFractalDefinition definition, Image background, Image marker)
            {
                Definition = definition;
                Background = background;
                Marker = marker;
            }

            public IFractalDefinition Definition { get; }
            public Image Background { get; }
            public Image Marker { get; }
        }
    }
}
