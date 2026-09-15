using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Grid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Movement
{
    /// <summary>The built-in movement modes. A mode is just a string key into the resolver registry.</summary>
    public static class MovementModes
    {
        public const string Walk = "walk";
        public const string Jump = "jump";
        public const string Teleport = "teleport";
    }

    /// <summary>
    /// One movement ability as authored in <c>abilities/*.json</c> with <c>"type": "movement"</c> (schema in
    /// docs/data.md). Immutable; all numbers are integers. Terrain interaction is a data table, Wesnoth-style:
    /// <see cref="CanEnter"/> and <see cref="EntryCost"/> consult the per-ability overrides first and fall back
    /// to the terrain catalogue, so "fly" is a walk whose table says every terrain costs 1.
    /// </summary>
    public sealed class MovementDef : AbilityDef
    {
        /// <summary>Sentinel stored in the cost table for terrains this movement can never enter.</summary>
        public const int Impassable = -1;

        private readonly Dictionary<string, int> _terrainCosts;
        private readonly List<string> _overriddenTerrainIds;

        /// <summary>Resolver key: <see cref="MovementModes"/> or any mode a later registry adds.</summary>
        public string Mode { get; }

        /// <summary>Walk: movement points. Jump / teleport: maximum hex distance.</summary>
        public int Range { get; }

        /// <summary>Walk: maximum height gained per step. Dropping down is never limited.</summary>
        public int MaxClimb { get; }

        /// <summary>Jump: how far above the origin tile the destination and every grazed tile may rise.</summary>
        public int JumpHeight { get; }

        /// <summary>When true every height rule is skipped (flight, phasing).</summary>
        public bool IgnoreHeight { get; }

        /// <summary>When true the destination must be visible from the origin (see <see cref="LineOfSight"/>).</summary>
        public bool RequiresLineOfSight { get; }

        public MovementDef(
            string id, string name, string mode, int range,
            int maxClimb = 1, int jumpHeight = 1,
            bool ignoreHeight = false, bool requiresLineOfSight = false,
            Dictionary<string, int> terrainCosts = null,
            string description = null)
            : base(id, name, TypeMovement, description)
        {
            Mode = mode ?? throw new ArgumentNullException(nameof(mode));
            if (range < 0) throw new ArgumentOutOfRangeException(nameof(range));
            if (maxClimb < 0) throw new ArgumentOutOfRangeException(nameof(maxClimb));
            if (jumpHeight < 0) throw new ArgumentOutOfRangeException(nameof(jumpHeight));
            Range = range;
            MaxClimb = maxClimb;
            JumpHeight = jumpHeight;
            IgnoreHeight = ignoreHeight;
            RequiresLineOfSight = requiresLineOfSight;
            _terrainCosts = terrainCosts != null
                ? new Dictionary<string, int>(terrainCosts, StringComparer.Ordinal)
                : new Dictionary<string, int>(StringComparer.Ordinal);
            _overriddenTerrainIds = new List<string>(_terrainCosts.Keys);
            _overriddenTerrainIds.Sort(string.CompareOrdinal);
        }

        /// <summary>True when this movement overrides the catalogue entry for <paramref name="terrainId"/>.</summary>
        public bool HasTerrainOverride(string terrainId) => terrainId != null && _terrainCosts.ContainsKey(terrainId);

        /// <summary>Terrain ids this movement overrides, sorted. The catalogue checks each one exists.</summary>
        public IReadOnlyList<string> OverriddenTerrainIds => _overriddenTerrainIds;

        /// <summary>
        /// Whether a unit using this movement may stand on <paramref name="tile"/>. An override wins outright
        /// (a cost makes even unwalkable terrain enterable; <see cref="Impassable"/> forbids walkable terrain);
        /// otherwise the tile's resolved walkability decides.
        /// </summary>
        public bool CanEnter(Tile tile)
        {
            if (tile == null) return false;
            int cost;
            if (_terrainCosts.TryGetValue(tile.Terrain, out cost)) return cost != Impassable;
            return tile.Walkable;
        }

        /// <summary>
        /// Movement points spent entering <paramref name="tile"/>: the override if present, else the terrain's
        /// catalogue cost. Never below 1, so a search bounded by <see cref="Range"/> always terminates and
        /// the reachable set stays within <see cref="Range"/> hexes. Meaningless when <see cref="CanEnter"/> is false.
        /// </summary>
        public int EntryCost(Tile tile, TerrainSet terrains)
        {
            int cost;
            if (!_terrainCosts.TryGetValue(tile.Terrain, out cost))
            {
                TerrainDef def;
                cost = terrains != null && terrains.TryGet(tile.Terrain, out def) ? def.MoveCost : 1;
            }
            return cost < 1 ? 1 : cost;
        }

        /// <summary>Parses one movement ability file. Throws <see cref="MapLoadException"/> on any schema violation.</summary>
        public static MovementDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Movement JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Movement JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "ability");
            if (version != 1) throw new MapLoadException($"Unsupported ability version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "ability");
            string where = $"ability '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string type = MapJson.RequireString(root, "type", where);
            if (!string.Equals(type, TypeMovement, StringComparison.Ordinal))
                throw new MapLoadException($"{where} has type '{type}'; MovementDef only loads type '{TypeMovement}'.");

            if (!(MapJson.Require(root, "movement", where) is JObject movement))
                throw new MapLoadException($"{where} field 'movement' must be an object.");

            string mode = MapJson.RequireString(movement, "mode", $"{where}.movement");
            int range = MapJson.RequireInt(movement, "range", $"{where}.movement");
            if (range < 0) throw new MapLoadException($"{where} has a negative range.");
            int maxClimb = MapJson.OptionalInt(movement, "maxClimb", 1, $"{where}.movement");
            if (maxClimb < 0) throw new MapLoadException($"{where} has a negative maxClimb.");
            int jumpHeight = MapJson.OptionalInt(movement, "jumpHeight", 1, $"{where}.movement");
            if (jumpHeight < 0) throw new MapLoadException($"{where} has a negative jumpHeight.");
            bool ignoreHeight = OptionalBool(movement, "ignoreHeight", false, $"{where}.movement");
            bool requiresLos = OptionalBool(movement, "requiresLineOfSight", false, $"{where}.movement");

            Dictionary<string, int> costs = null;
            var costsToken = movement["terrainCosts"];
            if (costsToken != null && costsToken.Type != JTokenType.Null)
            {
                if (!(costsToken is JObject costsObj))
                    throw new MapLoadException($"{where}.movement field 'terrainCosts' must be an object.");
                costs = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var property in costsObj.Properties())
                {
                    if (string.IsNullOrEmpty(property.Name))
                        throw new MapLoadException($"{where}.movement.terrainCosts has an empty terrain id.");
                    var value = property.Value;
                    if (value == null || value.Type == JTokenType.Null)
                    {
                        costs[property.Name] = Impassable;
                        continue;
                    }
                    if (value.Type != JTokenType.Integer)
                        throw new MapLoadException($"{where}.movement.terrainCosts['{property.Name}'] must be an integer or null.");
                    int cost = (int)value;
                    if (cost < 1)
                        throw new MapLoadException($"{where}.movement.terrainCosts['{property.Name}'] must be at least 1 (use null for impassable).");
                    costs[property.Name] = cost;
                }
            }

            return new MovementDef(id, name, mode, range, maxClimb, jumpHeight, ignoreHeight, requiresLos, costs, description);
        }

        private static bool OptionalBool(JObject obj, string field, bool fallback, string where)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type != JTokenType.Boolean)
                throw new MapLoadException($"{where} field '{field}' must be a boolean.");
            return (bool)token;
        }
    }

    /// <summary>The movement abilities available in a match, keyed by id, in load order.</summary>
    public sealed class MovementDefSet
    {
        private readonly List<MovementDef> _ordered = new List<MovementDef>();
        private readonly Dictionary<string, MovementDef> _byId = new Dictionary<string, MovementDef>(StringComparer.Ordinal);

        public IReadOnlyList<MovementDef> All => _ordered;

        public int Count => _ordered.Count;

        public void Add(MovementDef def)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (_byId.ContainsKey(def.Id)) throw new MapLoadException($"Duplicate movement ability id '{def.Id}'.");
            _byId[def.Id] = def;
            _ordered.Add(def);
        }

        /// <summary>Parses every JSON document in order. Duplicate ids throw.</summary>
        public static MovementDefSet FromJson(IEnumerable<string> jsonDocuments)
        {
            if (jsonDocuments == null) throw new ArgumentNullException(nameof(jsonDocuments));
            var set = new MovementDefSet();
            foreach (var json in jsonDocuments) set.Add(MovementDef.FromJson(json));
            return set;
        }

        public MovementDef Get(string id)
        {
            MovementDef def;
            if (id != null && _byId.TryGetValue(id, out def)) return def;
            throw new MapLoadException($"Unknown movement ability id '{id}'.");
        }

        public bool TryGet(string id, out MovementDef def)
        {
            if (id == null)
            {
                def = null;
                return false;
            }
            return _byId.TryGetValue(id, out def);
        }
    }
}
