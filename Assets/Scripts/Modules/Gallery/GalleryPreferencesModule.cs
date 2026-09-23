using System;
using System.Collections.Generic;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Favourites and the recent list, kept by fractal id in one storage document and offered to
    /// the gallery as <see cref="IGalleryPreferences"/>.
    ///
    /// "Recent" is recorded from the session, not from the gallery: a fractal opened from a
    /// bookmark or restored at start was opened all the same.
    /// </summary>
    public sealed class GalleryPreferencesModule : IAppModule, IGalleryPreferences
    {
        private const string StorageKey = "gallery";
        private const int RecentCapacity = 12;

        private readonly List<string> favorites = new();
        private readonly List<string> recent = new();
        private AppServices services;

        public string Id => "gallery";

        public event Action Changed;

        public IReadOnlyList<string> Recent => recent;

        public void Initialize(AppServices context)
        {
            services = context;
            Load();

            // The state store has already restored the session by now: what is open is recent.
            RecordRecent(services.Session.Definition);
            services.Session.Changed += OnSessionChanged;
            services.Provide<IGalleryPreferences>(this);
        }

        public void Tick()
        {
        }

        public void Shutdown()
        {
            if (services != null)
            {
                services.Session.Changed -= OnSessionChanged;
            }

            services = null;
        }

        public bool IsFavorite(string id) => !string.IsNullOrEmpty(id) && favorites.Contains(id);

        public void SetFavorite(string id, bool favorite)
        {
            if (string.IsNullOrEmpty(id) || IsFavorite(id) == favorite)
            {
                return;
            }

            if (favorite)
            {
                favorites.Add(id);
            }
            else
            {
                favorites.Remove(id);
            }

            Persist();
        }

        private void OnSessionChanged(SessionChange change)
        {
            if ((change & SessionChange.Definition) != 0)
            {
                RecordRecent(services.Session.Definition);
            }
        }

        private void RecordRecent(IFractalDefinition definition)
        {
            var id = definition?.Id;
            if (string.IsNullOrEmpty(id) || (recent.Count > 0 && recent[0] == id))
            {
                return;
            }

            recent.Remove(id);
            recent.Insert(0, id);
            if (recent.Count > RecentCapacity)
            {
                recent.RemoveRange(RecentCapacity, recent.Count - RecentCapacity);
            }

            Persist();
        }

        private void Load()
        {
            favorites.Clear();
            recent.Clear();
            if (!StateCodec.TryFromJson<GalleryDto>(services.Storage?.Read(StorageKey), out var dto))
            {
                return;
            }

            // An id that no longer names a fractal - one removed in an update - is dropped quietly
            // rather than shown as an empty card.
            Copy(dto.favorites, favorites);
            Copy(dto.recent, recent);
        }

        private void Copy(string[] source, List<string> target)
        {
            if (source == null)
            {
                return;
            }

            foreach (var id in source)
            {
                if (!string.IsNullOrEmpty(id) && !target.Contains(id) && Known(id))
                {
                    target.Add(id);
                }
            }
        }

        private bool Known(string id)
        {
            var catalog = services.Catalog;
            for (var i = 0; i < catalog.Count; i++)
            {
                if (catalog[i].Id == id)
                {
                    return true;
                }
            }

            return false;
        }

        private void Persist()
        {
            services?.Storage?.Write(StorageKey, StateCodec.ToJson(new GalleryDto
            {
                favorites = favorites.ToArray(),
                recent = recent.ToArray()
            }));
            Changed?.Invoke();
        }

        [Serializable]
        private sealed class GalleryDto
        {
            public int version = 1;
            public string[] favorites;
            public string[] recent;
        }
    }
}
