using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Mandelbrot set. Also the reference for what a fractal costs to add: this file, the two
    /// sampler structs next to it, and Shaders/Mandelbrot.shader.
    /// </summary>
    public sealed class MandelbrotDefinition : IFractalDefinition
    {
        private static readonly FractalParameterDescriptor[] NoParameters =
            Array.Empty<FractalParameterDescriptor>();

        public string Id => "mandelbrot";

        public string DisplayName => "Mandelbrot";

        public ViewState DefaultView => new()
        {
            x = -0.5m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => NoParameters;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/Mandelbrot";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            // Nothing of its own: the shared uniforms the renderer sets are the whole input.
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            // Deep renders go through perturbation. MandelbrotSamplerDD stays as the exact
            // reference the perturbation result is checked against.
            if (extendedPrecision)
            {
                host.RunPerturbed(new MandelbrotPerturbationSampler());
                return;
            }

            host.Run(new MandelbrotSamplerD());
        }
    }
}
