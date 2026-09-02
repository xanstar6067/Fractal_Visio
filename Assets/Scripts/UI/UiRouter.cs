using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FractalVisio.App;

namespace FractalVisio.UI
{
    /// <summary>
    /// Owns the interface: the screen list, the shared backdrop blur, and the button that opens
    /// settings. Registered as a module, so the bootstrap drives it like any other.
    ///
    /// It also answers <see cref="PointerOverUi"/>, which the input layer needs: without it a drag
    /// on a panel would pan the fractal underneath at the same time.
    /// </summary>
    public sealed class UiRouter : IAppModule
    {
        private readonly List<UiScreen> screens = new();

        private AppServices services;
        private RectTransform root;
        private BackdropBlur blur;
        private GlassPanel toggle;
        private SettingsScreen settings;
        private int cachedWidth;
        private int cachedHeight;

        public string Id => "ui";

        /// <summary>
        /// True while a finger or the cursor is over a panel or the settings button. Computed on
        /// demand rather than cached: the input layer asks before the router ticks, and a
        /// one-frame-stale answer is exactly the frame a tap lands on.
        /// </summary>
        public bool PointerOverUi => ComputePointerOverUi();

        public void Initialize(AppServices appServices)
        {
            services = appServices;
            blur = new BackdropBlur();
            Build();
        }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            if (Screen.width != cachedWidth || Screen.height != cachedHeight)
            {
                // Every size in the UI is derived from screen height, so a rotation or a resize
                // rebuilds rather than rescales - the generated corner sprites change with it.
                Teardown();
                Build();
            }

            var anyVisible = false;
            for (var i = 0; i < screens.Count; i++)
            {
                if (screens[i].IsVisible)
                {
                    anyVisible = true;
                    break;
                }
            }

            if (anyVisible && services.Backdrop != null)
            {
                blur.Refresh(services.Backdrop.Texture, services.Backdrop.UvRect);
            }

            var backdrop = blur.Texture;
            var deltaTime = Time.unscaledDeltaTime;
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Tick(deltaTime, backdrop);
            }

            if (toggle != null)
            {
                toggle.SetBackdrop(backdrop);
            }
        }

        public void Shutdown()
        {
            Teardown();
            blur?.Dispose();
            blur = null;
            services = null;
        }

        public void ToggleSettings() => settings?.Toggle();

        private void Build()
        {
            cachedWidth = Screen.width;
            cachedHeight = Screen.height;

            EnsureEventSystem();

            root = UiFactory.CreateRect("Ui", services.UiRoot);
            UiFactory.Stretch(root);
            root.SetAsLastSibling();

            BuildToggle();

            settings = new SettingsScreen();
            settings.Build(root, services);
            screens.Add(settings);
        }

        private void Teardown()
        {
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Dispose();
            }

            screens.Clear();
            settings = null;
            toggle = null;

            if (root != null)
            {
                Object.Destroy(root.gameObject);
                root = null;
            }

            // The rounded-corner sprites are generated at the old device scale.
            UiSprites.Clear();
        }

        private void BuildToggle()
        {
            toggle = GlassPanel.Create("SettingsToggle", root, 14f, UiTheme.ButtonTint, UiTheme.ButtonBorder);

            var size = UiTheme.Px(UiTheme.ToggleSize);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            UiFactory.Anchor(
                toggle.Root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-margin, margin),
                new Vector2(size, size));

            var barWidth = size * 0.44f;
            var barHeight = Mathf.Max(2f, UiTheme.Px(2.5f));
            var barGap = UiTheme.Px(7f);
            var barRadius = Mathf.Max(1, Mathf.RoundToInt(barHeight * 0.5f));

            for (var i = 0; i < 3; i++)
            {
                var bar = UiFactory.CreateImage("Bar" + i, toggle.Content, UiSprites.Rounded(barRadius), UiTheme.Text);
                UiFactory.Anchor(
                    bar.rectTransform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, (1 - i) * barGap),
                    new Vector2(barWidth, barHeight));
            }

            // Invisible hit area on top. Its base alpha is low and the normal tint zeroes it, so the
            // button is transparent at rest and flashes only while pressed.
            var hit = UiFactory.CreateImage(
                "Hit",
                toggle.Root,
                UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(14f)))),
                new Color(1f, 1f, 1f, 0.12f));
            UiFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;

            var button = hit.gameObject.AddComponent<Button>();
            button.targetGraphic = hit;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.6f),
                pressedColor = new Color(1f, 1f, 1f, 1.6f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(1f, 1f, 1f, 0f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            button.onClick.AddListener(ToggleSettings);
        }

        private void EnsureEventSystem()
        {
            var canvas = services.UiRoot != null ? services.UiRoot.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (Object.FindAnyObjectByType<EventSystem>() != null)
            {
                return;
            }

            var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            go.transform.SetParent(null, false);
        }

        private bool ComputePointerOverUi()
        {
            if (Input.touchCount > 0)
            {
                for (var i = 0; i < Input.touchCount; i++)
                {
                    var touch = Input.GetTouch(i);
                    if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                    {
                        continue;
                    }

                    if (IsOverUi(touch.position))
                    {
                        return true;
                    }
                }

                return false;
            }

            return IsOverUi(Input.mousePosition);
        }

        private bool IsOverUi(Vector2 screenPoint)
        {
            if (toggle != null &&
                toggle.Root != null &&
                RectTransformUtility.RectangleContainsScreenPoint(toggle.Root, screenPoint, null))
            {
                return true;
            }

            for (var i = 0; i < screens.Count; i++)
            {
                if (screens[i].ContainsScreenPoint(screenPoint))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
