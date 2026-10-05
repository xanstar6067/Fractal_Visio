using System;
using UnityEngine;
using UnityEngine.UI;

namespace FractalVisio.UI
{
    /// <summary>
    /// Where the light comes from, as a sun dragged over a dome. Direction around the circle is the
    /// sun's direction on the screen; distance from the middle is how low it stands - the middle is
    /// straight overhead, the rim is grazing. The dome is drawn lit by that sun, so the control shows
    /// what it does before the picture has been looked at: one finger sets both angles at once, where
    /// two sliders ("azimuth", "elevation") asked the user to imagine the result.
    ///
    /// Answers on touch-down, like the colour square, and is a 2D control: the panel scrolls by the
    /// rows around it, not through it.
    /// </summary>
    public sealed class LightDial : IDisposable
    {
        private const int Resolution = 96;
        private const float SunSize = 28f;

        private readonly Texture2D domeTexture;
        private readonly Color32[] domePixels = new Color32[Resolution * Resolution];
        private readonly RectTransform dome;
        private readonly RectTransform sun;
        private readonly float minimumHeight;
        private readonly float maximumHeight;
        private float angle = float.NaN;
        private float height = float.NaN;

        private LightDial(Texture2D domeTexture, RectTransform dome, RectTransform sun, float minimumHeight, float maximumHeight)
        {
            this.domeTexture = domeTexture;
            this.dome = dome;
            this.sun = sun;
            this.minimumHeight = minimumHeight;
            this.maximumHeight = maximumHeight;
        }

        /// <summary>Light angle (degrees from the right, counter-clockwise) and height (degrees) while the finger moves.</summary>
        public event Action<float, float> Changed;

        /// <summary>The finger lifted.</summary>
        public event Action Released;

