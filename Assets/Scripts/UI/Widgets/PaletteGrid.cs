using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// Palettes as a grid of cards, each its colour strip over its name. The strip is what the user
    /// recognises a palette by; the name is what they call it, so both stay - a list of names asked
    /// them to remember what "Aurora" looked like, a grid of bare strips what the one they saved
    /// was called.
    /// </summary>
    public sealed class PaletteGrid
    {
        /// <summary>Narrowest a card may get before the grid drops a column.</summary>
        private const float MinimumCardWidth = 140f;

        private const float CardHeight = 64f;

        private readonly Image[] cards;
        private readonly Image[] outlines;

        private PaletteGrid(Image[] cards, Image[] outlines)
        {
            this.cards = cards;
            this.outlines = outlines;
        }

        public static float MeasureHeight(int count, float width)
        {
            var columns = Columns(width);
            var rows = Mathf.CeilToInt(Mathf.Max(1, count) / (float)columns);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            return UiTheme.PanelPx(20f) + gap + rows * UiTheme.PanelPx(CardHeight) + (rows - 1) * gap;
        }

        public static PaletteGrid Create(
            RectTransform parent,
            string label,
            IReadOnlyList<string> names,
            IReadOnlyList<Texture> strips,
            Action<int> onSelect,
            float x,
            float y,
            float width)
        {
            var captionHeight = UiTheme.PanelPx(20f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var cardHeight = UiTheme.PanelPx(CardHeight);
            var columns = Columns(width);
            var cardWidth = (width - gap * (columns - 1)) / columns;
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var inset = UiTheme.PanelInset(cardWidth, 10f, 0.08f);

            var caption = UiFactory.CreateText(
                "Label_" + label, parent, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
            Place(caption.rectTransform, x, y, width, captionHeight);

            var count = names.Count;
            var cards = new Image[count];
            var outlines = new Image[count];
            var top = y - captionHeight - gap;
            for (var i = 0; i < count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var card = UiFactory.CreateImage("Palette_" + i, parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
                card.raycastTarget = true;
                Place(card.rectTransform, x + column * (cardWidth + gap), top - row * (cardHeight + gap), cardWidth, cardHeight);

                var stripHeight = cardHeight * 0.36f;
                var clipRadius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(radius * 0.6f, stripHeight * 0.4f)));
                var clip = UiFactory.CreateImage("StripClip", card.transform, UiSprites.Rounded(clipRadius), Color.white);
                clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
                Place(clip.rectTransform, inset, -inset, cardWidth - inset * 2f, stripHeight);
                var strip = UiFactory.CreateRawImage("Strip", clip.transform);
                strip.texture = strips != null && i < strips.Count ? strips[i] : null;
                UiFactory.Stretch(strip.rectTransform);

                var name = UiFactory.CreateText(
                    "Name", card.transform, names[i], UiTheme.SegmentFontSize - 1, UiTheme.Text,
                    TextAnchor.MiddleLeft, fitToRect: true, panelScale: true);
                Place(name.rectTransform, inset, -(inset + stripHeight), cardWidth - inset * 2f, cardHeight - inset * 1.5f - stripHeight);

                var outline = UiFactory.CreateImage(
                    "Selected", card.transform, UiSprites.RoundedOutline(radius, Mathf.Max(1.5f, UiTheme.PanelPx(2f))), UiTheme.Accent);
                UiFactory.Stretch(outline.rectTransform);
                outline.enabled = false;

                var button = card.gameObject.AddComponent<Button>();
                button.targetGraphic = card;
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
                var index = i;
                button.onClick.AddListener(() => onSelect(index));

                cards[i] = card;
                outlines[i] = outline;
            }

            return new PaletteGrid(cards, outlines);
        }

        public void SetSelected(int index)
        {
            for (var i = 0; i < cards.Length; i++)
            {
                var selected = i == index;
                cards[i].color = selected ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
                outlines[i].enabled = selected;
            }
        }

        private static int Columns(float width) =>
            Mathf.Clamp(Mathf.FloorToInt(width / UiTheme.PanelPx(MinimumCardWidth)), 2, 4);

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
