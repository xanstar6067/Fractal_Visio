using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Multibrot's Julia sets, z^p + C from every point - WPF's "generalised Julia" for a whole
    /// power. The samplers and the shader are the Multibrot's in their Julia form; C is picked on a
    /// map of the Multibrot at the same power (the map adopts it: <see cref="IParameterPlane"/>).
    /// </summary>
    public sealed class MultijuliaDefinition : IFractalDefinition, IParameterPlane
    {
        private const double DefaultReal = -0.2d;
        private const double DefaultImaginary = 0.7d;

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(MultibrotDefinition.PowerKey, "Power", 3d, MultibrotDefinition.MinimumPower, MultibrotDefinition.MaximumPower,
                FractalParameterKind.Int),
            new(JuliaDefinition.ConstantRealKey, "C (real)", DefaultReal, -2d, 2d),
            new(JuliaDefinition.ConstantImaginaryKey, "C (imaginary)", DefaultImaginary, -2d, 2d)
        };

        // The WPF version's presets for whole powers; they hold at any power, the shapes change.
        private static readonly PlanePreset[] PresetList =
        {
            new("cubic-julia", DefaultReal, DefaultImaginary),
            new("four-rays", -0.3d, 0.4d)
        };

        public string Id => "multijulia";

        public string DisplayName => "Multijulia";

        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/Multibrot";

        public string PlaneFractalId => "multibrot";

        public string RealKey => JuliaDefinition.ConstantRealKey;

        public string ImaginaryKey => JuliaDefinition.ConstantImaginaryKey;

        // The Multibrot's default frame: p - 1 bulbs, all inside radius 1.4.
        public PlaneRect PlaneBounds => new(-1.45d, 1.45d, -1.45d, 1.45d);

        public IReadOnlyList<PlanePreset> Presets => PresetList;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetFloat(MultibrotDefinition.PowerId, MultibrotDefinition.PowerOf(parameters));
            material.SetVector(JuliaDefinition.ConstantId, new Vector4(
                (float)parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal),
                (float)parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary),
                1f,
                0f));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var power = MultibrotDefinition.PowerOf(parameters);
            var real = parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal);
            var imaginary = parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary);
            if (extendedPrecision)
            {
                host.RunPerturbed(new MultibrotPerturbationSampler(power, real, imaginary));
                return;
            }

            host.Run(new MultibrotSamplerD(power, real, imaginary));
        }
    }
}
