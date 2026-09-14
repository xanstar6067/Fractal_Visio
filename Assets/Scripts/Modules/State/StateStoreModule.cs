using System;
using UnityEngine;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Puts the app back where the user left it: the last picture, and the render and interface
    /// settings. Restores once at start, saves a moment after things stop changing, and saves at
    /// once when the app loses focus or quits.
    ///
    /// The focus save is the one that matters on a phone. Android freezes a backgrounded app and
    /// may kill it without another callback, so "save on quit" alone loses the session exactly in
    /// the ordinary case of switching apps and never coming back.
    /// </summary>
    public sealed class StateStoreModule : IAppModule
    {
        private const string SessionKey = "session";
        private const string SettingsKey = "settings";

        /// <summary>A pinch changes the view every frame; one write after it settles is enough.</summary>
        private const float SaveDelaySeconds = 1.5f;

        private AppServices services;
        private bool dirty;
        private float changedAt;

        public string Id => "state-store";

        public void Initialize(AppServices context)
        {
            services = context;
            Restore();

            services.Session.Changed += OnSessionChanged;
            Application.focusChanged += OnFocusChanged;
            Application.quitting += SaveNow;
        }

        public void Tick()
        {
            if (dirty && Time.unscaledTime - changedAt >= SaveDelaySeconds)
            {
                SaveNow();
            }
        }

        public void Shutdown()
        {
            if (services == null)
            {
                return;
            }

            SaveNow();
            services.Session.Changed -= OnSessionChanged;
            Application.focusChanged -= OnFocusChanged;
            Application.quitting -= SaveNow;
            services = null;
        }

        private void OnSessionChanged(SessionChange change)
        {
            dirty = true;
            changedAt = Time.unscaledTime;
        }

        private void OnFocusChanged(bool focused)
        {
            if (!focused)
            {
                SaveNow();
            }
        }

        private void Restore()
        {
            var storage = services.Storage;
            if (storage == null)
            {
                return;
            }

            if (StateCodec.TryFromJson<AppSettingsDto>(storage.Read(SettingsKey), out var settings))
            {
                services.Session.SetRenderScale(settings.renderScale);

                var interfaceSettings = services.Session.Interface;
                interfaceSettings.Scale = settings.interfaceScale;
                services.Session.SetInterface(interfaceSettings);
            }

            if (StateCodec.TryFromJson<FractalStateDto>(storage.Read(SessionKey), out var state))
            {
                services.Session.Apply(state, services.Catalog, services.Palettes);
            }

            // Restoring is itself a session change; it is not a reason to write the same file back.
            dirty = false;
        }

        private void SaveNow()
        {
            if (services?.Storage == null)
            {
                return;
            }

            dirty = false;
            var session = services.Session;
            services.Storage.Write(SessionKey, StateCodec.ToJson(session.Capture()));
            services.Storage.Write(SettingsKey, StateCodec.ToJson(new AppSettingsDto
            {
                renderScale = session.Quality.RenderScale,
                interfaceScale = session.Interface.Scale
            }));
        }

        [Serializable]
        private sealed class AppSettingsDto
        {
            public int version = 1;
            public float renderScale;
            public float interfaceScale = 1f;
        }
    }
}
