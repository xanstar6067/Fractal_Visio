namespace FractalVisio.App
{
    /// <summary>
    /// An optional piece of the app: HUD, screenshot, saved states, bookmarks, settings. Modules
    /// know the <see cref="AppServices"/> and nothing about each other, so adding one is adding a
    /// file plus a line in the bootstrap's module list.
    /// </summary>
    public interface IAppModule
    {
        /// <summary>Stable key, used by saved state and by the bootstrap for diagnostics.</summary>
        string Id { get; }

        void Initialize(AppServices context);

        /// <summary>Called once per frame by the bootstrap, after the presenter has ticked.</summary>
        void Tick();

        void Shutdown();
    }
}
