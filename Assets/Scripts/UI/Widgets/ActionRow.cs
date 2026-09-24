using System;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    public enum ActionStyle
    {
        Normal,

        /// <summary>The main thing this panel does: save, add.</summary>
        Accent,

        /// <summary>Removes something.</summary>
        Danger
    }

    /// <summary>What the small button at the right end of a row shows.</summary>
    public enum TrailingGlyph
    {
        /// <summary>A cross: remove this.</summary>
        Cross,

        /// <summary>Three dots: this item's other actions.</summary>
        More
    }

    /// <summary>
    /// A full-width button with a label, optionally a second line of detail, a picture at its left
    /// end and a small trailing button - the actions of a bookmark. Same height and corner radius as
    /// an option row, so a panel that mixes both reads as one list.
    /// </summary>
    public sealed class ActionRow
    {
        private static readonly Color AccentFill = new(0.42f, 0.76f, 1f, 0.34f);
        private static readonly Color DangerFill = new(1f, 0.35f, 0.35f, 0.3f);

        private readonly Text label;

        private ActionRow(Text label, RawImage leading)
        {
            this.label = label;
            Leading = leading;
        }

        /// <summary>The picture at the left end, if the row was made with one. Hidden until <see cref="SetLeading"/> gives it a texture.</summary>
        public RawImage Leading { get; }

        /// <summary>Show <paramref name="texture"/> at the left end; null hides it.</summary>
        public void SetLeading(Texture texture)
        {
            if (Leading == null || ReferenceEquals(Leading.texture, texture))
            {
                return;
            }

            Leading.texture = texture;
            Leading.enabled = texture != null;
        }

        public static float MeasureHeight(bool withDetail = false) =>
            UiTheme.PanelPx(withDetail ? UiTheme.SegmentHeight * 1.25f : UiTheme.SegmentHeight);

        public string Label
        {
            get => label.text;
            set => label.text = value;
        }

        /// <param name="detail">Second, muted line under the label, or null.</param>
        /// <param name="onTrailing">If set, a square button at the right end runs this instead of <paramref name="onClick"/>.</param>
        /// <param name="trailingGlyph">What that button shows.</param>
        /// <param name="leadingImage">Leave a square at the left end for a picture - a bookmark's preview.</param>
        public static ActionRow Create(
            RectTransform parent,
            string text,
            float x,
            float y,
            float width,
            Action onClick,
            ActionStyle style = ActionStyle.Normal,
            string detail = null,
            Action onTrailing = null,
            TrailingGlyph trailingGlyph = TrailingGlyph.Cross,
            bool leadingImage = false)
        {
            var height = MeasureHeight(detail != null);
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var fill = style switch
            {
                ActionStyle.Accent => AccentFill,
                ActionStyle.Danger => DangerFill,
                _ => UiTheme.SegmentIdle
            };

            var background = UiFactory.CreateImage("Action_" + text, parent, UiSprites.Rounded(radius), fill);
            background.raycastTarget = true;
            Place(background.rectTransform, x, y, width, height);

            var inset = UiTheme.PanelInset(width, 14f, 0.05f);
            var trailingSize = onTrailing != null ? Mathf.Min(height, width * 0.22f) : 0f;

            RawImage leading = null;
            var textStart = inset;
            if (leadingImage)
            {
                var margin = Mathf.Max(2f, height * 0.1f);
                var side = height - margin * 2f;
                var clip = UiFactory.CreateImage(
                    "LeadingClip", background.transform,
                    UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(radius * 0.7f))), new Color(0f, 0f, 0f, 0.35f));
                clip.gameObject.AddComponent<Mask>().showMaskGraphic = true;
                UiFactory.Anchor(
                    clip.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(margin, 0f), new Vector2(side, side));
                leading = UiFactory.CreateRawImage("Leading", clip.transform);
                UiFactory.Stretch(leading.rectTransform);
                leading.enabled = false;
                textStart = margin + side + inset * 0.8f;
            }

            var name = UiFactory.CreateText(
                "Label", background.transform, text, UiTheme.SegmentFontSize, UiTheme.Text,
                detail != null ? TextAnchor.LowerLeft : TextAnchor.MiddleLeft, fitToRect: true, panelScale: true);
            var nameRect = name.rectTransform;
            nameRect.anchorMin = new Vector2(0f, detail != null ? 0.45f : 0f);
            nameRect.anchorMax = new Vector2(1f, 1f);
            nameRect.offsetMin = new Vector2(textStart, 0f);
            nameRect.offsetMax = new Vector2(-(inset + trailingSize), detail != null ? -height * 0.08f : 0f);

            if (detail != null)
            {
                var sub = UiFactory.CreateText(
                    "Detail", background.transform, detail, UiTheme.LabelFontSize, UiTheme.TextMuted,
                    TextAnchor.UpperLeft, fitToRect: true, panelScale: true);
                var subRect = sub.rectTransform;
                subRect.anchorMin = new Vector2(0f, 0f);
                subRect.anchorMax = new Vector2(1f, 0.45f);
                subRect.offsetMin = new Vector2(textStart, height * 0.08f);
                subRect.offsetMax = new Vector2(-(inset + trailingSize), 0f);
            }

            AttachButton(background, onClick);

            if (onTrailing != null)
            {
                var trailing = UiFactory.CreateImage(
                    "Trailing", background.transform, UiSprites.Rounded(radius), new Color(0f, 0f, 0f, 0.001f));
                trailing.raycastTarget = true;
                UiFactory.Anchor(
                    trailing.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    Vector2.zero, new Vector2(trailingSize, height));

                if (trailingGlyph == TrailingGlyph.More)
                {
                    var dotsSize = Mathf.Round(Mathf.Min(trailingSize, height) * 0.46f);
                    var dots = UiFactory.CreateImage("More", trailing.transform, null, UiTheme.TextMuted);
                    dots.sprite = UiSprites.More(Mathf.RoundToInt(dotsSize));
                    dots.type = Image.Type.Simple;
                    UiFactory.Anchor(
                        dots.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                        Vector2.zero, new Vector2(dotsSize, dotsSize));
                }
                else
                {
                    // A cross from two thin bars: no glyph to depend on in the runtime font.
                    var barLength = trailingSize * 0.34f;
                    var barThickness = Mathf.Max(2f, UiTheme.PanelPx(2.5f));
                    for (var i = 0; i < 2; i++)
                    {
                        var bar = UiFactory.CreateImage(
                            "Cross" + i, trailing.transform,
                            UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(barThickness * 0.5f))), UiTheme.TextMuted);
                        UiFactory.Anchor(
                            bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                            Vector2.zero, new Vector2(barLength, barThickness));
                        bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                    }
                }

                AttachButton(trailing, onTrailing);
            }

            return new ActionRow(name, leading);
        }

        private static void AttachButton(Image background, Action onClick)
        {
            var button = background.gameObject.AddComponent<Button>();
            button.targetGraphic = background;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.25f, 1.25f, 1.25f, 1.1f),
                pressedColor = new Color(0.8f, 0.8f, 0.8f, 1.4f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };

            if (onClick != null)
            {
                button.onClick.AddListener(() => onClick());
            }
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
