using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Small application coordinator: gestures -> precise view -> one of two
    /// renderers, with no third-party input or UI dependency.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    public sealed class FractalSceneController : MonoBehaviour
    {
        private enum RenderBackend
        {
            GpuFloat,
            Cpu
        }

        [Header("Scene references")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private Text scaleValueText;
        [SerializeField] private Text computeBackendText;

        [Header("Quality")]
        [SerializeField, Min(16)] private int interactionIterations = 96;
        [SerializeField, Min(32)] private int settledIterations = 320;
        [SerializeField, Min(64)] private int maximumIterations = 2048;
        [SerializeField, Min(0.05f)] private float settleDelay = 0.18f;
        [SerializeField, Min(32)] private int overrideTileSize;

        [Header("Precision")]
        [SerializeField] private double gpuMinimumScale = 2.5e-4d;
        [SerializeField] private double extendedPrecisionScale = 1e-12d;
        [SerializeField] private double minimumScale = 1e-24d;
        [SerializeField] private double maximumScale = 4d;
        [SerializeField, Range(0.2f, 2f)] private float pinchZoomSpeed = 1f;

        private FractalView view;
        private FractalView lastRequestedView;
        private MobileRenderProfile profile;
        private FractalGestureInput gestureInput;
        private FractalGpuRenderer gpuRenderer;
        private FractalCpuRenderer cpuRenderer;
        private RenderBackend currentBackend;
        private bool hasRequestedView;
        private bool lastRequestWasInteractive;
        private bool renderDirty;
        private float lastInteractionTime;
        private float nextHudUpdateTime;
        private int cachedScreenWidth;
        private int cachedScreenHeight;

        private RenderTexture interactiveGpuTexture;
        private RenderTexture settledGpuTexture;
        private Texture2D interactiveCpuTexture;
        private Texture2D settledCpuTexture;

        public double CurrentScale => view.scale.AsDouble;
        public double CurrentCenterX => view.x.AsDouble;
        public double CurrentCenterY => view.y.AsDouble;
        public bool IsCpuRendering => cpuRenderer != null && cpuRenderer.IsBusy;
        public float CpuRenderProgress => cpuRenderer != null ? cpuRenderer.Progress : 0f;
        public string ActiveTextureName => targetImage != null && targetImage.texture != null
            ? targetImage.texture.name
            : string.Empty;

        private void Awake()
        {
            InitializeRuntime();
        }

        private void OnEnable()
        {
            if (gpuRenderer == null || cpuRenderer == null)
            {
                InitializeRuntime();
            }
        }

        private void InitializeRuntime()
        {
            if (gpuRenderer != null && cpuRenderer != null)
            {
                return;
            }

            Application.targetFrameRate = Application.isMobilePlatform ? 60 : -1;
            profile = MobileRenderProfile.Detect();
            EnsureUi();

            gestureInput = GetComponent<FractalGestureInput>();
            if (gestureInput == null)
            {
                gestureInput = gameObject.AddComponent<FractalGestureInput>();
            }

            var gradient = BuildDefaultGradient();
            gpuRenderer = new FractalGpuRenderer(gradient);
            cpuRenderer = new FractalCpuRenderer(gradient);
            view = FractalView.Default;
            lastInteractionTime = -100f;
            RecreateTextures();
            renderDirty = true;
        }

        private void Update()
        {
            if (gpuRenderer == null || cpuRenderer == null || targetImage == null)
            {
                InitializeRuntime();
                if (gpuRenderer == null || cpuRenderer == null || targetImage == null)
                {
                    return;
                }
            }

            if (Screen.width != cachedScreenWidth || Screen.height != cachedScreenHeight)
            {
                RecreateTextures();
                renderDirty = true;
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
                renderDirty = true;
            }

            var interacting = gesture.IsInteracting || Time.unscaledTime - lastInteractionTime < settleDelay;
            if (interacting != lastRequestWasInteractive)
            {
                renderDirty = true;
            }

            if (renderDirty || !hasRequestedView || ViewChanged())
            {
                RequestRender(interacting);
            }

            cpuRenderer?.Update();
            UpdateHud(interacting);
        }

        private void OnDestroy()
        {
            cpuRenderer?.Dispose();
            gpuRenderer?.Dispose();
            DestroyTextures();
        }

        private void OnValidate()
        {
            interactionIterations = Mathf.Max(16, interactionIterations);
            settledIterations = Mathf.Max(interactionIterations, settledIterations);
            maximumIterations = Mathf.Max(settledIterations, maximumIterations);
            gpuMinimumScale = Math.Max(1e-8d, gpuMinimumScale);
            extendedPrecisionScale = Math.Min(gpuMinimumScale, Math.Max(1e-20d, extendedPrecisionScale));
            minimumScale = Math.Max(1e-28d, minimumScale);
            maximumScale = Math.Max(gpuMinimumScale, maximumScale);
        }

        /// <summary>Public entry point for future bookmarks/presets in the fractal manager.</summary>
        public void SetView(decimal centerX, decimal centerY, decimal scale)
        {
            var clampedScale = Math.Clamp((double)scale, minimumScale, maximumScale);
            view.x = new HighPrecision(centerX);
            view.y = new HighPrecision(centerY);
            view.scale = HighPrecision.FromDouble(clampedScale);
            renderDirty = true;
            RenderExternalViewNow();
        }

        public void ResetView()
        {
            view = FractalView.Default;
            renderDirty = true;
            RenderExternalViewNow();
        }

        private void RenderExternalViewNow()
        {
            // Awake has not necessarily run when a preset is assigned by another
            // component's Awake, so preserve the dirty flag in that case.
            if (gpuRenderer == null || cpuRenderer == null || targetImage == null)
            {
                return;
            }

            lastInteractionTime = -100f;
            RequestRender(false);
        }

        private void ApplyGesture(in FractalGestureFrame gesture)
        {
            var pinchMoved = (gesture.CurrentCenter - gesture.PreviousCenter).sqrMagnitude > 0.01f;
            if (gesture.HasZoom || pinchMoved)
            {
                ApplyPinch(gesture.PreviousCenter, gesture.CurrentCenter, gesture.ZoomRatio);
                return;
            }

            if (gesture.PanDelta.sqrMagnitude > 0.01f)
            {
                PanByPixels(gesture.PanDelta);
            }
        }

        private void PanByPixels(Vector2 delta)
        {
            var width = Math.Max(1, Screen.width);
            var height = Math.Max(1, Screen.height);
            var scale = view.scale.AsDecimal;
            var aspect = (decimal)width / height;
            var dx = -(decimal)delta.x / width * scale * aspect;
            var dy = -(decimal)delta.y / height * scale;
            view.x = new HighPrecision(view.x.AsDecimal + dx);
            view.y = new HighPrecision(view.y.AsDecimal + dy);
        }

        private void ApplyPinch(Vector2 previousCenter, Vector2 currentCenter, float rawZoomRatio)
        {
            var safeRatio = Mathf.Max(0.01f, rawZoomRatio);
            var zoomRatio = Math.Pow(safeRatio, pinchZoomSpeed);
            var oldPoint = ScreenToFractal(previousCenter, view);
            var newScaleDouble = Math.Clamp(view.scale.AsDouble / zoomRatio, minimumScale, maximumScale);
            view.scale = HighPrecision.FromDouble(newScaleDouble);
            var newPoint = ScreenToFractal(currentCenter, view);
            view.x = new HighPrecision(view.x.AsDecimal + oldPoint.x - newPoint.x);
            view.y = new HighPrecision(view.y.AsDecimal + oldPoint.y - newPoint.y);
        }

        private static (decimal x, decimal y) ScreenToFractal(Vector2 point, in FractalView sourceView)
        {
            var width = Math.Max(1, Screen.width);
            var height = Math.Max(1, Screen.height);
            var aspect = (decimal)width / height;
            var normalizedX = (decimal)point.x / width - 0.5m;
            var normalizedY = (decimal)point.y / height - 0.5m;
            var scale = sourceView.scale.AsDecimal;
            return (
                sourceView.x.AsDecimal + normalizedX * scale * aspect,
                sourceView.y.AsDecimal + normalizedY * scale);
        }

        private bool ViewChanged()
        {
            return !lastRequestedView.x.Equals(view.x) ||
                   !lastRequestedView.y.Equals(view.y) ||
                   !lastRequestedView.scale.Equals(view.scale);
        }

        private void RequestRender(bool interacting)
        {
            var scale = view.scale.AsDouble;
            var iterations = ResolveIterations(scale, interacting);
            view.iterations = iterations;
            currentBackend = gpuRenderer != null && gpuRenderer.IsAvailable && scale >= gpuMinimumScale
                ? RenderBackend.GpuFloat
                : RenderBackend.Cpu;

            if (currentBackend == RenderBackend.GpuFloat)
            {
                cpuRenderer?.Invalidate();
                var target = interacting ? interactiveGpuTexture : settledGpuTexture;
                gpuRenderer.Render(view, iterations, target);
                targetImage.texture = target;
            }
            else
            {
                var target = interacting ? interactiveCpuTexture : settledCpuTexture;
                var useExtendedPrecision = scale < extendedPrecisionScale;
                var tileSize = overrideTileSize > 0 ? overrideTileSize : profile.TileSize;
                cpuRenderer.Request(target, view, iterations, tileSize, useExtendedPrecision);
                targetImage.texture = target;
            }

            targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            lastRequestedView = view;
            lastRequestWasInteractive = interacting;
            hasRequestedView = true;
            renderDirty = false;
        }

        private int ResolveIterations(double scale, bool interacting)
        {
            var baseIterations = interacting ? interactionIterations : settledIterations;
            var depth = Math.Max(0d, -Math.Log10(Math.Max(scale, 1e-28d)) - 3d);
            var depthBudget = interacting
                ? depth * 10d
                : depth * 96d + Math.Max(0d, depth - 6d) * 192d;
            return Mathf.Clamp(baseIterations + Mathf.RoundToInt((float)depthBudget), 16, maximumIterations);
        }

        private void UpdateHud(bool interacting)
        {
            if (Time.unscaledTime < nextHudUpdateTime)
            {
                return;
            }

            nextHudUpdateTime = Time.unscaledTime + 0.1f;
            if (scaleValueText != null)
            {
                scaleValueText.text = "Масштаб  " + view.scale.AsDouble.ToString("0.00e+0", CultureInfo.InvariantCulture);
            }

            if (computeBackendText == null)
            {
                return;
            }

            if (currentBackend == RenderBackend.GpuFloat)
            {
                computeBackendText.text = "GPU · fp32" + (interacting ? " · интерактивно" : string.Empty);
                return;
            }

            var precision = view.scale.AsDouble < extendedPrecisionScale ? "double-double" : "fp64";
            var progress = cpuRenderer != null && cpuRenderer.IsBusy
                ? " · " + Mathf.RoundToInt(cpuRenderer.Progress * 100f) + "%"
                : string.Empty;
            computeBackendText.text = "CPU Parallel · " + precision + progress;
        }

        private void RecreateTextures()
        {
            if (cpuRenderer != null)
            {
                cpuRenderer.CompletePendingWork();
            }

            DestroyTextures();
            cachedScreenWidth = Mathf.Max(64, Screen.width);
            cachedScreenHeight = Mathf.Max(64, Screen.height);
            var interactiveSize = profile.ResolveSize(cachedScreenWidth, cachedScreenHeight, true);
            var settledSize = profile.ResolveSize(cachedScreenWidth, cachedScreenHeight, false);

            interactiveGpuTexture = CreateRenderTexture(interactiveSize, "Fractal GPU Interactive");
            settledGpuTexture = CreateRenderTexture(settledSize, "Fractal GPU Settled");
            interactiveCpuTexture = CreateCpuTexture(interactiveSize, "Fractal CPU Interactive");
            settledCpuTexture = CreateCpuTexture(settledSize, "Fractal CPU Settled");
            hasRequestedView = false;
        }

        private void DestroyTextures()
        {
            ReleaseRenderTexture(ref interactiveGpuTexture);
            ReleaseRenderTexture(ref settledGpuTexture);
            DestroyTexture(ref interactiveCpuTexture);
            DestroyTexture(ref settledCpuTexture);
        }

        private static RenderTexture CreateRenderTexture(Vector2Int size, string textureName)
        {
            var texture = new RenderTexture(size.x, size.y, 0, RenderTextureFormat.ARGB32)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            texture.Create();
            return texture;
        }

        private static Texture2D CreateCpuTexture(Vector2Int size, string textureName)
        {
            var texture = new Texture2D(size.x, size.y, TextureFormat.RGBA32, false, false)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            return texture;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            Destroy(texture);
            texture = null;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            Destroy(texture);
            texture = null;
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

            if (scaleValueText == null)
            {
                scaleValueText = CreateHudText("Scale", new Vector2(-24f, -24f));
            }

            if (computeBackendText == null)
            {
                computeBackendText = CreateHudText("Backend", new Vector2(-24f, -62f));
            }

            ConfigureHudText(scaleValueText, new Vector2(-24f, -24f));
            ConfigureHudText(computeBackendText, new Vector2(-24f, -62f));
        }

        private Text CreateHudText(string objectName, Vector2 anchoredPosition)
        {
            var child = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            child.transform.SetParent(transform, false);
            var text = child.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ConfigureHudText(text, anchoredPosition);
            return text;
        }

        private static void ConfigureHudText(Text text, Vector2 anchoredPosition)
        {
            if (text == null)
            {
                return;
            }

            var rect = text.rectTransform;
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = Vector2.one;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(460f, 34f);
            text.alignment = TextAnchor.MiddleRight;
            text.fontSize = 24;
            text.color = Color.white;
            text.raycastTarget = false;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static Gradient BuildDefaultGradient()
        {
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.015f, 0.025f, 0.12f), 0f),
                    new GradientColorKey(new Color(0.04f, 0.42f, 0.95f), 0.25f),
                    new GradientColorKey(new Color(0.15f, 0.95f, 0.85f), 0.5f),
                    new GradientColorKey(new Color(1f, 0.78f, 0.12f), 0.75f),
                    new GradientColorKey(new Color(0.9f, 0.08f, 0.24f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            return gradient;
        }
    }
}
