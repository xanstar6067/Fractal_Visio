using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Proposes a set of colours that go together, for the palette editor's "Harmony" button: hues
    /// from one of the classic schemes (analogous, complementary, triad, ...) around a random base,
    /// with brightness alternating dark and light.
    ///
    /// The alternation matters more than the hues. An escape-time picture is read through contrast
    /// between neighbouring bands; five equally bright colours make a flat picture however well
    /// their hues are chosen.
    /// </summary>
    public static class PaletteHarmony
    {
        /// <summary>Hue offsets, in turns, of each scheme. Colours take them in order, wrapping.</summary>
        private static readonly float[][] Schemes =
        {
            new[] { 0f, 1f / 12f, -1f / 12f, 2f / 12f },
            new[] { 0f, 0.5f },
            new[] { 0f, 1f / 3f, 2f / 3f },
            new[] { 0f, 5f / 12f, 7f / 12f },
            new[] { 0f, 0.25f, 0.5f, 0.75f },
            new[] { 0f }
        };

        /// <summary><paramref name="count"/> colours as hue, saturation and brightness, each 0..1.</summary>
        public static Vector3[] Generate(int count, System.Random random)
        {
            random ??= new System.Random();
            count = Math.Max(1, count);

            var scheme = Schemes[random.Next(Schemes.Length)];
            var baseHue = (float)random.NextDouble();

            // Start dark or light at random, so two presses of the button differ in more than hue.
            var darkFirst = random.Next(2) == 0;
            var result = new Vector3[count];
            for (var i = 0; i < count; i++)
            {
                var jitter = ((float)random.NextDouble() - 0.5f) * 0.04f;
                var hue = Wrap(baseHue + scheme[i % scheme.Length] + jitter);
                var dark = (i % 2 == 0) == darkFirst;
                var saturation = dark ? Range(random, 0.6f, 1f) : Range(random, 0.35f, 0.9f);
                var brightness = dark ? Range(random, 0.1f, 0.4f) : Range(random, 0.8f, 1f);
                result[i] = new Vector3(hue, saturation, brightness);
            }

            return result;
        }

        private static float Range(System.Random random, float minimum, float maximum) =>
            minimum + (float)random.NextDouble() * (maximum - minimum);

        private static float Wrap(float value) => value - Mathf.Floor(value);
    }
}
