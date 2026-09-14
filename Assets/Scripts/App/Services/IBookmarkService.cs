using System;
using System.Collections.Generic;
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

    /// <summary>Bookmarks, newest first. Offered by the bookmarks module.</summary>
    public interface IBookmarkService
    {
        IReadOnlyList<Bookmark> Items { get; }

        /// <summary>Raised after any add, remove or rename.</summary>
        event Action Changed;

        /// <summary>Save the session's current picture. Returns the new bookmark.</summary>
        Bookmark AddCurrent();

        /// <summary>Put a bookmark's picture back on screen.</summary>
        bool Open(Bookmark bookmark);

        void Remove(string id);
    }
}
