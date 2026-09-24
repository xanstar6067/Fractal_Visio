using System;
using UnityEngine;
using UnityEngine.UI;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.UI
{
    /// <summary>
    /// A drawn map of a parameter plane with a marker on one point of it: the Mandelbrot set under
    /// a Julia set, with the Julia set's C marked. Used twice - as the thumbnail on the explorer's
    /// map button and as the large map in the C panel.
    ///
    /// It draws itself through <see cref="IFractalThumbnails.Draw"/> into a render texture of its
    /// own, sized to its rectangle, and redraws only when its view, its size or the palette changed
    /// - or when the texture's contents were lost with the graphics context. It knows nothing about
    /// input: the C panel moves its view and reads points off it.
    ///
    /// The view is a centre and a height in plane units, like a <see cref="ViewState"/>, and the
    /// mapping is the shader's own (<c>FractalPlanePoint</c>): the marker sits exactly on the
    /// pixel the point was drawn at.
    /// </summary>
    public sealed class PlaneMap
    {
        private readonly RectTransform area;
        private readonly RawImage image;
        private readonly RectTransform marker;
        private readonly int maximumTexturePixels;

        private RenderTexture texture;
        private IFractalDefinition definition;
        private FractalParameterSet parameters = FractalParameterSet.Empty;
        private double centerX;
        private double centerY;
        private double scale = 1d;
        private bool dirty = true;
        private bool supported = true;
        private int drawnColoring = int.MinValue;

        private PlaneMap(RectTransform area, RawImage image, RectTransform marker, int maximumTexturePixels)
        {
            this.area = area;
            this.image = image;
            this.marker = marker;
            this.maximumTexturePixels = maximumTexturePixels;
        }

        /// <summary>The rectangle the map fills.</summary>
        public RectTransform Area => area;

        /// <summary>The picture itself. Make it a raycast target to put input on the map.</summary>
        public RawImage Surface => image;

        public double CenterX => centerX;
        public double CenterY => centerY;

        /// <summary>Height of the map in plane units.</summary>
        public double Scale => scale;

        /// <summary>Width over height of the map's rectangle.</summary>
        public double Aspect
        {
            get
            {
                var rect = area.rect;
                return rect.height > 0f ? rect.width / (double)rect.height : 1d;
            }
        }

        /// <summary>Plane units per device pixel: the finest step a finger can make on the map.</summary>
        public double UnitsPerPixel => scale / Math.Max(1f, area.rect.height);

        /// <param name="area">Rectangle to fill; laid out by the caller.</param>
        /// <param name="markerSize">Marker diameter, device pixels.</param>
        /// <param name="maximumTexturePixels">Cap on the texture's long side. The picture is soft past it, not slow.</param>
        public static PlaneMap Create(RectTransform area, float markerSize, int maximumTexturePixels)
        {
            var image = UiFactory.CreateRawImage("Map", area);
            UiFactory.Stretch(image.rectTransform);
            image.enabled = false;

            var marker = BuildMarker(area, markerSize);
            return new PlaneMap(area, image, marker, Mathf.Max(32, maximumTexturePixels));
        }

        /// <summary>The fractal drawn as the map. Its own defaults are its parameters.</summary>
        public void SetPlane(IFractalDefinition value)
        {
            if (ReferenceEquals(value, definition))
            {
                return;
            }

            definition = value;
            parameters = value != null ? FractalParameterSet.Defaults(value.Parameters) : FractalParameterSet.Empty;
            supported = true;
            dirty = true;
        }

        public void SetView(double x, double y, double height)
        {
            if (!(height > 0d) || double.IsNaN(x) || double.IsNaN(y) || double.IsInfinity(height))
            {
                return;
            }

            if (x == centerX && y == centerY && height == scale)
            {
                return;
            }

            centerX = x;
            centerY = y;
            scale = height;
            dirty = true;
        }

        /// <summary>The height at which <paramref name="bounds"/> fits inside the map, whole.</summary>
        public double FittedScale(in PlaneRect bounds)
        {
            return Math.Max(bounds.Height, bounds.Width / Math.Max(0.01d, Aspect));
        }

        /// <summary>Show all of <paramref name="bounds"/>, centred.</summary>
        public void Fit(in PlaneRect bounds)
        {
            SetView(bounds.CenterX, bounds.CenterY, FittedScale(bounds));
        }

        /// <summary>
        /// Two-finger move: the plane point under <paramref name="previousPivot"/> ends up under
        /// <paramref name="currentPivot"/>, and the map is zoomed by <paramref name="ratio"/> about it.
        /// Same arithmetic as <see cref="ViewNavigator.PinchZoomRotate"/>, without the rotation.
        /// </summary>
        public void Navigate(Vector2 previousPivot, Vector2 currentPivot, double ratio, double minimumScale, double maximumScale)
        {
            PlanePoint(previousPivot, out var anchorX, out var anchorY);
            var nextScale = Math.Clamp(scale / Math.Max(0.01d, ratio), minimumScale, maximumScale);
            var previousScale = scale;
            scale = nextScale;
            PlanePoint(currentPivot, out var movedX, out var movedY);
            scale = previousScale;
            SetView(centerX + anchorX - movedX, centerY + anchorY - movedY, nextScale);
        }

        /// <summary>The plane point under a screen point. True when the point is on the map.</summary>
        public bool PlanePoint(Vector2 screenPoint, out double x, out double y)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, screenPoint, null, out var local);
            var rect = area.rect;
            var u = (local.x - rect.xMin) / Mathf.Max(1f, rect.width);
            var v = (local.y - rect.yMin) / Mathf.Max(1f, rect.height);
            x = centerX + (u - 0.5d) * Aspect * scale;
            y = centerY + (v - 0.5d) * scale;
            return u >= 0f && u <= 1f && v >= 0f && v <= 1f;
        }

        /// <summary>Where a plane point is on screen, in pixels. For zooming about the marker.</summary>
        public Vector2 ScreenPoint(double x, double y)
        {
            var rect = area.rect;
            var u = (float)((x - centerX) / (Aspect * scale) + 0.5d);
            var v = (float)((y - centerY) / scale + 0.5d);
            var local = new Vector3(rect.xMin + u * rect.width, rect.yMin + v * rect.height, 0f);
            return RectTransformUtility.WorldToScreenPoint(null, area.TransformPoint(local));
        }

        /// <summary>Redraw if anything changed, and put the marker on (<paramref name="markerX"/>, <paramref name="markerY"/>).</summary>
        public void Tick(IFractalThumbnails thumbnails, double markerX, double markerY)
        {
            Redraw(thumbnails);
            PlaceMarker(markerX, markerY);
        }

        public void Dispose()
        {
            Release();
        }

        private void Redraw(IFractalThumbnails thumbnails)
        {
            if (thumbnails == null || definition == null || !supported)
            {
                return;
            }

            var rect = area.rect;
            if (rect.width < 2f || rect.height < 2f)
            {
                return;
            }

            // The texture keeps the rectangle's shape, at most maximumTexturePixels on its long side:
            // the GPU renderer takes its aspect from the target.
            var shrink = Mathf.Min(1f, maximumTexturePixels / Mathf.Max(rect.width, rect.height));
            var width = Mathf.Max(2, Mathf.RoundToInt(rect.width * shrink));
            var height = Mathf.Max(2, Mathf.RoundToInt(rect.height * shrink));

            var reshaped = texture == null || texture.width != width || texture.height != height;
            var lost = !reshaped && !texture.IsCreated();
            if (!reshaped && !lost && !dirty && drawnColoring == thumbnails.ColoringVersion)
            {
                return;
            }

            if (reshaped)
            {
                Release();
                texture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32)
                {
                    name = "Plane map",
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    useMipMap = false,
                    autoGenerateMips = false,
                    hideFlags = HideFlags.HideAndDontSave
                };
            }

            if (!texture.IsCreated())
            {
                texture.Create();
            }

            var view = new ViewState
            {
                x = HighPrecision.FromDouble(centerX),
                y = HighPrecision.FromDouble(centerY),
                scale = HighPrecision.FromDouble(scale),
                rotation = 0d
            };

            supported = thumbnails.Draw(definition, parameters, view, texture);
            image.texture = texture;
            image.enabled = supported;
            dirty = false;
            drawnColoring = thumbnails.ColoringVersion;
        }

        private void PlaceMarker(double x, double y)
        {
            var rect = area.rect;
            var u = (x - centerX) / (Aspect * scale) + 0.5d;
            var v = (y - centerY) / scale + 0.5d;
            var visible = u >= 0d && u <= 1d && v >= 0d && v <= 1d && !double.IsNaN(u) && !double.IsNaN(v);
            if (marker.gameObject.activeSelf != visible)
            {
                marker.gameObject.SetActive(visible);
            }

            if (visible)
            {
                marker.anchoredPosition = new Vector2((float)u * rect.width, (float)v * rect.height);
            }
        }

        /// <summary>
        /// A ring with a dot, white over a dark halo: it has to read over any palette, and the ring
        /// leaves the point itself - the structure C is being chosen by - visible.
        /// </summary>
        private static RectTransform BuildMarker(RectTransform parent, float size)
        {
            var root = UiFactory.CreateRect("Marker", parent);
            root.anchorMin = Vector2.zero;
            root.anchorMax = Vector2.zero;
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(size, size);

            var halo = UiFactory.CreateImage(
                "Halo", root,
                UiSprites.RoundedOutline(Mathf.Max(1, Mathf.RoundToInt(size * 0.5f)), Mathf.Max(2f, size * 0.24f)),
                new Color(0f, 0f, 0f, 0.6f));
            UiFactory.Stretch(halo.rectTransform);

            var ringSize = size * 0.84f;
            var ring = UiFactory.CreateImage(
                "Ring", root,
                UiSprites.RoundedOutline(Mathf.Max(1, Mathf.RoundToInt(ringSize * 0.5f)), Mathf.Max(1.5f, size * 0.1f)),
                Color.white);
            UiFactory.Anchor(ring.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(ringSize, ringSize));

            var dotSize = Mathf.Max(3f, size * 0.2f);
            var dot = UiFactory.CreateImage(
                "Dot", root, UiSprites.Rounded(Mathf.Max(1, Mathf.RoundToInt(dotSize * 0.5f))), Color.white);
            UiFactory.Anchor(dot.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(dotSize, dotSize));

            root.gameObject.SetActive(false);
            return root;
        }

        private void Release()
        {
            if (texture == null)
            {
                return;
            }

            if (image != null)
            {
                image.texture = null;
            }

            texture.Release();
            UnityEngine.Object.Destroy(texture);
            texture = null;
        }
    }
}
