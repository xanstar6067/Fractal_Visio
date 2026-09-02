using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// Everything the app needs to know about one fractal. This interface is the whole extension
    /// point: a new fractal is a sampler struct, one of these, and a shader. Nothing in
    /// <c>Rendering</c>, <c>App</c> or the UI is edited to add one - if it has to be, the
    /// abstraction leaked and the fix belongs here rather than in a special case there.
    /// </summary>
    public interface IFractalDefinition
    {
        /// <summary>Stable key used by saved state and bookmarks. Never rename a shipped one.</summary>
        string Id { get; }

        string DisplayName { get; }

        /// <summary>Where the view starts, and what "reset" returns to.</summary>
        ViewState DefaultView { get; }

        IReadOnlyList<FractalParameterDescriptor> Parameters { get; }

        PrecisionTier SupportedPrecision { get; }

        /// <summary>Shader path, e.g. "FractalVisio/Mandelbrot". Only needed for
        /// <see cref="PrecisionTier.Float"/>.</summary>
        string ShaderName { get; }

        /// <summary>Set the fractal's own uniforms. The renderer has already set the shared ones
        /// (centre, scale, aspect, rotation, iterations, palette).</summary>
        void BindMaterial(Material material, in FractalParameterSet parameters);

        /// <summary>
        /// Hand the CPU renderer a sampler for the requested precision by calling
        /// <see cref="ICpuPassHost.Run{T}"/> or <see cref="ICpuPassHost.RunExtended{T}"/> exactly
        /// once. See <see cref="ICpuPassHost"/> for why it is shaped as a callback.
        /// </summary>
        void RunCpuPass(ICpuPassHost host, in FractalParameterSet parameters, bool extendedPrecision);
    }
}
