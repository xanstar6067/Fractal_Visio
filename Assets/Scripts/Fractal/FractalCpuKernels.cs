using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace FractalVisio.Fractal
{
    /// <summary>
    /// Progressive CPU renderer. A small tile is calculated on background
    /// workers, so neither a deep zoom nor cancellation can stall Unity's main
    /// thread. Parallel.For adapts naturally to the processor count.
    /// </summary>
    internal sealed class FractalCpuRenderer : IDisposable
    {
        private const int PaletteResolution = 256;

        private readonly Color32[] palette;
        private readonly Dictionary<int, Color32[]> tileBuffers = new();
        private readonly int workerCount;
        private CancellationTokenSource tileCancellation;
        private Task<TileResult> tileTask;
        private bool discardActiveFrame;
        private bool hasQueuedFrame;
        private FrameRequest activeFrame;
        private FrameRequest queuedFrame;
        private RectInt activeRect;
        private int nextTileIndex;
        private int tilesSinceApply;

        public FractalCpuRenderer(Gradient gradient)
        {
            palette = new Color32[PaletteResolution];
            for (var i = 0; i < PaletteResolution; i++)
            {
                palette[i] = (Color32)gradient.Evaluate(i / (PaletteResolution - 1f));
            }

            // Keep one logical core free for Unity, rendering and Android OS work.
            workerCount = Math.Max(1, SystemInfo.processorCount - 1);
        }

        public bool IsBusy => tileTask != null || hasQueuedFrame || activeFrame.Target != null;
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

            if (tileTask != null)
            {
                discardActiveFrame = true;
                tileCancellation?.Cancel();
                return;
            }

            StartQueuedFrame();
        }

        /// <summary>Poll once per Update. Returns true when visible pixels were uploaded.</summary>
        public bool Update()
        {
            if (tileTask == null)
            {
                if (hasQueuedFrame)
                {
                    StartQueuedFrame();
                }

                return false;
            }

            if (!tileTask.IsCompleted)
            {
                return false;
            }

            TileResult result;
            try
            {
                result = tileTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                result = new TileResult(null, true, null);
            }
            catch (Exception exception)
            {
                result = new TileResult(null, false, exception.ToString());
            }

            tileTask = null;
            tileCancellation?.Dispose();
            tileCancellation = null;

            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError("CPU fractal tile failed: " + result.Error);
                activeFrame = default;
                discardActiveFrame = false;
                StartQueuedFrame();
                return false;
            }

            if (discardActiveFrame || result.Cancelled)
            {
                discardActiveFrame = false;
                activeFrame = default;
                StartQueuedFrame();
                return false;
            }

            activeFrame.Target.SetPixels32(
                activeRect.x,
                activeRect.y,
                activeRect.width,
                activeRect.height,
                result.Pixels);
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
                activeFrame = default;
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
            if (tileTask != null)
            {
                discardActiveFrame = true;
                tileCancellation?.Cancel();
            }
            else
            {
                activeFrame = default;
                Progress = 0f;
            }
        }

        public void CompletePendingWork()
        {
            activeFrame = default;
            hasQueuedFrame = false;
            Progress = 0f;

            if (tileTask != null)
            {
                discardActiveFrame = true;
                tileCancellation?.Cancel();
            }
            else
            {
                discardActiveFrame = false;
            }
        }

        public void Dispose()
        {
            CompletePendingWork();
            if (tileTask != null)
            {
                var cancellationToDispose = tileCancellation;
                tileTask.ContinueWith(
                    _ => cancellationToDispose?.Dispose(),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                tileTask = null;
                tileCancellation = null;
            }

            tileBuffers.Clear();
        }

        private void StartQueuedFrame()
        {
            if (!hasQueuedFrame || tileTask != null)
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

            activeRect = GetTileRect(activeFrame, nextTileIndex);
            var length = activeRect.width * activeRect.height;
            if (!tileBuffers.TryGetValue(length, out var pixels))
            {
                pixels = new Color32[length];
                tileBuffers.Add(length, pixels);
            }

            var request = new TileRequest(
                pixels,
                palette,
                activeFrame.Target.width,
                activeFrame.Target.height,
                activeRect,
                activeFrame.View,
                activeFrame.Iterations,
                activeFrame.ExtendedPrecision,
                workerCount);

            tileCancellation = new CancellationTokenSource();
            var token = tileCancellation.Token;
            tileTask = Task.Run(() => CalculateTile(request, token), token);
        }

        private static TileResult CalculateTile(TileRequest request, CancellationToken token)
        {
            try
            {
                var options = new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = request.WorkerCount
                };

                Parallel.For(0, request.Rect.height, options, localY =>
                {
                    var pixelY = request.Rect.y + localY;
                    for (var localX = 0; localX < request.Rect.width; localX++)
                    {
                        if ((localX & 15) == 0)
                        {
                            token.ThrowIfCancellationRequested();
                        }

                        var pixelX = request.Rect.x + localX;
                        var normalizedX = (((pixelX + 0.5d) / request.TargetWidth) - 0.5d) * request.Aspect;
                        var normalizedY = ((pixelY + 0.5d) / request.TargetHeight) - 0.5d;

                        int iteration;
                        if (request.ExtendedPrecision)
                        {
                            var cx = DoubleDouble.Add(request.CenterX, DoubleDouble.Multiply(request.Scale, normalizedX));
                            var cy = DoubleDouble.Add(request.CenterY, DoubleDouble.Multiply(request.Scale, normalizedY));
                            iteration = EvaluateExtended(cx, cy, request.MaxIterations, token);
                        }
                        else
                        {
                            var scale = request.Scale.ToDouble();
                            var cx = request.CenterX.ToDouble() + scale * normalizedX;
                            var cy = request.CenterY.ToDouble() + scale * normalizedY;
                            iteration = EvaluateDouble(cx, cy, request.MaxIterations, token);
                        }

                        request.Pixels[localY * request.Rect.width + localX] =
                            ResolveColor(iteration, request.MaxIterations, request.Palette);
                    }
                });

                return new TileResult(request.Pixels, false, null);
            }
            catch (OperationCanceledException)
            {
                return new TileResult(request.Pixels, true, null);
            }
            catch (Exception exception)
            {
                return new TileResult(request.Pixels, false, exception.ToString());
            }
        }

        private static int EvaluateDouble(double cx, double cy, int maxIterations, CancellationToken token)
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
                if ((iteration & 127) == 0 && token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

                var nextX = zx * zx - zy * zy + cx;
                zy = 2d * zx * zy + cy;
                zx = nextX;
                iteration++;
            }

            return iteration;
        }

        private static int EvaluateExtended(
            DoubleDouble cx,
            DoubleDouble cy,
            int maxIterations,
            CancellationToken token)
        {
            var zx = new DoubleDouble(0d);
            var zy = new DoubleDouble(0d);
            var iteration = 0;

            while (iteration < maxIterations)
            {
                if ((iteration & 63) == 0 && token.IsCancellationRequested)
                {
                    token.ThrowIfCancellationRequested();
                }

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

        private static Color32 ResolveColor(int iteration, int maxIterations, Color32[] colors)
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

        private readonly struct TileRequest
        {
            public TileRequest(
                Color32[] pixels,
                Color32[] palette,
                int targetWidth,
                int targetHeight,
                RectInt rect,
                FractalView view,
                int maxIterations,
                bool extendedPrecision,
                int workerCount)
            {
                Pixels = pixels;
                Palette = palette;
                TargetWidth = targetWidth;
                TargetHeight = targetHeight;
                Rect = rect;
                CenterX = DoubleDouble.FromDecimal(view.x.AsDecimal);
                CenterY = DoubleDouble.FromDecimal(view.y.AsDecimal);
                Scale = DoubleDouble.FromDecimal(view.scale.AsDecimal);
                MaxIterations = maxIterations;
                ExtendedPrecision = extendedPrecision;
                WorkerCount = workerCount;
                Aspect = targetWidth / (double)targetHeight;
            }

            public Color32[] Pixels { get; }
            public Color32[] Palette { get; }
            public int TargetWidth { get; }
            public int TargetHeight { get; }
            public RectInt Rect { get; }
            public DoubleDouble CenterX { get; }
            public DoubleDouble CenterY { get; }
            public DoubleDouble Scale { get; }
            public int MaxIterations { get; }
            public bool ExtendedPrecision { get; }
            public int WorkerCount { get; }
            public double Aspect { get; }
        }

        private readonly struct TileResult
        {
            public TileResult(Color32[] pixels, bool cancelled, string error)
            {
                Pixels = pixels;
                Cancelled = cancelled;
                Error = error;
            }

            public Color32[] Pixels { get; }
            public bool Cancelled { get; }
            public string Error { get; }
        }
    }
}
