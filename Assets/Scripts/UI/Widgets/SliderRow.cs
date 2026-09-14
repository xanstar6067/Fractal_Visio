using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A labelled slider with its value printed beside the label. For numbers that live on a
    /// range - fractal parameters, palette cycle length, a colour's hue - where a list of options
    /// would either be too coarse or too long.
    ///
    /// Two callbacks on purpose. <c>onChanged</c> fires while the thumb moves, for anything cheap
    /// (a palette remap). <c>onCommitted</c> fires once when the finger lifts, for anything that
    /// restarts the render: a fractal parameter applied on every drag step would discard the
    /// picture sixty times a second and show nothing but coarse passes until the finger stops.
    /// </summary>
    public sealed class SliderRow
    {
        /// <summary>Row height in dp: a caption line above a touch-sized track.</summary>
        private const float CaptionHeight = 20f;

        private readonly Slider slider;
        private readonly Text valueText;
        private readonly Func<double, string> format;
        private readonly double minimum;
        private readonly double maximum;
        private readonly bool logarithmic;
        private readonly bool integer;
        private bool suppress;

        private SliderRow(
            Slider slider, Text valueText, Func<double, string> format,
            double minimum, double maximum, bool logarithmic, bool integer)
        {
            this.slider = slider;
            this.valueText = valueText;
            this.format = format;
            this.minimum = minimum;
            this.maximum = maximum;
            this.logarithmic = logarithmic && minimum > 0d && maximum > minimum;
            this.integer = integer;
        }

        public static float MeasureHeight() =>
            UiTheme.PanelPx(CaptionHeight) + UiTheme.PanelPx(UiTheme.RowSpacing) * 0.5f + UiTheme.PanelPx(UiTheme.SegmentHeight);

        public float Height => MeasureHeight();

        /// <summary>Current value, in the slider's own units (after the log mapping, rounded if integer).</summary>
        public double Value => FromNormalized(slider.normalizedValue);

        /// <param name="format">Value text; null prints up to three significant decimals.</param>
        public static SliderRow Create(
            RectTransform parent,
            string label,
            double minimum,
            double maximum,
            double value,
            float x,
            float y,
            float width,
            Action<double> onChanged,
            Action<double> onCommitted,
            bool logarithmic = false,
            bool integer = false,
            Func<double, string> format = null)
        {
            var captionHeight = UiTheme.PanelPx(CaptionHeight);
            var trackHeight = UiTheme.PanelPx(UiTheme.SegmentHeight);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing) * 0.5f;

            var root = UiFactory.CreateRect("Slider_" + label, parent);
            Place(root, x, y, width, MeasureHeight());

            var caption = UiFactory.CreateText(
                "Label", root, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
            Place(caption.rectTransform, 0f, 0f, width * 0.62f, captionHeight);

            var valueText = UiFactory.CreateText(
                "Value", root, string.Empty, UiTheme.LabelFontSize, UiTheme.Text,
                TextAnchor.LowerRight, fitToRect: true, panelScale: true);
            Place(valueText.rectTransform, width * 0.62f, 0f, width * 0.38f, captionHeight);

            // The whole row height is the touch target; the drawn track is a thin bar inside it.
            var area = UiFactory.CreateImage(
                "Area", root, UiSprites.Rounded(UiTheme.PanelPxInt(UiTheme.SegmentRadius)), UiTheme.SegmentIdle);
            area.raycastTarget = true;
            Place(area.rectTransform, 0f, -(captionHeight + gap), width, trackHeight);

            var inset = UiTheme.PanelInset(width, 22f, 0.08f);
            var barHeight = Mathf.Max(3f, UiTheme.PanelPx(4f));
            var knob = Mathf.Min(UiTheme.PanelPx(26f), trackHeight * 0.7f);

            var track = UiFactory.CreateImage(
                "Track", area.transform, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(barHeight * 0.5f))),
                new Color(1f, 1f, 1f, 0.22f));
            var trackRect = track.rectTransform;
            trackRect.anchorMin = new Vector2(0f, 0.5f);
            trackRect.anchorMax = new Vector2(1f, 0.5f);
            trackRect.pivot = new Vector2(0.5f, 0.5f);
            trackRect.offsetMin = new Vector2(inset, -barHeight * 0.5f);
            trackRect.offsetMax = new Vector2(-inset, barHeight * 0.5f);

            var fillArea = UiFactory.CreateRect("FillArea", track.transform);
            UiFactory.Stretch(fillArea);
            var fill = UiFactory.CreateImage(
                "Fill", fillArea, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(barHeight * 0.5f))), UiTheme.Accent);
            fill.rectTransform.sizeDelta = Vector2.zero;

            var handleArea = UiFactory.CreateRect("HandleArea", track.transform);
            UiFactory.Stretch(handleArea);
            var handle = UiFactory.CreateImage(
                "Handle", handleArea, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(knob * 0.5f))), UiTheme.Text);
            handle.rectTransform.sizeDelta = new Vector2(knob, knob);

            var slider = area.gameObject.AddComponent<Slider>();
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = 0f;
            slider.maxValue = 1f;
            slider.transition = Selectable.Transition.None;

            var row = new SliderRow(
                slider, valueText, format ?? DefaultFormat, minimum, maximum, logarithmic, integer);
            row.SetValue(value);

            slider.onValueChanged.AddListener(normalized =>
            {
                if (row.suppress)
                {
                    return;
                }

                var current = row.FromNormalized(normalized);
                row.valueText.text = row.format(current);
                onChanged?.Invoke(current);
            });

            if (onCommitted != null)
            {
                area.gameObject.AddComponent<PointerUpRelay>().Released = () => onCommitted(row.Value);
            }

            return row;
        }

        /// <summary>Move the thumb without firing callbacks - for reflecting a change made elsewhere.</summary>
        public void SetValue(double value)
        {
            suppress = true;
            slider.normalizedValue = (float)ToNormalized(value);
            suppress = false;
            valueText.text = format(FromNormalized(slider.normalizedValue));
        }

        private double ToNormalized(double value)
        {
            var clamped = Math.Clamp(value, minimum, maximum);
            if (maximum <= minimum)
            {
                return 0d;
            }

            return logarithmic
                ? Math.Log(clamped / minimum) / Math.Log(maximum / minimum)
                : (clamped - minimum) / (maximum - minimum);
        }

        private double FromNormalized(float normalized)
        {
            var t = Math.Clamp(normalized, 0f, 1f);
            var value = logarithmic
                ? minimum * Math.Pow(maximum / minimum, t)
                : minimum + (maximum - minimum) * t;
            return integer ? Math.Round(value) : value;
        }

        private static string DefaultFormat(double value)
        {
            var magnitude = Math.Abs(value);
            var pattern = magnitude >= 100d ? "0" : magnitude >= 10d ? "0.#" : "0.###";
            return value.ToString(pattern, CultureInfo.InvariantCulture);
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
