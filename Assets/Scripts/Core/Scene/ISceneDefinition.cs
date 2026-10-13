using System;
using System.Collections.Generic;
using UnityEngine;

namespace FractalVisio.Core
{
    /// <summary>
    /// A 3D fractal drawn by ray marching a distance estimate on the GPU - the WPF version's
    /// <c>Fractal3DWindow</c> modes with a surface. The second kind of thing the gallery opens,
    /// beside <see cref="IFractalDefinition"/>; see <c>docs/ARCHITECTURE.md</c> §5.14.
    ///
    /// Adding one costs what a plane fractal does: this interface, one shader that writes its
    /// distance estimate and includes <c>Shaders/Common/SceneCommon.hlsl</c> and
    /// <c>SceneMarch.hlsl</c> around it, and a catalog line. Camera, gestures, lighting, colouring,
    /// panels, bookmarks and export are shared; nothing in them names a scene.
    /// </summary>
    public interface ISceneDefinition : IExplorationMode
    {
        /// <summary>Where the camera starts, framed for a screen at least as wide as it is tall.</summary>
        SceneCamera DefaultCamera { get; }

        /// <summary>Shader path, e.g. "FractalVisio/Mandelbulb".</summary>
        string ShaderName { get; }

        /// <summary>
        /// Radius of a sphere about the origin holding the whole shape. Rays start where they enter it:
        /// far from the shape a distance estimate can overshoot the front surface.
        /// </summary>
        float BoundingRadius(in FractalParameterSet parameters);

        /// <summary>
        /// Set the shape's own uniforms, and <c>_SceneIterations</c>, which colouring by iteration
        /// reads. The renderer has already set the camera, light and colouring.
        /// </summary>
        void BindMaterial(Material material, in FractalParameterSet parameters);

        /// <summary>
        /// The same distance estimate as the shader's, on the CPU: what the finger touches is found by
        /// marching one ray with it. Built once per gesture, so it may resolve its parameters up front.
        /// </summary>
        ISceneDistance CreateDistance(in FractalParameterSet parameters);
    }

    /// <summary>A scene's distance estimate on the CPU. See <see cref="ISceneDefinition.CreateDistance"/>.</summary>
    public interface ISceneDistance
    {
        /// <summary>
        /// Distance from <paramref name="point"/> to the surface, at most. <paramref name="stepScale"/>
        /// is how much shorter than that a step must be where the estimate is known to run high - 1
        /// where it is honest.
        /// </summary>
        float Estimate(Vector3 point, out float stepScale);
    }

    /// <summary>
    /// Implemented by a scene with views worth visiting - the WPF version's presets. Names are locale
    /// keys <c>fractal.&lt;id&gt;.place.&lt;place id&gt;</c>, as for a plane fractal's places.
    /// </summary>
    public interface IScenePlaces
    {
        IReadOnlyList<ScenePlace> Places { get; }
    }

    /// <summary>
    /// A view of a scene: a camera and the shape's parameters. Not a look - as with the places of a
    /// plane fractal, colours stay the user's.
    /// </summary>
    public readonly struct ScenePlace
    {
        public ScenePlace(string id, in SceneCamera camera, params ParameterValue[] parameters)
        {
            Id = id;
            Camera = camera;
            Parameters = parameters ?? Array.Empty<ParameterValue>();
        }

        /// <summary>Stable key for the locale name. Never rename a shipped one.</summary>
        public string Id { get; }

        /// <summary>Framed for a screen at least as wide as it is tall, like the default camera.</summary>
        public SceneCamera Camera { get; }

        public IReadOnlyList<ParameterValue> Parameters { get; }
    }
}
