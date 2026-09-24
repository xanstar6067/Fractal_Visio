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
    public sealed class MandelbrotDefinition : IFractalDefinition, IPlacesOfInterest
    {
        private static readonly FractalParameterDescriptor[] NoParameters =
            Array.Empty<FractalParameterDescriptor>();

        // The WPF version's points of interest (PresetManager), names in the locales.
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("seahorse-valley", -0.743643887037151m, 0.13182590420533m, 11500m),
            WpfPlace.At("minibrot-spike", -1.7497m, 0m, 800m),
            WpfPlace.At("vine", 0.3855604675494107229386479028m, -0.1050451711526294339131097223m, 150m),
            WpfPlace.At("spiral-galaxy", -0.16070135m, 1.0375665m, 3000m),
            WpfPlace.At("elephant-valley", 0.2869318688950451m, 0.014286693904085048m, 300m),
            WpfPlace.At("misiurewicz-spiral", -0.10109636384562m, 0.95628651080914m, 800m),
            WpfPlace.At("quad-spiral", -0.7746806106269039m, -0.1374168856037867m, 5000m),
            WpfPlace.At("period3-minibrot", -1.7548776662466927m, 0m, 60m)
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

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
