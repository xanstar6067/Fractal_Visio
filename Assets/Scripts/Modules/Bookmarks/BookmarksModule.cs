using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using FractalVisio.App;
using FractalVisio.Core;

namespace FractalVisio.Modules
{
    /// <summary>
    /// Saved pictures. Owns the list, its storage document and the previews, and offers them to the
    /// UI as <see cref="IBookmarkService"/>; the bookmarks panel and the gallery's "Saved" are views
    /// of it.
    ///
    /// A preview is the middle of the picture, 256 px square, taken once the render has finished -
    /// the sharp frame rather than a coarse pass - and only while the view is still the bookmarked
    /// one. It is written as a PNG next to the bookmarks (<c>previews/&lt;id&gt;.png</c>) and kept as
    /// a texture for the session. A bookmark saved before previews existed gets one the first time
    /// it is opened, the same way. A removed bookmark's preview stays on disk until the next start,
    /// so that Undo has it back; the start sweeps out every preview no bookmark refers to.
    /// </summary>
    public sealed class BookmarksModule : IAppModule, IBookmarkService
    {
        private const string StorageKey = "bookmarks";
        private const string PreviewFolder = "previews";
        private const int PreviewSize = 256;

        /// <summary>Frames the renderer must stay idle before the capture - as for a screenshot.</summary>
        private const int IdleFramesRequired = 3;

        /// <summary>Take what is on screen after this long, rather than never.</summary>
        private const float CaptureTimeoutSeconds = 30f;

        private readonly List<Bookmark> items = new();
        private readonly Dictionary<string, Texture2D> previews = new();
        private readonly HashSet<string> missing = new();
        private readonly FrameGrab grab = new();
        private AppServices services;

        // A capture waiting for a finished frame of the view it is for, and the one being read back.
        private string captureId;
        private ViewState captureView;
        private float captureRequestedAt;
        private int idleFrames;
        private string grabbingId;

        public string Id => "bookmarks";

        public IReadOnlyList<Bookmark> Items => items;

        public event Action Changed;

        public void Initialize(AppServices context)
        {
            services = context;
            Load();
            SweepPreviews();
            services.Provide<IBookmarkService>(this);
        }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            if (grab.IsBusy)
            {
                if (grab.Tick())
                {
                    FinishCapture();
                }

                return;
            }

            if (captureId == null)
            {
                return;
            }

            // The user has moved on: the frame on screen is not the bookmark any more.
            if (!SameView(services.Session.View, captureView))
            {
                captureId = null;
                return;
            }

            var status = services.Render.Status;
            idleFrames = status.IsBusy || status.Interacting ? 0 : idleFrames + 1;
            if (idleFrames < IdleFramesRequired && Time.unscaledTime - captureRequestedAt < CaptureTimeoutSeconds)
            {
                return;
            }

            BeginCapture();
        }

