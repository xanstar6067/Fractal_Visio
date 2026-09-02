using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>Small helpers so screens read as layout, not as GameObject plumbing.</summary>
    public static class UiFactory
    {
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        public static Image CreateImage(string name, Transform parent, Sprite sprite, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var image = go.GetComponent<Image>();
            image.sprite = sprite;
            image.type = sprite != null ? Image.Type.Sliced : Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;

            // Sprites are generated at device-pixel radius already, so the nine-slice corners must
            // be shown one texture pixel to one canvas pixel - scaling them again rounds the
            // corners twice as much as asked.
            image.pixelsPerUnitMultiplier = 1f;
            return image;
        }

        public static RawImage CreateRawImage(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var raw = go.GetComponent<RawImage>();
            raw.raycastTarget = false;
            return raw;
        }

        public static Text CreateText(string name, Transform parent, string content, int referenceFontSize, Color color, TextAnchor anchor)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);

            var text = go.GetComponent<Text>();
            text.font = UiTheme.Font;
            text.text = content;
            text.color = color;
            text.alignment = anchor;
            text.fontSize = Mathf.Max(8, Mathf.RoundToInt(UiTheme.Px(referenceFontSize)));
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>Fill the parent rectangle completely.</summary>
        public static void Stretch(RectTransform rect, float padding = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }
    }
}
