using System;
using System.Globalization;

namespace FractalVisio.Core
{
    /// <summary>
    /// Every string the user reads, by key. Code never holds a user-visible literal: it holds a
    /// key such as <c>settings.title</c>, and the active locale file says what that is in the
    /// user's language.
    ///
    /// Lookup is the whole contract, so anything in any layer can take one of these without
    /// learning where locales come from or how the language is chosen.
    /// </summary>
    public interface IStringCatalog
    {
        /// <summary>Code of the language strings are served in right now: "en", "ru".</summary>
        string Language { get; }

        /// <summary>
        /// The string for <paramref name="key"/> in the active language, falling back to the
        /// fallback language. False only when neither has it.
        /// </summary>
        bool TryGet(string key, out string value);

        /// <summary>
        /// As <see cref="TryGet"/>, but a missing key comes back as the key itself - visible on
        /// screen, so an untranslated string is noticed rather than silently blank - and is
        /// reported once in development builds.
        /// </summary>
        string Get(string key);
    }

    /// <summary>
    /// Lookups built on <see cref="IStringCatalog.TryGet"/>. The naming rules for keys that belong
    /// to content - a fractal, its parameters, a palette - live here and nowhere else.
    /// </summary>
    public static class StringCatalogExtensions
    {
        /// <summary>
        /// <see cref="string.Format(IFormatProvider, string, object[])"/> over the looked-up
        /// pattern, in the invariant culture: numbers read the same in every language, and a
        /// saved name never picks up a decimal comma.
        /// </summary>
        public static string Format(this IStringCatalog strings, string key, params object[] args)
        {
            var pattern = strings.Get(key);
            try
            {
                return string.Format(CultureInfo.InvariantCulture, pattern, args);
            }
            catch (FormatException)
            {
                // A translation with a broken placeholder should show its text, not throw from a
                // button handler.
                return pattern;
            }
        }

        /// <summary>
        /// The string for an optional key, or <paramref name="fallback"/>. For content that
        /// carries its own name - a new fractal works before anyone translates it.
        /// </summary>
        public static string GetOr(this IStringCatalog strings, string key, string fallback)
        {
            return strings != null && strings.TryGet(key, out var value) ? value : fallback;
        }

        /// <summary><c>fractal.&lt;id&gt;</c>, else the definition's own <see cref="IFractalDefinition.DisplayName"/>.</summary>
        public static string FractalName(this IStringCatalog strings, IFractalDefinition definition)
        {
            return definition == null ? string.Empty : strings.GetOr("fractal." + definition.Id, definition.DisplayName);
        }

        /// <summary><c>fractal.&lt;id&gt;.&lt;key&gt;</c>, else the descriptor's own label.</summary>
        public static string ParameterLabel(
            this IStringCatalog strings, IFractalDefinition definition, in FractalParameterDescriptor descriptor)
        {
            return definition == null
                ? descriptor.Label
                : strings.GetOr("fractal." + definition.Id + "." + descriptor.Key, descriptor.Label);
        }

        /// <summary>
        /// <c>palette.&lt;id&gt;</c>, else the palette's own name. Only built-ins have entries: a
        /// user palette's name is whatever the user called it, in whatever language they wrote it.
        /// </summary>
        public static string PaletteName(this IStringCatalog strings, PaletteData palette)
        {
            return palette == null ? string.Empty : strings.GetOr("palette." + palette.Id, palette.DisplayName);
        }
    }
}
