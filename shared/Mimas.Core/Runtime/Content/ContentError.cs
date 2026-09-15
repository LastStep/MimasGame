using System;
using System.Collections.Generic;
using System.Text;

namespace Mimas.Core.Content
{
    /// <summary>One problem found while loading content, always tied to the file it came from.</summary>
    public readonly struct ContentError
    {
        public readonly string File;
        public readonly string Message;

        public ContentError(string file, string message)
        {
            File = file ?? "<catalogue>";
            Message = message ?? "unknown error";
        }

        public override string ToString() => File + ": " + Message;
    }

    /// <summary>
    /// Thrown once by <see cref="ContentCatalog.Load"/> with <em>every</em> error found, so an author fixes
    /// a whole folder in one pass instead of one file per run.
    /// </summary>
    public sealed class ContentLoadException : Exception
    {
        public IReadOnlyList<ContentError> Errors { get; }

        public ContentLoadException(IReadOnlyList<ContentError> errors)
            : base(Describe(errors))
        {
            Errors = errors ?? new List<ContentError>();
        }

        private static string Describe(IReadOnlyList<ContentError> errors)
        {
            if (errors == null || errors.Count == 0) return "Content failed to load.";
            var sb = new StringBuilder();
            sb.Append("Content failed to load with ").Append(errors.Count).Append(errors.Count == 1 ? " error:" : " errors:");
            for (int i = 0; i < errors.Count; i++) sb.Append('\n').Append("  ").Append(errors[i]);
            return sb.ToString();
        }
    }
}
