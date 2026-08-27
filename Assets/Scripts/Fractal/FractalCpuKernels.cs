using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Progressive CPU renderer. One parallel tile is in flight at a time so a
    /// new gesture never blocks the main thread waiting for a full image.
    /// </summary>
    internal sealed class FractalCpuRenderer : IDisposable
    {
        private const int PaletteResolution = 256;

        private readonly NativeArray<Color32> palette;
        private NativeArray<Color32> nativeTile;
        private Color32[] managedTile;
        private JobHandle tileHandle;
        private bool tileScheduled;
        private bool discardActiveFrame;
        private bool hasQueuedFrame;
        private FrameRequest activeFrame;
        private FrameRequest queuedFrame;
        private int nextTileIndex;
        private int tilesSinceApply;
        private int tileScheduledFrame;

        public FractalCpuRenderer(Gradient gradient)
        {
            palette = new NativeArray<Color32>(PaletteResolution, Allocator.Persistent);
            for (var i = 0; i < PaletteResolution; i++)
            {
                palette[i] = (Color32)gradient.Evaluate(i / (PaletteResolution - 1f));
            }
        }

        public bool IsBusy => tileScheduled || hasQueuedFrame || activeFrame.Target != null;
        public float Progress { get; private set; }
        public bool UsesExtendedPrecision => activeFrame.ExtendedPrecision;

        public void Request(Texture2D target, in FractalView view, int iterations, int tileSize, bool extendedPrecision)
        {
            queuedFrame = new FrameRequest(
                target,
                view,
                Mathf.Max(1, iterations),
                Mathf.Max(32, tileSize),
                extendedPrecision);
            hasQueuedFrame = true;

            if (tileScheduled)
            {
                discardActiveFrame = true;
                return;
            }

            StartQueuedFrame();
        }

        /// <summary>Poll once per Update. Returns true when visible pixels were uploaded.</summary>
        public bool Update()
        {
            if (!tileScheduled)
            {
                if (hasQueuedFrame)
                {
                    StartQueuedFrame();
                }

                return false;
            }

            // Some Unity runtimes defer an unconsumed job batch for longer than
            // expected. Give workers two frames, then establish a dependency on
            // the small tile so progressive rendering always moves forward.
            if (!tileHandle.IsCompleted && Time.frameCount - tileScheduledFrame < 2)
            {
                return false;
            }

            tileHandle.Complete();
            tileScheduled = false;

            if (discardActiveFrame)
            {
                discardActiveFrame = false;
                StartQueuedFrame();
                return false;
            }

            var rect = GetTileRect(activeFrame, nextTileIndex);
            nativeTile.CopyTo(managedTile);
            activeFrame.Target.SetPixels32(rect.x, rect.y, rect.width, rect.height, managedTile);
            nextTileIndex++;
            tilesSinceApply++;

            var tileCount = GetTileCount(activeFrame);
            var frameComplete = nextTileIndex >= tileCount;
            var shouldUpload = frameComplete || tilesSinceApply >= 2;
            if (shouldUpload)
            {
                activeFrame.Target.Apply(false, false);
                tilesSinceApply = 0;
            }

            Progress = tileCount > 0 ? nextTileIndex / (float)tileCount : 1f;

            if (hasQueuedFrame)
            {
                StartQueuedFrame();
            }
            else if (frameComplete)
            {
                activeFrame = default;
            }
            else
            {
                ScheduleCurrentTile();
            }

            return shouldUpload;
        }

        public void Invalidate()
        {
            hasQueuedFrame = false;
            if (tileScheduled)
            {
                discardActiveFrame = true;
            }
            else
            {
                activeFrame = default;
                Progress = 0f;
            }
        }

        public void CompletePendingWork()
        {
            if (!tileScheduled)
            {
                return;
            }

            tileHandle.Complete();
            tileScheduled = false;
            activeFrame = default;
            hasQueuedFrame = false;
            discardActiveFrame = false;
        }

        public void Dispose()
        {
            CompletePendingWork();

            if (nativeTile.IsCreated)
            {
                nativeTile.Dispose();
            }

            if (palette.IsCreated)
            {
                palette.Dispose();
            }
        }

        private void StartQueuedFrame()
        {
            if (!hasQueuedFrame)
            {
                return;
            }

            activeFrame = queuedFrame;
            hasQueuedFrame = false;
            discardActiveFrame = false;
            nextTileIndex = 0;
            tilesSinceApply = 0;
            Progress = 0f;
            ScheduleCurrentTile();
        }

        private void ScheduleCurrentTile()
        {
            if (activeFrame.Target == null)
            {
                activeFrame = default;
                return;
            }

            var rect = GetTileRect(activeFrame, nextTileIndex);
            var length = rect.width * rect.height;
            EnsureTileBuffers(length);

            var job = new MandelbrotTileJob
            {
                Output = nativeTile,
                Palette = palette,
                TargetWidth = activeFrame.Target.width,
                TargetHeight = activeFrame.Target.height,
                RectX = rect.x,
                RectY = rect.y,
                RectWidth = rect.width,
                CenterX = DoubleDouble.FromDecimal(activeFrame.View.x.AsDecimal),
                CenterY = DoubleDouble.FromDecimal(activeFrame.View.y.AsDecimal),
                Scale = DoubleDouble.FromDecimal(activeFrame.View.scale.AsDecimal),
                MaxIterations = activeFrame.Iterations,
                ExtendedPrecision = activeFrame.ExtendedPrecision ? (byte)1 : (byte)0
            };

            tileHandle = job.Schedule(length, 64);
            JobHandle.ScheduleBatchedJobs();
            tileScheduledFrame = Time.frameCount;
            tileScheduled = true;
        }

        private void EnsureTileBuffers(int length)
        {
            if (!nativeTile.IsCreated || nativeTile.Length != length)
            {
                if (nativeTile.IsCreated)
                {
                    nativeTile.Dispose();
                }

                nativeTile = new NativeArray<Color32>(length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            }

            if (managedTile == null || managedTile.Length != length)
            {
                managedTile = new Color32[length];
            }
        }

        private static int GetTileCount(in FrameRequest request)
        {
            if (request.Target == null)
            {
                return 0;
            }

            var columns = (request.Target.width + request.TileSize - 1) / request.TileSize;
            var rows = (request.Target.height + request.TileSize - 1) / request.TileSize;
            return columns * rows;
        }

        private static RectInt GetTileRect(in FrameRequest request, int tileIndex)
        {
            var columns = (request.Target.width + request.TileSize - 1) / request.TileSize;
            var column = tileIndex % columns;
            var row = tileIndex / columns;
            var x = column * request.TileSize;
            var y = row * request.TileSize;
            return new RectInt(
                x,
                y,
                Mathf.Min(request.TileSize, request.Target.width - x),
                Mathf.Min(request.TileSize, request.Target.height - y));
        }

        private readonly struct FrameRequest
        {
            public FrameRequest(Texture2D target, FractalView view, int iterations, int tileSize, bool extendedPrecision)
            {
                Target = target;
                View = view;
                Iterations = iterations;
                TileSize = tileSize;
                ExtendedPrecision = extendedPrecision;
            }

            public Texture2D Target { get; }
            public FractalView View { get; }
            public int Iterations { get; }
            public int TileSize { get; }
            public bool ExtendedPrecision { get; }
        }

        [BurstCompile(FloatPrecision.Standard, FloatMode.Fast)]
        private struct MandelbrotTileJob : IJobParallelFor
        {
            [WriteOnly] public NativeArray<Color32> Output;
            [ReadOnly] public NativeArray<Color32> Palette;

            public int TargetWidth;
            public int TargetHeight;
            public int RectX;
            public int RectY;
            public int RectWidth;
            public DoubleDouble CenterX;
            public DoubleDouble CenterY;
            public DoubleDouble Scale;
            public int MaxIterations;
            public byte ExtendedPrecision;

            public void Execute(int index)
            {
                var localX = index % RectWidth;
                var localY = index / RectWidth;
                var pixelX = RectX + localX;
                var pixelY = RectY + localY;
                var aspect = TargetWidth / (double)TargetHeight;
                var normalizedX = (((pixelX + 0.5d) / TargetWidth) - 0.5d) * aspect;
                var normalizedY = ((pixelY + 0.5d) / TargetHeight) - 0.5d;

                int iteration;
                if (ExtendedPrecision != 0)
                {
                    var cx = DoubleDouble.Add(CenterX, DoubleDouble.Multiply(Scale, normalizedX));
                    var cy = DoubleDouble.Add(CenterY, DoubleDouble.Multiply(Scale, normalizedY));
                    iteration = EvaluateExtended(cx, cy, MaxIterations);
                }
                else
                {
                    var scale = Scale.ToDouble();
                    var cx = CenterX.ToDouble() + scale * normalizedX;
                    var cy = CenterY.ToDouble() + scale * normalizedY;
                    iteration = EvaluateDouble(cx, cy, MaxIterations);
                }

                Output[index] = ResolveColor(iteration, MaxIterations, Palette);
            }

            private static int EvaluateDouble(double cx, double cy, int maxIterations)
            {
                var x = cx - 0.25d;
                var y2 = cy * cy;
                var q = x * x + y2;
                if (q * (q + x) <= 0.25d * y2 || (cx + 1d) * (cx + 1d) + y2 <= 0.0625d)
                {
                    return maxIterations;
                }

                var zx = 0d;
                var zy = 0d;
                var iteration = 0;
                while (iteration < maxIterations && zx * zx + zy * zy <= 4d)
                {
                    var nextX = zx * zx - zy * zy + cx;
                    zy = 2d * zx * zy + cy;
                    zx = nextX;
                    iteration++;
                }

                return iteration;
            }

            private static int EvaluateExtended(DoubleDouble cx, DoubleDouble cy, int maxIterations)
            {
                var zx = new DoubleDouble(0d);
                var zy = new DoubleDouble(0d);
                var iteration = 0;

                while (iteration < maxIterations)
                {
                    var xSquared = DoubleDouble.Square(zx);
                    var ySquared = DoubleDouble.Square(zy);
                    if (DoubleDouble.Add(xSquared, ySquared).ToDouble() > 4d)
                    {
                        break;
                    }

                    var nextX = DoubleDouble.Add(DoubleDouble.Subtract(xSquared, ySquared), cx);
                    zy = DoubleDouble.Add(DoubleDouble.Multiply(DoubleDouble.Multiply(zx, zy), 2d), cy);
                    zx = nextX;
                    iteration++;
                }

                return iteration;
            }

            private static Color32 ResolveColor(int iteration, int maxIterations, NativeArray<Color32> colors)
            {
                if (iteration >= maxIterations)
                {
                    return new Color32(3, 5, 12, 255);
                }

                var palettePosition = iteration * 0.021d;
                palettePosition -= Math.Floor(palettePosition);
                var scaled = palettePosition * (colors.Length - 1);
                var firstIndex = (int)scaled;
                var secondIndex = firstIndex + 1;
                if (secondIndex >= colors.Length)
                {
                    secondIndex = 0;
                }

                var t = scaled - firstIndex;
                var a = colors[firstIndex];
                var b = colors[secondIndex];
                return new Color32(
                    LerpByte(a.r, b.r, t),
                    LerpByte(a.g, b.g, t),
                    LerpByte(a.b, b.b, t),
                    255);
            }

            private static byte LerpByte(byte a, byte b, double t)
            {
                return (byte)(a + (b - a) * t + 0.5d);
            }
        }
    }
}
