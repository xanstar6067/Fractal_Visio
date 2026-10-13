using System;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// How to move the picture, shown once: the first time the explorer appears, and again from
    /// Settings. One card over the dimmed, blurred picture - one finger moves it, two zoom and turn
    /// it, three bring back the start, a tap hides the buttons - and "Got it". A tap beside the card
    /// closes it too; the picture underneath does not move while it is up.
    ///
    /// A 3D scene has a card of its own, shown the first time a scene opens: there one finger turns
    /// the shape about the point under it and two approach that point, slide and roll.
    /// </summary>
    public sealed class GestureHintScreen : UiScreen
    {
        private static readonly string[] RowKeys =
        {
            "gestures.one_finger", "gestures.two_fingers", "gestures.three_fingers", "gestures.tap"
        };

        private static readonly string[] SceneRowKeys =
        {
            "gestures.scene_one_finger", "gestures.scene_two_fingers", "gestures.three_fingers", "gestures.tap"
        };

        private readonly Action<bool> dismissed;
        private bool forScene;
        private bool builtForScene;

        /// <param name="dismissed">Runs when the hint is put away, with whether it was the scene's: it has been seen.</param>
        public GestureHintScreen(Action<bool> dismissed)
        {
            this.dismissed = dismissed;
        }

        /// <summary>Open the hint for a plane fractal's gestures, or a scene's.</summary>
        public void Show(bool scene)
        {
            forScene = scene;
            if (forScene != builtForScene)
            {
                NeedsRebuild = true;
            }

            Open();
        }

        /// <summary>Put the hint away, as "Got it" does.</summary>
        public void Dismiss()
        {
            Close();
            dismissed?.Invoke(forScene);
        }

        protected override void OnBuild(Transform parent)
        {
            builtForScene = forScene;
            var rowKeys = forScene ? SceneRowKeys : RowKeys;
            Panel = GlassPanel.Create("GestureHint", parent, 0f, new Color(0.02f, 0.025f, 0.045f, 0.55f), Color.clear);
            UiFactory.Stretch(Panel.Root);

            // Everything around the card: a tap there is "got it" as well.
            var catcher = UiFactory.CreateImage("Dismiss", Panel.Content, null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(catcher.rectTransform);
            catcher.raycastTarget = true;
            catcher.gameObject.AddComponent<Button>().onClick.AddListener(Dismiss);

            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var availableWidth = Screen.width - UiTheme.SafeLeft - UiTheme.SafeRight - margin * 2f;
            var availableHeight = Screen.height - UiTheme.SafeTop - UiTheme.SafeBottom - margin * 2f;
            var width = Mathf.Min(UiTheme.Px(380f), availableWidth);

            var padding = UiTheme.Px(20f);
            var titleHeight = UiTheme.Px(34f);
            var buttonHeight = UiTheme.Px(UiTheme.SegmentHeight);
            var gap = UiTheme.Px(12f);
            var fixedHeight = padding * 2f + titleHeight + gap * 2f + buttonHeight;

            // Rows shrink before the card leaves the screen - on a phone on its side.
            var rowHeight = Mathf.Min(UiTheme.Px(64f), (availableHeight - fixedHeight) / rowKeys.Length);
            var height = fixedHeight + rowHeight * rowKeys.Length;

            var radius = UiTheme.PxInt(UiTheme.CardRadius);
            var card = UiFactory.CreateImage("Card", Panel.Content, UiSprites.Rounded(radius), new Color(0.035f, 0.045f, 0.075f, 0.92f));
            card.raycastTarget = true;
            UiFactory.Anchor(
                card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2((UiTheme.SafeLeft - UiTheme.SafeRight) * 0.5f, (UiTheme.SafeBottom - UiTheme.SafeTop) * 0.5f),
                new Vector2(width, height));

            var title = UiFactory.CreateText(
                "Title", card.transform, Strings.Get(forScene ? "gestures.scene_title" : "gestures.title"), UiTheme.TitleFontSize, UiTheme.Text,
                TextAnchor.MiddleLeft, fitToRect: true);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, padding, -padding, width - padding * 2f, titleHeight);

            var icons = new Func<int, Sprite>[] { UiSprites.FingerPan, UiSprites.FingerPinch, UiSprites.FingerThree, UiSprites.FingerTap };
            var iconSize = Mathf.Round(Mathf.Min(UiTheme.Px(40f), rowHeight * 0.75f));
            var textLeft = padding + iconSize + UiTheme.Px(16f);
            var cursor = padding + titleHeight + gap;
            for (var i = 0; i < rowKeys.Length; i++)
            {
                var icon = UiFactory.CreateImage("Icon", card.transform, null, UiTheme.Accent);
                icon.sprite = icons[i](Mathf.RoundToInt(iconSize));
                icon.type = Image.Type.Simple;
                Place(icon.rectTransform, padding, -(cursor + (rowHeight - iconSize) * 0.5f), iconSize, iconSize);

                var text = UiFactory.CreateText(
                    "Text", card.transform, Strings.Get(rowKeys[i]), UiTheme.SegmentFontSize - 2, UiTheme.Text,
                    TextAnchor.MiddleLeft, fitToRect: true);
                Place(text.rectTransform, textLeft, -cursor, width - textLeft - padding, rowHeight);
                cursor += rowHeight;
            }

            cursor += gap;
            var button = UiFactory.CreateImage(
                "GotIt", card.transform, UiSprites.Rounded(UiTheme.PxInt(UiTheme.SegmentRadius)), new Color(0.42f, 0.76f, 1f, 0.34f));
            button.raycastTarget = true;
            Place(button.rectTransform, padding, -cursor, width - padding * 2f, buttonHeight);
            button.gameObject.AddComponent<Button>().onClick.AddListener(Dismiss);

            var label = UiFactory.CreateText(
                "Label", button.transform, Strings.Get("gestures.got_it"), UiTheme.SegmentFontSize, UiTheme.Text,
                TextAnchor.MiddleCenter, fitToRect: true);
            UiFactory.Stretch(label.rectTransform);
        }
    }
}
