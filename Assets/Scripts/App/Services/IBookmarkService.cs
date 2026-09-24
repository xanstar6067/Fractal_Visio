using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>A saved picture the user can return to.</summary>
    [Serializable]
    public sealed class Bookmark
    {
        public string id;
        public string name;

        /// <summary>UTC ticks. A long rather than a DateTime: JsonUtility cannot write DateTime.</summary>
        public long createdTicks;

        public FractalStateDto state;
    }

    /// <summary>Bookmarks, newest first, and their previews. Offered by the bookmarks module.</summary>
    public interface IBookmarkService
    {
        IReadOnlyList<Bookmark> Items { get; }

        /// <summary>Raised after any add, remove, rename or restore. Not for previews - ask <see cref="GetPreview"/> again.</summary>
        event Action Changed;

        /// <summary>Save the session's current picture. Returns the new bookmark; its preview follows once the render has finished.</summary>
        Bookmark AddCurrent();

        /// <summary>Put a bookmark's picture back on screen.</summary>
        bool Open(Bookmark bookmark);

        /// <summary>Take a bookmark out of the list. Returns the position it had, for <see cref="Restore"/>, or -1.</summary>
        int Remove(string id);

        /// <summary>Put a removed bookmark back where it was - the "Undo" of a delete.</summary>
        void Restore(Bookmark bookmark, int index);

        void Rename(string id, string name);

        /// <summary>
        /// The bookmark's preview: the middle of the picture as it was saved, square. Null while there
        /// is none - a bookmark saved before previews existed gets one the first time it is opened.
        /// Cheap to ask every frame.
        /// </summary>
        Texture GetPreview(Bookmark bookmark);
    }
}
