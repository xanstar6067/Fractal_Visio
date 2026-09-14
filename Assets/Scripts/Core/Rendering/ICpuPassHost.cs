namespace FractalVisio.Core
{
    /// <summary>
    /// How a fractal hands its kernel to the renderer without either side knowing the other's type.
    ///
    /// The renderer implements this and passes it to
    /// <see cref="IFractalDefinition.RunCpuPass"/>; the fractal calls back with its own sampler
    /// struct. Because <c>Run</c> is generic and constrained to a struct, the whole progressive
    /// pass is compiled once per sampler type with the kernel inlined - a generic visitor, used
    /// here purely so the hot loop stays monomorphic.
    ///
    /// This is also the seam for the planned Burst migration: only these methods have to learn to
    /// schedule jobs, and no fractal changes.
    /// </summary>
    public interface ICpuPassHost
    {
        void Run<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerD;

        void RunExtended<TSampler>(TSampler sampler) where TSampler : struct, IEscapeSamplerDD;

        /// <summary>
        /// Deep render by perturbation. The host builds the reference orbit once, at the centre of
        /// the request, through <see cref="IPerturbationSampler.BuildReference"/>, then samples
        /// every pixel as an fp64 offset from that centre.
        /// </summary>
        void RunPerturbed<TSampler>(TSampler sampler) where TSampler : struct, IPerturbationSampler;
    }
}
