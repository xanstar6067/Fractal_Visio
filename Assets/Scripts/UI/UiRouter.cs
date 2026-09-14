using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FractalVisio.App;

namespace FractalVisio.UI
{
    /// <summary>
    /// Owns the interface: the toolbar in the bottom-right corner, the panels it opens, the shared
    /// backdrop blur and the short confirmation messages. Registered as a module, so the bootstrap
    /// drives it like any other - and registered <b>after</b> the modules whose services its screens
    /// use, so those services exist by the time the screens are built.
    ///
    /// One panel is open at a time: they all sit in the same corner above the toolbar.
    ///
    /// It also answers <see cref="PointerOverUi"/>, which the input layer needs: without it a drag
    /// on a panel would pan the fractal underneath at the same time.
    /// </summary>
    public sealed class UiRouter : IAppModule
    {
        private const float ToastSeconds = 2.6f;
        private const float ToastFadeSeconds = 0.25f;

        private readonly List<UiScreen> screens = new();
        private readonly List<GlassPanel> toolbar = new();

        private AppServices services;
        private RectTransform root;
        private BackdropBlur blur;
        private SettingsScreen settings;
        private BookmarksScreen bookmarks;
        private PaletteEditorScreen paletteEditor;
        private IScreenshotService screenshots;

        private GlassPanel toast;
        private Text toastText;
        private CanvasGroup toastGroup;
        private float toastUntil;

        private int cachedWidth;
        private int cachedHeight;
        private float builtInterfaceScale = 1f;

        public string Id => "ui";

        /// <summary>
        /// True while a finger or the cursor is over a panel or a toolbar button. Computed on
        /// demand rather than cached: the input layer asks before the router ticks, and a
        /// one-frame-stale answer is exactly the frame a tap lands on.
        /// </summary>
        public bool PointerOverUi => ComputePointerOverUi();

        public void Initialize(AppServices appServices)
        {
            services = appServices;
            blur = new BackdropBlur();

            // Screens are created once and survive rebuilds: a rotation or a new interface size
            // rebuilds their GameObjects, not their state - the palette being edited stays edited.
            settings = new SettingsScreen(OpenPaletteEditor);
            bookmarks = new BookmarksScreen();
            paletteEditor = new PaletteEditorScreen();
            screens.Add(settings);
            screens.Add(bookmarks);
            screens.Add(paletteEditor);

            screenshots = services.Get<IScreenshotService>();
            if (screenshots != null)
            {
                screenshots.StateChanged += OnScreenshotStateChanged;
            }

            Build();
        }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            if (Screen.width != cachedWidth ||
                Screen.height != cachedHeight ||
                !Mathf.Approximately(services.Session.Interface.Scale, builtInterfaceScale))
            {
                // Sizes come from the screen density and the interface setting, and the rounded
                // corner sprites are generated at a fixed pixel radius - so a rotation, a resize or
                // a new interface size rebuilds rather than rescales.
                Teardown();
                Build();
            }

            for (var i = 0; i < screens.Count; i++)
            {
                if (screens[i].NeedsRebuild)
                {
                    RebuildScreen(screens[i]);
                }
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

            for (var i = 0; i < toolbar.Count; i++)
            {
                toolbar[i].SetBackdrop(backdrop);
            }

            TickToast(backdrop);
        }

        public void Shutdown()
        {
            if (screenshots != null)
            {
                screenshots.StateChanged -= OnScreenshotStateChanged;
            }

            Teardown();
            screens.Clear();
            blur?.Dispose();
            blur = null;
            services = null;
        }

        public void ToggleSettings() => ToggleExclusive(settings);

        private void OpenPaletteEditor()
        {
            settings.Close();
            paletteEditor.Open();
        }

        private void ToggleExclusive(UiScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            var opening = !screen.IsOpen;
            for (var i = 0; i < screens.Count; i++)
            {
                if (screens[i] != screen)
                {
                    screens[i].Close();
                }
            }

            if (opening)
            {
                screen.Open();
            }
            else
            {
                screen.Close();
            }
        }

        private void Build()
        {
            cachedWidth = Screen.width;
            cachedHeight = Screen.height;

            // The interface scale is a session setting; UiTheme is where every size reads it from.
            builtInterfaceScale = services.Session.Interface.Scale;
            UiTheme.UserScale = builtInterfaceScale;

            EnsureEventSystem();

            root = UiFactory.CreateRect("Ui", services.UiRoot);
            UiFactory.Stretch(root);
            root.SetAsLastSibling();

            BuildToolbar();
            BuildToast();

            for (var i = 0; i < screens.Count; i++)
            {
                var wasOpen = screens[i].IsOpen;
                screens[i].Build(root, services);
                if (wasOpen)
                {
                    screens[i].OpenImmediately();
                }
            }
        }

        private void RebuildScreen(UiScreen screen)
        {
            var wasOpen = screen.IsOpen;
            screen.Dispose();
            screen.Build(root, services);
            if (wasOpen)
            {
                screen.OpenImmediately();
            }
        }

        private void Teardown()
        {
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Dispose();
            }

            toolbar.Clear();
            toast = null;
            toastText = null;
            toastGroup = null;

            if (root != null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                root = null;
            }

            // The rounded-corner sprites are generated at the old device scale.
            UiSprites.Clear();
        }

