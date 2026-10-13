using System.Collections.Generic;

namespace FractalVisio.Core
{
    /// <summary>
    /// Anything the gallery can open: a fractal of the plane (<see cref="IFractalDefinition"/>) or a
    /// scene in three dimensions (<see cref="ISceneDefinition"/>). What they share is what the
    /// interface needs to list, name, save and tune one - an id, a name and the parameters. How the
    /// picture is made, and what a view of it is, belongs to each kind on its own.
    /// </summary>
    public interface IExplorationMode
    {
        /// <summary>Stable key used by saved state and bookmarks. Never rename a shipped one.</summary>
        string Id { get; }

        string DisplayName { get; }

        IReadOnlyList<FractalParameterDescriptor> Parameters { get; }
    }
}
