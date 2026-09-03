using System;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// What changed in the session. Consumers read the flags to decide how much work the change
    /// costs them: a palette change only remaps the escape buffer, a view change re-renders the
    /// fractal.
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
        Quality = 1 << 5,

        /// <summary>Interface scale and anything else about the UI rather than the picture.</summary>
        Interface = 1 << 6
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

        /// <summary>
        /// Render resolution as a fraction of the screen, or 0 for "let the device profile decide".
        /// Explicit values bypass the profile's long-edge cap: choosing 100% is a request for the
        /// screen's own resolution, and silently rendering less than that would make the setting a
        /// lie.
        /// </summary>
        public float RenderScale;

        public static RenderQuality Default => new RenderQuality
        {
            SettledIterations = 320,
            MaximumIterations = 2048,
            GpuMinimumScale = 2.5e-4d,
            ExtendedPrecisionScale = 1e-12d,
            MinimumScale = 1e-24d,
            MaximumScale = 4d,
            RenderScale = 0f
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
            result.RenderScale = result.RenderScale <= 0f ? 0f : Mathf.Clamp(result.RenderScale, 0.25f, 1f);
            return result;
        }
    }

    /// <summary>
    /// The single owner of everything the app can change about the picture. UI, modules and the
    /// presenter read it and call its setters; nobody drives the renderers directly. Every setter
    /// funnels through <see cref="SetView"/>, so clamping and the iteration budget are decided in
    /// exactly one place.
    ///
    /// It owns the active fractal definition and its parameters, the palette and colouring, and -
    /// until stage 7 gives them a home of their own - the interface settings.
    /// </summary>
    public sealed class FractalSession
    {
        private ViewState view;
        private RenderQuality quality;
        private IFractalDefinition definition;
        private FractalParameterSet parameters;
        private PaletteData palette;
        private ColoringSettings coloring;
        private InterfaceSettings interfaceSettings;

        public FractalSession(IFractalDefinition definition, in RenderQuality quality)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.quality = quality.Sanitized();
            parameters = FractalParameterSet.Defaults(definition.Parameters);
            palette = PaletteLibrary.Default;
            coloring = ColoringSettings.Default;
            interfaceSettings = InterfaceSettings.Default;
            view = definition.DefaultView;
            ApplyBudget(ref view);
        }

        public event Action<SessionChange> Changed;

        public ViewState View => view;
        public RenderQuality Quality => quality;

        /// <summary>The active fractal. Everything fractal-specific goes through it.</summary>
        public IFractalDefinition Definition => definition;

        public FractalParameterSet Parameters => parameters;

        public PaletteData Palette => palette;

        public ColoringSettings Coloring => coloring;

        public InterfaceSettings Interface => interfaceSettings;

        /// <summary>Switch fractal: view and parameters return to that fractal's own defaults.</summary>
        public void SetDefinition(IFractalDefinition value)
        {
            if (value == null || ReferenceEquals(value, definition))
            {
                return;
            }

            definition = value;
            parameters = FractalParameterSet.Defaults(value.Parameters);
            view = value.DefaultView;
            ApplyBudget(ref view);
            Raise(SessionChange.Definition | SessionChange.Parameters | SessionChange.View);
        }

        public void SetParameter(string key, double value)
        {
            var index = parameters.IndexOf(key);
            if (index < 0)
            {
                return;
            }

            var next = parameters.With(key, value);
            if (next[index] == parameters[index])
            {
                return;
            }

            parameters = next;
            Raise(SessionChange.Parameters);
        }

        /// <summary>
        /// Change the colour ramp. Deliberately its own flag: this is a remap of the escape buffer
        /// the renderer already holds, not a reason to compute the fractal again.
        /// </summary>
        public void SetPalette(PaletteData value)
        {
            if (value == null || ReferenceEquals(value, palette))
            {
                return;
            }

            palette = value;
            Raise(SessionChange.Palette);
        }

        public void SetColoring(in ColoringSettings value)
        {
            var next = value.Sanitized();
            if (next.Equals(coloring))
            {
                return;
            }

            coloring = next;
            Raise(SessionChange.Coloring);
        }

        public void SetInterface(in InterfaceSettings value)
        {
            var next = value.Sanitized();
            if (next.Equals(interfaceSettings))
            {
                return;
            }

            interfaceSettings = next;
            Raise(SessionChange.Interface);
        }

        public void SetQuality(in RenderQuality value)
        {
            quality = value.Sanitized();
            var next = view;
            ApplyBudget(ref next);
            view = next;
            Raise(SessionChange.Quality | SessionChange.View);
        }

        /// <summary>Render resolution as a fraction of the screen; 0 hands the choice back to the device profile.</summary>
        public void SetRenderScale(float value)
        {
            var next = quality;
            next.RenderScale = value;
            next = next.Sanitized();
            if (Mathf.Approximately(next.RenderScale, quality.RenderScale))
            {
                return;
            }

            quality = next;
            Raise(SessionChange.Quality);
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
            SetView(definition.DefaultView);
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
