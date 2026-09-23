namespace FractalVisio.Fractals
{
    /// <summary>
    /// Perturbation arithmetic shared by the folded variants - Burning Ship, Celtic - whose
    /// iteration takes an absolute value somewhere. Ported from the WPF engine's
    /// <c>FoldedDelta</c>.
    /// </summary>
    internal static class Fold
    {
        /// <summary>
        /// |Z + d| - |Z| without catastrophic cancellation. While d has not flipped the sign of Z
        /// (the usual case deep in) this is exactly +-d; on a flip it is the reflected expression,
        /// and d is then comparable to Z, so the pixel rebases right after.
        /// </summary>
        public static double Delta(double reference, double delta)
        {
            if (reference > 0d)
            {
                return delta > -reference ? delta : -(delta + 2d * reference);
            }

            if (reference < 0d)
            {
                return delta < -reference ? -delta : delta + 2d * reference;
            }

            return System.Math.Abs(delta);
        }
    }
}
