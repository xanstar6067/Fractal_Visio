using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Hybrid perturbation renderer:
    /// 1) CPU builds reference orbit rarely and caches it.
    /// 2) GPU renders delta field in full screen pass.
    /// 3) Optional CPU fallback progressively repaints the frame when precision
    ///    requirements exceed the GPU approximation.
    ///
    /// FIXES applied:
    /// - Reference orbit stores an extra Z(n+1) sample so the shader can test
    ///   escape against the same iteration state used by the delta recurrence.
    /// - _CenterDelta computed via decimal arithmetic to avoid catastrophic
    ///   cancellation when subtracting two nearly-equal doubles.
    /// - Aspect ratio passed to shader so UV mapping matches CPU kernel exactly.
    /// - CPU fallback uses the same iteration count as the main request,
    ///   not an inflated budget that caused tile seam brightness mismatches.
    /// - OrbitReuseCenterFactor tightened from 0.20 → 0.05 to force orbit
    ///   rebuild earlier; eliminates "blooming" at deep zoom from stale orbits.
    /// </summary>
    public sealed class PerturbationFractalRenderer : IFractalRenderer, IDisposable
    {
        private const string ShaderName = "FractalVisio/MandelbrotPerturbation";
        private const int OrbitTextureWidth = 1024;
        private const float GlitchThreshold = 1e-4f;

        // Tightened from 0.20 – stale orbits at deep zoom cause bloom/banding.
        private const double OrbitReuseCenterFactor = 0.05d;
        private const double OrbitReuseScaleFactor  = 0.05d;

        private readonly Gradient gradient;
        private readonly bool enableCpuFallback;
        private readonly List<TileDescriptor> fallbackTiles = new();

        private Material perturbationMaterial;
        private Texture2D paletteTexture;
        private NativeArray<Color32> nativePalette;

        // Two float textures store a high component and residual for each
        // reference coordinate. The shader still runs the recurrence in float.
        // Layout: pixel (i) → orbit step i; R = X component, G = Y component.
        private Texture2D orbitTexHigh; // upper float of each (zx, zy)
        private Texture2D orbitTexLow;  // lower float (remainder after subtracting high)

        private bool gpuAvailable;
        private Color32[] cpuTileBuffer;
        private NativeArray<Color32> nativeCpuTileBuffer;

        // Cached in decimal for high-precision delta computation.
        private decimal referenceCxDecimal;
        private decimal referenceCyDecimal;
        private double  referenceScale;
        private int     cachedOrbitIterations;

        public PerturbationFractalRenderer(Gradient gradient, bool enableCpuFallback = false)
        {
            this.gradient          = gradient;
            this.enableCpuFallback = enableCpuFallback;

            var shader = Shader.Find(ShaderName);
            if (shader != null && shader.isSupported)
            {
                perturbationMaterial = new Material(shader)
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                paletteTexture = BuildPaletteTexture(gradient);
                perturbationMaterial.SetTexture("_PaletteTex", paletteTexture);
                gpuAvailable = true;
            }

            nativePalette = BuildNativePalette(gradient, Allocator.Persistent);
        }

        public RenderMode Mode => RenderMode.Perturbation;

        public bool RenderToGpu(in FractalRenderRequest request, RenderTexture target)
        {
            if (!gpuAvailable || target == null)
                return false;

            EnsureReferenceOrbit(request.View);
            UpdateMaterialConstants(request.View, target.width, target.height);
            Graphics.Blit(null, target, perturbationMaterial, 0);
            return true;
        }

        public IReadOnlyList<TileDescriptor> BuildFallbackTiles(
            in FractalRenderRequest request, int width, int height, int tileSize)
        {
            fallbackTiles.Clear();
            if (!enableCpuFallback)
                return fallbackTiles;

            var tileIndex = 0;
            for (var y = 0; y < height; y += tileSize)
            for (var x = 0; x < width;  x += tileSize)
            {
                var rectWidth  = Mathf.Min(tileSize, width  - x);
                var rectHeight = Mathf.Min(tileSize, height - y);
                fallbackTiles.Add(new TileDescriptor(new RectInt(x, y, rectWidth, rectHeight), tileIndex++));
            }

            return fallbackTiles;
        }

        public void Render(in FractalRenderRequest request, Texture target, TileDescriptor tile)
        {
            if (target is not Texture2D texture2D)
                return;

            // FIX: use request.View.iterations directly – no more inflated budget
            // that caused brightness seams between GPU pass and CPU fallback tiles.
            EnsureCpuTileBuffer(tile.PixelRect.width * tile.PixelRect.height);
            FractalCpuKernels.RenderMandelbrotTileBurst(
                cpuTileBuffer,
                ref nativeCpuTileBuffer,
                nativePalette,
                texture2D.width, texture2D.height,
                tile, request.View,
                request.View.iterations); // was: iterations + iterations/2
            FractalCpuKernels.BlitTile(texture2D, tile, cpuTileBuffer);
        }

        // ─── Reference orbit ──────────────────────────────────────────────────

        private void EnsureReferenceOrbit(in FractalView view)
        {
            var centerX = view.x.AsDouble;
            var centerY = view.y.AsDouble;
            var scale   = view.scale.AsDouble;

            var requiredOrbitLength = Mathf.Max(2, view.iterations + 1);
            var needsRebuild =
                orbitTexHigh == null ||
                cachedOrbitIterations != requiredOrbitLength ||
                Math.Abs((double)(view.x.AsDecimal - referenceCxDecimal)) >
                    Math.Max(scale * OrbitReuseCenterFactor, 1e-18d) ||
                Math.Abs((double)(view.y.AsDecimal - referenceCyDecimal)) >
                    Math.Max(scale * OrbitReuseCenterFactor, 1e-18d) ||
                Math.Abs(referenceScale - scale) >
                    Math.Max(scale * OrbitReuseScaleFactor,  1e-18d);

            if (!needsRebuild)
                return;

            cachedOrbitIterations = requiredOrbitLength;
            referenceCxDecimal    = view.x.AsDecimal;
            referenceCyDecimal    = view.y.AsDecimal;
            referenceScale        = scale;

            var rows = Mathf.Max(1, Mathf.CeilToInt(cachedOrbitIterations / (float)OrbitTextureWidth));
            EnsureOrbitTextures(rows);

            // Build Z_0 through Z_iterations. The shader uses Z_i for the
            // recurrence and Z_(i+1) for escape testing after delta updates.
            var pixelsHigh = new Color[OrbitTextureWidth * rows];
            var pixelsLow  = new Color[OrbitTextureWidth * rows];

            var zx = 0d;
            var zy = 0d;
            for (var i = 0; i < cachedOrbitIterations; i++)
            {
                var col = i % OrbitTextureWidth;
                var row = i / OrbitTextureWidth;
                var idx = row * OrbitTextureWidth + col;

                // Split each double into a float and residual before uploading
                // to the orbit textures.
                var zxHi = (float)zx;
                var zyHi = (float)zy;
                var zxLo = (float)(zx - zxHi);
                var zyLo = (float)(zy - zyHi);

                pixelsHigh[idx] = new Color(zxHi, zyHi, 0f, 1f);
                pixelsLow [idx] = new Color(zxLo, zyLo, 0f, 1f);

                var xt = zx * zx - zy * zy + centerX;
                zy = 2d * zx * zy + centerY;
                zx = xt;
            }

            orbitTexHigh.SetPixels(pixelsHigh);
            orbitTexHigh.Apply(false, false);
            orbitTexLow.SetPixels(pixelsLow);
            orbitTexLow.Apply(false, false);

            perturbationMaterial.SetTexture("_ReferenceOrbitTexHigh", orbitTexHigh);
            perturbationMaterial.SetTexture("_ReferenceOrbitTexLow",  orbitTexLow);
            perturbationMaterial.SetInt("_OrbitLength", cachedOrbitIterations);
        }

        private void EnsureOrbitTextures(int rows)
        {
            if (orbitTexHigh != null &&
                orbitTexHigh.width  == OrbitTextureWidth &&
                orbitTexHigh.height == rows)
                return;

            if (orbitTexHigh != null) UnityEngine.Object.Destroy(orbitTexHigh);
            if (orbitTexLow  != null) UnityEngine.Object.Destroy(orbitTexLow);

            var desc = new Func<Texture2D>(() => new Texture2D(
                OrbitTextureWidth, rows, TextureFormat.RGBAFloat, false, true)
            {
                wrapMode    = TextureWrapMode.Clamp,
                filterMode  = FilterMode.Point
            });
            orbitTexHigh = desc();
            orbitTexLow  = desc();
        }

        // ─── Shader constants ─────────────────────────────────────────────────

        private void UpdateMaterialConstants(in FractalView view, int texWidth, int texHeight)
        {
            // FIX: compute delta in decimal to avoid catastrophic cancellation.
            var dxDecimal = view.x.AsDecimal - referenceCxDecimal;
            var dyDecimal = view.y.AsDecimal - referenceCyDecimal;

            var dxHi = (float)(double)dxDecimal;
            var dyHi = (float)(double)dyDecimal;
            var dxLo = (float)((double)dxDecimal - dxHi);
            var dyLo = (float)((double)dyDecimal - dyHi);

            // Pass split delta so shader can reconstruct accurate perturbation offset.
            perturbationMaterial.SetVector("_CenterDeltaHigh", new Vector4(dxHi, dyHi, 0f, 0f));
            perturbationMaterial.SetVector("_CenterDeltaLow",  new Vector4(dxLo, dyLo, 0f, 0f));

            // Kept for shaders that still read the legacy single-precision uniform.
            perturbationMaterial.SetVector("_CenterDelta", new Vector4(dxHi, dyHi, 0f, 0f));

            perturbationMaterial.SetFloat("_Scale",       (float)view.scale.AsDouble);
            perturbationMaterial.SetFloat("_Iterations",  view.iterations);
            perturbationMaterial.SetFloat("_GlitchThreshold", GlitchThreshold);

            // FIX: pass aspect ratio so GPU UV mapping matches CPU kernel.
            var aspect = texHeight > 0 ? (float)texWidth / texHeight : 1f;
            perturbationMaterial.SetFloat("_Aspect", aspect);
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static Texture2D BuildPaletteTexture(Gradient gradient)
        {
            const int resolution = 256;
            var texture = new Texture2D(resolution, 1, TextureFormat.RGBA32, false, true)
            {
                wrapMode   = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (var i = 0; i < resolution; i++)
                texture.SetPixel(i, 0, gradient.Evaluate(i / (resolution - 1f)));

            texture.Apply(false, true);
            return texture;
        }

        private static NativeArray<Color32> BuildNativePalette(Gradient gradient, Allocator allocator)
        {
            const int resolution = 256;
            var palette = new NativeArray<Color32>(resolution, allocator);
            for (var i = 0; i < resolution; i++)
            {
                palette[i] = (Color32)gradient.Evaluate(i / (resolution - 1f));
            }

            return palette;
        }

        private void EnsureCpuTileBuffer(int requiredLength)
        {
            if (cpuTileBuffer != null && cpuTileBuffer.Length == requiredLength)
                return;
            cpuTileBuffer = new Color32[requiredLength];
        }

        public void Dispose()
        {
            if (nativeCpuTileBuffer.IsCreated)
            {
                nativeCpuTileBuffer.Dispose();
            }

            if (nativePalette.IsCreated)
            {
                nativePalette.Dispose();
            }
        }
    }
}