        private void BuildToolbar()
        {
            toolbar.Clear();
            var slot = 0;

            AddToolbarButton("SettingsToggle", slot++, BuildMenuIcon, ToggleSettings);

            if (services.Get<IBookmarkService>() != null)
            {
                AddToolbarButton("BookmarksToggle", slot++,
                    (parent, size) => AddIcon(parent, UiSprites.Star(Mathf.RoundToInt(size * 0.5f)), size * 0.5f),
                    () => ToggleExclusive(bookmarks));
            }

            if (screenshots != null)
            {
                AddToolbarButton("SaveImage", slot,
                    (parent, size) => AddIcon(parent, UiSprites.Camera(Mathf.RoundToInt(size * 0.52f)), size * 0.52f),
                    RequestScreenshot);
            }
        }

        private void RequestScreenshot()
        {
            // The panels are not in the image, but closing them lets the viewer see what is being saved.
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Close();
            }

            screenshots.Request();
        }

        private void OnScreenshotStateChanged(ScreenshotState state)
        {
            ShowToast(screenshots.LastMessage, state == ScreenshotState.WaitingForRender ? 30f : ToastSeconds);
        }

        /// <summary>A round glass button in the toolbar, <paramref name="slot"/> places left of the corner.</summary>
        private void AddToolbarButton(string name, int slot, Action<RectTransform, float> buildIcon, Action onClick)
        {
            var button = GlassPanel.Create(name, root, 14f, UiTheme.ButtonTint, UiTheme.ButtonBorder);

            var size = UiTheme.Px(UiTheme.ToggleSize);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var gap = UiTheme.Px(UiTheme.RowSpacing * 1.5f);
            UiFactory.Anchor(
                button.Root,
                new Vector2(1f, 0f),
                new Vector2(1f, 0f),
                new Vector2(-margin - slot * (size + gap), margin),
                new Vector2(size, size));

            buildIcon(button.Content, size);

            // Invisible hit area on top. Its base alpha is low and the normal tint zeroes it, so the
            // button is transparent at rest and flashes only while pressed.
            var hit = UiFactory.CreateImage(
                "Hit",
                button.Root,
                UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(UiTheme.Px(14f)))),
                new Color(1f, 1f, 1f, 0.12f));
            UiFactory.Stretch(hit.rectTransform);
            hit.raycastTarget = true;

            var control = hit.gameObject.AddComponent<Button>();
            control.targetGraphic = hit;
            control.transition = Selectable.Transition.ColorTint;
            control.colors = new ColorBlock
            {
                normalColor = new Color(1f, 1f, 1f, 0f),
                highlightedColor = new Color(1f, 1f, 1f, 0.6f),
                pressedColor = new Color(1f, 1f, 1f, 1.6f),
                selectedColor = new Color(1f, 1f, 1f, 0f),
                disabledColor = new Color(1f, 1f, 1f, 0f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            control.onClick.AddListener(() => onClick());

            toolbar.Add(button);
        }

        private static void BuildMenuIcon(RectTransform parent, float size)
        {
            var barWidth = size * 0.44f;
            var barHeight = Mathf.Max(2f, UiTheme.Px(2.5f));
            var barGap = UiTheme.Px(7f);
            var barRadius = Mathf.Max(1, Mathf.RoundToInt(barHeight * 0.5f));

            for (var i = 0; i < 3; i++)
            {
                var bar = UiFactory.CreateImage("Bar" + i, parent, UiSprites.Rounded(barRadius), UiTheme.Text);
                UiFactory.Anchor(
                    bar.rectTransform,
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0.5f, 0.5f),
                    new Vector2(0f, (1 - i) * barGap),
                    new Vector2(barWidth, barHeight));
            }
        }

        private static void AddIcon(RectTransform parent, Sprite sprite, float size)
        {
            var icon = UiFactory.CreateImage("Icon", parent, null, UiTheme.Text);
            icon.sprite = sprite;
            icon.type = Image.Type.Simple;
            UiFactory.Anchor(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size, size));
        }

        private void BuildToast()
        {
            toast = GlassPanel.Create("Toast", root, 14f, UiTheme.PanelTint, UiTheme.PanelBorder);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var height = UiTheme.Px(44f);
            var width = Mathf.Min(UiTheme.Px(360f), Screen.width - margin * 2f);
            UiFactory.Anchor(
                toast.Root,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2(0f, margin * 2f + UiTheme.Px(UiTheme.ToggleSize)),
                new Vector2(width, height));

            toastText = UiFactory.CreateText(
                "Message", toast.Content, string.Empty, UiTheme.LabelFontSize + 2, UiTheme.Text,
                TextAnchor.MiddleCenter, fitToRect: true);
            UiFactory.Stretch(toastText.rectTransform, UiTheme.Px(10f));

            toastGroup = toast.Root.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastGroup.interactable = false;
            toastGroup.alpha = 0f;
            toast.Root.gameObject.SetActive(false);
        }

        private void ShowToast(string message, float seconds)
        {
            if (toast == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            toastText.text = message;
            toastUntil = Time.unscaledTime + seconds;
            toast.Root.gameObject.SetActive(true);
        }

        private void TickToast(Texture backdrop)
        {
            if (toast == null || !toast.Root.gameObject.activeSelf)
            {
                return;
            }

            var remaining = toastUntil - Time.unscaledTime;
            toastGroup.alpha = Mathf.Clamp01(remaining / ToastFadeSeconds);
            toast.SetBackdrop(backdrop);
            if (remaining <= 0f)
            {
                toast.Root.gameObject.SetActive(false);
            }
        }

        private void EnsureEventSystem()
        {
            var canvas = services.UiRoot != null ? services.UiRoot.GetComponentInParent<Canvas>() : null;
            if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }

            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null)
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
            for (var i = 0; i < toolbar.Count; i++)
            {
                if (toolbar[i].Root != null &&
                    RectTransformUtility.RectangleContainsScreenPoint(toolbar[i].Root, screenPoint, null))
                {
                    return true;
                }
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
