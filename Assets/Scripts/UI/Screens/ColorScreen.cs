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

        private void RefreshSelection()
        {
            var session = Services.Session;
            paletteGrid?.SetSelected(Services.Palettes.IndexOf(session.Palette));
            coloringSection?.SetSelected(ColoringIndex(session.Coloring));
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
