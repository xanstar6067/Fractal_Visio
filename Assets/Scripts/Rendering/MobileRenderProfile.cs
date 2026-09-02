using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    public readonly struct MobileRenderProfile
    {
        public MobileRenderProfile(
            int maxLongEdge,
            float interactionScale,
            float settledScale,
            int tileSize,
            float cpuOverscan)
        {
            MaxLongEdge = maxLongEdge;
            InteractionScale = interactionScale;
            SettledScale = settledScale;
            TileSize = tileSize;
            CpuOverscan = cpuOverscan;
        }

        public int MaxLongEdge { get; }
        public float InteractionScale { get; }
        public float SettledScale { get; }
        public int TileSize { get; }

        /// <summary>
        /// Margin the CPU buffer renders beyond each edge, as a fraction of the visible size.
        /// Those pixels are what a pan or a zoom-out reveals instead of a stretched edge.
        ///
        /// Only the CPU path gets a margin: it reprojects the previous frame during a gesture,
        /// so it is the only path that can expose uncovered pixels. The GPU path re-renders the
        /// whole view every frame and would gain nothing for the extra samples.
        ///
        /// Cost is (1 + 2 * CpuOverscan)^2 in samples: 0.06 -> +25%.
        /// </summary>
        public float CpuOverscan { get; }

        public static MobileRenderProfile Detect()
        {
            if (!Application.isMobilePlatform)
            {
                return new MobileRenderProfile(1440, 0.5f, 1f, 64, 0.06f);
            }

            var memoryMb = SystemInfo.systemMemorySize;
            var cores = Mathf.Max(1, SystemInfo.processorCount);
            var graphicsMemoryMb = SystemInfo.graphicsMemorySize;

            if (cores <= 4 || memoryMb <= 3072 || (graphicsMemoryMb > 0 && graphicsMemoryMb <= 512))
            {
                return new MobileRenderProfile(720, 0.38f, 0.68f, 48, 0.04f);
            }

            if (cores <= 6 || memoryMb <= 6144)
            {
                return new MobileRenderProfile(1080, 0.45f, 0.82f, 56, 0.05f);
            }

            return new MobileRenderProfile(1440, 0.52f, 1f, 64, 0.06f);
        }

        /// <summary>GPU target geometry. No overscan - see <see cref="CpuOverscan"/>.</summary>
        public Viewport ResolveViewport(int screenWidth, int screenHeight, bool interacting)
        {
            var size = ResolveSize(screenWidth, screenHeight, interacting ? InteractionScale : SettledScale);
            return new Viewport(size.x, size.y);
        }

        /// <summary>
        /// CPU target geometry: settled resolution plus the reprojection margin. The CPU renderer
        /// uses one buffer for both states - interaction changes how many progressive passes run,
        /// not the buffer size.
        /// </summary>
        public Viewport ResolveCpuViewport(int screenWidth, int screenHeight)
        {
            var visible = ResolveSize(screenWidth, screenHeight, SettledScale);
            var field = 1f + 2f * Mathf.Clamp(CpuOverscan, 0f, 0.5f);
            var bufferWidth = AlignToEight(Mathf.RoundToInt(visible.x * field));
            var bufferHeight = AlignToEight(Mathf.RoundToInt(visible.y * field));
            return new Viewport(visible.x, visible.y, bufferWidth, bufferHeight);
        }

        private Vector2Int ResolveSize(int screenWidth, int screenHeight, float qualityScale)
        {
            var safeWidth = Mathf.Max(64, screenWidth);
            var safeHeight = Mathf.Max(64, screenHeight);
            var longEdge = Mathf.Max(safeWidth, safeHeight);
            var targetLongEdge = Mathf.Max(64, Mathf.RoundToInt(Mathf.Min(longEdge, MaxLongEdge) * qualityScale));
            var scale = targetLongEdge / (float)longEdge;
            var width = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeWidth * scale)));
            var height = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeHeight * scale)));
            return new Vector2Int(width, height);
        }

        private static int AlignToEight(int value) => (value + 7) & ~7;
    }
}
