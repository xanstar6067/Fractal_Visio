using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;
using FractalVisio.Rendering;

namespace FractalVisio.App
{
    /// <summary>
    /// Draws the gallery's preview tiles with the same GPU path the explorer uses, one small render
    /// texture per fractal, in the session's palette.
    ///
    /// Previews are drawn a couple per frame, and only those the gallery asked for in the last
    /// second: opening the gallery never costs a long frame, and a closed gallery costs nothing. A
    /// palette change marks every preview stale and they redraw in place, so the gallery follows
    /// the user's colours without being rebuilt.
    ///
    /// It lives in App rather than in Modules because it needs a renderer, and no module is handed
    /// one. It is still an <see cref="IAppModule"/> so the bootstrap ticks it like the rest.
    /// </summary>
    public sealed class ThumbnailModule : IAppModule, IFractalThumbnails
    {
        private const int MinimumSize = 64;
        private const int MaximumSize = 512;

        /// <summary>Sizes are rounded up to this, so a card a few pixels wider does not redraw.</summary>
        private const int SizeStep = 64;

        private const int DrawsPerFrame = 2;

        /// <summary>A preview asked for within this long counts as on screen; others wait.</summary>
        private const float WantedSeconds = 1f;

        private readonly Dictionary<string, Slot> slots = new();
        private AppServices services;
        private FractalGpuRenderer renderer;
        private bool coloringDirty = true;

        public string Id => "thumbnails";

        public void Initialize(AppServices context)
        {
            services = context;
            renderer = new FractalGpuRenderer();
            services.Session.Changed += OnSessionChanged;
            services.Provide<IFractalThumbnails>(this);
        }

        public int ColoringVersion { get; private set; }

        public void Tick()
        {
            if (services == null)
            {
                return;
            }

            ApplyColoring();

            var now = Time.unscaledTime;
            var budget = DrawsPerFrame;
            foreach (var slot in slots.Values)
            {
                if (budget == 0)
                {
                    break;
                }

                if (!slot.Supported || now - slot.RequestedAt > WantedSeconds)
                {
                    continue;
                }

                // A render texture's contents can be lost with the graphics context (an Android app
                // sent to the background), which reads as a black tile: redraw it then too.
                var current = slot.Texture != null && slot.Texture.IsCreated() && slot.Texture.width == slot.Size;
                if (current && !slot.Stale)
                {
                    continue;
                }

                Draw(slot);
                budget--;
            }
        }

        public void Shutdown()
        {
            if (services != null)
            {
                services.Session.Changed -= OnSessionChanged;
            }

            foreach (var slot in slots.Values)
            {
                Release(slot);
            }

            slots.Clear();
            renderer?.Dispose();
            renderer = null;
            services = null;
        }

        public Texture Get(in CatalogEntry entry, int size)
        {
            if (renderer == null || entry.Definition == null)
            {
                return null;
            }

            if (!slots.TryGetValue(entry.Id, out var slot))
            {
                slot = new Slot { Entry = entry, Stale = true, Supported = true };
                slots.Add(entry.Id, slot);
            }

            slot.RequestedAt = Time.unscaledTime;
            slot.Size = Quantize(size);

            // Until a redraw at a new size lands, the old picture is a better answer than nothing.
            return slot.Supported && slot.Texture != null && slot.Texture.IsCreated() ? slot.Texture : null;
        }

        public bool Draw(IFractalDefinition definition, in FractalParameterSet parameters, in ViewState view, RenderTexture target)
        {
            if (renderer == null || definition == null || target == null || !renderer.Supports(definition))
            {
                return false;
            }

            // Asked between two ticks, right after a palette change: draw in the new one.
            ApplyColoring();

            var framed = view;
            framed.iterations = services.Session.IterationBudget(view.scale.AsDouble);
            renderer.Render(definition, parameters, framed, framed.iterations, target);
            return true;
        }

        private void OnSessionChanged(SessionChange change)
        {
            if ((change & (SessionChange.Palette | SessionChange.Coloring)) != 0)
            {
                coloringDirty = true;
            }
        }

        private void ApplyColoring()
        {
            if (!coloringDirty)
            {
                return;
            }

            coloringDirty = false;
            ColoringVersion++;
            renderer.SetColoring(services.Session.Palette, services.Session.Coloring);
            foreach (var slot in slots.Values)
            {
                slot.Stale = true;
            }
        }

        private void Draw(Slot slot)
        {
            var definition = slot.Entry.Definition;
            if (!renderer.Supports(definition))
            {
                // No GPU path for it here: the gallery shows the card without a picture.
                slot.Supported = false;
                Release(slot);
                return;
            }

            if (slot.Texture == null || slot.Texture.width != slot.Size || !slot.Texture.IsCreated())
            {
                Release(slot);
                slot.Texture = CreateTarget(slot.Size, slot.Entry.Id);
            }

            var view = slot.Entry.PreviewView;
            view.iterations = services.Session.IterationBudget(view.scale.AsDouble);
            renderer.Render(
                definition,
                FractalParameterSet.Defaults(definition.Parameters),
                view,
                view.iterations,
                slot.Texture);
            slot.Stale = false;
        }

        private static int Quantize(int size)
        {
            var clamped = Mathf.Clamp(size, MinimumSize, MaximumSize);
            return Mathf.Min(MaximumSize, (clamped + SizeStep - 1) / SizeStep * SizeStep);
        }

        private static RenderTexture CreateTarget(int size, string id)
        {
            var texture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32)
            {
                name = "Thumbnail " + id,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            texture.Create();
            return texture;
        }

        private static void Release(Slot slot)
        {
            if (slot.Texture == null)
            {
                return;
            }

            slot.Texture.Release();
            Object.Destroy(slot.Texture);
            slot.Texture = null;
        }

        private sealed class Slot
        {
            public CatalogEntry Entry;
            public RenderTexture Texture;
            public int Size;
            public bool Stale;
            public bool Supported;
            public float RequestedAt;
        }
    }
}
