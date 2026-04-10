using UnityEngine;

namespace FractalVisio.Fractal
{
    public enum RenderMode
    {
        Fast,
        Perturbation,
        PerturbationWithFallback
    }

    public readonly struct FractalRenderRequest
    {
        public FractalRenderRequest(FractalView view, int generationId, bool isInteracting)
        {
            View = view;
            GenerationId = generationId;
            IsInteracting = isInteracting;
        }

        public FractalView View { get; }
        public int GenerationId { get; }
        public bool IsInteracting { get; }
    }

    public readonly struct TileDescriptor
    {
        public TileDescriptor(RectInt pixelRect, int tileIndex)
        {
            PixelRect = pixelRect;
            TileIndex = tileIndex;
        }

        public RectInt PixelRect { get; }
        public int TileIndex { get; }
    }

    public interface IFractalRenderer
    {
        RenderMode Mode { get; }
        void Render(in FractalRenderRequest request, Texture2D target, TileDescriptor tile);
    }
}
