using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Simonobrot, z -> z^p |z|^p + c: the Multibrot with a radial factor, which keeps the
    /// p-fold symmetry but bends every bulb. A negative power turns it inside out; the inversion
    /// negates c's real part (WPF's two options). Power is whole here - WPF also allows fractions,
    /// which cut the plane along z^p's branch line.
    /// </summary>
    public sealed class SimonobrotDefinition : IFractalDefinition, IPlacesOfInterest
    {
        public const string PowerKey = "power";
        public const string InversionKey = "inversion";

        internal static readonly int PowerId = Shader.PropertyToID("_Power");
        internal static readonly int InversionId = Shader.PropertyToID("_Inversion");

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(PowerKey, "Power", 2d, SimonobrotStep.MinimumPower, SimonobrotStep.MaximumPower, FractalParameterKind.Int),
            new(InversionKey, "Inversion", 0d, 0d, 1d, FractalParameterKind.Bool)
        };

        // The WPF version's points of interest, each at its power, less its overview.
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("crystal-cave", 0.835m, -0.5725m, 93.75m, Power(5), Inversion(false)),
            WpfPlace.At("star", 0m, 0m, 0.5m, Power(-2), Inversion(false)),
            WpfPlace.At("thorn", 0.875m, 0.75m, 3m, Power(-3), Inversion(true)),
            WpfPlace.At("four-leaf", 0.3m, 0m, 1.2m, Power(5), Inversion(false)),
            WpfPlace.At("lilac-fjords", -0.928m, 0.3955m, 468.8m, Power(2), Inversion(false)),
            WpfPlace.At("turquoise-blades", 0.675m, 0.575m, 93.75m, Power(3), Inversion(false))
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

        public string Id => "simonobrot";

        public string DisplayName => "Simonobrot";

        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        // fp64 only: see SimonobrotSamplerD.
        public PrecisionTier SupportedPrecision => PrecisionTier.Float | PrecisionTier.Double;

        public string ShaderName => "FractalVisio/Simonobrot";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            Bind(material, parameters, false, 0d, 0d);
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            host.Run(new SimonobrotSamplerD(PowerOf(parameters), InversionOf(parameters)));
        }

        internal static int PowerOf(in FractalParameterSet parameters) =>
            SimonobrotStep.ClampPower((int)System.Math.Round(parameters.Get(PowerKey, 2d)));

        internal static bool InversionOf(in FractalParameterSet parameters) => parameters.Get(InversionKey) >= 0.5d;

        /// <summary>Every uniform the shader reads: the plane and its Julia sets share the material.</summary>
        internal static void Bind(Material material, in FractalParameterSet parameters, bool julia, double real, double imaginary)
        {
            material.SetFloat(PowerId, PowerOf(parameters));
            material.SetFloat(InversionId, InversionOf(parameters) ? 1f : 0f);
            material.SetVector(JuliaDefinition.ConstantId, new Vector4((float)real, (float)imaginary, julia ? 1f : 0f, 0f));
        }

        private static ParameterValue Power(int power) => new(PowerKey, power);

        private static ParameterValue Inversion(bool on) => new(InversionKey, on ? 1d : 0d);
    }

    /// <summary>
    /// The Simonobrot's Julia sets, with the same power and inversion; C is picked on a map of the
    /// Simonobrot drawn with them.
    /// </summary>
    public sealed class JuliaSimonobrotDefinition : IFractalDefinition, IParameterPlane
    {
        private const double DefaultReal = -0.5d;
        private const double DefaultImaginary = 0.2d;

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(SimonobrotDefinition.PowerKey, "Power", 2d, SimonobrotStep.MinimumPower, SimonobrotStep.MaximumPower,
                FractalParameterKind.Int),
            new(SimonobrotDefinition.InversionKey, "Inversion", 0d, 0d, 1d, FractalParameterKind.Bool),
            new(JuliaDefinition.ConstantRealKey, "C (real)", DefaultReal, -2d, 2d),
            new(JuliaDefinition.ConstantImaginaryKey, "C (imaginary)", DefaultImaginary, -2d, 2d)
        };

        private static readonly PlanePreset[] PresetList =
        {
            new("radial-julia", DefaultReal, DefaultImaginary),
            new("radial-three-rays", -0.3d, 0.4d),
            new("constant-inversion", 0.5d, 0.2d)
        };

        public string Id => "julia-simonobrot";

        public string DisplayName => "Simonobrot Julia";

        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision => PrecisionTier.Float | PrecisionTier.Double;

        public string ShaderName => "FractalVisio/Simonobrot";

        public string PlaneFractalId => "simonobrot";

        public string RealKey => JuliaDefinition.ConstantRealKey;

        public string ImaginaryKey => JuliaDefinition.ConstantImaginaryKey;

        public PlaneRect PlaneBounds => new(-2d, 2d, -1.5d, 1.5d);

        public IReadOnlyList<PlanePreset> Presets => PresetList;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            SimonobrotDefinition.Bind(
                material, parameters, true,
                parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal),
                parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            host.Run(new SimonobrotSamplerD(
                SimonobrotDefinition.PowerOf(parameters),
                SimonobrotDefinition.InversionOf(parameters),
                parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal),
                parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary)));
        }
    }
}
