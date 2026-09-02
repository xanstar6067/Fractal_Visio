using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.UI
{
    /// <summary>
    /// Rounded-rectangle sprites generated at runtime, so the UI needs no imported art. Each sprite
    /// is a small nine-sliced texture: only the corners are real pixels, the middle is stretched.
    /// Cached by shape, because a slider or a list can ask for the same one many times.
    /// </summary>
    public static class UiSprites
    {
        private static readonly Dictionary<int, Sprite> Cache = new();

        /// <summary>Filled rounded rectangle. Colour it through the Image's tint.</summary>
        public static Sprite Rounded(int radius) => Build(radius, 0f);

        /// <summary>Rounded outline of the given thickness, transparent inside.</summary>
        public static Sprite RoundedOutline(int radius, float thickness) => Build(radius, Mathf.Max(0.5f, thickness));

        public static void Clear()
        {
            foreach (var sprite in Cache.Values)
            {
                if (sprite == null)
                {
                    continue;
                }

                Object.Destroy(sprite.texture);
                Object.Destroy(sprite);
            }

            Cache.Clear();
        }

        private static Sprite Build(int radius, float thickness)
        {
            radius = Mathf.Clamp(radius, 1, 128);
            var key = radius * 1000 + Mathf.RoundToInt(thickness * 10f);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            // Two extra pixels of slack on each side keep the antialiased edge inside the corner
            // slice, so stretching the middle never smears it.
            var border = radius + 2;
            var size = border * 2 + 2;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = thickness > 0f ? $"UiRoundedOutline{radius}" : $"UiRounded{radius}",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            var half = size * 0.5f;
            var inner = half - radius;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Signed distance to the rounded rectangle: negative inside, zero on the edge.
                    var dx = Mathf.Abs(x + 0.5f - half) - inner;
                    var dy = Mathf.Abs(y + 0.5f - half) - inner;
                    var outside = new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f));
                    var distance = outside.magnitude + Mathf.Min(Mathf.Max(dx, dy), 0f) - radius;

                    float alpha;
                    if (thickness > 0f)
                    {
                        // Ring: full inside the band, fading on both of its edges.
                        var band = Mathf.Abs(distance + thickness * 0.5f) - thickness * 0.5f;
                        alpha = Mathf.Clamp01(0.5f - band);
                    }
                    else
                    {
                        alpha = Mathf.Clamp01(0.5f - distance);
                    }

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(border, border, border, border));
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;

            Cache[key] = sprite;
            return sprite;
        }
    }
}
