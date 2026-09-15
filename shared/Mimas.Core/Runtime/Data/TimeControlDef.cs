using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>A chess-style clock preset from <c>timecontrols.json</c>. Milliseconds, all integers.</summary>
    public sealed class TimeControlDef : IContentDef
    {
        public string Id { get; }
        public string Name { get; }
        public int BaseMs { get; }
        public int IncrementMs { get; }
        public int TurnCapMs { get; }

        public TimeControlDef(string id, string name, int baseMs, int incrementMs, int turnCapMs)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            if (baseMs <= 0) throw new ArgumentOutOfRangeException(nameof(baseMs));
            if (incrementMs < 0) throw new ArgumentOutOfRangeException(nameof(incrementMs));
            if (turnCapMs <= 0) throw new ArgumentOutOfRangeException(nameof(turnCapMs));
            BaseMs = baseMs;
            IncrementMs = incrementMs;
            TurnCapMs = turnCapMs;
        }

        /// <summary>Parses the whole <c>timecontrols.json</c> file (one file, many presets).</summary>
        public static List<TimeControlDef> ListFromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Time control JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Time control JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "timecontrols");
            if (version != 1) throw new MapLoadException($"Unsupported timecontrols version {version} (expected 1).");

            if (!(root["timeControls"] is JArray array))
                throw new MapLoadException("Time control JSON is missing the 'timeControls' array.");

            var list = new List<TimeControlDef>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject entry))
                    throw new MapLoadException($"timeControls[{i}] is not an object.");
                string where = $"timeControls[{i}]";
                string id = MapJson.RequireString(entry, "id", where);
                if (!seen.Add(id)) throw new MapLoadException($"Duplicate time control id '{id}'.");
                string name = MapJson.OptionalString(entry, "name", where) ?? id;
                int baseMs = MapJson.RequireInt(entry, "baseMs", where);
                int incrementMs = MapJson.OptionalInt(entry, "incrementMs", 0, where);
                int turnCapMs = MapJson.RequireInt(entry, "turnCapMs", where);
                if (baseMs <= 0) throw new MapLoadException($"{where} ('{id}') baseMs must be positive.");
                if (incrementMs < 0) throw new MapLoadException($"{where} ('{id}') incrementMs must not be negative.");
                if (turnCapMs <= 0) throw new MapLoadException($"{where} ('{id}') turnCapMs must be positive.");
                list.Add(new TimeControlDef(id, name, baseMs, incrementMs, turnCapMs));
            }
            return list;
        }
    }
}
