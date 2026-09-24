using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// The C map: the fractal on whose plane the active fractal's point lives - the Mandelbrot set
    /// under a Julia set - drawn large, with C marked on it (<see cref="IParameterPlane"/>). A tap puts
    /// C where it landed, a drag carries it, two fingers or the wheel move the map. Opened by the
    /// explorer's map button and from the fractal panel.
    ///
    /// Two things make it a picker rather than a picture:
    /// <list type="bullet">
    /// <item>While it is open the picture moves out from under it (<see cref="ShiftView"/>): the
    /// Julia set is centred in the part of the screen the panel leaves free, and put back on close
    /// unless the user moved it meanwhile. Choosing C is watching the Julia set change.</item>
    /// <item>C follows the finger live while the GPU draws the picture - a frame there costs a
    /// millisecond - and only on release while the CPU does, where every change restarts a render
    /// that would never finish mid-drag. Same rule as a parameter slider, applied per backend.</item>
    /// </list>
    /// </summary>
    public sealed class ParameterMapScreen : UiScreen
    {
        /// <summary>Width over height of the map: the Mandelbrot set is a little wider than tall.</summary>
        private const float MapAspect = 1.3f;

        /// <summary>
        /// Finest map height. The map is drawn in fp32 on the GPU; deeper than this it turns to noise
        /// before the finger could use the precision.
        /// </summary>
        private const double MinimumMapScale = 1e-4d;

        /// <summary>Furthest out the map goes, as a multiple of the whole-plane framing.</summary>
        private const double MaximumMapZoomOut = 2d;

        private const double ButtonZoom = 2d;

        private IFractalDefinition builtFor;
        private IParameterPlane plane;
        private IFractalDefinition planeDefinition;
        private IFractalThumbnails thumbnails;
        private PlaneMap map;
        private PlaneMapInput input;
        private Text readout;
        private GameObject wholeButton;
        private float panelWidth;
        private float panelHeight;

        // The map's view survives a rebuild (a rotation) for as long as the plane is the same.
        private string mapViewFor;
        private double mapX;
        private double mapY;
        private double mapScale;

        // C as the marker shows it: the session's, or the one under the finger while it is placed.
        private double pointX;
        private double pointY;
        private bool pointing;
        private bool pointDirty;
        private string shownFor;

        private bool shifted;
        private ViewState viewBeforeShift;
        private ViewState shiftedView;

        /// <summary>The fractal drawn as the map for <paramref name="definition"/>, or null when it has none.</summary>
        public static IFractalDefinition FindPlane(AppServices services, IFractalDefinition definition)
        {
            if (!(definition is IParameterPlane parameterPlane) || services == null)
            {
                return null;
            }

            var catalog = services.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Id == parameterPlane.PlaneFractalId)
                {
                    return catalog[i];
                }
            }

            return null;
        }

        protected override void OnBuild(Transform parent)
        {
            var definition = Services.Session.Definition;
            builtFor = definition;
            plane = definition as IParameterPlane;
            planeDefinition = FindPlane(Services, definition);
            thumbnails = Services.Get<IFractalThumbnails>();
            shownFor = null;

            // Nothing to pick on: no panel. The router never opens it for such a fractal.
            if (plane == null || planeDefinition == null)
            {
                return;
            }

            var padding = UiTheme.PanelPx(UiTheme.PanelPadding);
            var titleHeight = UiTheme.PanelPx(28f);
            var hintHeight = UiTheme.PanelPx(20f);
            var gap = UiTheme.PanelPx(10f);
            var chrome = padding + titleHeight + hintHeight + gap + padding;

            float mapWidth;
            float mapHeight;
            if (UiTheme.ToolbarVertical)
            {
                // Landscape: a column as tall as the room beside the toolbar, and no wider than half
                // the screen, so the picture keeps the other half.
                mapHeight = Mathf.Max(UiTheme.PanelPx(96f), UiTheme.AvailablePanelHeight - chrome);
                mapWidth = Mathf.Min(mapHeight * MapAspect, UiTheme.AvailablePanelWidth * 0.5f - padding * 2f);
                mapHeight = Mathf.Min(mapHeight, mapWidth / MapAspect * 1.15f);
            }
            else
            {
                // Portrait: a sheet across the screen, no taller than half the room above the
                // toolbar - the Julia set moves into the other half.
                mapWidth = Mathf.Min(UiTheme.AvailablePanelWidth, UiTheme.PanelPx(560f)) - padding * 2f;
                mapHeight = Mathf.Max(
                    UiTheme.PanelPx(96f),
                    Mathf.Min(mapWidth / MapAspect, UiTheme.AvailablePanelHeight * 0.5f - chrome));
            }

            panelWidth = mapWidth + padding * 2f;
            panelHeight = mapHeight + chrome;

            Panel = GlassPanel.Create("ParameterMap", parent, UiTheme.PanelRadius, UiTheme.PanelTint, UiTheme.PanelBorder);
            UiTheme.DockPanel(Panel.Root, panelWidth, panelHeight);
            var content = Panel.Content;

            var title = UiFactory.CreateText(
                "Title", content, Strings.Get("plane.title"), UiTheme.TitleFontSize, UiTheme.Text,
                TextAnchor.UpperLeft, fitToRect: true, panelScale: true);
            title.fontStyle = FontStyle.Bold;
            Place(title.rectTransform, padding, -padding, mapWidth * 0.46f, titleHeight);

            readout = UiFactory.CreateText(
                "Value", content, string.Empty, UiTheme.LabelFontSize + 1, UiTheme.Accent,
                TextAnchor.MiddleRight, fitToRect: true, panelScale: true);
            Place(readout.rectTransform, padding + mapWidth * 0.46f, -padding, mapWidth * 0.54f, titleHeight);

            var hint = UiFactory.CreateText(
                "Hint", content, Strings.Format("plane.hint", Strings.FractalName(planeDefinition)),
                UiTheme.LabelFontSize - 1, UiTheme.TextMuted, TextAnchor.MiddleLeft, fitToRect: true, panelScale: true);
            Place(hint.rectTransform, padding, -(padding + titleHeight), mapWidth, hintHeight);

            BuildMap(content, padding, -(padding + titleHeight + hintHeight + gap), mapWidth, mapHeight);
        }

        public override void Dispose()
        {
            map?.Dispose();
            map = null;
            input = null;
            readout = null;
            wholeButton = null;
            base.Dispose();
        }

        protected override void OnOpenChanged(bool open)
        {
            if (open)
            {
                if (!ReferenceEquals(Services.Session.Definition, builtFor))
                {
                    // Built for another fractal: rebuild first; the router reopens it, and this runs again.
                    NeedsRebuild = true;
                    return;
                }

                if (Panel != null)
                {
                    ShiftView();
                }

                return;
            }

            if (pointDirty)
            {
                Apply();
            }

            pointing = false;
            ReleaseShift();
        }

        protected override void OnTick()
        {
            if (!ReferenceEquals(Services.Session.Definition, builtFor))
            {
                NeedsRebuild = true;
                return;
            }

            if (map == null)
            {
                return;
            }

            if (pointing)
            {
                if (pointDirty && LiveUpdates)
                {
                    Apply();
                }
            }
            else if (!pointDirty)
            {
                // Nobody is placing it: show the session's C, which a preset or a bookmark may change.
                var parameters = Services.Session.Parameters;
                pointX = parameters.Get(plane.RealKey);
                pointY = parameters.Get(plane.ImaginaryKey);
            }

            map.Tick(thumbnails, pointX, pointY);
            RememberMapView();
            RefreshReadout();

            if (wholeButton != null)
            {
                var whole = Math.Abs(map.Scale - map.FittedScale(plane.PlaneBounds)) < map.Scale * 1e-6d &&
                            Math.Abs(map.CenterX - plane.PlaneBounds.CenterX) < map.Scale * 1e-6d &&
                            Math.Abs(map.CenterY - plane.PlaneBounds.CenterY) < map.Scale * 1e-6d;
                if (wholeButton.activeSelf == whole)
                {
                    wholeButton.SetActive(!whole);
                }
            }
        }

        /// <summary>
        /// Whether C may follow the finger. The GPU redraws the whole picture in a frame; the CPU
        /// starts over on every change and would show nothing but coarse passes until the finger
        /// lifted.
        /// </summary>
        private bool LiveUpdates => Services.Render == null || Services.Render.Status.Backend == RenderBackend.GpuFloat;

        private void BuildMap(RectTransform content, float x, float y, float width, float height)
        {
            var frame = UiFactory.CreateRect("MapFrame", content);
            Place(frame, x, y, width, height);

            // The map is clipped to rounded corners, and dark underneath until its first frame lands.
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);
            var clip = UiFactory.CreateImage("Clip", frame, UiSprites.Rounded(radius), new Color(0f, 0f, 0f, 0.35f));
            UiFactory.Stretch(clip.rectTransform);
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = true;

            var area = clip.rectTransform;
            map = PlaneMap.Create(area, UiTheme.PanelPx(26f), 1024);
            map.SetPlane(planeDefinition);
            map.Surface.raycastTarget = true;

            // Restore the map where the user left it, or show the whole plane. Both need the laid-out
            // rectangle, which exists once the frame is placed.
            if (mapViewFor == planeDefinition.Id && mapScale > 0d)
            {
                map.SetView(mapX, mapY, mapScale);
            }
            else
            {
                map.Fit(plane.PlaneBounds);
            }

            input = map.Surface.gameObject.AddComponent<PlaneMapInput>();
            input.Pointed = OnPointed;
            input.Released = OnReleased;
            input.Navigated = (from, to, ratio) =>
                map.Navigate(from, to, ratio, MinimumMapScale, map.FittedScale(plane.PlaneBounds) * MaximumMapZoomOut);

            var button = UiTheme.PanelPx(46f);
            var inset = UiTheme.PanelPx(8f);
            AddRoundButton(area, "ZoomIn", true, new Vector2(-inset, -inset), button, () => Zoom(ButtonZoom, true));
            AddRoundButton(area, "ZoomOut", false, new Vector2(-inset, -(inset * 2f + button)), button, () => Zoom(1d / ButtonZoom, false));
            wholeButton = AddWholeButton(area, inset, button);
        }

        private void OnPointed(Vector2 screenPoint)
        {
            map.PlanePoint(screenPoint, out var x, out var y);

            // A finger sets C to a map pixel, not to seventeen digits: rounded to the pixel, a C
            // reads as -0.1 + 0.83i rather than as the float noise of a screen coordinate.
            var decimals = PointDecimals;
            var definition = builtFor;
            pointX = Math.Round(Clamp(definition, plane.RealKey, x), decimals);
            pointY = Math.Round(Clamp(definition, plane.ImaginaryKey, y), decimals);
            pointing = true;
            pointDirty = true;
        }

        /// <summary>
        /// Decimals a finger can set on the map at its current zoom: one past the pixel. More would
        /// be noise, fewer would hide the change a small drag makes.
        /// </summary>
        private int PointDecimals =>
            Mathf.Clamp((int)Math.Ceiling(-Math.Log10(Math.Max(1e-15d, map.UnitsPerPixel))) + 1, 3, 12);

        private void OnReleased()
        {
            pointing = false;
            if (pointDirty)
            {
                Apply();
            }
        }

        /// <summary>Write C into the session: two parameters, one picture.</summary>
        private void Apply()
        {
            pointDirty = false;
            if (!ReferenceEquals(Services.Session.Definition, builtFor))
            {
                return;
            }

            Services.Session.SetParameter(plane.RealKey, pointX);
            Services.Session.SetParameter(plane.ImaginaryKey, pointY);
        }

        /// <summary>Zoom the map about C when it is in view and <paramref name="aboutPoint"/> asks for it, else about the middle.</summary>
        private void Zoom(double ratio, bool aboutPoint)
        {
            var rect = map.Area;
            var middle = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var pivot = middle;
            if (aboutPoint)
            {
                var marker = map.ScreenPoint(pointX, pointY);
                if (RectTransformUtility.RectangleContainsScreenPoint(rect, marker, null))
                {
                    // The marker comes to the middle as the map zooms in on it: the next "+" stays on it.
                    map.Navigate(marker, middle, ratio, MinimumMapScale, map.FittedScale(plane.PlaneBounds) * MaximumMapZoomOut);
                    return;
                }
            }

            map.Navigate(pivot, pivot, ratio, MinimumMapScale, map.FittedScale(plane.PlaneBounds) * MaximumMapZoomOut);
        }

        private void RememberMapView()
        {
            mapViewFor = planeDefinition.Id;
            mapX = map.CenterX;
            mapY = map.CenterY;
            mapScale = map.Scale;
        }

        /// <summary>"C = -0.80000 + 0.15600i", to the decimals a finger can set on the map (<see cref="PointDecimals"/>).</summary>
        private void RefreshReadout()
        {
            var decimals = PointDecimals;
            var key = pointX.ToString("R", CultureInfo.InvariantCulture) + "|" +
                      pointY.ToString("R", CultureInfo.InvariantCulture) + "|" + decimals;
            if (key == shownFor)
            {
                return;
            }

            shownFor = key;
            readout.text = Strings.Format("plane.value", FormatPoint(pointX, pointY, decimals));
        }

        /// <summary>A complex number in the invariant culture with fixed decimals: "-0.80000 + 0.15600i". Steady while it changes.</summary>
        public static string FormatPoint(double x, double y, int decimals)
        {
            return FormatPoint(x, y, "0." + new string('0', Mathf.Clamp(decimals, 1, 15)));
        }

        /// <summary>The same, as short as it can be: "-0.8 + 0.156i". For a value at rest.</summary>
        public static string FormatPoint(double x, double y)
        {
            return FormatPoint(x, y, "0.##########");
        }

        private static string FormatPoint(double x, double y, string format)
        {
            return x.ToString(format, CultureInfo.InvariantCulture) +
                   (y < 0d ? " - " : " + ") +
                   Math.Abs(y).ToString(format, CultureInfo.InvariantCulture) + "i";
        }

        private static double Clamp(IFractalDefinition definition, string key, double value)
        {
            var descriptors = definition.Parameters;
            for (var i = 0; i < descriptors.Count; i++)
            {
                if (descriptors[i].Key == key)
                {
                    return descriptors[i].Clamp(value);
                }
            }

            return value;
        }

        /// <summary>
        /// Move the picture so its centre sits in the middle of what the panel leaves free: above
        /// the sheet in portrait, left of the column in landscape. It is an ordinary pan of the
        /// session's view - the renderers see nothing new - and <see cref="ReleaseShift"/> undoes it.
        /// </summary>
        private void ShiftView()
        {
            if (shifted)
            {
                ReleaseShift();
            }

            var margin = UiTheme.Px(UiTheme.ScreenMargin);
            Vector2 shift;
            if (UiTheme.ToolbarVertical)
            {
                var panelLeft = Screen.width - (UiTheme.SafeRight + margin * 2f + UiTheme.ToolbarThickness) - panelWidth;
                shift = new Vector2((UiTheme.SafeLeft + panelLeft) * 0.5f - Screen.width * 0.5f, 0f);
            }
            else
            {
                var panelTop = UiTheme.SafeBottom + margin * 2f + UiTheme.ToolbarThickness + panelHeight;
                var barBottom = Screen.height - UiTheme.SafeTop - margin - UiTheme.TopBarHeight;
                shift = new Vector2(0f, (panelTop + barBottom) * 0.5f - Screen.height * 0.5f);
            }

            if (shift.sqrMagnitude < 1f)
            {
                return;
            }

            // The view first goes through the session's limits as it stands. A view made in the
            // other orientation can be wider than this one allows, and the next SetView clamps it:
            // the shift is in this screen's pixels, and would grow with the clamp if it came first.
            var session = Services.Session;
            session.SetView(session.View);
            viewBeforeShift = session.View;
            var view = viewBeforeShift;
            ViewNavigator.Pan(ref view, new Viewport(Screen.width, Screen.height), shift);
            session.SetView(view);
            shiftedView = session.View;
            shifted = true;
        }

        /// <summary>Put the picture back - unless the user has moved it since, in which case where they put it stands.</summary>
        private void ReleaseShift()
        {
            if (!shifted)
            {
                return;
            }

            shifted = false;
            var session = Services.Session;
            if (SameView(session.View, shiftedView))
            {
                session.SetView(viewBeforeShift);
            }
        }

        private static bool SameView(in ViewState a, in ViewState b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.scale.Equals(b.scale) && a.rotation == b.rotation;
        }

        /// <summary>A round "+" or "-" over the map, drawn from bars: no glyph to depend on in the runtime font.</summary>
        private static void AddRoundButton(RectTransform parent, string name, bool plus, Vector2 topRight, float size, Action onClick)
        {
            var disc = UiFactory.CreateImage(
                name, parent, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(size * 0.5f))), UiTheme.Scrim);
            disc.raycastTarget = true;
            UiFactory.Anchor(disc.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), topRight, new Vector2(size, size));

            var length = size * 0.4f;
            var thickness = Mathf.Max(2f, size * 0.08f);
            for (var i = 0; i < (plus ? 2 : 1); i++)
            {
                var bar = UiFactory.CreateImage(
                    "Bar" + i, disc.transform,
                    UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(thickness * 0.5f))), UiTheme.Text);
                UiFactory.Anchor(
                    bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                    i == 0 ? new Vector2(length, thickness) : new Vector2(thickness, length));
            }

            AttachButton(disc, onClick);
        }

        private GameObject AddWholeButton(RectTransform parent, float inset, float height)
        {
            var pill = UiFactory.CreateImage(
                "Whole", parent, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(height * 0.5f))), UiTheme.Scrim);
            pill.raycastTarget = true;

            var label = UiFactory.CreateText(
                "Label", pill.transform, Strings.Get("plane.whole"), UiTheme.LabelFontSize + 1, UiTheme.Text,
                TextAnchor.MiddleCenter, panelScale: true);
            var width = label.preferredWidth + height * 0.8f;
            UiFactory.Anchor(pill.rectTransform, Vector2.zero, Vector2.zero, new Vector2(inset, inset), new Vector2(width, height));
            UiFactory.Stretch(label.rectTransform);

            AttachButton(pill, () => map.Fit(plane.PlaneBounds));
            pill.gameObject.SetActive(false);
            return pill.gameObject;
        }

        private static void AttachButton(Image graphic, Action onClick)
        {
            var button = graphic.gameObject.AddComponent<Button>();
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = new ColorBlock
            {
                normalColor = Color.white,
                highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1.1f),
                pressedColor = new Color(1.6f, 1.6f, 1.6f, 1.3f),
                selectedColor = Color.white,
                disabledColor = new Color(1f, 1f, 1f, 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
            button.onClick.AddListener(() => onClick());
        }
    }
}
