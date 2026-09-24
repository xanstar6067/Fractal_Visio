using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A one-line text field shaped like an option row, with an accent outline so it reads as
    /// something to type in rather than a label: for a name. Tapping it brings up the system
    /// keyboard, which on a phone shows its own copy of the text above itself - the field may well
    /// be under the keyboard.
    /// </summary>
    public static class TextInputRow
    {
        public static float MeasureHeight() => UiTheme.PanelPx(UiTheme.SegmentHeight);

        public static InputField Create(
            RectTransform parent, string text, float x, float y, float width, int characterLimit)
        {
            var height = MeasureHeight();
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var background = UiFactory.CreateImage("Input", parent, UiSprites.Rounded(radius), UiTheme.SegmentIdle);
            background.raycastTarget = true;
            var rect = background.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);

            var outline = UiFactory.CreateImage(
                "Outline", background.transform,
                UiSprites.RoundedOutline(radius, Mathf.Max(1f, UiTheme.PanelPx(1.5f))),
                new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.55f));
            UiFactory.Stretch(outline.rectTransform);

            var label = UiFactory.CreateText(
                "Text", background.transform, string.Empty, UiTheme.SegmentFontSize, UiTheme.Text,
                TextAnchor.MiddleLeft, panelScale: true);
            label.supportRichText = false;
            UiFactory.Stretch(label.rectTransform, UiTheme.PanelInset(width, 14f, 0.05f));

            var input = background.gameObject.AddComponent<InputField>();
            input.targetGraphic = background;
            input.textComponent = label;
            input.lineType = InputField.LineType.SingleLine;
            input.characterLimit = characterLimit;
            input.caretColor = UiTheme.Text;
            input.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.4f);
            input.text = text ?? string.Empty;
            return input;
        }
    }
}