        /// <param name="size">Side of the square the dome fills.</param>
        /// <param name="minimumHeight">Sun height at the rim, in degrees.</param>
        /// <param name="maximumHeight">Sun height in the middle, in degrees.</param>
        public static LightDial Create(
            RectTransform parent, float x, float y, float size, float minimumHeight, float maximumHeight)
        {
            var texture = new Texture2D(Resolution, Resolution, TextureFormat.RGBA32, false)
            {
                name = "Light Dial",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var image = UiFactory.CreateRawImage("LightDial", parent);
            image.texture = texture;
            image.raycastTarget = true;
            var rect = image.rectTransform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(size, size);

            var outline = UiFactory.CreateImage(
                "Rim", rect, UiSprites.RoundedOutline(Mathf.Max(1, Mathf.RoundToInt(size * 0.5f)), Mathf.Max(1f, UiTheme.PanelPx(1.5f))),
                new Color(1f, 1f, 1f, 0.25f));
            UiFactory.Stretch(outline.rectTransform);
            outline.raycastTarget = false;

            // The sun sits on the dome's parent, not inside it, so it is never cut at the rim.
            var sunSize = UiTheme.PanelPx(SunSize);
            var sunRadius = Mathf.Max(1, Mathf.RoundToInt(sunSize * 0.5f));
            var halo = UiFactory.CreateImage("Sun", parent, UiSprites.Rounded(sunRadius), new Color(0f, 0f, 0f, 0.55f));
            halo.raycastTarget = false;
            var sun = halo.rectTransform;
            sun.anchorMin = new Vector2(0f, 1f);
            sun.anchorMax = new Vector2(0f, 1f);
            sun.pivot = new Vector2(0.5f, 0.5f);
            sun.sizeDelta = new Vector2(sunSize, sunSize);
            var ring = UiFactory.CreateImage("Ring", sun, UiSprites.Rounded(sunRadius), UiTheme.Text);
            UiFactory.Stretch(ring.rectTransform, Mathf.Max(1f, UiTheme.PanelPx(1.5f)));
            ring.raycastTarget = false;
            var fill = UiFactory.CreateImage("Fill", sun, UiSprites.Rounded(sunRadius), new Color(1f, 0.84f, 0.38f, 1f));
            UiFactory.Stretch(fill.rectTransform, Mathf.Max(2f, UiTheme.PanelPx(4f)));
            fill.raycastTarget = false;

            var dial = new LightDial(texture, rect, sun, minimumHeight, maximumHeight);
            var surface = image.gameObject.AddComponent<DragSurface>();
            surface.Pressed = dial.OnPoint;
            surface.Dragged = dial.OnPoint;
            surface.Released = dial.OnReleased;
            surface.Tapped = _ => dial.OnReleased();
            return dial;
        }

        /// <summary>Show this light without raising <see cref="Changed"/>.</summary>
        public void SetLight(float lightAngle, float lightHeight)
        {
            if (Mathf.Approximately(lightAngle, angle) && Mathf.Approximately(lightHeight, height))
            {
                return;
            }

            angle = lightAngle;
            height = lightHeight;
            Redraw();
        }

        public void Dispose()
        {
            if (domeTexture != null)
            {
                UnityEngine.Object.Destroy(domeTexture);
            }
        }

        private void OnPoint(Vector2 point)
        {
            var offset = point * 2f - Vector2.one;
            var reach = Mathf.Clamp01(offset.magnitude);

            // At the very middle the direction is undefined; keep the one the sun had.
            var nextAngle = offset.sqrMagnitude > 1e-6f
                ? Mathf.Repeat(Mathf.Atan2(offset.y, offset.x) * Mathf.Rad2Deg, 360f)
                : angle;
            var nextHeight = Mathf.Lerp(maximumHeight, minimumHeight, reach);

            SetLight(nextAngle, nextHeight);
            Changed?.Invoke(angle, height);
        }

        private void OnReleased() => Released?.Invoke();

        private float Reach => Mathf.InverseLerp(maximumHeight, minimumHeight, height);

        private void Redraw()
        {
            var azimuth = angle * Mathf.Deg2Rad;
            var elevation = height * Mathf.Deg2Rad;
            var light = new Vector3(
                Mathf.Cos(elevation) * Mathf.Cos(azimuth),
                Mathf.Cos(elevation) * Mathf.Sin(azimuth),
                Mathf.Sin(elevation));
            var half = (light + Vector3.forward).normalized;

            // A hemisphere seen from above, lit by the sun: what "light from there, this high" means.
            var edge = 1.5f / Resolution;
            for (var row = 0; row < Resolution; row++)
            {
                var v = (row + 0.5f) / Resolution * 2f - 1f;
                for (var column = 0; column < Resolution; column++)
                {
                    var u = (column + 0.5f) / Resolution * 2f - 1f;
                    var r2 = u * u + v * v;
                    var r = Mathf.Sqrt(r2);
                    var alpha = Mathf.Clamp01((1f - r) / edge);
                    if (alpha <= 0f)
                    {
                        domePixels[row * Resolution + column] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    var normal = new Vector3(u, v, Mathf.Sqrt(Mathf.Max(0f, 1f - r2)));
                    var diffuse = Mathf.Max(0f, Vector3.Dot(normal, light));
                    var highlight = Mathf.Pow(Mathf.Max(0f, Vector3.Dot(normal, half)), 40f) * 0.6f;
                    var shade = 0.1f + 0.7f * diffuse;
                    var color = new Color(
                        Mathf.Clamp01(shade * 0.82f + highlight),
                        Mathf.Clamp01(shade * 0.88f + highlight),
                        Mathf.Clamp01(shade + highlight),
                        alpha);
                    domePixels[row * Resolution + column] = color;
                }
            }

            domeTexture.SetPixels32(domePixels);
            domeTexture.Apply(false, false);

            var radius = dome.sizeDelta.x * 0.5f;
            var centre = dome.anchoredPosition + new Vector2(radius, -radius);
            sun.anchoredPosition = centre + new Vector2(Mathf.Cos(azimuth), Mathf.Sin(azimuth)) * (Reach * radius);
        }
    }
}
