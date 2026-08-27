using UnityEngine;

namespace FractalVisio.Fractal
{
    internal readonly struct MobileRenderProfile
    {
        public MobileRenderProfile(int maxLongEdge, float interactionScale, float settledScale, int tileSize)
        {
            MaxLongEdge = maxLongEdge;
            InteractionScale = interactionScale;
            SettledScale = settledScale;
            TileSize = tileSize;
        }

        public int MaxLongEdge { get; }
        public float InteractionScale { get; }
        public float SettledScale { get; }
        public int TileSize { get; }

        public static MobileRenderProfile Detect()
        {
            if (!Application.isMobilePlatform)
            {
                return new MobileRenderProfile(1440, 0.5f, 1f, 64);
            }

            var memoryMb = SystemInfo.systemMemorySize;
            var cores = Mathf.Max(1, SystemInfo.processorCount);
            var graphicsMemoryMb = SystemInfo.graphicsMemorySize;

            if (cores <= 4 || memoryMb <= 3072 || (graphicsMemoryMb > 0 && graphicsMemoryMb <= 512))
            {
                return new MobileRenderProfile(720, 0.38f, 0.68f, 48);
            }

            if (cores <= 6 || memoryMb <= 6144)
            {
                return new MobileRenderProfile(1080, 0.45f, 0.82f, 56);
            }

            return new MobileRenderProfile(1440, 0.52f, 1f, 64);
        }

        public Vector2Int ResolveSize(int screenWidth, int screenHeight, bool interacting)
        {
            var safeWidth = Mathf.Max(64, screenWidth);
            var safeHeight = Mathf.Max(64, screenHeight);
            var longEdge = Mathf.Max(safeWidth, safeHeight);
            var qualityScale = interacting ? InteractionScale : SettledScale;
            var targetLongEdge = Mathf.Max(64, Mathf.RoundToInt(Mathf.Min(longEdge, MaxLongEdge) * qualityScale));
            var scale = targetLongEdge / (float)longEdge;
            var width = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeWidth * scale)));
            var height = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeHeight * scale)));
            return new Vector2Int(width, height);
        }

        private static int AlignToEight(int value) => (value + 7) & ~7;
    }
}
