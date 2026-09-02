namespace FractalVisio.App
{
    public enum RenderBackend
    {
        GpuFloat,
        Cpu
    }

    /// <summary>
    /// What the renderer is doing right now. Read-only, rebuilt on demand: the HUD and any future
    /// progress UI read this instead of reaching into the renderers.
    /// </summary>
    public readonly struct RenderStatus
    {
        public RenderStatus(
            RenderBackend backend,
            bool interacting,
            int iterations,
            bool extendedPrecision,
            bool isBusy,
            int pass,
            int passCount,
            float progress)
        {
            Backend = backend;
            Interacting = interacting;
            Iterations = iterations;
            ExtendedPrecision = extendedPrecision;
            IsBusy = isBusy;
            Pass = pass;
            PassCount = passCount;
            Progress = progress;
        }

        public RenderBackend Backend { get; }
        public bool Interacting { get; }
        public int Iterations { get; }
        public bool ExtendedPrecision { get; }
        public bool IsBusy { get; }
        public int Pass { get; }
        public int PassCount { get; }
        public float Progress { get; }
    }

    public interface IRenderStatusSource
    {
        RenderStatus Status { get; }
    }
}
