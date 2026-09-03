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
    public sealed class BurningShipDefinition : IFractalDefinition
    {
        public const string BailoutKey = "bailout";

        // Bailout is on the squared modulus. The default is far above the 4 that decides
        // membership: the smooth escape count needs the orbit to be well clear of the set before
        // it approximates anything, and below ~64 the image bands again. See EscapeMath.Smooth.
        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(BailoutKey, "Bailout", 256d, 4d, 65536d, FractalParameterKind.Double, logarithmic: true)
        };

        public string Id => "burning-ship";

        public string DisplayName => "Burning Ship";

        // Sits lower and further left than the Mandelbrot, and the whole shape fits in a span of 3.
        public ViewState DefaultView => new()
        {
            x = -0.4m,
            y = -0.5m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble;

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
                host.RunExtended(new BurningShipSamplerDD(bailout));
                return;
            }

            host.Run(new BurningShipSamplerD(bailout));
        }

        private static readonly int BailoutId = Shader.PropertyToID("_Bailout");
    }
}
