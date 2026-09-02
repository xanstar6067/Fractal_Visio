using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Everything a module is allowed to reach. Grows as the app does - the catalog, the palette
    /// library, the state store and the UI router all land here - but a module never gets a
    /// renderer or another module.
    /// </summary>
    public sealed class AppServices
    {
        public AppServices(FractalSession session, IRenderStatusSource render, Transform uiRoot)
        {
            Session = session;
            Render = render;
            UiRoot = uiRoot;
        }

        public FractalSession Session { get; }

        public IRenderStatusSource Render { get; }

        /// <summary>Canvas transform modules may parent their own UI under.</summary>
        public Transform UiRoot { get; }
    }
}
