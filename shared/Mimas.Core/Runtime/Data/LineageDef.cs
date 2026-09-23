using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// A divine lineage from <c>lineages/*.json</c> (design: #lineage): the Blessing a player starts with
    /// and the pool of boons the draft draws from. The link rules (the starting Blessing exists and belongs
    /// here, every pool boon belongs here, the pool holds all three kinds and covers every item) are the
    /// catalogue's; this class only knows the file.
    /// </summary>
    public sealed class LineageDef : IContentDef
    {
        private readonly List<string> _poolIds;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Presentation key for an icon, or null.</summary>
        public string Icon { get; }

        /// <summary>Presentation: the dark hue of the lineage's painting (<c>#rrggbb</c>), or null (docs/ui/language.md §1).</summary>
        public string HueDark { get; }

        /// <summary>Presentation: the light hue of the lineage's painting (<c>#rrggbb</c>), or null.</summary>
        public string HueLight { get; }

        /// <summary>The Blessing granted at character select (a <see cref="BoonDef"/> id).</summary>
        public string StartingBlessingId { get; }

        /// <summary>The boon ids the draft draws from, in authored order, unique.</summary>
        public IReadOnlyList<string> PoolIds => _poolIds;

        public LineageDef(string id, string name, string startingBlessingId, List<string> poolIds, string description = null, string icon = null, string hueDark = null, string hueLight = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            if (string.IsNullOrEmpty(startingBlessingId)) throw new ArgumentException("A lineage needs a starting Blessing.", nameof(startingBlessingId));
            if (poolIds == null) throw new ArgumentNullException(nameof(poolIds));
            StartingBlessingId = startingBlessingId;
            _poolIds = new List<string>(poolIds);
            for (int i = 0; i < _poolIds.Count; i++)
                for (int j = i + 1; j < _poolIds.Count; j++)
                    if (_poolIds[i] == _poolIds[j]) throw new ArgumentException($"Pool lists '{_poolIds[i]}' twice.", nameof(poolIds));
            Description = description;
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
            if (hueDark != null && !IsHexColour(hueDark)) throw new ArgumentException($"hueDark '{hueDark}' is not #rrggbb.", nameof(hueDark));
            if (hueLight != null && !IsHexColour(hueLight)) throw new ArgumentException($"hueLight '{hueLight}' is not #rrggbb.", nameof(hueLight));
            HueDark = hueDark;
            HueLight = hueLight;
        }

        /// <summary><c>#</c> and six hex digits, the only colour form data carries.</summary>
        public static bool IsHexColour(string value)
        {
            if (value == null || value.Length != 7 || value[0] != '#') return false;
            for (int i = 1; i < 7; i++)
            {
                char c = value[i];
                bool hex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!hex) return false;
            }
            return true;
        }

        public bool PoolContains(string boonId) => boonId != null && _poolIds.Contains(boonId);

        public static LineageDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Lineage JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Lineage JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "lineage");
            if (version != 1) throw new MapLoadException($"Unsupported lineage version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "lineage");
            string where = $"lineage '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);
            string starting = MapJson.RequireString(root, "startingBlessing", where);
            var pool = MapJson.RequireStringList(root, "pool", where);
            string hueDark = MapJson.OptionalString(root, "hueDark", where);
            string hueLight = MapJson.OptionalString(root, "hueLight", where);
            if (hueDark != null && !IsHexColour(hueDark)) throw new MapLoadException($"{where}: hueDark must be #rrggbb, was '{hueDark}'.");
            if (hueLight != null && !IsHexColour(hueLight)) throw new MapLoadException($"{where}: hueLight must be #rrggbb, was '{hueLight}'.");

            return new LineageDef(id, name, starting, pool, description, icon, hueDark, hueLight);
        }
    }
}
