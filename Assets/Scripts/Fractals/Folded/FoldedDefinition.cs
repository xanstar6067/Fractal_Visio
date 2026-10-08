using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// A member of the folded polynomial family on its parameter plane: z_0 = 0, c = the pixel.
    /// One class for all of them - a member is data (<see cref="FoldedFamily"/>), not a type - and
    /// one shader, <c>Shaders/Folded.shader</c>, which receives the formula as uniforms.
    /// </summary>
    public sealed class FoldedDefinition : IFractalDefinition, IPlacesOfInterest
    {
        public const string ShaderPath = "FractalVisio/Folded";

        private static readonly FractalParameterDescriptor[] NoParameters = Array.Empty<FractalParameterDescriptor>();

        private readonly FoldedFormula formula;
        private readonly ViewState defaultView;
        private readonly PlaceOfInterest[] places;

        public FoldedDefinition(string id, string name, in FoldedFormula formula, in ViewState defaultView, params PlaceOfInterest[] places)
        {
            Id = id;
            DisplayName = name;
            this.formula = formula;
            this.defaultView = defaultView;
            this.places = places ?? Array.Empty<PlaceOfInterest>();
        }

        public string Id { get; }

        public string DisplayName { get; }

        public FoldedFormula Formula => formula;

        public ViewState DefaultView => defaultView;

        public IReadOnlyList<FractalParameterDescriptor> Parameters => NoParameters;

        public IReadOnlyList<PlaceOfInterest> Places => places;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => ShaderPath;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            FoldedShader.Bind(material, formula, false, 0d, 0d);
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            // FoldedSamplerDD stays as the exact reference the perturbation is checked against.
            if (extendedPrecision)
            {
                host.RunPerturbed(new FoldedPerturbationSampler(formula));
                return;
            }

            host.Run(new FoldedSamplerD(formula));
        }
    }

    /// <summary>
    /// The Julia sets of a folded formula: the pixel is the start, C is fixed and chosen on a map of
    /// the formula's own parameter plane - the same map, so a C picked on it means this map.
    /// </summary>
    public sealed class FoldedJuliaDefinition : IFractalDefinition, IParameterPlane
    {
        private readonly FoldedFormula formula;
        private readonly FractalParameterDescriptor[] parameters;
        private readonly PlanePreset[] presets;
        private readonly double defaultReal;
        private readonly double defaultImaginary;

        /// <param name="planeId">The fractal drawn as the map of C: the same formula's parameter plane.</param>
        /// <param name="planeBounds">What that map shows at first.</param>
        public FoldedJuliaDefinition(
            string id, string name, in FoldedFormula formula, string planeId, in PlaneRect planeBounds,
            double defaultReal, double defaultImaginary, params PlanePreset[] presets)
        {
            Id = id;
            DisplayName = name;
            this.formula = formula;
            PlaneFractalId = planeId;
            PlaneBounds = planeBounds;
            this.defaultReal = defaultReal;
            this.defaultImaginary = defaultImaginary;
            this.presets = presets ?? Array.Empty<PlanePreset>();
            parameters = new[]
            {
                new FractalParameterDescriptor(JuliaDefinition.ConstantRealKey, "C (real)", defaultReal, -2.5d, 2.5d),
                new FractalParameterDescriptor(JuliaDefinition.ConstantImaginaryKey, "C (imaginary)", defaultImaginary, -2.5d, 2.5d)
            };
        }

        public string Id { get; }

        public string DisplayName { get; }

        // WPF frames every Julia set of the family the same way: centred, 3 high.
        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => parameters;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => FoldedDefinition.ShaderPath;

        public string PlaneFractalId { get; }

        public string RealKey => JuliaDefinition.ConstantRealKey;

        public string ImaginaryKey => JuliaDefinition.ConstantImaginaryKey;

        public PlaneRect PlaneBounds { get; }

        public IReadOnlyList<PlanePreset> Presets => presets;

        public void BindMaterial(Material material, in FractalParameterSet values)
        {
            FoldedShader.Bind(
                material, formula, true,
                values.Get(JuliaDefinition.ConstantRealKey, defaultReal),
                values.Get(JuliaDefinition.ConstantImaginaryKey, defaultImaginary));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet values, bool extendedPrecision)
        {
            var real = values.Get(JuliaDefinition.ConstantRealKey, defaultReal);
            var imaginary = values.Get(JuliaDefinition.ConstantImaginaryKey, defaultImaginary);
            if (extendedPrecision)
            {
                host.RunPerturbed(new FoldedPerturbationSampler(formula, real, imaginary));
                return;
            }

            host.Run(new FoldedSamplerD(formula, real, imaginary));
        }
    }

    /// <summary>The formula as <c>Shaders/Folded.shader</c> reads it: polynomial coefficients and flags.</summary>
    internal static class FoldedShader
    {
        private static readonly int PolyAId = UnityEngine.Shader.PropertyToID("_FoldPolyA");
        private static readonly int PolyCId = UnityEngine.Shader.PropertyToID("_FoldPolyC");
        private static readonly int TailId = UnityEngine.Shader.PropertyToID("_FoldTail");
        private static readonly int FlagsId = UnityEngine.Shader.PropertyToID("_FoldFlags");
        private static readonly int FormId = UnityEngine.Shader.PropertyToID("_FoldForm");

        /// <summary>Every uniform the shader reads, Julia ones included: the material is shared by all members.</summary>
        public static void Bind(Material material, in FoldedFormula formula, bool julia, double real, double imaginary)
        {
            material.SetVector(PolyAId, new Vector4((float)formula.A40, (float)formula.A22, (float)formula.A04, (float)formula.A20));
            material.SetVector(PolyCId, new Vector4((float)formula.C40, (float)formula.C22, (float)formula.C04, (float)formula.C20));
            material.SetVector(TailId, new Vector4((float)formula.A02, (float)formula.A00, (float)formula.C02, (float)formula.C00));
            material.SetVector(FlagsId, new Vector4(
                formula.FoldX ? 1f : 0f, formula.FoldY ? 1f : 0f, formula.FoldA ? 1f : 0f, formula.FoldC ? 1f : 0f));
            material.SetVector(FormId, new Vector4(
                formula.Odd ? 1f : 0f, (float)formula.ImaginaryFactor, formula.Swap ? 1f : 0f, formula.Degree));
            material.SetVector(JuliaDefinition.ConstantId, new Vector4((float)real, (float)imaginary, julia ? 1f : 0f, 0f));
        }
    }
}
