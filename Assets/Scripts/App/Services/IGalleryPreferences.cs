using System;
using System.Collections.Generic;

namespace FractalVisio.App
{
    /// <summary>
    /// What the user has marked and opened in the gallery: favourites and the recent list, both by
    /// fractal id. Offered by the gallery module; the gallery screen is only a view of it.
    /// </summary>
    public interface IGalleryPreferences
    {
        /// <summary>Raised after a favourite is toggled or the recent list changes.</summary>
        event Action Changed;

        bool IsFavorite(string id);

        void SetFavorite(string id, bool favorite);

        /// <summary>Fractal ids, most recently opened first.</summary>
        IReadOnlyList<string> Recent { get; }
    }
}
