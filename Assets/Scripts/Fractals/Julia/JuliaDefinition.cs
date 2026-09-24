using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Julia set of z^2 + c. Its constant C is two ordinary parameters, and
    /// <see cref="IParameterPlane"/> tells the UI they are one point of the Mandelbrot set - which is
    /// how C is chosen: on a map of the Mandelbrot set, not with two sliders.
    /// </summary>
    public sealed class JuliaDefinition : IFractalDefinition, IParameterPlane, IPlacesOfInterest
    {
        public const string ConstantRealKey = "c_re";
        public const string ConstantImaginaryKey = "c_im";

        // The WPF version's default: a spiral near the main cardioid's edge. The ranges cover the
        // map with room to spare - a C far outside the Mandelbrot set gives only dust.
        private const double DefaultReal = -0.8d;
        private const double DefaultImaginary = 0.156d;

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(ConstantRealKey, "C (real)", DefaultReal, -2.5d, 2d),
            new(ConstantImaginaryKey, "C (imaginary)", DefaultImaginary, -2d, 2d)
        };

        // The WPF version's presets (their names are in the locales), then two classics it lacked.
        private static readonly PlanePreset[] PresetList =
        {
            new("classic-spiral", -0.8d, 0.156d),
            new("douady-rabbit", -0.122561d, 0.744862d),
            new("dendrite", 0d, 1d),
            new("snowflake", -0.70176d, -0.3842d),
            new("fire-whirl", 0.285d, 0.01d),
            new("lace-spiral", -0.4d, 0.6d),
            new("double-curls", 0.355d, 0.355d),
            new("rainbow-dragon", -0.835d, -0.2321d),
            new("neon-cloud", -0.7269d, 0.1889d),
            new("siegel-disk", -0.390541d, -0.586788d),
            new("san-marco", -0.75d, 0d)
        };

        // The one WPF point of interest that is a place rather than a C: the default spiral, closer.
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("spiral-closeup", 0.3m, 0.1m, 6m,
                new ParameterValue(ConstantRealKey, -0.8d), new ParameterValue(ConstantImaginaryKey, 0.156d))
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

        /// <summary>The shaders' uniform for C. Shared with <see cref="JuliaBurningShipDefinition"/>.</summary>
        internal static readonly int ConstantId = Shader.PropertyToID("_JuliaC");

        public string Id => "julia";

        public string DisplayName => "Julia";

        // The default set spans about 3 across; framed as a height on a wide screen, and the
        // session widens it on an upright one.
        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3.2m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/Julia";

        public string PlaneFractalId => "mandelbrot";

        public string RealKey => ConstantRealKey;

        public string ImaginaryKey => ConstantImaginaryKey;

        public PlaneRect PlaneBounds => new(-2.15d, 0.65d, -1.25d, 1.25d);

        public IReadOnlyList<PlanePreset> Presets => PresetList;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetVector(ConstantId, new Vector4(
                (float)parameters.Get(ConstantRealKey, DefaultReal),
                (float)parameters.Get(ConstantImaginaryKey, DefaultImaginary),
                0f,
                0f));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var real = parameters.Get(ConstantRealKey, DefaultReal);
            var imaginary = parameters.Get(ConstantImaginaryKey, DefaultImaginary);

            // JuliaSamplerDD stays as the exact reference the perturbation result is checked against.
            if (extendedPrecision)
            {
                host.RunPerturbed(new JuliaPerturbationSampler(real, imaginary));
                return;
            }

            host.Run(new JuliaSamplerD(real, imaginary));
        }
    }
}
