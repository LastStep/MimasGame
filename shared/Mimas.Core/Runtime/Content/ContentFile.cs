using System;
using System.Collections.Generic;
using System.IO;

namespace Mimas.Core.Content
{
    /// <summary>
    /// One data file handed to the catalogue: its path relative to the data root (forward slashes, e.g.
    /// <c>abilities/jump.json</c>) and its text. Where the bytes came from (disk, a Unity TextAsset, a test
    /// string) is the caller's business; the catalogue only ever sees these pairs, which is what makes
    /// server and client parity possible by construction.
    /// </summary>
    public readonly struct ContentFile
    {
        public readonly string Path;
        public readonly string Text;

        public ContentFile(string path, string text)
        {
            Path = NormalizePath(path);
            Text = text ?? string.Empty;
        }

        /// <summary>Forward slashes, no leading <c>./</c> or <c>/</c>, trimmed.</summary>
        public static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Content path must not be empty.", nameof(path));
            string p = path.Trim().Replace('\\', '/');
            while (p.StartsWith("./", StringComparison.Ordinal)) p = p.Substring(2);
            while (p.StartsWith("/", StringComparison.Ordinal)) p = p.Substring(1);
            return p;
        }

        public override string ToString() => Path;
    }

    /// <summary>Helpers that turn real sources into <see cref="ContentFile"/> lists.</summary>
    public static class ContentFiles
    {
        /// <summary>
        /// Every <c>*.json</c> under <paramref name="root"/>, recursively, with paths relative to it. Used by
        /// the server and the tests; the Unity client reads from a generated manifest instead. Order does
        /// not matter: the catalogue sorts.
        /// </summary>
        public static List<ContentFile> FromDirectory(string root)
        {
            if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("Root must not be empty.", nameof(root));
            string full = System.IO.Path.GetFullPath(root);
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException("Content root not found: " + full);

            var files = new List<ContentFile>();
            foreach (string file in Directory.GetFiles(full, "*.json", SearchOption.AllDirectories))
            {
                string relative = file.Substring(full.Length).TrimStart('\\', '/');
                files.Add(new ContentFile(relative, File.ReadAllText(file)));
            }
            return files;
        }
    }
}
