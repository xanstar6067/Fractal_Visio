using System;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// What changed in the session. Consumers read the flags to decide how much work the change
    /// costs them: a palette change only remaps colours, a view change re-renders the fractal.
    /// </summary>
    [Flags]
    public enum SessionChange
    {
        None = 0,
        View = 1 << 0,
        Definition = 1 << 1,
        Parameters = 1 << 2,
        Palette = 1 << 3,
        Coloring = 1 << 4,
        Quality = 1 << 5
    }

    /// <summary>Iteration budget and the scale thresholds that pick a backend and a precision.</summary>
    public struct RenderQuality
    {
        public int SettledIterations;
        public int MaximumIterations;
        public double GpuMinimumScale;
        public double ExtendedPrecisionScale;
        public double MinimumScale;
        public double MaximumScale;

        public static RenderQuality Default => new RenderQuality
        {
            SettledIterations = 320,
            MaximumIterations = 2048,
            GpuMinimumScale = 2.5e-4d,
            ExtendedPrecisionScale = 1e-12d,
            MinimumScale = 1e-24d,
            MaximumScale = 4d
        }.Sanitized();

        public RenderQuality Sanitized()
        {
            var result = this;
            result.SettledIterations = Math.Max(32, result.SettledIterations);
            result.MaximumIterations = Math.Max(result.SettledIterations, result.MaximumIterations);
            result.GpuMinimumScale = Math.Max(1e-8d, result.GpuMinimumScale);
            result.ExtendedPrecisionScale = Math.Min(result.GpuMinimumScale, Math.Max(1e-20d, result.ExtendedPrecisionScale));
            result.MinimumScale = Math.Max(1e-28d, result.MinimumScale);
            result.MaximumScale = Math.Max(result.GpuMinimumScale, result.MaximumScale);
            return result;
        }
    }

    /// <summary>
    /// The single owner of everything the app can change about the picture. UI, modules and the
    /// presenter read it and call its setters; nobody drives the renderers directly. Every setter
    /// funnels through <see cref="SetView"/>, so clamping and the iteration budget are decided in
    /// exactly one place.
    ///
    /// Later stages add the active fractal definition, its parameters, palette and colouring here;
    /// the <see cref="SessionChange"/> flags already carry them.
    /// </summary>
    public sealed class FractalSession
    {
        private ViewState view;
        private RenderQuality quality;

        public FractalSession(in RenderQuality quality)
        {
            this.quality = quality.Sanitized();
            view = ViewState.Default;
            ApplyBudget(ref view);
        }

        public event Action<SessionChange> Changed;

        public ViewState View => view;
        public RenderQuality Quality => quality;

        public void SetQuality(in RenderQuality value)
        {
            quality = value.Sanitized();
            var next = view;
            ApplyBudget(ref next);
            view = next;
            Raise(SessionChange.Quality | SessionChange.View);
        }

        /// <summary>
        /// Replace the view. The scale is clamped and the iteration budget recomputed here, so no
        /// caller has to remember to do either.
        /// </summary>
        public void SetView(in ViewState value)
        {
            var next = value;
            var scale = Math.Clamp(next.scale.AsDouble, quality.MinimumScale, quality.MaximumScale);
            if (scale != next.scale.AsDouble)
            {
                next.scale = HighPrecision.FromDouble(scale);
            }

            ApplyBudget(ref next);
            if (Same(next, view))
            {
                return;
            }

            view = next;
            Raise(SessionChange.View);
        }

        /// <summary>Entry point for presets, bookmarks and restored state.</summary>
        public void SetCenter(decimal centerX, decimal centerY, decimal scale)
        {
            var next = view;
            next.x = new HighPrecision(centerX);
            next.y = new HighPrecision(centerY);
            next.scale = new HighPrecision(scale);
            SetView(next);
        }

        public void ResetView()
        {
            SetView(ViewState.Default);
        }

        private void ApplyBudget(ref ViewState target)
        {
            target.iterations = ResolveIterations(target.scale.AsDouble);
        }

        // One budget for every state. The iteration count never drops during a gesture:
        // responsiveness comes from coarse render passes, not fewer iterations (a reduced
        // budget was visibly changing the image).
        private int ResolveIterations(double scale)
        {
            var depth = Math.Max(0d, -Math.Log10(Math.Max(scale, 1e-28d)) - 3d);
            var depthBudget = depth * 96d + Math.Max(0d, depth - 6d) * 192d;
            return Mathf.Clamp(
                quality.SettledIterations + Mathf.RoundToInt((float)depthBudget),
                16,
                quality.MaximumIterations);
        }

        private static bool Same(in ViewState a, in ViewState b)
        {
            return a.x.Equals(b.x) &&
                   a.y.Equals(b.y) &&
                   a.scale.Equals(b.scale) &&
                   a.rotation == b.rotation &&
                   a.iterations == b.iterations;
        }

        private void Raise(SessionChange change)
        {
            Changed?.Invoke(change);
        }
    }
}
