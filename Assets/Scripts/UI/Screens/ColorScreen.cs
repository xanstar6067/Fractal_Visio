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

            blocks.Add(new PaletteGridBlock(
                Strings.Get("settings.palette"), Names(palettes, p => Strings.PaletteName(p)), textures, SelectPalette,
                grid => paletteGrid = grid,
                Strings.Get("settings.palette.edit"), openPaletteEditor,
                Strings.Get("settings.palette.new"), createPalette));
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.coloring"), Localize(ColoringKeys), SelectColoring, s => coloringSection = s));

            relief?.Dispose();
            relief = null;
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
            // A new palette is a new row: the panel changes shape, which is a rebuild.
            if (paletteVersion != builtPaletteVersion)
            {
                NeedsRebuild = true;
                return;
            }

            RefreshSelection();
        }

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
