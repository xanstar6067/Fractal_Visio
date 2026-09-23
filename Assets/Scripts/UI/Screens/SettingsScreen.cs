using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The settings panel: blocks of controls laid out in one to three balanced columns. Adding a
    /// setting is one block - an option list, or a widget from <c>UI/Widgets</c> - plus the lines
    /// that read and write it on the session; layout, scrolling and touch targets are handled here.
    ///
    /// Fractals come from <c>AppServices.Catalog</c>, palettes from <c>AppServices.Palettes</c>, and
    /// the PARAMETERS block is generated from the active fractal's
    /// <see cref="IFractalDefinition.Parameters"/>. The UI never references the Fractals assembly,
    /// so a new fractal - with its own knobs - appears here without this file changing.
    /// </summary>
    public sealed class SettingsScreen : UiScreen
    {
        /// <summary>Render resolution as a fraction of the screen; 0 means the device profile decides.</summary>
        private static readonly float[] ResolutionScales = { 0f, 0.5f, 0.75f, 1f };

        /// <summary>Percentages read the same in every language; only "Auto" is a word.</summary>
        private static readonly string[] ResolutionPercentNames = { "50%", "75%", "100%" };

        /// <summary>
        /// Colouring as three presets rather than two switches. They are not independent in
        /// practice - nobody wants unsmoothed logarithmic - and a preset row is one tap where two
        /// toggles are two.
        /// </summary>
        private static readonly (bool Smooth, ColoringMode Mode)[] ColoringPresets =
        {
            (false, ColoringMode.Linear),
            (true, ColoringMode.Linear),
            (true, ColoringMode.Logarithmic)
        };

        private static readonly string[] ColoringKeys =
            { "settings.coloring.bands", "settings.coloring.smooth", "settings.coloring.smooth_log" };

        // Centred on 1.0 - the density figure the screen reports - with room either side. Sizes
        // rather than adjectives: "Large" means nothing without knowing the base, XS to XXL is a
        // ladder, and none of it needs translating.
        private static readonly float[] InterfaceScales = { 0.8f, 1f, 1.2f, 1.45f, 1.75f, 2.1f };

        private static readonly string[] InterfaceNames = { "XS", "S", "M", "L", "XL", "XXL" };

        /// <summary>Seconds a flicked view coasts for; 0 is off. The reference app offers the same ladder.</summary>
        private static readonly float[] InertiaLengths = { 0f, 2f, 5f, 10f };

        private static readonly string[] InertiaKeys =
            { "settings.inertia.off", "settings.inertia.short", "settings.inertia.medium", "settings.inertia.long" };

        private static readonly string[] BoolKeys = { "common.off", "common.on" };

        /// <summary>Most columns to split into. Past three the rows get too narrow to read.</summary>
        private const int MaximumColumns = 3;

        private readonly Action openPaletteEditor;
        private readonly List<IFractalDefinition> fractals = new();
        private readonly List<ParameterControl> parameterControls = new();

        private SettingsSection fractalSection;
        private SettingsSection paletteSection;
        private SettingsSection coloringSection;
        private SettingsSection resolutionSection;
        private SettingsSection interfaceSection;
        private SettingsSection inertiaSection;
        private SettingsSection languageSection;

        private IFractalDefinition builtFor;
        private int builtPaletteCount;

        public SettingsScreen(Action openPaletteEditor)
        {
            this.openPaletteEditor = openPaletteEditor;
        }

        protected override void OnBuild(Transform parent)
        {
            var session = Services.Session;
            parameterControls.Clear();
            builtFor = session.Definition;
            builtPaletteCount = Services.Palettes.All.Count;

            fractals.Clear();
            for (var i = 0; i < Services.Catalog.Count; i++)
            {
                fractals.Add(Services.Catalog[i]);
            }

            var strings = Strings;
            var blocks = new List<Block>
            {
                new OptionsBlock(
                    strings.Get("settings.fractal"), Names(fractals, f => strings.FractalName(f)), SelectFractal, s => fractalSection = s),
            };

            if (session.Definition.Parameters.Count > 0)
            {
                blocks.Add(new ParametersBlock(this, session.Definition));
            }

            blocks.Add(new OptionsBlock(
                strings.Get("settings.palette"), Names(Services.Palettes.All, p => strings.PaletteName(p)), SelectPalette,
                s => paletteSection = s, strings.Get("settings.palette.edit"), openPaletteEditor));
            blocks.Add(new OptionsBlock(
                strings.Get("settings.coloring"), Localize(ColoringKeys), SelectColoring, s => coloringSection = s));
            blocks.Add(new OptionsBlock(
                strings.Get("settings.resolution"), ResolutionNames(), SelectResolution, s => resolutionSection = s));
            blocks.Add(new OptionsBlock(
                strings.Get("settings.inertia"), Localize(InertiaKeys), SelectInertia, s => inertiaSection = s));
            blocks.Add(new OptionsBlock(
                strings.Get("settings.interface_size"), InterfaceNames, SelectInterfaceScale, s => interfaceSection = s));
            blocks.Add(new OptionsBlock(
                strings.Get("settings.language"), LanguageNames(), SelectLanguage, s => languageSection = s));

            var width = ResolvePanelWidth(MaximumColumns, out var columns);
            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var sectionGap = Mathf.Min(UiTheme.PanelPx(UiTheme.SectionSpacing), UiTheme.AvailablePanelHeight * 0.04f);
            var titleHeight = UiTheme.PanelPx(28f);
            var columnWidth = (width - padding * 2f - padding * (columns - 1)) / columns;

            // Shortest column first, so blocks of different lengths end up balanced instead of one
            // column running off the bottom while the next is half empty.
            var cursors = new float[columns];
            var top = -(padding + titleHeight + sectionGap);
            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            var plan = new int[blocks.Count];
            for (var i = 0; i < blocks.Count; i++)
            {
                var column = ShortestColumn(cursors);
                plan[i] = column;
                cursors[column] -= blocks[i].Measure() + sectionGap;
            }

            var tallest = 0f;
            for (var i = 0; i < columns; i++)
            {
                tallest = Mathf.Max(tallest, -cursors[i]);
            }

            var contentHeight = tallest - sectionGap + padding;
            var content = CreateScrollingPanel(parent, "SettingsPanel", width, contentHeight);
            AddTitle(content, strings.Get("settings.title"), padding, width);

            for (var i = 0; i < columns; i++)
            {
                cursors[i] = top;
            }

            for (var i = 0; i < blocks.Count; i++)
            {
                var column = plan[i];
                var x = padding + column * (columnWidth + padding);
                blocks[i].Build(content, x, cursors[column], columnWidth);
                cursors[column] -= blocks[i].Measure() + sectionGap;
            }

            RefreshSelection();
        }

        protected override void OnTick()
        {
            // Another fractal has other parameters, and a new palette is a new row: both change
            // the shape of the panel, which is a rebuild rather than an update.
            if (!ReferenceEquals(Services.Session.Definition, builtFor) ||
                Services.Palettes.All.Count != builtPaletteCount)
            {
                NeedsRebuild = true;
                return;
            }

            RefreshSelection();
        }

        private static int ShortestColumn(float[] cursors)
        {
            var best = 0;
            for (var i = 1; i < cursors.Length; i++)
            {
                if (cursors[i] > cursors[best])
                {
                    best = i;
                }
            }

            return best;
        }

        private static string[] Names<T>(IReadOnlyList<T> items, Func<T, string> name)
        {
            var names = new string[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                names[i] = name(items[i]);
            }

            return names;
        }

        private string[] ResolutionNames()
        {
            var names = new string[ResolutionPercentNames.Length + 1];
            names[0] = Strings.Get("settings.resolution.auto");
            ResolutionPercentNames.CopyTo(names, 1);
            return names;
        }

        /// <summary>
        /// "Device language" first, then every locale by its own name - "Русский", not "Russian":
        /// the list has to be readable by someone who cannot read the language it is shown in.
        /// </summary>
        private string[] LanguageNames()
        {
            var languages = Strings.Languages;
            var names = new string[languages.Count + 1];
            names[0] = Strings.Get("settings.language.system");
            for (var i = 0; i < languages.Count; i++)
            {
                names[i + 1] = languages[i].NativeName;
            }

            return names;
        }

        private void SelectFractal(int index)
        {
            if (index >= 0 && index < fractals.Count)
            {
                Services.Session.SetDefinition(fractals[index]);
            }
        }

        private void SelectPalette(int index)
        {
            var palettes = Services.Palettes.All;
            if (index >= 0 && index < palettes.Count)
            {
                Services.Session.SetPalette(palettes[index]);
            }
        }

        private void SelectColoring(int index)
        {
            if (index < 0 || index >= ColoringPresets.Length)
            {
                return;
            }

            var settings = Services.Session.Coloring;
            settings.Smooth = ColoringPresets[index].Smooth;
            settings.Mode = ColoringPresets[index].Mode;
            Services.Session.SetColoring(settings);
        }

        private void SelectResolution(int index)
        {
            if (index >= 0 && index < ResolutionScales.Length)
            {
                Services.Session.SetRenderScale(ResolutionScales[index]);
            }
        }

        private void SelectInterfaceScale(int index)
        {
            if (index < 0 || index >= InterfaceScales.Length)
            {
                return;
            }

            var settings = Services.Session.Interface;
            settings.Scale = InterfaceScales[index];
            Services.Session.SetInterface(settings);
        }

        private void SelectInertia(int index)
        {
            if (index < 0 || index >= InertiaLengths.Length)
            {
                return;
            }

            var settings = Services.Session.Interface;
            settings.InertiaSeconds = InertiaLengths[index];
            Services.Session.SetInterface(settings);
        }

        private void SelectLanguage(int index)
        {
            var languages = Strings.Languages;
            if (index < 0 || index > languages.Count)
            {
                return;
            }

            // The router sees the new language and rebuilds every screen, this one included.
            var settings = Services.Session.Interface;
            settings.Language = index == 0 ? string.Empty : languages[index - 1].Code;
            Services.Session.SetInterface(settings);
        }

        private int LanguageIndex(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return 0;
            }

            var languages = Strings.Languages;
            for (var i = 0; i < languages.Count; i++)
            {
                if (string.Equals(languages[i].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1;
                }
            }

            return -1;
        }

        private void RefreshSelection()
        {
            var session = Services.Session;

            fractalSection?.SetSelected(fractals.IndexOf(session.Definition));
            paletteSection?.SetSelected(Services.Palettes.IndexOf(session.Palette));
            coloringSection?.SetSelected(ColoringIndex(session.Coloring));
            resolutionSection?.SetSelected(NearestIndex(ResolutionScales, session.Quality.RenderScale));
            interfaceSection?.SetSelected(NearestIndex(InterfaceScales, session.Interface.Scale));
            inertiaSection?.SetSelected(NearestIndex(InertiaLengths, session.Interface.InertiaSeconds));
            languageSection?.SetSelected(LanguageIndex(session.Interface.Language));

            for (var i = 0; i < parameterControls.Count; i++)
            {
                parameterControls[i].Refresh(session.Parameters);
            }
        }

        private static int ColoringIndex(in ColoringSettings settings)
        {
            for (var i = 0; i < ColoringPresets.Length; i++)
            {
                if (ColoringPresets[i].Smooth == settings.Smooth && ColoringPresets[i].Mode == settings.Mode)
                {
                    return i;
                }
            }

            return -1;
        }

        private static int NearestIndex(float[] values, float target)
        {
            var best = -1;
            var bestDistance = float.MaxValue;
            for (var i = 0; i < values.Length; i++)
            {
                var distance = Mathf.Abs(values[i] - target);
                if (distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                best = i;
            }

            return bestDistance <= 0.01f ? best : -1;
        }

        /// <summary>One piece of the panel that the column planner can measure and place.</summary>
        private abstract class Block
        {
            public abstract float Measure();

            public abstract void Build(RectTransform content, float x, float y, float width);
        }

        /// <summary>A <see cref="SettingsSection"/>, optionally followed by one action row.</summary>
        private sealed class OptionsBlock : Block
        {
            private readonly string label;
            private readonly IReadOnlyList<string> options;
            private readonly Action<int> onSelect;
            private readonly Action<SettingsSection> onBuilt;
            private readonly string actionLabel;
            private readonly Action action;

            public OptionsBlock(
                string label,
                IReadOnlyList<string> options,
                Action<int> onSelect,
                Action<SettingsSection> onBuilt,
                string actionLabel = null,
                Action action = null)
            {
                this.label = label;
                this.options = options;
                this.onSelect = onSelect;
                this.onBuilt = onBuilt;
                this.actionLabel = actionLabel;
                this.action = action;
            }

            private bool HasAction => actionLabel != null && action != null;

            public override float Measure()
            {
                var height = SettingsSection.MeasureHeight(options.Count);
                if (HasAction)
                {
                    height += UiTheme.PanelPx(UiTheme.RowSpacing) + ActionRow.MeasureHeight();
                }

                return height;
            }

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var section = SettingsSection.Create(content, label, options, onSelect, x, y, width);
                onBuilt(section);

                if (HasAction)
                {
                    var actionY = y - section.Height - UiTheme.PanelPx(UiTheme.RowSpacing);
                    ActionRow.Create(content, actionLabel, x, actionY, width, action, ActionStyle.Accent);
                }
            }
        }

        /// <summary>Controls generated from the active fractal's parameter descriptors.</summary>
        private sealed class ParametersBlock : Block
        {
            private readonly SettingsScreen owner;
            private readonly IFractalDefinition definition;
            private readonly IReadOnlyList<FractalParameterDescriptor> descriptors;

            public ParametersBlock(SettingsScreen owner, IFractalDefinition definition)
            {
                this.owner = owner;
                this.definition = definition;
                descriptors = definition.Parameters;
            }

            public override float Measure()
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
