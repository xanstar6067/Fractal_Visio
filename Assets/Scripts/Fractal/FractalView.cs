using System;

namespace FractalVisio.Fractal
{
    [Serializable]
    public struct FractalView
    {
        public HighPrecision x;
        public HighPrecision y;
        public HighPrecision scale;
        public int iterations;

        public static FractalView Default => new()
        {
            x = -0.5m,
            y = 0m,
            scale = 3m,
            iterations = 128
        };
    }
}
