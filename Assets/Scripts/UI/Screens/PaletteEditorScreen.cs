using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// Edits a palette: its stops on a gradient bar (tap to pick, drag sideways to move), the picked
    /// stop's colour on a square and hue strip, a few one-tap tools, and how the ring is laid over
    /// the fractal. The picker also edits the interior colour, which is part of the colouring rather
    /// than of the palette but is chosen the same way.
    ///
    /// Every change is shown on the picture at once. That is affordable only because colour is not
    /// part of the render - the CPU path remaps the escape buffer it already holds - so dragging a
    /// colour at a depth where a render takes seconds still updates in milliseconds.
    ///
    /// Nothing is kept until Save. Closing the panel any other way puts the palette and colouring
    /// back as they were. A built-in palette is never overwritten: saving one creates a user
    /// palette from it.
    /// </summary>
    public sealed class PaletteEditorScreen : UiScreen
    {
        private const int MinimumStops = 2;
        private const int MaximumStops = 12;
        private const int NameLimit = 32;
        private const int RecentLimit = 8;

        /// <summary>Remaps are cheap but not free; one per this many seconds while a finger moves.</summary>
        private const float ApplyIntervalSeconds = 0.06f;

        private enum EditTarget
        {
            Stop,
            Interior
        }

        /// <summary>Colours set by hand this run, newest first - across palettes, which is the point.</summary>
        private static readonly List<Color32> Recent = new();

        private readonly List<DraftStop> stops = new();
        private readonly List<float> stripPositions = new();
        private readonly List<Color32> stripColors = new();
        private readonly System.Random random = new();

        private PaletteData originalPalette;
        private ColoringSettings originalColoring;
        private string draftId;
        private string draftName;
        private bool draftBands;
        private DraftStop interior = new();
        private EditTarget target;
        private bool editingUserPalette;
        private bool editing;
        private bool committed;
        private int selected;

        /// <summary>Set before opening: start from a freshly generated palette instead of the current one.</summary>
        public bool StartFresh { get; set; }

        private Texture2D previewTexture;
        private readonly Color32[] previewPixels = new Color32[PaletteData.Resolution];
        private GradientStrip strip;
        private ColorPicker picker;
        private SegmentedRow targetRow;
        private SegmentedRow blendRow;
        private InputField nameInput;
        private InputField hexInput;
        private RectTransform recentRow;
        private float recentChipSize;
        private bool paletteDirty;
        private bool interiorDirty;
        private float lastApplyTime;

        protected override void OnBuild(Transform parent)
        {
            picker?.Dispose();

            var width = ResolvePanelWidth(2, out var columns);
            var padding = UiTheme.PanelInset(width, UiTheme.PanelPadding, 0.06f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var sectionGap = UiTheme.PanelPx(UiTheme.SectionSpacing);
            var inner = width - padding * 2f;
            var columnGap = sectionGap;
            var columnWidth = columns >= 2 ? (inner - columnGap) * 0.5f : inner;

            var nameHeight = TextInputRow.MeasureHeight();
            var stripHeight = GradientStrip.MeasureHeight();
            var buttonHeight = ActionRow.MeasureHeight();
            var segmentHeight = SegmentedRow.MeasureHeight();
            var captionHeight = UiTheme.PanelPx(20f);
            var sliderHeight = SliderRow.MeasureHeight();
            var pickerHeight = Mathf.Min(columnWidth * 0.62f, UiTheme.PanelPx(200f));

            var colourColumn = nameHeight + sectionGap + stripHeight + gap + buttonHeight + sectionGap +
                               segmentHeight + gap + pickerHeight + gap + buttonHeight;
            var settingsColumn = captionHeight + gap + buttonHeight + gap + segmentHeight + sectionGap +
                                 captionHeight + gap + 2f * (sliderHeight + gap) + sectionGap - gap +
                                 buttonHeight + (editingUserPalette ? gap + buttonHeight : 0f);
            var contentHeight = padding * 2f + (columns >= 2
                ? Mathf.Max(colourColumn, settingsColumn)
                : colourColumn + sectionGap + settingsColumn);

            var content = CreateScrollingPanel(parent, "PaletteEditorPanel", width, contentHeight);
            EnsurePreviewTexture();

            // Colour column: what the palette is.
            var x = padding;
            var cursor = -padding;
            var halfWidth = (columnWidth - gap) * 0.5f;

            nameInput = TextInputRow.Create(content, draftName, x, cursor, columnWidth, NameLimit);
            nameInput.onEndEdit.AddListener(Rename);
            cursor -= nameHeight + sectionGap;

            strip = GradientStrip.Create(content, previewTexture, x, cursor, columnWidth);
            strip.Selected += SelectStop;
            strip.Moved += MoveStop;
            cursor -= stripHeight + gap;

            ActionRow.Create(content, Strings.Get("palette_editor.add_colour"), x, cursor, halfWidth, AddStop);
            ActionRow.Create(content, Strings.Get("palette_editor.remove_colour"), x + halfWidth + gap, cursor, halfWidth, RemoveStop);
            cursor -= buttonHeight + sectionGap;

            targetRow = SegmentedRow.Create(
                content,
                new[] { Strings.Get("palette_editor.target_stop"), Strings.Get("palette_editor.target_interior") },
                x, cursor, columnWidth, SelectTarget);
            cursor -= segmentHeight + gap;

            picker = ColorPicker.Create(content, x, cursor, columnWidth, pickerHeight);
            picker.Changed += EditColour;
            picker.Released += RememberCurrent;
            cursor -= pickerHeight + gap;

            var hexWidth = Mathf.Min(columnWidth * 0.4f, UiTheme.PanelPx(130f));
            hexInput = TextInputRow.Create(content, string.Empty, x, cursor, hexWidth, 7);
            hexInput.onEndEdit.AddListener(ApplyHex);
            recentRow = UiFactory.CreateRect("Recent", content);
            Place(recentRow, x + hexWidth + gap, cursor, columnWidth - hexWidth - gap, buttonHeight);
            recentChipSize = buttonHeight;
            cursor -= buttonHeight;

            // Settings column: tools, then how the ring meets the fractal.
            if (columns >= 2)
            {
                x = padding + columnWidth + columnGap;
                cursor = -padding;
            }
            else
            {
                cursor -= sectionGap;
            }

            var thirdWidth = (columnWidth - gap * 2f) / 3f;
            cursor -= AddCaption(content, Strings.Get("palette_editor.tools"), x, cursor, columnWidth) + gap;
            ActionRow.Create(content, Strings.Get("palette_editor.reverse"), x, cursor, thirdWidth, Reverse);
            ActionRow.Create(content, Strings.Get("palette_editor.spread"), x + thirdWidth + gap, cursor, thirdWidth, SpreadEvenly);
            ActionRow.Create(content, Strings.Get("palette_editor.harmony"), x + 2f * (thirdWidth + gap), cursor, thirdWidth, Harmonize);
            cursor -= buttonHeight + gap;

            blendRow = SegmentedRow.Create(
                content,
                new[] { Strings.Get("palette_editor.smooth"), Strings.Get("palette_editor.bands") },
                x, cursor, columnWidth, SelectBlend);
            cursor -= segmentHeight + sectionGap;

            var coloring = Services.Session.Coloring;
            cursor -= AddCaption(content, Strings.Get("palette_editor.coloring"), x, cursor, columnWidth) + gap;
            SliderRow.Create(content, Strings.Get("palette_editor.cycle_length"), 4d, 2048d, coloring.CycleLength, x, cursor, columnWidth,
                value => EditColoring(c => c.Value.CycleLength = (float)value), null, logarithmic: true, format: v => Mathf.RoundToInt((float)v).ToString());
            cursor -= sliderHeight + gap;
            SliderRow.Create(content, Strings.Get("palette_editor.offset"), 0d, 1d, coloring.Offset, x, cursor, columnWidth,
                value => EditColoring(c => c.Value.Offset = (float)value), null, format: Percent);
            cursor -= sliderHeight + sectionGap;

            ActionRow.Create(content, Strings.Get("common.cancel"), x, cursor, halfWidth, Close);
            ActionRow.Create(content, Strings.Get("common.save"), x + halfWidth + gap, cursor, halfWidth, Save, ActionStyle.Accent);
            cursor -= buttonHeight + gap;

            if (editingUserPalette)
            {
                ActionRow.Create(content, Strings.Get("palette_editor.delete"), x, cursor, columnWidth, DeletePalette, ActionStyle.Danger);
            }

            RefreshAll();
            RefreshRecent();
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

            paletteDirty = false;
            interiorDirty = false;
            editing = false;
        }

        protected override void OnTick()
        {
            if ((paletteDirty || interiorDirty) && Time.unscaledTime - lastApplyTime >= ApplyIntervalSeconds)
            {
                ApplyDraft();
            }
        }

        public override void Dispose()
        {
            base.Dispose();
            picker?.Dispose();
            picker = null;
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
            var fresh = StartFresh;
            StartFresh = false;
            editingUserPalette = !fresh && Services.Palettes.IsUserPalette(originalPalette);
            draftId = editingUserPalette ? originalPalette.Id : Services.Palettes.NewUserId();
            draftName = editingUserPalette ? originalPalette.DisplayName : NextCustomName();
            draftBands = !fresh && originalPalette.Bands;
            interior = DraftStop.From(0f, originalColoring.InteriorColor);
            target = EditTarget.Stop;
            committed = false;
            editing = true;
            selected = 0;

            if (fresh)
            {
                stops.Clear();
                Harmonize();
            }
            else
            {
                LoadStops(originalPalette);
            }

            // The panel was built for the previous draft: its name, and whether it offers Delete.
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
            else if (stops[0].Position > 0.001f)
            {
                // The ring starts at the first stop, which the bar keeps at 0. A palette that begins
                // later gets a stop there in the colour it already has, so nothing changes.
                stops.Insert(0, DraftStop.From(0f, palette.Sample(0f)));
            }
        }

        private void SelectStop(int index)
        {
            target = EditTarget.Stop;
            selected = Mathf.Clamp(index, 0, stops.Count - 1);
            RefreshAll();
        }

        private void MoveStop(int index, float position)
        {
            if (index <= 0 || index >= stops.Count)
            {
                return;
            }

            stops[index].Position = position;
            paletteDirty = true;
            RefreshStrip();
            RefreshPreview();
        }

        private void SelectTarget(int index)
        {
            target = index == 1 ? EditTarget.Interior : EditTarget.Stop;
            RefreshAll();
        }

        private void SelectBlend(int index)
        {
            draftBands = index == 1;
            paletteDirty = true;
            RefreshAll();
        }

        private DraftStop Current =>
            target == EditTarget.Interior
                ? interior
                : stops[Mathf.Clamp(selected, 0, stops.Count - 1)];

        private void EditColour(float hue, float saturation, float brightness)
        {
            var current = Current;
            current.Hue = hue;
            current.Saturation = saturation;
            current.Brightness = brightness;
            MarkColourDirty();
            RefreshStrip();
            RefreshPreview();
            RefreshHex();
        }

        private void SetCurrentColour(Color32 colour)
        {
            var current = Current;
            var from = DraftStop.From(current.Position, colour);
            current.Hue = from.Hue;
            current.Saturation = from.Saturation;
            current.Brightness = from.Brightness;
            MarkColourDirty();
            RefreshAll();
        }

        private void MarkColourDirty()
        {
            if (target == EditTarget.Interior)
            {
                interiorDirty = true;
            }
            else
            {
                paletteDirty = true;
            }
        }

        private void RememberCurrent()
        {
            var colour = Current.ToColor();
            Recent.RemoveAll(c => SameColor(c, colour));
            Recent.Insert(0, colour);
            if (Recent.Count > RecentLimit)
            {
                Recent.RemoveRange(RecentLimit, Recent.Count - RecentLimit);
            }

            RefreshRecent();
        }

        private void ApplyHex(string text)
        {
            var value = (text ?? string.Empty).Trim();
            if (!value.StartsWith("#"))
            {
                value = "#" + value;
            }

            if (value.Length == 7 && ColorUtility.TryParseHtmlString(value, out var parsed))
            {
                SetCurrentColour(parsed);
                RememberCurrent();
            }
            else
            {
                RefreshHex();
            }
        }

        private void Rename(string text)
        {
            var name = (text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                nameInput?.SetTextWithoutNotify(draftName);
                return;
            }

            draftName = name;
            paletteDirty = true;
        }

        private void AddStop()
        {
            if (stops.Count >= MaximumStops)
            {
                return;
            }

            // Halfway between the selected colour and the next one round the ring, in the colour the
            // ramp already has there - so adding a stop changes nothing until it is edited.
            var from = target == EditTarget.Stop ? selected : stops.Count - 1;
            var current = stops[from].Position;
            var next = from + 1 < stops.Count ? stops[from + 1].Position : 1f;
            var position = (current + next) * 0.5f;
            var colour = BuildDraft().Sample(position);

            stops.Insert(from + 1, DraftStop.From(position, colour));
            selected = from + 1;
            target = EditTarget.Stop;
            paletteDirty = true;
            RefreshAll();
        }

        private void RemoveStop()
        {
            if (stops.Count <= MinimumStops || target != EditTarget.Stop)
            {
                return;
            }

            stops.RemoveAt(selected);

            // The ring starts at 0; if its first stop went, the next one takes its place.
            stops[0].Position = 0f;
            selected = Mathf.Clamp(selected, 0, stops.Count - 1);
            paletteDirty = true;
            RefreshAll();
        }

        /// <summary>Run the ring the other way. The first stop stays where the ring starts.</summary>
        private void Reverse()
        {
            if (stops.Count < 3)
            {
                return;
            }

            var tail = stops.GetRange(1, stops.Count - 1);
            tail.Reverse();
            for (var i = 0; i < tail.Count; i++)
            {
                tail[i].Position = 1f - tail[i].Position;
                stops[i + 1] = tail[i];
            }

            if (selected > 0)
            {
                selected = stops.Count - selected;
            }

            paletteDirty = true;
            RefreshAll();
        }

        private void SpreadEvenly()
        {
            for (var i = 0; i < stops.Count; i++)
            {
                stops[i].Position = i / (float)stops.Count;
            }

            paletteDirty = true;
            RefreshAll();
        }

        private void Harmonize()
        {
            var count = stops.Count >= 3 ? Mathf.Min(stops.Count, 8) : 5;
            var colours = PaletteHarmony.Generate(count, random);
            stops.Clear();
            for (var i = 0; i < colours.Length; i++)
            {
                stops.Add(new DraftStop
                {
                    Position = i / (float)colours.Length,
                    Hue = colours[i].x,
                    Saturation = colours[i].y,
                    Brightness = colours[i].z
                });
            }

            selected = 0;
            target = EditTarget.Stop;
            paletteDirty = true;
            RefreshAll();
        }

        private void EditColoring(System.Action<ColoringSettingsBox> edit)
        {
            var box = new ColoringSettingsBox { Value = Services.Session.Coloring };
            edit(box);
            Services.Session.SetColoring(box.Value);
        }

        private void Save()
        {
            ApplyDraft();
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
            Services.Session.SetColoring(originalColoring);
            committed = true;
            Close();
        }

        private void ApplyDraft()
        {
            lastApplyTime = Time.unscaledTime;
            if (paletteDirty)
            {
                paletteDirty = false;
                Services.Session.SetPalette(BuildDraft());
            }

            if (interiorDirty)
            {
                interiorDirty = false;
                var coloring = Services.Session.Coloring;
                coloring.InteriorColor = interior.ToColor();
                Services.Session.SetColoring(coloring);
            }
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
            return PaletteData.FromStops(draftId, draftName, draftBands, built);
        }

        private void RefreshAll()
        {
            // The router builds every screen at start-up, long before the editor is first opened.
            if (stops.Count == 0)
            {
                return;
            }

            RefreshStrip();
            RefreshPreview();
            RefreshHex();
            var current = Current;
            picker?.SetColor(current.Hue, current.Saturation, current.Brightness);
            targetRow?.SetSelected(target == EditTarget.Interior ? 1 : 0);
            blendRow?.SetSelected(draftBands ? 1 : 0);
        }

        private void RefreshStrip()
        {
            if (strip == null)
            {
                return;
            }

            stripPositions.Clear();
            stripColors.Clear();
            for (var i = 0; i < stops.Count; i++)
            {
                stripPositions.Add(stops[i].Position);
                stripColors.Add(stops[i].ToColor());
            }

            strip.SetStops(stripPositions, stripColors, target == EditTarget.Stop ? selected : -1);
        }

        private void RefreshHex()
        {
            if (hexInput != null && stops.Count > 0)
            {
                hexInput.SetTextWithoutNotify("#" + ColorUtility.ToHtmlStringRGB(Current.ToColor()));
            }
        }

        private void RefreshRecent()
        {
            if (recentRow == null)
            {
                return;
            }

            for (var i = recentRow.childCount - 1; i >= 0; i--)
            {
                Object.Destroy(recentRow.GetChild(i).gameObject);
            }

            var gap = UiTheme.PanelPx(UiTheme.RowSpacing) * 0.75f;
            var size = recentChipSize;
            var fits = Mathf.FloorToInt((recentRow.rect.width + gap) / (size + gap));
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            for (var i = 0; i < Recent.Count && i < fits; i++)
            {
                var colour = Recent[i];
                var chip = UiFactory.CreateImage("Recent" + i, recentRow, UiSprites.Rounded(radius), colour);
                chip.raycastTarget = true;
                Place(chip.rectTransform, i * (size + gap), 0f, size, size);
                var button = chip.gameObject.AddComponent<Button>();
                button.targetGraphic = chip;
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => SetCurrentColour(colour));
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

            return Strings.Format("palette_editor.custom_name", count);
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
