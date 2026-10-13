using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A caption over mutually exclusive options laid out as a grid of tiles - for a choice from a
    /// list too long to stack (nine shading styles, eleven colouring sources) and of short names. A
    /// <see cref="SettingsSection"/> would spend a screen on it; a <see cref="SegmentedRow"/> holds
    /// three at most. Tiles are the panel's row height, so the touch targets are the same as anywhere.
    /// </summary>
    public sealed class ChoiceGrid
    {
        /// <summary>Narrowest a tile may get before the grid drops a column, in dp.</summary>
        private const float MinimumTileWidth = 104f;

        private const int MaximumColumns = 3;

        private readonly Image[] tiles;

        private ChoiceGrid(Image[] tiles)
        {
            this.tiles = tiles;
        }

        public static float MeasureHeight(int count, float width)
        {
            var rows = Mathf.CeilToInt(Mathf.Max(1, count) / (float)Columns(width));
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            return UiTheme.PanelPx(20f) + gap + rows * UiTheme.PanelPx(UiTheme.SegmentHeight) + (rows - 1) * gap;
        }

        public static ChoiceGrid Create(
            RectTransform parent, string label, IReadOnlyList<string> options, Action<int> onSelect,
            float x, float y, float width)
        {
            var captionHeight = UiTheme.PanelPx(20f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var tileHeight = UiTheme.PanelPx(UiTheme.SegmentHeight);
            var columns = Columns(width);
            var tileWidth = (width - gap * (columns - 1)) / columns;
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);

            var caption = UiFactory.CreateText(
                "Label_" + label, parent, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
            Place(caption.rectTransform, x, y, width, captionHeight);

            var top = y - captionHeight - gap;
            var tiles = new Image[options.Count];
            for (var i = 0; i < options.Count; i++)
            {
                var column = i % columns;
                var row = i / columns;
                var tile = UiFactory.CreateImage("Choice_" + label + "_" + i, parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
                tile.raycastTarget = true;
                Place(tile.rectTransform, x + column * (tileWidth + gap), top - row * (tileHeight + gap), tileWidth, tileHeight);

                var text = UiFactory.CreateText(
                    "Label", tile.transform, options[i], UiTheme.SegmentFontSize - 1, UiTheme.Text,
                    TextAnchor.MiddleCenter, fitToRect: true, panelScale: true);
                UiFactory.Stretch(text.rectTransform, UiTheme.PanelInset(tileWidth, 6f, 0.06f));

                var button = tile.gameObject.AddComponent<Button>();
                button.targetGraphic = tile;
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
                tiles[i] = tile;
            }

            return new ChoiceGrid(tiles);
        }

        public void SetSelected(int index)
        {
            for (var i = 0; i < tiles.Length; i++)
            {
                tiles[i].color = i == index ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
            }
        }

        private static int Columns(float width) =>
            Mathf.Clamp(Mathf.FloorToInt(width / UiTheme.PanelPx(MinimumTileWidth)), 2, MaximumColumns);

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
