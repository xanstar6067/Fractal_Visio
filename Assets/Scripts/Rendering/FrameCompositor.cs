using System;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// Assembles what the viewer sees out of rendered frames that were each computed for their own
    /// view. Nothing here recomputes or rewrites fractal pixels: every layer is sampled once,
    /// through the affine map from <see cref="FramePlacement"/>, straight into the display buffer.
    ///
    /// Up to three layers, in order: a wide, coarse frame that covers far more than the screen; the
    /// newest frame for the current view over it; and, over both, a retained earlier frame that is
    /// still the sharper picture, or is dissolving away. The wide one exists for exactly one job - a
    /// zoom-out asks to see area that the sharp frame never contained, and something correct has to
    /// be there while the new render arrives.
    /// </summary>
    public sealed class FrameCompositor : IDisposable
    {
        private const string ShaderName = "FractalVisio/FrameComposite";
        private const int BasePass = 0;
        private const int OverPass = 1;

        private static readonly int FrameUvRow0Id = Shader.PropertyToID("_FrameUvRow0");
        private static readonly int FrameUvRow1Id = Shader.PropertyToID("_FrameUvRow1");
        private static readonly int FallbackColorId = Shader.PropertyToID("_FallbackColor");
        private static readonly int LayerAlphaId = Shader.PropertyToID("_LayerAlpha");

        private readonly Material material;
        private RenderTexture target;

        public FrameCompositor(Color fallbackColor)
        {
            FallbackColor = fallbackColor;

            var shader = Shader.Find(ShaderName);
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
                material.SetColor(FallbackColorId, fallbackColor);
            }
        }

        /// <summary>Colour for display pixels no layer could cover. Follows the interior colour.</summary>
        public Color FallbackColor { get; private set; }

        public void SetFallbackColor(Color value)
        {
            if (material == null || FallbackColor == value)
            {
                return;
            }

            FallbackColor = value;
            material.SetColor(FallbackColorId, value);
        }

        /// <summary>The composed image, or null before the first successful compose.</summary>
        public Texture Texture => target;

        public bool IsSupported => material != null;

        /// <summary>
        /// Draw one frame over the whole display. <paramref name="wide"/> may be null, in which case
        /// anything <paramref name="main"/> does not cover shows <see cref="FallbackColor"/>.
        /// Returns false when nothing could be composed and the caller should keep its old output.
        /// </summary>
        /// <param name="retained">
        /// Optional third layer drawn over <paramref name="main"/> at <paramref name="retainedAlpha"/>:
        /// an earlier frame that is still sharper than the newest one, or one dissolving away. See
        /// <see cref="RetainedFrame"/>.
        /// </param>
        public bool Compose(
            Viewport display,
            Texture main,
            in FramePlacement mainPlacement,
            Texture wide,
            in FramePlacement widePlacement,
            Texture retained = null,
            in FramePlacement retainedPlacement = default,
            float retainedAlpha = 0f)
        {
            if (material == null || main == null || !mainPlacement.IsValid)
            {
                return false;
            }

            if (!EnsureTarget(display.VisibleWidth, display.VisibleHeight))
            {
                return false;
            }

            var hasWide = wide != null && widePlacement.IsValid;
            if (hasWide)
            {
                Draw(wide, widePlacement, BasePass, 1f);
                Draw(main, mainPlacement, OverPass, 1f);
            }
            else
            {
                Draw(main, mainPlacement, BasePass, 1f);
            }

            if (retained != null && retainedPlacement.IsValid && retainedAlpha > 0.001f)
            {
                Draw(retained, retainedPlacement, OverPass, Mathf.Clamp01(retainedAlpha));
            }

            return true;
        }

        public void Dispose()
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }

            ReleaseTarget();
        }

        private void Draw(Texture frame, in FramePlacement placement, int pass, float alpha)
        {
            material.SetVector(FrameUvRow0Id, placement.UvRow0);
            material.SetVector(FrameUvRow1Id, placement.UvRow1);
            material.SetFloat(LayerAlphaId, alpha);
            Graphics.Blit(frame, target, material, pass);
        }

        private bool EnsureTarget(int width, int height)
        {
            var safeWidth = Mathf.Max(16, width);
            var safeHeight = Mathf.Max(16, height);
            if (target != null && target.width == safeWidth && target.height == safeHeight)
            {
                return true;
            }

            ReleaseTarget();
            target = new RenderTexture(safeWidth, safeHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Fractal Composite",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

            return target.Create();
        }

        private void ReleaseTarget()
        {
            if (target == null)
            {
                return;
            }

            target.Release();
            UnityEngine.Object.Destroy(target);
            target = null;
        }
    }
}
