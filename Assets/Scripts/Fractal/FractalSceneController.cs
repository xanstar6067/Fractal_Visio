using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private int baseWidth = 1080;
        [SerializeField] private int baseHeight = 1920;

        [Header("Quality")]
        [SerializeField] private int tileSize = 96;
        [SerializeField] private int interactIterations = 96;
        [SerializeField] private int settleIterations = 256;
        [SerializeField] private float settleDelay = 0.15f;

        [Header("Zoom")]
        [SerializeField] private float pinchZoomSpeed = 1f;
        [SerializeField] private float minScale = 1e-20f;
        [SerializeField] private float maxScale = 4f;

        private readonly Dictionary<RenderMode, IFractalRenderer> renderers = new();

        private FractalPrecisionManager precisionManager;
        private Texture2D renderTexture;
        private Texture2D previewTexture;
        private FractalView view;

        private int generationId;
        private float lastInteractionTime;
        private bool isInteracting;

        private Vector2 previousPinchCenter;
        private float previousPinchDistance;
        private Vector2 previousSingleFingerPosition;
        private bool hasPreviousSingleFingerPosition;
        private int previousFingerMode;

        private void Awake()
        {
            EnsureTargetImage();
            precisionManager = new FractalPrecisionManager();
            view = FractalView.Default;
            BuildDefaultGradient(out var gradient);

            renderers[RenderMode.Fast] = new FastFractalRenderer(gradient);
            renderers[RenderMode.Perturbation] = new PerturbationFractalRenderer(gradient);
            renderers[RenderMode.PerturbationWithFallback] = new PerturbationFractalRenderer(gradient);

            EnsureTextures(baseWidth, baseHeight);
            PushTexture();
            RequestRender();
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
            isInteracting = HandleTouchInput();

            if (!isInteracting && Time.unscaledTime - lastInteractionTime > settleDelay && view.iterations != settleIterations)
            {
                view.iterations = settleIterations;
                RequestRender();
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
                if (hasPreviousSingleFingerPosition)
                {
                    ApplySingleFingerPan(previousSingleFingerPosition, currentPosition);
                }

                previousSingleFingerPosition = currentPosition;
                hasPreviousSingleFingerPosition = true;

                lastInteractionTime = Time.unscaledTime;
                view.iterations = interactIterations;
                RequestRender();
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
                }

                previousPinchCenter = center;
                previousPinchDistance = distance;

                lastInteractionTime = Time.unscaledTime;
                view.iterations = interactIterations;
                RequestRender();
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

            foreach (var tile in TilePlanner.BuildTiles(renderTexture.width, renderTexture.height, tileSize))
            {
                if (requestGeneration != generationId)
                {
                    yield break;
                }

                renderer.Render(request, renderTexture, tile);
                renderTexture.Apply(false, false);
                yield return null;
            }

            PushTexture();
            if (targetImage != null)
            {
                targetImage.uvRect = new Rect(0f, 0f, 1f, 1f);
            }
        }

        private void CachePreview()
        {
            if (previewTexture == null || renderTexture == null)
            {
                return;
            }

            previewTexture.SetPixels32(renderTexture.GetPixels32());
            previewTexture.Apply(false, false);

            if (targetImage != null)
            {
                targetImage.texture = previewTexture;
                targetImage.uvRect = new Rect(-0.02f, -0.02f, 1.04f, 1.04f);
            }
        }

        private void EnsureTextures(int width, int height)
        {
            renderTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            previewTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            renderTexture.wrapMode = TextureWrapMode.Clamp;
            previewTexture.wrapMode = TextureWrapMode.Clamp;
        }

        private void PushTexture()
        {
            if (targetImage != null)
            {
                targetImage.texture = renderTexture;
            }
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
