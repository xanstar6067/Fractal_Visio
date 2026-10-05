using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// Single-pass fp32 renderer, used only while pixel spacing is still safe for the GPU. It owns
    /// the shared uniforms - centre, span, aspect, rotation, budget, palette, colouring - and knows
    /// nothing about any particular fractal: the definition names the shader and sets its own
    /// uniforms.
    ///
    /// The colouring uniforms exist so this path and the CPU mapper produce the same picture. They
    /// have to agree: the backend switches under the viewer mid-zoom, and a palette that shifts at
    /// that moment reads as a glitch.
    /// </summary>
    public sealed class FractalGpuRenderer : IDisposable
    {
        private static readonly int CenterId = Shader.PropertyToID("_Center");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int AspectId = Shader.PropertyToID("_Aspect");
        private static readonly int RotationId = Shader.PropertyToID("_Rotation");
        private static readonly int IterationsId = Shader.PropertyToID("_Iterations");
        private static readonly int PaletteId = Shader.PropertyToID("_PaletteTex");
        private static readonly int ColorCycleId = Shader.PropertyToID("_ColorCycle");
        private static readonly int ColorOffsetId = Shader.PropertyToID("_ColorOffset");
        private static readonly int ColorSmoothId = Shader.PropertyToID("_ColorSmooth");
        private static readonly int ColorLogarithmicId = Shader.PropertyToID("_ColorLogarithmic");
        private static readonly int InteriorColorId = Shader.PropertyToID("_InteriorColor");
        private static readonly int ReliefLightId = Shader.PropertyToID("_ReliefLight");
        private static readonly int ReliefHalfId = Shader.PropertyToID("_ReliefHalf");
        private static readonly int ReliefToneId = Shader.PropertyToID("_ReliefTone");

        /// <summary>
        /// Shader variant that carries the orbit's derivative. A keyword rather than a uniform: the
        /// derivative costs the inner loop, and the picture without relief must not pay for it.
        /// </summary>
        private const string ReliefKeyword = "FRACTAL_RELIEF";

        private readonly Texture2D palette;
        private readonly Color32[] palettePixels = new Color32[PaletteData.Resolution];

        // Keyed by shader name, misses included: Shader.Find is not cheap enough to call per frame.
        private readonly Dictionary<string, Material> materials = new();

        private PaletteData paletteData;
        private ColoringSettings coloring = ColoringSettings.Default;

        public FractalGpuRenderer()
        {
            palette = new Texture2D(PaletteData.Resolution, 1, TextureFormat.RGBA32, false, true)
            {
                name = "Fractal Palette",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave
            };

            SetColoring(PaletteLibrary.Default, ColoringSettings.Default);
        }

        /// <summary>Whether this fractal can run on the GPU at all on this device.</summary>
        public bool Supports(IFractalDefinition definition)
        {
            return definition != null &&
                   (definition.SupportedPrecision & PrecisionTier.Float) != 0 &&
                   ResolveMaterial(definition.ShaderName) != null;
        }

        /// <summary>
        /// Point the shader at a palette. Rewrites the one palette texture in place, so every
        /// material that already holds a reference to it follows without being touched.
        /// </summary>
        public void SetColoring(PaletteData value, in ColoringSettings settings)
        {
            coloring = settings;

            var next = value ?? PaletteLibrary.Default;
            if (ReferenceEquals(next, paletteData))
            {
                return;
            }

            paletteData = next;
            for (var i = 0; i < palettePixels.Length; i++)
            {
                palettePixels[i] = next[i * next.Count / palettePixels.Length];
            }

            palette.SetPixels32(palettePixels);
            palette.Apply(false, false);
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
            material.SetFloat(ColorCycleId, Mathf.Max(1f, coloring.CycleLength));
            material.SetFloat(ColorOffsetId, coloring.Offset);
            material.SetFloat(ColorSmoothId, coloring.Smooth ? 1f : 0f);
            material.SetFloat(ColorLogarithmicId, coloring.Mode == ColoringMode.Logarithmic ? 1f : 0f);
            material.SetColor(InteriorColorId, coloring.InteriorColor);

            if (coloring.Relief)
            {
                var light = ReliefLight.From(coloring);
                material.SetVector(ReliefLightId, new Vector4(light.LightX, light.LightY, light.LightZ, light.Slope));
                material.SetVector(ReliefHalfId, new Vector4(light.HalfX, light.HalfY, light.HalfZ, light.Shininess));
                material.SetVector(ReliefToneId, new Vector4(ReliefLight.Ambient, light.InverseFlat, light.Specular, 0f));
                material.EnableKeyword(ReliefKeyword);
            }
            else
            {
                material.DisableKeyword(ReliefKeyword);
            }

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
            else
            {
                // Only ever seen on a device - the editor finds every shader. The fractal still draws,
                // on the CPU alone, and its gallery card has no picture; this line in the log is what
                // says why. Misses are cached, so it is logged once per renderer.
                Debug.LogWarning(shader == null
                    ? "Shader '" + shaderName + "' is not in this build: it is missing from Always Included Shaders."
                    : "Shader '" + shaderName + "' is not supported by this GPU.");
            }

            materials[shaderName] = material;
            return material;
        }
    }
}
