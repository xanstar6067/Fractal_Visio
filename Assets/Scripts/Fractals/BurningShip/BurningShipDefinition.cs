using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Burning Ship. Added as the acceptance test for the template: this file, the two sampler
    /// structs beside it, one shader, and one line in <see cref="FractalCatalog"/> - nothing in
    /// Rendering, App or the UI was touched to make it work.
    ///
    /// It also carries a parameter, which Mandelbrot does not, so the descriptor -> parameter set
    /// -> sampler / material path is exercised end to end.
    /// </summary>
    public sealed class BurningShipDefinition : IFractalDefinition, IPlacesOfInterest
    {
        public const string BailoutKey = "bailout";

        // The WPF version's points of interest, less its overview of the whole ship - that is "reset view".
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("deep-sea-ship", -1.7623214771385076201641266142m, 0.0200163188745603751465416114m, 40m),
            WpfPlace.At("ghost-sails", -1.7423683296426555512135816837m, 0.0648050817843091259643027922m, 76m),
            WpfPlace.At("armada", -1.78m, 0.035m, 25m),
            WpfPlace.At("golden-mini-ship", -1.8621m, 0.001m, 640m),
            WpfPlace.At("stern-ship", -1.9405m, 0.0018m, 1000m),
            WpfPlace.At("copper-lace", -0.5m, 1m, 6m)
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

        // Bailout is on the squared modulus. The default is far above the 4 that decides
        // membership: the smooth escape count needs the orbit to be well clear of the set before
        // it approximates anything, and below ~64 the image bands again. See EscapeMath.Smooth.
        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(BailoutKey, "Bailout", 256d, 4d, 65536d, FractalParameterKind.Double, logarithmic: true)
        };

        public string Id => "burning-ship";

        public string DisplayName => "Burning Ship";

        // Sits higher and further left than the Mandelbrot, and the whole shape fits in a span of 3.
        public ViewState DefaultView => new()
        {
            x = -0.4m,
            y = 0.5m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/BurningShip";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetFloat(BailoutId, (float)parameters.Get(BailoutKey, 256d));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var bailout = parameters.Get(BailoutKey, 256d);

            if (extendedPrecision)
            {
                host.RunPerturbed(new BurningShipPerturbationSampler(bailout));
                return;
            }

            host.Run(new BurningShipSamplerD(bailout));
        }

        private static readonly int BailoutId = Shader.PropertyToID("_Bailout");
    }
}
