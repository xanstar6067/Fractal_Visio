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
    public sealed class CelticDefinition : IFractalDefinition, IPlacesOfInterest
    {
        private static readonly FractalParameterDescriptor[] NoParameters =
            Array.Empty<FractalParameterDescriptor>();

        // The WPF version's points of interest, less its overview.
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("mini-celtic", -1.415m, 0.137m, 150m),
            WpfPlace.At("antenna-celtic", -1.395m, 0m, 60m),
            WpfPlace.At("amethyst-celtic", -1.479m, 0m, 150m),
            WpfPlace.At("misty-antenna", -1.5m, 0m, 6m)
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

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
