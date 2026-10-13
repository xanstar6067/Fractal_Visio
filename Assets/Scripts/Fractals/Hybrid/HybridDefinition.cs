using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// WPF's hybrid constructor on its parameter plane: z_0 = 0, c = the pixel, and the iteration
    /// cycles through a sequence of formulas the user builds - each row a formula, its power and how
    /// many times in a row it is taken (<see cref="IStepSequence"/>, edited in the fractal panel).
    /// Every step is a <see cref="FoldedFormula"/>, so the engine is the folded family's with the
    /// formula changing from step to step; the shader is <c>Shaders/Hybrid.shader</c>.
    /// </summary>
    public sealed class HybridDefinition : IFractalDefinition, IPlacesOfInterest, IStepSequence
    {
        public const string ShaderPath = "FractalVisio/Hybrid";

        private static readonly FractalParameterDescriptor[] ParameterList = HybridSequence.Descriptors().ToArray();

        // WPF's second view - framed to show the whole set, which WPF's cut off on the left - and a
        // few more cycles worth seeing whole. Each carries its sequence.
        private static readonly PlaceOfInterest[] PlaceList =
        {
            new PlaceOfInterest("celtic-mandelbar", -0.6m, 0m, 2.4m,
                HybridSequence.Values(("celtic", 2, 2), ("tricorn", 3, 1))),
            new PlaceOfInterest("ship-and-bulb", -0.35m, 0m, 2.6m,
                HybridSequence.Values(("burning-ship", 2, 1), ("mandelbrot", 3, 1))),
            new PlaceOfInterest("perpendicular-weave", -0.3m, 0m, 2.8m,
                HybridSequence.Values(("perpendicular-mandelbrot", 2, 1), ("perpendicular-burning-ship", 2, 1))),
            new PlaceOfInterest("quartic-heart", 0m, 0m, 2.4m,
                HybridSequence.Values(("mandelbrot", 2, 1), ("quartic-false-quasi-heart", 4, 1)))
        };

        public string Id => "hybrid";

        public string DisplayName => "Hybrid";

        // WPF's frame: centred at -0.35, 4 wide - 3 high on its 4:3 canvas.
        public ViewState DefaultView => new()
        {
            x = -0.35m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => ShaderPath;

        public int MaximumRows => HybridSequence.MaximumRows;

        public int MaximumCycle => HybridSequence.MaximumCycle;

        public string RowsKey => HybridSequence.RowsKey;

        public string FormulaKey(int row) => HybridSequence.FormulaKey(row);

        public string PowerKey(int row) => HybridSequence.PowerKey(row);

        public string RepeatsKey(int row) => HybridSequence.RepeatsKey(row);

        public IReadOnlyList<StepChoice> Choices => HybridSteps.Choices;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            HybridShader.Bind(material, HybridCycle.Of(parameters), false, 0d, 0d);
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var cycle = HybridCycle.Of(parameters);

            // HybridSamplerDD stays as the exact reference the perturbation is checked against.
            if (extendedPrecision)
            {
                host.RunPerturbed(new HybridPerturbationSampler(cycle));
                return;
            }

            host.Run(new HybridSamplerD(cycle));
        }
    }

    /// <summary>
    /// A hybrid's Julia sets: the pixel is the start, C is fixed and picked on a map of the hybrid's
    /// own plane - drawn with the same sequence, which the map takes from here by key.
    /// </summary>
    public sealed class HybridJuliaDefinition : IFractalDefinition, IParameterPlane, IStepSequence
    {
        // Just inside the default sequence's set, where its Julia set is lace. WPF's two constants
        // lie deep inside: solid blots with a ragged edge; they follow in the list.
        private const double DefaultReal = -0.55587d;
        private const double DefaultImaginary = 0.60259d;

        private static readonly FractalParameterDescriptor[] ParameterList = BuildParameters();

        // On the edge of the default sequence's set, then WPF's default and its "other view".
        private static readonly PlanePreset[] PresetList =
        {
            new("lace", DefaultReal, DefaultImaginary),
            new("twin-isles", 0.16282d, 0.64624d),
            new("ribbons", -0.10612d, 0.87849d),
            new("cloud-island", -0.4d, -0.2d),
            new("diamond", -0.4d, 0.2d)
        };

        public string Id => "julia-hybrid";

        public string DisplayName => "Hybrid Julia";

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

        public string ShaderName => HybridDefinition.ShaderPath;

        public string PlaneFractalId => "hybrid";

        public string RealKey => JuliaDefinition.ConstantRealKey;

        public string ImaginaryKey => JuliaDefinition.ConstantImaginaryKey;

        // The plane's default view on WPF's 4:3 canvas: 4 wide, 3 high, around -0.35.
        public PlaneRect PlaneBounds => new(-2.35d, 1.65d, -1.5d, 1.5d);

        public IReadOnlyList<PlanePreset> Presets => PresetList;

        public int MaximumRows => HybridSequence.MaximumRows;

        public int MaximumCycle => HybridSequence.MaximumCycle;

        public string RowsKey => HybridSequence.RowsKey;

        public string FormulaKey(int row) => HybridSequence.FormulaKey(row);

        public string PowerKey(int row) => HybridSequence.PowerKey(row);

        public string RepeatsKey(int row) => HybridSequence.RepeatsKey(row);

        public IReadOnlyList<StepChoice> Choices => HybridSteps.Choices;

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            HybridShader.Bind(
                material, HybridCycle.Of(parameters), true,
                parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal),
                parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var cycle = HybridCycle.Of(parameters);
            var real = parameters.Get(JuliaDefinition.ConstantRealKey, DefaultReal);
            var imaginary = parameters.Get(JuliaDefinition.ConstantImaginaryKey, DefaultImaginary);
            if (extendedPrecision)
            {
                host.RunPerturbed(new HybridPerturbationSampler(cycle, real, imaginary));
                return;
            }

            host.Run(new HybridSamplerD(cycle, real, imaginary));
        }

        private static FractalParameterDescriptor[] BuildParameters()
        {
            // The sequence first: HybridCycle reads it by position.
            var list = HybridSequence.Descriptors();
            list.Add(new FractalParameterDescriptor(JuliaDefinition.ConstantRealKey, "C (real)", DefaultReal, -2.5d, 2.5d));
            list.Add(new FractalParameterDescriptor(JuliaDefinition.ConstantImaginaryKey, "C (imaginary)", DefaultImaginary, -2.5d, 2.5d));
            return list.ToArray();
        }
    }

    /// <summary>
    /// The cycle as <c>Shaders/Hybrid.shader</c> reads it: per phase, the same five vectors
    /// <see cref="FoldedShader"/> sets for one formula, as arrays of <see cref="Slots"/>.
    /// </summary>
    internal static class HybridShader
    {
        /// <summary>Array length in the shader. A uniform array's size is fixed by its first upload: always this many.</summary>
        public const int Slots = HybridSequence.MaximumCycle;

        private static readonly int PolyAId = Shader.PropertyToID("_HybridPolyA");
        private static readonly int PolyCId = Shader.PropertyToID("_HybridPolyC");
        private static readonly int TailId = Shader.PropertyToID("_HybridTail");
        private static readonly int FlagsId = Shader.PropertyToID("_HybridFlags");
        private static readonly int FormId = Shader.PropertyToID("_HybridForm");
        private static readonly int CycleId = Shader.PropertyToID("_HybridCycle");

        // Reused: materials are bound on the main thread only.
        private static readonly Vector4[] PolyA = new Vector4[Slots];
        private static readonly Vector4[] PolyC = new Vector4[Slots];
        private static readonly Vector4[] Tail = new Vector4[Slots];
        private static readonly Vector4[] Flags = new Vector4[Slots];
        private static readonly Vector4[] Form = new Vector4[Slots];

        /// <summary>Every uniform the shader reads, Julia ones included: the material is shared by both hybrids.</summary>
        public static void Bind(Material material, HybridCycle cycle, bool julia, double real, double imaginary)
        {
            var steps = cycle.Steps;
            for (var i = 0; i < Slots; i++)
            {
                // Unused slots repeat the first step: harmless, and never read.
                ref readonly var formula = ref steps[i < steps.Length ? i : 0];
                PolyA[i] = new Vector4((float)formula.A40, (float)formula.A22, (float)formula.A04, (float)formula.A20);
                PolyC[i] = new Vector4((float)formula.C40, (float)formula.C22, (float)formula.C04, (float)formula.C20);
                Tail[i] = new Vector4((float)formula.A02, (float)formula.A00, (float)formula.C02, (float)formula.C00);
                Flags[i] = new Vector4(
                    formula.FoldX ? 1f : 0f, formula.FoldY ? 1f : 0f, formula.FoldA ? 1f : 0f, formula.FoldC ? 1f : 0f);
                Form[i] = new Vector4(
                    formula.Odd ? 1f : 0f, (float)formula.ImaginaryFactor, formula.Swap ? 1f : 0f, formula.Degree);
            }

            material.SetVectorArray(PolyAId, PolyA);
            material.SetVectorArray(PolyCId, PolyC);
            material.SetVectorArray(TailId, Tail);
            material.SetVectorArray(FlagsId, Flags);
            material.SetVectorArray(FormId, Form);
            material.SetVector(CycleId, new Vector4(steps.Length, 0f, 0f, 0f));
            material.SetVector(JuliaDefinition.ConstantId, new Vector4((float)real, (float)imaginary, julia ? 1f : 0f, 0f));
        }
    }
}
