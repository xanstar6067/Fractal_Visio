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

    }
}
