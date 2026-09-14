using System;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// A GPU copy of a frame that has just been replaced, kept on top of its successor for as long
    /// as it is the better picture, then dissolved.
    ///
    /// Two jobs, one mechanism. While the view moves, renders are cut short to keep them fresh, so a
    /// new frame is often coarser than the one it replaces - a 4x4 pass of the current view against
    /// a full-detail frame of a nearby one. Swapping them outright makes the picture visibly worse
    /// every time something new arrives. Kept on top, the old frame goes on showing its detail
    /// wherever it still covers, and the new one fills in only where the old one runs out. Once a
    /// newer frame is at least as sharp - or whenever a frame is simply refined in place - the copy
    /// fades out over <see cref="FadeSeconds"/> instead of popping, which is what makes a progressive
    /// render read as developing rather than jumping.
    ///
    /// "Sharp" is compared as <b>sample spacing on the plane</b>: a frame's step times its view's
    /// scale. That is independent of where the viewer is now, so a magnified old frame and a fresh
    /// coarse one are compared fairly, and it is all the information the decision needs.
    /// </summary>
    public sealed class RetainedFrame : IDisposable
    {
        public const float FadeSeconds = 0.15f;

        private RenderTexture texture;
        private double fadeStart;
        private bool holding;

        public bool IsActive { get; private set; }

        public ViewState View { get; private set; }

        public double Aspect { get; private set; } = 1d;

        /// <summary>Sample spacing on the plane (step x scale). Smaller is sharper.</summary>
        public double Spacing { get; private set; }

        /// <summary>True while it is the sharper picture and stays fully on top.</summary>
        public bool IsHolding => IsActive && holding;

        public Texture Texture => IsActive ? texture : null;

        /// <summary>Plane sample spacing of a frame of <paramref name="view"/> rendered at <paramref name="step"/>.</summary>
        public static double SpacingOf(in ViewState view, int step) => Math.Max(1, step) * Math.Abs(view.scale.AsDouble);

        /// <summary>Copy <paramref name="source"/> now, before it is overwritten.</summary>
        public void Capture(Texture source, in ViewState view, double aspect, double spacing, bool hold, double now)
        {
            if (source == null || !EnsureTexture(source.width, source.height))
            {
                Release();
                return;
            }

            Graphics.Blit(source, texture);
            View = view;
            Aspect = aspect;
            Spacing = spacing;
            IsActive = true;
            holding = hold;
            fadeStart = now;
        }

        /// <summary>Stop holding and begin the dissolve, if it is not already dissolving.</summary>
        public void BeginFade(double now)
        {
            if (!IsActive || !holding)
            {
                return;
            }

            holding = false;
            fadeStart = now;
        }

        /// <summary>Current opacity; deactivates itself once the dissolve has finished.</summary>
        public float Alpha(double now)
        {
            if (!IsActive)
            {
                return 0f;
            }

            if (holding)
            {
                return 1f;
            }

            var alpha = 1f - (float)((now - fadeStart) / FadeSeconds);
            if (alpha <= 0f)
            {
                IsActive = false;
                return 0f;
            }

            return alpha;
        }

        /// <summary>Forget the copy at once - it shows something no longer true (another fractal, other colours).</summary>
        public void Release()
        {
            IsActive = false;
            holding = false;
        }

        public void Dispose()
        {
            Release();
            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
                texture = null;
            }
        }

        private bool EnsureTexture(int width, int height)
        {
            if (texture != null && texture.width == width && texture.height == height)
            {
                return true;
            }

            if (texture != null)
            {
                texture.Release();
                UnityEngine.Object.Destroy(texture);
            }

            texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
            {
                name = "Fractal Retained Frame",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false
            };

            return texture.Create();
        }
    }
}
