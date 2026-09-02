using UnityEngine;
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

        public void Open() => target = 1f;

        public void Close() => target = 0f;

        public void Toggle() => target = IsOpen ? 0f : 1f;

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
