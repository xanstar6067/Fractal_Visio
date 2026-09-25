using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// Two or three mutually exclusive options side by side in one row: a choice too small to spend
    /// a <see cref="SettingsSection"/> list on - smooth or bands, which colour the picker edits.
    /// </summary>
    public sealed class SegmentedRow
    {
        private readonly Image[] segments;

        private SegmentedRow(Image[] segments)
        {
            this.segments = segments;
        }

        public static float MeasureHeight() => UiTheme.PanelPx(UiTheme.SegmentHeight);

        public static SegmentedRow Create(
            RectTransform parent, IReadOnlyList<string> options, float x, float y, float width, Action<int> onSelect)
        {
            var height = MeasureHeight();
            var gap = Mathf.Min(UiTheme.PanelPx(UiTheme.RowSpacing), width * 0.03f);
            var count = Mathf.Max(1, options.Count);
            var segmentWidth = (width - gap * (count - 1)) / count;
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var segments = new Image[options.Count];

            for (var i = 0; i < options.Count; i++)
            {
                var background = UiFactory.CreateImage("Segment_" + options[i], parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
                background.raycastTarget = true;
                var rect = background.rectTransform;
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x + i * (segmentWidth + gap), y);
                rect.sizeDelta = new Vector2(segmentWidth, height);

                var label = UiFactory.CreateText(
                    "Label", background.transform, options[i], UiTheme.SegmentFontSize, UiTheme.Text,
                    TextAnchor.MiddleCenter, fitToRect: true, panelScale: true);
                UiFactory.Stretch(label.rectTransform, UiTheme.PanelInset(segmentWidth, 8f, 0.08f));

                var button = background.gameObject.AddComponent<Button>();
                button.targetGraphic = background;
                button.transition = Selectable.Transition.None;
                var index = i;
                button.onClick.AddListener(() => onSelect(index));

                segments[i] = background;
            }

            return new SegmentedRow(segments);
        }

        public void SetSelected(int index)
        {
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i].color = i == index ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
            }
        }
    }
}
