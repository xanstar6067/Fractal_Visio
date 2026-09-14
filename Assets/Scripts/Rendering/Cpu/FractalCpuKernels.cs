using System;
using System.Diagnostics;
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
    /// What it accumulates is <b>escape values, not colours</b>. Colour is applied once per
    /// published pass through an <see cref="IColorMapper"/>, which is what makes a palette or
    /// colouring change a remap of the existing buffer - milliseconds - instead of a re-render,
    /// which at depth is seconds.
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

        /// <summary>How long a worker parked by <see cref="CpuWorkerBudget"/> waits before looking again.</summary>
        private const int ParkSleepMilliseconds = 4;

        /// <summary>
        /// A pass with fewer samples than this is too short to time: the coarse passes are a few
        /// thousand samples, and waking the workers is most of their wall time. Timed, they inflated
        /// the per-sample estimate and the time budget cut the next run after its first pass.
        /// </summary>
        private const long MinimumTimedSamples = 16384;

        /// <summary>
        /// Measured wall-clock cost of one sample in the most recent passes, 0 until the first
        /// timed pass. Written by the render worker, read when the next request is planned.
        /// </summary>
        private double secondsPerSample;

        // Passes at or above this step also cover the overscan margin; finer passes stay inside
        // the visible rectangle. The margin is only ever seen mid-gesture, where a coarse but
        // correct edge is enough, and refining it at every step would spend a quarter of the
        // render on pixels nobody is looking at.
        private const int MarginStepThreshold = 4;

        private readonly IColorMapper mapper;
        private readonly CpuWorkerBudget budget;
        private readonly bool background;
        private readonly int[] workerRanks;
        private readonly int workerCount;

        /// <summary>Reference orbit for perturbation renders, reused between requests.</summary>
        private readonly ReferenceOrbit referenceOrbit = new();

        /// <summary>Escape values, one per pixel. Negative means the point never escaped.</summary>
        private float[] escape = Array.Empty<float>();

        /// <summary>Colours for the escape buffer, produced at publish time.</summary>
        private Color32[] mapScratch = Array.Empty<Color32>();

        // Scratch for smoothing a coarse pass (see MapInterpolated), reused between publishes.
        private float[] blockEscape = Array.Empty<float>();
        private Color32[] blockColors = Array.Empty<Color32>();
        private int[] columnLower = Array.Empty<int>();
        private int[] columnUpper = Array.Empty<int>();
        private int[] columnWeight = Array.Empty<int>();

        // What the main thread actually uploads. The render worker fills it only between passes -
        // i.e. when the escape buffer is a whole-image render at one step size, never a
        // half-updated mix of a pass and the coarser image beneath it. Guarded by `publishLock`
        // so SetPixels32 never reads it mid-copy. The view that snapshot stands for travels with it.
        private Color32[] publishFrame = Array.Empty<Color32>();
        private ViewState publishView;
        private int publishStep = StepPlan[0];
        private bool publishValid;
        private readonly object publishLock = new object();

        /// <summary>
        /// Palette and colouring in force. Volatile because the render worker reads it when it maps
        /// a pass and the main thread replaces it when the user picks a palette - a pass published
        /// after the change should already wear the new colours.
        /// </summary>
        private volatile ColorState colorState = new(PaletteLibrary.Default, ColoringSettings.Default);

        private int frameWidth;
        private int frameHeight;

        // Set by the render worker at the first publish, read by the main thread when the palette
        // changes: volatile so a remap right after the first pass is not decided on a stale copy.
        private volatile bool hasEscapeData;

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

        /// <param name="workerBudget">
        /// Thread budget shared with every other CPU renderer. See <see cref="CpuWorkerBudget"/>.
        /// </param>
        /// <param name="isBackground">
        /// True for the wide background layer: it takes the budget's small background share, so it
        /// cannot starve the renderer whose output the viewer is actually looking at.
        /// </param>
        public FractalCpuRenderer(IColorMapper colorMapper, CpuWorkerBudget workerBudget, bool isBackground)
        {
            mapper = colorMapper ?? throw new ArgumentNullException(nameof(colorMapper));
            budget = workerBudget ?? throw new ArgumentNullException(nameof(workerBudget));
            background = isBackground;
            workerRanks = budget.RanksFor(isBackground);
            workerCount = workerRanks.Length;
        }

        public bool IsBusy => renderActive || hasQueued;
        public float Progress { get; private set; }
        public bool UsesExtendedPrecision => activeRequest.ExtendedPrecision;

        /// <summary>Arithmetic the current or last render actually ran in. Set by the fractal's choice of sampler.</summary>
        public PrecisionTier ActivePrecision => (PrecisionTier)activePrecision;

        private volatile int activePrecision;
        public int PassCount => Mathf.Max(1, passCeilingIndex - passFloorIndex);
        public int CurrentPass => Mathf.Clamp(passCursor - passFloorIndex + 1, 1, PassCount);

        /// <summary>True once the target texture holds a whole-image render of a known view.</summary>
        public bool HasPublished { get; private set; }

        /// <summary>The view the target texture shows. Only meaningful with <see cref="HasPublished"/>.</summary>
        public ViewState PublishedView { get; private set; }

        /// <summary>Aspect of the published buffer, margins included. Feeds <see cref="FramePlacement"/>.</summary>
        public double PublishedAspect { get; private set; } = 1d;

        /// <summary>
        /// Change palette or colouring. Recolours the existing escape buffer when the renderer is
        /// idle; while a render is running the next published pass picks the change up on its own.
        /// Either way the fractal is not recomputed.
        /// </summary>
        public void SetColoring(PaletteData palette, in ColoringSettings settings)
        {
            colorState = new ColorState(palette ?? PaletteLibrary.Default, settings);

            if (renderActive || !hasEscapeData || target == null)
            {
                return;
            }

            MapAndStage(escape, publishView, publishStep);
        }

        /// <summary>Progressive step of the published frame: 16 for the coarsest pass, 1 for full detail.</summary>
        public int PublishedStep { get; private set; } = StepPlan[0];

        /// <summary>
        /// Raised on the main thread just before the texture is overwritten with a newer frame, with
        /// the view and step of the incoming one. The last moment the outgoing picture can still be
        /// copied - which is how a sharper frame is kept on screen over a coarser successor.
        /// </summary>
        public event Action<ViewState, int> FrameReplacing;

        /// <param name="timeBudgetSeconds">
        /// Stop after the passes that fit in this much time, estimated from how fast the previous
        /// passes went; 0 renders every pass. A moving view wants a fresh coarse frame soon rather
        /// than a sharp one of a place it has already left - the reference app aims for ~250 ms.
        /// </param>
        public void Request(
            Texture2D texture,
            in Viewport viewport,
            IFractalDefinition definition,
            in FractalParameterSet parameters,
            in ViewState view,
            int iterations,
            bool extendedPrecision,
            double timeBudgetSeconds = 0d)
        {
            queued = new FrameRequest(
                texture, viewport, definition, parameters, view, Mathf.Max(1, iterations),
                extendedPrecision, timeBudgetSeconds);
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
                    Progress = 1f;
                }
            }

            if (!renderActive && hasQueued)
            {
                StartQueued();
            }

            if (frameDirty)
            {
                var now = Time.realtimeSinceStartupAsDouble;
                // Mid-render the upload rate is capped so SetPixels32 does not eat the frame; an
                // idle renderer has nothing to protect, so a remap shows up immediately.
                if (!renderActive || now - lastUploadTime >= UploadIntervalSeconds)
                {
                    lastUploadTime = now;
                    uploaded = UploadFrame();
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
        /// placeholder - call it when the fractal or its parameters changed, because the frame is
        /// then a picture of something else entirely rather than of another view. A palette change
        /// is not one of these: that is <see cref="SetColoring"/>.
        /// </summary>
        public void DiscardPublished()
        {
            HasPublished = false;
            hasEscapeData = false;
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
            escape = Array.Empty<float>();
            mapScratch = Array.Empty<Color32>();
            lock (publishLock)
            {
                publishFrame = Array.Empty<Color32>();
                publishValid = false;
            }

            HasPublished = false;
            hasEscapeData = false;
            frameWidth = 0;
            frameHeight = 0;
        }

        internal void SetPassCursor(int index) => passCursor = index;
        internal void AddSamples(long count) => Interlocked.Add(ref samplesDone, count);

        /// <summary>
        /// Colour <paramref name="source"/> and stage it as the next image to upload, together with
        /// the view it depicts. The render worker calls this only from its sequential section
        /// between passes, so the buffer holds one coherent step size rather than a torn pass
        /// boundary.
        /// </summary>
        internal void PublishPass(float[] source, in ViewState view, int step)
        {
            hasEscapeData = true;
            MapAndStage(source, view, step);
        }

        /// <summary>
        /// Fold one pass's measured cost into the per-sample estimate the time budget is planned
        /// with. Weighted towards the newest pass: the cost follows the view, and the view moves.
        /// </summary>
        internal void RecordThroughput(double seconds, long samples)
        {
            if (samples < MinimumTimedSamples || !(seconds > 0d))
            {
                return;
            }

            var measured = seconds / samples;
            var previous = Volatile.Read(ref secondsPerSample);
            Volatile.Write(ref secondsPerSample, previous > 0d ? previous + (measured - previous) * 0.5d : measured);
        }

        /// <summary>
        /// Colour a coarse pass as a smooth picture rather than as blocks: map one colour per sample
        /// and blend between neighbouring samples bilinearly. A 16x16 pass shown as hard squares
        /// reads as a broken picture; the same samples filtered read as an out-of-focus one, which
        /// is what the reference app shows while it moves - it renders coarse phases small and lets
        /// the canvas filter them up. Only the colours are smoothed: the escape buffer keeps its
        /// blocks, because later passes and remaps read it.
        /// </summary>
        private void MapInterpolated(float[] source, Color32[] target, int step, ColorState state)
        {
            var width = frameWidth;
            var height = frameHeight;
            var columns = (width + step - 1) / step;
            var rows = (height + step - 1) / step;
            var samples = columns * rows;

            if (blockEscape.Length < samples)
            {
                blockEscape = new float[samples];
                blockColors = new Color32[samples];
            }

            if (columnLower.Length < width)
            {
                columnLower = new int[width];
                columnUpper = new int[width];
                columnWeight = new int[width];
            }

            for (var row = 0; row < rows; row++)
            {
                var sourceRow = row * step * width;
                var blockRow = row * columns;
                for (var column = 0; column < columns; column++)
                {
                    blockEscape[blockRow + column] = source[sourceRow + column * step];
                }
            }

            mapper.MapRange(blockEscape, blockColors, 0, samples, state.Palette, state.Settings);

            // Samples stay at pixel (k * step)'s centre at every refinement level.
            // Interpolate that fixed lattice; centring them in their blocks would shift
            // the picture by half a block whenever the pass changes.
            // Weights are fixed-point out of 256 so the inner loop stays integer.
            for (var x = 0; x < width; x++)
            {
                var position = x / (float)step;
                var lower = Mathf.Clamp(Mathf.FloorToInt(position), 0, columns - 1);
                columnLower[x] = lower;
                columnUpper[x] = Math.Min(lower + 1, columns - 1);
                columnWeight[x] = Mathf.Clamp(Mathf.RoundToInt((position - lower) * 256f), 0, 256);
            }

            var localBlocks = blockColors;
            var lowerColumns = columnLower;
            var upperColumns = columnUpper;
            var weights = columnWeight;
            var rowsPerChunk = Math.Max(16, height / Math.Max(1, workerCount * 4));
            var chunks = (height + rowsPerChunk - 1) / rowsPerChunk;

            Parallel.For(
                0,
                chunks,
                new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                chunk =>
                {
                    var endRow = Math.Min(height, (chunk + 1) * rowsPerChunk);
                    for (var y = chunk * rowsPerChunk; y < endRow; y++)
                    {
                        var position = y / (float)step;
                        var lower = Mathf.Clamp(Mathf.FloorToInt(position), 0, rows - 1);
                        var upper = Math.Min(lower + 1, rows - 1);
                        var wy = Mathf.Clamp(Mathf.RoundToInt((position - lower) * 256f), 0, 256);
                        var top = lower * columns;
                        var bottom = upper * columns;
                        var pixelRow = y * width;

                        for (var x = 0; x < width; x++)
                        {
                            var wx = weights[x];
                            var c00 = localBlocks[top + lowerColumns[x]];
                            var c10 = localBlocks[top + upperColumns[x]];
                            var c01 = localBlocks[bottom + lowerColumns[x]];
                            var c11 = localBlocks[bottom + upperColumns[x]];

                            var r0 = c00.r * (256 - wx) + c10.r * wx;
                            var g0 = c00.g * (256 - wx) + c10.g * wx;
                            var b0 = c00.b * (256 - wx) + c10.b * wx;
                            var r1 = c01.r * (256 - wx) + c11.r * wx;
                            var g1 = c01.g * (256 - wx) + c11.g * wx;
                            var b1 = c01.b * (256 - wx) + c11.b * wx;

                            target[pixelRow + x] = new Color32(
                                (byte)((r0 * (256 - wy) + r1 * wy + 32768) >> 16),
                                (byte)((g0 * (256 - wy) + g1 * wy + 32768) >> 16),
                                (byte)((b0 * (256 - wy) + b1 * wy + 32768) >> 16),
                                255);
                        }
                    }
                });
        }

        private void MapAndStage(float[] source, in ViewState view, int step)
        {
            var length = source.Length;
            if (length <= 0)
            {
                return;
            }

            if (mapScratch.Length != length)
            {
                mapScratch = new Color32[length];
            }

            var state = colorState;
            var localTarget = mapScratch;

            if (step > 1 && frameWidth * frameHeight == length)
            {
                MapInterpolated(source, localTarget, step, state);
            }
            else
            {
                var chunk = Math.Max(4096, length / Math.Max(1, workerCount * 4));
                var chunks = (length + chunk - 1) / chunk;
                var localMapper = mapper;
                var localSource = source;
                var settings = state.Settings;
                var palette = state.Palette;

                if (chunks <= 1)
                {
                    localMapper.MapRange(localSource, localTarget, 0, length, palette, settings);
                }
                else
                {
                    Parallel.For(
                        0,
                        chunks,
                        new ParallelOptions { MaxDegreeOfParallelism = workerCount },
                        index => localMapper.MapRange(
                            localSource, localTarget, index * chunk, chunk, palette, settings));
                }
            }

            lock (publishLock)
            {
                if (publishFrame.Length != length)
                {
                    publishFrame = new Color32[length];
                }

                Array.Copy(localTarget, publishFrame, length);
                publishView = view;
                publishStep = step;
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

            var visibleRect = ResolveVisibleRect(activeRequest.Viewport);
            var fullRect = new RectInt(0, 0, frameWidth, frameHeight);

            // The time budget decides how fine this run goes, the same way at every depth: passes
            // are added while their estimated cost still fits. The first pass always runs - a run
            // that publishes nothing would leave a moving view with no new picture at all.
            var ceiling = StepPlan.Length;
            var perSample = Volatile.Read(ref secondsPerSample);
            var estimated = 0d;
            samplesTotal = 0;
            for (var p = floor; p < StepPlan.Length; p++)
            {
                var region = StepPlan[p] >= MarginStepThreshold ? fullRect : visibleRect;
                var samples = CountNewSamples(region.width, region.height, StepPlan[p], p == floor);
                estimated += samples * perSample;
                if (p > floor && activeRequest.TimeBudgetSeconds > 0d && perSample > 0d &&
                    estimated > activeRequest.TimeBudgetSeconds)
                {
                    ceiling = p;
                    break;
                }

                samplesTotal += samples;
            }

            passFloorIndex = floor;
            passCeilingIndex = ceiling;
            passCursor = floor;
            samplesDone = 0;

            Progress = 0f;
            lastUploadTime = 0d;

            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var job = new RenderJob(
                this, escape, frameWidth, frameHeight, visibleRect, activeRequest,
                budget, workerRanks, referenceOrbit, floor, ceiling);
            renderActive = true;
            renderTask = Task.Run(() => RenderProgressive(job, token));
        }

        /// <summary>
        /// Report a failed render. A cancelled one is not a failure and, by design, arrives here as
        /// an ordinary completion: cancellation unwinds by returning, never by throwing, because a
        /// gesture cancels renders several times a second and each throw would be paid on every
        /// worker and again on the main thread.
        /// </summary>
        private void DrainTask()
        {
            if (!renderTask.IsFaulted || renderTask.Exception == null)
            {
                return;
            }

            foreach (var inner in renderTask.Exception.Flatten().InnerExceptions)
            {
                if (inner is OperationCanceledException)
                {
                    continue;
                }

                UnityEngine.Debug.LogError("CPU fractal render failed: " + inner);
            }
        }

        private bool UploadFrame()
        {
            if (target == null || target.width != frameWidth || target.height != frameHeight)
            {
                return false;
            }

            ViewState uploadedView;
            int uploadedStep;
            lock (publishLock)
            {
                if (!publishValid || publishFrame.Length != frameWidth * frameHeight)
                {
                    return false;
                }

                uploadedView = publishView;
                uploadedStep = publishStep;
            }

            // Outside the lock: a handler copies the texture on the GPU, and the worker must not wait
            // on that to stage its next pass.
            if (HasPublished)
            {
                FrameReplacing?.Invoke(uploadedView, uploadedStep);
            }

            lock (publishLock)
            {
                // The worker may have staged a newer pass in between; upload whatever is newest and
                // report the view that belongs to it.
                target.SetPixels32(publishFrame);
                uploadedView = publishView;
                uploadedStep = publishStep;
            }

            target.Apply(false, false);
            frameDirty = false;

            PublishedView = uploadedView;
            PublishedStep = uploadedStep;
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
            if (frameWidth == width && frameHeight == height && escape.Length == required)
            {
                return; // keep the previous image; passes overwrite it in place
            }

            escape = new float[required];
            for (var i = 0; i < escape.Length; i++)
            {
                escape[i] = EscapeMath.Interior;
            }

            mapScratch = new Color32[required];
            lock (publishLock)
            {
                publishFrame = new Color32[required];
                publishValid = false;
            }

            HasPublished = false;
            hasEscapeData = false;
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
            if (job.Owner.background)
            {
                job.Budget.BeginBackground();
            }

            try
            {
                // The definition calls back into the host with its own sampler struct; everything
                // from there down is compiled once per sampler type. See ICpuPassHost for why.
                var host = new PassHost(job, token);
                job.Definition.RunCpuPass(host, job.Parameters, job.ExtendedPrecision);
            }
            finally
            {
                if (job.Owner.background)
                {
                    job.Budget.EndBackground();
                }
            }
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
                job.Owner.activePrecision = (int)PrecisionTier.Double;
                RunPasses(job, new PlaneSamplerD<TSampler>(sampler), token);
            }

            public void RunExtended<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerDD
            {
                job.Owner.activePrecision = (int)PrecisionTier.DoubleDouble;
                RunPasses(job, new PlaneSamplerDD<TSampler>(sampler), token);
            }

            public void RunPerturbed<TSampler>(TSampler sampler) where TSampler : struct, IPerturbationSampler
            {
                job.Owner.activePrecision = (int)PrecisionTier.Perturbation;

                // The reference is the centre of the request. Every buffer pixel lies within half
                // the buffer's diagonal of it, which is the bound the BLA radii are built against.
                var orbit = job.Orbit;
                var maxDeltaC = job.ScaleDouble * 0.5d * Math.Sqrt(job.Aspect * job.Aspect + 1d) * 1.01d;
                sampler.BuildReference(orbit, job.CenterX, job.CenterY, job.MaxIterations, maxDeltaC);
                if (token.IsCancellationRequested)
                {
                    return;
                }

                RunPasses(job, new PlaneSamplerPerturbed<TSampler>(sampler, orbit), token);
            }
        }

        /// <summary>Pixel to plane point to escape value. Structs only - see IEscapeSamplerD.</summary>
        private interface IPlaneSampler
        {
            float SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token);
        }

        private readonly struct PlaneSamplerD<TSampler> : IPlaneSampler
            where TSampler : struct, IEscapeSamplerD
        {
            private readonly TSampler sampler;

            public PlaneSamplerD(TSampler sampler)
            {
                this.sampler = sampler;
            }

            public float SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token)
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

            public float SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token)
            {
                Normalize(job, pixelX, pixelY, out var rotatedX, out var rotatedY);
                var cx = DoubleDouble.Add(job.CenterX, DoubleDouble.Multiply(job.Scale, rotatedX));
                var cy = DoubleDouble.Add(job.CenterY, DoubleDouble.Multiply(job.Scale, rotatedY));
                return sampler.Sample(cx, cy, job.MaxIterations, token);
            }
        }

        private readonly struct PlaneSamplerPerturbed<TSampler> : IPlaneSampler
            where TSampler : struct, IPerturbationSampler
        {
            private readonly TSampler sampler;
            private readonly ReferenceOrbit orbit;

            public PlaneSamplerPerturbed(TSampler sampler, ReferenceOrbit orbit)
            {
                this.sampler = sampler;
                this.orbit = orbit;
            }

            public float SampleAt(RenderJob job, int pixelX, int pixelY, CancellationToken token)
            {
                // The offset from the centre is a screen-sized number times the scale: fp64 holds
                // it to full relative precision at any depth, which is what perturbation relies on.
                Normalize(job, pixelX, pixelY, out var rotatedX, out var rotatedY);
                return sampler.Sample(
                    orbit, job.ScaleDouble * rotatedX, job.ScaleDouble * rotatedY, job.MaxIterations, token);
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
                if (token.IsCancellationRequested)
                {
                    return;
                }

                job.Owner.SetPassCursor(p);

                var step = StepPlan[p];

                // Coarse passes cover the overscan margin as well; fine passes stay inside the
                // visible rectangle. See MarginStepThreshold.
                var tiles = step >= MarginStepThreshold ? fullTiles : visibleTiles;

                var started = Stopwatch.GetTimestamp();
                RenderPass(job, tiles, step, p == job.PassFloor, sampler, token);

                if (token.IsCancellationRequested)
                {
                    return;
                }

                var region = step >= MarginStepThreshold ? new RectInt(0, 0, job.Width, job.Height) : job.VisibleRect;
                job.Owner.RecordThroughput(
                    (Stopwatch.GetTimestamp() - started) / (double)Stopwatch.Frequency,
                    CountNewSamples(region.width, region.height, step, p == job.PassFloor));

                // Every pass is published. A coarse pass never makes the picture worse: while a
                // sharper earlier frame still covers the view, the presenter keeps a copy of it on
                // top (see FrameReplacing), so the new pass only shows where the old one runs out.

                // Whole frame now covered at this step: colour it and publish it as one piece.
                // Marking dirty per tile instead would upload a frame that is part this
                // pass and part the coarser image beneath it, and that boundary is the
                // seam seen while panning - worst on the CPU-only deep-zoom path where a
                // gesture keeps restarting the render before it can finish a pass.
                if (token.IsCancellationRequested)
                {
                    return;
                }

                job.Owner.PublishPass(job.Escape, job.View, step);
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
            var cursor = new TileCursor();

            // No CancellationToken in the options: Parallel.For would answer a cancel by throwing on
            // every worker. The workers watch the token themselves and simply stop taking tiles.
            var options = new ParallelOptions { MaxDegreeOfParallelism = job.Workers };

            Parallel.For(0, job.Workers, options, worker =>
            {
                var rank = job.Ranks[worker];
                long produced = 0;

                while (!token.IsCancellationRequested)
                {
                    // A parked worker must still leave a finished pass. Otherwise Parallel.For
                    // waits for it forever while the reduced budget stays in effect.
                    if (Volatile.Read(ref cursor.Next) >= tiles.Length)
                    {
                        break;
                    }

                    // Parked by the frame-pacing budget: hold the thread, not a tile, so the rest
                    // of the pass keeps flowing to the workers that are still allowed to run.
                    if (!job.Budget.MayRun(rank))
                    {
                        Thread.Sleep(ParkSleepMilliseconds);
                        continue;
                    }

                    var index = Interlocked.Increment(ref cursor.Next) - 1;
                    if (index >= tiles.Length)
                    {
                        break;
                    }

                    if (!RenderTile(job, tiles[index], step, first, sampler, token, ref produced))
                    {
                        break;
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

        /// <summary>One tile of one pass. Returns false if the render was cancelled part-way.</summary>
        private static bool RenderTile<TPlane>(
            RenderJob job,
            in RectInt tile,
            int step,
            bool first,
            TPlane sampler,
            CancellationToken token,
            ref long produced)
            where TPlane : struct, IPlaneSampler
        {
            var coarse = step << 1;

            for (var by = tile.yMin; by < tile.yMax; by += step)
            {
                var rowOnCoarse = !first && by % coarse == 0;
                for (var bx = tile.xMin; bx < tile.xMax; bx += step)
                {
                    if (rowOnCoarse && bx % coarse == 0)
                    {
                        continue; // this sample was already computed in a coarser pass
                    }

                    // Nested grids must sample the same coordinates where they overlap.
                    // A block-centred sample moves when step halves and cannot be reused
                    // by the coarse-grid skip above, even at the final one-pixel pass.
                    var value = sampler.SampleAt(job, bx, by, token);
                    produced++;

                    // A cancelled sampler returns whatever it had; that value must not reach the
                    // buffer, which a later remap would otherwise show.
                    if (token.IsCancellationRequested)
                    {
                        return false;
                    }

                    FillBlock(job.Escape, job.Width, job.Height, bx, by, step, value);
                }
            }

            return true;
        }

        /// <summary>Shared "next tile please" counter. A class so the lambda can take it by ref.</summary>
        private sealed class TileCursor
        {
            public int Next;
        }

        private static void FillBlock(float[] buffer, int width, int height, int originX, int originY, int step, float value)
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
                    buffer[row + x] = value;
                }
            }
        }

        /// <summary>Palette plus colouring, swapped as one so a worker never sees half a change.</summary>
        private sealed class ColorState
        {
            public ColorState(PaletteData palette, in ColoringSettings settings)
            {
                Palette = palette;
                Settings = settings;
            }

            public PaletteData Palette { get; }
            public ColoringSettings Settings { get; }
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
                double timeBudgetSeconds)
            {
                Target = target;
                Viewport = viewport;
                Definition = definition;
                Parameters = parameters;
                View = view;
                Iterations = iterations;
                ExtendedPrecision = extendedPrecision;
                TimeBudgetSeconds = timeBudgetSeconds;
            }

            public Texture2D Target { get; }
            public Viewport Viewport { get; }
            public IFractalDefinition Definition { get; }
            public FractalParameterSet Parameters { get; }
            public ViewState View { get; }
            public int Iterations { get; }
            public bool ExtendedPrecision { get; }

            /// <summary>Seconds the run may take, 0 for no limit. See <see cref="Request"/>.</summary>
            public double TimeBudgetSeconds { get; }
        }

        private sealed class RenderJob
        {
            public RenderJob(
                FractalCpuRenderer owner,
                float[] escape,
                int width,
                int height,
                RectInt visibleRect,
                in FrameRequest request,
                CpuWorkerBudget budget,
                int[] ranks,
                ReferenceOrbit orbit,
                int passFloor,
                int passCeiling)
            {
                Budget = budget;
                Ranks = ranks;
                Orbit = orbit;
                Owner = owner;
                Escape = escape;
                Width = width;
                Height = height;
                Definition = request.Definition;
                Parameters = request.Parameters;
                View = request.View;
                VisibleRect = visibleRect;
                HasMargin = visibleRect.width < width || visibleRect.height < height;
                Aspect = width / (double)height;
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
                Workers = ranks.Length;
                PassFloor = passFloor;
                PassCeiling = passCeiling;
            }

            public FractalCpuRenderer Owner { get; }

            /// <summary>Which fractal to evaluate. The renderer never looks inside it.</summary>
            public IFractalDefinition Definition { get; }

            public FractalParameterSet Parameters { get; }

            /// <summary>Escape values, one per pixel. Colour is applied later, at publish time.</summary>
            public float[] Escape { get; }

            public int Width { get; }
            public int Height { get; }
            public double Aspect { get; }

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

            public CpuWorkerBudget Budget { get; }

            /// <summary>Park rank of each worker index. See <see cref="CpuWorkerBudget.RanksFor"/>.</summary>
            public int[] Ranks { get; }

            /// <summary>Reference orbit storage for perturbation renders. Owned by the renderer.</summary>
            public ReferenceOrbit Orbit { get; }

            public int Workers { get; }
            public int PassFloor { get; }
            public int PassCeiling { get; }

            /// <summary>Part of the buffer the viewer sees; the rest is the overscan margin.</summary>
            public RectInt VisibleRect { get; }

            public bool HasMargin { get; }
        }
    }
}
