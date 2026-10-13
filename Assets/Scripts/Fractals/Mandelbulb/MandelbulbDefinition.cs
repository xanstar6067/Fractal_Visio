using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Mandelbulb: z -> z^n + c in three dimensions, the power taken in spherical coordinates -
    /// the radius to the n-th power, both angles times n. Ported from the WPF version's
    /// <c>MandelbulbMode</c> and the bulb branch of <c>Fractal3DShader.Map</c>, the shader in
    /// <c>Shaders/Mandelbulb.shader</c> and the same estimate on the CPU in <see cref="Distance"/>.
    /// </summary>
    public sealed class MandelbulbDefinition : ISceneDefinition, IScenePlaces
    {
        public const string PowerKey = "power";
        public const string IterationsKey = "iterations";
        public const string BailoutKey = "bailout";

        private const float DefaultPower = 8f;
        private const int DefaultIterations = 9;
        private const float DefaultBailout = 4f;

        private static readonly int ShapeId = Shader.PropertyToID("_Shape");
        private static readonly int IterationsId = Shader.PropertyToID("_SceneIterations");

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(PowerKey, "Power", DefaultPower, 2d, 16d),
            new(IterationsKey, "Iterations", DefaultIterations, 2d, 30d, FractalParameterKind.Int),
            new(BailoutKey, "Escape radius", DefaultBailout, 1.5d, 16d)
        };

        // The WPF presets, as camera and shape. Their palettes and shading styles stay out, as the
        // plane fractals' places leave the palette alone: how it looks is the user's choice.
        private static readonly ScenePlace[] PlaceList =
        {
            new("classic", Camera(35f, 18f, 2.8f, Vector3.zero), Shape(8d, 9)),
            new("north-pole", Camera(12f, 68f, 2.4f, Vector3.zero), Shape(8d, 9)),
            new("smooth-petals", Camera(35f, 18f, 3f, Vector3.zero), Shape(3d, 12)),
            new("needle-crown", Camera(35f, 18f, 2.8f, Vector3.zero), Shape(16d, 8)),
            new("fold-closeup", Camera(118f, 6f, 1.35f, new Vector3(0.32f, 0.12f, 0f)), Shape(8d, 12))
        };

        public string Id => "mandelbulb";

        public string DisplayName => "Mandelbulb";

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public SceneCamera DefaultCamera => Camera(35f, 18f, 2.8f, Vector3.zero);

        public string ShaderName => "FractalVisio/Mandelbulb";

        public IReadOnlyList<ScenePlace> Places => PlaceList;

        /// <summary>The escape radius: an orbit is tested against it, so nothing that stays lies outside.</summary>
        public float BoundingRadius(in FractalParameterSet parameters) => Bailout(parameters);

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetVector(ShapeId, new Vector4(Power(parameters), Bailout(parameters), 0f, 0f));
            material.SetFloat(IterationsId, Iterations(parameters));
        }

        public ISceneDistance CreateDistance(in FractalParameterSet parameters) =>
            new Distance(Power(parameters), Iterations(parameters), Bailout(parameters));

        private static float Power(in FractalParameterSet parameters) =>
            (float)ParameterList[0].Clamp(parameters.Get(PowerKey, DefaultPower));

        private static int Iterations(in FractalParameterSet parameters) =>
            (int)ParameterList[1].Clamp(parameters.Get(IterationsKey, DefaultIterations));

        private static float Bailout(in FractalParameterSet parameters) =>
            (float)ParameterList[2].Clamp(parameters.Get(BailoutKey, DefaultBailout));

        private static SceneCamera Camera(float yaw, float pitch, float distance, Vector3 target) =>
            SceneCamera.FromAngles(yaw, pitch, 0f, distance, target);

        private static ParameterValue[] Shape(double power, int iterations) => new[]
        {
            new ParameterValue(PowerKey, power),
            new ParameterValue(IterationsKey, iterations),
            new ParameterValue(BailoutKey, DefaultBailout)
        };

        /// <summary>
        /// The bulb's distance estimate, line for line the shader's <c>SceneMap</c>: the CPU finds the
        /// point under the finger with it, and that point must lie where the picture shows it.
        /// </summary>
        private sealed class Distance : ISceneDistance
        {
            /// <summary>Least step scale near the poles; below it a ray spends every step and never arrives.</summary>
            private const float StepFloor = 0.125f;

            private readonly float power;
            private readonly int iterations;
            private readonly float bailout;

            public Distance(float power, int iterations, float bailout)
            {
                this.power = power;
                this.iterations = iterations;
                this.bailout = bailout;
            }

            public float Estimate(Vector3 point, out float stepScale)
            {
                stepScale = 1f;
                var z = point;
                var dr = 1f;
                float r;
                for (var i = 0; i < iterations; i++)
                {
                    r = z.magnitude;
                    if (r > bailout)
                    {
                        break;
                    }

                    var inverse = 1f / Mathf.Max(r, 1e-12f);
                    var theta = Mathf.Acos(Mathf.Clamp(z.z * inverse, -1f, 1f));
                    var phi = Mathf.Atan2(z.y, z.x);

                    // Along a parallel the power stretches by sin(n theta) / sin(theta), near a pole
                    // almost n - more than along the radius, which the estimate counts. There it runs
                    // high, so the ray steps short.
                    var sinTheta0 = Mathf.Abs(Mathf.Sin(theta));
                    var stretch = sinTheta0 > 1e-4f ? Mathf.Abs(Mathf.Sin(theta * power)) / sinTheta0 : Mathf.Abs(power);
                    stepScale /= Mathf.Max(stretch, 1f);
                    dr = Mathf.Pow(r, power - 1f) * power * dr + 1f;

                    var zr = Mathf.Pow(r, power);
                    theta *= power;
                    phi *= power;
                    var sinTheta = Mathf.Sin(theta);
                    z = zr * new Vector3(sinTheta * Mathf.Cos(phi), sinTheta * Mathf.Sin(phi), Mathf.Cos(theta)) + point;
                }

                r = z.magnitude;
                stepScale = Mathf.Max(stepScale, StepFloor);
                return 0.5f * Mathf.Log(Mathf.Max(r, 1.000001f)) * r / Mathf.Max(dr, 1e-9f);
            }
        }
    }
}
