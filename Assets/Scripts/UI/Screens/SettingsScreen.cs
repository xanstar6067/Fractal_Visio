using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;

namespace FractalVisio.UI
{
    /// <summary>
    /// App settings: how the picture is computed and how the interface behaves - not what is on
    /// screen. The fractal and its parameters live in <see cref="FractalScreen"/>, palette and
    /// colouring in <see cref="ColorScreen"/>; this panel is the same from the gallery and from the
    /// explorer.
    /// </summary>
    public sealed class SettingsScreen : BlockScreen
    {
        /// <summary>Render resolution as a fraction of the screen; 0 means the device profile decides.</summary>
        private static readonly float[] ResolutionScales = { 0f, 0.5f, 0.75f, 1f };

        /// <summary>Percentages read the same in every language; only "Auto" is a word.</summary>
        private static readonly string[] ResolutionPercentNames = { "50%", "75%", "100%" };

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

        private static readonly Vector2Int[] ScreenshotSizes =
        {
            new(0, 0), new(1280, 720), new(1920, 1080), new(2560, 1440), new(3840, 2160)
        };

        private SettingsSection resolutionSection;
        private SettingsSection interfaceSection;
        private SettingsSection inertiaSection;
        private SettingsSection languageSection;
        private SettingsSection debugSection;
        private SettingsSection screenshotSection;
        private InputField screenshotInput;

        protected override string BuildTitle() => Strings.Get("settings.title");

        protected override void CollectBlocks(List<Block> blocks)
        {
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.resolution"), ResolutionNames(), SelectResolution, s => resolutionSection = s));
            blocks.Add(new ScreenshotCustomBlock(this));
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.inertia"), Localize(InertiaKeys), SelectInertia, s => inertiaSection = s));
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.interface_size"), InterfaceNames, SelectInterfaceScale, s => interfaceSection = s));
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.language"), LanguageNames(), SelectLanguage, s => languageSection = s));
            blocks.Add(new OptionsBlock(
                Strings.Get("settings.debug_info"), Localize(BoolKeys), SelectDebugInfo, s => debugSection = s));
        }

        protected override void OnBuild(Transform parent)
        {
            base.OnBuild(parent);
            RefreshSelection();
        }

        protected override void OnTick()
        {
            RefreshSelection();
        }

        private string[] ResolutionNames()
        {
            var names = new string[ResolutionPercentNames.Length + 1];
            names[0] = Strings.Get("settings.resolution.auto");
            ResolutionPercentNames.CopyTo(names, 1);
            return names;
        }

        private string[] ScreenshotNames()
        {
            var names = new string[ScreenshotSizes.Length];
            names[0] = Strings.Get("settings.screenshot_native");
            for (var i = 1; i < names.Length; i++)
            {
                names[i] = ScreenshotSizes[i].x + " × " + ScreenshotSizes[i].y;
            }

            return names;
        }

        private void SelectScreenshotSize(int index)
        {
            if (index < 0 || index >= ScreenshotSizes.Length)
            {
                return;
            }

            SetScreenshotSize(ScreenshotSizes[index].x, ScreenshotSizes[index].y);
        }

        private void SetScreenshotSize(int width, int height)
        {
            var settings = Services.Session.Interface;
            settings.ScreenshotWidth = width;
            settings.ScreenshotHeight = height;
            Services.Session.SetInterface(settings);
        }

        private void ApplyCustomScreenshotSize()
        {
            if (screenshotInput == null)
            {
                return;
            }

            var parts = screenshotInput.text.ToLowerInvariant().Replace('×', 'x').Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out var width) ||
                !int.TryParse(parts[1].Trim(), out var height) ||
                width < 16 || height < 16 || width > 8192 || height > 8192 ||
                (long)width * height > 16000000 ||
                width > SystemInfo.maxTextureSize || height > SystemInfo.maxTextureSize)
            {
                screenshotInput.text = Strings.Get("settings.screenshot_invalid");
                return;
            }

            SetScreenshotSize(width, height);
            screenshotInput.text = width + " × " + height;
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

        private void SelectDebugInfo(int index)
        {
            var settings = Services.Session.Interface;
            settings.ShowDebugInfo = index == 1;
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
            resolutionSection?.SetSelected(NearestIndex(ResolutionScales, session.Quality.RenderScale));
            interfaceSection?.SetSelected(NearestIndex(InterfaceScales, session.Interface.Scale));
            inertiaSection?.SetSelected(NearestIndex(InertiaLengths, session.Interface.InertiaSeconds));
            languageSection?.SetSelected(LanguageIndex(session.Interface.Language));
            debugSection?.SetSelected(session.Interface.ShowDebugInfo ? 1 : 0);
            var selected = -1;
            for (var i = 0; i < ScreenshotSizes.Length; i++)
            {
                if (session.Interface.ScreenshotWidth == ScreenshotSizes[i].x &&
                    session.Interface.ScreenshotHeight == ScreenshotSizes[i].y)
                {
                    selected = i;
                    break;
                }
            }

            screenshotSection?.SetSelected(selected);
        }

        private sealed class ScreenshotCustomBlock : Block
        {
            private readonly SettingsScreen owner;

            public ScreenshotCustomBlock(SettingsScreen owner) => this.owner = owner;

            public override float Measure(float width) =>
                SettingsSection.MeasureHeight(ScreenshotSizes.Length) +
                ActionRow.MeasureHeight() * 2f + UiTheme.PanelPx(UiTheme.RowSpacing) * 2f;

            public override void Build(RectTransform content, float x, float y, float width)
            {
                var section = SettingsSection.Create(content,
                    owner.Strings.Get("settings.screenshot_resolution"), owner.ScreenshotNames(),
                    owner.SelectScreenshotSize, x, y, width);
                owner.screenshotSection = section;
                y -= section.Height + UiTheme.PanelPx(UiTheme.RowSpacing);
                var height = ActionRow.MeasureHeight();
                var background = UiFactory.CreateImage("ScreenshotCustomSize", content,
                    UiSprites.Rounded(UiTheme.PanelPxInt(UiTheme.SegmentRadius)), UiTheme.SegmentIdle);
                background.raycastTarget = true;
                var rect = background.rectTransform;
                rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.anchoredPosition = new Vector2(x, y);
                rect.sizeDelta = new Vector2(width, height);

                var inputText = UiFactory.CreateText("Value", background.transform, string.Empty,
                    UiTheme.SegmentFontSize, UiTheme.Text, TextAnchor.MiddleLeft, panelScale: true);
                UiFactory.Stretch(inputText.rectTransform, UiTheme.PanelPx(12f));
                var input = background.gameObject.AddComponent<InputField>();
                input.targetGraphic = background;
                input.textComponent = inputText;
                input.contentType = InputField.ContentType.Standard;
                input.keyboardType = TouchScreenKeyboardType.NumbersAndPunctuation;
                input.characterLimit = 11;
                var current = owner.Services.Session.Interface;
                input.text = current.ScreenshotWidth > 0
                    ? current.ScreenshotWidth + " × " + current.ScreenshotHeight
                    : "1920 × 1080";
                input.onSelect.AddListener(_ =>
                {
                    if (input.text == owner.Strings.Get("settings.screenshot_invalid"))
                    {
                        input.text = string.Empty;
                    }
                });
                owner.screenshotInput = input;

                ActionRow.Create(content, owner.Strings.Get("settings.screenshot_apply"),
                    x, y - height - UiTheme.PanelPx(UiTheme.RowSpacing), width,
                    owner.ApplyCustomScreenshotSize, ActionStyle.Accent);
            }
        }
    }
}
