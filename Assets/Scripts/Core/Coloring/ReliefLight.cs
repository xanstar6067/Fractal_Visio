using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Pseudo-3D lighting: the picture as a surface lit by one sun, with the set as a plateau and
    /// the outside falling away from it.
    ///
    /// The surface's slope at a pixel is the direction in which the escape potential rises -
    /// <c>grad log|z_n|</c>, computed exactly from the orbit's derivative (for z^2 + c, the
    /// direction of <c>z conj(dz/dc)</c>; a folded map carries a real 2x2 Jacobian instead). Nothing
    /// comes from neighbouring pixels, so a pixel lights the same in every progressive pass, in every
    /// export tile and in a perturbation render. The WPF engine took finite differences of the
    /// distance estimate: noisy wherever the estimate is, creased wherever two boundaries are equally
    /// near, and blind to its own neighbours across a pass or a tile.
    ///
    /// The tilt is the same everywhere (<see cref="Slope"/>); only its direction varies. That keeps
    /// the look the same at every depth, resolution and tile size - any measure of steepness would
    /// need a length to be measured against, and a tile of an export does not know the image's.
    ///
    /// This struct is the derived constants. The per-pixel formula lives in exactly two places,
    /// like the palette position: <c>EscapeColorMapper</c> and <c>FractalCommon.hlsl</c>
    /// (<c>FractalReliefColor</c>). They must agree - the backend switches under the viewer.
    /// </summary>
    public readonly struct ReliefLight
    {
        /// <summary>Light a slope facing away from the sun still gets, as a fraction of full.</summary>
        public const float Ambient = 0.22f;

        /// <summary>Unit vector towards the sun, in screen space with z towards the viewer.</summary>
        public readonly float LightX;
        public readonly float LightY;
        public readonly float LightZ;

        /// <summary>Halfway between the sun and the viewer: where the highlight is brightest.</summary>
        public readonly float HalfX;
        public readonly float HalfY;
        public readonly float HalfZ;

        /// <summary>Tangent of the tilt every pixel's surface has.</summary>
        public readonly float Slope;

        /// <summary>
        /// One over what a flat surface would receive, so that a flat surface keeps the palette's own
        /// colour at any sun height: the relief only brightens and darkens around it.
        /// </summary>
        public readonly float InverseFlat;

        /// <summary>Highlight exponent: higher is a smaller, sharper highlight.</summary>
        public readonly float Shininess;

        /// <summary>Highlight strength, 0 for none.</summary>
        public readonly float Specular;

        private ReliefLight(in ColoringSettings settings)
        {
            var azimuth = settings.LightAngle * Mathf.Deg2Rad;
            var elevation = Mathf.Clamp(
                settings.LightHeight, ColoringSettings.MinimumLightHeight, ColoringSettings.MaximumLightHeight) * Mathf.Deg2Rad;
            var horizontal = Mathf.Cos(elevation);
            LightX = horizontal * Mathf.Cos(azimuth);
            LightY = horizontal * Mathf.Sin(azimuth);
            LightZ = Mathf.Sin(elevation);

            var half = new Vector3(LightX, LightY, LightZ + 1f).normalized;
            HalfX = half.x;
            HalfY = half.y;
            HalfZ = half.z;

            // Squared so the low end of the slider is fine control: most good pictures want a
            // gentle relief, and 1 (a 76 degree tilt) is already a caricature.
            var depth = Mathf.Clamp01(settings.ReliefDepth);
            Slope = 4f * depth * depth;
            InverseFlat = 1f / (Ambient + (1f - Ambient) * LightZ);

            var shine = Mathf.Clamp01(settings.ReliefShine);
            Shininess = 10f + 50f * shine;
            Specular = 0.8f * shine;
        }

        public static ReliefLight From(in ColoringSettings settings) => new(settings);

        /// <summary>
        /// The plane-space slope a sampler returned, turned into screen space and packed for the CPU
        /// buffer: two signed 16-bit components of the unit direction. 0 means "no slope" and lights
        /// as a flat surface - the interior, and pixels rendered while the relief was off.
        /// </summary>
        /// <param name="rotationCos">Cosine of the view's rotation (plane = rotation x screen).</param>
        public static int PackScreenSlope(double planeX, double planeY, double rotationCos, double rotationSin)
        {
            // The screen gradient is the plane gradient turned back by the view's rotation.
            var x = planeX * rotationCos + planeY * rotationSin;
            var y = -planeX * rotationSin + planeY * rotationCos;

            // Scaled by the larger component first: a derivative deep in can be large enough that
            // its square is not a double.
            var largest = System.Math.Max(System.Math.Abs(x), System.Math.Abs(y));
            if (!(largest > 0d) || double.IsInfinity(largest))
            {
                return 0;
            }

            x /= largest;
            y /= largest;
            var inverseLength = 32767d / System.Math.Sqrt(x * x + y * y);
            var packedX = (int)System.Math.Round(x * inverseLength);
            var packedY = (int)System.Math.Round(y * inverseLength);
            return (packedX & 0xFFFF) | (packedY << 16);
        }

        /// <summary>Undo <see cref="PackScreenSlope"/>: a unit vector, or (0, 0).</summary>
        public static void UnpackSlope(int packed, out float x, out float y)
        {
            const float scale = 1f / 32767f;
            x = (short)(packed & 0xFFFF) * scale;
            y = (packed >> 16) * scale;
        }
    }
}
