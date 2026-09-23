using System;
using System.Collections.Generic;
using UnityEngine;
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
    /// </summary>
    public sealed class FractalScreen : BlockScreen
    {
        private static readonly string[] BoolKeys = { "common.off", "common.on" };

        private readonly Action openGallery;
        private readonly List<ParameterControl> parameterControls = new();
        private IFractalDefinition builtFor;

        public FractalScreen(Action openGallery)
        {
            this.openGallery = openGallery;
        }

        /// <summary>Parameters read best one under another; a second column only splits a short list.</summary>
        protected override int MaximumColumns => 2;

        protected override string BuildTitle() => Strings.FractalName(Services.Session.Definition);

        protected override void CollectBlocks(List<Block> blocks)
        {
            var definition = Services.Session.Definition;
            builtFor = definition;
            parameterControls.Clear();

            var description = Strings.FractalDescription(definition);
            if (!string.IsNullOrEmpty(description))
            {
                blocks.Add(new TextBlock(description));
            }

            if (definition.Parameters.Count > 0)
            {
                blocks.Add(new ParametersBlock(this, definition));
            }

            blocks.Add(new ActionBlock(Strings.Get("fractal_panel.reset_view"), () => Services.Session.ResetView()));
            blocks.Add(new ActionBlock(Strings.Get("fractal_panel.all_fractals"), openGallery, ActionStyle.Accent));
        }

        protected override void OnBuild(Transform parent)
        {
            base.OnBuild(parent);
            RefreshParameters();
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
        }

        /// <summary>Controls generated from the active fractal's parameter descriptors.</summary>
        private sealed class ParametersBlock : Block
        {
            private readonly FractalScreen owner;
            private readonly IFractalDefinition definition;
            private readonly IReadOnlyList<FractalParameterDescriptor> descriptors;

            public ParametersBlock(FractalScreen owner, IFractalDefinition definition)
            {
                this.owner = owner;
                this.definition = definition;
                descriptors = definition.Parameters;
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
