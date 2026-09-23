using System;
using System.Collections.Generic;
using UnityEngine;
using FractalVisio.Core;

namespace FractalVisio.App
{
    /// <summary>
    /// The app's <see cref="IStringCatalog"/>: every locale file under
    /// <c>Resources/Localization</c>, and the one of them the session's language setting picks.
    ///
    /// Adding a language is one file, <c>Resources/Localization/&lt;code&gt;.txt</c>, with a
    /// <see cref="LocaleTable.NameKey"/> entry. It appears in the LANGUAGE setting by itself; a
    /// key it lacks falls back to English, and development builds list what is missing at start.
    ///
    /// The language follows <see cref="InterfaceSettings.Language"/>: empty means "the device's
    /// language, if there is a file for it". Anyone who shows text rebuilds when
    /// <see cref="Changed"/> fires - strings are read when a UI is built, never cached across it.
    /// </summary>
    public sealed class Localizer : IStringCatalog
    {
        /// <summary>The language every key must exist in, and what any other one falls back to.</summary>
        public const string FallbackLanguage = "en";

        /// <summary>Folder under a <c>Resources</c> directory that holds the locale files.</summary>
        public const string ResourcesFolder = "Localization";

        private readonly List<LocaleTable> locales = new();
        private readonly HashSet<string> reportedMissing = new(StringComparer.Ordinal);
        private readonly LocaleTable fallback;
        private readonly FractalSession session;
        private LocaleTable active;

        public Localizer(FractalSession session, IEnumerable<LocaleTable> tables)
        {
            this.session = session;

            if (tables != null)
            {
                foreach (var table in tables)
                {
                    if (table != null && Find(table.Code) == null)
                    {
                        locales.Add(table);
                    }
                }
            }

            fallback = Find(FallbackLanguage);
            if (fallback == null)
            {
                // Without the English file every key would print as itself. Keep going - a
                // readable key beats a crash - but say so loudly.
                Debug.LogError($"Localizer: no '{FallbackLanguage}' locale in Resources/{ResourcesFolder}.");
                fallback = LocaleTable.Parse(FallbackLanguage, string.Empty);
                locales.Add(fallback);
            }

            // Fallback first, then the rest by code: a stable order for the language picker.
            locales.Sort((a, b) =>
                ReferenceEquals(a, fallback) ? -1 :
                ReferenceEquals(b, fallback) ? 1 :
                string.CompareOrdinal(a.Code, b.Code));

            if (Debug.isDebugBuild)
            {
                ReportIncompleteLocales();
            }

            active = Resolve(session != null ? session.Interface.Language : string.Empty);
            if (session != null)
            {
                session.Changed += OnSessionChanged;
            }
        }

        /// <summary>Raised after the language in use changes.</summary>
        public event Action Changed;

        /// <summary>Every available language, English first.</summary>
        public IReadOnlyList<LocaleTable> Languages => locales;

        public string Language => active.Code;

        /// <summary>Every <c>.txt</c> in <c>Resources/Localization</c>, the file name being the language code.</summary>
        public static Localizer LoadFromResources(FractalSession session)
        {
            var assets = Resources.LoadAll<TextAsset>(ResourcesFolder);
            var tables = new List<LocaleTable>(assets.Length);
            for (var i = 0; i < assets.Length; i++)
            {
                tables.Add(LocaleTable.Parse(assets[i].name, assets[i].text));
            }

            return new Localizer(session, tables);
        }

        public bool TryGet(string key, out string value)
        {
            return active.TryGet(key, out value) || fallback.TryGet(key, out value);
        }

        public string Get(string key)
        {
            if (TryGet(key, out var value))
            {
                return value;
            }

            if (Debug.isDebugBuild && key != null && reportedMissing.Add(key))
            {
                Debug.LogWarning($"Localizer: no string for '{key}' in any locale.");
            }

            return key ?? string.Empty;
        }

        /// <summary>
        /// The locale a setting value selects. Empty follows the device; an unknown code, or a
        /// device language without a file, lands on the fallback.
        /// </summary>
        public LocaleTable Resolve(string requested)
        {
            var code = string.IsNullOrEmpty(requested) ? SystemLanguageCode() : requested;
            return Find(code) ?? fallback;
        }

        /// <summary>Code of the device language, as locale files are named. Null for one this map does not know.</summary>
        public static string SystemLanguageCode()
        {
            return Application.systemLanguage switch
            {
                SystemLanguage.English => "en",
                SystemLanguage.Russian => "ru",
                SystemLanguage.Ukrainian => "uk",
                SystemLanguage.Belarusian => "be",
                SystemLanguage.German => "de",
                SystemLanguage.French => "fr",
                SystemLanguage.Spanish => "es",
                SystemLanguage.Italian => "it",
                SystemLanguage.Portuguese => "pt",
                SystemLanguage.Polish => "pl",
                SystemLanguage.Czech => "cs",
                SystemLanguage.Dutch => "nl",
                SystemLanguage.Turkish => "tr",
                SystemLanguage.Japanese => "ja",
                SystemLanguage.Korean => "ko",
                SystemLanguage.Chinese => "zh",
                SystemLanguage.ChineseSimplified => "zh",
                SystemLanguage.ChineseTraditional => "zh-hant",
                _ => null
            };
        }

        private LocaleTable Find(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return null;
            }

            for (var i = 0; i < locales.Count; i++)
            {
                if (string.Equals(locales[i].Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return locales[i];
                }
            }

            return null;
        }

        private void OnSessionChanged(SessionChange change)
        {
            if ((change & SessionChange.Interface) == 0)
            {
                return;
            }

            var next = Resolve(session.Interface.Language);
            if (ReferenceEquals(next, active))
            {
                return;
            }

            active = next;
            Changed?.Invoke();
        }

        /// <summary>
        /// One line per locale that is missing keys English has, or still has keys English
        /// dropped. The first is what a translator has to do next; the second is dead text.
        /// </summary>
        private void ReportIncompleteLocales()
        {
            for (var i = 0; i < locales.Count; i++)
            {
                var locale = locales[i];
                if (ReferenceEquals(locale, fallback))
                {
                    continue;
                }

                var missing = new List<string>();
                foreach (var key in fallback.Keys)
                {
                    if (!locale.Contains(key))
                    {
                        missing.Add(key);
                    }
                }

                var stale = new List<string>();
                foreach (var key in locale.Keys)
                {
                    if (!fallback.Contains(key))
                    {
                        stale.Add(key);
                    }
                }

                if (missing.Count > 0)
                {
                    Debug.LogWarning($"Localizer: '{locale.Code}' lacks {missing.Count} key(s), shown in English: {string.Join(", ", missing)}");
                }

                if (stale.Count > 0)
                {
                    Debug.LogWarning($"Localizer: '{locale.Code}' has {stale.Count} key(s) English does not: {string.Join(", ", stale)}");
                }
            }
        }
    }
}
