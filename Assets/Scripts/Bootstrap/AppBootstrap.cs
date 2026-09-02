using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;
using FractalVisio.Gestures;
using FractalVisio.Modules;

namespace FractalVisio.Bootstrap
{
    /// <summary>
    /// Composition root: the one MonoBehaviour on the scene. It builds the session, the presenter
    /// and the module list, then drives them each frame - gestures into the session, session into
    /// the presenter, presenter status into the modules.
    ///
    /// It lives in its own assembly on purpose. Wiring needs to see every layer, and if the
    /// bootstrap sat in App then App would have to reference Modules, which is the exact cycle the
    /// asmdef layout exists to prevent.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class AppBootstrap : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private Text scaleValueText;
        [SerializeField] private Text computeBackendText;

        [Header("Debug HUD")]
        [SerializeField, Min(8)] private int hudFontSize = 110;

        [Header("Quality")]
        [SerializeField, Min(32)] private int settledIterations = 320;
        [SerializeField, Min(64)] private int maximumIterations = 2048;
        [SerializeField, Min(0.05f)] private float settleDelay = 0.18f;

        [Header("Precision")]
        [SerializeField] private double gpuMinimumScale = 2.5e-4d;
        [SerializeField] private double extendedPrecisionScale = 1e-12d;
        [SerializeField] private double minimumScale = 1e-24d;
        [SerializeField] private double maximumScale = 4d;
        [SerializeField, Range(0.2f, 2f)] private float pinchZoomSpeed = 1f;

        private readonly List<IAppModule> modules = new();

        private FractalSession session;
        private FractalPresenter presenter;
        private AppServices context;
        private FractalGestureInput gestureInput;
        private float lastInteractionTime;

        /// <summary>The one place anything outside may read or change what is on screen.</summary>
        public FractalSession Session
        {
            get
            {
                EnsureInitialized();
                return session;
            }
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void OnEnable()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            EnsureInitialized();
            if (presenter == null)
            {
                return;
            }

            var gesture = gestureInput != null ? gestureInput.Current : default;
            if (gesture.ResetRequested)
            {
                ResetView();
            }
            else if (gesture.Changed)
            {
                ApplyGesture(gesture);
                lastInteractionTime = Time.unscaledTime;
            }

            var interacting = gesture.IsInteracting || Time.unscaledTime - lastInteractionTime < settleDelay;
            presenter.Tick(interacting);

            for (var i = 0; i < modules.Count; i++)
            {
                modules[i].Tick();
            }
        }

        private void OnDestroy()
        {
            for (var i = modules.Count - 1; i >= 0; i--)
            {
                modules[i].Shutdown();
            }

            modules.Clear();
            presenter?.Dispose();
            presenter = null;
            session = null;
            context = null;
        }

        private void OnValidate()
        {
            settledIterations = Mathf.Max(32, settledIterations);
            maximumIterations = Mathf.Max(settledIterations, maximumIterations);
            gpuMinimumScale = Math.Max(1e-8d, gpuMinimumScale);
            extendedPrecisionScale = Math.Min(gpuMinimumScale, Math.Max(1e-20d, extendedPrecisionScale));
            minimumScale = Math.Max(1e-28d, minimumScale);
            maximumScale = Math.Max(gpuMinimumScale, maximumScale);

            session?.SetQuality(BuildQuality());
        }

        /// <summary>Public entry point for presets and bookmarks.</summary>
        public void SetView(decimal centerX, decimal centerY, decimal scale)
        {
            EnsureInitialized();
            lastInteractionTime = -100f;
            session.SetCenter(centerX, centerY, scale);
        }

        public void ResetView()
        {
            EnsureInitialized();
            lastInteractionTime = -100f;
            session.ResetView();
        }

        private void EnsureInitialized()
        {
            if (session != null && presenter != null)
            {
                return;
            }

            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;

            EnsureUi();

            gestureInput = GetComponent<FractalGestureInput>();
            if (gestureInput == null)
            {
                gestureInput = gameObject.AddComponent<FractalGestureInput>();
            }

            session ??= new FractalSession(BuildQuality());
            presenter ??= new FractalPresenter(targetImage, session);
            context ??= new AppServices(session, presenter, transform);

            if (modules.Count == 0)
            {
                // Adding a module is one line here plus its file. Order is the tick order.
                modules.Add(new HudModule(scaleValueText, computeBackendText, hudFontSize));

                for (var i = 0; i < modules.Count; i++)
                {
                    modules[i].Initialize(context);
                }
            }

            lastInteractionTime = -100f;
        }

        private RenderQuality BuildQuality()
        {
            return new RenderQuality
            {
                SettledIterations = settledIterations,
                MaximumIterations = maximumIterations,
                GpuMinimumScale = gpuMinimumScale,
                ExtendedPrecisionScale = extendedPrecisionScale,
                MinimumScale = minimumScale,
                MaximumScale = maximumScale
            };
        }

        private void ApplyGesture(in FractalGestureFrame gesture)
        {
            var view = session.View;
            var viewport = presenter.DisplayViewport;
            var quality = session.Quality;

            var pinchMoved = (gesture.CurrentCenter - gesture.PreviousCenter).sqrMagnitude > 0.01f;
            if (gesture.HasZoom || gesture.HasRotation || pinchMoved)
            {
                ViewNavigator.PinchZoomRotate(
                    ref view,
                    viewport,
                    gesture.PreviousCenter,
                    gesture.CurrentCenter,
                    gesture.ZoomRatio,
                    pinchZoomSpeed,
                    gesture.RotationDelta,
                    quality.MinimumScale,
                    quality.MaximumScale);
            }
            else if (gesture.PanDelta.sqrMagnitude > 0.01f)
            {
                ViewNavigator.Pan(ref view, viewport, gesture.PanDelta);
            }
            else
            {
                return;
            }

            session.SetView(view);
        }

        private void EnsureUi()
        {
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            if (targetImage == null)
            {
                var output = new GameObject("FractalOutput", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
                output.transform.SetParent(transform, false);
                targetImage = output.GetComponent<RawImage>();
            }

            Stretch(targetImage.rectTransform);
            targetImage.raycastTarget = false;
            targetImage.color = Color.white;
            targetImage.transform.SetAsFirstSibling();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
