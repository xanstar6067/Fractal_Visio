using System;
using System.IO;
using UnityEngine;

namespace FractalVisio.App
{
    /// <summary>
    /// Named text documents that survive a restart. Everything the app saves - the last session,
    /// bookmarks, user palettes - is one document here, so a test or a future cloud sync replaces
    /// one class.
    /// </summary>
    public interface IAppStorage
    {
        /// <summary>The document's text, or null if there is none.</summary>
        string Read(string key);

        void Write(string key, string text);

        void Delete(string key);

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

        private string PathFor(string key) => Path.Combine(root, key + ".json");
    }
}
