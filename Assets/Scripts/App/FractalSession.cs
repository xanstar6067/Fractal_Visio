using System;
using System.Collections.Generic;
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
        Interface = 1 << 6,

        /// <summary>A 3D scene's camera.</summary>
        Camera = 1 << 7,

        /// <summary>A 3D scene's look: shading, colouring source, light. Unlike the plane's colouring, a new render.</summary>
        Look = 1 << 8
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
        /// Deepest scale for a fractal with <see cref="PrecisionTier.Arbitrary"/>. The reference orbit
        /// could go further; the pixel offsets cannot - they are fp64, and near 1e-300 the offset of one
        /// pixel leaves the normal double range. (The WPF engine continues with FloatExp offsets.)
        /// </summary>
        public const double ArbitraryMinimumScale = 1e-280d;

        /// <summary>
        /// Iteration cap for those depths. A minibrot 1e-100 down takes tens of thousands of
        /// iterations; <see cref="MaximumIterations"/> is the budget of the ordinary range.
        /// </summary>
        public const int ArbitraryMaximumIterations = 200000;

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
    ///
    /// What is on screen is a <see cref="Mode"/>: a plane fractal or a 3D scene (<see cref="Scene"/>,
    /// with its <see cref="Camera"/> and <see cref="Look"/>). While a scene is open
    /// <see cref="Definition"/> stays the last plane fractal - never null, so nothing that only a
    /// plane fractal uses has to guard against it - and <see cref="Parameters"/> are the scene's.
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
        private double displayAspect = 1d;
        private ISceneDefinition scene;
        private SceneCamera camera;
        private SceneLook look = SceneLook.Default;

        /// <param name="displayAspect">Width over height of the screen, for framing the default view; see <see cref="SetDisplayAspect"/>.</param>
        public FractalSession(IFractalDefinition definition, in RenderQuality quality, double displayAspect = 1d)
        {
            this.definition = definition ?? throw new ArgumentNullException(nameof(definition));
            this.quality = quality.Sanitized();
            SetDisplayAspect(displayAspect);
            parameters = FractalParameterSet.Defaults(definition.Parameters);
            palette = PaletteLibrary.Default;
            coloring = ColoringSettings.Default;
            interfaceSettings = InterfaceSettings.Default;
            view = DefaultView;
            ApplyBudget(ref view);
        }

        public event Action<SessionChange> Changed;

        public ViewState View => view;
        public RenderQuality Quality => quality;

        /// <summary>
        /// The plane fractal - the one on screen, or while a scene is open the last one. Everything
        /// specific to plane fractals goes through it.
        /// </summary>
        public IFractalDefinition Definition => definition;

        /// <summary>What is on screen: <see cref="Scene"/> while one is open, else <see cref="Definition"/>.</summary>
        public IExplorationMode Mode => scene != null ? scene : definition;

        /// <summary>The 3D scene on screen, or null while a plane fractal is.</summary>
        public ISceneDefinition Scene => scene;

        public bool IsScene => scene != null;

        /// <summary>The scene's camera. Meaningless while no scene is open.</summary>
        public SceneCamera Camera => camera;

        /// <summary>How scenes are shaded and coloured. Kept across scenes, as the palette is across fractals.</summary>
        public SceneLook Look => look;

        /// <summary>
        /// Where the scene's camera starts on this screen: its <see cref="ISceneDefinition.DefaultCamera"/>,
        /// backed away on a screen held upright as a plane fractal's default view is widened.
        /// </summary>
        public SceneCamera DefaultCamera => scene != null ? FittedCamera(scene.DefaultCamera) : camera;

        /// <summary>
        /// How far in from the start, as the readouts print it: x1 at the default view, x10 at a view
        /// ten times closer. A scene's is the default camera's distance over its camera's.
        /// </summary>
        public double Zoom
        {
            get
            {
                if (scene != null)
                {
                    var distance = (double)camera.Distance;
                    return distance > 0d ? DefaultCamera.Distance / distance : 1d;
                }

                var scale = view.scale.AsDouble;
                return scale > 0d ? DefaultView.scale.AsDouble / scale : 1d;
            }
        }

        public FractalParameterSet Parameters => parameters;

        public PaletteData Palette => palette;

        public ColoringSettings Coloring => coloring;

        public InterfaceSettings Interface => interfaceSettings;

        /// <summary>
        /// Where the active fractal starts on this screen: its <see cref="IFractalDefinition.DefaultView"/>,
        /// widened on a screen held upright. Also the reference "x1" of a zoom readout.
        /// </summary>
        public ViewState DefaultView => Fitted(definition.DefaultView);

        /// <summary>
        /// Furthest out a view may go on this screen: <see cref="RenderQuality.MaximumScale"/> as a
        /// width when the screen is held upright, like the defaults - or the fitted default view
        /// itself would be clamped, and the first pinch would jump in. Gestures clamp to this too.
        /// </summary>
        public double MaximumScale => displayAspect >= 1d ? quality.MaximumScale : quality.MaximumScale / displayAspect;

        /// <summary>
        /// Furthest in a view may go: <see cref="RenderQuality.ArbitraryMinimumScale"/> for a fractal whose
        /// reference orbit takes any number of digits (<see cref="PrecisionTier.Arbitrary"/>),
        /// <see cref="RenderQuality.MinimumScale"/>, where double-double runs out, for one with a deep
        /// kernel, else <see cref="RenderQuality.ExtendedPrecisionScale"/>: an fp64-only fractal (the
        /// Simonobrot) turns to blocks of equal pixels below it. Gestures clamp to this too.
        /// </summary>
        public double MinimumScale => SupportsArbitraryDepth
            ? RenderQuality.ArbitraryMinimumScale
            : SupportsExtendedDepth ? quality.MinimumScale : quality.ExtendedPrecisionScale;

        private bool SupportsArbitraryDepth => (definition.SupportedPrecision & PrecisionTier.Arbitrary) != 0;

        private bool SupportsExtendedDepth =>
            (definition.SupportedPrecision & (PrecisionTier.DoubleDouble | PrecisionTier.Perturbation)) != 0;

        /// <summary>
        /// Width over height of the screen the picture is shown on. A definition's default view is
        /// framed as a height, for a screen at least as wide as it is tall; on a phone held upright
        /// the same height leaves the sides of the set cut off. Defaults are therefore widened until
        /// that height fits across the width instead. Only defaults: a view the user made, saved or
        /// restored is never refit, and a new aspect changes nothing until the next reset.
        /// </summary>
        public void SetDisplayAspect(double aspect)
        {
            if (aspect > 0d && !double.IsNaN(aspect) && !double.IsInfinity(aspect))
            {
                displayAspect = aspect;
            }
        }

        /// <summary>Open a plane fractal or a scene, whichever <paramref name="value"/> is.</summary>
        public void SetMode(IExplorationMode value)
        {
            if (value is ISceneDefinition sceneValue)
            {
                SetScene(sceneValue);
            }
            else if (value is IFractalDefinition definitionValue)
            {
                SetDefinition(definitionValue);
            }
        }

        /// <summary>Switch fractal: view and parameters return to that fractal's own defaults.</summary>
        public void SetDefinition(IFractalDefinition value)
        {
            if (value == null || (scene == null && ReferenceEquals(value, definition)))
            {
                return;
            }

            scene = null;
            definition = value;
            parameters = FractalParameterSet.Defaults(value.Parameters);
            view = DefaultView;
            ApplyBudget(ref view);
            Raise(SessionChange.Definition | SessionChange.Parameters | SessionChange.View);
        }

        /// <summary>Open a 3D scene: camera and parameters start at its own defaults; the look is kept.</summary>
        public void SetScene(ISceneDefinition value)
        {
            if (value == null || ReferenceEquals(value, scene))
            {
                return;
            }

            scene = value;
            parameters = FractalParameterSet.Defaults(value.Parameters);
            camera = DefaultCamera;
            Raise(SessionChange.Definition | SessionChange.Parameters | SessionChange.Camera);
        }

        /// <summary>Move the scene's camera. A damaged camera keeps the current one; no scene, no change.</summary>
        public void SetCamera(in SceneCamera value)
        {
            if (scene == null)
            {
                return;
            }

            var next = value.Sanitized(camera);
            if (next.Equals(camera))
            {
                return;
            }

            camera = next;
            Raise(SessionChange.Camera);
        }

        /// <summary>
        /// A camera framed the way a default one is - for a screen at least as wide as it is tall -
        /// fitted to this screen: for a scene's places, as <see cref="SetFramedView"/> is for a plane's.
        /// </summary>
        public void SetFramedCamera(in SceneCamera framing)
        {
            SetCamera(FittedCamera(framing));
        }

        public void SetLook(in SceneLook value)
        {
            var next = value.Sanitized();
            if (next.Equals(look))
            {
                return;
            }

            look = next;
            Raise(SessionChange.Look);
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
        /// Several parameters as one change - a hybrid's rows swapped or removed - so nothing renders
        /// or saves the half-applied set in between. Keys this fractal does not have are skipped.
        /// </summary>
        public void SetParameters(IReadOnlyList<ParameterValue> values)
        {
            if (values == null)
            {
                return;
            }

            var next = parameters;
            for (var i = 0; i < values.Count; i++)
            {
                next = next.With(values[i].Key, values[i].Value);
            }

            if (next.SameValues(parameters))
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
            Normalize(ref next);
            if (Same(next, view))
            {
                return;
            }

            view = next;
            Raise(SessionChange.View);
        }

        /// <summary>Entry point for presets, bookmarks and restored state.</summary>
        public void SetCenter(in HighPrecision centerX, in HighPrecision centerY, in HighPrecision scale)
        {
            var next = view;
            next.x = centerX;
            next.y = centerY;
            next.scale = scale;
            SetView(next);
        }

        /// <summary>Back to the start: the default camera of a scene, the default view of a plane fractal.</summary>
        public void ResetView()
        {
            if (scene != null)
            {
                SetCamera(DefaultCamera);
                return;
            }

            SetView(DefaultView);
        }

        /// <summary>
        /// Show a framing given the way a definition's default view is - a height for a screen at
        /// least as wide as it is tall - fitted to this screen, as <see cref="DefaultView"/> is. For
        /// places of interest; a saved view is put back exactly as it was, through <see cref="Apply"/>.
        /// </summary>
        public void SetFramedView(in ViewState framing)
        {
            SetView(Fitted(framing));
        }

        private ViewState Fitted(ViewState target)
        {
            if (displayAspect >= 1d)
            {
                return target;
            }

            // Scale is the height the view spans; dividing by the aspect makes the width span it.
            // At full precision, like every other change to a view's scale.
            target.scale = target.scale / displayAspect;
            return target;
        }

        /// <summary>
        /// The field of view is a height; upright, the same height leaves a shape's sides cut off, so
        /// the camera backs away until what was the screen's height fits across its width.
        /// </summary>
        private SceneCamera FittedCamera(in SceneCamera target)
        {
            if (displayAspect >= 1d)
            {
                return target;
            }

            return target.WithDistance(Mathf.Min(target.Distance / (float)displayAspect, SceneCamera.MaximumDistance));
        }

        /// <summary>The picture as saved state: fractal, view, parameters, palette and colouring.</summary>
        public FractalStateDto Capture()
        {
            var values = new ParameterValueDto[parameters.Count];
            for (var i = 0; i < values.Length; i++)
            {
                values[i] = new ParameterValueDto { key = parameters.Descriptors[i].Key, value = parameters[i] };
            }

            // The look rides along with a plane fractal too, as the palette rides along with a scene:
            // a bookmark puts back the whole appearance it was saved with.
            var sceneState = StateCodec.ToDto(camera, look);
            if (scene == null)
            {
                sceneState.distance = 0f;
            }

            return new FractalStateDto
            {
                version = FractalStateDto.CurrentVersion,
                fractal = Mode.Id,
                centerX = view.x.ToInvariantString(),
                centerY = view.y.ToInvariantString(),
                scale = view.scale.ToInvariantString(),
                rotation = view.rotation,
                palette = palette.Id,
                coloring = StateCodec.ToDto(coloring),
                parameters = values,
                scene = sceneState
            };
        }

        /// <summary>
        /// Put a saved picture back, as one change. Anything the file names that no longer exists -
        /// a removed palette, a renamed parameter - keeps its current value instead of failing the
        /// whole restore. Returns false, and changes nothing, when the fractal itself is unknown.
        /// </summary>
        public bool Apply(
            FractalStateDto state,
            IReadOnlyList<IFractalDefinition> catalog,
            IReadOnlyList<ISceneDefinition> scenes,
            PaletteCatalog palettes)
        {
            state = StateCodec.Upgrade(state);
            if (state == null || catalog == null)
            {
                return false;
            }

            IFractalDefinition target = null;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Id == state.fractal)
                {
                    target = catalog[i];
                    break;
                }
            }

            ISceneDefinition targetScene = null;
            for (var i = 0; target == null && scenes != null && i < scenes.Count; i++)
            {
                if (scenes[i].Id == state.fractal)
                {
                    targetScene = scenes[i];
                }
            }

            if (target == null && targetScene == null)
            {
                return false;
            }

            var change = SessionChange.Parameters;
            if (target != null)
            {
                change |= SessionChange.View;
                if (!ReferenceEquals(target, definition) || scene != null)
                {
                    definition = target;
                    scene = null;
                    change |= SessionChange.Definition;
                }
            }
            else
            {
                change |= SessionChange.Camera;
                if (!ReferenceEquals(targetScene, scene))
                {
                    scene = targetScene;
                    change |= SessionChange.Definition;
                }
            }

            var nextParameters = FractalParameterSet.Defaults(Mode.Parameters);
            if (state.parameters != null)
            {
                foreach (var value in state.parameters)
                {
                    if (value != null)
                    {
                        nextParameters = nextParameters.With(value.key, value.value);
                    }
                }
            }

            parameters = nextParameters;

            var nextLook = StateCodec.LookFromDto(state.scene?.look, look);
            if (!nextLook.Equals(look))
            {
                look = nextLook;
                change |= SessionChange.Look;
            }

            if (targetScene != null)
            {
                // A saved camera is put back exactly as it was, like a saved view: never refit.
                camera = StateCodec.CameraFromDto(state.scene, FittedCamera(targetScene.DefaultCamera));
                ApplyAppearance(state, palettes, ref change);
                Raise(change);
                return true;
            }

            var nextView = Fitted(target.DefaultView);
            if (HighPrecision.TryParse(state.centerX, out var x) &&
                HighPrecision.TryParse(state.centerY, out var y) &&
                HighPrecision.TryParse(state.scale, out var scale) &&
                scale.Sign > 0)
            {
                nextView.x = x;
                nextView.y = y;
                nextView.scale = scale;
                nextView.rotation = double.IsNaN(state.rotation) ? 0d : state.rotation;
            }

            Normalize(ref nextView);
            view = nextView;

            ApplyAppearance(state, palettes, ref change);
            Raise(change);
            return true;
        }

        /// <summary>The palette and the plane's colouring a saved state names, where they still exist.</summary>
        private void ApplyAppearance(FractalStateDto state, PaletteCatalog palettes, ref SessionChange change)
        {
            var nextPalette = palettes?.Find(state.palette);
            if (nextPalette != null && !ReferenceEquals(nextPalette, palette))
            {
                palette = nextPalette;
                change |= SessionChange.Palette;
            }

            if (state.coloring != null)
            {
                var nextColoring = StateCodec.FromDto(state.coloring);
                if (!nextColoring.Equals(coloring))
                {
                    coloring = nextColoring;
                    change |= SessionChange.Coloring;
                }
            }
        }

        /// <summary>
        /// The iteration budget a view of this scale gets. For pictures drawn beside the session -
        /// gallery previews - so they use the one budget rule instead of inventing another.
        /// </summary>
        public int IterationBudget(double scale) => ResolveIterations(scale);

        /// <summary>Clamp the scale and derive the iteration budget: the one definition of both.</summary>
        private void Normalize(ref ViewState target)
        {
            var scale = Math.Clamp(target.scale.AsDouble, MinimumScale, MaximumScale);
            if (scale != target.scale.AsDouble)
            {
                target.scale = HighPrecision.FromDouble(scale);
            }

            // Keep the centre to what this scale can use - a pixel is about 2^-12 of the scale, so 80
            // bits below it are 68 below a pixel. Panning adds digits on every frame; without this a
            // shallow view would carry a thousand-bit centre into every comparison and saved file.
            var fractionBits = Math.Max(64, (int)Math.Ceiling(-Math.Log(scale, 2d)) + 80);
            target.x = target.x.Quantize(fractionBits);
            target.y = target.y.Quantize(fractionBits);
            target.scale = target.scale.Quantize(fractionBits + 64);

            ApplyBudget(ref target);
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
            var depth = Math.Max(0d, -Math.Log10(Math.Max(scale, 1e-300d)) - 3d);
            var depthBudget = depth * 96d + Math.Max(0d, depth - 6d) * 192d;
            var cap = SupportsArbitraryDepth
                ? Math.Max(quality.MaximumIterations, RenderQuality.ArbitraryMaximumIterations)
                : quality.MaximumIterations;
            return (int)Math.Clamp(quality.SettledIterations + Math.Round(depthBudget), 16d, cap);
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
