using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Tricorn, or Mandelbar: the Mandelbrot iteration with the conjugate squared. First of the
    /// family variants ported from the WPF catalog (block 2 of docs/ROADMAP-WPF.md) - this file,
    /// its samplers, <c>Shaders/Tricorn.shader</c> and a catalog line.
    /// </summary>
    public sealed class TricornDefinition : IFractalDefinition
    {
        private static readonly FractalParameterDescriptor[] NoParameters =
            Array.Empty<FractalParameterDescriptor>();

        public string Id => "tricorn";

        public string DisplayName => "Tricorn";

        // Three arms at 180, 60 and 300 degrees: the real axis runs from -2 to 1/4, the other two
        // arms reach x = 1 and |y| = 1.7, so the frame is centred left of the origin.
        public ViewState DefaultView => new()
        {
            x = -0.5m,
            y = 0m,
            scale = 3.8m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => NoParameters;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/Tricorn";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            if (extendedPrecision)
            {
                host.RunPerturbed(new TricornPerturbationSampler());
                return;
            }

            host.Run(new TricornSamplerD());
        }
    }
}
