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
                interfaceSettings.InertiaSeconds = settings.inertiaSeconds;
                interfaceSettings.Language = settings.language;
                interfaceSettings.ShowDebugInfo = settings.showDebugInfo;
                interfaceSettings.ScreenshotWidth = settings.screenshotWidth;
                interfaceSettings.ScreenshotHeight = settings.screenshotHeight;
                interfaceSettings.ScreenshotSupersampling = settings.screenshotSupersampling;
                interfaceSettings.ScreenshotJpeg = settings.screenshotJpeg;
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
                interfaceScale = session.Interface.Scale,
                inertiaSeconds = session.Interface.InertiaSeconds,
                language = session.Interface.Language,
                showDebugInfo = session.Interface.ShowDebugInfo,
                screenshotWidth = session.Interface.ScreenshotWidth,
                screenshotHeight = session.Interface.ScreenshotHeight,
                screenshotSupersampling = session.Interface.ScreenshotSupersampling,
                screenshotJpeg = session.Interface.ScreenshotJpeg
            }));
        }

        [Serializable]
        private sealed class AppSettingsDto
        {
            public int version = 1;
            public float renderScale;
            public float interfaceScale = 1f;

            /// <summary>Missing from files written before inertia existed; the initialiser is what they read as.</summary>
            public float inertiaSeconds = 5f;

            /// <summary>Locale code, or empty for the device language - which is also what files from before stage 13 read as.</summary>
            public string language = string.Empty;

            /// <summary>The debug readout. Files from before it could be hidden read as hidden - the new default.</summary>
            public bool showDebugInfo;
            public int screenshotWidth;
            public int screenshotHeight;

            /// <summary>Files from before supersampling read as off.</summary>
            public int screenshotSupersampling = 1;
            public bool screenshotJpeg;
        }
    }
}
