using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>Thrown whenever game data fails to parse or fails a design invariant (see docs/data.md).</summary>
    public sealed class MapLoadException : Exception
    {
        public MapLoadException(string message) : base(message) { }
        public MapLoadException(string message, Exception inner) : base(message, inner) { }
    }

    /// <summary>Shared, deterministic JSON accessors. Every failure surfaces as a <see cref="MapLoadException"/>.</summary>
    internal static class MapJson
    {
        internal static JToken Require(JObject obj, string field, string where)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null)
                throw new MapLoadException($"{where} is missing required field '{field}'.");
            return token;
        }

        internal static string RequireString(JObject obj, string field, string where)
        {
            var token = Require(obj, field, where);
            if (token.Type != JTokenType.String)
                throw new MapLoadException($"{where} field '{field}' must be a string.");
            var value = (string)token;
            if (string.IsNullOrEmpty(value))
                throw new MapLoadException($"{where} field '{field}' must not be empty.");
            return value;
        }

        internal static int RequireInt(JObject obj, string field, string where)
        {
            var token = Require(obj, field, where);
            if (token.Type != JTokenType.Integer)
                throw new MapLoadException($"{where} field '{field}' must be an integer.");
            return (int)token;
        }

        internal static bool RequireBool(JObject obj, string field, string where)
        {
            var token = Require(obj, field, where);
            if (token.Type != JTokenType.Boolean)
                throw new MapLoadException($"{where} field '{field}' must be a boolean.");
            return (bool)token;
        }

        internal static int OptionalInt(JObject obj, string field, int fallback, string where)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return fallback;
            if (token.Type != JTokenType.Integer)
                throw new MapLoadException($"{where} field '{field}' must be an integer.");
            return (int)token;
        }

        internal static string OptionalString(JObject obj, string field, string where)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String)
                throw new MapLoadException($"{where} field '{field}' must be a string.");
            var value = (string)token;
            return string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>A required array of non-empty, unique strings.</summary>
        internal static List<string> RequireStringList(JObject obj, string field, string where)
        {
            var token = Require(obj, field, where);
            return ParseStringList(token, field, where);
        }

        /// <summary>An optional array of non-empty, unique strings; empty when absent.</summary>
        internal static List<string> OptionalStringList(JObject obj, string field, string where)
        {
            var token = obj[field];
            if (token == null || token.Type == JTokenType.Null) return new List<string>();
            return ParseStringList(token, field, where);
        }

        private static List<string> ParseStringList(JToken token, string field, string where)
        {
            if (!(token is JArray array))
                throw new MapLoadException($"{where} field '{field}' must be an array of strings.");
            var list = new List<string>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String || string.IsNullOrEmpty((string)array[i]))
                    throw new MapLoadException($"{where} {field}[{i}] must be a non-empty string.");
                string value = (string)array[i];
                if (!seen.Add(value))
                    throw new MapLoadException($"{where} {field} lists '{value}' twice.");
                list.Add(value);
            }
            return list;
        }

        internal static Hex RequireHex(JObject obj, string field, string where)
        {
            var token = Require(obj, field, where);
            if (!(token is JObject o))
                throw new MapLoadException($"{where} field '{field}' must be an object with q and r.");
            return new Hex(RequireInt(o, "q", $"{where}.{field}"), RequireInt(o, "r", $"{where}.{field}"));
        }
    }

    /// <summary>One authored hex of a <see cref="MapData"/> before terrain resolution.</summary>
    public sealed class MapHex
    {
        public Hex Position { get; }
        public string Terrain { get; }
        public int Height { get; }
        public string EffectId { get; }   // null = none

        public MapHex(Hex position, string terrain, int height, string effectId)
        {
            Position = position;
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Height = height;
            EffectId = effectId;
        }
    }

    /// <summary>
    /// A map definition loaded from <c>maps/*.json</c>. Parsing only checks the schema;
    /// the design invariants (symmetry, spawn placement, connectivity) are enforced by
    /// <see cref="BuildTileMap"/>, which needs the terrain catalogue to know what is walkable.
    /// </summary>
    public sealed class MapData : Mimas.Core.Content.IContentDef
    {
        /// <summary>The only symmetry the validator currently understands.</summary>
        public const string SymmetryRotational180 = "rotational-180";

        private readonly List<MapHex> _hexes;

        public string Id { get; }
        public string Name { get; }
        public string Symmetry { get; }
        public int LadderPosition { get; }
        public Hex SpawnP1 { get; }
        public Hex SpawnP2 { get; }

        /// <summary>Authored hexes in file order (deterministic).</summary>
        public IEnumerable<MapHex> Hexes => _hexes;

        public int HexCount => _hexes.Count;

        private MapData(string id, string name, string symmetry, int ladderPosition, Hex p1, Hex p2, List<MapHex> hexes)
        {
            Id = id;
            Name = name;
            Symmetry = symmetry;
            LadderPosition = ladderPosition;
            SpawnP1 = p1;
            SpawnP2 = p2;
            _hexes = hexes;
        }

        /// <summary>Parses a map file. Throws <see cref="MapLoadException"/> on any schema violation.</summary>
        public static MapData FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Map JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Map JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "map");
            if (version != 1) throw new MapLoadException($"Unsupported map version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "map");
            string name = MapJson.RequireString(root, "name", "map");
            string symmetry = MapJson.RequireString(root, "symmetry", $"map '{id}'");
            int ladderPosition = MapJson.RequireInt(root, "ladderPosition", $"map '{id}'");

            if (!(MapJson.Require(root, "spawns", $"map '{id}'") is JObject spawns))
                throw new MapLoadException($"map '{id}' field 'spawns' must be an object.");
            Hex p1 = MapJson.RequireHex(spawns, "p1", $"map '{id}' spawns");
            Hex p2 = MapJson.RequireHex(spawns, "p2", $"map '{id}' spawns");

            if (!(root["hexes"] is JArray array))
                throw new MapLoadException($"map '{id}' is missing the 'hexes' array.");
            if (array.Count == 0)
                throw new MapLoadException($"map '{id}' contains no hexes.");

            var hexes = new List<MapHex>(array.Count);
            var seen = new HashSet<Hex>();
            for (int i = 0; i < array.Count; i++)
            {
                if (!(array[i] is JObject entry))
                    throw new MapLoadException($"map '{id}' hexes[{i}] is not an object.");

                string where = $"map '{id}' hexes[{i}]";
                var position = new Hex(MapJson.RequireInt(entry, "q", where), MapJson.RequireInt(entry, "r", where));
                if (!seen.Add(position))
                    throw new MapLoadException($"map '{id}' declares duplicate hex {position}.");

                hexes.Add(new MapHex(
                    position,
                    MapJson.RequireString(entry, "terrain", where),
                    MapJson.OptionalInt(entry, "height", 0, where),
                    MapJson.OptionalString(entry, "effect", where)));
            }

            return new MapData(id, name, symmetry, ladderPosition, p1, p2, hexes);
        }

        /// <summary>
        /// Builds the playable <see cref="TileMap"/>, resolving walkability from <paramref name="terrains"/>,
        /// then enforces the map invariants from docs/data.md: the declared symmetry holds (shape *and*
        /// terrain), both spawns exist, are walkable and are at maximal hex distance among walkable tiles,
        /// and all walkable tiles form a single connected component.
        /// </summary>
        public TileMap BuildTileMap(TerrainSet terrains)
        {
            if (terrains == null) throw new ArgumentNullException(nameof(terrains));

            var map = new TileMap();
            foreach (var h in _hexes)
            {
                TerrainDef def;
                if (!terrains.TryGet(h.Terrain, out def))
                    throw new MapLoadException($"map '{Id}' hex {h.Position} uses unknown terrain id '{h.Terrain}'.");

                var tile = new Tile(h.Position, h.Terrain, h.Height);
                tile.EffectId = h.EffectId;
                tile.Walkable = def.Walkable;
                map.Add(tile);
            }

            ValidateSymmetry(map);
            ValidateSpawns(map);
            return map;
        }

        private void ValidateSymmetry(TileMap map)
        {
            if (!string.Equals(Symmetry, SymmetryRotational180, StringComparison.Ordinal))
                throw new MapLoadException($"map '{Id}' declares unsupported symmetry '{Symmetry}'.");

            if (!map.IsRotationallySymmetric())
                throw new MapLoadException($"map '{Id}' is not rotationally symmetric: a tile has no 180° twin.");

            foreach (var h in _hexes)
            {
                var twin = map[h.Position.Rotate180()];
                if (!string.Equals(twin.Terrain, h.Terrain, StringComparison.Ordinal))
                    throw new MapLoadException(
                        $"map '{Id}' breaks rotational symmetry: {h.Position} is '{h.Terrain}' but its twin {twin.Position} is '{twin.Terrain}'.");
            }
        }

        private void ValidateSpawns(TileMap map)
        {
            Tile t1, t2;
            if (!map.TryGet(SpawnP1, out t1)) throw new MapLoadException($"map '{Id}' spawn p1 {SpawnP1} is not on the map.");
            if (!map.TryGet(SpawnP2, out t2)) throw new MapLoadException($"map '{Id}' spawn p2 {SpawnP2} is not on the map.");
            if (!t1.Walkable) throw new MapLoadException($"map '{Id}' spawn p1 {SpawnP1} is not walkable.");
            if (!t2.Walkable) throw new MapLoadException($"map '{Id}' spawn p2 {SpawnP2} is not walkable.");

            // Walkable tiles in authored order — never iterate the map's dictionary, order must be stable.
            var walkable = new List<Hex>(_hexes.Count);
            foreach (var h in _hexes)
                if (map[h.Position].Walkable) walkable.Add(h.Position);

            int spawnDistance = Hex.Distance(SpawnP1, SpawnP2);
            for (int i = 0; i < walkable.Count; i++)
            {
                for (int j = i + 1; j < walkable.Count; j++)
                {
                    int d = Hex.Distance(walkable[i], walkable[j]);
                    if (d > spawnDistance)
                        throw new MapLoadException(
                            $"map '{Id}' spawns are {spawnDistance} apart but {walkable[i]}..{walkable[j]} are {d} apart; spawns must be at maximal distance.");
                }
            }

            var reached = map.ReachableWithin(SpawnP1, int.MaxValue);
            foreach (var h in walkable)
            {
                if (!reached.ContainsKey(h))
                    throw new MapLoadException($"map '{Id}' walkable tiles are not connected: {h} is unreachable from spawn p1 {SpawnP1}.");
            }
        }
    }
}
