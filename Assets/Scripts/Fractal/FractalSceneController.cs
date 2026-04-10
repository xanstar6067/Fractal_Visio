using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using Lean.Touch;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Scene-level orchestrator for Fractal_Manager.unity.
    /// Handles only input + pipeline orchestration and does not depend on scene wiring details.
    /// </summary>
    public sealed class FractalSceneController : MonoBehaviour
    {
        [Header("Output")]
        [SerializeField] private RawImage targetImage;
        [SerializeField] private Text scaleValueText;
        [SerializeField] private int baseWidth = 1080;
        [SerializeField] private int baseHeight = 1920;
        [SerializeField] private int minTextureSize = 64;

        [Header("Quality")]
        [SerializeField] private int tileSize = 96;
        [SerializeField] private int interactIterations = 96;
        [SerializeField] private int settleIterations = 256;
        [SerializeField] [Range(0.1f, 1f)] private float interactRenderScale = 0.5f;
        [SerializeField] [Range(0.25f, 1f)] private float settleRenderScale = 1f;
        [SerializeField] private float settleDelay = 0.15f;
        [SerializeField] private int cpuApplyTileBatch = 4;
        [SerializeField] private float fallbackFrameBudgetMs = 5f;
        [SerializeField] private int maxFallbackTilesPerFrame = 8;

        [Header("Mobile Adaptive Quality")]
        [SerializeField] private float mobileTargetFrameTimeMs = 16.6f;
        [SerializeField] [Range(0.25f, 1f)] private float mobileMinInteractScale = 0.4f;
        [SerializeField] [Range(0.25f, 1f)] private float mobileMaxInteractScale = 0.65f;
        [SerializeField] private float mobileScaleAdjustSpeed = 0.35f;

        [Header("Zoom")]
        [SerializeField] private float pinchZoomSpeed = 1f;
        [SerializeField] private float minScale = 1e-20f;
        [SerializeField] private float maxScale = 4f;

        private readonly Dictionary<RenderMode, IFractalRenderer> renderers = new();

        private FractalPrecisionManager precisionManager;
        private Texture2D cpuRenderTexture;
        private Texture2D cpuPreviewTexture;
        private Texture2D transitionPreviewTexture;
        private RenderTexture gpuRenderTexture;
        private RenderTexture gpuPreviewTexture;
        private FractalView view;
        private bool lastFrameGpu = true;

        private int generationId;
        private int currentTextureWidth;
        private int currentTextureHeight;
        private float currentRenderScale = -1f;
        private float lastInteractionTime;
        private bool isInteracting;

        private Vector2 previousPinchCenter;
        private float previousPinchDistance;
        private Vector2 previousSingleFingerPosition;
        private bool hasPreviousSingleFingerPosition;
        private int previousFingerMode;
        private double averageTileMs = 0.4d;
        private double averageApplyMs = 0.3d;
        private float smoothedFrameTimeMs = 16.6f;
        private float adaptiveInteractRenderScale;

        private void Awake()
        {
            EnsureTargetImage();
            precisionManager = new FractalPrecisionManager();
            view = FractalView.Default;
            view.iterations = settleIterations;
            UpdateScaleText();
            BuildDefaultGradient(out var gradient);
            adaptiveInteractRenderScale = interactRenderScale;

            renderers[RenderMode.Fast] = new FastFractalRenderer(gradient);
            renderers[RenderMode.Perturbation] = new PerturbationFractalRenderer(gradient, enableCpuFallback: false);
            renderers[RenderMode.PerturbationWithFallback] = new PerturbationFractalRenderer(gradient, enableCpuFallback: true);
        }

        private void Start()
        {
            EnsureTargetImage();
            LogShaderDiagnostics();
            RecreateTexturesIfNeeded(force: true, ResolveDesiredRenderScale());
            RequestRender();
        }

        private static void LogShaderDiagnostics()
        {
            Debug.Log($"[FractalSceneController] Active Graphics API: {SystemInfo.graphicsDeviceType}");

            LogShaderStatus("FractalVisio/MandelbrotFloat");
            LogShaderStatus("FractalVisio/MandelbrotPerturbation");
        }

        private static void LogShaderStatus(string shaderName)
        {
            var shader = Shader.Find(shaderName);
            Debug.Log($"[FractalSceneController] Shader.Find('{shaderName}') => {(shader != null ? "FOUND" : "MISSING")}, isSupported: {shader?.isSupported}");
        }

        private void EnsureTargetImage()
        {
            if (targetImage != null)
            {
                return;
            }

            targetImage = GetComponent<RawImage>();
            if (targetImage != null)
            {
                return;
            }

            targetImage = GetComponentInChildren<RawImage>(true);
            if (targetImage != null)
            {
                return;
            }

            if (TryGetComponent<Graphic>(out _))
            {
                var child = new GameObject("FractalOutput", typeof(RectTransform), typeof(RawImage));
                child.transform.SetParent(transform, false);
                targetImage = child.GetComponent<RawImage>();
                return;
            }

            targetImage = gameObject.AddComponent<RawImage>();
        }

        private void Update()
        {
            TrackFrameTiming();
            if (RecreateTexturesIfNeeded(force: false, ResolveDesiredRenderScale()))
            {
                RequestRender();
            }

            isInteracting = HandleTouchInput();
            UpdateScaleText();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (RecreateTexturesIfNeeded(force: false, ResolveDesiredRenderScale()))
            {
                RequestRender();
            }
        }

        private void OnDestroy()
        {
            ReleaseTextures();
            if (transitionPreviewTexture != null)
            {
                Destroy(transitionPreviewTexture);
                transitionPreviewTexture = null;
            }
        }

        private bool HandleTouchInput()
        {
            var fingers = LeanTouch.GetFingers(true, true);
            var fingerCount = fingers.Count;
            var fingerMode = fingerCount >= 2 ? 2 : fingerCount == 1 ? 1 : 0;

            if (fingerMode != previousFingerMode)
            {
                ResetTouchTrackingState();
                previousFingerMode = fingerMode;
            }

            if (fingerMode == 1)
            {
                var finger = fingers[0];
                var currentPosition = finger.ScreenPosition;
                var hasViewChanged = false;
                if (hasPreviousSingleFingerPosition)
                {
                    ApplySingleFingerPan(previousSingleFingerPosition, currentPosition);
                    hasViewChanged = (currentPosition - previousSingleFingerPosition).sqrMagnitude > 0f;
                }

                previousSingleFingerPosition = currentPosition;
                hasPreviousSingleFingerPosition = true;

                if (hasViewChanged)
                {
                    lastInteractionTime = Time.unscaledTime;
                    RequestRender();
                }
                return true;
            }

            if (fingerMode == 2)
            {
                var center = LeanGesture.GetScreenCenter(fingers);
                var distance = LeanGesture.GetScreenDistance(fingers);

                if (previousPinchDistance > 0.001f)
                {
                    var zoomFactor = Mathf.Pow(distance / previousPinchDistance, pinchZoomSpeed);
                    ApplyPinchZoom(center, zoomFactor);
                    ApplyPanFromPinchCenter(previousPinchCenter, center);
                    var hasZoomChanged = Mathf.Abs(zoomFactor - 1f) > 0.0001f;
                    var hasPanChanged = (center - previousPinchCenter).sqrMagnitude > 0f;
                    if (hasZoomChanged || hasPanChanged)
                    {
                        lastInteractionTime = Time.unscaledTime;
                        RequestRender();
                    }
                }

                previousPinchCenter = center;
                previousPinchDistance = distance;
                return true;
            }

            previousFingerMode = 0;
            return false;
        }


        private void ResetTouchTrackingState()
        {
            previousPinchCenter = Vector2.zero;
            previousPinchDistance = 0f;
            previousSingleFingerPosition = Vector2.zero;
            hasPreviousSingleFingerPosition = false;
        }

        private void ApplySingleFingerPan(Vector2 oldPosition, Vector2 newPosition)
        {
            var oldWorld = ScreenToFractal(oldPosition, view);
            var newWorld = ScreenToFractal(newPosition, view);

            view.x += HighPrecision.FromDouble(oldWorld.x - newWorld.x);
            view.y += HighPrecision.FromDouble(oldWorld.y - newWorld.y);
            UpdateScaleText();
        }
        private void ApplyPanFromPinchCenter(Vector2 oldCenter, Vector2 newCenter)
        {
            if (oldCenter == Vector2.zero)
            {
                return;
            }

            var oldWorld = ScreenToFractal(oldCenter, view);
            var newWorld = ScreenToFractal(newCenter, view);
            var dx = oldWorld.x - newWorld.x;
            var dy = oldWorld.y - newWorld.y;

            view.x += HighPrecision.FromDouble(dx);
            view.y += HighPrecision.FromDouble(dy);
        }

        private void ApplyPinchZoom(Vector2 screenCenter, float zoomFactor)
        {
            zoomFactor = Mathf.Clamp(zoomFactor, 0.5f, 2f);
            var oldView = view;
            var oldPoint = ScreenToFractal(screenCenter, oldView);

            var newScale = Mathf.Clamp((float)oldView.scale.AsDouble / zoomFactor, minScale, maxScale);
            view.scale = HighPrecision.FromDouble(newScale);

            var newPoint = ScreenToFractal(screenCenter, view);
            view.x += HighPrecision.FromDouble(oldPoint.x - newPoint.x);
            view.y += HighPrecision.FromDouble(oldPoint.y - newPoint.y);
            UpdateScaleText();
        }

        private void UpdateScaleText()
        {
            if (scaleValueText == null)
            {
                return;
            }

            var scaleAsText = view.scale.AsDouble.ToString("0.0e+0", CultureInfo.InvariantCulture).Replace('.', ',');
            scaleValueText.text = scaleAsText;
        }

        private (double x, double y) ScreenToFractal(Vector2 screenPoint, in FractalView srcView)
        {
            var hasTargetRect = TryGetNormalizedPointInTarget(screenPoint, out var nx, out var ny, out var width, out var height);
            if (!hasTargetRect)
            {
                width = Screen.width;
                height = Screen.height;

                if (width <= 0f || height <= 0f)
                {
                    return (srcView.x.AsDouble, srcView.y.AsDouble);
                }

                nx = screenPoint.x / width;
                ny = screenPoint.y / height;
            }

            var aspect = width / height;
            var halfScale = srcView.scale.AsDouble * 0.5d;

            var x = srcView.x.AsDouble + ((nx - 0.5d) * 2d * halfScale * aspect);
            var y = srcView.y.AsDouble + ((ny - 0.5d) * 2d * halfScale);
            return (x, y);
        }

        private bool TryGetNormalizedPointInTarget(Vector2 screenPoint, out double nx, out double ny, out double width, out double height)
        {
            nx = 0.5d;
            ny = 0.5d;
            width = 0d;
            height = 0d;

            if (targetImage == null)
            {
                return false;
            }

            var rectTransform = targetImage.rectTransform;
            var rect = rectTransform.rect;
            width = rect.width;
            height = rect.height;

            if (width <= 0d || height <= 0d)
            {
                return false;
            }

            var canvas = targetImage.canvas;
            var screenCamera = canvas != null && canvas.renderMode != UnityEngine.RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, screenCamera, out var localPoint))
            {
                return false;
            }

            nx = Mathf.Clamp01((localPoint.x - rect.xMin) / rect.width);
            ny = Mathf.Clamp01((localPoint.y - rect.yMin) / rect.height);
            return true;
        }

        private void RequestRender()
        {
            generationId++;
            CachePreview();
            StopAllCoroutines();
            StartCoroutine(RenderRoutine(generationId));
        }

        private IEnumerator RenderRoutine(int requestGeneration)
        {
            var mode = precisionManager.GetMode(view);
            var adjustedView = view;
            adjustedView.iterations = precisionManager.ResolveIterations(view, isInteracting);
            var request = new FractalRenderRequest(adjustedView, requestGeneration, isInteracting);
            var renderer = renderers[mode];

            if (mode == RenderMode.Fast && gpuRenderTexture != null)
            {
                renderer.Render(request, gpuRenderTexture, new TileDescriptor(new RectInt(0, 0, gpuRenderTexture.width, gpuRenderTexture.height), 0));
                lastFrameGpu = true;
                PushTexture();
                if (targetImage != null)
                {
                    targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                }

                yield break;
            }

            if ((mode == RenderMode.Perturbation || mode == RenderMode.PerturbationWithFallback) &&
                gpuRenderTexture != null && renderer is PerturbationFractalRenderer perturbationRenderer)
            {
                if (perturbationRenderer.RenderToGpu(request, gpuRenderTexture))
                {
                    if (mode == RenderMode.PerturbationWithFallback && cpuRenderTexture != null)
                    {
                        CopyRenderTextureToCpu(gpuRenderTexture, cpuRenderTexture);
                        var fallbackTileList = perturbationRenderer.BuildFallbackTiles(request, cpuRenderTexture.width, cpuRenderTexture.height, tileSize);
                        var tilesPerFrame = ResolveFallbackTilesPerFrame();
                        var applyBatch = ResolveApplyBatchSize();
                        var renderedSinceApply = 0;

                        foreach (var fallbackTile in fallbackTileList)
                        {
                            if (requestGeneration != generationId)
                            {
                                yield break;
                            }

                            var tileStartMs = Time.realtimeSinceStartupAsDouble * 1000d;
                            perturbationRenderer.Render(request, cpuRenderTexture, fallbackTile);
                            TrackTileMetric((Time.realtimeSinceStartupAsDouble * 1000d) - tileStartMs);
                            renderedSinceApply++;

                            if (renderedSinceApply >= applyBatch)
                            {
                                ApplyCpuTexture();
                                renderedSinceApply = 0;
                            }

                            if (tilesPerFrame > 0 && fallbackTile.TileIndex % tilesPerFrame == tilesPerFrame - 1)
                            {
                                if (renderedSinceApply > 0)
                                {
                                    ApplyCpuTexture();
                                    renderedSinceApply = 0;
                                }

                                yield return null;
                            }
                        }

                        if (renderedSinceApply > 0)
                        {
                            ApplyCpuTexture();
                        }

                        lastFrameGpu = false;
                    }
                    else
                    {
                        lastFrameGpu = true;
                    }

                    PushTexture();
                    if (targetImage != null)
                    {
                        targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
                    }

                    yield break;
                }
            }

            var cpuTilesPerFrame = Mathf.Max(1, ResolveFallbackTilesPerFrame());
            var cpuApplyBatch = ResolveApplyBatchSize();
            var frameTileCount = 0;
            var pendingApplyTiles = 0;
            foreach (var tile in TilePlanner.BuildTiles(cpuRenderTexture.width, cpuRenderTexture.height, tileSize))
            {
                if (requestGeneration != generationId)
                {
                    yield break;
                }

                var tileStartMs = Time.realtimeSinceStartupAsDouble * 1000d;
                renderer.Render(request, cpuRenderTexture, tile);
                TrackTileMetric((Time.realtimeSinceStartupAsDouble * 1000d) - tileStartMs);
                pendingApplyTiles++;
                frameTileCount++;

                if (pendingApplyTiles >= cpuApplyBatch)
                {
                    ApplyCpuTexture();
                    pendingApplyTiles = 0;
                }

                if (frameTileCount >= cpuTilesPerFrame)
                {
                    if (pendingApplyTiles > 0)
                    {
                        ApplyCpuTexture();
                        pendingApplyTiles = 0;
                    }

                    frameTileCount = 0;
                    yield return null;
                }
            }

            if (pendingApplyTiles > 0)
            {
                ApplyCpuTexture();
            }

            lastFrameGpu = false;
            PushTexture();
            if (targetImage != null)
            {
                targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private bool RecreateTexturesIfNeeded(bool force, float renderScale)
        {
            var targetSize = ComputeTargetTextureSize(renderScale);
            if (!force && cpuRenderTexture != null && cpuPreviewTexture != null && gpuRenderTexture != null && gpuPreviewTexture != null &&
                currentTextureWidth == targetSize.width && currentTextureHeight == targetSize.height &&
                Mathf.Abs(currentRenderScale - renderScale) < 0.001f)
            {
                return false;
            }

            CaptureTransitionPreview();
            ReleaseTextures();
            EnsureTextures(targetSize.width, targetSize.height);
            currentTextureWidth = targetSize.width;
            currentTextureHeight = targetSize.height;
            currentRenderScale = renderScale;

            PushTexture();
            return true;
        }

        private (int width, int height) ComputeTargetTextureSize(float renderScale)
        {
            var width = Mathf.Max(minTextureSize, baseWidth);
            var height = Mathf.Max(minTextureSize, baseHeight);

            if (targetImage != null)
            {
                var rect = targetImage.rectTransform.rect;
                if (rect.width > 0f && rect.height > 0f)
                {
                    width = Mathf.Max(minTextureSize, Mathf.RoundToInt(rect.width * renderScale));
                    height = Mathf.Max(minTextureSize, Mathf.RoundToInt(rect.height * renderScale));
                    return (width, height);
                }
            }

            width = Mathf.Max(minTextureSize, Mathf.RoundToInt((Screen.width > 0 ? Screen.width : width) * renderScale));
            height = Mathf.Max(minTextureSize, Mathf.RoundToInt((Screen.height > 0 ? Screen.height : height) * renderScale));
            return (width, height);
        }

        private void CachePreview()
        {
            if (targetImage == null)
            {
                return;
            }

            if (lastFrameGpu && gpuPreviewTexture != null && gpuRenderTexture != null)
            {
                Graphics.Blit(gpuRenderTexture, gpuPreviewTexture);
                targetImage.texture = gpuPreviewTexture;
                targetImage.uvRect = new Rect(-0.02f, -0.02f, 1.04f, 1.04f);
                return;
            }

            if (cpuPreviewTexture == null || cpuRenderTexture == null)
            {
                return;
            }

            cpuPreviewTexture.SetPixels32(cpuRenderTexture.GetPixels32());
            cpuPreviewTexture.Apply(false, false);
            targetImage.texture = cpuPreviewTexture;
            targetImage.uvRect = new Rect(-0.02f, -0.02f, 1.04f, 1.04f);
        }


        private static void CopyRenderTextureToCpu(RenderTexture source, Texture2D destination)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = source;
            destination.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0, false);
            destination.Apply(false, false);
            RenderTexture.active = previous;
        }

        private int ResolveFallbackTilesPerFrame()
        {
            var estimatedTileMs = Mathf.Max(0.05f, (float)averageTileMs);
            var budgetTiles = Mathf.FloorToInt(fallbackFrameBudgetMs / estimatedTileMs);
            return Mathf.Clamp(budgetTiles, 1, maxFallbackTilesPerFrame);
        }

        private int ResolveApplyBatchSize()
        {
            if (averageApplyMs > 1.5d)
            {
                return Mathf.Max(cpuApplyTileBatch, 8);
            }

            if (averageApplyMs > 0.75d)
            {
                return Mathf.Max(cpuApplyTileBatch, 6);
            }

            return Mathf.Max(1, cpuApplyTileBatch);
        }

        private void ApplyCpuTexture()
        {
            var applyStartMs = Time.realtimeSinceStartupAsDouble * 1000d;
            cpuRenderTexture.Apply(false, false);
            TrackApplyMetric((Time.realtimeSinceStartupAsDouble * 1000d) - applyStartMs);
        }

        private void TrackTileMetric(double tileMs)
        {
            const double alpha = 0.15d;
            averageTileMs = (averageTileMs * (1d - alpha)) + (tileMs * alpha);
        }

        private void TrackApplyMetric(double applyMs)
        {
            const double alpha = 0.15d;
            averageApplyMs = (averageApplyMs * (1d - alpha)) + (applyMs * alpha);
        }

        private void EnsureTextures(int width, int height)
        {
            cpuRenderTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cpuPreviewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            cpuRenderTexture.wrapMode = TextureWrapMode.Clamp;
            cpuPreviewTexture.wrapMode = TextureWrapMode.Clamp;

            gpuRenderTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            gpuPreviewTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            gpuRenderTexture.wrapMode = TextureWrapMode.Clamp;
            gpuPreviewTexture.wrapMode = TextureWrapMode.Clamp;
            gpuRenderTexture.Create();
            gpuPreviewTexture.Create();
        }

        private void ReleaseTextures()
        {
            if (cpuRenderTexture != null)
            {
                Destroy(cpuRenderTexture);
                cpuRenderTexture = null;
            }

            if (cpuPreviewTexture != null)
            {
                Destroy(cpuPreviewTexture);
                cpuPreviewTexture = null;
            }

            if (gpuRenderTexture != null)
            {
                gpuRenderTexture.Release();
                Destroy(gpuRenderTexture);
                gpuRenderTexture = null;
            }

            if (gpuPreviewTexture != null)
            {
                gpuPreviewTexture.Release();
                Destroy(gpuPreviewTexture);
                gpuPreviewTexture = null;
            }

            currentTextureWidth = 0;
            currentTextureHeight = 0;
            currentRenderScale = -1f;
        }

        private void PushTexture()
        {
            if (targetImage != null)
            {
                targetImage.texture = lastFrameGpu && gpuRenderTexture != null ? gpuRenderTexture : cpuRenderTexture;
                if (transitionPreviewTexture != null)
                {
                    Destroy(transitionPreviewTexture);
                    transitionPreviewTexture = null;
                }
            }
        }

        private void CaptureTransitionPreview()
        {
            if (targetImage == null)
            {
                return;
            }

            if (transitionPreviewTexture != null)
            {
                Destroy(transitionPreviewTexture);
                transitionPreviewTexture = null;
            }

            if (cpuRenderTexture == null || cpuRenderTexture.width <= 0 || cpuRenderTexture.height <= 0)
            {
                return;
            }

            transitionPreviewTexture = new Texture2D(cpuRenderTexture.width, cpuRenderTexture.height, TextureFormat.RGBA32, false);
            transitionPreviewTexture.SetPixels32(cpuRenderTexture.GetPixels32());
            transitionPreviewTexture.Apply(false, false);
            targetImage.texture = transitionPreviewTexture;
            targetImage.uvRect = new Rect(-0.02f, -0.02f, 1.04f, 1.04f);
        }

        private float ResolveDesiredRenderScale()
        {
            return Mathf.Clamp01(settleRenderScale);
        }

        private float ResolveInteractRenderScale()
        {
            var defaultScale = Mathf.Clamp(interactRenderScale, 0.1f, 1f);
            if (!Application.isMobilePlatform)
            {
                adaptiveInteractRenderScale = defaultScale;
                return defaultScale;
            }

            var minScale = Mathf.Min(mobileMinInteractScale, mobileMaxInteractScale);
            var maxScale = Mathf.Max(mobileMinInteractScale, mobileMaxInteractScale);
            adaptiveInteractRenderScale = Mathf.Clamp(adaptiveInteractRenderScale, minScale, maxScale);

            var targetMs = Mathf.Max(8f, mobileTargetFrameTimeMs);
            if (smoothedFrameTimeMs > targetMs + 0.75f)
            {
                adaptiveInteractRenderScale -= mobileScaleAdjustSpeed * Time.unscaledDeltaTime;
            }
            else if (smoothedFrameTimeMs < targetMs - 1f)
            {
                adaptiveInteractRenderScale += mobileScaleAdjustSpeed * Time.unscaledDeltaTime;
            }

            return Mathf.Clamp(adaptiveInteractRenderScale, minScale, maxScale);
        }

        private void TrackFrameTiming()
        {
            var frameTimeMs = Mathf.Max(0.1f, Time.unscaledDeltaTime * 1000f);
            smoothedFrameTimeMs = Mathf.Lerp(smoothedFrameTimeMs, frameTimeMs, 0.12f);
        }

        private static void BuildDefaultGradient(out Gradient gradient)
        {
            gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.05f, 0.1f, 0.3f), 0f),
                    new GradientColorKey(new Color(0.2f, 0.8f, 1f), 0.35f),
                    new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0.7f),
                    new GradientColorKey(new Color(1f, 1f, 1f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
        }
    }
}
