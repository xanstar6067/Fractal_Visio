using System;

namespace FractalVisio.Core
{
    [Serializable]
    public struct ViewState
    {
        public HighPrecision x;
        public HighPrecision y;
        public HighPrecision scale;

        /// <summary>Screen rotation about the view centre, in radians, counter-clockwise.</summary>
        public double rotation;
        public int iterations;

        public static ViewState Default => new()
        {
            x = -0.5m,
            y = 0m,
            scale = 3m,
            rotation = 0d,
            iterations = 128
        };
    }
}
