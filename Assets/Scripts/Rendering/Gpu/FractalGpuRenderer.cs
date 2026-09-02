using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// Single-pass fp32 renderer, used only while pixel spacing is still safe for the GPU. It owns
    /// the shared uniforms - centre, span, aspect, rotation, budget, palette - and knows nothing
    /// about any particular fractal: the definition names the shader and sets its own uniforms.
    /// </summary>
    public sealed class FractalGpuRenderer : IDisposable
    {
        private const int PaletteResolution = 256;

        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private static readonly int IterationsId = Shader.PropertyToID("_Iterations");
        private static readonly int PaletteId = Shader.PropertyToID("_PaletteTex");

        private readonly Texture2D palette;

        // Keyed by shader name, misses included: Shader.Find is not cheap enough to call per frame.
        private readonly Dictionary<string, Material> materials = new();

        public FractalGpuRenderer(Gradient gradient)
        {
            palette = BuildPalette(gradient);
        }

        /// <summary>Whether this fractal can run on the GPU at all on this device.</summary>
        public bool Supports(IFractalDefinition definition)
        {
            return definition != null &&
                   (definition.SupportedPrecision & PrecisionTier.Float) != 0 &&
                   ResolveMaterial(definition.ShaderName) != null;
        }

        public void Render(
            IFractalDefinition definition,
            in FractalParameterSet parameters,
            in ViewState view,
            int iterations,
            RenderTexture target)
        {
            if (definition == null || target == null)
            {
                return;
            }

            var material = ResolveMaterial(definition.ShaderName);
            if (material == null)
            {
                return;
            }

            material.SetVector(CenterId, new Vector4(
                (float)view.x.AsDouble,
                (float)view.y.AsDouble,
                0f,
                0f));
            material.SetFloat(ScaleId, (float)view.scale.AsDouble);
            material.SetFloat(AspectId, target.width / (float)Mathf.Max(1, target.height));
            material.SetFloat(RotationId, (float)view.rotation);
            material.SetInt(IterationsId, Mathf.Max(1, iterations));

            definition.BindMaterial(material, parameters);

            Graphics.Blit(null, target, material, 0);
        }

        public void Dispose()
        {
            foreach (var material in materials.Values)
            {
                if (material != null)
                {
                    UnityEngine.Object.Destroy(material);
                }
            }

            materials.Clear();

            if (palette != null)
            {
                UnityEngine.Object.Destroy(palette);
            }
        }

        private Material ResolveMaterial(string shaderName)
        {
            if (string.IsNullOrEmpty(shaderName))
            {
                return null;
            }

            if (materials.TryGetValue(shaderName, out var cached))
            {
                return cached;
            }

            Material material = null;
            var shader = Shader.Find(shaderName);
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetTexture(PaletteId, palette);
            }

            materials[shaderName] = material;
            return material;
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
