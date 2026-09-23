using System;
using System.Collections.Generic;
using System.Globalization;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Saved pictures. Owns the list and its storage document and offers them to the UI as
    /// <see cref="IBookmarkService"/>; the bookmarks screen is only a view of it.
    /// </summary>
    public sealed class BookmarksModule : IAppModule, IBookmarkService
    {
        private const string StorageKey = "bookmarks";

        private readonly List<Bookmark> items = new();
        private AppServices services;

        public string Id => "bookmarks";

        public IReadOnlyList<Bookmark> Items => items;

        public event Action Changed;

        public void Initialize(AppServices context)
        {
            services = context;
            Load();
            services.Provide<IBookmarkService>(this);
        }

        public void Tick()
        {
        }

        public void Shutdown()
        {
            services = null;
        }

        public Bookmark AddCurrent()
        {
            if (services == null)
            {
                return null;
            }

            var session = services.Session;
            var bookmark = new Bookmark
            {
                id = "bm-" + DateTime.UtcNow.Ticks.ToString("x", CultureInfo.InvariantCulture),
                name = DescribeCurrent(session, services.Strings),
                createdTicks = DateTime.UtcNow.Ticks,
                state = session.Capture()
            };

            items.Insert(0, bookmark);
            Persist();
            return bookmark;
        }

        public bool Open(Bookmark bookmark)
        {
            return bookmark?.state != null &&
                   services != null &&
                   services.Session.Apply(bookmark.state, services.Catalog, services.Palettes);
        }

        public void Remove(string id)
        {
            if (items.RemoveAll(item => item.id == id) > 0)
            {
                Persist();
            }
        }

        /// <summary>
        /// "Mandelbrot  x2.4e+09": what it is and how deep, which is what tells two apart. The name
        /// is stored, so it stays in the language it was saved in - it is the user's label now.
        /// </summary>
        private static string DescribeCurrent(FractalSession session, IStringCatalog strings)
        {
            var reference = session.DefaultView.scale.AsDouble;
            var scale = session.View.scale.AsDouble;
            var zoom = scale > 0d ? reference / scale : 1d;
            var depth = zoom < 1000d
                ? zoom.ToString("0.#", CultureInfo.InvariantCulture)
                : zoom.ToString("0.#e+00", CultureInfo.InvariantCulture);
            return strings.FractalName(session.Definition) + "  x" + depth;
        }

        private void Load()
        {
            items.Clear();
            if (StateCodec.TryFromJson<BookmarkListDto>(services.Storage?.Read(StorageKey), out var list) &&
                list.items != null)
            {
                foreach (var item in list.items)
                {
                    if (item?.state != null && !string.IsNullOrEmpty(item.id))
                    {
                        items.Add(item);
                    }
                }
            }

            Changed?.Invoke();
        }

        private void Persist()
        {
            services?.Storage?.Write(StorageKey, StateCodec.ToJson(new BookmarkListDto { items = items.ToArray() }));
            Changed?.Invoke();
        }

        [Serializable]
        private sealed class BookmarkListDto
        {
            public int version = 1;
            public Bookmark[] items;
        }
    }
}
