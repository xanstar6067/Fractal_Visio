using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Named text documents that survive a restart, and binary files beside them. Everything the app
    /// saves - the last session, bookmarks, user palettes, bookmark previews - is here, so a test or
    /// a future cloud sync replaces one class.
    /// </summary>
    public interface IAppStorage
    {
        /// <summary>The document's text, or null if there is none.</summary>
        string Read(string key);

        void Write(string key, string text);

        void Delete(string key);

        /// <summary>
        /// A binary file, by a key that is a relative path with its extension - "previews/bm-1.png".
        /// Null if there is none. Safe to call from a worker thread for distinct keys.
        /// </summary>
        byte[] ReadBytes(string key);

        void WriteBytes(string key, byte[] bytes);

        void DeleteBytes(string key);

        /// <summary>Keys of the binary files in one folder ("previews"), for sweeping out orphans.</summary>
        IReadOnlyList<string> ListBytes(string folder);

        /// <summary>Folder for files the user takes out of the app, such as saved images.</summary>
        string ExportDirectory { get; }
    }

    /// <summary>
    /// <see cref="IAppStorage"/> over <see cref="Application.persistentDataPath"/>. Writes go to a
    /// temporary file that then replaces the old one, so an app killed mid-write - which on
    /// Android is an ordinary way for an app to stop - leaves the previous document intact rather
    /// than half of the new one.
    /// </summary>
    public sealed class FileAppStorage : IAppStorage
    {
        private readonly string root;

        public FileAppStorage(string rootDirectory)
        {
            root = rootDirectory;
            ExportDirectory = Path.Combine(rootDirectory, "Images");
        }

        public static FileAppStorage CreateDefault() => new(Application.persistentDataPath);

        public string ExportDirectory { get; }

        public string Read(string key)
        {
            var path = PathFor(key);
            try
            {
                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not read '{key}': {exception.Message}");
                return null;
            }
        }

        public void Write(string key, string text)
        {
            var path = PathFor(key);
            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(temporary, text ?? string.Empty);
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not write '{key}': {exception.Message}");
            }
        }

        public void Delete(string key)
        {
            var path = PathFor(key);
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not delete '{key}': {exception.Message}");
            }
        }

        public byte[] ReadBytes(string key)
        {
            var path = BinaryPathFor(key);
            try
            {
                return path != null && File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not read '{key}': {exception.Message}");
                return null;
            }
        }

        public void WriteBytes(string key, byte[] bytes)
        {
            var path = BinaryPathFor(key);
            if (path == null)
            {
                return;
            }

            // Unique temporary name: two writers of different keys may run at once on workers.
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? root);
                File.WriteAllBytes(temporary, bytes ?? Array.Empty<byte>());
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(temporary, path);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not write '{key}': {exception.Message}");
                TryDelete(temporary);
            }
        }

        public void DeleteBytes(string key)
        {
            var path = BinaryPathFor(key);
            if (path != null)
            {
                TryDelete(path);
            }
        }

        public IReadOnlyList<string> ListBytes(string folder)
        {
            var keys = new List<string>();
            var directory = BinaryPathFor(folder);
            try
            {
                if (directory != null && Directory.Exists(directory))
                {
                    foreach (var path in Directory.GetFiles(directory))
                    {
                        if (!path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                        {
                            keys.Add(folder.TrimEnd('/') + "/" + Path.GetFileName(path));
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not list '{folder}': {exception.Message}");
            }

            return keys;
        }

        private string PathFor(string key) => Path.Combine(root, key + ".json");

        /// <summary>A binary key as a path under the root, or null for one that would leave it.</summary>
        private string BinaryPathFor(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Contains("..") || Path.IsPathRooted(key))
            {
                return null;
            }

            return Path.Combine(root, key.Replace('/', Path.DirectorySeparatorChar));
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.LogWarning($"Could not delete '{path}': {exception.Message}");
            }
        }
    }
}
