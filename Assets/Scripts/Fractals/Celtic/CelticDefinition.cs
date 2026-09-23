using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Celtic Mandelbrot: the absolute value of the real part of z^2. Ported from the WPF
    /// catalog with its perturbation kernel; see <see cref="CelticPerturbationSampler"/>.
    /// </summary>
    public sealed class CelticDefinition : IFractalDefinition
    {
        private static readonly FractalParameterDescriptor[] NoParameters =
            Array.Empty<FractalParameterDescriptor>();

        public string Id => "celtic";

        public string DisplayName => "Celtic Mandelbrot";

        // Roughly the Mandelbrot's footprint - the same real axis - so the same frame.
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

        public string ShaderName => "FractalVisio/Celtic";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            if (extendedPrecision)
            {
                host.RunPerturbed(new CelticPerturbationSampler());
                return;
            }

            host.Run(new CelticSamplerD());
        }
    }
}
