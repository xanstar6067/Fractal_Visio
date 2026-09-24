using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Burning Ship's Julia sets: the ship's map with the pixel as the start and C fixed, chosen
    /// on a map of the ship. Symmetric about both axes, because the map only ever sees |x| and |y|.
    /// </summary>
    public sealed class JuliaBurningShipDefinition : IFractalDefinition, IParameterPlane
    {
        // The WPF version's "Psychonaut" preset. Its default - a C on the needle, in the period-3
        // minibrot - gives a set that is a line across the screen: correct, and a poor first
        // picture or gallery card. It is still the preset "needle-ship".
        private const double DefaultReal = 0.736607134342194d;
        private const double DefaultImaginary = 1.09152793884277d;

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(JuliaDefinition.ConstantRealKey, "C (real)", DefaultReal, -2.5d, 2d),
            new(JuliaDefinition.ConstantImaginaryKey, "C (imaginary)", DefaultImaginary, -2d, 2d)
        };

        // The WPF version's presets and its default.
        private static readonly PlanePreset[] PresetList =
        {
            new("psychonaut", DefaultReal, DefaultImaginary),
            new("violet-flame", 0.598214268684387d, 1.17851734161377d),
            new("pulsar-ruby", -0.0517381690442562d, -0.267557740211487d),
            new("needle-ship", -1.7551867961883d, 0.01068d),
            new("crystal-axis", -1.7623d, 0.02d)
        };

        public string Id => "julia-burning-ship";

        public string DisplayName => "Burning Ship Julia";

        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3.6m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/JuliaBurningShip";

        public string PlaneFractalId => "burning-ship";

        public string RealKey => JuliaDefinition.ConstantRealKey;

        public string ImaginaryKey => JuliaDefinition.ConstantImaginaryKey;

        // The ship with masts up sits mostly above the real axis (BurningShipSamplerD): measured at
        // 300 iterations it spans x -2..1.11, y -0.45..1.6.
        public PlaneRect PlaneBounds => new(-2.1d, 1.25d, -0.6d, 1.75d);

        public IReadOnlyList<PlanePreset> Presets => PresetList;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetVector(JuliaDefinition.ConstantId, new Vector4(
                (float)parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal),
                (float)parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary),
                0f,
                0f));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var real = parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal);
            var imaginary = parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary);

            // JuliaBurningShipSamplerDD stays as the exact reference the perturbation is checked against.
            if (extendedPrecision)
            {
                host.RunPerturbed(new JuliaBurningShipPerturbationSampler(real, imaginary));
                return;
            }

            host.Run(new JuliaBurningShipSamplerD(real, imaginary));
        }
    }
}
