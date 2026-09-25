using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Owns the interface and the way around it. Two places: the <b>gallery</b> - the main menu,
    /// open at start - and the <b>explorer</b>, the picture with its chrome (gallery button, toolbar)
    /// and one panel at a time beside the toolbar. Registered as a module, so the bootstrap drives
    /// it like any other, and registered <b>after</b> the modules whose services its screens use.
    ///
    /// Layers, bottom to top: explorer chrome, gallery, panels, toast. The gallery covers the
    /// chrome; a panel opened from the gallery (settings) sits above it.
    ///
    /// Back (Android's button, Escape on a desktop) walks outwards: a panel, then hidden chrome,
    /// then the gallery; in the gallery a second press within two seconds leaves the app.
    ///
    /// It also answers <see cref="PointerOverUi"/>, which the input layer needs: without it a drag
    /// on a panel would pan the fractal underneath at the same time.
    /// </summary>
    public sealed class UiRouter : IAppModule
    {
        private const string RootName = "Ui";
        private const float ToastSeconds = 2.6f;
        private const float ToastFadeSeconds = 0.25f;

        /// <summary>How long "Undo" stays on offer: long enough to reach for, short enough not to linger.</summary>
        private const float UndoSeconds = 6f;

        /// <summary>Share of the toast's width its action word takes.</summary>
        private const float ToastActionFraction = 0.32f;
        private const float ExitWindowSeconds = 2f;

        /// <summary>Travel before a press on a control becomes a drag, in dp - the touch slop, as in the gesture layer.</summary>
        private const float DragThresholdDp = 8f;

        private readonly List<UiScreen> screens = new();
        private readonly List<UiScreen> panels = new();

        private AppServices services;
        private RectTransform root;
        private BackdropBlur blur;
        private ExplorerChrome chrome;
        private GalleryScreen gallery;
        private FractalScreen fractalPanel;
        private ParameterMapScreen planeMap;
        private ColorScreen colorPanel;
        private SettingsScreen settings;
        private BookmarksScreen bookmarks;
        private BookmarkActionsScreen bookmarkActions;
        private PaletteEditorScreen paletteEditor;
        private IScreenshotService screenshots;
        private IBookmarkService bookmarkService;
        private Image panelShield;

        private GlassPanel toast;
        private Text toastText;
        private CanvasGroup toastGroup;
        private GameObject toastActionObject;
        private Text toastActionText;
        private Action toastAction;
        private float toastUntil;

        /// <summary>Chrome hidden by a tap on the picture, to see it whole.</summary>
        private bool chromeHidden;

        private float exitArmedUntil;

        private int cachedWidth;
        private int cachedHeight;
        private Rect cachedSafeArea;
        private float builtInterfaceScale = 1f;
        private string builtLanguage;

        public string Id => "ui";

        /// <summary>
        /// True while a finger or the cursor is over a panel, the gallery or the chrome. Computed on
        /// demand rather than cached: the input layer asks before the router ticks, and a
        /// one-frame-stale answer is exactly the frame a tap lands on.
        /// </summary>
        public bool PointerOverUi => ComputePointerOverUi();

        public void Initialize(AppServices appServices)
        {
            services = appServices;
            blur = new BackdropBlur();

            // Screens are created once and survive rebuilds: a rotation or a new interface size
            // rebuilds their GameObjects, not their state - the palette being edited stays edited,
            // the gallery keeps its filter.
            bookmarkService = services.Get<IBookmarkService>();
            gallery = new GalleryScreen(() => ToggleExclusive(settings), bookmark => ShowBookmarkActions(bookmark, false));
            fractalPanel = new FractalScreen(OpenGallery, () => OpenExclusive(planeMap));
            planeMap = new ParameterMapScreen();
            colorPanel = new ColorScreen(OpenPaletteEditor);
            settings = new SettingsScreen();
            bookmarks = new BookmarksScreen(bookmark => ShowBookmarkActions(bookmark, true));
            bookmarkActions = new BookmarkActionsScreen(OpenBookmark, ShowUndo);
            paletteEditor = new PaletteEditorScreen();

            // Build order is draw order: the gallery under the panels, so settings opened from the
            // gallery appear on top of it.
            screens.Add(gallery);
            panels.Add(fractalPanel);
            panels.Add(planeMap);
            panels.Add(colorPanel);
            panels.Add(bookmarks);
            panels.Add(bookmarkActions);
            panels.Add(settings);
            panels.Add(paletteEditor);
            screens.AddRange(panels);

            screenshots = services.Get<IScreenshotService>();
            if (screenshots != null)
            {
                screenshots.StateChanged += OnScreenshotStateChanged;
            }

            Build();

            // The gallery is the main menu: the app opens on it.
            gallery.OpenImmediately();
        }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            if (Screen.width != cachedWidth ||
                Screen.height != cachedHeight ||
                Screen.safeArea != cachedSafeArea ||
                !Mathf.Approximately(services.Session.Interface.Scale, builtInterfaceScale) ||
                services.Strings.Language != builtLanguage)
            {
                // Sizes come from the screen density and the interface setting, and the rounded
                // corner sprites are generated at a fixed pixel radius - so a rotation, a resize or
                // a new interface size rebuilds rather than rescales. Text is read from the string
                // catalog while building, so a new language is a rebuild too: every screen then
                // picks up the new strings without having to know the language changed.
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

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                HandleBack();
            }

            // The C map belongs to a fractal with a plane; another fractal has nothing to pick on.
            if (planeMap.IsOpen && ParameterMapScreen.FindPlane(services, services.Session.Definition) == null)
            {
                planeMap.Close();
            }

            chrome.SetVisible(!gallery.IsOpen && !chromeHidden);
            panelShield.gameObject.SetActive(gallery.IsOpen && AnyPanelOpen());

            var anyVisible = chrome.IsVisible;
            for (var i = 0; i < screens.Count && !anyVisible; i++)
            {
                anyVisible = screens[i].IsVisible;
            }

            if (anyVisible && services.Backdrop != null)
            {
                blur.Refresh(services.Backdrop.Texture, services.Backdrop.UvRect);
            }

            var backdrop = blur.Texture;
            var deltaTime = Time.unscaledDeltaTime;
            chrome.Tick(deltaTime, backdrop);
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Tick(deltaTime, backdrop);
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
            panels.Clear();
            blur?.Dispose();
            blur = null;
            services = null;
        }

        public void OpenGallery()
        {
            CloseAllPanels();
            chromeHidden = false;
            gallery.Open();
        }

        /// <summary>
        /// A tap on the picture, outside every control. It closes an open panel - the usual way to
        /// dismiss a sheet on a touch screen - and with none open it shows or hides the chrome.
        /// </summary>
        public void HandleBackgroundTap()
        {
            if (gallery.IsOpen)
            {
                return;
            }

            if (AnyPanelOpen())
            {
                CloseAllPanels();
                return;
            }

            chromeHidden = !chromeHidden;
        }

        /// <summary>Whether a screen point is on any visible control. For the bootstrap to tell a tap on the picture from a tap on a button.</summary>
        public bool IsOverUi(Vector2 screenPoint)
        {
            if (chrome != null && chrome.ContainsScreenPoint(screenPoint))
            {
                return true;
            }

            // A toast with an action takes touches; a plain one lets them through to the picture.
            if (toast != null && toastGroup != null && toastGroup.blocksRaycasts &&
                toast.Root.gameObject.activeInHierarchy &&
                RectTransformUtility.RectangleContainsScreenPoint(toast.Root, screenPoint, null))
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

        private void HandleBack()
        {
            if (paletteEditor.IsOpen)
            {
                // Closing the editor without saving puts the palette back; the colour panel it
                // came from is where the user expects to land.
                paletteEditor.Close();
                colorPanel.Open();
                return;
            }

            if (AnyPanelOpen())
            {
                CloseAllPanels();
                return;
            }

            if (!gallery.IsOpen)
            {
                if (chromeHidden)
                {
                    chromeHidden = false;
                    return;
                }

                OpenGallery();
                return;
            }

            if (Application.platform != RuntimePlatform.Android)
            {
                // A desktop has no "leave the app" button to honour: back from the menu is back to the picture.
                gallery.Close();
                return;
            }

            if (Time.unscaledTime < exitArmedUntil)
            {
                Application.Quit();
                return;
            }

            exitArmedUntil = Time.unscaledTime + ExitWindowSeconds;
            ShowToast(services.Strings.Get("gallery.exit_hint"), ExitWindowSeconds);
        }

        private bool AnyPanelOpen()
        {
            for (var i = 0; i < panels.Count; i++)
            {
                if (panels[i].IsOpen)
                {
                    return true;
                }
            }

            return false;
        }

        private void CloseAllPanels()
        {
            for (var i = 0; i < panels.Count; i++)
            {
                panels[i].Close();
            }
        }

        private void OpenPaletteEditor()
        {
            colorPanel.Close();
            paletteEditor.Open();
        }

        /// <summary><paramref name="screen"/> as the one panel, left open if it already is - for a panel that replaces the one it was opened from.</summary>
        private void OpenExclusive(UiScreen screen)
        {
            if (!screen.IsOpen)
            {
                ToggleExclusive(screen);
            }
        }

        /// <summary>
        /// Rename and delete for one bookmark. From the bookmarks panel the sheet replaces it and
        /// hands it back afterwards; over the gallery the gallery simply stays underneath.
        /// </summary>
        private void ShowBookmarkActions(Bookmark bookmark, bool fromPanel)
        {
            bookmarkActions.Show(bookmark, fromPanel ? () => OpenExclusive(bookmarks) : null);
            OpenExclusive(bookmarkActions);
        }

        /// <summary>Go to a bookmark with nothing in the way: panels and the gallery close.</summary>
        private void OpenBookmark(Bookmark bookmark)
        {
            if (bookmarkService == null || !bookmarkService.Open(bookmark))
            {
                return;
            }

            CloseAllPanels();
            chromeHidden = false;
            if (gallery.IsOpen)
            {
                gallery.Close();
            }
        }

        /// <summary>Open <paramref name="screen"/> as the one panel, or close it if it is the one open.</summary>
        private void ToggleExclusive(UiScreen screen)
        {
            if (screen == null)
            {
                return;
            }

            var opening = !screen.IsOpen;
            for (var i = 0; i < panels.Count; i++)
            {
                if (panels[i] != screen)
                {
                    panels[i].Close();
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
            cachedSafeArea = Screen.safeArea;

            // The interface scale is a session setting; UiTheme is where every size reads it from.
            builtInterfaceScale = services.Session.Interface.Scale;
            UiTheme.UserScale = builtInterfaceScale;
            builtLanguage = services.Strings.Language;

            EnsureEventSystem();
            RemoveLeftoverRoots();

            root = UiFactory.CreateRect(RootName, services.UiRoot);
            UiFactory.Stretch(root);
            root.SetAsLastSibling();

            chrome = new ExplorerChrome();
            chrome.Build(root, services, BuildToolbarItems(), OpenGallery, () => ToggleExclusive(planeMap), () => planeMap.IsOpen);

            BuildScreen(gallery);
            BuildPanelShield();
            for (var i = 0; i < panels.Count; i++)
            {
                BuildScreen(panels[i]);
            }

            BuildToast();
        }

        private void BuildScreen(UiScreen screen)
        {
            var wasOpen = screen.IsOpen;
            screen.Build(root, services);
            if (wasOpen)
            {
                screen.OpenImmediately();
            }
        }

        /// <summary>
        /// Sibling index of a screen's panel under the root: chrome 0, gallery 1, shield 2, then the
        /// panels in order. A rebuilt panel is appended and has to be put back in its layer.
        /// </summary>
        private int LayerIndex(UiScreen screen)
        {
            return screen == gallery ? 1 : 3 + Mathf.Max(0, panels.IndexOf(screen));
        }

        /// <summary>
        /// Between the gallery and the panels: a dim, full-screen catch for a tap beside a panel
        /// opened over the gallery. The explorer does not need one - there a tap on the picture
        /// closes the panel through <see cref="HandleBackgroundTap"/>, and a drag still moves the
        /// picture - but the gallery is itself interface, and a tap on it would open a fractal.
        /// </summary>
        private void BuildPanelShield()
        {
            panelShield = UiFactory.CreateImage("PanelShield", root, null, new Color(0f, 0f, 0f, 0.35f));
            UiFactory.Stretch(panelShield.rectTransform);
            panelShield.raycastTarget = true;

            var button = panelShield.gameObject.AddComponent<Button>();
            button.transition = Selectable.Transition.None;
            button.onClick.AddListener(CloseAllPanels);
            panelShield.gameObject.SetActive(false);
        }

        /// <summary>
        /// The toolbar, in the order of the question the user is asking: what is this, how does it
        /// look, where have I been, keep it, and the app itself.
        /// </summary>
        private List<ExplorerChrome.Item> BuildToolbarItems()
        {
            var items = new List<ExplorerChrome.Item>
            {
                new("toolbar.fractal", UiSprites.Sliders, () => ToggleExclusive(fractalPanel), () => fractalPanel.IsOpen),
                new("toolbar.colour", UiSprites.Palette, () => ToggleExclusive(colorPanel),
                    () => colorPanel.IsOpen || paletteEditor.IsOpen)
            };

            if (services.Get<IBookmarkService>() != null)
            {
                items.Add(new ExplorerChrome.Item(
                    "toolbar.bookmarks", UiSprites.Star, () => ToggleExclusive(bookmarks),
                    () => bookmarks.IsOpen || bookmarkActions.IsOpen));
            }

            if (screenshots != null)
            {
                items.Add(new ExplorerChrome.Item("toolbar.snapshot", UiSprites.Camera, RequestScreenshot));
            }

            items.Add(new ExplorerChrome.Item("toolbar.settings", UiSprites.Gear, () => ToggleExclusive(settings), () => settings.IsOpen));
            return items;
        }

        private void RebuildScreen(UiScreen screen)
        {
            var wasOpen = screen.IsOpen;
            screen.Dispose();
            screen.Build(root, services);

            // Build appends: put it back at its layer, or a rebuilt gallery would cover the panels.
            if (screen.Panel != null)
            {
                screen.Panel.Root.SetSiblingIndex(Mathf.Min(root.childCount - 1, LayerIndex(screen)));
            }

            if (wasOpen)
            {
                screen.OpenImmediately();
            }
        }

        /// <summary>
        /// Destroy interface roots this router does not own. A script edited during Play mode
        /// reloads the domain: every module is created again, while the previous interface - plain
        /// GameObjects - survives, and the old full-screen gallery then covers the new one.
        /// </summary>
        private void RemoveLeftoverRoots()
        {
            var parent = services.UiRoot;
            if (parent == null)
            {
                return;
            }

            for (var i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name == RootName && child != root)
                {
                    UnityEngine.Object.Destroy(child.gameObject);
                }
            }
        }

        private void Teardown()
        {
            for (var i = 0; i < screens.Count; i++)
            {
                screens[i].Dispose();
            }

            chrome?.Dispose();
            chrome = null;
            panelShield = null;
            toast = null;
            toastText = null;
            toastGroup = null;
            toastActionObject = null;
            toastActionText = null;
            toastAction = null;

            if (root != null)
            {
                UnityEngine.Object.Destroy(root.gameObject);
                root = null;
            }

            // The rounded-corner sprites are generated at the old device scale.
            UiSprites.Clear();
        }

        private void RequestScreenshot()
        {
            // The panels are not in the image, but closing them lets the viewer see what is being saved.
            CloseAllPanels();
            screenshots.Request();
        }

        private void OnScreenshotStateChanged(ScreenshotState state)
        {
            ShowScreenshotToast(state);
        }

        /// <summary>
        /// The screenshot's progress or outcome, with Cancel while the image renders and Share once
        /// it is in the gallery - the one place either can be reached from.
        /// </summary>
        private void ShowScreenshotToast(ScreenshotState state)
        {
            if (screenshots.CanCancel)
            {
                ShowToast(screenshots.LastMessage, 3600f, services.Strings.Get("common.cancel"), screenshots.Cancel);
            }
            else if (screenshots.CanShare)
            {
                ShowToast(screenshots.LastMessage, UndoSeconds, services.Strings.Get("screenshot.share"), screenshots.Share);
            }
            else
            {
                ShowToast(screenshots.LastMessage, IsScreenshotBusy(state) ? 3600f : ToastSeconds);
            }
        }

        private static bool IsScreenshotBusy(ScreenshotState state) =>
            state == ScreenshotState.WaitingForRender || state == ScreenshotState.ReadingPixels ||
            state == ScreenshotState.Encoding || state == ScreenshotState.AddingToGallery;

        private void BuildToast()
        {
            toast = GlassPanel.Create("Toast", root, 14f, UiTheme.PanelTint, UiTheme.PanelBorder);
            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            var height = UiTheme.Px(UiTheme.SegmentHeight);
            var width = Mathf.Min(UiTheme.Px(380f), Screen.width - UiTheme.SafeLeft - UiTheme.SafeRight - margin * 2f);

            // Above the toolbar when it runs along the bottom; at the bottom when it is on the side.
            var bottom = UiTheme.SafeBottom + margin + (UiTheme.ToolbarVertical ? 0f : UiTheme.ToolbarThickness + margin);
            UiFactory.Anchor(
                toast.Root,
                new Vector2(0.5f, 0f),
                new Vector2(0.5f, 0f),
                new Vector2((UiTheme.SafeLeft - UiTheme.SafeRight) * 0.5f, bottom),
                new Vector2(width, height));

            toastText = UiFactory.CreateText(
                "Message", toast.Content, string.Empty, UiTheme.LabelFontSize + 2, UiTheme.Text,
                TextAnchor.MiddleCenter, fitToRect: true);
            UiFactory.Stretch(toastText.rectTransform, UiTheme.Px(10f));

            // The action - Undo, Cancel, Share - is a word at the right end, the whole height of the
            // toast: a finger-sized target without a button drawn inside a button.
            var actionWidth = width * ToastActionFraction;
            var action = UiFactory.CreateImage(
                "Action", toast.Content, UiSprites.Rounded(UiTheme.PxInt(14f)), new Color(1f, 1f, 1f, 0.12f));
            action.raycastTarget = true;
            UiFactory.Anchor(
                action.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), Vector2.zero,
                new Vector2(actionWidth, height));
            toastActionText = UiFactory.CreateText(
                "Label", action.transform, string.Empty, UiTheme.LabelFontSize + 2, UiTheme.Accent,
                TextAnchor.MiddleCenter, fitToRect: true);
            toastActionText.fontStyle = FontStyle.Bold;
            UiFactory.Stretch(toastActionText.rectTransform, UiTheme.Px(6f));
            var button = action.gameObject.AddComponent<Button>();
            button.targetGraphic = action;
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
            button.onClick.AddListener(RunToastAction);
            toastActionObject = action.gameObject;
            toastActionObject.SetActive(false);

            toastGroup = toast.Root.gameObject.AddComponent<CanvasGroup>();
            toastGroup.blocksRaycasts = false;
            toastGroup.interactable = false;
            toastGroup.alpha = 0f;
            toast.Root.gameObject.SetActive(false);
        }

        /// <summary>
        /// A message along the bottom for <paramref name="seconds"/>. With an action it gets a word at
        /// its right end that runs it - "Undo" after a delete, "Cancel" while an image renders - and
        /// then it takes touches, which it otherwise lets through to the picture.
        /// </summary>
        private void ShowToast(string message, float seconds, string actionLabel = null, Action action = null)
        {
            if (toast == null || string.IsNullOrEmpty(message))
            {
                return;
            }

            var hasAction = action != null && !string.IsNullOrEmpty(actionLabel);
            toastAction = hasAction ? action : null;
            toastActionObject.SetActive(hasAction);
            toastActionText.text = hasAction ? actionLabel : string.Empty;

            var padding = UiTheme.Px(10f);
            var textRect = toastText.rectTransform;
            textRect.offsetMin = new Vector2(padding, padding * 0.5f);
            textRect.offsetMax = new Vector2(
                hasAction ? -toast.Root.sizeDelta.x * ToastActionFraction : -padding, -padding * 0.5f);
            toastText.alignment = hasAction ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter;

            toastGroup.blocksRaycasts = hasAction;
            toastGroup.interactable = hasAction;
            toastText.text = message;
            toastUntil = Time.unscaledTime + seconds;
            toast.Root.gameObject.SetActive(true);
        }

        /// <summary>"Removed" with an Undo beside it, for a screen that just deleted something.</summary>
        private void ShowUndo(string message, Action undo)
        {
            ShowToast(message, UndoSeconds, services.Strings.Get("common.undo"), undo);
        }

        private void RunToastAction()
        {
            var action = toastAction;
            toastAction = null;
            toastUntil = Mathf.Min(toastUntil, Time.unscaledTime + ToastFadeSeconds);
            toastGroup.blocksRaycasts = false;
            action?.Invoke();
        }

        private void TickToast(Texture backdrop)
        {
            if (screenshots != null && IsScreenshotBusy(screenshots.State))
            {
                if (toast != null && !toast.Root.gameObject.activeSelf)
                {
                    ShowScreenshotToast(screenshots.State);
                }

                if (screenshots.State == ScreenshotState.WaitingForRender && toastText != null)
                {
                    toastText.text = services.Strings.Format("screenshot.rendering_progress",
                        Mathf.RoundToInt(screenshots.RenderProgress * 100f),
                        Mathf.FloorToInt(screenshots.PhaseSeconds));
                }
                else if (toastText != null)
                {
                    toastText.text = services.Strings.Format("screenshot.stage_progress",
                        screenshots.LastMessage, Mathf.FloorToInt(screenshots.PhaseSeconds));
                }
            }

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

            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
            {
                var go = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                go.transform.SetParent(null, false);
                eventSystem = go.GetComponent<EventSystem>();
            }

            // The default is 10 pixels whatever the screen - about 1 dp on a modern phone, so a
            // finger's wobble during a tap turned it into a drag and the card in the scrolling
            // gallery never received its click. The same slop the gesture layer uses, in dp.
            eventSystem.pixelDragThreshold = Mathf.Max(10, Mathf.RoundToInt(ScreenScale.Dp(DragThresholdDp)));
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
    }
}
