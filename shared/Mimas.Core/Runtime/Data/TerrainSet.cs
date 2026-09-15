using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>One terrain kind (see <c>terrains.json</c> in docs/data.md). Costs are integers — no floats in rules.</summary>
    public sealed class TerrainDef
    {
        public string Id { get; }
        public bool Walkable { get; }

        /// <summary>Steps consumed when entering this terrain. Defaults to 1; unwalkable terrain uses 0.</summary>
        public int MoveCost { get; }

        public TerrainDef(string id, bool walkable, int moveCost = 1)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Walkable = walkable;
            MoveCost = moveCost;
        }
    }

    /// <summary>
    /// The terrain catalogue loaded from <c>terrains.json</c>. Maps reference terrains by id, so the
    /// same set must be loaded on client and server before any <see cref="MapData"/> is built.
    /// </summary>
    public sealed class TerrainSet
    {
        private readonly Dictionary<string, TerrainDef> _byId;
        private readonly List<TerrainDef> _ordered;

        private TerrainSet(List<TerrainDef> terrains)
        {
            _ordered = terrains;
            _byId = new Dictionary<string, TerrainDef>(terrains.Count, StringComparer.Ordinal);
            foreach (var t in terrains) _byId[t.Id] = t;
        }

        /// <summary>All terrains in file order (deterministic — never rely on dictionary order).</summary>
        public IEnumerable<TerrainDef> All => _ordered;

        public int Count => _ordered.Count;

        /// <summary>Parses <c>terrains.json</c>. Throws <see cref="MapLoadException"/> on any schema violation.</summary>
        public static TerrainSet FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Terrain JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Terrain JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "terrains");
            if (version != 1) throw new MapLoadException($"Unsupported terrains version {version} (expected 1).");

            if (!(root["terrains"] is JArray array))
                throw new MapLoadException("Terrain JSON is missing the 'terrains' array.");
            if (array.Count == 0)
                throw new MapLoadException("Terrain JSON contains no terrains.");

            var terrains = new List<TerrainDef>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject entry))
                    throw new MapLoadException($"terrains[{i}] is not an object.");

                string id = MapJson.RequireString(entry, "id", $"terrains[{i}]");
                if (!seen.Add(id))
                    throw new MapLoadException($"Duplicate terrain id '{id}'.");

                bool walkable = MapJson.RequireBool(entry, "walkable", $"terrains[{i}]");
                int moveCost = MapJson.OptionalInt(entry, "moveCost", walkable ? 1 : 0, $"terrains[{i}]");
                if (moveCost < 0)
                    throw new MapLoadException($"terrains[{i}] ('{id}') has a negative moveCost.");

                terrains.Add(new TerrainDef(id, walkable, moveCost));
            }

            return new TerrainSet(terrains);
        }

        /// <summary>Looks up a terrain; throws <see cref="MapLoadException"/> if the id is unknown.</summary>
        public TerrainDef Get(string id)
        {
            TerrainDef def;
            if (id != null && _byId.TryGetValue(id, out def)) return def;
            throw new MapLoadException($"Unknown terrain id '{id}'.");
        }

        public bool TryGet(string id, out TerrainDef def)
        {
            if (id == null)
            {
                def = null;
                return false;
            }
            return _byId.TryGetValue(id, out def);
        }

        /// <summary>True if the terrain exists and is walkable. Unknown ids are not walkable.</summary>
        public bool IsWalkable(string terrainId)
        {
            TerrainDef def;
            return TryGet(terrainId, out def) && def.Walkable;
        }
    }
}
