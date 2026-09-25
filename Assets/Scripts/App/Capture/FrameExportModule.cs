using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using FractalVisio.Core;
using FractalVisio.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// Offers <see cref="IFrameExport"/>: the picture rendered again at the size of the saved image.
    ///
    /// The image is cut into tiles and each tile is an ordinary render of a smaller view - its centre
    /// moved to the tile's middle, its scale the tile's share of the height - so both engines draw
    /// it without knowing it is a tile. The GPU draws while the spacing between samples is one fp32
    /// can still tell apart (the same limit the explorer switches at, measured per sample rather than
    /// per screen); past it the CPU does, with perturbation where the explorer would use it.
    /// Supersampling renders each tile that many times larger and averages blocks of samples.
    ///
    /// It lives in App rather than in Modules for the reason <see cref="ThumbnailModule"/> does: it
    /// needs renderers, and no module is handed one.
    /// </summary>
    public sealed class FrameExportModule : IAppModule, IFrameExport
    {
        private readonly FractalPresenter presenter;
        private readonly List<TiledRender> jobs = new();
        private AppServices services;
        private FractalGpuRenderer gpuRenderer;

        public FrameExportModule(FractalPresenter presenter)
        {
            this.presenter = presenter;
        }

        public string Id => "frame-export";

        public void Initialize(AppServices context)
        {
            services = context;
            services.Provide<IFrameExport>(this);
        }

        public IFrameExportJob Begin(int width, int height, int supersampling)
        {
            if (services == null || presenter == null || width < 1 || height < 1)
            {
                return null;
            }

            // Materials are cached per shader, so one GPU renderer serves every export of the run.
            gpuRenderer ??= new FractalGpuRenderer();
            var display = presenter.DisplayViewport;
            var job = new TiledRender(
                services.Session, gpuRenderer, presenter.WorkerBudget,
                width, height, Mathf.Clamp(supersampling, 1, 3), display.Width, display.Height);
            jobs.Add(job);
            return job;
        }

        public void Tick()
        {
            for (var i = jobs.Count - 1; i >= 0; i--)
            {
                var job = jobs[i];
                if (job.IsDisposed)
                {
                    jobs.RemoveAt(i);
                    continue;
                }

                job.Tick();
            }
        }

        public void Shutdown()
        {
            for (var i = 0; i < jobs.Count; i++)
            {
                jobs[i].Dispose();
            }

            jobs.Clear();
            gpuRenderer?.Dispose();
            gpuRenderer = null;
            services = null;
        }

        /// <summary>
        /// One export: tiles in rows from the bottom, the finished ones averaged down into the output
        /// on the thread pool while the next ones render.
        /// </summary>
        private sealed class TiledRender : IFrameExportJob
        {
            /// <summary>
            /// Tile side in samples on the GPU. About a phone screen's worth of pixels at most, which
            /// is what a single draw is known to finish in; a draw several times longer risks the
            /// driver treating the GPU as hung.
            /// </summary>
            private const int GpuTileSamples = 1024;

            /// <summary>Tile side in samples on the CPU. Each tile computes its own reference orbit, so not too small.</summary>
            private const int CpuTileSamples = 1024;

            /// <summary>GPU tiles in flight at once: the read back takes a frame or two, the draw far less.</summary>
            private const int GpuTilesInFlight = 4;

            private readonly IFractalDefinition definition;
            private readonly FractalParameterSet parameters;
            private readonly ViewState view;
            private readonly PaletteData palette;
            private readonly ColoringSettings coloring;
            private readonly FractalGpuRenderer gpuRenderer;
            private readonly CpuWorkerBudget workerBudget;
            private readonly bool useGpu;
            private readonly bool extendedPrecision;
            private readonly int samples;
            private readonly int sampleWidth;
            private readonly int sampleHeight;
            private readonly List<RectInt> tiles = new();
            private readonly List<GpuTile> gpuInFlight = new();
            private readonly List<Task> merging = new();

            private FractalCpuRenderer cpuRenderer;
            private Texture2D cpuTexture;
            private int cpuTile = -1;
            private ViewState cpuTileView;
            private int nextTile;
            private long samplesDone;
            private byte[] output;
            private bool failed;
            private bool cancelled;

            public TiledRender(
                FractalSession session,
                FractalGpuRenderer gpu,
                CpuWorkerBudget budget,
                int width,
                int height,
                int supersampling,
                int displayWidth,
                int displayHeight)
            {
                Width = width;
                Height = height;
                samples = supersampling;
                sampleWidth = width * supersampling;
                sampleHeight = height * supersampling;
                gpuRenderer = gpu;
                workerBudget = budget;

                definition = session.Definition;
                parameters = session.Parameters;
                palette = session.Palette;
                coloring = session.Coloring;
                view = Cropped(session.View, displayWidth / (double)Mathf.Max(1, displayHeight), width / (double)height);

                // The explorer's limits are scales of a screen-high view; per sample they become
                // spacings, which is what decides whether an engine can resolve neighbouring pixels.
                var quality = session.Quality;
                var spacing = view.scale.AsDouble / sampleHeight;
                var perDisplayPixel = 1d / Mathf.Max(1, displayHeight);
                useGpu = gpu.Supports(definition) && spacing >= quality.GpuMinimumScale * perDisplayPixel;
                extendedPrecision = spacing < quality.ExtendedPrecisionScale * perDisplayPixel &&
                                    (definition.SupportedPrecision & (PrecisionTier.DoubleDouble | PrecisionTier.Perturbation)) != 0;

                try
                {
                    output = new byte[width * height * 4];
                }
                catch (OutOfMemoryException)
                {
                    failed = true;
                    return;
                }

                var side = (useGpu ? GpuTileSamples : CpuTileSamples) / samples;
                for (var y = 0; y < height; y += side)
                {
                    for (var x = 0; x < width; x += side)
                    {
                        tiles.Add(new RectInt(x, y, Mathf.Min(side, width - x), Mathf.Min(side, height - y)));
                    }
                }

                Debug.Log($"Export {width}x{height}, {samples}x{samples} samples, {tiles.Count} tiles on the " +
                          (useGpu ? "GPU" : extendedPrecision ? "CPU (extended precision)" : "CPU") + ".");
            }

            public int Width { get; }
            public int Height { get; }

            public float Progress
            {
                get
                {
                    var total = (double)sampleWidth * sampleHeight;
                    var done = (double)samplesDone;
                    if (cpuRenderer != null && cpuTile >= 0)
                    {
                        var tile = tiles[cpuTile];
                        done += cpuRenderer.Progress * (double)tile.width * tile.height * samples * samples;
                    }

                    return total > 0d ? Mathf.Clamp01((float)(done / total)) : 0f;
                }
            }

            public bool IsDone { get; private set; }

            public bool IsDisposed { get; private set; }

            public byte[] Pixels => IsDone && !failed && !cancelled ? output : null;

            public void Cancel()
            {
                if (IsDone)
                {
                    return;
                }

                cancelled = true;
                Finish();
            }

            public void Tick()
            {
                if (IsDone)
                {
                    return;
                }

                if (failed)
                {
                    Finish();
                    return;
                }

                if (useGpu)
                {
                    TickGpu();
                }
                else
                {
                    TickCpu();
                }

                if (nextTile < tiles.Count || gpuInFlight.Count > 0 || cpuTile >= 0)
                {
                    return;
                }

                for (var i = 0; i < merging.Count; i++)
                {
                    if (!merging[i].IsCompleted)
                    {
                        return;
                    }

                    if (merging[i].IsFaulted)
                    {
                        Debug.LogWarning("Export failed: " + merging[i].Exception?.GetBaseException().Message);
                        failed = true;
                    }
                }

                Finish();
            }

            public void Dispose()
            {
                if (!IsDone)
                {
                    cancelled = true;
                    Finish();
                }

                IsDisposed = true;
            }

            /// <summary>
            /// The view of an image of <paramref name="outputAspect"/> cut centrally out of the screen:
            /// a wider image keeps the screen's width and loses height, a narrower one keeps its height.
            /// </summary>
            private static ViewState Cropped(in ViewState screen, double displayAspect, double outputAspect)
            {
                var result = screen;
                if (outputAspect > displayAspect)
                {
                    result.scale = new HighPrecision(screen.scale.AsDecimal * (decimal)(displayAspect / outputAspect));
                }

                return result;
            }

            /// <summary>
            /// The view that draws <paramref name="tile"/> of the whole image: the whole image's plane
            /// point under the tile's middle, and the tile's share of the height. Sample positions
            /// relative to a view's centre do not depend on the buffer's size, so the tiles meet
            /// without a seam.
            /// </summary>
            private ViewState TileView(RectInt tile)
            {
                var whole = new Viewport(sampleWidth, sampleHeight);
                var middle = new Vector2(
                    (tile.x + tile.width * 0.5f) * samples,
                    (tile.y + tile.height * 0.5f) * samples);
                var (x, y) = ViewNavigator.ScreenToFractal(view, whole, middle);
                var result = view;
                result.x = new HighPrecision(x);
                result.y = new HighPrecision(y);
                result.scale = new HighPrecision(view.scale.AsDecimal * (tile.height * samples) / sampleHeight);
                return result;
            }

            private void TickGpu()
            {
                for (var i = gpuInFlight.Count - 1; i >= 0; i--)
                {
                    var pending = gpuInFlight[i];
                    if (pending.Grab.IsBusy && !pending.Grab.Tick())
                    {
                        continue;
                    }

                    gpuInFlight.RemoveAt(i);
                    var pixels = pending.Grab.Pixels;
                    pending.Grab.Dispose();
                    if (pixels == null)
                    {
                        failed = true;
                        continue;
                    }

                    Merge(pending.Tile, pixels);
                }

                gpuRenderer.SetColoring(palette, coloring);
                while (!failed && nextTile < tiles.Count && gpuInFlight.Count < GpuTilesInFlight)
                {
                    var tile = tiles[nextTile++];
                    var width = tile.width * samples;
                    var height = tile.height * samples;
                    var target = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
                    try
                    {
                        gpuRenderer.Render(definition, parameters, TileView(tile), view.iterations, target);
                        var grab = new FrameGrab();
                        if (!grab.Begin(target, new Rect(0f, 0f, 1f, 1f), width, height))
                        {
                            failed = true;
                            break;
                        }

                        gpuInFlight.Add(new GpuTile(tile, grab));
                    }
                    finally
                    {
                        RenderTexture.ReleaseTemporary(target);
                    }
                }
            }

            private void TickCpu()
            {
                if (cpuRenderer == null)
                {
                    cpuRenderer = new FractalCpuRenderer(new EscapeColorMapper(), workerBudget, false);
                    cpuRenderer.SetColoring(palette, coloring);
                }

                if (cpuTile >= 0)
                {
                    cpuRenderer.Update();
                    if (cpuRenderer.IsBusy || !cpuRenderer.HasPublished || cpuRenderer.PublishedStep != 1 ||
                        !SameView(cpuRenderer.PublishedView, cpuTileView))
                    {
                        return;
                    }

                    Merge(tiles[cpuTile], cpuTexture.GetRawTextureData());
                    cpuTile = -1;
                }

                if (nextTile >= tiles.Count)
                {
                    return;
                }

                cpuTile = nextTile++;
                var tile = tiles[cpuTile];
                var width = tile.width * samples;
                var height = tile.height * samples;
                if (cpuTexture == null || cpuTexture.width != width || cpuTexture.height != height)
                {
                    if (cpuTexture != null)
                    {
                        UnityEngine.Object.Destroy(cpuTexture);
                    }

                    cpuTexture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                    {
                        name = "Fractal Export Tile",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                }

                cpuTileView = TileView(tile);
                cpuRenderer.DiscardPublished();
                cpuRenderer.Request(
                    cpuTexture, new Viewport(width, height), definition, parameters, cpuTileView,
                    view.iterations, extendedPrecision);
            }

            private static bool SameView(in ViewState a, in ViewState b) =>
                a.x.Equals(b.x) && a.y.Equals(b.y) && a.scale.Equals(b.scale) && a.rotation == b.rotation;

            /// <summary>Average a finished tile's sample blocks into the output, off the main thread.</summary>
            private void Merge(RectInt tile, byte[] pixels)
            {
                samplesDone += (long)tile.width * tile.height * samples * samples;
                var target = output;
                var imageWidth = Width;
                var n = samples;
                merging.Add(Task.Run(() => Downsample(pixels, tile, n, target, imageWidth)));
            }

            private static void Downsample(byte[] source, RectInt tile, int n, byte[] target, int imageWidth)
            {
                var sourceWidth = tile.width * n;
                var count = n * n;
                var half = count / 2;
                for (var row = 0; row < tile.height; row++)
                {
                    var targetIndex = ((tile.y + row) * imageWidth + tile.x) * 4;
                    if (n == 1)
                    {
                        Buffer.BlockCopy(source, row * sourceWidth * 4, target, targetIndex, tile.width * 4);
                        continue;
                    }

                    for (var column = 0; column < tile.width; column++, targetIndex += 4)
                    {
                        int r = 0, g = 0, b = 0;
                        for (var sy = 0; sy < n; sy++)
                        {
                            var sourceIndex = ((row * n + sy) * sourceWidth + column * n) * 4;
                            for (var sx = 0; sx < n; sx++, sourceIndex += 4)
                            {
                                r += source[sourceIndex];
                                g += source[sourceIndex + 1];
                                b += source[sourceIndex + 2];
                            }
                        }

                        target[targetIndex] = (byte)((r + half) / count);
                        target[targetIndex + 1] = (byte)((g + half) / count);
                        target[targetIndex + 2] = (byte)((b + half) / count);
                        target[targetIndex + 3] = 255;
                    }
                }
            }

            private void Finish()
            {
                IsDone = true;
                for (var i = 0; i < gpuInFlight.Count; i++)
                {
                    gpuInFlight[i].Grab.Dispose();
                }

                gpuInFlight.Clear();
                cpuTile = -1;
                cpuRenderer?.Dispose();
                cpuRenderer = null;
                if (cpuTexture != null)
                {
                    UnityEngine.Object.Destroy(cpuTexture);
                    cpuTexture = null;
                }

                if (failed || cancelled)
                {
                    output = null;
                }
            }

            private readonly struct GpuTile
            {
                public GpuTile(RectInt tile, FrameGrab grab)
                {
                    Tile = tile;
                    Grab = grab;
                }

                public RectInt Tile { get; }
                public FrameGrab Grab { get; }
            }
        }
    }
}
