using System;
using System.Collections.Generic;
using System.Text;

namespace FractalVisio.Core
{
    /// <summary>
    /// One language's strings, parsed from a locale file.
    ///
    /// The format is deliberately the plainest there is, so a translator needs a text editor and
    /// nothing else:
    /// <code>
    /// # comment
    /// language.name = Русский
    /// settings.title = Настройки
    /// screenshot.saved_to = Сохранено в {0}
    /// </code>
    /// One entry per line, split at the first <c>=</c>, both sides trimmed. <c>\n</c> in a value is
    /// a line break and <c>\\</c> a backslash. Not JSON: JsonUtility cannot read a dictionary, and
    /// an array of key/value objects is three times the text for a translator to get wrong.
    /// </summary>
    public sealed class LocaleTable
    {
        /// <summary>The entry every locale file must have: the language's name in itself.</summary>
        public const string NameKey = "language.name";

        private readonly Dictionary<string, string> entries;

        private LocaleTable(string code, Dictionary<string, string> entries)
        {
            Code = code;
            this.entries = entries;
            NativeName = entries.TryGetValue(NameKey, out var name) && name.Length > 0 ? name : code;
        }

        /// <summary>Language code, from the file name: "en", "ru".</summary>
        public string Code { get; }

        /// <summary>"English", "Русский" - how the language picker lists it.</summary>
        public string NativeName { get; }

        public int Count => entries.Count;

        public IEnumerable<string> Keys => entries.Keys;

        public bool TryGet(string key, out string value)
        {
            if (key != null && entries.TryGetValue(key, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        public bool Contains(string key) => key != null && entries.ContainsKey(key);

        public static LocaleTable Parse(string code, string text)
        {
            if (string.IsNullOrEmpty(code))
            {
                throw new ArgumentException("A locale needs a code.", nameof(code));
            }

            var entries = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(text))
            {
                return new LocaleTable(code, entries);
            }

            var start = 0;
            while (start < text.Length)
            {
                var end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    end = text.Length;
                }

                ParseLine(text, start, end, entries);
                start = end + 1;
            }

            return new LocaleTable(code, entries);
        }

        private static void ParseLine(string text, int start, int end, Dictionary<string, string> entries)
        {
            var line = text.Substring(start, end - start).Trim();

            // A BOM survives on the first line when the file was saved by an editor that writes one.
            if (line.Length > 0 && line[0] == '﻿')
            {
                line = line.Substring(1).TrimStart();
            }

            if (line.Length == 0 || line[0] == '#')
            {
                return;
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                return;
            }

            var key = line.Substring(0, split).Trim();
            if (key.Length == 0)
            {
                return;
            }

            entries[key] = Unescape(line.Substring(split + 1).Trim());
        }

        private static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                var next = value[++i];
                switch (next)
                {
                    case 'n':
                        builder.Append('\n');
                        break;
                    case '\\':
                        builder.Append('\\');
                        break;
                    default:
                        // Unknown escape: keep it as written rather than eat a character.
                        builder.Append('\\').Append(next);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
