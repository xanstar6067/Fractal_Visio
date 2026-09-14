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
    ///
    /// Colour is likewise kept out of the render: the CPU renderer accumulates escape values and
    /// applies a palette at publish time, so a palette change costs a remap rather than a render.
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

        /// <summary>
        /// Time a render may take while the view moves. Past it the run stops at the passes that
        /// fit, so a moving view gets a fresh frame several times a second instead of one sharp frame
        /// of a place already left. The reference app plans manual moves around 250 ms.
        /// </summary>
        private const double InteractiveTimeBudgetSeconds = 0.2d;

        /// <summary>A budgeted render still running after this many budgets is treated as stuck and restarted.</summary>
        private const double StaleRequestBudgets = 4d;

        /// <summary>
        /// Spacings within this ratio count as equally sharp. Pass steps differ by factors of two, so
        /// a real reason to hold a copy is always well past it; what stays inside is two frames of
        /// the same step whose fields were widened a few percent differently, and holding the older
        /// one over the newer for that gains nothing but a faint seam at its edge.
        /// </summary>
        private const double SharpnessTolerance = 1.25d;

        private static readonly Rect FullRect = new(0f, 0f, 1f, 1f);

        private readonly RawImage targetImage;
        private readonly FractalSession session;
        private readonly MobileRenderProfile profile;
        private readonly IColorMapper colorMapper = new EscapeColorMapper();
        private readonly CpuWorkerBudget workerBudget;

        private FractalGpuRenderer gpuRenderer;
        private FractalCpuRenderer cpuRenderer;
        private WideFieldLayer wideLayer;
        private FrameCompositor compositor;
        private readonly RetainedFrame retained = new();
        private IViewForecast forecast;

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
        private bool coloringDirty = true;
        private bool placeholdersStale;
        private bool hasRequestedView;
        private bool lastRequestWasInteractive;
        private bool lastUsedExtendedPrecision;
        private double lastFieldFactor = 1d;
        private double lastRequestTime;
        private float builtRenderScale = -1f;
        private int cachedScreenWidth;
        private int cachedScreenHeight;

        public FractalPresenter(RawImage targetImage, FractalSession session)
        {
            this.targetImage = targetImage;
            this.session = session;

            profile = MobileRenderProfile.Detect();
            workerBudget = CpuWorkerBudget.Detect(profile.WideWorkers);

            gpuRenderer = new FractalGpuRenderer();
            cpuRenderer = new FractalCpuRenderer(colorMapper, workerBudget, false);
            wideLayer = new WideFieldLayer(colorMapper, workerBudget);
            compositor = new FrameCompositor(session.Coloring.InteriorColor);
            cpuRenderer.FrameReplacing += OnMainFrameReplacing;

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
                var precision = currentBackend == RenderBackend.GpuFloat
                    ? PrecisionTier.Float
                    : cpuRenderer != null && cpuRenderer.ActivePrecision != PrecisionTier.None
                        ? cpuRenderer.ActivePrecision
                        : lastUsedExtendedPrecision ? PrecisionTier.DoubleDouble : PrecisionTier.Double;

                return new RenderStatus(
                    currentBackend,
                    lastRequestWasInteractive,
                    session.View.iterations,
                    lastUsedExtendedPrecision,
                    precision,
                    busy,
                    cpuRenderer != null ? cpuRenderer.CurrentPass : 0,
                    cpuRenderer != null ? cpuRenderer.PassCount : 0,
                    cpuRenderer != null ? cpuRenderer.Progress : 0f,
                    workerBudget.Allowed,
                    workerBudget.Total);
            }
        }

        /// <summary>
        /// Frame interval the pacing budget aims for. Uncapped desktops are held to 60 Hz, and a
        /// 120 Hz phone to 90: past that, parking fractal workers to win the last few milliseconds of
        /// a frame costs more in render speed than the eye gains in smoothness.
        /// </summary>
        private static double TargetFrameSeconds =>
            Application.targetFrameRate > 0 ? 1d / Math.Min(Application.targetFrameRate, 90) : 1d / 60d;

        public string ActiveTextureName => targetImage != null && targetImage.texture != null
            ? targetImage.texture.name
            : string.Empty;

        /// <summary>Pixel size of the buffer the fractal is actually computed into, for the HUD.</summary>
        public Vector2Int RenderSize => currentBackend == RenderBackend.GpuFloat
            ? new Vector2Int(settledViewport.Width, settledViewport.Height)
            : cpuBuffer;

        /// <summary>What is on screen, for the UI backdrop. See <see cref="IBackdropSource"/>.</summary>
        Texture IBackdropSource.Texture => targetImage != null ? targetImage.texture : null;

        Rect IBackdropSource.UvRect => targetImage != null ? targetImage.uvRect : FullRect;

        /// <summary>Drive one frame. <paramref name="interacting"/> comes from the input layer.</summary>
        /// <param name="viewForecast">Where the view is going, when that is known - a coasting view. May be null.</param>
        public void Tick(bool interacting, IViewForecast viewForecast = null)
        {
            if (targetImage == null)
            {
                return;
            }

            forecast = viewForecast;

            if (Screen.width != cachedScreenWidth ||
                Screen.height != cachedScreenHeight ||
                !Mathf.Approximately(session.Quality.RenderScale, builtRenderScale))
            {
                RecreateTargets();
                renderDirty = true;
            }

            ApplyColoring();

            var view = session.View;
            motion.Sample(view.scale.AsDouble, Time.unscaledDeltaTime);

            var backend = ResolveBackend(view);

            // Frame pacing: only the CPU path competes with the main thread for cores.
            workerBudget.Regulate(
                Time.unscaledDeltaTime,
                TargetFrameSeconds,
                interacting && hasBackend && currentBackend == RenderBackend.Cpu);
            if (!hasBackend || backend != currentBackend)
            {
                // Nothing computed for the outgoing backend stands for what the incoming one is
                // about to draw, so neither placeholder may be placed under it.
                placeholdersStale = true;
                renderDirty = true;
                currentBackend = backend;
                hasBackend = true;
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
            if (cpuRenderer != null)
            {
                cpuRenderer.FrameReplacing -= OnMainFrameReplacing;
            }

            retained.Dispose();
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
                           SessionChange.Parameters)) != 0)
            {
                renderDirty = true;
            }

            // A different fractal or parameter makes every kept frame a picture of something else.
            // A different view does not - that is the whole point of keeping them - and neither
            // does a different palette, which is a recolour of the same escape values.
            if ((change & (SessionChange.Definition | SessionChange.Parameters)) != 0)
            {
                placeholdersStale = true;
            }

            if ((change & (SessionChange.Palette | SessionChange.Coloring)) != 0)
            {
                coloringDirty = true;
            }
        }

        /// <summary>
        /// Push palette and colouring down. The CPU path recolours the escape buffer it already
        /// has; the GPU path has no buffer to recolour, so there it costs one re-render - cheap,
        /// because the GPU path only runs where a whole frame is a millisecond.
        /// </summary>
        private void ApplyColoring()
        {
            if (!coloringDirty)
            {
                return;
            }

            coloringDirty = false;
            var palette = session.Palette;
            var coloring = session.Coloring;

            // The retained copy is colour, not escape values: it would show the old palette on top.
            retained.Release();

            cpuRenderer?.SetColoring(palette, coloring);
            wideLayer?.SetColoring(palette, coloring);
            gpuRenderer?.SetColoring(palette, coloring);
            compositor?.SetFallbackColor(coloring.InteriorColor);

            if (currentBackend == RenderBackend.GpuFloat)
            {
                renderDirty = true;
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
            retained.Release();
            cpuRenderer?.Invalidate();
            wideLayer?.Suspend();
            DropStalePlaceholders();

            if (!renderDirty && hasRequestedView && !(lastRequestWasInteractive && !interacting))
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
                RequestCpuRender(view, interacting, fieldFactor, displayAspect);
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

        private bool ForecastActive => forecast != null && forecast.IsActive;

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

            // The gesture ended and the last render was widened to anticipate it: redo it at full
            // resolution. This, and not the touch itself, is what "interacting" is allowed to
            // trigger - a finger resting on the glass must leave a finished frame alone.
            if (!interacting && lastFieldFactor > 1.0001d)
            {
                return true;
            }

            if (!renderDirty)
            {
                return false;
            }

            if (!interacting || !cpuRenderer.IsBusy)
            {
                return true;
            }

            // A coasting view was rendered ahead of itself on purpose, so "does the running render
            // cover where the viewer is right now" is the wrong question: during a zoom-in the view
            // now is wider than the frame aimed at a moment from now, and asking it restarted the
            // render every frame so that nothing was ever published. A budgeted run is short anyway;
            // restart only a render that was never budgeted (the settle render from before the
            // flick) or one that has overrun its budget several times over.
            if (ForecastActive)
            {
                return !lastRequestWasInteractive ||
                       Time.unscaledTimeAsDouble - lastRequestTime > InteractiveTimeBudgetSeconds * StaleRequestBudgets;
            }

            var placement = FramePlacement.Resolve(requestedView, compositeViewport.Aspect, view, displayAspect);

            if (Math.Abs(fieldFactor - lastFieldFactor) > FieldChangeThreshold)
            {
                return true;
            }

            return !placement.IsValid || placement.Overhang > InFlightOverhangLimit;
        }

        private void RequestCpuRender(in ViewState view, bool interacting, double fieldFactor, double displayAspect)
        {
            // A coasting view's future is known exactly, so aim the frame at the middle of the time it
            // will be on screen - from when it lands to when the next one does - and widen the field
            // until it covers both ends. Rendering the view as it is now would deliver a frame of a
            // place the viewer has already left.
            var target = view;
            if (interacting && ForecastActive)
            {
                var budget = InteractiveTimeBudgetSeconds;
                target = forecast.Predict(view, budget * 1.5d);
                fieldFactor = CoveringFieldFactor(
                    target,
                    forecast.Predict(view, budget),
                    forecast.Predict(view, budget * 2d),
                    fieldFactor,
                    displayAspect);
            }

            var viewport = profile.ResolveCpuViewport(cpuBuffer, fieldFactor);
            lastCpuViewport = viewport;
            lastFieldFactor = fieldFactor;
            lastUsedExtendedPrecision = ResolveExtendedPrecision(target.scale.AsDouble);
            requestedView = ViewNavigator.ForViewport(target, viewport);

            cpuRenderer.Request(
                cpuTexture,
                viewport,
                session.Definition,
                session.Parameters,
                requestedView,
                target.iterations,
                lastUsedExtendedPrecision,
                interacting ? InteractiveTimeBudgetSeconds : 0d);

            lastRequestWasInteractive = interacting;
            lastRequestTime = Time.unscaledTimeAsDouble;
            hasRequestedView = true;
            renderDirty = false;
        }

        /// <summary>
        /// How sharp a frame of plane sample spacing <paramref name="spacing"/> actually looks right
        /// now. Detail finer than the display's own pixels is invisible - and, minified without mip
        /// maps, it only shimmers - so everything is clamped to the spacing a full-detail frame of
        /// the current view would have. Without the clamp, a frame from deeper in held its place over
        /// every later frame after a zoom-out, because nothing at the new scale could ever be "sharper".
        /// </summary>
        private double VisibleSpacing(double spacing) => Math.Max(spacing, Math.Abs(session.View.scale.AsDouble));

        /// <summary>
        /// Smallest field factor, from <paramref name="baseFactor"/> up, at which a frame of
        /// <paramref name="center"/> covers both <paramref name="entry"/> and <paramref name="exit"/>.
        /// Overhang is a fraction of the display, so widening by twice it closes the gap in one or
        /// two rounds; the profile's ceiling still applies.
        /// </summary>
        private double CoveringFieldFactor(in ViewState center, in ViewState entry, in ViewState exit, double baseFactor, double displayAspect)
        {
            var factor = Math.Max(1d, baseFactor);
            for (var round = 0; round < 4 && factor < profile.CpuFieldMax; round++)
            {
                var frame = ViewNavigator.ForViewport(center, profile.ResolveCpuViewport(cpuBuffer, factor));
                var atEntry = FramePlacement.Resolve(frame, compositeViewport.Aspect, entry, displayAspect);
                var atExit = FramePlacement.Resolve(frame, compositeViewport.Aspect, exit, displayAspect);
                if (!atEntry.IsValid || !atExit.IsValid)
                {
                    break;
                }

                var overhang = Math.Max(atEntry.Overhang, atExit.Overhang);
                if (overhang <= 0f)
                {
                    break;
                }

                factor = Math.Min(profile.CpuFieldMax, factor * (1d + 2d * overhang));
            }

            return factor;
        }

        /// <summary>
        /// The main frame is about to be overwritten. If the outgoing picture is sharper than the
        /// incoming one, copy it and keep it on top; otherwise copy it to dissolve into its
        /// successor. See <see cref="RetainedFrame"/>.
        /// </summary>
        private void OnMainFrameReplacing(ViewState incomingView, int incomingStep)
        {
            if (!cpuRenderer.HasPublished || compositor == null || !compositor.IsSupported)
            {
                return;
            }

            var outgoingView = cpuRenderer.PublishedView;
            var outgoingStep = cpuRenderer.PublishedStep;
            if (outgoingStep == incomingStep && SameView(outgoingView, incomingView))
            {
                return; // a recolour of the same frame, not a new one
            }

            var now = Time.unscaledTimeAsDouble;
            var outgoingSpacing = RetainedFrame.SpacingOf(outgoingView, outgoingStep);
            var incomingSpacing = VisibleSpacing(RetainedFrame.SpacingOf(incomingView, incomingStep));

            if (retained.IsHolding && VisibleSpacing(retained.Spacing) <= VisibleSpacing(outgoingSpacing))
            {
                // Already holding something at least as sharp as what is leaving: keep that.
                if (incomingSpacing <= VisibleSpacing(retained.Spacing) * SharpnessTolerance)
                {
                    retained.BeginFade(now);
                }

                return;
            }

            var placement = FramePlacement.Resolve(
                outgoingView, cpuRenderer.PublishedAspect, session.View, compositeViewport.Aspect);
            if (!placement.IsValid || placement.Overhang >= 1f)
            {
                return; // not on screen at all: nothing to keep or to dissolve from
            }

            var hold = VisibleSpacing(outgoingSpacing) * SharpnessTolerance < incomingSpacing;
            retained.Capture(cpuTexture, outgoingView, cpuRenderer.PublishedAspect, outgoingSpacing, hold, now);
        }

        /// <summary>
        /// Place every frame we have under the current view and hand the result to the RawImage.
        /// Runs every frame: the placement changes with the view, not with the pixels, so a gesture
        /// keeps moving the picture even while no render is running.
        /// </summary>
        private void Compose(in ViewState view, double displayAspect)
        {
            var now = Time.unscaledTimeAsDouble;

            var mainPlacement = cpuRenderer.HasPublished
                ? FramePlacement.Resolve(cpuRenderer.PublishedView, cpuRenderer.PublishedAspect, view, displayAspect)
                : FramePlacement.Invalid;

            var widePlacement = wideLayer.HasFrame
                ? FramePlacement.Resolve(wideLayer.FrameView, wideLayer.FrameAspect, view, displayAspect)
                : FramePlacement.Invalid;

            var retainedPlacement = retained.IsActive
                ? FramePlacement.Resolve(retained.View, retained.Aspect, view, displayAspect)
                : FramePlacement.Invalid;

            if (retained.IsHolding)
            {
                if (cpuRenderer.HasPublished &&
                    VisibleSpacing(RetainedFrame.SpacingOf(cpuRenderer.PublishedView, cpuRenderer.PublishedStep)) <=
                    VisibleSpacing(retained.Spacing) * SharpnessTolerance)
                {
                    retained.BeginFade(now);
                }
                else if (!retainedPlacement.IsValid || retainedPlacement.Overhang >= 1f)
                {
                    retained.Release(); // the view has left it entirely
                }
            }

            var retainedAlpha = retained.Alpha(now);

            var composed = mainPlacement.IsValid
                ? compositor.Compose(
                    compositeViewport, cpuTexture, mainPlacement, wideLayer.Texture, widePlacement,
                    retained.Texture, retainedPlacement, retainedAlpha)
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

        private static bool SameView(in ViewState a, in ViewState b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.scale.Equals(b.scale) && a.rotation == b.rotation;
        }

        private bool ResolveExtendedPrecision(double scale)
        {
            return scale < session.Quality.ExtendedPrecisionScale &&
                   (session.Definition.SupportedPrecision & (PrecisionTier.DoubleDouble | PrecisionTier.Perturbation)) != 0;
        }

        private void DropStalePlaceholders()
        {
            if (!placeholdersStale)
            {
                return;
            }

            placeholdersStale = false;
            retained.Release();
            cpuRenderer?.DiscardPublished();
            wideLayer?.Discard();
        }

        private void RecreateTargets()
        {
            cpuRenderer?.CompletePendingWork();
            retained.Release();

            DestroyTargets();
            cachedScreenWidth = Mathf.Max(64, Screen.width);
            cachedScreenHeight = Mathf.Max(64, Screen.height);
            builtRenderScale = session.Quality.RenderScale;

            interactiveViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, true, builtRenderScale);
            settledViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, false, builtRenderScale);
            cpuBuffer = profile.ResolveCpuBuffer(cachedScreenWidth, cachedScreenHeight, builtRenderScale);
            compositeViewport = new Viewport(cpuBuffer.x, cpuBuffer.y);
            lastCpuViewport = profile.ResolveCpuViewport(cpuBuffer, 1d);
            lastFieldFactor = 1d;

            interactiveGpuTexture = CreateRenderTexture(interactiveViewport, "Fractal GPU Interactive");
            settledGpuTexture = CreateRenderTexture(settledViewport, "Fractal GPU Settled");
            cpuTexture = CreateCpuTexture(cpuBuffer, "Fractal CPU", session.Coloring.InteriorColor);
            wideLayer?.Resize(profile.ResolveWideViewport(cachedScreenWidth, cachedScreenHeight));

            motion.Reset();
            hasRequestedView = false;
            coloringDirty = true;
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

        private static Texture2D CreateCpuTexture(Vector2Int size, string textureName, Color32 clearColor)
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
                pixels[i] = clearColor;
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
    }
}
