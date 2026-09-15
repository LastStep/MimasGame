using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// A playable class from <c>classes/*.json</c>: what a fresh unit of this class starts with. Only the
    /// ability list exists so far; base stats join when the unit model gets stats. Ability ids are strings
    /// resolved against the catalogue at link time, so a class file can never reference a missing ability.
    /// </summary>
    public sealed class ClassDef : IContentDef
    {
        private readonly List<string> _abilityIds;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Starting ability ids in authored order, unique.</summary>
        public IReadOnlyList<string> AbilityIds => _abilityIds;

        public ClassDef(string id, string name, string description, List<string> abilityIds)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Description = description;
            _abilityIds = abilityIds != null ? new List<string>(abilityIds) : new List<string>();
        }

        public static ClassDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Class JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Class JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "class");
            if (version != 1) throw new MapLoadException($"Unsupported class version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "class");
            string where = $"class '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);

            if (!(MapJson.Require(root, "abilities", where) is JArray array))
                throw new MapLoadException($"{where} field 'abilities' must be an array of ability ids.");

            var ids = new List<string>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String || string.IsNullOrEmpty((string)array[i]))
                    throw new MapLoadException($"{where} abilities[{i}] must be a non-empty string.");
                string abilityId = (string)array[i];
                if (!seen.Add(abilityId))
                    throw new MapLoadException($"{where} lists ability '{abilityId}' twice.");
                ids.Add(abilityId);
            }

            return new ClassDef(id, name, description, ids);
        }
    }
}
