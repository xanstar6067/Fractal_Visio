using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.Rendering
{
    /// <summary>
    /// Progressive full-frame CPU renderer. Every pass covers the whole image at
    /// once on background workers, going coarse to fine: pass 0 paints one sample
    /// per 16x16 block, then 8x8, 4x4, 2x2 and finally every pixel. Work is handed
    /// out as tiles from a shared cursor, ordered from the centre outward, so the
    /// middle of the screen sharpens first and one tile of pure interior cannot
    /// hold up the rest of the pass.
    ///
    /// The renderer never warps its own pixels to follow the view. A published frame
    /// is a picture of one <see cref="ViewState"/>, kept in <see cref="PublishedView"/>,
    /// and following the gesture is the compositor's job (see <c>FrameCompositor</c> and
    /// <see cref="FramePlacement"/>). That is why the buffer is only ever uploaded at a
    /// pass boundary: until the first pass of a request covers the whole buffer, the
    /// texture still holds the previous frame, which is a correct picture of a
    /// different view rather than a half-correct picture of this one.
    ///
    /// NOTE (see CLAUDE.md "Rendering notes"): the kernel is deliberately plain
    /// managed <see cref="Parallel.For"/> for now, matching the WPF prototype
    /// and avoiding new packages. The planned speed-up is Burst + Unity.Jobs with
    /// the per-pixel iteration moved into an IJobParallelFor.
    /// </summary>
    public sealed class FractalCpuRenderer : IDisposable
    {
        private const int PaletteResolution = 256;
        private const int InteractivePassCount = 2;      // steps 16, 8 while the view keeps moving
        private const double UploadIntervalSeconds = 0.04d;

        internal static readonly int[] StepPlan = { 16, 8, 4, 2, 1 };

        /// <summary>
        /// Every render region and tile starts on this grid. The "already computed in a coarser
        /// pass" skip tests absolute pixel coordinates, so a region or a tile that began off-grid
        /// would sample different points than the pass before it and seam against it.
        /// </summary>
        private const int SampleAlign = 16;

        /// <summary>
        /// Unit of work handed to a thread. A multiple of <see cref="SampleAlign"/>, and small
        /// enough that one tile of pure interior - the most expensive thing a fractal can hand a
        /// worker - is a fraction of a pass rather than its tail.
        /// </summary>
        private const int TileSize = 64;

        // Passes at or above this step also cover the overscan margin; finer passes stay inside
        // the visible rectangle. The margin is only ever seen mid-gesture, where a coarse but
        // correct edge is enough, and refining it at every step would spend a quarter of the
        // render on pixels nobody is looking at.
        private const int MarginStepThreshold = 4;

        /// <summary>Interior, and "nothing rendered here yet". Shared with the compositor.</summary>
        public static readonly Color32 InteriorColor = new(3, 5, 12, 255);

        private readonly Color32[] palette;
        private readonly int workerCount;

        private Color32[] frame = Array.Empty<Color32>();

        // What the main thread actually uploads. The render worker copies `frame`
        // into it only between passes - i.e. when `frame` is a whole-image render
        // at one step size, never a half-updated mix of a pass and the coarser
        // image beneath it. Guarded by `publishLock` so SetPixels32 never reads it
        // mid-copy. The view that snapshot stands for travels with it.
        private Color32[] publishFrame = Array.Empty<Color32>();
        private ViewState publishView;
        private bool publishValid;
        private readonly object publishLock = new object();

        private int frameWidth;
        private int frameHeight;

        private Texture2D target;
        private Task renderTask;
        private CancellationTokenSource cancellation;

        private FrameRequest queued;
        private FrameRequest activeRequest;
        private bool hasQueued;
        private volatile bool renderActive;

        private volatile bool frameDirty;
        private volatile int passCursor;
        private volatile int passFloorIndex;
        private volatile int passCeilingIndex;
        private long samplesDone;
        private long samplesTotal;
        private double lastUploadTime;

        /// <param name="maximumWorkers">
        /// Cap on background threads, 0 for "as many as the machine can spare". The wide
        /// background layer passes a small number so it cannot starve the renderer whose output
        /// the viewer is actually looking at.
        /// </param>
        public FractalCpuRenderer(Gradient gradient, int maximumWorkers = 0)
        {
            palette = new Color32[PaletteResolution];
            for (var i = 0; i < PaletteResolution; i++)
            {
                palette[i] = (Color32)gradient.Evaluate(i / (PaletteResolution - 1f));
            }

            // Keep one logical core free for Unity, rendering and Android OS work.
            var available = Math.Max(1, SystemInfo.processorCount - 1);
            workerCount = maximumWorkers > 0 ? Math.Min(available, maximumWorkers) : available;
        }

        public bool IsBusy => renderActive || hasQueued;
        public float Progress { get; private set; }
        public bool UsesExtendedPrecision => activeRequest.ExtendedPrecision;
        public int PassCount => Mathf.Max(1, passCeilingIndex - passFloorIndex);
        public int CurrentPass => Mathf.Clamp(passCursor - passFloorIndex + 1, 1, PassCount);

        /// <summary>True once the target texture holds a whole-image render of a known view.</summary>
        public bool HasPublished { get; private set; }

        /// <summary>The view the target texture shows. Only meaningful with <see cref="HasPublished"/>.</summary>
        public ViewState PublishedView { get; private set; }

        /// <summary>Aspect of the published buffer, margins included. Feeds <see cref="FramePlacement"/>.</summary>
        public double PublishedAspect { get; private set; } = 1d;

        public void Request(
            Texture2D texture,
            in Viewport viewport,
            IFractalDefinition definition,
            in FractalParameterSet parameters,
            in ViewState view,
            int iterations,
            bool extendedPrecision,
            bool interacting)
        {
            queued = new FrameRequest(
                texture, viewport, definition, parameters, view, Mathf.Max(1, iterations), extendedPrecision, interacting);
            hasQueued = true;

            if (renderActive)
            {
                // Update() picks up the queued frame once the running task unwinds.
                cancellation?.Cancel();
                return;
            }

            StartQueued();
        }

        /// <summary>Poll once per Update. Returns true when visible pixels were uploaded.</summary>
        public bool Update()
        {
            var uploaded = false;

            if (renderActive && renderTask != null && renderTask.IsCompleted)
            {
                DrainTask();
                renderTask = null;
                cancellation?.Dispose();
                cancellation = null;
                renderActive = false;

                if (!hasQueued)
                {
                    uploaded = UploadFrame();
                    Progress = 1f;
                }
            }

            if (!renderActive && hasQueued)
            {
                StartQueued();
            }

            if (renderActive && frameDirty)
            {
                var now = Time.realtimeSinceStartupAsDouble;
                if (now - lastUploadTime >= UploadIntervalSeconds)
                {
                    lastUploadTime = now;
                    uploaded |= UploadFrame();
                }
            }

            return uploaded;
        }

        public void Invalidate()
        {
            hasQueued = false;
            if (renderActive)
            {
                cancellation?.Cancel();
            }
            else
            {
                Progress = 0f;
            }
        }

        /// <summary>
        /// Forget what the texture shows. The pixels stay, but they stop being usable as a
        /// placeholder - call it when the fractal, its parameters or the palette changed, because
        /// the frame is then a picture of something else entirely rather than of another view.
        /// </summary>
        public void DiscardPublished()
        {
            HasPublished = false;
            lock (publishLock)
            {
                publishValid = false;
            }
        }

        public void CompletePendingWork()
        {
            hasQueued = false;
            target = null;
            Progress = 0f;
            DiscardPublished();
            if (renderActive)
            {
                cancellation?.Cancel();
            }
        }

        public void Dispose()
        {
            hasQueued = false;
            target = null;

            if (cancellation != null)
            {
                cancellation.Cancel();
                var toDispose = cancellation;
                var task = renderTask;
                if (task != null)
                {
                    task.ContinueWith(
                        _ => toDispose.Dispose(),
                        CancellationToken.None,
                        TaskContinuationOptions.ExecuteSynchronously,
                        TaskScheduler.Default);
                }
                else
                {
                    toDispose.Dispose();
                }
            }

            cancellation = null;
            renderTask = null;
            renderActive = false;
            frame = Array.Empty<Color32>();
            lock (publishLock)
            {
                publishFrame = Array.Empty<Color32>();
                publishValid = false;
            }

            HasPublished = false;
            frameWidth = 0;
            frameHeight = 0;
        }

        internal void SetPassCursor(int index) => passCursor = index;
        internal void AddSamples(long count) => Interlocked.Add(ref samplesDone, count);

        /// <summary>
        /// Snapshot <paramref name="source"/> as the next image to upload, together with the view
        /// it depicts. The render worker calls this only from its sequential section between
        /// passes, so the buffer holds one coherent step size rather than a torn pass boundary.
        /// </summary>
        internal void PublishPass(Color32[] source, in ViewState view)
        {
            lock (publishLock)
            {
                if (publishFrame.Length != source.Length)
                {
                    publishFrame = new Color32[source.Length];
                }

                Array.Copy(source, publishFrame, source.Length);
                publishView = view;
                publishValid = true;
            }

            frameDirty = true;
        }

        private void StartQueued()
        {
            if (!hasQueued)
            {
                return;
            }

            activeRequest = queued;
            hasQueued = false;
            target = activeRequest.Target;
            if (target == null)
            {
                renderActive = false;
                Progress = 0f;
                return;
            }

            EnsureFrameBuffer(target.width, target.height);

            var minDim = Math.Min(frameWidth, frameHeight);
            var floor = 0;
            while (floor < StepPlan.Length - 1 && StepPlan[floor] > Math.Max(1, minDim / 4))
            {
                floor++;
            }

            // Only the double-double range is heavy enough to need a cap during a
            // gesture. Plain fp64 ("medium depth") renders every pass live.
            var capPasses = activeRequest.Interacting && activeRequest.ExtendedPrecision;
            var ceiling = capPasses
                ? Math.Min(StepPlan.Length, floor + InteractivePassCount)
                : StepPlan.Length;

            passFloorIndex = floor;
            passCeilingIndex = ceiling;
            passCursor = floor;
            samplesDone = 0;
            samplesTotal = 0;
            var visibleRect = ResolveVisibleRect(activeRequest.Viewport);
            var fullRect = new RectInt(0, 0, frameWidth, frameHeight);
            for (var p = floor; p < ceiling; p++)
            {
                var region = StepPlan[p] >= MarginStepThreshold ? fullRect : visibleRect;
                samplesTotal += CountNewSamples(region.width, region.height, StepPlan[p], p == floor);
            }

            Progress = 0f;
            lastUploadTime = 0d;

            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var job = new RenderJob(
                this, frame, frameWidth, frameHeight, visibleRect, activeRequest, workerCount, floor, ceiling);
            renderActive = true;
            renderTask = Task.Run(() => RenderProgressive(job, token), token);
        }

        private void DrainTask()
        {
            try
            {
                renderTask.GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                {
                    if (inner is OperationCanceledException)
                    {
                        continue;
                    }

                    Debug.LogError("CPU fractal render failed: " + inner);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError("CPU fractal render failed: " + exception);
            }
        }

        private bool UploadFrame()
        {
            if (target == null || target.width != frameWidth || target.height != frameHeight)
            {
                return false;
            }

            ViewState uploadedView;
            lock (publishLock)
            {
                if (!publishValid || publishFrame.Length != frameWidth * frameHeight)
                {
                    return false;
                }

                target.SetPixels32(publishFrame);
                uploadedView = publishView;
            }

            target.Apply(false, false);
            frameDirty = false;

            PublishedView = uploadedView;
            PublishedAspect = frameWidth / (double)Math.Max(1, frameHeight);
            HasPublished = true;

            var done = Interlocked.Read(ref samplesDone);
            Progress = samplesTotal > 0
                ? Mathf.Clamp01((float)(done / (double)samplesTotal))
                : (renderActive ? 0f : 1f);
            return true;
        }

        private void EnsureFrameBuffer(int width, int height)
        {
            var required = width * height;
            if (frameWidth == width && frameHeight == height && frame.Length == required)
            {
                return; // keep the previous image; passes overwrite it in place
            }

            frame = new Color32[required];
            for (var i = 0; i < frame.Length; i++)
            {
                frame[i] = InteriorColor;
            }

            lock (publishLock)
            {
                publishFrame = new Color32[required];
                publishValid = false;
            }

            HasPublished = false;
            frameWidth = width;
            frameHeight = height;
        }

        /// <summary>
        /// The part of the buffer the viewer actually sees, snapped outwards to the coarse sample
        /// grid so that restricting a pass to it keeps every sample on the same grid as a
        /// full-frame pass - otherwise the margin and the visible area would sample different
        /// points and show a seam between them.
        /// </summary>
        private RectInt ResolveVisibleRect(in Viewport viewport)
        {
            if (!viewport.HasOverscan || viewport.Width != frameWidth || viewport.Height != frameHeight)
            {
                return new RectInt(0, 0, frameWidth, frameHeight);
            }

            var rect = viewport.VisibleRect;
            var x0 = Mathf.Clamp(AlignDown(rect.xMin), 0, frameWidth);
            var y0 = Mathf.Clamp(AlignDown(rect.yMin), 0, frameHeight);
            var x1 = Mathf.Clamp(AlignUp(rect.xMax), x0, frameWidth);
            var y1 = Mathf.Clamp(AlignUp(rect.yMax), y0, frameHeight);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        private static int AlignDown(int value) => value - value % SampleAlign;

        private static int AlignUp(int value) => (value + SampleAlign - 1) / SampleAlign * SampleAlign;

        private static long CountNewSamples(int width, int height, int step, bool first)
        {
            var columns = (width + step - 1L) / step;
            var rows = (height + step - 1L) / step;
            var total = columns * rows;
            if (first)
            {
                return total;
            }

            var coarse = step << 1;
            var coarseColumns = (width + coarse - 1L) / coarse;
            var coarseRows = (height + coarse - 1L) / coarse;
            return total - coarseColumns * coarseRows;
        }

        /// <summary>
        /// Cuts <paramref name="region"/> into tiles ordered from its centre outward. Tiles, not
        /// bands: a fractal pixel costs anything from a few iterations to the whole budget, so
        /// equal-area bands finish at wildly different times and a pass ends when the slowest one
        /// does. Small tiles pulled from a shared cursor even that out.
        /// </summary>
        private static RectInt[] BuildTiles(RectInt region)
        {
            var width = Mathf.Max(1, region.width);
            var height = Mathf.Max(1, region.height);
            var columns = Mathf.Max(1, Mathf.CeilToInt(width / (float)TileSize));
            var rows = Mathf.Max(1, Mathf.CeilToInt(height / (float)TileSize));

            var tiles = new RectInt[columns * rows];
            var index = 0;
            for (var row = 0; row < rows; row++)
            {
                var y0 = region.yMin + row * TileSize;
                var y1 = Mathf.Min(region.yMax, y0 + TileSize);
                for (var column = 0; column < columns; column++)
                {
                    var x0 = region.xMin + column * TileSize;
                    var x1 = Mathf.Min(region.xMax, x0 + TileSize);
                    tiles[index++] = new RectInt(x0, y0, x1 - x0, y1 - y0);
                }
            }

            var midX = region.xMin + width * 0.5f;
            var midY = region.yMin + height * 0.5f;
            Array.Sort(tiles, (a, b) =>
                SquaredDistanceToCentre(a, midX, midY).CompareTo(SquaredDistanceToCentre(b, midX, midY)));
            return tiles;
        }

        private static float SquaredDistanceToCentre(in RectInt tile, float midX, float midY)
        {
            var dx = (tile.xMin + tile.xMax) * 0.5f - midX;
            var dy = (tile.yMin + tile.yMax) * 0.5f - midY;
            return dx * dx + dy * dy;
        }

        private static void RenderProgressive(RenderJob job, CancellationToken token)
        {
            // The definition calls back into the host with its own sampler struct; everything from
            // there down is compiled once per sampler type. See ICpuPassHost for why.
            var host = new PassHost(job, token);
            job.Definition.RunCpuPass(host, job.Parameters, job.ExtendedPrecision);
        }

        /// <summary>
        /// Bridges the fractal's sampler type into the generic pass machinery: one virtual call per
        /// render, and specialised code for every pixel after it.
        /// </summary>
        private sealed class PassHost : ICpuPassHost
        {
            private readonly RenderJob job;
            private readonly CancellationToken token;

            public PassHost(RenderJob job, CancellationToken token)
            {
                this.job = job;
                this.token = token;
            }

            public void Run<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerD
            {
                RunPasses(job, new PlaneSamplerD<TSampler>(sampler), token);
            }

            public void RunExtended<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerDD
            {
                RunPasses(job, new PlaneSamplerDD<TSampler>(sampler), token);
            }
        }

        /// <summary>Pixel to plane point to iteration count. Structs only - see IEscapeSamplerD.</summary>
        private interface IPlaneSampler
        {
            int SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token);
        }

        private readonly struct PlaneSamplerD<TSampler> : IPlaneSampler
            where TSampler : struct, IEscapeSamplerD
        {
            private readonly TSampler sampler;

            public PlaneSamplerD(TSampler sampler)
            {
                this.sampler = sampler;
            }

            public int SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token)
            {
                Normalize(job, pixelX, pixelY, out var rotatedX, out var rotatedY);
                var cx = job.CenterXDouble + job.ScaleDouble * rotatedX;
                var cy = job.CenterYDouble + job.ScaleDouble * rotatedY;
                return sampler.Sample(cx, cy, job.MaxIterations, token);
            }
        }

        private readonly struct PlaneSamplerDD<TSampler> : IPlaneSampler
            where TSampler : struct, IEscapeSamplerDD
        {
            private readonly TSampler sampler;

            public PlaneSamplerDD(TSampler sampler)
            {
                this.sampler = sampler;
            }

            public int SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token)
            {
                Normalize(job, pixelX, pixelY, out var rotatedX, out var rotatedY);
                var cx = DoubleDouble.Add(job.CenterX, DoubleDouble.Multiply(job.Scale, rotatedX));
                var cy = DoubleDouble.Add(job.CenterY, DoubleDouble.Multiply(job.Scale, rotatedY));
                return sampler.Sample(cx, cy, job.MaxIterations, token);
            }
        }

        /// <summary>
        /// Pixel centre into view space. This applies the same screen-space rotation the GPU shader
        /// does, so the two backends agree across the fp32 -> fp64 handoff.
        /// </summary>
        private static void Normalize(RenderJob job, int pixelX, int pixelY, out double rotatedX, out double rotatedY)
        {
            var normalizedX = ((pixelX + 0.5d) / job.Width - 0.5d) * job.Aspect;
            var normalizedY = (pixelY + 0.5d) / job.Height - 0.5d;
            rotatedX = normalizedX * job.RotationCos - normalizedY * job.RotationSin;
            rotatedY = normalizedX * job.RotationSin + normalizedY * job.RotationCos;
        }

        private static void RunPasses<TPlane>(RenderJob job, TPlane sampler, CancellationToken token)
            where TPlane : struct, IPlaneSampler
        {
            var fullTiles = BuildTiles(new RectInt(0, 0, job.Width, job.Height));
            var visibleTiles = job.HasMargin ? BuildTiles(job.VisibleRect) : fullTiles;

            for (var p = job.PassFloor; p < job.PassCeiling; p++)
            {
                token.ThrowIfCancellationRequested();
                job.Owner.SetPassCursor(p);

                var step = StepPlan[p];

                // Coarse passes cover the overscan margin as well; fine passes stay inside the
                // visible rectangle. See MarginStepThreshold.
                var tiles = step >= MarginStepThreshold ? fullTiles : visibleTiles;

                RenderPass(job, tiles, step, p == job.PassFloor, sampler, token);

                // Whole frame now covered at this step: publish it as one piece.
                // Marking dirty per tile instead would upload a frame that is part this
                // pass and part the coarser image beneath it, and that boundary is the
                // seam seen while panning - worst on the CPU-only deep-zoom path where a
                // gesture keeps restarting the render before it can finish a pass.
                token.ThrowIfCancellationRequested();
                job.Owner.PublishPass(job.Frame, job.View);
            }
        }

        private static void RenderPass<TPlane>(
            RenderJob job,
            RectInt[] tiles,
            int step,
            bool first,
            TPlane sampler,
            CancellationToken token)
            where TPlane : struct, IPlaneSampler
        {
            var coarse = step << 1;
            var cursor = new TileCursor();
            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = job.Workers
            };

            Parallel.For(0, job.Workers, options, _ =>
            {
                long produced = 0;

                while (true)
                {
                    var index = Interlocked.Increment(ref cursor.Next) - 1;
                    if (index >= tiles.Length)
                    {
                        break;
                    }

                    token.ThrowIfCancellationRequested();
                    var tile = tiles[index];

                    for (var by = tile.yMin; by < tile.yMax; by += step)
                    {
                        var rowOnCoarse = !first && by % coarse == 0;
                        for (var bx = tile.xMin; bx < tile.xMax; bx += step)
                        {
                            if (rowOnCoarse && bx % coarse == 0)
                            {
                                continue; // this sample was already computed in a coarser pass
                            }

                            var sx = bx + (step >> 1);
                            if (sx >= job.Width)
                            {
                                sx = job.Width - 1;
                            }

                            var sy = by + (step >> 1);
                            if (sy >= job.Height)
                            {
                                sy = job.Height - 1;
                            }

                            var iteration = sampler.SampleAt(job, sx, sy, token);
                            produced++;

                            if (!job.TrustInterior && iteration >= job.MaxIterations)
                            {
                                // A budget-capped "did not escape" is unknown, not proven
                                // interior. Keep whatever is already in the buffer (the
                                // coarser pass) instead of stamping it black.
                                continue;
                            }

                            var color = ResolveColor(iteration, job.MaxIterations, job.Palette);
                            FillBlock(job.Frame, job.Width, job.Height, bx, by, step, color);
                        }
                    }

                    if (produced >= 4096)
                    {
                        job.Owner.AddSamples(produced);
                        produced = 0;
                    }
                }

                job.Owner.AddSamples(produced);
            });
        }

        /// <summary>Shared "next tile please" counter. A class so the lambda can take it by ref.</summary>
        private sealed class TileCursor
        {
            public int Next;
        }

        private static void FillBlock(Color32[] buffer, int width, int height, int originX, int originY, int step, Color32 color)
        {
            var x1 = originX + step;
            if (x1 > width)
            {
                x1 = width;
            }

            var y1 = originY + step;
            if (y1 > height)
            {
                y1 = height;
            }

            for (var y = originY; y < y1; y++)
            {
                var row = y * width;
                for (var x = originX; x < x1; x++)
                {
                    buffer[row + x] = color;
                }
            }
        }

        private static Color32 ResolveColor(int iteration, int maxIterations, Color32[] colors)
        {
            if (iteration >= maxIterations)
            {
                return InteriorColor;
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

        private readonly struct FrameRequest
        {
            public FrameRequest(
                Texture2D target,
                Viewport viewport,
                IFractalDefinition definition,
                FractalParameterSet parameters,
                ViewState view,
                int iterations,
                bool extendedPrecision,
                bool interacting)
            {
                Target = target;
                Viewport = viewport;
                Definition = definition;
                Parameters = parameters;
                View = view;
                Iterations = iterations;
                ExtendedPrecision = extendedPrecision;
                Interacting = interacting;
            }

            public Texture2D Target { get; }
            public Viewport Viewport { get; }
            public IFractalDefinition Definition { get; }
            public FractalParameterSet Parameters { get; }
            public ViewState View { get; }
            public int Iterations { get; }
            public bool ExtendedPrecision { get; }
            public bool Interacting { get; }
        }

        private sealed class RenderJob
        {
            public RenderJob(
                FractalCpuRenderer owner,
                Color32[] frame,
                int width,
                int height,
                RectInt visibleRect,
                in FrameRequest request,
                int workers,
                int passFloor,
                int passCeiling)
            {
                Owner = owner;
                Frame = frame;
                Width = width;
                Height = height;
                Definition = request.Definition;
                Parameters = request.Parameters;
                View = request.View;
                VisibleRect = visibleRect;
                HasMargin = visibleRect.width < width || visibleRect.height < height;
                Aspect = width / (double)height;
                Palette = owner.palette;
                CenterX = DoubleDouble.FromDecimal(request.View.x.AsDecimal);
                CenterY = DoubleDouble.FromDecimal(request.View.y.AsDecimal);
                Scale = DoubleDouble.FromDecimal(request.View.scale.AsDecimal);
                CenterXDouble = CenterX.ToDouble();
                CenterYDouble = CenterY.ToDouble();
                ScaleDouble = Scale.ToDouble();
                RotationCos = Math.Cos(request.View.rotation);
                RotationSin = Math.Sin(request.View.rotation);
                MaxIterations = request.Iterations;
                ExtendedPrecision = request.ExtendedPrecision;
                // Distrust "interior" only where a gesture forces a capped budget
                // (deep double-double). Plain fp64 interaction paints real verdicts.
                TrustInterior = !(request.Interacting && request.ExtendedPrecision);
                Workers = workers;
                PassFloor = passFloor;
                PassCeiling = passCeiling;
            }

            public FractalCpuRenderer Owner { get; }

            /// <summary>Which fractal to evaluate. The renderer never looks inside it.</summary>
            public IFractalDefinition Definition { get; }

            public FractalParameterSet Parameters { get; }

            public Color32[] Frame { get; }
            public int Width { get; }
            public int Height { get; }
            public double Aspect { get; }
            public Color32[] Palette { get; }

            /// <summary>The view this render depicts; travels with the frame when it is published.</summary>
            public ViewState View { get; }

            public DoubleDouble CenterX { get; }
            public DoubleDouble CenterY { get; }
            public DoubleDouble Scale { get; }
            public double CenterXDouble { get; }
            public double CenterYDouble { get; }
            public double ScaleDouble { get; }
            public double RotationCos { get; }
            public double RotationSin { get; }
            public int MaxIterations { get; }
            public bool ExtendedPrecision { get; }
            public bool TrustInterior { get; }
            public int Workers { get; }
            public int PassFloor { get; }
            public int PassCeiling { get; }

            /// <summary>Part of the buffer the viewer sees; the rest is the overscan margin.</summary>
            public RectInt VisibleRect { get; }

            public bool HasMargin { get; }
        }
    }
}
