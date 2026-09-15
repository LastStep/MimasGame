using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Content
{
    /// <summary>
    /// A fingerprint of a content set that is independent of formatting, key order, line endings and file
    /// order: SHA-256 over each file's path plus its canonical JSON (object keys sorted ordinally, no
    /// whitespace), files sorted by path. Client and server compare this before a match so they can never
    /// play on different balance data. Bit-identical across .NET and IL2CPP because only integers and
    /// strings are involved.
    /// </summary>
    public static class ContentHash
    {
        public static string Compute(IEnumerable<ContentFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var sorted = new List<ContentFile>(files);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                for (int i = 0; i < sorted.Count; i++)
                {
                    sb.Clear();
                    sb.Append(sorted[i].Path).Append('\n');
                    AppendCanonical(sb, sorted[i].Text);
                    sb.Append('\n');
                    byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
                }
                sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                return ToHex(sha.Hash);
            }
        }

        /// <summary>Canonical form of one JSON document. Unparseable text is hashed verbatim so it still contributes.</summary>
        public static string Canonical(string json)
        {
            var sb = new StringBuilder();
            AppendCanonical(sb, json);
            return sb.ToString();
        }

        private static void AppendCanonical(StringBuilder sb, string json)
        {
            JToken token;
            try
            {
                token = JToken.Parse(json ?? string.Empty);
            }
            catch (Exception)
            {
                sb.Append(json ?? string.Empty);
                return;
            }
            WriteCanonical(sb, token);
        }

        private static void WriteCanonical(StringBuilder sb, JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Object:
                {
                    var properties = new List<JProperty>(((JObject)token).Properties());
                    properties.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
                    sb.Append('{');
                    for (int i = 0; i < properties.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append(JsonString(properties[i].Name)).Append(':');
                        WriteCanonical(sb, properties[i].Value);
                    }
                    sb.Append('}');
                    return;
                }
                case JTokenType.Array:
                {
                    sb.Append('[');
                    int i = 0;
                    foreach (var item in (JArray)token)
                    {
                        if (i++ > 0) sb.Append(',');
                        WriteCanonical(sb, item);
                    }
                    sb.Append(']');
                    return;
                }
                case JTokenType.String:
                    sb.Append(JsonString((string)token));
                    return;
                case JTokenType.Integer:
                    sb.Append(((long)token).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case JTokenType.Float:
                    sb.Append(((double)token).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                    return;
                case JTokenType.Boolean:
                    sb.Append((bool)token ? "true" : "false");
                    return;
                case JTokenType.Null:
                case JTokenType.Undefined:
                    sb.Append("null");
                    return;
                default:
                    sb.Append(JsonString(token.ToString()));
                    return;
            }
        }

        private static string JsonString(string value)
        {
            var sb = new StringBuilder(value.Length + 2);
            sb.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        private static string ToHex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }
    }
}
