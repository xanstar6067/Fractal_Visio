using System;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>Single-pass fp32 renderer used only while pixel spacing is safe for the GPU.</summary>
    internal sealed class FractalGpuRenderer : IDisposable
    {
        private const string ShaderName = "FractalVisio/MandelbrotFloat";
        private const int PaletteResolution = 256;

        private readonly Material material;
        private readonly Texture2D palette;

        public FractalGpuRenderer(Gradient gradient)
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                return;
            }

            material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            palette = BuildPalette(gradient);
            material.SetTexture("_PaletteTex", palette);
        }

        public bool IsAvailable => material != null;

        public void Render(in FractalView view, int iterations, RenderTexture target)
        {
            if (!IsAvailable || target == null)
            {
                return;
            }

            material.SetVector("_Center", new Vector4(
                (float)view.x.AsDouble,
                (float)view.y.AsDouble,
                0f,
                0f));
            material.SetFloat("_Scale", (float)view.scale.AsDouble);
            material.SetFloat("_Aspect", target.width / (float)Mathf.Max(1, target.height));
            material.SetFloat("_Rotation", (float)view.rotation);
            material.SetInt("_Iterations", Mathf.Max(1, iterations));
            Graphics.Blit(null, target, material, 0);
        }

        public void Dispose()
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }

            if (palette != null)
            {
                UnityEngine.Object.Destroy(palette);
            }
        }

        private static Texture2D BuildPalette(Gradient gradient)
        {
            var texture = new Texture2D(PaletteResolution, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Fractal Palette",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            for (var i = 0; i < PaletteResolution; i++)
            {
                texture.SetPixel(i, 0, gradient.Evaluate(i / (PaletteResolution - 1f)));
            }

            texture.Apply(false, true);
            return texture;
        }
    }
}
