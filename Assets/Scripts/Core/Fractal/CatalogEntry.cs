namespace FractalVisio.Core
{
    /// <summary>
    /// One tile of the gallery: what it opens, the section and folder it is filed under, and the view
    /// its preview shows.
    ///
    /// Placement is the catalog's business rather than the fractal's, so it sits beside the
    /// definition instead of inside it: the same definition could be filed twice, or moved, without
    /// being edited. The gallery works with entries, never with the catalog's order of definitions,
    /// which is what let the 3D scenes (<see cref="ISceneDefinition"/>) join it: an entry holds an
    /// <see cref="IExplorationMode"/>, a plane fractal or a scene.
    /// </summary>
    public readonly struct CatalogEntry
    {
        public CatalogEntry(IFractalDefinition definition, string section, string folder = null, ViewState? preview = null)
        {
            Mode = definition;
            Section = section ?? string.Empty;
            Folder = folder ?? string.Empty;
            PreviewView = preview ?? (definition != null ? definition.DefaultView : default);
        }

        public CatalogEntry(ISceneDefinition scene, string section, string folder = null)
        {
            Mode = scene;
            Section = section ?? string.Empty;
            Folder = folder ?? string.Empty;
            PreviewView = default;
        }

        /// <summary>What the tile opens.</summary>
        public IExplorationMode Mode { get; }

        /// <summary>The plane fractal the tile opens, or null for a scene.</summary>
        public IFractalDefinition Definition => Mode as IFractalDefinition;

        /// <summary>The 3D scene the tile opens, or null for a plane fractal. Its preview shows the default camera.</summary>
        public ISceneDefinition Scene => Mode as ISceneDefinition;

        /// <summary>Stable id the gallery groups by. Shown as the string <c>section.&lt;id&gt;</c>.</summary>
        public string Section { get; }

        /// <summary>
        /// Stable id of the folder inside the section - a group the gallery opens and closes, for a
        /// section with too many cards to scroll through (the Mandelbrot family has sixty). Shown as
        /// <c>folder.&lt;id&gt;</c>. Empty: the card sits in the section itself.
        /// </summary>
        public string Folder { get; }

        /// <summary>What a plane fractal's preview tile shows: the definition's default view unless the catalog frames it better.</summary>
        public ViewState PreviewView { get; }

        public string Id => Mode != null ? Mode.Id : string.Empty;
    }
}
