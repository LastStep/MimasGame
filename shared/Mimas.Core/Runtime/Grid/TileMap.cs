using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Grid
{
    /// <summary>A single map cell. Terrain, height and effects are data-driven ids (see docs/data.md).</summary>
    public sealed class Tile
    {
        public Hex Position { get; }
        public string Terrain { get; }
        public int Height { get; }
        public string EffectId { get; set; }   // null = none
        public bool Walkable { get; set; } = true;

        public Tile(Hex position, string terrain, int height = 0)
        {
            Position = position;
            Terrain = terrain ?? throw new ArgumentNullException(nameof(terrain));
            Height = height;
        }
    }

    /// <summary>
    /// Shape-agnostic hex map: any set of hex coordinates (ring, board, islands). Adjacency is derived
    /// from hex neighbours that exist in the map, so "graph" queries work for every shape.
    /// </summary>
    public sealed class TileMap
    {
        private readonly Dictionary<Hex, Tile> _tiles = new Dictionary<Hex, Tile>();

        public int Count => _tiles.Count;
        public IEnumerable<Tile> Tiles => _tiles.Values;

        public void Add(Tile tile)
        {
            if (_tiles.ContainsKey(tile.Position)) throw new InvalidOperationException($"Duplicate tile at {tile.Position}");
            _tiles[tile.Position] = tile;
        }

        public bool Contains(Hex h) => _tiles.ContainsKey(h);

        public bool TryGet(Hex h, out Tile tile) => _tiles.TryGetValue(h, out tile);

        public Tile this[Hex h] => _tiles[h];

        /// <summary>Existing neighbouring tiles (up to 6).</summary>
        public IEnumerable<Tile> NeighborsOf(Hex h)
        {
            for (int i = 0; i < 6; i++)
            {
                Tile t;
                if (_tiles.TryGetValue(h.Neighbor(i), out t)) yield return t;
            }
        }

        /// <summary>
        /// Tiles reachable within <paramref name="movement"/> steps over walkable tiles (uniform cost, BFS).
        /// Movement-type specific costs (jump/fly/teleport) will be layered on top of this later.
        /// </summary>
        public Dictionary<Hex, int> ReachableWithin(Hex start, int movement)
        {
            var dist = new Dictionary<Hex, int> { [start] = 0 };
            if (!_tiles.ContainsKey(start)) return dist;
            var frontier = new Queue<Hex>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                int d = dist[cur];
                if (d == movement) continue;
                // Iterate directions in fixed order for determinism.
                for (int i = 0; i < 6; i++)
                {
                    var next = cur.Neighbor(i);
                    if (!_tiles.TryGetValue(next, out var tile) || !tile.Walkable) continue;
                    if (dist.ContainsKey(next)) continue;
                    dist[next] = d + 1;
                    frontier.Enqueue(next);
                }
            }
            return dist;
        }

        /// <summary>
        /// Shortest walkable path from <paramref name="start"/> to <paramref name="goal"/>, inclusive of both,
        /// as a uniform-cost BFS over existing tiles. The start tile's own walkability is not required (a unit
        /// may be standing on it); every later step must be walkable. Neighbours are expanded in
        /// <see cref="Hex.Directions"/> order, so the chosen path is deterministic when several are equally short.
        /// Returns null when either hex is off the map, the goal is unwalkable, or no path exists.
        /// </summary>
        public List<Hex> FindPath(Hex start, Hex goal)
        {
            if (!_tiles.ContainsKey(start)) return null;
            Tile goalTile;
            if (!_tiles.TryGetValue(goal, out goalTile) || !goalTile.Walkable) return null;
            if (start == goal) return new List<Hex> { start };

            var cameFrom = new Dictionary<Hex, Hex> { [start] = start };
            var frontier = new Queue<Hex>();
            frontier.Enqueue(start);
            while (frontier.Count > 0)
            {
                var cur = frontier.Dequeue();
                // Iterate directions in fixed order for determinism.
                for (int i = 0; i < 6; i++)
                {
                    var next = cur.Neighbor(i);
                    if (cameFrom.ContainsKey(next)) continue;
                    Tile tile;
                    if (!_tiles.TryGetValue(next, out tile) || !tile.Walkable) continue;
                    cameFrom[next] = cur;
                    if (next == goal) return Reconstruct(cameFrom, start, goal);
                    frontier.Enqueue(next);
                }
            }
            return null;
        }

        private static List<Hex> Reconstruct(Dictionary<Hex, Hex> cameFrom, Hex start, Hex goal)
        {
            var path = new List<Hex>();
            var cur = goal;
            while (cur != start)
            {
                path.Add(cur);
                cur = cameFrom[cur];
            }
            path.Add(start);
            path.Reverse();
            return path;
        }

        /// <summary>Checks that every tile has a counterpart under 180° rotation about the origin.</summary>
        public bool IsRotationallySymmetric()
        {
            foreach (var h in _tiles.Keys)
                if (!_tiles.ContainsKey(h.Rotate180())) return false;
            return true;
        }
    }
}
