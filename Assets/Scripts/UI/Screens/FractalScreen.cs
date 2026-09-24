using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The fractal on screen: what it is, its parameters, and the way back to its start. Choosing
    /// another fractal is the gallery's job; this panel links to it rather than repeating the list.
    ///
    /// The PARAMETERS block is generated from the active fractal's
    /// <see cref="IFractalDefinition.Parameters"/>, and the UI never references the Fractals
    /// assembly - so a new fractal, with its own knobs, appears here without this file changing.
    ///
    /// A fractal with a parameter plane (<see cref="IParameterPlane"/>) gets its point as one value
    /// with a way to the C map and its well-known points, instead of two sliders: two sliders over a
    /// range of four cannot set a C to the third decimal, and do not say that C is a place. And the
    /// fractal a plane is drawn from - the Mandelbrot set - gets a button to the Julia set of the
    /// point in the middle of the screen: the link between the two, as a button rather than a
    /// gesture nobody would guess.
    /// </summary>
    public sealed class FractalScreen : BlockScreen
    {
        private static readonly string[] BoolKeys = { "common.off", "common.on" };

        private readonly Action openGallery;
        private readonly Action openPlaneMap;
        private readonly List<ParameterControl> parameterControls = new();
        private IFractalDefinition builtFor;
        private IParameterPlane plane;
        private Text constantText;
        private SettingsSection presetSection;
        private double shownReal = double.NaN;
        private double shownImaginary = double.NaN;

        public FractalScreen(Action openGallery, Action openPlaneMap)
        {
            this.openGallery = openGallery;
            this.openPlaneMap = openPlaneMap;
        }

        /// <summary>Parameters read best one under another; a second column only splits a short list.</summary>
        protected override int MaximumColumns => 2;

        protected override string BuildTitle() => Strings.FractalName(Services.Session.Definition);

        protected override void CollectBlocks(List<Block> blocks)
        {
            var definition = Services.Session.Definition;
            builtFor = definition;
            parameterControls.Clear();
            constantText = null;
            presetSection = null;
            shownReal = double.NaN;
            shownImaginary = double.NaN;

            // A plane only counts when its map can be found and drawn: otherwise C stays two sliders.
            plane = ParameterMapScreen.FindPlane(Services, definition) != null ? definition as IParameterPlane : null;

            var description = Strings.FractalDescription(definition);
            if (!string.IsNullOrEmpty(description))
            {
                blocks.Add(new TextBlock(description));
            }

            if (plane != null)
            {
                blocks.Add(new ConstantBlock(this));
                if (plane.Presets.Count > 0)
                {
                    var names = new string[plane.Presets.Count];
                    for (var i = 0; i < names.Length; i++)
                    {
                        names[i] = Strings.PresetName(definition, plane.Presets[i]);
                    }

                    blocks.Add(new OptionsBlock(
                        Strings.Get("fractal_panel.presets"), names, ApplyPreset, section => presetSection = section));
                }
            }

            var descriptors = OwnParameters(definition);
            if (descriptors.Count > 0)
            {
                blocks.Add(new ParametersBlock(this, definition, descriptors));
            }

            var julia = FindJuliaOf(definition);
            if (julia != null)
            {
                blocks.Add(new ActionBlock(Strings.Get("fractal_panel.julia_here"), () => OpenJuliaHere(julia)));
            }

            blocks.Add(new ActionBlock(Strings.Get("fractal_panel.reset_view"), () => Services.Session.ResetView()));
            blocks.Add(new ActionBlock(Strings.Get("fractal_panel.all_fractals"), openGallery, ActionStyle.Accent));
        }

        protected override void OnBuild(Transform parent)
        {
            base.OnBuild(parent);
            RefreshParameters();
        }

        /// <summary>The descriptors that get a control of their own: all of them, less a plane's point.</summary>
        private List<FractalParameterDescriptor> OwnParameters(IFractalDefinition definition)
        {
            var result = new List<FractalParameterDescriptor>();
            var all = definition.Parameters;
            for (var i = 0; i < all.Count; i++)
            {
                var key = all[i].Key;
                if (plane != null && (key == plane.RealKey || key == plane.ImaginaryKey))
                {
                    continue;
                }

                result.Add(all[i]);
            }

            return result;
        }

        /// <summary>The catalog's fractal whose plane is <paramref name="definition"/>: the Julia sets of the Mandelbrot set.</summary>
        private IFractalDefinition FindJuliaOf(IFractalDefinition definition)
        {
            var catalog = Services.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i] is IParameterPlane candidate && candidate.PlaneFractalId == definition.Id)
                {
                    return catalog[i];
                }
            }

            return null;
        }

        /// <summary>
        /// The Julia set of the point in the middle of the screen. The panel closes behind it: the
        /// new picture is the answer, and its map button - top right - is the way back to choosing C.
        /// </summary>
        private void OpenJuliaHere(IFractalDefinition julia)
        {
            var session = Services.Session;
            var x = session.View.x.AsDouble;
            var y = session.View.y.AsDouble;
            var target = (IParameterPlane)julia;

            session.SetDefinition(julia);
            session.SetParameter(target.RealKey, x);
            session.SetParameter(target.ImaginaryKey, y);
            Close();
        }

        /// <summary>A well-known C, seen whole: the view returns to the start, since a detail of the old set means nothing in the new one.</summary>
        private void ApplyPreset(int index)
        {
            if (plane == null || index < 0 || index >= plane.Presets.Count)
            {
                return;
            }

            var preset = plane.Presets[index];
            var session = Services.Session;
            session.SetParameter(plane.RealKey, preset.Real);
            session.SetParameter(plane.ImaginaryKey, preset.Imaginary);
            session.ResetView();
        }

        protected override void OnTick()
        {
            // Another fractal has other parameters and another name: a different panel.
            if (!ReferenceEquals(Services.Session.Definition, builtFor))
            {
                NeedsRebuild = true;
                return;
            }

            RefreshParameters();
        }

        protected override void OnOpenChanged(bool open)
        {
            if (open && !ReferenceEquals(Services.Session.Definition, builtFor))
            {
                NeedsRebuild = true;
            }
        }

        private void RefreshParameters()
        {
            var parameters = Services.Session.Parameters;
            for (var i = 0; i < parameterControls.Count; i++)
            {
                parameterControls[i].Refresh(parameters);
            }

            if (plane == null)
            {
                return;
            }

            var real = parameters.Get(plane.RealKey);
            var imaginary = parameters.Get(plane.ImaginaryKey);
            if (real.Equals(shownReal) && imaginary.Equals(shownImaginary))
            {
                return;
            }

            shownReal = real;
            shownImaginary = imaginary;
            if (constantText != null)
            {
                constantText.text = Strings.Format("plane.value", ParameterMapScreen.FormatPoint(real, imaginary));
            }

            // Lit only while C is exactly a preset's: after a drag on the map, none is.
            var selected = -1;
            for (var i = 0; i < plane.Presets.Count; i++)
            {
                if (Math.Abs(plane.Presets[i].Real - real) < 1e-12d && Math.Abs(plane.Presets[i].Imaginary - imaginary) < 1e-12d)
                {
                    selected = i;
                    break;
                }
            }

            presetSection?.SetSelected(selected);
        }

        /// <summary>
        /// A plane's point: its value, and the way to the map. The map is a panel of its own
        /// (<see cref="ParameterMapScreen"/>), opened from here and from the map button.
        /// </summary>
        private sealed class ConstantBlock : Block
        {
            private readonly FractalScreen owner;

            public ConstantBlock(FractalScreen owner)
            {
                this.owner = owner;
            }

            private static float ValueHeight => UiTheme.PanelPx(30f);

            public override float Measure(float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                return UiTheme.PanelPx(20f) + gap * 0.5f + ValueHeight + gap + ActionRow.MeasureHeight();
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                var cursor = y - AddCaption(content, owner.Strings.Get("fractal_panel.constant"), x, y, width) - gap * 0.5f;

                var value = UiFactory.CreateText(
                    "Constant", content, string.Empty, UiTheme.SegmentFontSize, UiTheme.Accent,
                    TextAnchor.MiddleLeft, fitToRect: true, panelScale: true);
                Place(value.rectTransform, x, cursor, width, ValueHeight);
                owner.constantText = value;
                cursor -= ValueHeight + gap;

                ActionRow.Create(
                    content, owner.Strings.Get("fractal_panel.pick_on_map"), x, cursor, width, owner.openPlaneMap, ActionStyle.Accent);
            }
        }

        /// <summary>Controls generated from the active fractal's parameter descriptors.</summary>
        private sealed class ParametersBlock : Block
        {
            private readonly FractalScreen owner;
            private readonly IFractalDefinition definition;
            private readonly IReadOnlyList<FractalParameterDescriptor> descriptors;

            public ParametersBlock(FractalScreen owner, IFractalDefinition definition, IReadOnlyList<FractalParameterDescriptor> descriptors)
            {
                this.owner = owner;
                this.definition = definition;
                this.descriptors = descriptors;
            }

            public override float Measure(float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                var height = UiTheme.PanelPx(20f);
                for (var i = 0; i < descriptors.Count; i++)
                {
                    height += gap + MeasureControl(descriptors[i]);
                }

                return height;
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                var strings = owner.Strings;
                var cursor = y - AddCaption(content, strings.Get("settings.parameters"), x, y, width);
                var session = owner.Services.Session;
                var boolNames = owner.Localize(BoolKeys);

                for (var i = 0; i < descriptors.Count; i++)
                {
                    cursor -= gap;
                    var descriptor = descriptors[i];
                    var value = session.Parameters.Get(descriptor.Key, descriptor.Default);
                    var label = strings.ParameterLabel(definition, descriptor);

                    if (descriptor.Kind == FractalParameterKind.Bool)
                    {
                        var section = SettingsSection.Create(
                            content, label, boolNames,
                            index => session.SetParameter(descriptor.Key, index),
                            x, cursor, width);
                        owner.parameterControls.Add(new ParameterControl(descriptor.Key, section));
                    }
                    else
                    {
                        var slider = SliderRow.Create(
                            content, label, descriptor.Minimum, descriptor.Maximum, value,
                            x, cursor, width,
                            onChanged: null,
                            // Applied on release: every parameter change re-renders the fractal
                            // from scratch, which mid-drag would only ever show coarse passes.
                            onCommitted: committed => session.SetParameter(descriptor.Key, committed),
                            logarithmic: descriptor.Logarithmic,
                            integer: descriptor.Kind == FractalParameterKind.Int);
                        owner.parameterControls.Add(new ParameterControl(descriptor.Key, slider));
                    }

                    cursor -= MeasureControl(descriptor);
                }
            }

            private static float MeasureControl(in FractalParameterDescriptor descriptor)
            {
                return descriptor.Kind == FractalParameterKind.Bool
                    ? SettingsSection.MeasureHeight(BoolKeys.Length)
                    : SliderRow.MeasureHeight();
            }
        }

        /// <summary>
        /// Keeps a generated control in step with the session when the value changes elsewhere - a
        /// bookmark, a restored session. Only on an actual change: pushing the session value every
        /// frame would yank a slider back while the finger is still dragging it.
        /// </summary>
        private sealed class ParameterControl
        {
            private readonly string key;
            private readonly SliderRow slider;
            private readonly SettingsSection toggle;
            private double lastSeen = double.NaN;

            public ParameterControl(string key, SliderRow slider)
            {
                this.key = key;
                this.slider = slider;
            }

            public ParameterControl(string key, SettingsSection toggle)
            {
                this.key = key;
                this.toggle = toggle;
            }

            public void Refresh(in FractalParameterSet parameters)
            {
                if (!parameters.TryGet(key, out var value) || value.Equals(lastSeen))
                {
                    return;
                }

                lastSeen = value;
                slider?.SetValue(value);
                toggle?.SetSelected(value >= 0.5d ? 1 : 0);
            }
        }
    }
}
