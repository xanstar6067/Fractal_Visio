using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

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

        private void Awake()
        {
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
            if (Input.touchCount < 2)
            {
                previousPinchDistance = 0f;
                return false;
            }

            var t0 = Input.GetTouch(0);
            var t1 = Input.GetTouch(1);

            var p0 = t0.position;
            var p1 = t1.position;
            var center = (p0 + p1) * 0.5f;
            var distance = Vector2.Distance(p0, p1);

            if (previousPinchDistance > 0.001f)
            {
                var zoomFactor = Mathf.Pow(distance / previousPinchDistance, pinchZoomSpeed);
                ApplyPinchZoom(center, zoomFactor);
                ApplyPanFromPinchCenter(previousPinchCenter, center);
                lastInteractionTime = Time.unscaledTime;
                view.iterations = interactIterations;
                RequestRender();
            }

            previousPinchCenter = center;
            previousPinchDistance = distance;
            return true;
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
            var nx = screenPoint.x / Screen.width;
            var ny = screenPoint.y / Screen.height;
            var aspect = (double)renderTexture.width / renderTexture.height;
            var halfScale = srcView.scale.AsDouble * 0.5d;

            var x = srcView.x.AsDouble + ((nx - 0.5d) * 2d * halfScale * aspect);
            var y = srcView.y.AsDouble + ((ny - 0.5d) * 2d * halfScale);
            return (x, y);
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
