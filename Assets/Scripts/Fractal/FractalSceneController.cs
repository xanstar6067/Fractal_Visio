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
        private Texture2D cpuTexture;

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
            settledIterations = Mathf.Max(32, settledIterations);
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
            if (gesture.HasZoom || gesture.HasRotation || pinchMoved)
            {
                ApplyTwoFinger(gesture.PreviousCenter, gesture.CurrentCenter, gesture.ZoomRatio, gesture.RotationDelta);
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
            var aspect = (double)width / height;

            // Drag direction is screen-space; turn it into view-space by the rotation.
            var ndx = -(double)delta.x / width * aspect;
            var ndy = -(double)delta.y / height;
            var cos = Math.Cos(view.rotation);
            var sin = Math.Sin(view.rotation);
            var rx = ndx * cos - ndy * sin;
            var ry = ndx * sin + ndy * cos;

            var scale = view.scale.AsDecimal;
            view.x = new HighPrecision(view.x.AsDecimal + (decimal)rx * scale);
            view.y = new HighPrecision(view.y.AsDecimal + (decimal)ry * scale);
        }

        private void ApplyTwoFinger(Vector2 previousPivot, Vector2 currentPivot, float rawZoomRatio, float rotationDelta)
        {
            var safeRatio = Mathf.Max(0.01f, rawZoomRatio);
            var zoomRatio = Math.Pow(safeRatio, pinchZoomSpeed);

            // Fractal point under the pivot before the transform.
            var anchor = ScreenToFractal(previousPivot, view);

            var newScaleDouble = Math.Clamp(view.scale.AsDouble / zoomRatio, minimumScale, maximumScale);
            view.scale = HighPrecision.FromDouble(newScaleDouble);
            view.rotation += rotationDelta;

            // Slide the centre so that same fractal point sits under the pivot again;
            // with a rotation-aware mapping this also turns the view about the pivot.
            var moved = ScreenToFractal(currentPivot, view);
            view.x = new HighPrecision(view.x.AsDecimal + anchor.x - moved.x);
            view.y = new HighPrecision(view.y.AsDecimal + anchor.y - moved.y);
        }

        private static (decimal x, decimal y) ScreenToFractal(Vector2 point, in FractalView sourceView)
        {
            var width = Math.Max(1, Screen.width);
            var height = Math.Max(1, Screen.height);
            var aspect = (double)width / height;

            var nx = ((double)point.x / width - 0.5d) * aspect;
            var ny = (double)point.y / height - 0.5d;
            var cos = Math.Cos(sourceView.rotation);
            var sin = Math.Sin(sourceView.rotation);
            var rx = nx * cos - ny * sin;
            var ry = nx * sin + ny * cos;

            var scale = sourceView.scale.AsDecimal;
            return (
                sourceView.x.AsDecimal + (decimal)rx * scale,
                sourceView.y.AsDecimal + (decimal)ry * scale);
        }

        private bool ViewChanged()
        {
            return !lastRequestedView.x.Equals(view.x) ||
                   !lastRequestedView.y.Equals(view.y) ||
                   !lastRequestedView.scale.Equals(view.scale) ||
                   lastRequestedView.rotation != view.rotation;
        }

        private void RequestRender(bool interacting)
        {
            var scale = view.scale.AsDouble;
            currentBackend = gpuRenderer != null && gpuRenderer.IsAvailable && scale >= gpuMinimumScale
                ? RenderBackend.GpuFloat
                : RenderBackend.Cpu;

            var iterations = ResolveIterations(scale);
            view.iterations = iterations;

            if (currentBackend == RenderBackend.GpuFloat)
            {
                cpuRenderer?.Invalidate();
                var target = interacting ? interactiveGpuTexture : settledGpuTexture;
                gpuRenderer.Render(view, iterations, target);
                targetImage.texture = target;
            }
            else
            {
                var useExtendedPrecision = scale < extendedPrecisionScale;
                cpuRenderer.Request(cpuTexture, view, iterations, useExtendedPrecision, interacting);
                targetImage.texture = cpuTexture;
            }

            targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            lastRequestedView = view;
            lastRequestWasInteractive = interacting;
            hasRequestedView = true;
            renderDirty = false;
        }

        // One budget for every state. The iteration count never drops during a
        // gesture: responsiveness comes from coarse render passes, not fewer
        // iterations (a reduced budget was visibly changing the image).
        private int ResolveIterations(double scale)
        {
            var depth = Math.Max(0d, -Math.Log10(Math.Max(scale, 1e-28d)) - 3d);
            var depthBudget = depth * 96d + Math.Max(0d, depth - 6d) * 192d;
            return Mathf.Clamp(settledIterations + Mathf.RoundToInt((float)depthBudget), 16, maximumIterations);
        }

        private void UpdateHud(bool interacting)
        {
            if (Time.unscaledTime < nextHudUpdateTime)
            {
                return;
            }

            nextHudUpdateTime = Time.unscaledTime + 0.1f;

            var scale = view.scale.AsDouble;
            var reference = FractalView.Default.scale.AsDouble;
            var zoom = scale > 0d ? reference / scale : 0d;

            if (scaleValueText != null)
            {
                var rotationDegrees = view.rotation * (180d / Math.PI);
                rotationDegrees -= Math.Floor(rotationDegrees / 360d) * 360d;

                // Full-precision centre so a view can be reproduced on the PC via SetView().
                scaleValueText.text = string.Concat(
                    "scale  ", scale.ToString("0.000000e+00", CultureInfo.InvariantCulture), "\n",
                    "zoom   x", zoom.ToString("0.###e+00", CultureInfo.InvariantCulture), "\n",
                    "rot    ", rotationDegrees.ToString("0.0", CultureInfo.InvariantCulture), " deg\n",
                    "X  ", view.x.AsDecimal.ToString("G29", CultureInfo.InvariantCulture), "\n",
                    "Y  ", view.y.AsDecimal.ToString("G29", CultureInfo.InvariantCulture));
            }

            if (computeBackendText == null)
            {
                return;
            }

            string engineLine;
            string detailLine;
            if (currentBackend == RenderBackend.GpuFloat)
            {
                engineLine = "GPU fp32" + (interacting ? "  interactive" : string.Empty);
                detailLine = "iter " + view.iterations;
            }
            else
            {
                var precision = scale < extendedPrecisionScale ? "double-double" : "fp64";
                engineLine = "CPU Parallel  " + precision + (interacting ? "  interactive" : string.Empty);
                if (cpuRenderer != null && cpuRenderer.IsBusy)
                {
                    detailLine = string.Concat(
                        "iter ", view.iterations.ToString(CultureInfo.InvariantCulture),
                        "  pass ", cpuRenderer.CurrentPass.ToString(CultureInfo.InvariantCulture),
                        "/", cpuRenderer.PassCount.ToString(CultureInfo.InvariantCulture),
                        "  ", Mathf.RoundToInt(cpuRenderer.Progress * 100f).ToString(CultureInfo.InvariantCulture), "%");
                }
                else
                {
                    detailLine = "iter " + view.iterations + "  done";
                }
            }

            computeBackendText.text = engineLine + "\n" + detailLine;
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
            cpuTexture = CreateCpuTexture(settledSize, "Fractal CPU");
            hasRequestedView = false;
        }

        private void DestroyTextures()
        {
            ReleaseRenderTexture(ref interactiveGpuTexture);
            ReleaseRenderTexture(ref settledGpuTexture);
            DestroyTexture(ref cpuTexture);
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
                scaleValueText = CreateHudText("Scale", new Vector2(24f, -24f));
            }

            if (computeBackendText == null)
            {
                computeBackendText = CreateHudText("Backend", new Vector2(24f, -24f - 5.6f * hudFontSize));
            }

            ConfigureHudText(scaleValueText, new Vector2(24f, -24f));
            ConfigureHudText(computeBackendText, new Vector2(24f, -24f - 5.6f * hudFontSize));
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

        private void ConfigureHudText(Text text, Vector2 anchoredPosition)
        {
            if (text == null)
            {
                return;
            }

            // The scene's Text objects point at the old built-in Arial (removed in
            // Unity 6), so they render nothing. Force a font that ships at runtime.
            if (text.font == null || text.font.name != "LegacyRuntime")
            {
                var runtimeFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (runtimeFont != null)
                {
                    text.font = runtimeFont;
                }
            }

            var rect = text.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = new Vector2(3600f, 8f * hudFontSize);
            text.alignment = TextAnchor.UpperLeft;
            text.fontSize = Mathf.Max(8, hudFontSize);
            text.fontStyle = FontStyle.Bold;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.color = Color.white;
            text.raycastTarget = false;

            // High-contrast edge so the readout stays legible over any fractal colour.
            var outline = text.GetComponent<Outline>();
            if (outline == null)
            {
                outline = text.gameObject.AddComponent<Outline>();
            }

            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(4f, -4f);
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
