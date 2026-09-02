using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// Everything a module or a screen is allowed to reach. Grows as the app does - the palette
    /// library and the state store land here next - but nobody gets a renderer or another module.
    /// </summary>
    public sealed class AppServices
    {
        public AppServices(
            FractalSession session,
            IRenderStatusSource render,
            IBackdropSource backdrop,
            IReadOnlyList<IFractalDefinition> catalog,
            Transform uiRoot)
        {
            Session = session;
            Render = render;
            Backdrop = backdrop;
            Catalog = catalog ?? Array.Empty<IFractalDefinition>();
            UiRoot = uiRoot;
        }

        public FractalSession Session { get; }

        public IRenderStatusSource Render { get; }

        /// <summary>What is on screen right now, for backdrop effects and future capture.</summary>
        public IBackdropSource Backdrop { get; }

        /// <summary>
        /// Every fractal the app knows, as Core interfaces. This is how the UI lists fractals
        /// without referencing the Fractals assembly.
        /// </summary>
        public IReadOnlyList<IFractalDefinition> Catalog { get; }

        /// <summary>Canvas transform modules and screens parent their own UI under.</summary>
        public Transform UiRoot { get; }
    }
}
