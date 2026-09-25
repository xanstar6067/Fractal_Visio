using System;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// A colour as a point on a square - saturation across, brightness up - beside a strip of hues.
    /// One finger reaches any colour in one movement, where three HSV sliders took three, and the
    /// square shows what is around the colour before the finger goes there.
    ///
    /// Both parts answer on touch-down, not after the drag threshold: a colour control that waits
    /// for the finger to move feels broken. That makes them 2D controls a vertical drag cannot
    /// scroll the panel through; the rows above and below are where the panel scrolls.
    /// </summary>
    public sealed class ColorPicker : IDisposable
    {
        private const int SquareResolution = 32;
        private const int HueResolution = 96;
        private const float StripWidth = 44f;
        private const float KnobSize = 26f;

        private readonly Texture2D squareTexture;
        private readonly Texture2D hueTexture;
        private readonly Color32[] squarePixels = new Color32[SquareResolution * SquareResolution];
        private readonly RectTransform square;
        private readonly RectTransform knob;
        private readonly Image knobFill;
        private readonly RectTransform hueMarker;
        private float hue = -1f;
        private float saturation;
        private float brightness;
        private float hueStripTop;
        private float hueStripHeight;

        private ColorPicker(
            Texture2D squareTexture, Texture2D hueTexture, RectTransform square,
            RectTransform knob, Image knobFill, RectTransform hueMarker)
        {
            this.squareTexture = squareTexture;
            this.hueTexture = hueTexture;
            this.square = square;
            this.knob = knob;
            this.knobFill = knobFill;
            this.hueMarker = hueMarker;
        }

        /// <summary>Hue, saturation and brightness while the finger moves.</summary>
        public event Action<float, float, float> Changed;

        /// <summary>The finger lifted: a colour was chosen, not just passed through.</summary>
        public event Action Released;

        public static ColorPicker Create(RectTransform parent, float x, float y, float width, float height)
        {
            var stripWidth = Mathf.Min(UiTheme.PanelPx(StripWidth), width * 0.2f);
            var gap = UiTheme.PanelPx(UiTheme.RowSpacing);
            var squareWidth = width - stripWidth - gap;
            var radius = UiTheme.PanelPxInt(UiTheme.SegmentRadius);

            var squareTexture = CreateTexture("Colour Square", SquareResolution, SquareResolution);
            var hueTexture = CreateTexture("Hue Strip", 1, HueResolution);
            var huePixels = new Color32[HueResolution];
            for (var i = 0; i < HueResolution; i++)
            {
                // Red at the top, as the square's brightness runs bottom to top.
                huePixels[i] = Color.HSVToRGB(1f - i / (HueResolution - 1f), 1f, 1f);
            }

            hueTexture.SetPixels32(huePixels);
            hueTexture.Apply(false, false);

            var squareClip = CreateClip("SquareClip", parent, radius, x, y, squareWidth, height);
            var squareImage = UiFactory.CreateRawImage("Square", squareClip);
            squareImage.texture = squareTexture;
            UiFactory.Stretch(squareImage.rectTransform);
            // Half a texel in from each edge: bilinear sampling then reaches exactly white, black and
            // the pure hue at the corners instead of stopping short of them.
            var half = 0.5f / SquareResolution;
            squareImage.uvRect = new Rect(half, half, 1f - 2f * half, 1f - 2f * half);

            var stripClip = CreateClip("HueClip", parent, radius, x + squareWidth + gap, y, stripWidth, height);
            var stripImage = UiFactory.CreateRawImage("Hue", stripClip);
            stripImage.texture = hueTexture;
            UiFactory.Stretch(stripImage.rectTransform);
            var hueHalf = 0.5f / HueResolution;
            stripImage.uvRect = new Rect(0f, hueHalf, 1f, 1f - 2f * hueHalf);

            // Knobs sit outside the clips so they are not cut in half at an edge.
            var knobSize = UiTheme.PanelPx(KnobSize);
            var knobRadius = Mathf.Max(1, Mathf.RoundToInt(knobSize * 0.5f));
            var knobHalo = UiFactory.CreateImage("Knob", parent, UiSprites.Rounded(knobRadius), new Color(0f, 0f, 0f, 0.55f));
            var knob = knobHalo.rectTransform;
            knob.anchorMin = new Vector2(0f, 1f);
            knob.anchorMax = new Vector2(0f, 1f);
            knob.pivot = new Vector2(0.5f, 0.5f);
            knob.sizeDelta = new Vector2(knobSize, knobSize);
            var knobRing = UiFactory.CreateImage("Ring", knob, UiSprites.Rounded(knobRadius), UiTheme.Text);
            UiFactory.Stretch(knobRing.rectTransform, Mathf.Max(1f, UiTheme.PanelPx(1.5f)));
            var knobFill = UiFactory.CreateImage("Fill", knob, UiSprites.Rounded(knobRadius), Color.white);
            UiFactory.Stretch(knobFill.rectTransform, Mathf.Max(2f, UiTheme.PanelPx(4.5f)));

            var markerHeight = Mathf.Max(6f, UiTheme.PanelPx(10f));
            var markerImage = UiFactory.CreateImage(
                "HueMarker", parent,
                UiSprites.RoundedOutline(Mathf.Max(1, Mathf.RoundToInt(markerHeight * 0.5f)), Mathf.Max(1.5f, UiTheme.PanelPx(2.5f))),
                UiTheme.Text);
            var hueMarker = markerImage.rectTransform;
            hueMarker.anchorMin = new Vector2(0f, 1f);
            hueMarker.anchorMax = new Vector2(0f, 1f);
            hueMarker.pivot = new Vector2(0.5f, 0.5f);
            hueMarker.sizeDelta = new Vector2(stripWidth + UiTheme.PanelPx(6f), markerHeight);
            hueMarker.anchoredPosition = new Vector2(x + squareWidth + gap + stripWidth * 0.5f, y);

            var picker = new ColorPicker(squareTexture, hueTexture, squareClip, knob, knobFill, hueMarker);

            var squareSurface = squareClip.gameObject.AddComponent<DragSurface>();
            squareSurface.Pressed = picker.OnSquare;
            squareSurface.Dragged = picker.OnSquare;
            squareSurface.Released = picker.OnReleased;
            squareSurface.Tapped = _ => picker.OnReleased();

            var stripSurface = stripClip.gameObject.AddComponent<DragSurface>();
            stripSurface.Pressed = picker.OnStrip;
            stripSurface.Dragged = picker.OnStrip;
            stripSurface.Released = picker.OnReleased;
            stripSurface.Tapped = _ => picker.OnReleased();

            picker.hueStripTop = y;
            picker.hueStripHeight = height;
            return picker;
        }

        /// <summary>Show this colour without raising <see cref="Changed"/>.</summary>
        public void SetColor(float h, float s, float v)
        {
            if (!Mathf.Approximately(h, hue))
            {
                hue = h;
                RedrawSquare();
            }

            saturation = s;
            brightness = v;
            PlaceMarkers();
        }

        public void Dispose()
        {
            if (squareTexture != null)
            {
                UnityEngine.Object.Destroy(squareTexture);
            }

            if (hueTexture != null)
            {
                UnityEngine.Object.Destroy(hueTexture);
            }
        }

        private void OnSquare(Vector2 point)
        {
            saturation = Mathf.Clamp01(point.x);
            brightness = Mathf.Clamp01(point.y);
            PlaceMarkers();
            Changed?.Invoke(hue, saturation, brightness);
        }

        private void OnStrip(Vector2 point)
        {
            hue = Mathf.Clamp01(1f - point.y);
            RedrawSquare();
            PlaceMarkers();
            Changed?.Invoke(hue, saturation, brightness);
        }

        private void OnReleased() => Released?.Invoke();

        private void PlaceMarkers()
        {
            var squarePosition = square.anchoredPosition;
            var size = square.sizeDelta;
            knob.anchoredPosition = new Vector2(
                squarePosition.x + saturation * size.x,
                squarePosition.y - (1f - brightness) * size.y);
            knobFill.color = Color.HSVToRGB(hue, saturation, brightness);

            var markerPosition = hueMarker.anchoredPosition;
            hueMarker.anchoredPosition = new Vector2(markerPosition.x, hueStripTop - Mathf.Clamp01(hue) * hueStripHeight);
        }

        private void RedrawSquare()
        {
            var pure = Color.HSVToRGB(Mathf.Clamp01(hue), 1f, 1f);
            for (var yIndex = 0; yIndex < SquareResolution; yIndex++)
            {
                var value = yIndex / (SquareResolution - 1f);
                for (var xIndex = 0; xIndex < SquareResolution; xIndex++)
                {
                    var s = xIndex / (SquareResolution - 1f);
                    squarePixels[yIndex * SquareResolution + xIndex] = Color.Lerp(Color.white, pure, s) * value;
                }
            }

            // Color * value scaled alpha too; the square is opaque.
            for (var i = 0; i < squarePixels.Length; i++)
            {
                squarePixels[i].a = 255;
            }

            squareTexture.SetPixels32(squarePixels);
            squareTexture.Apply(false, false);
        }

        private static RectTransform CreateClip(string name, RectTransform parent, int radius, float x, float y, float width, float height)
        {
            var clip = UiFactory.CreateImage(name, parent, UiSprites.Rounded(radius), Color.white);
            clip.raycastTarget = true;
            clip.gameObject.AddComponent<Mask>().showMaskGraphic = false;
            var rect = clip.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Texture2D CreateTexture(string name, int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}
