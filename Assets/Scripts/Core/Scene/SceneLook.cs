using System;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// How a lit surface becomes colour - the WPF version's built-in 3D shaders. The values are
    /// saved as numbers: new styles are only ever appended.
    /// </summary>
    public enum SceneShading
    {
        /// <summary>Diffuse light, highlight, soft shadow, shaded folds and haze into the distance.</summary>
        Classic = 0,

        /// <summary>Clay: soft wrapped light, no highlight, deeper folds.</summary>
        Clay = 1,

        /// <summary>Metal: the sky reflected and a hard highlight over dimmed diffuse light.</summary>
        Metal = 2,

        /// <summary>Glow: how close the ray passed to the surface adds up into a halo.</summary>
        Glow = 3,

        /// <summary>Density: the ray goes straight through and gathers brightness on the way.</summary>
        Density = 4,

        /// <summary>Studio: lit from the camera's own axes, the same from every side.</summary>
        Studio = 5,

        /// <summary>Cartoon: light in steps and a dark outline along the silhouette.</summary>
        Toon = 6,

        /// <summary>Translucent: thin places glow with light from behind.</summary>
        Translucent = 7,

        /// <summary>Mirror: one reflected ray shows the sky and the shape's other parts.</summary>
        Mirror = 8
    }

    /// <summary>
    /// Which number the palette colours the surface by. <see cref="Material"/> and
    /// <see cref="Normal"/> use no palette. Saved as numbers: only ever appended.
    /// </summary>
    public enum SceneColoring
    {
        /// <summary>One material colour: a surface that keeps its colour as the camera moves.</summary>
        Material = 0,

        /// <summary>The surface normal itself, as a colour.</summary>
        Normal = 1,

        /// <summary>Smallest radius of the orbit: the classic spherical trap.</summary>
        OrbitTrap = 2,

        /// <summary>How far the ray travelled.</summary>
        Depth = 3,

        /// <summary>Smallest distance of the orbit to the coordinate planes.</summary>
        CrossTrap = 4,

        /// <summary>The iteration the orbit left at - what colours the plane fractals.</summary>
        Iteration = 5,

        /// <summary>Radius of the orbit on leaving - like a plane fractal's escape speed.</summary>
        Escape = 6,

        /// <summary>Height of the point along the world's vertical.</summary>
        Height = 7,

        /// <summary>How much the point is hidden by its neighbours.</summary>
        Occlusion = 8,

        /// <summary>Angle between the surface and the ray: the edges coloured unlike the faces.</summary>
        Fresnel = 9,

        /// <summary>Steps the ray took: a map of how hard the silhouette is.</summary>
        Steps = 10
    }

    /// <summary>
    /// Everything about a 3D picture that is neither its shape nor where it is seen from: the shading
    /// style, what the palette colours, the light and how strong each effect is. The palette itself is
    /// the session's, shared with the plane fractals.
    ///
    /// The light is given on the screen, as the relief's is (<see cref="ColoringSettings"/>): an
    /// azimuth and a height above the picture's plane. It follows the camera - the WPF version fixes
    /// it in the world, where turning a shape round showed its unlit back; with a finger turning the
    /// shape all the time, the side facing the viewer should be the lit one.
    /// </summary>
    public struct SceneLook : IEquatable<SceneLook>
    {
        public const float MinimumLightHeight = 10f;
        public const float MaximumLightHeight = 90f;
        public const float MinimumColorScale = 0.1f;
        public const float MaximumColorScale = 10f;

        public SceneShading Shading;
        public SceneColoring Coloring;

        /// <summary>Degrees on the screen: 0 from the right, 90 from above.</summary>
        public float LightAngle;

        /// <summary>Degrees above the picture's plane, <see cref="MinimumLightHeight"/> to 90.</summary>
        public float LightHeight;

        /// <summary>Light every point gets, 0 to 1.</summary>
        public float Ambient;

        /// <summary>Highlight strength, 0 to 1.5.</summary>
        public float Specular;

        public bool Shadows;

        /// <summary>Darkened folds (ambient occlusion).</summary>
        public bool Occlusion;

        /// <summary>Strength of the style's own effect - halo, density, rim, steps, reflection - 0 to 2.</summary>
        public float Effect;

        /// <summary>How many palette sweeps one unit of the colouring value spans.</summary>
        public float ColorScale;

        /// <summary>Shift along the palette, 0 to 1.</summary>
        public float ColorOffset;

        public static SceneLook Default => new SceneLook
        {
            Shading = SceneShading.Classic,
            Coloring = SceneColoring.Material,
            LightAngle = 100f,
            LightHeight = 60f,
            Ambient = 0.2f,
            Specular = 0.3f,
            Shadows = true,
            Occlusion = true,
            Effect = 1f,
            ColorScale = 1f,
            ColorOffset = 0f
        };

        /// <summary>Whether the palette shows at all: one material colour and the normal use none.</summary>
        public readonly bool UsesPalette => Coloring != SceneColoring.Material && Coloring != SceneColoring.Normal;

        public readonly SceneLook Sanitized()
        {
            var result = this;
            if (!Enum.IsDefined(typeof(SceneShading), result.Shading))
            {
                result.Shading = SceneShading.Classic;
            }

            if (!Enum.IsDefined(typeof(SceneColoring), result.Coloring))
            {
                result.Coloring = SceneColoring.Material;
            }

            result.LightAngle = Repeat(result.LightAngle);
            result.LightHeight = Clamp(result.LightHeight, MinimumLightHeight, MaximumLightHeight, Default.LightHeight);
            result.Ambient = Clamp(result.Ambient, 0f, 1f, Default.Ambient);
            result.Specular = Clamp(result.Specular, 0f, 1.5f, Default.Specular);
            result.Effect = Clamp(result.Effect, 0f, 2f, Default.Effect);
            result.ColorScale = Clamp(result.ColorScale, MinimumColorScale, MaximumColorScale, Default.ColorScale);
            result.ColorOffset = float.IsNaN(result.ColorOffset) ? 0f : result.ColorOffset - Mathf.Floor(result.ColorOffset);
            return result;
        }

        public readonly bool Equals(SceneLook other) =>
            Shading == other.Shading && Coloring == other.Coloring && LightAngle == other.LightAngle &&
            LightHeight == other.LightHeight && Ambient == other.Ambient && Specular == other.Specular &&
            Shadows == other.Shadows && Occlusion == other.Occlusion && Effect == other.Effect &&
            ColorScale == other.ColorScale && ColorOffset == other.ColorOffset;

        public override readonly bool Equals(object obj) => obj is SceneLook other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(
            (int)Shading, (int)Coloring, LightAngle, LightHeight, Ambient, Specular, Effect,
            HashCode.Combine(Shadows, Occlusion, ColorScale, ColorOffset));

        private static float Repeat(float degrees) =>
            float.IsNaN(degrees) || float.IsInfinity(degrees) ? Default.LightAngle : Mathf.Repeat(degrees, 360f);

        private static float Clamp(float value, float minimum, float maximum, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, minimum, maximum);
    }
}
