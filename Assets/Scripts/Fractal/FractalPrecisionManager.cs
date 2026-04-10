namespace FractalVisio.Fractal
{
    public sealed class FractalPrecisionManager
    {
        private readonly HighPrecision fastThreshold;
        private readonly HighPrecision perturbationThreshold;

        public FractalPrecisionManager(double fastThresholdScale, double perturbationThresholdScale)
        {
            fastThreshold = HighPrecision.FromDouble(fastThresholdScale);
            perturbationThreshold = HighPrecision.FromDouble(perturbationThresholdScale);
        }

        public RenderMode GetMode(in FractalView view)
        {
            if (view.scale > fastThreshold)
            {
                return RenderMode.Fast;
            }

            if (view.scale > perturbationThreshold)
            {
                return RenderMode.Perturbation;
            }

            return RenderMode.PerturbationWithFallback;
        }

        public int ResolveIterations(in FractalView view, bool interacting)
        {
            var baseIterations = view.iterations;

            if (interacting)
            {
                return baseIterations / 2;
            }

            return baseIterations;
        }
    }
}
