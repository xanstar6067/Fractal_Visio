using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Fractals
{
    /// <summary>
    /// The Multibrot, z^p + c, for a whole power from 3 to 8 (2 is the Mandelbrot itself). The WPF
    /// "generalised Mandelbrot" also allows fractional and negative powers; those have no
    /// perturbation kernel there either and stay out of this one.
    /// </summary>
    public sealed class MultibrotDefinition : IFractalDefinition, IPlacesOfInterest
    {
        public const string PowerKey = "power";
        public const int MinimumPower = 3;
        public const int MaximumPower = 8;

        private static readonly FractalParameterDescriptor[] ParameterList =
        {
            new(PowerKey, "Power", 3d, MinimumPower, MaximumPower, FractalParameterKind.Int)
        };

        // The WPF version's points of interest, each with the power it lives at; its overviews of
        // p = 3, 4, 5 are the power slider plus "reset view".
        private static readonly PlaceOfInterest[] PlaceList =
        {
            WpfPlace.At("trefoil-valley", 0.42375m, -0.61425m, 625m, Power(3)),
            WpfPlace.At("mini-trefoil", 0.277125m, 0.73725m, 625m, Power(3)),
            WpfPlace.At("fire-spiral", -0.685875m, -0.313125m, 625m, Power(4)),
            WpfPlace.At("sunset-branches", 0.595875m, 0.672375m, 625m, Power(4)),
            WpfPlace.At("ice-spiral", 0.19425m, 0.69525m, 625m, Power(5))
        };

        public IReadOnlyList<PlaceOfInterest> Places => PlaceList;

        private static ParameterValue Power(int power) => new(PowerKey, power);

        private static readonly int PowerId = Shader.PropertyToID("_Power");

        public string Id => "multibrot";

        public string DisplayName => "Multibrot";

        // p - 1 bulbs around the origin, all inside radius 1.4 for p >= 3.
        public ViewState DefaultView => new()
        {
            x = 0m,
            y = 0m,
            scale = 2.8m,
            rotation = 0d,
            iterations = 128
        };

        public IReadOnlyList<FractalParameterDescriptor> Parameters => ParameterList;

        public PrecisionTier SupportedPrecision =>
            PrecisionTier.Float | PrecisionTier.Double | PrecisionTier.DoubleDouble | PrecisionTier.Perturbation;

        public string ShaderName => "FractalVisio/Multibrot";

        public void BindMaterial(Material material, in FractalParameterSet parameters)
        {
            material.SetFloat(PowerId, PowerOf(parameters));
        }

        public void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision)
        {
            var power = PowerOf(parameters);
            if (extendedPrecision)
            {
                host.RunPerturbed(new MultibrotPerturbationSampler(power));
                return;
            }

            host.Run(new MultibrotSamplerD(power));
        }

        /// <summary>The power the samplers and the shader agree on: whole, and inside the supported range.</summary>
        public static int ClampPower(int power) => System.Math.Clamp(power, 2, MaximumPower);

        /// <summary>(x + iy)^power in double-double, by repeated multiplication.</summary>
        public static void Power(in DoubleDouble x, in DoubleDouble y, int power, out DoubleDouble resultX, out DoubleDouble resultY)
        {
            resultX = x;
            resultY = y;
            for (var k = 1; k < power; k++)
            {
                var nextX = DoubleDouble.Subtract(DoubleDouble.Multiply(resultX, x), DoubleDouble.Multiply(resultY, y));
                resultY = DoubleDouble.Add(DoubleDouble.Multiply(resultX, y), DoubleDouble.Multiply(resultY, x));
                resultX = nextX;
            }
        }

        private static int PowerOf(in FractalParameterSet parameters) =>
            ClampPower((int)System.Math.Round(parameters.Get(PowerKey, MinimumPower)));
    }
}
