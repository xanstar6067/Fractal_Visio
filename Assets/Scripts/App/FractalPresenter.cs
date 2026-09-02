using System;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.Core;
using FractalVisio.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// Turns the session's view into pixels. Owns the render targets, picks a backend by scale and
    /// keeps the RawImage pointed at the right texture and sub-rectangle. It reads the session and
    /// never writes to it; input, HUD and menus live elsewhere.
    /// </summary>
    public sealed class FractalPresenter : IRenderStatusSource, IBackdropSource, IDisposable
    {
        private readonly RawImage targetImage;
        private readonly FractalSession session;
        private readonly MobileRenderProfile profile;

        private FractalGpuRenderer gpuRenderer;
        private FractalCpuRenderer cpuRenderer;

        private RenderTexture interactiveGpuTexture;
        private RenderTexture settledGpuTexture;
        private Texture2D cpuTexture;

        private Viewport interactiveViewport;
        private Viewport settledViewport;
        private Viewport cpuViewport;

        private RenderBackend currentBackend;
        private bool renderDirty;
        private bool hasRequestedView;
        private bool lastRequestWasInteractive;
        private bool lastUsedExtendedPrecision;
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

            session.Changed += OnSessionChanged;
            RecreateTextures();
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

        Rect IBackdropSource.UvRect => targetImage != null ? targetImage.uvRect : new Rect(0f, 0f, 1f, 1f);

        /// <summary>Drive one frame. <paramref name="interacting"/> comes from the input layer.</summary>
        public void Tick(bool interacting)
        {
            if (targetImage == null)
            {
                return;
            }

            if (Screen.width != cachedScreenWidth || Screen.height != cachedScreenHeight)
            {
                RecreateTextures();
                renderDirty = true;
            }

            if (interacting != lastRequestWasInteractive)
            {
                renderDirty = true;
            }

            if (renderDirty || !hasRequestedView)
            {
                RequestRender(interacting);
            }

            cpuRenderer?.Update();
        }

        public void Dispose()
        {
            session.Changed -= OnSessionChanged;
            cpuRenderer?.Dispose();
            gpuRenderer?.Dispose();
            cpuRenderer = null;
            gpuRenderer = null;
            DestroyTextures();
        }

        private void OnSessionChanged(SessionChange change)
        {
            if ((change & (SessionChange.View | SessionChange.Quality | SessionChange.Definition |
                           SessionChange.Parameters | SessionChange.Palette | SessionChange.Coloring)) != 0)
            {
                renderDirty = true;
            }
        }

        private void RequestRender(bool interacting)
        {
            var view = session.View;
            var quality = session.Quality;
            var definition = session.Definition;
            var scale = view.scale.AsDouble;

            currentBackend = gpuRenderer != null &&
                             scale >= quality.GpuMinimumScale &&
                             gpuRenderer.Supports(definition)
                ? RenderBackend.GpuFloat
                : RenderBackend.Cpu;

            if (currentBackend == RenderBackend.GpuFloat)
            {
                cpuRenderer?.Invalidate();
                var viewport = interacting ? interactiveViewport : settledViewport;
                var target = interacting ? interactiveGpuTexture : settledGpuTexture;
                lastUsedExtendedPrecision = false;
                gpuRenderer.Render(
                    definition,
                    session.Parameters,
                    ViewNavigator.ForViewport(view, viewport),
                    view.iterations,
                    target);
                targetImage.texture = target;
                targetImage.uvRect = viewport.VisibleUvRect;
            }
            else
            {
                // The CPU path always draws into the settled-size buffer; interaction only
                // changes how many progressive passes it runs.
                var useExtendedPrecision = scale < quality.ExtendedPrecisionScale &&
                                           (definition.SupportedPrecision & PrecisionTier.DoubleDouble) != 0;
                lastUsedExtendedPrecision = useExtendedPrecision;
                cpuRenderer.Request(
                    cpuTexture,
                    cpuViewport,
                    definition,
                    session.Parameters,
                    ViewNavigator.ForViewport(view, cpuViewport),
                    view.iterations,
                    useExtendedPrecision,
                    interacting);
                targetImage.texture = cpuTexture;
                targetImage.uvRect = cpuViewport.VisibleUvRect;
            }

            lastRequestWasInteractive = interacting;
            hasRequestedView = true;
            renderDirty = false;
        }

        private void RecreateTextures()
        {
            cpuRenderer?.CompletePendingWork();

            DestroyTextures();
            cachedScreenWidth = Mathf.Max(64, Screen.width);
            cachedScreenHeight = Mathf.Max(64, Screen.height);
            interactiveViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, true);
            settledViewport = profile.ResolveViewport(cachedScreenWidth, cachedScreenHeight, false);
            cpuViewport = profile.ResolveCpuViewport(cachedScreenWidth, cachedScreenHeight);

            interactiveGpuTexture = CreateRenderTexture(interactiveViewport, "Fractal GPU Interactive");
            settledGpuTexture = CreateRenderTexture(settledViewport, "Fractal GPU Settled");
            cpuTexture = CreateCpuTexture(cpuViewport, "Fractal CPU");
            hasRequestedView = false;
        }

        private void DestroyTextures()
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

        private static Texture2D CreateCpuTexture(in Viewport viewport, string textureName)
        {
            var texture = new Texture2D(viewport.Width, viewport.Height, TextureFormat.RGBA32, false, false)
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
