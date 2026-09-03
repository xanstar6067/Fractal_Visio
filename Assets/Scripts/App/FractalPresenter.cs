using System;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.Core;
using FractalVisio.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// Turns the session's view into pixels. Owns the render targets, picks a backend by scale and
    /// keeps the RawImage pointed at the right texture. It reads the session and never writes to
    /// it; input, HUD and menus live elsewhere.
    ///
    /// The CPU path is a compositor, not a single buffer. Three things are kept apart on purpose:
    /// what has been computed (frames, each remembering its own <see cref="ViewState"/>), where the
    /// viewer is now (the session), and how the two are reconciled (an affine map per frame, see
    /// <see cref="FramePlacement"/>). Nothing rewrites computed pixels to chase a gesture, which is
    /// what keeps a long pan from turning into a pile of resamplings of resamplings.
    /// </summary>
    public sealed class FractalPresenter : IRenderStatusSource, IBackdropSource, IDisposable
    {
        /// <summary>
        /// How far ahead the field of view is sized, in seconds. Roughly how long it takes a coarse
        /// pass to land and reach the screen; the render covers where the view will be by then
        /// rather than where it is now.
        /// </summary>
        private const double FieldLookaheadSeconds = 0.35d;

        /// <summary>Re-request only when the field moved by more than this, to avoid thrashing.</summary>
        private const double FieldChangeThreshold = 0.06d;

        /// <summary>
        /// While a render is already running for a nearby view, let it finish instead of restarting
        /// it. Measured as <see cref="FramePlacement.Overhang"/> of the display against the
        /// in-flight request: negative means the running render still covers where the viewer is.
        /// Without this a continuous pinch cancels the render every frame and no pass ever lands -
        /// which is exactly when the picture most needs new pixels.
        /// </summary>
        private const float InFlightOverhangLimit = -0.02f;

        private static readonly Rect FullRect = new(0f, 0f, 1f, 1f);

        private readonly RawImage targetImage;
        private readonly FractalSession session;
        private readonly MobileRenderProfile profile;

        private FractalGpuRenderer gpuRenderer;
        private FractalCpuRenderer cpuRenderer;
        private WideFieldLayer wideLayer;
        private FrameCompositor compositor;

        private RenderTexture interactiveGpuTexture;
        private RenderTexture settledGpuTexture;
        private Texture2D cpuTexture;

        private Viewport interactiveViewport;
        private Viewport settledViewport;
        private Vector2Int cpuBuffer;
        private Viewport compositeViewport;
        private Viewport lastCpuViewport;

        private ViewMotion motion;
        private ViewState requestedView;
        private RenderBackend currentBackend;
        private bool hasBackend;
        private bool renderDirty;
        private bool placeholdersStale;
        private bool hasRequestedView;
        private bool lastRequestWasInteractive;
        private bool lastUsedExtendedPrecision;
        private double lastFieldFactor = 1d;
        private int cachedScreenWidth;
        private int cachedScreenHeight;

        public FractalPresenter(RawImage targetImage, FractalSession session)
        {
            this.targetImage = targetImage;
            this.session = session;

            profile = MobileRenderProfile.Detect();

            var gradient = BuildDefaultGradient();
            gpuRenderer = new FractalGpuRenderer(gradient);
            cpuRenderer = new FractalCpuRenderer(gradient);
            wideLayer = new WideFieldLayer(gradient, profile.WideWorkers);
            compositor = new FrameCompositor(FractalCpuRenderer.InteriorColor);

            session.Changed += OnSessionChanged;
            RecreateTargets();
            renderDirty = true;
        }

        /// <summary>Viewport the user is looking at: gestures are expressed in these pixels.</summary>
        public Viewport DisplayViewport => new(Screen.width, Screen.height);

        public RenderStatus Status
        {
            get
            {
                var busy = cpuRenderer != null && cpuRenderer.IsBusy;
                return new RenderStatus(
                    currentBackend,
                    lastRequestWasInteractive,
                    session.View.iterations,
                    lastUsedExtendedPrecision,
                    busy,
                    cpuRenderer != null ? cpuRenderer.CurrentPass : 0,
                    cpuRenderer != null ? cpuRenderer.PassCount : 0,
                    cpuRenderer != null ? cpuRenderer.Progress : 0f);
            }
        }

        public string ActiveTextureName => targetImage != null && targetImage.texture != null
            ? targetImage.texture.name
            : string.Empty;

        /// <summary>What is on screen, for the UI backdrop. See <see cref="IBackdropSource"/>.</summary>
        Texture IBackdropSource.Texture => targetImage != null ? targetImage.texture : null;

        Rect IBackdropSource.UvRect => targetImage != null ? targetImage.uvRect : FullRect;

        /// <summary>Drive one frame. <paramref name="interacting"/> comes from the input layer.</summary>
        public void Tick(bool interacting)
        {
            if (targetImage == null)
            {
                return;
            }

            if (Screen.width != cachedScreenWidth || Screen.height != cachedScreenHeight)
            {
                RecreateTargets();
                renderDirty = true;
            }

            var view = session.View;
            motion.Sample(view.scale.AsDouble, Time.unscaledDeltaTime);

            var backend = ResolveBackend(view);
            if (!hasBackend || backend != currentBackend)
            {
                // Nothing computed for the outgoing backend stands for what the incoming one is
                // about to draw, so neither placeholder may be placed under it.
                placeholdersStale = true;
                renderDirty = true;
                currentBackend = backend;
                hasBackend = true;
            }

            if (interacting != lastRequestWasInteractive)
            {
                renderDirty = true;
            }

            if (currentBackend == RenderBackend.GpuFloat)
            {
                TickGpu(view, interacting);
            }
            else
            {
                TickCpu(view, interacting);
            }
        }

        public void Dispose()
        {
            session.Changed -= OnSessionChanged;
            cpuRenderer?.Dispose();
            gpuRenderer?.Dispose();
            wideLayer?.Dispose();
            compositor?.Dispose();
            cpuRenderer = null;
            gpuRenderer = null;
            wideLayer = null;
            compositor = null;
            DestroyTargets();
        }

        private void OnSessionChanged(SessionChange change)
        {
            if ((change & (SessionChange.View | SessionChange.Quality | SessionChange.Definition |
                           SessionChange.Parameters | SessionChange.Palette | SessionChange.Coloring)) != 0)
            {
                renderDirty = true;
            }

            // A different fractal, parameter or palette makes every kept frame a picture of
            // something else. A different view does not - that is the whole point of keeping them.
            if ((change & (SessionChange.Definition | SessionChange.Parameters |
                           SessionChange.Palette | SessionChange.Coloring)) != 0)
            {
                placeholdersStale = true;
            }
        }

        private RenderBackend ResolveBackend(in ViewState view)
        {
            var quality = session.Quality;
            return gpuRenderer != null &&
                   view.scale.AsDouble >= quality.GpuMinimumScale &&
                   gpuRenderer.Supports(session.Definition)
                ? RenderBackend.GpuFloat
                : RenderBackend.Cpu;
        }

        private void TickGpu(in ViewState view, bool interacting)
        {
            cpuRenderer?.Invalidate();
            wideLayer?.Suspend();
            DropStalePlaceholders();

            if (!renderDirty && hasRequestedView)
            {
                return;
            }

            var viewport = interacting ? interactiveViewport : settledViewport;
            var target = interacting ? interactiveGpuTexture : settledGpuTexture;
            lastUsedExtendedPrecision = false;

            gpuRenderer.Render(
                session.Definition,
                session.Parameters,
                ViewNavigator.ForViewport(view, viewport),
                view.iterations,
                target);

            targetImage.texture = target;
            targetImage.uvRect = viewport.VisibleUvRect;

            lastRequestWasInteractive = interacting;
            hasRequestedView = true;
            renderDirty = false;
        }

        private void TickCpu(in ViewState view, bool interacting)
        {
            DropStalePlaceholders();

            // While the view is moving, cover more than the screen; the widening is paid for in
            // resolution, not in pixels, because the buffer size never changes. See
            // MobileRenderProfile.CpuFieldBase.
            var fieldFactor = interacting
                ? motion.FieldFactor(FieldLookaheadSeconds, profile.CpuFieldBase, profile.CpuFieldMax)
                : 1d;

            var displayAspect = compositeViewport.Aspect;
            if (ShouldRequestCpuRender(view, interacting, fieldFactor, displayAspect))
            {
                RequestCpuRender(view, interacting, fieldFactor);
            }

            cpuRenderer.Update();

            wideLayer.Tick(
                session.Definition,
                session.Parameters,
                view,
                profile.WideFieldFactor,
                view.iterations,
                ResolveExtendedPrecision(view.scale.AsDouble * profile.WideFieldFactor),
                displayAspect);

            Compose(view, displayAspect);
        }

        /// <summary>
        /// Whether to start a new CPU render. The interesting case is the middle one: mid-gesture,
        /// with a render already running for a view that still covers the screen. Cancelling it
        /// would be the third restart this second and the tenth this gesture, and none of them ever
        /// produces a pixel. Letting it land is what the widened field was bought for.
        /// </summary>
        private bool ShouldRequestCpuRender(in ViewState view, bool interacting, double fieldFactor, double displayAspect)
        {
            if (!hasRequestedView)
            {
                return true;
            }

            if (!renderDirty)
            {
                return false;
            }

            // Settling, or the interaction state just flipped: the settled pass must be exact.
            if (!interacting || interacting != lastRequestWasInteractive)
            {
                return true;
            }

            if (Math.Abs(fieldFactor - lastFieldFactor) > FieldChangeThreshold || !cpuRenderer.IsBusy)
            {
                return true;
            }

            var placement = FramePlacement.Resolve(requestedView, compositeViewport.Aspect, view, displayAspect);
            return !placement.IsValid || placement.Overhang > InFlightOverhangLimit;
        }

        private void RequestCpuRender(in ViewState view, bool interacting, double fieldFactor)
        {
            var viewport = profile.ResolveCpuViewport(cpuBuffer, fieldFactor);
            lastCpuViewport = viewport;
            lastFieldFactor = fieldFactor;
            lastUsedExtendedPrecision = ResolveExtendedPrecision(view.scale.AsDouble);
            requestedView = ViewNavigator.ForViewport(view, viewport);

            cpuRenderer.Request(
                cpuTexture,
                viewport,
                session.Definition,
                session.Parameters,
                requestedView,
                view.iterations,
                lastUsedExtendedPrecision,
                interacting);

            lastRequestWasInteractive = interacting;
            hasRequestedView = true;
            renderDirty = false;
        }

        /// <summary>
        /// Place every frame we have under the current view and hand the result to the RawImage.
        /// Runs every frame: the placement changes with the view, not with the pixels, so a gesture
        /// keeps moving the picture even while no render is running.
        /// </summary>
        private void Compose(in ViewState view, double displayAspect)
        {
            var mainPlacement = cpuRenderer.HasPublished
                ? FramePlacement.Resolve(cpuRenderer.PublishedView, cpuRenderer.PublishedAspect, view, displayAspect)
                : FramePlacement.Invalid;

            var widePlacement = wideLayer.HasFrame
                ? FramePlacement.Resolve(wideLayer.FrameView, wideLayer.FrameAspect, view, displayAspect)
                : FramePlacement.Invalid;

            var composed = mainPlacement.IsValid
                ? compositor.Compose(compositeViewport, cpuTexture, mainPlacement, wideLayer.Texture, widePlacement)
                : widePlacement.IsValid &&
                  compositor.Compose(compositeViewport, wideLayer.Texture, widePlacement, null, FramePlacement.Invalid);

            if (composed && compositor.Texture != null)
            {
                targetImage.texture = compositor.Texture;
                targetImage.uvRect = FullRect;
                return;
            }

            // No compositor (unsupported shader) or nothing published yet: show the raw buffer.
            // Correct at rest, simply does not follow the gesture.
            targetImage.texture = cpuTexture;
            targetImage.uvRect = lastCpuViewport.VisibleUvRect;
        }

        private bool ResolveExtendedPrecision(double scale)
        {
            return scale < session.Quality.ExtendedPrecisionScale &&
                   (session.Definition.SupportedPrecision & PrecisionTier.DoubleDouble) != 0;
        }

        private void DropStalePlaceholders()
        {
            if (!placeholdersStale)
            {
                return;
            }

            placeholdersStale = false;
            cpuRenderer?.DiscardPublished();
            wideLayer?.Discard();
        }

        private void RecreateTargets()
        {
            cpuRenderer?.CompletePendingWork();

            DestroyTargets();
            cachedScreenWidth = Mathf.Max(64, Screen.width);
            cachedScreenHeight = Mathf.Max(64, Screen.height);

            interactiveViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, true);
            settledViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, false);
            cpuBuffer = profile.ResolveCpuBuffer(cachedScreenWidth, cachedScreenHeight);
            compositeViewport = new Viewport(cpuBuffer.x, cpuBuffer.y);
            lastCpuViewport = profile.ResolveCpuViewport(cpuBuffer, 1d);
            lastFieldFactor = 1d;

            interactiveGpuTexture = CreateRenderTexture(interactiveViewport, "Fractal GPU Interactive");
            settledGpuTexture = CreateRenderTexture(settledViewport, "Fractal GPU Settled");
            cpuTexture = CreateCpuTexture(cpuBuffer, "Fractal CPU");
            wideLayer?.Resize(profile.ResolveWideViewport(cachedScreenWidth, cachedScreenHeight));

            motion.Reset();
            hasRequestedView = false;
        }

        private void DestroyTargets()
        {
            ReleaseRenderTexture(ref interactiveGpuTexture);
            ReleaseRenderTexture(ref settledGpuTexture);
            DestroyTexture(ref cpuTexture);
        }

        private static RenderTexture CreateRenderTexture(in Viewport viewport, string textureName)
        {
            var texture = new RenderTexture(viewport.Width, viewport.Height, 0, RenderTextureFormat.ARGB32)
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

            // A fresh Texture2D holds whatever was in that memory. Clearing it once per resize is
            // the difference between a dark frame and a flash of garbage before the first pass.
            var pixels = new Color32[size.x * size.y];
            for (var i = 0; i < pixels.Length; i++)
            {
                pixels[i] = FractalCpuRenderer.InteriorColor;
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (texture == null)
            {
                return;
            }

            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        private static void DestroyTexture(ref Texture2D texture)
        {
            if (texture == null)
            {
                return;
            }

            UnityEngine.Object.Destroy(texture);
            texture = null;
        }

        // Stage 5 replaces this with a PaletteAsset chosen through the session.
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
