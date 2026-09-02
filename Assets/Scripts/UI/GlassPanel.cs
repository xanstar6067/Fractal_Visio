using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A frosted-glass surface: the blurred backdrop, a translucent tint over it, a hairline border,
    /// and a content area, all clipped to rounded corners.
    ///
    /// The backdrop is one shared blurred texture (see <see cref="BackdropBlur"/>); each panel shows
    /// the part of it that sits behind the panel, which is what makes the glass track the image
    /// instead of looking painted on.
    /// </summary>
    public sealed class GlassPanel
    {
        private static readonly Vector3[] Corners = new Vector3[4];

        private readonly RawImage backdrop;

        private GlassPanel(RectTransform root, RectTransform content, RawImage backdrop)
        {
            Root = root;
            Content = content;
            this.backdrop = backdrop;
        }

        public RectTransform Root { get; }

        /// <summary>Parent for rows and controls. Clipped to the rounded corners.</summary>
        public RectTransform Content { get; }

        public static GlassPanel Create(string name, Transform parent, float referenceRadius, Color tint, Color border)
        {
            var root = UiFactory.CreateRect(name, parent);
            var radius = Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(referenceRadius)));

            // The mask graphic is the rounded shape itself; everything below is clipped to it.
            var clip = UiFactory.CreateImage("Clip", root, UiSprites.Rounded(radius), Color.white);
            UiFactory.Stretch(clip.rectTransform);
            var mask = clip.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var backdrop = UiFactory.CreateRawImage("Backdrop", clip.transform);
            UiFactory.Stretch(backdrop.rectTransform);

            var tintLayer = UiFactory.CreateImage("Tint", clip.transform, null, tint);
            UiFactory.Stretch(tintLayer.rectTransform);

            var highlight = UiFactory.CreateImage("Highlight", clip.transform, null, UiTheme.Highlight);
            var highlightRect = highlight.rectTransform;
            highlightRect.anchorMin = new Vector2(0f, 1f);
            highlightRect.anchorMax = new Vector2(1f, 1f);
            highlightRect.pivot = new Vector2(0.5f, 1f);
            highlightRect.anchoredPosition = Vector2.zero;
            highlightRect.sizeDelta = new Vector2(0f, Mathf.Max(1f, UiTheme.Px(1f)));

            var content = UiFactory.CreateRect("Content", clip.transform);
            UiFactory.Stretch(content);

            var outline = UiFactory.CreateImage(
                "Border",
                root,
                UiSprites.RoundedOutline(radius, Mathf.Max(1f, UiTheme.Px(1f))),
                border);
            UiFactory.Stretch(outline.rectTransform);

            return new GlassPanel(root, content, backdrop);
        }

        /// <summary>
        /// Point the glass at the blurred screen copy. Called every frame the panel is visible: the
        /// panel may have moved, and the fractal underneath keeps rendering.
        /// </summary>
        public void SetBackdrop(Texture blurred)
        {
            if (backdrop == null)
            {
                return;
            }

            backdrop.texture = blurred;
            if (blurred == null)
            {
                return;
            }

            Root.GetWorldCorners(Corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, Corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, Corners[2]);

            var width = Mathf.Max(1f, Screen.width);
            var height = Mathf.Max(1f, Screen.height);
            backdrop.uvRect = new Rect(
                min.x / width,
                min.y / height,
                (max.x - min.x) / width,
                (max.y - min.y) / height);
        }
    }
}