        public void Shutdown()
        {
            grab.Dispose();
            foreach (var texture in previews.Values)
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }
            }

            previews.Clear();
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
            RequestCapture(bookmark.id);
            return bookmark;
        }

        public bool Open(Bookmark bookmark)
        {
            if (bookmark?.state == null || services == null ||
                !services.Session.Apply(bookmark.state, services.Catalog, services.Palettes))
            {
                return false;
            }

            if (GetPreview(bookmark) == null)
            {
                RequestCapture(bookmark.id);
            }

            return true;
        }

        public int Remove(string id)
        {
            var index = items.FindIndex(item => item.id == id);
            if (index < 0)
            {
                return -1;
            }

            items.RemoveAt(index);
            if (captureId == id)
            {
                captureId = null;
            }

            Persist();
            return index;
        }

        public void Restore(Bookmark bookmark, int index)
        {
            if (bookmark == null || string.IsNullOrEmpty(bookmark.id) || items.Exists(item => item.id == bookmark.id))
            {
                return;
            }

            items.Insert(Mathf.Clamp(index, 0, items.Count), bookmark);
            Persist();
        }

        public void Rename(string id, string name)
        {
            var bookmark = items.Find(item => item.id == id);
            var trimmed = name?.Trim();
            if (bookmark == null || string.IsNullOrEmpty(trimmed) || trimmed == bookmark.name)
            {
                return;
            }

            bookmark.name = trimmed;
            Persist();
        }

        public Texture GetPreview(Bookmark bookmark)
        {
            if (bookmark == null || string.IsNullOrEmpty(bookmark.id) || services == null)
            {
                return null;
            }

            if (previews.TryGetValue(bookmark.id, out var cached) && cached != null)
            {
                return cached;
            }

            if (missing.Contains(bookmark.id))
            {
                return null;
            }

            var bytes = services.Storage?.ReadBytes(PreviewKey(bookmark.id));
            var texture = bytes != null ? NewPreviewTexture(2, 2) : null;
            if (texture == null || !texture.LoadImage(bytes, true))
            {
                if (texture != null)
                {
                    UnityEngine.Object.Destroy(texture);
                }

                missing.Add(bookmark.id);
                return null;
            }

            previews[bookmark.id] = texture;
            return texture;
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

        private void RequestCapture(string id)
        {
            captureId = id;
            captureView = services.Session.View;
            captureRequestedAt = Time.unscaledTime;
            idleFrames = 0;
        }

        private void BeginCapture()
        {
            var id = captureId;
            captureId = null;

            var source = services.Backdrop?.Texture;
            if (source == null)
            {
                return;
            }

            var uv = FrameGrab.CentreSquare(services.Backdrop.UvRect, source.width, source.height);
            if (!grab.Begin(source, uv, PreviewSize, PreviewSize))
            {
                return;
            }

            grabbingId = id;
            if (!grab.IsBusy)
            {
                // A synchronous read back finished inside Begin.
                FinishCapture();
            }
        }

        private void FinishCapture()
        {
            var id = grabbingId;
            grabbingId = null;
            var pixels = grab.Pixels;
            if (id == null || pixels == null || services == null)
            {
                return;
            }

            var texture = NewPreviewTexture(PreviewSize, PreviewSize);
            texture.LoadRawTextureData(pixels);
            texture.Apply(false, true);
            if (previews.TryGetValue(id, out var old) && old != null)
            {
                UnityEngine.Object.Destroy(old);
            }

            previews[id] = texture;
            missing.Remove(id);

            // PNG off the main thread; the texture above is what the interface shows meanwhile.
            var storage = services.Storage;
            var key = PreviewKey(id);
            var format = QualitySettings.activeColorSpace == ColorSpace.Linear
                ? GraphicsFormat.R8G8B8A8_SRGB
                : GraphicsFormat.R8G8B8A8_UNorm;
            Task.Run(() =>
            {
                var png = ImageConversion.EncodeArrayToPNG(pixels, format, PreviewSize, PreviewSize);
                storage?.WriteBytes(key, png);
            });
        }

        /// <summary>Delete previews no bookmark refers to - left by a removal that was not undone.</summary>
        private void SweepPreviews()
        {
            var storage = services.Storage;
            if (storage == null)
            {
                return;
            }

            var known = new HashSet<string>();
            foreach (var item in items)
            {
                known.Add(item.id);
            }

            foreach (var key in storage.ListBytes(PreviewFolder))
            {
                if (!known.Contains(Path.GetFileNameWithoutExtension(key)))
                {
                    storage.DeleteBytes(key);
                }
            }
        }

        private static string PreviewKey(string id) => PreviewFolder + "/" + id + ".png";

        /// <summary>sRGB, like the pixels read back from the screen and the PNG written from them.</summary>
        private static Texture2D NewPreviewTexture(int width, int height)
        {
            return new Texture2D(width, height, TextureFormat.RGBA32, false, false)
            {
                name = "Bookmark preview",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
        }

        private static bool SameView(in ViewState a, in ViewState b)
        {
            return a.x.Equals(b.x) && a.y.Equals(b.y) && a.scale.Equals(b.scale) && a.rotation == b.rotation;
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
