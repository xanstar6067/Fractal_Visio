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

        /// <summary>Five-pointed star icon, <paramref name="size"/> device pixels square. Tint through the Image.</summary>
        public static Sprite Star(int size) => BuildIcon(1, size, StarCoverage);

        /// <summary>Camera icon: body with a lens cut out of it.</summary>
        public static Sprite Camera(int size) => BuildIcon(2, size, CameraCoverage);

        private static bool StarCoverage(float x, float y)
        {
            // Point in a 10-vertex star polygon centred on (0.5, 0.52), by the even-odd rule.
            const int points = 5;
            const float outer = 0.48f;
            const float inner = 0.2f;
            var inside = false;
            var previousX = 0f;
            var previousY = 0f;
            for (var i = 0; i <= points * 2; i++)
            {
                var angle = Mathf.PI * 0.5f + i * Mathf.PI / points;
                var radius = i % 2 == 0 ? outer : inner;
                var px = 0.5f + Mathf.Cos(angle) * radius;
                var py = 0.47f + Mathf.Sin(angle) * radius;
                if (i > 0 && (py > y) != (previousY > y) &&
                    x < (previousX - px) * (y - py) / (previousY - py) + px)
                {
                    inside = !inside;
                }

                previousX = px;
                previousY = py;
            }

            return inside;
        }

        private static bool CameraCoverage(float x, float y)
        {
            var body = RoundedBox(x - 0.5f, y - 0.44f, 0.46f, 0.3f, 0.08f);
            var hump = RoundedBox(x - 0.5f, y - 0.77f, 0.14f, 0.06f, 0.03f);
            var dx = x - 0.5f;
            var dy = y - 0.44f;
            var distance = Mathf.Sqrt(dx * dx + dy * dy);
            var lensRing = distance > 0.19f;
            var lensDot = distance < 0.1f;
            return (body && (lensRing || lensDot)) || hump;
        }

        private static bool RoundedBox(float x, float y, float halfWidth, float halfHeight, float radius)
        {
            var qx = Mathf.Abs(x) - halfWidth + radius;
            var qy = Mathf.Abs(y) - halfHeight + radius;
            var outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - radius <= 0f;
        }

        /// <summary>Rasterise a shape given as a coverage test on the unit square, 4x4 supersampled.</summary>
        private static Sprite BuildIcon(int kind, int size, System.Func<float, float, bool> covers)
        {
            size = Mathf.Clamp(size, 8, 256);
            var key = -(kind * 1000 + size);
            if (Cache.TryGetValue(key, out var cached) && cached != null)
            {
                return cached;
            }

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "UiIcon" + kind + "_" + size,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            const int samples = 4;
            var pixels = new Color32[size * size];
            for (var py = 0; py < size; py++)
            {
                for (var px = 0; px < size; px++)
                {
                    var hits = 0;
                    for (var sy = 0; sy < samples; sy++)
                    {
                        for (var sx = 0; sx < samples; sx++)
                        {
                            var u = (px + (sx + 0.5f) / samples) / size;
                            var v = (py + (sy + 0.5f) / samples) / size;
                            if (covers(u, v))
                            {
                                hits++;
                            }
                        }
                    }

                    pixels[py * size + px] = new Color32(255, 255, 255, (byte)(hits * 255 / (samples * samples)));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, false);

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            Cache[key] = sprite;
            return sprite;
        }

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
