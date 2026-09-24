using System;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The explorer's "C map" button, in the top-right corner opposite the gallery button: a live
    /// thumbnail of the map fractal with C marked on it, and the words. It is there only while the
    /// fractal on screen has a parameter plane (<see cref="IParameterPlane"/>).
    ///
    /// The thumbnail is the point of it. A Julia set's C is a place on the Mandelbrot set, and a
    /// button that shows that place - the set, with a dot on it - says so without a word of
    /// explanation, which a long press on the Mandelbrot set never did. Part of
    /// <see cref="ExplorerChrome"/>: it fades and hides with the rest of it.
    /// </summary>
    internal sealed class PlaneMapButton
    {
        private readonly AppServices services;
        private readonly Func<bool> isActive;
        private readonly Image selection;
        private readonly PlaneMap map;
        private readonly IFractalThumbnails thumbnails;
        private IFractalDefinition shownFor;
        private bool lit;

        private PlaneMapButton(AppServices services, GlassPanel surface, Image selection, PlaneMap map, Func<bool> isActive)
        {
            this.services = services;
            Surface = surface;
            this.selection = selection;
            this.map = map;
            this.isActive = isActive;
            thumbnails = services.Get<IFractalThumbnails>();
        }

        public GlassPanel Surface { get; }

        /// <summary>On screen for the fractal now showing.</summary>
        public bool IsShown => Surface.Root.gameObject.activeSelf;

        /// <summary>Its width in device pixels: the gallery button leaves room for it.</summary>
        public float Width { get; private set; }

        public static PlaneMapButton Build(RectTransform parent, AppServices services, Action onClick, Func<bool> isActive)
        {
            var height = UiTheme.TopBarHeight;
            var inset = Mathf.Round(height * 0.13f);
            var thumbHeight = height - inset * 2f;
            var thumbWidth = Mathf.Round(thumbHeight * 1.35f);

            var surface = GlassPanel.Create("PlaneMapButton", parent, UiTheme.SegmentHeight * 0.5f, UiTheme.ButtonTint, UiTheme.ButtonBorder);

            var selection = UiFactory.CreateImage(
                "Selection", surface.Content, UiSprites.Rounded(UiTheme.PxInt(UiTheme.SegmentHeight * 0.5f)), UiTheme.SegmentSelected);
            UiFactory.Stretch(selection.rectTransform);
            selection.enabled = false;

            // The thumbnail, in a rounded window at the capsule's left end.
            var thumbRadius = Mathf.Max(1, Mathf.RoundToInt(thumbHeight * 0.3f));
            var window = UiFactory.CreateImage("Thumb", surface.Content, UiSprites.Rounded(thumbRadius), new Color(0f, 0f, 0f, 0.4f));
            UiFactory.Anchor(
                window.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(inset, 0f), new Vector2(thumbWidth, thumbHeight));
            window.gameObject.AddComponent<Mask>().showMaskGraphic = true;
            var map = PlaneMap.Create(window.rectTransform, Mathf.Max(7f, UiTheme.Px(11f)), 256);

            var text = UiFactory.CreateText(
                "Label", surface.Content, services.Strings.Get("plane.button"), UiTheme.SegmentFontSize - 1, UiTheme.Text,
                TextAnchor.MiddleLeft);
            text.fontStyle = FontStyle.Bold;
            var textStart = inset + thumbWidth + height * 0.22f;
            var textEnd = height * 0.4f;
            var width = textStart + text.preferredWidth + textEnd;

            var labelRect = text.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(textStart, 0f);
            labelRect.offsetMax = new Vector2(-textEnd, 0f);

            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            UiFactory.Anchor(
                surface.Root, new Vector2(1f, 1f), new Vector2(1f, 1f),
                new Vector2(-(UiTheme.SafeRight + margin), -(UiTheme.SafeTop + margin)),
                new Vector2(width, height));

            ExplorerChrome.AddHitArea(surface.Root, UiTheme.SegmentHeight * 0.5f, onClick);

            var button = new PlaneMapButton(services, surface, selection, map, isActive) { Width = width };
            button.Refresh();
            return button;
        }

        /// <summary>Show or hide it for the fractal on screen, redraw the thumbnail if needed, move the dot, light it while the map is open.</summary>
        public void Refresh()
        {
            var definition = services.Session.Definition;
            if (!ReferenceEquals(definition, shownFor))
            {
                shownFor = definition;
                var planeDefinition = ParameterMapScreen.FindPlane(services, definition);
                var shown = planeDefinition != null;
                Surface.Root.gameObject.SetActive(shown);
                if (shown)
                {
                    map.SetPlane(planeDefinition);
                    map.Fit(((IParameterPlane)definition).PlaneBounds);
                }
            }

            if (!IsShown)
            {
                return;
            }

            var plane = (IParameterPlane)definition;
            var parameters = services.Session.Parameters;
            map.Tick(thumbnails, parameters.Get(plane.RealKey), parameters.Get(plane.ImaginaryKey));

            var active = isActive != null && isActive();
            if (active != lit)
            {
                lit = active;
                selection.enabled = active;
            }
        }

        public void Dispose()
        {
            map.Dispose();
        }
    }
}
