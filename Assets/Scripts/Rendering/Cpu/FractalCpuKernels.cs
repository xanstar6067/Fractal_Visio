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
    /// per 16x16 block, then 8x8, 4x4, 2x2 and finally every pixel. Work is split
    /// into horizontal bands ordered from the centre outward, so the middle of the
    /// screen sharpens first. A shared buffer is uploaded to the texture a few
    /// times per second while the render runs, then once more when it settles.
    ///
    /// NOTE (see CLAUDE.md "Rendering notes"): the kernel is deliberately plain
    /// managed <see cref="Parallel.ForEach"/> for now, matching the WPF prototype
    /// and avoiding new packages. The planned speed-up is Burst + Unity.Jobs with
    /// the per-pixel iteration moved into an IJobParallelFor.
    /// </summary>
    public sealed class FractalCpuRenderer : IDisposable
    {
        private const int PaletteResolution = 256;
        private const int InteractivePassCount = 2;      // steps 16, 8 while the view keeps moving
        private const double UploadIntervalSeconds = 0.04d;

        internal static readonly int[] StepPlan = { 16, 8, 4, 2, 1 };

        // Cell of the "reprojection had no source pixel here" grid. Matches the coarsest step,
        // so one flagged cell is exactly one first-pass sample block.
        private const int UncoveredCell = 16;

        // Passes at or above this step also cover the overscan margin; finer passes stay inside
        // the visible rectangle. The margin is only ever seen mid-gesture, where a coarse but
        // correct edge is enough, and refining it at every step would spend a quarter of the
        // render on pixels nobody is looking at.
        private const int MarginStepThreshold = 4;

        private readonly Color32[] palette;
        private readonly int workerCount;

        private Color32[] frame = Array.Empty<Color32>();
        private Color32[] reprojectScratch = Array.Empty<Color32>();

        // One flag per UncoveredCell block, set when reprojection found no source pixel for it.
        // Those blocks are the stretched edge: the first pass renders them before anything else,
        // so a smear is replaced by real pixels in milliseconds instead of seconds.
        private bool[] uncoveredBlocks = Array.Empty<bool>();
        private int blockGridWidth;
        private int blockGridHeight;

        // What the main thread actually uploads. The render worker copies `frame`
        // into it only between passes - i.e. when `frame` is a whole-image render
        // at one step size, never a half-updated mix of a pass and the coarser
        // image beneath it. That mix is what showed up as a hard horizontal seam
        // while panning. Guarded by `publishLock` so SetPixels32 never reads it
        // mid-copy.
        private Color32[] publishFrame = Array.Empty<Color32>();
        private readonly object publishLock = new object();

        private int frameWidth;
        private int frameHeight;

        // The view `frame` currently represents, so a new request can warp the old
        // pixels into place (pan / zoom / rotation) as an instant placeholder.
        private ViewState frameView;
        private bool frameViewValid;

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

        public bool IsBusy => renderActive || hasQueued;
        public float Progress { get; private set; }
        public bool UsesExtendedPrecision => activeRequest.ExtendedPrecision;
        public int PassCount => Mathf.Max(1, passCeilingIndex - passFloorIndex);
        public int CurrentPass => Mathf.Clamp(passCursor - passFloorIndex + 1, 1, PassCount);

        public void Request(
            Texture2D texture,
            in Viewport viewport,
            in ViewState view,
            int iterations,
            bool extendedPrecision,
            bool interacting)
        {
            queued = new FrameRequest(texture, viewport, view, Mathf.Max(1, iterations), extendedPrecision, interacting);
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
                    UploadFrame();
                    Progress = 1f;
                    uploaded = true;
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
                    UploadFrame();
                    uploaded = true;
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

        public void CompletePendingWork()
        {
            hasQueued = false;
            target = null;
            Progress = 0f;
            frameViewValid = false; // a texture rebuild follows; don't warp across it
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
            reprojectScratch = Array.Empty<Color32>();
            lock (publishLock)
            {
                publishFrame = Array.Empty<Color32>();
            }

            frameViewValid = false;
            frameWidth = 0;
            frameHeight = 0;
        }

        internal void SetPassCursor(int index) => passCursor = index;
        internal void AddSamples(long count) => Interlocked.Add(ref samplesDone, count);

        /// <summary>
        /// Snapshot <paramref name="source"/> as the next image to upload. The render
        /// worker calls this only from its sequential section between passes, so the
        /// buffer holds one coherent step size rather than a torn pass boundary.
        /// </summary>
        internal void PublishPass(Color32[] source)
        {
            lock (publishLock)
            {
                if (publishFrame.Length != source.Length)
                {
                    publishFrame = new Color32[source.Length];
                }

                Array.Copy(source, publishFrame, source.Length);
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

            // Warp the last image into the new view so the picture tracks the gesture
            // instead of freezing while the fresh passes catch up.
            if (frameViewValid)
            {
                ReprojectFrame(frameView, activeRequest.View);
            }
            else
            {
                MarkEverythingUncovered();
            }

            frameView = activeRequest.View;
            frameViewValid = true;

            // Make the warped placeholder uploadable right away; passes replace it
            // one whole step at a time from here.
            PublishPass(frame);

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
            frameDirty = true;              // push the placeholder / previous image right away
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

        private void UploadFrame()
        {
            if (target == null || target.width != frameWidth || target.height != frameHeight)
            {
                return;
            }

            lock (publishLock)
            {
                if (publishFrame.Length != frameWidth * frameHeight)
                {
                    return;
                }

                target.SetPixels32(publishFrame);
            }

            target.Apply(false, false);
            frameDirty = false;

            var done = Interlocked.Read(ref samplesDone);
            Progress = samplesTotal > 0
                ? Mathf.Clamp01((float)(done / (double)samplesTotal))
                : (renderActive ? 0f : 1f);
        }

        private void EnsureFrameBuffer(int width, int height)
        {
            var required = width * height;
            if (frameWidth == width && frameHeight == height && frame.Length == required)
            {
                return; // keep the previous image as a placeholder under the new render
            }

            var previous = frame;
            var previousWidth = frameWidth;
            var previousHeight = frameHeight;
            frame = new Color32[required];
            lock (publishLock)
            {
                publishFrame = new Color32[required];
            }

            if (previous.Length == previousWidth * previousHeight && previousWidth > 0 && previousHeight > 0)
            {
                ResampleNearest(previous, previousWidth, previousHeight, frame, width, height);
            }
            else
            {
                var fill = new Color32(3, 5, 12, 255);
                for (var i = 0; i < frame.Length; i++)
                {
                    frame[i] = fill;
                }
            }

            frameWidth = width;
            frameHeight = height;
            blockGridWidth = (width + UncoveredCell - 1) / UncoveredCell;
            blockGridHeight = (height + UncoveredCell - 1) / UncoveredCell;
            uncoveredBlocks = new bool[Math.Max(1, blockGridWidth * blockGridHeight)];
        }

        private void MarkEverythingUncovered()
        {
            Array.Fill(uncoveredBlocks, true);
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

        private static int AlignDown(int value) => value - value % UncoveredCell;

        private static int AlignUp(int value) => (value + UncoveredCell - 1) / UncoveredCell * UncoveredCell;

        private static void ResampleNearest(Color32[] src, int srcWidth, int srcHeight, Color32[] dst, int dstWidth, int dstHeight)
        {
            for (var y = 0; y < dstHeight; y++)
            {
                var sy = (int)((long)y * srcHeight / dstHeight);
                if (sy >= srcHeight)
                {
                    sy = srcHeight - 1;
                }

                var dstRow = y * dstWidth;
                var srcRow = sy * srcWidth;
                for (var x = 0; x < dstWidth; x++)
                {
                    var sx = (int)((long)x * srcWidth / dstWidth);
                    if (sx >= srcWidth)
                    {
                        sx = srcWidth - 1;
                    }

                    dst[dstRow + x] = src[srcRow + sx];
                }
            }
        }

        /// <summary>
        /// Resamples <see cref="frame"/> (currently showing view <paramref name="from"/>)
        /// so it shows view <paramref name="to"/> instead: one similarity warp covering
        /// pan, zoom and rotation. Newly exposed pixels take the clamped edge colour.
        /// </summary>
        private void ReprojectFrame(in ViewState from, in ViewState to)
        {
            Array.Clear(uncoveredBlocks, 0, uncoveredBlocks.Length);

            var count = frameWidth * frameHeight;
            if (count <= 0 || frame.Length != count)
            {
                MarkEverythingUncovered();
                return;
            }

            var scaleFrom = from.scale.AsDouble;
            var scaleTo = to.scale.AsDouble;
            if (!(scaleFrom > 0d) || !(scaleTo > 0d) || double.IsNaN(scaleFrom) || double.IsNaN(scaleTo))
            {
                MarkEverythingUncovered();
                return;
            }

            if (from.x.Equals(to.x) && from.y.Equals(to.y) &&
                scaleFrom == scaleTo && from.rotation == to.rotation)
            {
                return; // nothing moved: every pixel still stands for its own place
            }

            // dst pixel (view 'to') -> D_to -> fractal point -> D_from -> src pixel.
            //   D_from = b + M * D_to
            //   M = (scaleTo / scaleFrom) * Rot(theta_to - theta_from)
            //   b = Rot(-theta_from) * ((C_to - C_from) / scaleFrom)
            var ratio = scaleTo / scaleFrom;
            var deltaTheta = to.rotation - from.rotation;
            var mCos = Math.Cos(deltaTheta) * ratio;
            var mSin = Math.Sin(deltaTheta) * ratio;

            var gx = (double)((to.x.AsDecimal - from.x.AsDecimal) / from.scale.AsDecimal);
            var gy = (double)((to.y.AsDecimal - from.y.AsDecimal) / from.scale.AsDecimal);
            var fCos = Math.Cos(-from.rotation);
            var fSin = Math.Sin(-from.rotation);
            var bx = gx * fCos - gy * fSin;
            var by = gx * fSin + gy * fCos;

            var w = frameWidth;
            var h = frameHeight;
            var aspect = w / (double)h;
            var invAspect = 1d / aspect;

            if (reprojectScratch.Length != count)
            {
                reprojectScratch = new Color32[count];
            }

            var src = frame;
            var dst = reprojectScratch;
            var blocks = uncoveredBlocks;
            var gridWidth = blockGridWidth;
            var options = new ParallelOptions { MaxDegreeOfParallelism = workerCount };

            Parallel.For(0, h, options, py =>
            {
                var normalizedYTo = ((py + 0.5d) / h) - 0.5d;
                var rowBase = py * w;
                for (var px = 0; px < w; px++)
                {
                    var normalizedXTo = (((px + 0.5d) / w) - 0.5d) * aspect;

                    var dFromX = bx + (normalizedXTo * mCos - normalizedYTo * mSin);
                    var dFromY = by + (normalizedXTo * mSin + normalizedYTo * mCos);

                    var srcXf = (dFromX * invAspect + 0.5d) * w - 0.5d;
                    var srcYf = (dFromY + 0.5d) * h - 0.5d;

                    var sx = (int)Math.Round(srcXf);
                    var sy = (int)Math.Round(srcYf);
                    if (sx < 0 || sx >= w || sy < 0 || sy >= h)
                    {
                        // No source pixel for this one: flag its block so the first pass renders
                        // it before anything else. The clamped colour below is only a stand-in
                        // for the few milliseconds until that happens - it is the smear that
                        // used to sit at the edge for the whole render.
                        blocks[py / UncoveredCell * gridWidth + px / UncoveredCell] = true;
                        if (sx < 0) sx = 0; else if (sx >= w) sx = w - 1;
                        if (sy < 0) sy = 0; else if (sy >= h) sy = h - 1;
                    }

                    dst[rowBase + px] = src[sy * w + sx];
                }
            });

            reprojectScratch = src;
            frame = dst;
        }

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

        private static RectInt[] BuildBands(RectInt region, int desiredCount, int align)
        {
            // Band boundaries must land on the coarsest sample grid, otherwise the
            // per-pass "already computed" skip in RenderProgressive drifts out of phase.
            var safeAlign = Mathf.Max(1, align);
            var height = Mathf.Max(1, region.height);
            var count = Mathf.Clamp(desiredCount, 1, height);
            var raw = Mathf.Max(1, Mathf.CeilToInt(height / (float)count));
            var bandHeight = ((raw + safeAlign - 1) / safeAlign) * safeAlign;
            var actual = Mathf.CeilToInt(height / (float)bandHeight);
            var bands = new RectInt[actual];
            for (var i = 0; i < actual; i++)
            {
                var y0 = region.yMin + i * bandHeight;
                var y1 = Mathf.Min(region.yMax, y0 + bandHeight);
                bands[i] = new RectInt(region.xMin, y0, region.width, y1 - y0);
            }

            var mid = region.yMin + height * 0.5f;
            Array.Sort(bands, (a, b) =>
            {
                var da = Mathf.Abs((a.yMin + a.yMax) * 0.5f - mid);
                var db = Mathf.Abs((b.yMin + b.yMax) * 0.5f - mid);
                return da.CompareTo(db);
            });
            return bands;
        }

        private static void RenderProgressive(RenderJob job, CancellationToken token)
        {
            var fullBands = BuildBands(new RectInt(0, 0, job.Width, job.Height), job.Workers * 3, StepPlan[0]);
            var visibleBands = job.HasMargin
                ? BuildBands(job.VisibleRect, job.Workers * 3, StepPlan[0])
                : fullBands;

            var options = new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = job.Workers
            };

            for (var p = job.PassFloor; p < job.PassCeiling; p++)
            {
                token.ThrowIfCancellationRequested();
                job.Owner.SetPassCursor(p);

                var step = StepPlan[p];
                var first = p == job.PassFloor;

                // Coarse passes cover the overscan margin as well; fine passes stay inside the
                // visible rectangle. See MarginStepThreshold.
                var bands = step >= MarginStepThreshold ? fullBands : visibleBands;

                if (first)
                {
                    // Blocks the reprojection could not fill are the stretched edge. Rendering
                    // them before everything else turns that smear into real pixels almost at
                    // once, instead of after a whole sweep over the image.
                    RenderPass(job, bands, options, step, true, PassFilter.UncoveredOnly, token);
                    token.ThrowIfCancellationRequested();
                    job.Owner.PublishPass(job.Frame);
                    RenderPass(job, bands, options, step, true, PassFilter.CoveredOnly, token);
                }
                else
                {
                    RenderPass(job, bands, options, step, false, PassFilter.All, token);
                }

                // Whole frame now covered at this step: publish it as one piece.
                // Marking dirty per band instead uploaded a frame that was part
                // this pass and part the coarser image / reprojected placeholder,
                // and that boundary is the horizontal seam seen while panning -
                // worst on the CPU-only deep-zoom path where a gesture keeps
                // restarting the render before it can finish a pass.
                token.ThrowIfCancellationRequested();
                job.Owner.PublishPass(job.Frame);
            }
        }

        private enum PassFilter
        {
            All,
            UncoveredOnly,
            CoveredOnly
        }

        private static void RenderPass(
            RenderJob job,
            RectInt[] bands,
            ParallelOptions options,
            int step,
            bool first,
            PassFilter filter,
            CancellationToken token)
        {
            var coarse = step << 1;

            Parallel.ForEach(bands, options, band =>
            {
                long produced = 0;
                for (var by = band.yMin; by < band.yMax; by += step)
                {
                    if ((by & 31) == 0)
                    {
                        token.ThrowIfCancellationRequested();
                    }

                    var rowOnCoarse = !first && (by % coarse == 0);
                    for (var bx = band.xMin; bx < band.xMax; bx += step)
                    {
                        if (rowOnCoarse && (bx % coarse == 0))
                        {
                            continue; // this sample was already computed in a coarser pass
                        }

                        if (filter != PassFilter.All &&
                            job.IsBlockUncovered(bx, by) != (filter == PassFilter.UncoveredOnly))
                        {
                            continue;
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

                        var iteration = ComputeIteration(job, sx, sy, token);
                        produced++;

                        if (!job.TrustInterior && iteration >= job.MaxIterations)
                        {
                            // A budget-capped "did not escape" is unknown, not proven
                            // interior. Keep whatever is already on screen (previous
                            // frame / coarser pass) instead of stamping it black.
                            continue;
                        }

                        var color = ResolveColor(iteration, job.MaxIterations, job.Palette);
                        FillBlock(job.Frame, job.Width, job.Height, bx, by, step, color);
                    }
                }

                job.Owner.AddSamples(produced);
            });
        }

        private static int ComputeIteration(RenderJob job, int pixelX, int pixelY, CancellationToken token)
        {
            var normalizedX = (((pixelX + 0.5d) / job.Width) - 0.5d) * job.Aspect;
            var normalizedY = ((pixelY + 0.5d) / job.Height) - 0.5d;

            // Same screen-space rotation the GPU shader applies, so the two backends
            // agree across the fp32 -> fp64 handoff.
            var rotatedX = normalizedX * job.RotationCos - normalizedY * job.RotationSin;
            var rotatedY = normalizedX * job.RotationSin + normalizedY * job.RotationCos;

            if (job.ExtendedPrecision)
            {
                var cx = DoubleDouble.Add(job.CenterX, DoubleDouble.Multiply(job.Scale, rotatedX));
                var cy = DoubleDouble.Add(job.CenterY, DoubleDouble.Multiply(job.Scale, rotatedY));
                return EvaluateExtended(cx, cy, job.MaxIterations, token);
            }

            var doubleCx = job.CenterXDouble + job.ScaleDouble * rotatedX;
            var doubleCy = job.CenterYDouble + job.ScaleDouble * rotatedY;
            return EvaluateDouble(doubleCx, doubleCy, job.MaxIterations, token);
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

        private readonly struct FrameRequest
        {
            public FrameRequest(Texture2D target, Viewport viewport, ViewState view, int iterations, bool extendedPrecision, bool interacting)
            {
                Target = target;
                Viewport = viewport;
                View = view;
                Iterations = iterations;
                ExtendedPrecision = extendedPrecision;
                Interacting = interacting;
            }

            public Texture2D Target { get; }
            public Viewport Viewport { get; }
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
                VisibleRect = visibleRect;
                HasMargin = visibleRect.width < width || visibleRect.height < height;
                Uncovered = owner.uncoveredBlocks;
                BlockGridWidth = owner.blockGridWidth;
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
            public Color32[] Frame { get; }
            public int Width { get; }
            public int Height { get; }
            public double Aspect { get; }
            public Color32[] Palette { get; }
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

            /// <summary>Blocks the reprojection could not fill. See FractalCpuRenderer.uncoveredBlocks.</summary>
            public bool[] Uncovered { get; }

            public int BlockGridWidth { get; }

            public bool IsBlockUncovered(int x, int y)
            {
                if (BlockGridWidth <= 0 || Uncovered.Length == 0)
                {
                    return true;
                }

                var index = y / UncoveredCell * BlockGridWidth + x / UncoveredCell;
                return (uint)index < (uint)Uncovered.Length && Uncovered[index];
            }
        }
    }
}
