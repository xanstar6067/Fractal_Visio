using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Placeholder perturbation renderer contract.
    /// Current implementation still uses CPU path, but follows the same interface and fallback rules.
    /// </summary>
    public sealed class PerturbationFractalRenderer : IFractalRenderer
    {
        private readonly Gradient gradient;

        public PerturbationFractalRenderer(Gradient gradient)
        {
            this.gradient = gradient;
        }

        public RenderMode Mode => RenderMode.Perturbation;

        public void Render(in FractalRenderRequest request, Texture2D target, TileDescriptor tile)
        {
            var iterationBudget = request.View.iterations + (request.IsInteracting ? 0 : request.View.iterations / 2);
            var sampleStep = request.IsInteracting ? 3 : 1;

            FractalCpuKernels.RenderMandelbrotTile(target, tile, request.View, iterationBudget, sampleStep, gradient);

            if (request.View.scale <= HighPrecision.FromDouble(1e-16))
            {
                // Minimal fallback sample refinement for very deep zooms.
                FractalCpuKernels.RenderMandelbrotTile(target, tile, request.View, iterationBudget * 2, 1, gradient);
            }
        }
    }
}
