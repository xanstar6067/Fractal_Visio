using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A labelled list of mutually exclusive options - the only control the settings panel has, and
    /// deliberately so: every setting so far (fractal, palette, resolution, interface size) is a
    /// choice from a short list, and one control type means one set of touch targets to get right.
    ///
    /// Adding a section is one <see cref="Create"/> call plus advancing the caller's cursor by
    /// <see cref="Height"/>. Nothing here knows what the options mean.
    /// </summary>
    public sealed class SettingsSection
    {
        private readonly Image[] backgrounds;
        private readonly Image[] markers;

        private SettingsSection(Image[] backgrounds, Image[] markers, float height)
        {
            this.backgrounds = backgrounds;
            this.markers = markers;
            Height = height;
        }

        /// <summary>Total height in device pixels, label included.</summary>
        public float Height { get; }

        public static float MeasureHeight(int optionCount)
        {
            var count = Mathf.Max(1, optionCount);
            return UiTheme.Px(20f) + UiTheme.Px(UiTheme.RowSpacing) +
                   count * UiTheme.Px(UiTheme.SegmentHeight) +
                   (count - 1) * UiTheme.Px(UiTheme.RowSpacing);
        }

        /// <summary>
        /// Build the section into <paramref name="parent"/> with its top-left corner at
        /// (<paramref name="x"/>, <paramref name="y"/>), measured downwards from the parent's top.
        /// </summary>
        public static SettingsSection Create(
            RectTransform parent,
            string label,
            IReadOnlyList<string> options,
            Action<int> onSelect,
            float x,
            float y,
            float width)
        {
            var labelHeight = UiTheme.Px(20f);
            var rowHeight = UiTheme.Px(UiTheme.SegmentHeight);
            var gap = UiTheme.Px(UiTheme.RowSpacing);
            var radius = UiTheme.PxInt(UiTheme.SegmentRadius);

            var cursor = y;

            var caption = UiFactory.CreateText(
                "Label_" + label, parent, label, UiTheme.LabelFontSize, UiTheme.TextMuted, TextAnchor.LowerLeft);
            Place(caption.rectTransform, x, cursor, width, labelHeight);
            cursor -= labelHeight + gap;

            var count = options.Count;
            var backgrounds = new Image[count];
            var markers = new Image[count];

            for (var i = 0; i < count; i++)
            {
                var background = UiFactory.CreateImage(
                    "Option_" + label + "_" + i, parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
                background.raycastTarget = true;
                Place(background.rectTransform, x, cursor, width, rowHeight);

                var name = UiFactory.CreateText(
                    "Name", background.transform, options[i], UiTheme.SegmentFontSize, UiTheme.Text, TextAnchor.MiddleLeft);
                UiFactory.Stretch(name.rectTransform);
                name.rectTransform.offsetMin = new Vector2(UiTheme.Px(14f), 0f);
                name.rectTransform.offsetMax = new Vector2(-UiTheme.Px(40f), 0f);

                // A dot rather than a tick: no glyph to depend on, and it reads at a glance.
                var marker = UiFactory.CreateImage(
                    "Marker", background.transform, UiSprites.Rounded(UiTheme.PxInt(5f)), UiTheme.Accent);
                UiFactory.Anchor(
                    marker.rectTransform,
                    new Vector2(1f, 0.5f),
                    new Vector2(1f, 0.5f),
                    new Vector2(-UiTheme.Px(14f), 0f),
                    new Vector2(UiTheme.Px(10f), UiTheme.Px(10f)));

                AttachButton(background, i, onSelect);

                backgrounds[i] = background;
                markers[i] = marker;
                cursor -= rowHeight + gap;
            }

            return new SettingsSection(backgrounds, markers, MeasureHeight(count));
        }

        public void SetSelected(int index)
        {
            for (var i = 0; i < backgrounds.Length; i++)
            {
                var selected = i == index;
                backgrounds[i].color = selected ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
                markers[i].enabled = selected;
            }
        }

        private static void AttachButton(Image background, int index, Action<int> onSelect)
        {
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

            var captured = index;
            button.onClick.AddListener(() => onSelect(captured));
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
