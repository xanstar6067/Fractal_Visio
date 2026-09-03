using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// What this device can afford: buffer sizes, and how much wider than the screen the CPU path
    /// is allowed to render while a gesture is running.
    /// </summary>
    public readonly struct MobileRenderProfile
    {
        public MobileRenderProfile(
            int maxLongEdge,
            float interactionScale,
            float settledScale,
            int tileSize,
            float cpuFieldBase,
            float cpuFieldMax,
            float wideFieldFactor,
            int wideLongEdge,
            int wideWorkers)
        {
            MaxLongEdge = maxLongEdge;
            InteractionScale = interactionScale;
            SettledScale = settledScale;
            TileSize = tileSize;
            CpuFieldBase = cpuFieldBase;
            CpuFieldMax = cpuFieldMax;
            WideFieldFactor = wideFieldFactor;
            WideLongEdge = wideLongEdge;
            WideWorkers = wideWorkers;
        }

        public int MaxLongEdge { get; }
        public float InteractionScale { get; }
        public float SettledScale { get; }
        public int TileSize { get; }

        /// <summary>
        /// Field of view a CPU render covers while the viewer is moving, as a multiple of the
        /// visible area, when nothing in particular is happening. The margin is real computed
        /// pixels that a pan or a small zoom-out reveals instead of an uncovered edge.
        ///
        /// The buffer does not grow to hold it: the field widens and the visible part of the same
        /// buffer shrinks, so the margin costs resolution during the gesture rather than time. That
        /// is the right trade for a gesture, where the picture is moving anyway, and it is why this
        /// can go far past the ~1.1 a fixed pixel margin could afford.
        /// </summary>
        public float CpuFieldBase { get; }

        /// <summary>
        /// Ceiling for the same factor when the view is zooming out fast. Past roughly this much
        /// widening the visible pixels get too coarse to be worth it, and the wide background layer
        /// is the better answer.
        /// </summary>
        public float CpuFieldMax { get; }

        /// <summary>
        /// How much wider than the screen the background layer covers. Its whole purpose is to have
        /// something correct under the sharp frame during a zoom-out, so it is deliberately far
        /// wider than any single gesture can outrun.
        /// </summary>
        public float WideFieldFactor { get; }

        /// <summary>Long edge of the background layer's buffer. Coarse on purpose - it is a backdrop.</summary>
        public int WideLongEdge { get; }

        /// <summary>Worker cap for the background layer, so it cannot starve the main render.</summary>
        public int WideWorkers { get; }

        public static MobileRenderProfile Detect()
        {
            if (!Application.isMobilePlatform)
            {
                return new MobileRenderProfile(1440, 0.5f, 1f, 64, 1.12f, 2.6f, 8f, 320, 2);
            }

            var memoryMb = SystemInfo.systemMemorySize;
            var cores = Mathf.Max(1, SystemInfo.processorCount);
            var graphicsMemoryMb = SystemInfo.graphicsMemorySize;

            if (cores <= 4 || memoryMb <= 3072 || (graphicsMemoryMb > 0 && graphicsMemoryMb <= 512))
            {
                return new MobileRenderProfile(720, 0.38f, 0.68f, 48, 1.08f, 2f, 8f, 192, 1);
            }

            if (cores <= 6 || memoryMb <= 6144)
            {
                return new MobileRenderProfile(1080, 0.45f, 0.82f, 56, 1.1f, 2.2f, 8f, 224, 1);
            }

            return new MobileRenderProfile(1440, 0.52f, 1f, 64, 1.12f, 2.6f, 8f, 288, 2);
        }

        /// <summary>GPU target geometry. No margin: the GPU re-renders the whole view every frame.</summary>
        public Viewport ResolveViewport(int screenWidth, int screenHeight, bool interacting)
        {
            var size = ResolveSize(screenWidth, screenHeight, interacting ? InteractionScale : SettledScale);
            return new Viewport(size.x, size.y);
        }

        /// <summary>
        /// Size of the CPU buffer. Fixed for a given screen: the field factor changes what the
        /// buffer covers, never how many pixels it has, so the texture is allocated once.
        /// </summary>
        public Vector2Int ResolveCpuBuffer(int screenWidth, int screenHeight)
        {
            return ResolveSize(screenWidth, screenHeight, SettledScale);
        }

        /// <summary>
        /// CPU render geometry for one request: the same buffer, told how much of itself the viewer
        /// is meant to see. <paramref name="fieldFactor"/> of 1 means "exactly the screen".
        /// </summary>
        public Viewport ResolveCpuViewport(Vector2Int buffer, double fieldFactor)
        {
            var factor = Mathf.Clamp((float)fieldFactor, 1f, Mathf.Max(1f, CpuFieldMax));
            var visibleWidth = Mathf.Max(16, Mathf.RoundToInt(buffer.x / factor));
            var visibleHeight = Mathf.Max(16, Mathf.RoundToInt(buffer.y / factor));
            return new Viewport(visibleWidth, visibleHeight, buffer.x, buffer.y);
        }

        /// <summary>Background layer geometry: screen aspect, small, no margin of its own.</summary>
        public Viewport ResolveWideViewport(int screenWidth, int screenHeight)
        {
            var safeWidth = Mathf.Max(64, screenWidth);
            var safeHeight = Mathf.Max(64, screenHeight);
            var longEdge = Mathf.Max(safeWidth, safeHeight);
            var scale = Mathf.Max(1, WideLongEdge) / (float)longEdge;
            var width = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeWidth * scale)));
            var height = AlignToEight(Mathf.Max(64, Mathf.RoundToInt(safeHeight * scale)));
            return new Viewport(width, height);
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
