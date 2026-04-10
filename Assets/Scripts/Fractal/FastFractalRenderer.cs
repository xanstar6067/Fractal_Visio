using UnityEngine;

namespace FractalVisio.Fractal
{
    public sealed class FastFractalRenderer : IFractalRenderer
    {
        private readonly Gradient gradient;

        public FastFractalRenderer(Gradient gradient)
        {
            this.gradient = gradient;
        }

        public RenderMode Mode => RenderMode.Fast;

        public void Render(in FractalRenderRequest request, Texture2D target, TileDescriptor tile)
        {
            var sampleStep = request.IsInteracting ? 2 : 1;
            FractalCpuKernels.RenderMandelbrotTile(target, tile, request.View, request.View.iterations, sampleStep, gradient);
        }
    }
}
