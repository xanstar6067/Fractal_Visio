using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// Everything a module or a screen is allowed to reach. Nobody gets a renderer or another
    /// module.
    ///
    /// What a module offers the rest of the app it offers as an interface declared here in App -
    /// <see cref="IBookmarkService"/>, <see cref="IScreenshotService"/> - and registers with
    /// <see cref="Provide{T}"/> during its <c>Initialize</c>. A screen finds it with
    /// <see cref="Get{T}"/>. That is how the UI saves a bookmark without the UI assembly referencing
    /// the Modules assembly, which the asmdef layout forbids.
    /// </summary>
    public sealed class AppServices
    {
        private readonly Dictionary<Type, object> services = new();

        public AppServices(
            FractalSession session,
            IRenderStatusSource render,
            IBackdropSource backdrop,
            IReadOnlyList<IFractalDefinition> catalog,
            PaletteCatalog palettes,
            IAppStorage storage,
            Transform uiRoot)
        {
            Session = session;
            Render = render;
            Backdrop = backdrop;
            Catalog = catalog ?? Array.Empty<IFractalDefinition>();
            Palettes = palettes;
            Storage = storage;
            UiRoot = uiRoot;
        }

        public FractalSession Session { get; }

        public IRenderStatusSource Render { get; }

        /// <summary>What is on screen right now, for backdrop effects and image capture.</summary>
        public IBackdropSource Backdrop { get; }

        /// <summary>
        /// Every fractal the app knows, as Core interfaces. This is how the UI lists fractals
        /// without referencing the Fractals assembly.
        /// </summary>
        public IReadOnlyList<IFractalDefinition> Catalog { get; }

        /// <summary>Built-in and user palettes.</summary>
        public PaletteCatalog Palettes { get; }

        public IAppStorage Storage { get; }

        /// <summary>Canvas transform modules and screens parent their own UI under.</summary>
        public Transform UiRoot { get; }

        /// <summary>Register a service a module offers. One implementation per interface.</summary>
        public void Provide<T>(T service) where T : class
        {
            services[typeof(T)] = service ?? throw new ArgumentNullException(nameof(service));
        }

        /// <summary>The registered service, or null when no module offers it.</summary>
        public T Get<T>() where T : class
        {
            return services.TryGetValue(typeof(T), out var service) ? service as T : null;
        }
    }
}
