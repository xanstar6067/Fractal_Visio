namespace FractalVisio.Core
{
    /// <summary>
    /// One tile of the gallery: which fractal, the section it is filed under, and the view its
    /// preview shows.
    ///
    /// Placement is the catalog's business rather than the fractal's, so it sits beside the
    /// definition instead of inside it: the same definition could be filed twice, or moved, without
    /// being edited. The gallery works with entries, never with the catalog's order of definitions,
    /// which is also what lets a future kind of mode that is not an escape-time fractal join it.
    /// </summary>
    public readonly struct CatalogEntry
    {
        public CatalogEntry(IFractalDefinition definition, string section, ViewState? preview = null)
        {
            Definition = definition;
            Section = section ?? string.Empty;
            PreviewView = preview ?? (definition != null ? definition.DefaultView : default);
        }

        public IFractalDefinition Definition { get; }

        /// <summary>Stable id the gallery groups by. Shown as the string <c>section.&lt;id&gt;</c>.</summary>
        public string Section { get; }

        /// <summary>What the preview tile shows: the definition's default view unless the catalog frames it better.</summary>
        public ViewState PreviewView { get; }

        public string Id => Definition != null ? Definition.Id : string.Empty;
    }
}
