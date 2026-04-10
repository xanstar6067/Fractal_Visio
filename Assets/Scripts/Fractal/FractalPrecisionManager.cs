namespace FractalVisio.Fractal
{
    public sealed class FractalPrecisionManager
    {
        private static readonly HighPrecision FastThreshold = HighPrecision.FromDouble(1e-8);
        private static readonly HighPrecision PerturbationThreshold = HighPrecision.FromDouble(1e-16);

        public RenderMode GetMode(in FractalView view)
        {
            if (view.scale > FastThreshold)
            {
                return RenderMode.Fast;
            }

            if (view.scale > PerturbationThreshold)
            {
                return RenderMode.Perturbation;
            }

            return RenderMode.PerturbationWithFallback;
        }

        public int ResolveIterations(in FractalView view, bool interacting)
        {
            return view.iterations;
        }
    }
}
