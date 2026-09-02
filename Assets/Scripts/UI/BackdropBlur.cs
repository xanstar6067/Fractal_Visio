using System;
using UnityEngine;

namespace FractalVisio.UI
{
    /// <summary>
    /// Keeps a small blurred copy of whatever is on screen, for panels to show through themselves.
    ///
    /// This is a real backdrop blur rather than a painted-on gradient, and it is affordable because
    /// the background is a single texture the app already owns: crop it to the visible rectangle,
    /// downscale to a few hundred pixels, and run a separable gaussian over that. Panels then sample
    /// this one texture through their own screen rectangle.
    /// </summary>
    public sealed class BackdropBlur : IDisposable
    {
        private const string ShaderName = "FractalVisio/UiBackdropBlur";
        private const int LongEdge = 384;

        private static readonly int DirectionId = Shader.PropertyToID("_BlurDirection");

        private readonly Material material;
        private RenderTexture blurred;

        public BackdropBlur()
        {
            var shader = Shader.Find(ShaderName);
            if (shader != null && shader.isSupported)
            {
                material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            }
        }

        /// <summary>The blurred image, covering exactly what the screen shows. Null until refreshed.</summary>
        public Texture Texture => blurred;

        public void Refresh(Texture source, Rect uvRect)
        {
            if (source == null)
            {
                return;
            }

            EnsureTarget();
            if (blurred == null)
            {
                return;
            }

            var descriptor = blurred.descriptor;
            var cropped = RenderTexture.GetTemporary(descriptor);

            // Crop to the visible sub-rectangle and downscale in one blit: the render buffers carry
            // an overscan margin that the viewer never sees, and it must not bleed into the glass.
            Graphics.Blit(
                source,
                cropped,
                new Vector2(uvRect.width, uvRect.height),
                new Vector2(uvRect.x, uvRect.y));

            if (material == null)
            {
                Graphics.Blit(cropped, blurred);
                RenderTexture.ReleaseTemporary(cropped);
                return;
            }

            var scratch = RenderTexture.GetTemporary(descriptor);

            // Two passes of increasing spread: one is still visibly ringed on a fractal edge, two
            // read as frosted glass. Both run at 384 pixels, so the whole thing is fractions of a
            // millisecond.
            BlurStep(cropped, scratch, new Vector4(1.6f, 0f, 0f, 0f));
            BlurStep(scratch, cropped, new Vector4(0f, 1.6f, 0f, 0f));
            BlurStep(cropped, scratch, new Vector4(3.2f, 0f, 0f, 0f));
            BlurStep(scratch, blurred, new Vector4(0f, 3.2f, 0f, 0f));

            RenderTexture.ReleaseTemporary(scratch);
            RenderTexture.ReleaseTemporary(cropped);
        }

        public void Dispose()
        {
            if (material != null)
            {
                UnityEngine.Object.Destroy(material);
            }

            ReleaseTarget();
        }

        private void BlurStep(Texture source, RenderTexture destination, Vector4 direction)
        {
            material.SetVector(DirectionId, direction);
            Graphics.Blit(source, destination, material, 0);
        }

        private void EnsureTarget()
        {
            var width = Mathf.Max(1, Screen.width);
            var height = Mathf.Max(1, Screen.height);
            var scale = LongEdge / (float)Mathf.Max(width, height);
            var targetWidth = Mathf.Max(16, Mathf.RoundToInt(width * scale));
            var targetHeight = Mathf.Max(16, Mathf.RoundToInt(height * scale));

            if (blurred != null && blurred.width == targetWidth && blurred.height == targetHeight)
            {
                return;
            }

            ReleaseTarget();
            blurred = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32)
            {
                name = "Ui Backdrop Blur",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };
            blurred.Create();
        }

        private void ReleaseTarget()
        {
            if (blurred == null)
            {
                return;
            }

            blurred.Release();
            UnityEngine.Object.Destroy(blurred);
            blurred = null;
        }
    }
}
