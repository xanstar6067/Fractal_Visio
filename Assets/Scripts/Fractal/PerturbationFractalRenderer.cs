using System;
using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Hybrid perturbation renderer:
    /// 1) CPU builds reference orbit rarely and caches it.
    /// 2) GPU renders delta field in full screen pass.
    /// 3) Optional CPU fallback repaints problematic tiles.
    /// </summary>
    public sealed class PerturbationFractalRenderer : IFractalRenderer
    {
        private const string ShaderName = "FractalVisio/MandelbrotPerturbation";
        private const int OrbitTextureWidth = 1024;
        private const double OrbitReuseCenterFactor = 0.20d;
        private const double OrbitReuseScaleFactor = 0.20d;

        private readonly Gradient gradient;
        private readonly bool enableCpuFallback;
        private readonly List<TileDescriptor> fallbackTiles = new();

        private Material perturbationMaterial;
        private Texture2D paletteTexture;
        private Texture2D orbitTexture;
        private bool gpuAvailable;
        private Color32[] cpuTileBuffer;

        private double referenceCx;
        private double referenceCy;
        private double referenceScale;
        private int cachedOrbitIterations;

        public PerturbationFractalRenderer(Gradient gradient, bool enableCpuFallback = false)
        {
            this.gradient = gradient;
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
        }

        public RenderMode Mode => RenderMode.Perturbation;

        public bool RenderToGpu(in FractalRenderRequest request, RenderTexture target)
        {
            if (!gpuAvailable || target == null)
            {
                return false;
            }

            EnsureReferenceOrbit(request.View);
            UpdateMaterialConstants(request.View);
            Graphics.Blit(null, target, perturbationMaterial, 0);
            return true;
        }

        public IReadOnlyList<TileDescriptor> BuildFallbackTiles(in FractalRenderRequest request, int width, int height, int tileSize)
        {
            fallbackTiles.Clear();
            if (!enableCpuFallback)
            {
                return fallbackTiles;
            }
            var tileIndex = 0;

            // Accuracy-first fallback mode: repaint full frame by tiles on CPU.
            // This avoids perturbation divergence artifacts that can look like blur/structure averaging.
            for (var y = 0; y < height; y += tileSize)
            {
                for (var x = 0; x < width; x += tileSize)
                {
                    var rectWidth = Mathf.Min(tileSize, width - x);
                    var rectHeight = Mathf.Min(tileSize, height - y);
                    fallbackTiles.Add(new TileDescriptor(new RectInt(x, y, rectWidth, rectHeight), tileIndex++));
                }
            }

            return fallbackTiles;
        }

        public void Render(in FractalRenderRequest request, Texture target, TileDescriptor tile)
        {
            if (target is not Texture2D texture2D)
            {
                return;
            }

            var iterationBudget = request.View.iterations + (request.View.iterations / 2);
            EnsureCpuTileBuffer(tile.PixelRect.width * tile.PixelRect.height);
            FractalCpuKernels.RenderMandelbrotTile(cpuTileBuffer, texture2D.width, texture2D.height, tile, request.View, iterationBudget, 1, gradient);
            FractalCpuKernels.BlitTile(texture2D, tile, cpuTileBuffer);
        }

        private void EnsureReferenceOrbit(in FractalView view)
        {
            var centerX = view.x.AsDouble;
            var centerY = view.y.AsDouble;
            var scale = view.scale.AsDouble;
            var needsRebuild = orbitTexture == null ||
                               cachedOrbitIterations != view.iterations ||
                               Math.Abs(referenceCx - centerX) > Math.Max(scale * OrbitReuseCenterFactor, 1e-18d) ||
                               Math.Abs(referenceCy - centerY) > Math.Max(scale * OrbitReuseCenterFactor, 1e-18d) ||
                               Math.Abs(referenceScale - scale) > Math.Max(scale * OrbitReuseScaleFactor, 1e-18d);

            if (!needsRebuild)
            {
                return;
            }

            cachedOrbitIterations = Mathf.Max(4, view.iterations);
            referenceCx = centerX;
            referenceCy = centerY;
            referenceScale = scale;

            var orbitPixelCount = Mathf.CeilToInt(cachedOrbitIterations / (float)OrbitTextureWidth);
            orbitPixelCount = Mathf.Max(1, orbitPixelCount);

            if (orbitTexture == null || orbitTexture.width != OrbitTextureWidth || orbitTexture.height != orbitPixelCount)
            {
                orbitTexture = new Texture2D(OrbitTextureWidth, orbitPixelCount, TextureFormat.RGBAFloat, false, true)
                {
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Point
                };
            }

            var orbitPixels = new Color[OrbitTextureWidth * orbitPixelCount];
            var zx = 0d;
            var zy = 0d;
            for (var i = 0; i < cachedOrbitIterations; i++)
            {
                var x = i % OrbitTextureWidth;
                var y = i / OrbitTextureWidth;
                orbitPixels[y * OrbitTextureWidth + x] = new Color((float)zx, (float)zy, 0f, 0f);

                var xt = zx * zx - zy * zy + referenceCx;
                zy = 2d * zx * zy + referenceCy;
                zx = xt;
            }

            orbitTexture.SetPixels(orbitPixels);
            orbitTexture.Apply(false, false);
            perturbationMaterial.SetTexture("_ReferenceOrbitTex", orbitTexture);
            perturbationMaterial.SetInt("_OrbitLength", cachedOrbitIterations);
        }

        private void UpdateMaterialConstants(in FractalView view)
        {
            var centerDeltaX = view.x.AsDouble - referenceCx;
            var centerDeltaY = view.y.AsDouble - referenceCy;
            perturbationMaterial.SetVector("_CenterDelta", new Vector4((float)centerDeltaX, (float)centerDeltaY, 0f, 0f));
            perturbationMaterial.SetFloat("_Scale", (float)view.scale.AsDouble);
            perturbationMaterial.SetFloat("_Iterations", view.iterations);
        }

        private static Texture2D BuildPaletteTexture(Gradient gradient)
        {
            const int resolution = 256;
            var texture = new Texture2D(resolution, 1, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            for (var i = 0; i < resolution; i++)
            {
                var t = i / (resolution - 1f);
                texture.SetPixel(i, 0, gradient.Evaluate(t));
            }

            texture.Apply(false, true);
            return texture;
        }

        private void EnsureCpuTileBuffer(int requiredLength)
        {
            if (cpuTileBuffer != null && cpuTileBuffer.Length == requiredLength)
            {
                return;
            }

            cpuTileBuffer = new Color32[requiredLength];
        }
    }
}
