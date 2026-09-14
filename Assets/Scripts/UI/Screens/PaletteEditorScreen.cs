using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Edits a palette as a handful of colours around a ring, plus the two colouring numbers that
    /// decide how the ring is laid over the fractal: cycle length and offset.
    ///
    /// Every change is shown on the picture at once. That is affordable only because colour is not
    /// part of the render - the CPU path remaps the escape buffer it already holds - so dragging a
    /// hue slider at a depth where a render takes seconds still updates in milliseconds.
    ///
    /// Nothing is kept until Save. Closing the panel any other way puts the palette and colouring
    /// back as they were. A built-in palette is never overwritten: saving one creates a user
    /// palette from it.
    /// </summary>
    public sealed class PaletteEditorScreen : UiScreen
    {
        private const int MinimumStops = 2;
        private const int MaximumStops = 12;

        /// <summary>Remaps are cheap but not free; one per this many seconds while a slider moves.</summary>
        private const float ApplyIntervalSeconds = 0.06f;

        private readonly List<DraftStop> stops = new();
        private readonly List<Image> chips = new();
        private readonly List<Image> chipMarkers = new();

        private PaletteData originalPalette;
        private ColoringSettings originalColoring;
        private string draftId;
        private string draftName;
        private bool editingUserPalette;
        private bool editing;
        private bool committed;
        private int selected;

        private Texture2D previewTexture;
        private readonly Color32[] previewPixels = new Color32[PaletteData.Resolution];
        private SliderRow hueSlider;
        private SliderRow saturationSlider;
        private SliderRow brightnessSlider;
        private bool paletteDirty;
        private float lastApplyTime;

        protected override void OnBuild(Transform parent)
        {
            chips.Clear();
            chipMarkers.Clear();

            var width = ResolvePanelWidth(1, out _);
            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var sectionGap = UiTheme.PanelPx(UiTheme.SectionSpacing);
            var titleHeight = UiTheme.PanelPx(28f);
            var rowWidth = width - padding * 2f;
            var previewHeight = UiTheme.PanelPx(30f);
            var chipHeight = UiTheme.PanelPx(UiTheme.SegmentHeight);
            var halfWidth = (rowWidth - gap) * 0.5f;
            var sliderHeight = SliderRow.MeasureHeight();
            var buttonHeight = ActionRow.MeasureHeight();

            var contentHeight = padding + titleHeight + sectionGap +
                                previewHeight + gap + chipHeight + gap +
                                3f * (sliderHeight + gap) +
                                buttonHeight + sectionGap +
                                UiTheme.PanelPx(20f) + 2f * (gap + sliderHeight) + sectionGap +
                                buttonHeight +
                                (editingUserPalette ? gap + buttonHeight : 0f) +
                                padding;

            var content = CreateScrollingPanel(parent, "PaletteEditorPanel", width, contentHeight);
            AddTitle(content, string.IsNullOrEmpty(draftName) ? "Palette" : draftName, padding, width);

            var cursor = -(padding + titleHeight + sectionGap);

            // Preview of the whole ring as it will wrap over the fractal.
            EnsurePreviewTexture();
            var preview = UiFactory.CreateRawImage("Preview", content);
            preview.texture = previewTexture;
            Place(preview.rectTransform, padding, cursor, rowWidth, previewHeight);
            cursor -= previewHeight + gap;

            BuildChips(content, padding, cursor, rowWidth, chipHeight);
            cursor -= chipHeight + gap;

            var stop = stops.Count > 0 ? stops[Mathf.Clamp(selected, 0, stops.Count - 1)] : new DraftStop();
            hueSlider = SliderRow.Create(content, "Hue", 0d, 1d, stop.Hue, padding, cursor, rowWidth,
                value => EditSelected((s, v) => s.Hue = (float)v, value), null, format: v => Mathf.RoundToInt((float)v * 360f) + "°");
            cursor -= sliderHeight + gap;
            saturationSlider = SliderRow.Create(content, "Saturation", 0d, 1d, stop.Saturation, padding, cursor, rowWidth,
                value => EditSelected((s, v) => s.Saturation = (float)v, value), null, format: Percent);
            cursor -= sliderHeight + gap;
            brightnessSlider = SliderRow.Create(content, "Brightness", 0d, 1d, stop.Brightness, padding, cursor, rowWidth,
                value => EditSelected((s, v) => s.Brightness = (float)v, value), null, format: Percent);
            cursor -= sliderHeight + gap;

            ActionRow.Create(content, "Add colour", padding, cursor, halfWidth, AddStop);
            ActionRow.Create(content, "Remove", padding + halfWidth + gap, cursor, halfWidth, RemoveStop);
            cursor -= buttonHeight + sectionGap;

            var coloring = Services.Session.Coloring;
            cursor -= AddCaption(content, "COLOURING", padding, cursor, rowWidth) + gap;
            SliderRow.Create(content, "Cycle length", 4d, 2048d, coloring.CycleLength, padding, cursor, rowWidth,
                value => EditColoring(c => c.Value.CycleLength = (float)value), null, logarithmic: true, format: v => Mathf.RoundToInt((float)v).ToString());
            cursor -= sliderHeight + gap;
            SliderRow.Create(content, "Offset", 0d, 1d, coloring.Offset, padding, cursor, rowWidth,
                value => EditColoring(c => c.Value.Offset = (float)value), null, format: Percent);
            cursor -= sliderHeight + sectionGap;

            ActionRow.Create(content, "Cancel", padding, cursor, halfWidth, Close);
            ActionRow.Create(content, "Save", padding + halfWidth + gap, cursor, halfWidth, Save, ActionStyle.Accent);
            cursor -= buttonHeight + gap;

            if (editingUserPalette)
            {
                ActionRow.Create(content, "Delete palette", padding, cursor, rowWidth, DeletePalette, ActionStyle.Danger);
            }

            RefreshPreview();
        }

        protected override void OnOpenChanged(bool open)
        {
            if (open)
            {
                if (!editing)
                {
                    BeginEditing();
                }

                return;
            }

            if (editing && !committed)
            {
                // Closed without saving: put everything back.
                Services.Session.SetPalette(originalPalette);
                Services.Session.SetColoring(originalColoring);
            }

            editing = false;
        }

        protected override void OnTick()
        {
            if (paletteDirty && Time.unscaledTime - lastApplyTime >= ApplyIntervalSeconds)
            {
                ApplyDraft();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            if (previewTexture != null)
            {
                Object.Destroy(previewTexture);
                previewTexture = null;
            }
        }

        private void BeginEditing()
        {
            var session = Services.Session;
            originalPalette = session.Palette;
            originalColoring = session.Coloring;
            editingUserPalette = Services.Palettes.IsUserPalette(originalPalette);
            draftId = editingUserPalette ? originalPalette.Id : Services.Palettes.NewUserId();
            draftName = editingUserPalette ? originalPalette.DisplayName : NextCustomName();
            committed = false;
            editing = true;
            selected = 0;

            LoadStops(originalPalette);

            // The panel was built for the previous draft; its chip row is the wrong length now.
            NeedsRebuild = true;
        }

        private void LoadStops(PaletteData palette)
        {
            stops.Clear();
            var source = palette.Stops;
            var count = source.Count;

            // The closing stop at 1.0 that repeats the first colour is how a cyclic ramp is stored,
            // not a colour the user chose; the editor adds it back on save.
            if (count >= 2 && source[count - 1].Position >= 0.999f && SameColor(source[count - 1].Color, source[0].Color))
            {
                count--;
            }

            for (var i = 0; i < count; i++)
            {
                stops.Add(DraftStop.From(source[i].Position, source[i].Color));
            }

            if (stops.Count < MinimumStops)
            {
                // Built from raw colours: sample five evenly spaced ones to start from.
                stops.Clear();
                for (var i = 0; i < 5; i++)
                {
                    var position = i / 5f;
                    stops.Add(DraftStop.From(position, palette.Sample(position)));
                }
            }
        }

        private void BuildChips(RectTransform content, float x, float y, float width, float height)
        {
            var count = Mathf.Max(1, stops.Count);
            var gap = Mathf.Min(UiTheme.PanelPx(6f), width * 0.02f);
            var chipWidth = (width - gap * (count - 1)) / count;
            var radius = Mathf.Max(1, Mathf.RoundToInt(Mathf.Min(UiTheme.PanelPx(UiTheme.SegmentRadius), chipWidth * 0.3f)));

            for (var i = 0; i < stops.Count; i++)
            {
                var chip = UiFactory.CreateImage("Chip" + i, content, UiSprites.Rounded(radius), stops[i].ToColor());
                chip.raycastTarget = true;
                Place(chip.rectTransform, x + i * (chipWidth + gap), y, chipWidth, height);

                var marker = UiFactory.CreateImage(
                    "Selected", chip.transform, UiSprites.RoundedOutline(radius, Mathf.Max(2f, UiTheme.PanelPx(3f))), UiTheme.Text);
                UiFactory.Stretch(marker.rectTransform);

                var index = i;
                var button = chip.gameObject.AddComponent<Button>();
                button.targetGraphic = chip;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => Select(index));

                chips.Add(chip);
                chipMarkers.Add(marker);
            }

            RefreshChips();
        }

        private void Select(int index)
        {
            selected = Mathf.Clamp(index, 0, stops.Count - 1);
            var stop = stops[selected];
            hueSlider?.SetValue(stop.Hue);
            saturationSlider?.SetValue(stop.Saturation);
            brightnessSlider?.SetValue(stop.Brightness);
            RefreshChips();
        }

        private void EditSelected(System.Action<DraftStop, double> edit, double value)
        {
            if (selected < 0 || selected >= stops.Count)
            {
                return;
            }

            var stop = stops[selected];
            edit(stop, value);
            RefreshChips();
            RefreshPreview();
            paletteDirty = true;
        }

        private void EditColoring(System.Action<ColoringSettingsBox> edit)
        {
            var box = new ColoringSettingsBox { Value = Services.Session.Coloring };
            edit(box);
            Services.Session.SetColoring(box.Value);
        }

        private void AddStop()
        {
            if (stops.Count >= MaximumStops)
            {
                return;
            }

            // Halfway between the selected colour and the next one round the ring, in the colour the
            // ramp already has there - so adding a stop changes nothing until it is edited.
            var current = stops[selected].Position;
            var next = selected + 1 < stops.Count ? stops[selected + 1].Position : 1f;
            var position = (current + next) * 0.5f;
            var colour = BuildDraft().Sample(position);

            stops.Insert(selected + 1, DraftStop.From(position, colour));
            selected++;
            paletteDirty = true;
            NeedsRebuild = true;
        }

        private void RemoveStop()
        {
            if (stops.Count <= MinimumStops)
            {
                return;
            }

            stops.RemoveAt(selected);
            selected = Mathf.Clamp(selected, 0, stops.Count - 1);
            paletteDirty = true;
            NeedsRebuild = true;
        }

        private void Save()
        {
            var palette = BuildDraft();
            Services.Palettes.Save(palette);
            Services.Session.SetPalette(Services.Palettes.Find(palette.Id) ?? palette);
            committed = true;
            Close();
        }

        private void DeletePalette()
        {
            Services.Palettes.Remove(draftId);
            Services.Session.SetPalette(PaletteLibrary.Default);
            committed = true;
            Close();
        }

        private void ApplyDraft()
        {
            paletteDirty = false;
            lastApplyTime = Time.unscaledTime;
            Services.Session.SetPalette(BuildDraft());
        }

        private PaletteData BuildDraft()
        {
            var built = new PaletteData.ColorStop[stops.Count + 1];
            for (var i = 0; i < stops.Count; i++)
            {
                built[i] = new PaletteData.ColorStop(stops[i].Position, stops[i].ToColor());
            }

            // Close the ring on the first colour so the wrap is seamless.
            built[stops.Count] = new PaletteData.ColorStop(1f, stops.Count > 0 ? stops[0].ToColor() : new Color32(255, 255, 255, 255));
            return PaletteData.FromStops(draftId, draftName, built);
        }

        private void RefreshChips()
        {
            for (var i = 0; i < chips.Count && i < stops.Count; i++)
            {
                chips[i].color = stops[i].ToColor();
                chipMarkers[i].enabled = i == selected;
            }
        }

        private void RefreshPreview()
        {
            if (previewTexture == null || stops.Count == 0)
            {
                return;
            }

            var draft = BuildDraft();
            for (var i = 0; i < previewPixels.Length; i++)
            {
                previewPixels[i] = draft[i];
            }

            previewTexture.SetPixels32(previewPixels);
            previewTexture.Apply(false, false);
        }

        private void EnsurePreviewTexture()
        {
            if (previewTexture != null)
            {
                return;
            }

            previewTexture = new Texture2D(PaletteData.Resolution, 1, TextureFormat.RGBA32, false)
            {
                name = "Palette Preview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private string NextCustomName()
        {
            var count = 1;
            var all = Services.Palettes.All;
            for (var i = 0; i < all.Count; i++)
            {
                if (Services.Palettes.IsUserPalette(all[i]))
                {
                    count++;
                }
            }

            return "Custom " + count;
        }

        private static string Percent(double value) => Mathf.RoundToInt((float)value * 100f) + "%";

        private static bool SameColor(Color32 a, Color32 b) => a.r == b.r && a.g == b.g && a.b == b.b;

        /// <summary>One colour of the draft, kept as HSV so a grey stop does not forget its hue.</summary>
        private sealed class DraftStop
        {
            public float Position;
            public float Hue;
            public float Saturation;
            public float Brightness;

            public static DraftStop From(float position, Color32 color)
            {
                Color.RGBToHSV(color, out var h, out var s, out var v);
                return new DraftStop { Position = position, Hue = h, Saturation = s, Brightness = v };
            }

            public Color32 ToColor() => Color.HSVToRGB(Hue, Saturation, Brightness);
        }

        /// <summary>Lets a lambda edit a struct in place.</summary>
        private sealed class ColoringSettingsBox
        {
            public ColoringSettings Value;
        }
    }
}
