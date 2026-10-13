namespace FractalVisio.Core
{
    /// <summary>
    /// How hard a ray tries, the same for the picture (<c>SceneGpuRenderer</c>) and for the point
    /// under a finger (<see cref="ScenePicker"/>): the WPF version's defaults.
    /// </summary>
    public static class SceneMarching
    {
        /// <summary>Most steps one ray of the picture takes.</summary>
        public const int MaximumSteps = 160;

        /// <summary>Pixel tolerance: the surface counts as reached within this many pixels' width.</summary>
        public const float Detail = 1f;

        /// <summary>Range of haze and of depth colouring, in world units; rays go further when the shape is.</summary>
        public const float TraceDistance = 24f;
    }
}
