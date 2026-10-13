using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Colour: which palette, how it is laid over the escape counts, and the way into the palette
    /// editor. Every change here is a recolour of the frame already computed, never a new render,
    /// so it is applied the moment a row is tapped.
    ///
    /// Palettes are a grid of cards, each its strip over its name (<see cref="PaletteGrid"/>): a list
    /// of names ("Aurora", "Ember") asks the user to remember what each looked like.
    ///
    /// A 3D scene is coloured otherwise (<see cref="SceneLook"/>): a shading style, what the palette
    /// colours by, the light on a dial as the relief's is, shadows and shaded folds, the strength of the
    /// style's effect and the palette's scale and shift. Every change there is a new render, drawn as a
    /// draft while a slider moves. The palette is the one the plane fractals use; choosing it while the
    /// surface is one material colour switches to colouring by the orbit - the tap asks to see it.
    /// </summary>
    public sealed class ColorScreen : BlockScreen
    {
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

        /// <summary>In <see cref="SceneShading"/> order.</summary>
        private static readonly string[] ShadingKeys =
        {
            "colour.shading.classic", "colour.shading.clay", "colour.shading.metal", "colour.shading.glow",
            "colour.shading.density", "colour.shading.studio", "colour.shading.toon", "colour.shading.translucent",
            "colour.shading.mirror"
        };

        /// <summary>In <see cref="SceneColoring"/> order.</summary>
        private static readonly string[] SceneColoringKeys =
        {
            "colour.source.material", "colour.source.normal", "colour.source.orbit", "colour.source.depth",
            "colour.source.cross", "colour.source.iteration", "colour.source.escape", "colour.source.height",
            "colour.source.occlusion", "colour.source.fresnel", "colour.source.steps"
        };

        private static readonly string[] SwitchKeys = { "common.off", "common.on" };

        /// <summary>Swatch resolution. A strip a few dozen pixels wide needs no more.</summary>
        private const int SwatchWidth = 128;

        private readonly Action openPaletteEditor;
        private readonly Action createPalette;
        private readonly List<Texture2D> swatches = new();

        private PaletteGrid paletteGrid;
        private SettingsSection coloringSection;
        private ReliefControls relief;
        private int paletteVersion;
        private int builtPaletteVersion;
        private bool builtForScene;
        private SceneControls scene;

        public ColorScreen(Action openPaletteEditor, Action createPalette)
        {
            this.openPaletteEditor = openPaletteEditor;
            this.createPalette = createPalette;
        }

        protected override int MaximumColumns => 2;

        protected override string BuildTitle() => Strings.Get("colour.title");

        protected override void CollectBlocks(List<Block> blocks)
        {
            // Saved, edited or deleted palettes change the rows and their strips alike.
            Services.Palettes.Changed -= OnPalettesChanged;
            Services.Palettes.Changed += OnPalettesChanged;
            builtPaletteVersion = paletteVersion;

            var palettes = Services.Palettes.All;
            BuildSwatches(palettes);

            var textures = new Texture[swatches.Count];
            for (var i = 0; i < swatches.Count; i++)
            {
                textures[i] = swatches[i];
            }

            var paletteBlock = new PaletteGridBlock(
                Strings.Get("settings.palette"), Names(palettes, p => Strings.PaletteName(p)), textures, SelectPalette,
                grid => paletteGrid = grid,
                Strings.Get("settings.palette.edit"), openPaletteEditor,
                Strings.Get("settings.palette.new"), createPalette);

            relief?.Dispose();
            relief = null;
            scene?.Dispose();
            scene = null;
            coloringSection = null;
            builtForScene = Services.Session.IsScene;
            if (builtForScene)
            {
                CollectSceneBlocks(blocks, paletteBlock);
                return;
            }

            blocks.Add(paletteBlock);
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.coloring"), Localize(ColoringKeys), SelectColoring, s => coloringSection = s));

            blocks.Add(new ReliefBlock(
                Strings.Get("colour.relief"),
                new[] { Strings.Get("colour.relief.off"), Strings.Get("colour.relief.on") },
                Strings.Get("colour.relief.depth"),
                Strings.Get("colour.relief.shine"),
                Services.Session.Coloring,
                ChangeRelief,
                controls => relief = controls));
        }

        protected override void OnBuild(Transform parent)
        {
            base.OnBuild(parent);
            RefreshSelection();
        }

        protected override void OnTick()
        {
            // A new palette is a new row: the panel changes shape, which is a rebuild. So is a scene
            // opened or closed under it.
            if (paletteVersion != builtPaletteVersion || builtForScene != Services.Session.IsScene)
            {
                NeedsRebuild = true;
                return;
            }

            RefreshSelection();
        }

        protected override void OnOpenChanged(bool open)
        {
            if (open && builtForScene != Services.Session.IsScene)
            {
                NeedsRebuild = true;
            }
        }

        /// <summary>A scene's colour: style, source, palette, light, shadows and folds, the effect and the palette's spread.</summary>
        private void CollectSceneBlocks(List<Block> blocks, Block paletteBlock)
        {
            scene = new SceneControls();
            var controls = scene;
            var look = Services.Session.Look;

            blocks.Add(new ChoiceGridBlock(
                Strings.Get("colour.shading"), Localize(ShadingKeys), SelectShading, grid => controls.Shading = grid));
            blocks.Add(new ChoiceGridBlock(
                Strings.Get("colour.source"), Localize(SceneColoringKeys), SelectSceneColoring, grid => controls.Coloring = grid));
            blocks.Add(paletteBlock);
            blocks.Add(new SceneLightBlock(
                Strings.Get("colour.light"), Strings.Get("colour.light.ambient"), Strings.Get("colour.light.specular"),
                look, ChangeLook, controls));

            var switches = Localize(SwitchKeys);
            blocks.Add(new SwitchBlock(Strings.Get("colour.shadows"), switches,
                index => ChangeLook(l =>
                {
                    l.Shadows = index == 1;
                    return l;
                }), row => controls.Shadows = row));
            blocks.Add(new SwitchBlock(Strings.Get("colour.occlusion"), switches,
                index => ChangeLook(l =>
                {
                    l.Occlusion = index == 1;
                    return l;
                }), row => controls.Occlusion = row));

            blocks.Add(new SliderStackBlock(new[]
            {
                new SliderSpec(Strings.Get("colour.effect"), 0d, 2d, look.Effect, false, Percent,
                    value => ChangeLook(l =>
                    {
                        l.Effect = (float)value;
                        return l;
                    }), slider => controls.Effect = slider),
                new SliderSpec(Strings.Get("colour.palette_scale"), SceneLook.MinimumColorScale, SceneLook.MaximumColorScale,
                    look.ColorScale, true, null,
                    value => ChangeLook(l =>
                    {
                        l.ColorScale = (float)value;
                        return l;
                    }), slider => controls.Scale = slider),
                new SliderSpec(Strings.Get("colour.palette_offset"), 0d, 1d, look.ColorOffset, false, Percent,
                    value => ChangeLook(l =>
                    {
                        l.ColorOffset = (float)value;
                        return l;
                    }), slider => controls.Offset = slider)
            }));
        }

        private void SelectShading(int index)
        {
            if (index >= 0 && index < ShadingKeys.Length)
            {
                ChangeLook(l =>
                {
                    l.Shading = (SceneShading)index;
                    return l;
                });
            }
        }

        private void SelectSceneColoring(int index)
        {
            if (index >= 0 && index < SceneColoringKeys.Length)
            {
                ChangeLook(l =>
                {
                    l.Coloring = (SceneColoring)index;
                    return l;
                });
            }
        }

        /// <summary>Every scene control writes through here: a new look, a new render.</summary>
        private void ChangeLook(Func<SceneLook, SceneLook> change)
        {
            Services.Session.SetLook(change(Services.Session.Look));
        }

        private static string Percent(double value) =>
            Mathf.RoundToInt((float)value * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";

        public override void Dispose()
        {
            if (Services?.Palettes != null)
            {
                Services.Palettes.Changed -= OnPalettesChanged;
            }

            base.Dispose();
            ReleaseSwatches();
            relief?.Dispose();
            relief = null;
            scene?.Dispose();
            scene = null;
        }

        private void OnPalettesChanged()
        {
            paletteVersion++;
            if (!IsVisible)
            {
                // Hidden: nothing to redraw now, but the next open must not show the old list.
                NeedsRebuild = true;
            }
        }

        private void BuildSwatches(IReadOnlyList<PaletteData> palettes)
        {
            ReleaseSwatches();
            var pixels = new Color32[SwatchWidth];
            for (var i = 0; i < palettes.Count; i++)
            {
                var palette = palettes[i];
                for (var x = 0; x < SwatchWidth; x++)
                {
                    pixels[x] = palette.Sample(x / (SwatchWidth - 1f));
                }

                var texture = new Texture2D(SwatchWidth, 1, TextureFormat.RGBA32, false)
                {
                    name = "Swatch " + palette.Id,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    hideFlags = HideFlags.HideAndDontSave
                };
                texture.SetPixels32(pixels);
                texture.Apply(false, false);
                swatches.Add(texture);
            }
        }

        private void ReleaseSwatches()
        {
            for (var i = 0; i < swatches.Count; i++)
            {
                if (swatches[i] != null)
                {
                    UnityEngine.Object.Destroy(swatches[i]);
                }
            }

            swatches.Clear();
        }

        private void SelectPalette(int index)
        {
            var palettes = Services.Palettes.All;
            if (index >= 0 && index < palettes.Count)
            {
                Services.Session.SetPalette(palettes[index]);

                // A palette chosen for a surface of one colour asks to see the palette: show it by the orbit.
                var session = Services.Session;
                if (session.IsScene && !session.Look.UsesPalette)
                {
                    var look = session.Look;
                    look.Coloring = SceneColoring.OrbitTrap;
                    session.SetLook(look);
                }
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

        /// <summary>
        /// Every relief control writes through here. All of it is a remap of the frame on screen
        /// except the switch itself, which the presenter answers with one render.
        /// </summary>
        private void ChangeRelief(Func<ColoringSettings, ColoringSettings> change)
        {
            Services.Session.SetColoring(change(Services.Session.Coloring));
        }

        private void RefreshSelection()
        {
            var session = Services.Session;
            paletteGrid?.SetSelected(Services.Palettes.IndexOf(session.Palette));
            coloringSection?.SetSelected(ColoringIndex(session.Coloring));
            relief?.Sync(session.Coloring);
            scene?.Sync(session.Look);
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

        /// <summary>
        /// The relief's controls once built. The light, depth and shine stay visible while the relief
        /// is off - dimmed and inert - so the panel keeps its shape when it is switched.
        /// </summary>
        private sealed class ReliefControls : IDisposable
        {
            public SegmentedRow Switch;
            public LightDial Dial;
            public SliderRow Depth;
            public SliderRow Shine;
            public CanvasGroup Group;

            private bool synced;
            private ColoringSettings shown;

            public void Sync(in ColoringSettings coloring)
            {
                // Only what changed: a slider pushed back to the session's value under the finger
                // jumps, and the dial redraws its dome on every call that moves the sun.
                if (!synced || coloring.Relief != shown.Relief)
                {
                    Switch.SetSelected(coloring.Relief ? 1 : 0);
                    Group.alpha = coloring.Relief ? 1f : 0.35f;
                    Group.interactable = coloring.Relief;
                    Group.blocksRaycasts = coloring.Relief;
                }

                Dial.SetLight(coloring.LightAngle, coloring.LightHeight);

                if (!synced || !Mathf.Approximately(coloring.ReliefDepth, shown.ReliefDepth))
                {
                    Depth.SetValue(coloring.ReliefDepth);
                }

                if (!synced || !Mathf.Approximately(coloring.ReliefShine, shown.ReliefShine))
                {
                    Shine.SetValue(coloring.ReliefShine);
                }

                shown = coloring;
                synced = true;
            }

            public void Dispose() => Dial?.Dispose();
        }

        /// <summary>
        /// Relief: an off / on switch over the sun (<see cref="LightDial"/>) with the depth and shine
        /// sliders beside it. Every control applies live - the light is a remap of the frame on
        /// screen, the same as a palette.
        /// </summary>
        private sealed class ReliefBlock : Block
        {
            /// <summary>Largest the dial gets, in dp: beyond that it only takes room from the sliders.</summary>
            private const float MaximumDial = 150f;

            private readonly string label;
            private readonly IReadOnlyList<string> switchLabels;
            private readonly string depthLabel;
            private readonly string shineLabel;
            private readonly ColoringSettings coloring;
            private readonly Action<Func<ColoringSettings, ColoringSettings>> change;
            private readonly Action<ReliefControls> onBuilt;

            public ReliefBlock(
                string label, IReadOnlyList<string> switchLabels, string depthLabel, string shineLabel,
                in ColoringSettings coloring, Action<Func<ColoringSettings, ColoringSettings>> change,
                Action<ReliefControls> onBuilt)
            {
                this.label = label;
                this.switchLabels = switchLabels;
                this.depthLabel = depthLabel;
                this.shineLabel = shineLabel;
                this.coloring = coloring;
                this.change = change;
                this.onBuilt = onBuilt;
            }

            private static float CaptionHeight => UiTheme.PanelPx(20f);

            private static float Gap => UiTheme.PanelPx(UiTheme.RowSpacing);

            private static float DialSize(float width) => Mathf.Min(width * 0.42f, UiTheme.PanelPx(MaximumDial));

            private static float SlidersHeight => SliderRow.MeasureHeight() * 2f + Gap;

            public override float Measure(float width) =>
                CaptionHeight + Gap + SegmentedRow.MeasureHeight() + Gap * 1.5f +
                Mathf.Max(DialSize(width), SlidersHeight);

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var caption = UiFactory.CreateText(
                    "Label_" + label, content, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                    TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
                Place(caption.rectTransform, x, y, width, CaptionHeight);

                var cursor = y - CaptionHeight - Gap;
                var controls = new ReliefControls();
                controls.Switch = SegmentedRow.Create(content, switchLabels, x, cursor, width, index =>
                    change(c =>
                    {
                        c.Relief = index == 1;
                        return c;
                    }));
                cursor -= SegmentedRow.MeasureHeight() + Gap * 1.5f;

                // One group for everything the switch governs, so "off" dims it in one place.
                var group = UiFactory.CreateRect("Relief", content);
                group.anchorMin = new Vector2(0f, 1f);
                group.anchorMax = new Vector2(0f, 1f);
                group.pivot = new Vector2(0f, 1f);
                group.anchoredPosition = Vector2.zero;
                group.sizeDelta = Vector2.zero;
                controls.Group = group.gameObject.AddComponent<CanvasGroup>();

                var dialSize = DialSize(width);
                var rowHeight = Mathf.Max(dialSize, SlidersHeight);
                controls.Dial = LightDial.Create(
                    group, x, cursor - (rowHeight - dialSize) * 0.5f, dialSize,
                    ColoringSettings.MinimumLightHeight, ColoringSettings.MaximumLightHeight);
                controls.Dial.SetLight(coloring.LightAngle, coloring.LightHeight);
                controls.Dial.Changed += (angle, height) => change(c =>
                {
                    c.LightAngle = angle;
                    c.LightHeight = height;
                    return c;
                });

                var slidersX = x + dialSize + Gap * 1.5f;
                var slidersWidth = width - dialSize - Gap * 1.5f;
                var slidersY = cursor - (rowHeight - SlidersHeight) * 0.5f;
                controls.Depth = SliderRow.Create(
                    group, depthLabel, 0d, 1d, coloring.ReliefDepth, slidersX, slidersY, slidersWidth,
                    value => change(c =>
                    {
                        c.ReliefDepth = (float)value;
                        return c;
                    }),
                    null, format: Percent);
                controls.Shine = SliderRow.Create(
                    group, shineLabel, 0d, 1d, coloring.ReliefShine, slidersX, slidersY - SliderRow.MeasureHeight() - Gap,
                    slidersWidth,
                    value => change(c =>
                    {
                        c.ReliefShine = (float)value;
                        return c;
                    }),
                    null, format: Percent);

                controls.Sync(coloring);
                onBuilt?.Invoke(controls);
            }

            private static string Percent(double value) =>
                Mathf.RoundToInt((float)value * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>A scene's controls once built, kept in step with the session's look.</summary>
        private sealed class SceneControls : IDisposable
        {
            public ChoiceGrid Shading;
            public ChoiceGrid Coloring;
            public LightDial Dial;
            public SliderRow Ambient;
            public SliderRow Specular;
            public SegmentedRow Shadows;
            public SegmentedRow Occlusion;
            public SliderRow Effect;
            public SliderRow Scale;
            public SliderRow Offset;

            private bool synced;
            private SceneLook shown;

            public void Sync(in SceneLook look)
            {
                // Only what changed: a slider pushed back under the finger jumps.
                if (!synced || look.Shading != shown.Shading)
                {
                    Shading?.SetSelected((int)look.Shading);
                }

                if (!synced || look.Coloring != shown.Coloring)
                {
                    Coloring?.SetSelected((int)look.Coloring);
                }

                if (!synced || look.Shadows != shown.Shadows)
                {
                    Shadows?.SetSelected(look.Shadows ? 1 : 0);
                }

                if (!synced || look.Occlusion != shown.Occlusion)
                {
                    Occlusion?.SetSelected(look.Occlusion ? 1 : 0);
                }

                if (!synced || look.LightAngle != shown.LightAngle || look.LightHeight != shown.LightHeight)
                {
                    Dial?.SetLight(look.LightAngle, look.LightHeight);
                }

                SyncSlider(Ambient, look.Ambient, shown.Ambient);
                SyncSlider(Specular, look.Specular, shown.Specular);
                SyncSlider(Effect, look.Effect, shown.Effect);
                SyncSlider(Scale, look.ColorScale, shown.ColorScale);
                SyncSlider(Offset, look.ColorOffset, shown.ColorOffset);
                shown = look;
                synced = true;
            }

            private void SyncSlider(SliderRow slider, float value, float before)
            {
                if (slider != null && (!synced || !Mathf.Approximately(value, before)))
                {
                    slider.SetValue(value);
                }
            }

            public void Dispose() => Dial?.Dispose();
        }

        /// <summary>A caption over a grid of choices (<see cref="ChoiceGrid"/>).</summary>
        private sealed class ChoiceGridBlock : Block
        {
            private readonly string label;
            private readonly IReadOnlyList<string> options;
            private readonly Action<int> onSelect;
            private readonly Action<ChoiceGrid> onBuilt;

            public ChoiceGridBlock(string label, IReadOnlyList<string> options, Action<int> onSelect, Action<ChoiceGrid> onBuilt)
            {
                this.label = label;
                this.options = options;
                this.onSelect = onSelect;
                this.onBuilt = onBuilt;
            }

            public override float Measure(float width) => ChoiceGrid.MeasureHeight(options.Count, width);

            public override void Build(RectTransform content, float x, float y, float width)
            {
                onBuilt?.Invoke(ChoiceGrid.Create(content, label, options, onSelect, x, y, width));
            }
        }

        /// <summary>A caption over an off / on pair.</summary>
        private sealed class SwitchBlock : Block
        {
            private readonly string label;
            private readonly IReadOnlyList<string> options;
            private readonly Action<int> onSelect;
            private readonly Action<SegmentedRow> onBuilt;

            public SwitchBlock(string label, IReadOnlyList<string> options, Action<int> onSelect, Action<SegmentedRow> onBuilt)
            {
                this.label = label;
                this.options = options;
                this.onSelect = onSelect;
                this.onBuilt = onBuilt;
            }

            private static float CaptionHeight => UiTheme.PanelPx(20f);

            public override float Measure(float width) =>
                CaptionHeight + UiTheme.PanelPx(UiTheme.RowSpacing) + SegmentedRow.MeasureHeight();

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var caption = UiFactory.CreateText(
                    "Label_" + label, content, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                    TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
                Place(caption.rectTransform, x, y, width, CaptionHeight);
                var row = SegmentedRow.Create(
                    content, options, x, y - CaptionHeight - UiTheme.PanelPx(UiTheme.RowSpacing), width, onSelect);
                onBuilt?.Invoke(row);
            }
        }

        private readonly struct SliderSpec
        {
            public SliderSpec(
                string label, double minimum, double maximum, double value, bool logarithmic,
                Func<double, string> format, Action<double> onChanged, Action<SliderRow> onBuilt)
            {
                Label = label;
                Minimum = minimum;
                Maximum = maximum;
                Value = value;
                Logarithmic = logarithmic;
                Format = format;
                OnChanged = onChanged;
                OnBuilt = onBuilt;
            }

            public string Label { get; }
            public double Minimum { get; }
            public double Maximum { get; }
            public double Value { get; }
            public bool Logarithmic { get; }
            public Func<double, string> Format { get; }
            public Action<double> OnChanged { get; }
            public Action<SliderRow> OnBuilt { get; }
        }

        /// <summary>
        /// Sliders one under another, each applied live: a scene draws a draft of every value the
        /// finger passes, which is what makes a slider worth dragging.
        /// </summary>
        private sealed class SliderStackBlock : Block
        {
            private readonly IReadOnlyList<SliderSpec> sliders;

            public SliderStackBlock(IReadOnlyList<SliderSpec> sliders)
            {
                this.sliders = sliders;
            }

            private static float Gap => UiTheme.PanelPx(UiTheme.RowSpacing);

            public override float Measure(float width) =>
                sliders.Count * SliderRow.MeasureHeight() + Mathf.Max(0, sliders.Count - 1) * Gap;

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var cursor = y;
                for (var i = 0; i < sliders.Count; i++)
                {
                    var spec = sliders[i];
                    var slider = SliderRow.Create(
                        content, spec.Label, spec.Minimum, spec.Maximum, spec.Value, x, cursor, width,
                        spec.OnChanged, null, logarithmic: spec.Logarithmic, format: spec.Format);
                    spec.OnBuilt?.Invoke(slider);
                    cursor -= SliderRow.MeasureHeight() + Gap;
                }
            }
        }

        /// <summary>
        /// A scene's light: the sun on the dial - where it shines from on the screen, as for the
        /// relief - with the ambient light and the highlight beside it.
        /// </summary>
        private sealed class SceneLightBlock : Block
        {
            private const float MaximumDial = 150f;

            private readonly string label;
            private readonly string ambientLabel;
            private readonly string specularLabel;
            private readonly SceneLook look;
            private readonly Action<Func<SceneLook, SceneLook>> change;
            private readonly SceneControls controls;

            public SceneLightBlock(
                string label, string ambientLabel, string specularLabel, in SceneLook look,
                Action<Func<SceneLook, SceneLook>> change, SceneControls controls)
            {
                this.label = label;
                this.ambientLabel = ambientLabel;
                this.specularLabel = specularLabel;
                this.look = look;
                this.change = change;
                this.controls = controls;
            }

            private static float CaptionHeight => UiTheme.PanelPx(20f);

            private static float Gap => UiTheme.PanelPx(UiTheme.RowSpacing);

            private static float DialSize(float width) => Mathf.Min(width * 0.42f, UiTheme.PanelPx(MaximumDial));

            private static float SlidersHeight => SliderRow.MeasureHeight() * 2f + Gap;

            public override float Measure(float width) => CaptionHeight + Gap + Mathf.Max(DialSize(width), SlidersHeight);

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var caption = UiFactory.CreateText(
                    "Label_" + label, content, label, UiTheme.LabelFontSize, UiTheme.TextMuted,
                    TextAnchor.LowerLeft, fitToRect: true, panelScale: true);
                Place(caption.rectTransform, x, y, width, CaptionHeight);

                var cursor = y - CaptionHeight - Gap;
                var dialSize = DialSize(width);
                var rowHeight = Mathf.Max(dialSize, SlidersHeight);
                controls.Dial = LightDial.Create(
                    content, x, cursor - (rowHeight - dialSize) * 0.5f, dialSize,
                    SceneLook.MinimumLightHeight, SceneLook.MaximumLightHeight);
                controls.Dial.SetLight(look.LightAngle, look.LightHeight);
                controls.Dial.Changed += (angle, height) => change(l =>
                {
                    l.LightAngle = angle;
                    l.LightHeight = height;
                    return l;
                });

                var slidersX = x + dialSize + Gap * 1.5f;
                var slidersWidth = width - dialSize - Gap * 1.5f;
                var slidersY = cursor - (rowHeight - SlidersHeight) * 0.5f;
                controls.Ambient = SliderRow.Create(
                    content, ambientLabel, 0d, 1d, look.Ambient, slidersX, slidersY, slidersWidth,
                    value => change(l =>
                    {
                        l.Ambient = (float)value;
                        return l;
                    }),
                    null, format: Percent);
                controls.Specular = SliderRow.Create(
                    content, specularLabel, 0d, 1.5d, look.Specular, slidersX, slidersY - SliderRow.MeasureHeight() - Gap,
                    slidersWidth,
                    value => change(l =>
                    {
                        l.Specular = (float)value;
                        return l;
                    }),
                    null, format: Percent);
            }

            private static string Percent(double value) =>
                Mathf.RoundToInt((float)value * 100f).ToString(System.Globalization.CultureInfo.InvariantCulture) + "%";
        }

        /// <summary>The palette grid with its two commands under it: change this one, make a new one.</summary>
        private sealed class PaletteGridBlock : Block
        {
            private readonly string label;
            private readonly IReadOnlyList<string> names;
            private readonly IReadOnlyList<Texture> strips;
            private readonly Action<int> onSelect;
            private readonly Action<PaletteGrid> onBuilt;
            private readonly string editLabel;
            private readonly Action edit;
            private readonly string createLabel;
            private readonly Action create;

            public PaletteGridBlock(
                string label, IReadOnlyList<string> names, IReadOnlyList<Texture> strips, Action<int> onSelect,
                Action<PaletteGrid> onBuilt, string editLabel, Action edit, string createLabel, Action create)
            {
                this.label = label;
                this.names = names;
                this.strips = strips;
                this.onSelect = onSelect;
                this.onBuilt = onBuilt;
                this.editLabel = editLabel;
                this.edit = edit;
                this.createLabel = createLabel;
                this.create = create;
            }

            public override float Measure(float width) =>
                PaletteGrid.MeasureHeight(names.Count, width) + UiTheme.PanelPx(UiTheme.RowSpacing) + ActionRow.MeasureHeight();

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var grid = PaletteGrid.Create(content, label, names, strips, onSelect, x, y, width);
                onBuilt?.Invoke(grid);

                var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
                var actionY = y - PaletteGrid.MeasureHeight(names.Count, width) - gap;
                var half = (width - gap) * 0.5f;
                ActionRow.Create(content, editLabel, x, actionY, half, edit, ActionStyle.Accent);
                ActionRow.Create(content, createLabel, x + half + gap, actionY, half, create);
            }
        }
    }
}
