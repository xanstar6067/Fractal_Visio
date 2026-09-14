using System;
using System.Collections.Generic;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// Every palette the user can pick: the built-in ones from <see cref="PaletteLibrary"/>, then the
    /// ones they made in the palette editor. The built-ins are read-only; user palettes are saved as
    /// their stops in one storage document.
    ///
    /// No ScriptableObject behind either: built-ins are code for the same reason the fractal
    /// catalog is, and user palettes are made on the phone, where there is no asset database to put
    /// them in. <see cref="PaletteData"/> is the one type both halves share.
    /// </summary>
    public sealed class PaletteCatalog
    {
        private const string StorageKey = "palettes";
        private const string UserIdPrefix = "user-";

        private readonly IAppStorage storage;
        private readonly List<PaletteData> user = new();
        private readonly List<PaletteData> all = new();

        public PaletteCatalog(IAppStorage storage)
        {
            this.storage = storage;
            Load();
        }

        /// <summary>Raised after a user palette is added, replaced or removed.</summary>
        public event Action Changed;

        /// <summary>Built-ins first, then user palettes in creation order.</summary>
        public IReadOnlyList<PaletteData> All => all;

        public bool IsUserPalette(PaletteData palette) => palette != null && user.Contains(palette);

        public PaletteData Find(string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                return null;
            }

            for (var i = 0; i < all.Count; i++)
            {
                if (all[i].Id == id)
                {
                    return all[i];
                }
            }

            return null;
        }

        public int IndexOf(PaletteData palette)
        {
            // By id rather than by reference: an edited palette is a new instance with the same id.
            return palette == null ? -1 : all.FindIndex(candidate => candidate.Id == palette.Id);
        }

        /// <summary>A fresh id for a palette about to be created.</summary>
        public string NewUserId()
        {
            return UserIdPrefix + DateTime.UtcNow.Ticks.ToString("x");
        }

        /// <summary>Add a user palette, or replace the one with the same id. Built-ins cannot be replaced.</summary>
        public void Save(PaletteData palette)
        {
            if (palette == null || PaletteLibrary.Find(palette.Id) != null)
            {
                return;
            }

            var index = user.FindIndex(candidate => candidate.Id == palette.Id);
            if (index >= 0)
            {
                user[index] = palette;
            }
            else
            {
                user.Add(palette);
            }

            Rebuild();
            Persist();
        }

        public void Remove(string id)
        {
            if (user.RemoveAll(candidate => candidate.Id == id) == 0)
            {
                return;
            }

            Rebuild();
            Persist();
        }

        private void Load()
        {
            user.Clear();
            if (StateCodec.TryFromJson<PaletteListDto>(storage?.Read(StorageKey), out var list) && list.palettes != null)
            {
                foreach (var dto in list.palettes)
                {
                    var palette = StateCodec.FromDto(dto);
                    if (palette != null && PaletteLibrary.Find(palette.Id) == null)
                    {
                        user.Add(palette);
                    }
                }
            }

            Rebuild();
        }

        private void Rebuild()
        {
            all.Clear();
            all.AddRange(PaletteLibrary.All);
            all.AddRange(user);
            Changed?.Invoke();
        }

        private void Persist()
        {
            if (storage == null)
            {
                return;
            }

            var list = new PaletteListDto { palettes = new PaletteDto[user.Count] };
            for (var i = 0; i < user.Count; i++)
            {
                list.palettes[i] = StateCodec.ToDto(user[i]);
            }

            storage.Write(StorageKey, StateCodec.ToJson(list));
        }

        [Serializable]
        private sealed class PaletteListDto
        {
            public PaletteDto[] palettes;
        }
    }
}
