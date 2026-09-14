using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;

namespace FractalVisio.UI
{
    /// <summary>
    /// One panel of the interface. A screen reads the session and calls its setters; it never
    /// touches a renderer, and it owns nothing but its own GameObjects.
    /// </summary>
    public abstract class UiScreen
    {
        private const float FadeSeconds = 0.16f;

        private CanvasGroup group;
        private Vector2 restingPosition;
        private float visibility;
        private float target;

        protected AppServices Services { get; private set; }

        public GlassPanel Panel { get; protected set; }

        public bool IsOpen => target > 0.5f;

        /// <summary>True while any part of it is on screen, including the closing animation.</summary>
        public bool IsVisible => visibility > 0.001f;

        public void Build(Transform parent, AppServices services)
        {
            Services = services;
            NeedsRebuild = false;
            OnBuild(parent);

            if (Panel == null)
            {
                return;
            }

            group = Panel.Root.gameObject.AddComponent<CanvasGroup>();
            restingPosition = Panel.Root.anchoredPosition;
            visibility = 0f;
            target = 0f;
            Apply();
        }

        /// <summary>
        /// Set by a screen whose content no longer matches the session - another fractal with other
        /// parameters, a palette added. The router rebuilds the interface and reopens the screen.
        /// </summary>
        public bool NeedsRebuild { get; protected set; }

        public void Open() => SetOpen(true);

        public void Close() => SetOpen(false);

        public void Toggle() => SetOpen(!IsOpen);

        /// <summary>Show at once, without the fade. For a screen rebuilt while it was open.</summary>
        public void OpenImmediately()
        {
            SetOpen(true);
            if (Panel == null)
            {
                return;
            }

            visibility = 1f;
            Apply();
        }

        private void SetOpen(bool open)
        {
            var wasOpen = IsOpen;
            target = open ? 1f : 0f;
            if (wasOpen != open)
            {
                OnOpenChanged(open);
            }
        }

        /// <summary>Called when the screen is asked to open or close, before the animation.</summary>
        protected virtual void OnOpenChanged(bool open)
        {
        }

        public virtual void Tick(float deltaTime, Texture backdrop)
        {
            if (Panel == null)
            {
                return;
            }

            if (!Mathf.Approximately(visibility, target))
            {
                var step = deltaTime / FadeSeconds;
                visibility = Mathf.MoveTowards(visibility, target, step);
                Apply();
            }

            if (!IsVisible)
            {
                return;
            }

            Panel.SetBackdrop(backdrop);
            OnTick();
        }

        public bool ContainsScreenPoint(Vector2 point)
        {
            return IsVisible &&
                   Panel != null &&
                   RectTransformUtility.RectangleContainsScreenPoint(Panel.Root, point, null);
        }

        public virtual void Dispose()
        {
            if (Panel != null && Panel.Root != null)
            {
                Object.Destroy(Panel.Root.gameObject);
            }

            Panel = null;
        }

        protected abstract void OnBuild(Transform parent);

        /// <summary>Called once per frame while visible, after the backdrop is refreshed.</summary>
        protected virtual void OnTick()
        {
        }

        /// <summary>Width of a panel with up to <paramref name="maximumColumns"/> natural columns on this screen.</summary>
        protected static float ResolvePanelWidth(int maximumColumns, out int columns)
        {
            var availableWidth = UiTheme.AvailablePanelWidth;
            var naturalWidth = UiTheme.PanelPx(UiTheme.PanelWidth);
            columns = Mathf.Clamp(Mathf.FloorToInt(availableWidth / naturalWidth), 1, Mathf.Max(1, maximumColumns));
            return Mathf.Min(columns * naturalWidth, availableWidth);
        }

        /// <summary>
        /// The glass panel every screen sits in, anchored bottom-right above the toolbar, and a
        /// scrolling content rectangle inside it of height <paramref name="contentHeight"/>.
        /// </summary>
        protected RectTransform CreateScrollingPanel(Transform parent, string name, float width, float contentHeight)
        {
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var height = Mathf.Min(contentHeight, UiTheme.AvailablePanelHeight);

            Panel = GlassPanel.Create(name, parent, UiTheme.PanelRadius, UiTheme.PanelTint, UiTheme.PanelBorder);
            UiFactory.Anchor(
                Panel.Root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-margin, margin * 2f + UiTheme.Px(UiTheme.ToggleSize)),
                new Vector2(width, height));

            return BuildScroll(contentHeight, height);
        }

        /// <summary>Title line at the top of a panel's content. Returns its height.</summary>
        protected static float AddTitle(RectTransform content, string text, float padding, float width)
        {
            var titleHeight = UiTheme.PanelPx(28f);
            var title = UiFactory.CreateText(
                "Title", content, text, UiTheme.TitleFontSize, UiTheme.Text,
                TextAnchor.UpperLeft, fitToRect: true, panelScale: true);
            Place(title.rectTransform, padding, -padding, width - padding * 2f, titleHeight);
            title.fontStyle = FontStyle.Bold;
            return titleHeight;
        }

        /// <summary>Section caption in the muted label style. Returns its height.</summary>
        protected static float AddCaption(RectTransform content, string text, float x, float y, float width)
        {
            var height = UiTheme.PanelPx(20f);
            var caption = UiFactory.CreateText(
                "Caption_" + text, content, text, UiTheme.LabelFontSize, UiTheme.TextMuted,
                TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
            Place(caption.rectTransform, x, y, width, height);
            return height;
        }

        protected static void Place(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Wrap the panel content in a scroll view. The viewport is the glass panel's own content
        /// rectangle, which is already inside its rounded mask - so the list is clipped by the same
        /// shape that draws the panel, with no second mask to keep in sync.
        /// </summary>
        private RectTransform BuildScroll(float contentHeight, float viewportHeight)
        {
            var content = UiFactory.CreateRect("ScrollContent", Panel.Content);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, contentHeight);

            // Something has to catch the drag in the gaps between rows, or a finger that starts
            // between two options scrolls nothing.
            var dragArea = UiFactory.CreateImage("DragArea", content, null, new Color(0f, 0f, 0f, 0f));
            UiFactory.Stretch(dragArea.rectTransform);
            dragArea.raycastTarget = true;

            var scroll = Panel.Content.gameObject.AddComponent<ScrollRect>();
            scroll.content = content;
            scroll.viewport = Panel.Content;
            scroll.horizontal = false;
            scroll.vertical = contentHeight > viewportHeight + 1f;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = UiTheme.PanelPx(28f);

            return content;
        }

        private void Apply()
        {
            // Smoothstep so the panel settles instead of stopping dead, and a small rise on the way
            // in - the movement is what makes it read as a sheet of glass rather than a fade.
            var eased = visibility * visibility * (3f - 2f * visibility);

            group.alpha = eased;
            group.blocksRaycasts = eased > 0.5f;
            group.interactable = eased > 0.5f;

            Panel.Root.anchoredPosition = restingPosition + new Vector2(0f, (eased - 1f) * UiTheme.Px(14f));
            var scale = Mathf.Lerp(0.97f, 1f, eased);
            Panel.Root.localScale = new Vector3(scale, scale, 1f);

            Panel.Root.gameObject.SetActive(eased > 0.001f);
        }
    }
}
