namespace FractalVisio.Fractal
{
    public sealed class FractalPrecisionManager
    {
        // Keep FAST mode longer: perturbation approximation over a wide view window can smear details.
        // Switch only when float quantization is typically visible on mobile.
        private static readonly HighPrecision FastThreshold = HighPrecision.FromDouble(5e-4);

        // Enable CPU fallback earlier than before to correct perturbation artifacts on branch-heavy regions.
        private static readonly HighPrecision PerturbationThreshold = HighPrecision.FromDouble(2e-4);

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
