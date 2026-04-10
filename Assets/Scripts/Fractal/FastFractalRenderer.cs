using UnityEngine;

namespace FractalVisio.Fractal
{
    public sealed class FastFractalRenderer : IFractalRenderer
    {
        private const string ShaderName = "FractalVisio/MandelbrotFloat";
        private const int PaletteResolution = 256;

        private readonly Gradient gradient;
        private readonly Material fractalMaterial;
        private readonly Texture2D paletteTexture;
        private readonly bool gpuAvailable;
        private Color32[] cpuTileBuffer;

        public FastFractalRenderer(Gradient gradient)
        {
            this.gradient = gradient;

            var shader = Shader.Find(ShaderName);
            if (shader != null && shader.isSupported)
            {
                fractalMaterial = new Material(shader);
                fractalMaterial.hideFlags = HideFlags.HideAndDontSave;
                paletteTexture = BuildPaletteTexture(gradient);
                fractalMaterial.SetTexture("_PaletteTex", paletteTexture);
                gpuAvailable = true;
            }
        }

        public RenderMode Mode => RenderMode.Fast;

        public void Render(in FractalRenderRequest request, Texture target, TileDescriptor tile)
        {
            if (gpuAvailable && target is RenderTexture renderTexture)
            {
                fractalMaterial.SetVector("_Center", new Vector4((float)request.View.x.AsDouble, (float)request.View.y.AsDouble, 0f, 0f));
                fractalMaterial.SetFloat("_Scale", (float)request.View.scale.AsDouble);
                fractalMaterial.SetFloat("_Iterations", request.View.iterations);
                Graphics.Blit(null, renderTexture, fractalMaterial, 0);
                return;
            }

            if (target is not Texture2D texture2D)
            {
                return;
            }

            var sampleStep = request.IsInteracting ? 2 : 1;
            EnsureCpuTileBuffer(tile.PixelRect.width * tile.PixelRect.height);
            FractalCpuKernels.RenderMandelbrotTile(cpuTileBuffer, texture2D.width, texture2D.height, tile, request.View, request.View.iterations, sampleStep, gradient);
            FractalCpuKernels.BlitTile(texture2D, tile, cpuTileBuffer);
        }

        private static Texture2D BuildPaletteTexture(Gradient gradient)
        {
            var texture = new Texture2D(PaletteResolution, 1, TextureFormat.RGBA32, false, true);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            for (var i = 0; i < PaletteResolution; i++)
            {
                var t = i / (PaletteResolution - 1f);
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
