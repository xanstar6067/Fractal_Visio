using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// One row of a step sequence (a hybrid): its formula as a full-width row - tap to change it or
    /// move the step, the cross removes it - and under it the power as four segments, z² to z⁵, and
    /// how many times in a row the step is taken as a − ×n + stepper. A formula of fixed degree shows
    /// its own power lit and the others dimmed: visible, not choosable.
    ///
    /// The row is built once per shape of the sequence and then only refreshed (<see cref="Show"/>),
    /// so tapping + four times does not rebuild the panel four times.
    /// </summary>
    public sealed class StepRow
    {
        private static readonly string[] PowerLabels = { "z²", "z³", "z⁴", "z⁵" };
        private const int FirstPower = 2;

        private readonly ActionRow name;
        private readonly Image[] powerSegments;
        private readonly Text[] powerLabels;
        private readonly Button[] powerButtons;
        private readonly Text repeatsText;
        private readonly Image minus;
        private readonly Image plus;

        private StepRow(ActionRow name, Image[] powerSegments, Text[] powerLabels, Button[] powerButtons, Text repeatsText, Image minus, Image plus)
        {
            this.name = name;
            this.powerSegments = powerSegments;
            this.powerLabels = powerLabels;
            this.powerButtons = powerButtons;
            this.repeatsText = repeatsText;
            this.minus = minus;
            this.plus = plus;
        }

        public static float MeasureHeight() =>
            ActionRow.MeasureHeight() + UiTheme.PanelPx(UiTheme.RowSpacing) * 0.75f + UiTheme.PanelPx(UiTheme.SegmentHeight);

        /// <param name="open">Tap on the formula: the step's own sheet.</param>
        /// <param name="remove">The cross; null when the step cannot go - the last one.</param>
        /// <param name="setPower">A power segment, with the power it stands for.</param>
        /// <param name="changeRepeats">The stepper, with -1 or +1.</param>
        public static StepRow Create(
            RectTransform parent, float x, float y, float width,
            Action open, Action remove, Action<int> setPower, Action<int> changeRepeats)
        {
            var nameRow = ActionRow.Create(parent, string.Empty, x, y, width, open, onTrailing: remove);

            var top = y - ActionRow.MeasureHeight() - UiTheme.PanelPx(UiTheme.RowSpacing) * 0.75f;
            var height = UiTheme.PanelPx(UiTheme.SegmentHeight);
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var gap = Mathf.Min(UiTheme.PanelPx(UiTheme.RowSpacing) * 0.75f, width * 0.02f);

            // Power takes a little over half the width, the stepper the rest after a wider gap.
            var stepperGap = Mathf.Min(UiTheme.PanelPx(UiTheme.RowSpacing) * 2f, width * 0.05f);
            var powerWidth = (width - stepperGap) * 0.56f;
            var segmentWidth = (powerWidth - gap * (PowerLabels.Length - 1)) / PowerLabels.Length;

            var segments = new Image[PowerLabels.Length];
            var labels = new Text[PowerLabels.Length];
            var buttons = new Button[PowerLabels.Length];
            for (var i = 0; i < PowerLabels.Length; i++)
            {
                var segment = UiFactory.CreateImage("Power" + (FirstPower + i), parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
                segment.raycastTarget = true;
                Place(segment.rectTransform, x + i * (segmentWidth + gap), top, segmentWidth, height);

                var label = UiFactory.CreateText(
                    "Label", segment.transform, PowerLabels[i], UiTheme.SegmentFontSize, UiTheme.Text,
                    TextAnchor.MiddleCenter, fitToRect: true, panelScale: true);
                UiFactory.Stretch(label.rectTransform, UiTheme.PanelInset(segmentWidth, 4f, 0.08f));

                var button = segment.gameObject.AddComponent<Button>();
                button.targetGraphic = segment;
                button.transition = Selectable.Transition.None;
                var power = FirstPower + i;
                button.onClick.AddListener(() => setPower(power));

                segments[i] = segment;
                labels[i] = label;
                buttons[i] = button;
            }

            // − ×n +: the two buttons square-ish, the count between them.
            var stepperX = x + powerWidth + stepperGap;
            var stepperWidth = width - powerWidth - stepperGap;
            var buttonWidth = Mathf.Min(height, stepperWidth * 0.34f);
            var minusButton = StepperButton(parent, "−", stepperX, top, buttonWidth, height, radius, () => changeRepeats(-1));
            var plusButton = StepperButton(
                parent, "+", stepperX + stepperWidth - buttonWidth, top, buttonWidth, height, radius, () => changeRepeats(1));

            var count = UiFactory.CreateText(
                "Repeats", parent, string.Empty, UiTheme.SegmentFontSize, UiTheme.Text,
                TextAnchor.MiddleCenter, fitToRect: true, panelScale: true);
            Place(count.rectTransform, stepperX + buttonWidth, top, stepperWidth - buttonWidth * 2f, height);

            return new StepRow(nameRow, segments, labels, buttons, count, minusButton, plusButton);
        }

        /// <param name="title">"1. Mandelbrot".</param>
        /// <param name="power">The row's chosen power.</param>
        /// <param name="fixedPower">The formula's own power, or 0 when the row chooses.</param>
        /// <param name="canAdd">Whether + still fits in the cycle.</param>
        public void Show(string title, int power, int fixedPower, int repeats, bool canAdd)
        {
            if (name.Label != title)
            {
                name.Label = title;
            }

            var lit = fixedPower > 0 ? fixedPower : power;
            for (var i = 0; i < powerSegments.Length; i++)
            {
                var segmentPower = FirstPower + i;
                var selected = segmentPower == lit;
                var choosable = fixedPower <= 0;
                powerSegments[i].color = selected ? UiTheme.SegmentSelected : UiTheme.SegmentIdle;
                powerLabels[i].color = choosable || selected ? UiTheme.Text : Dimmed(UiTheme.TextMuted);
                powerButtons[i].interactable = choosable;
            }

            repeatsText.text = "×" + repeats.ToString(CultureInfo.InvariantCulture);
            SetEnabled(minus, repeats > 1);
            SetEnabled(plus, canAdd);
        }

        private static Image StepperButton(
            RectTransform parent, string glyph, float x, float y, float width, float height, int radius, Action onClick)
        {
            var background = UiFactory.CreateImage("Stepper" + glyph, parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
            background.raycastTarget = true;
            Place(background.rectTransform, x, y, width, height);

            var label = UiFactory.CreateText(
                "Glyph", background.transform, glyph, UiTheme.SegmentFontSize + 4, UiTheme.Text,
                TextAnchor.MiddleCenter, panelScale: true);
            UiFactory.Stretch(label.rectTransform);

            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1.1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1.4f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.35f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            button.onClick.AddListener(() => onClick());
            return background;
        }

        private static void SetEnabled(Image stepper, bool enabled)
        {
            var button = stepper.GetComponent<Button>();
            if (button.interactable != enabled)
            {
                button.interactable = enabled;
            }

            var glyph = stepper.GetComponentInChildren<Text>();
            glyph.color = enabled ? UiTheme.Text : Dimmed(UiTheme.TextMuted);
        }

        private static Color Dimmed(Color color) => new(color.r, color.g, color.b, color.a * 0.4f);

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
