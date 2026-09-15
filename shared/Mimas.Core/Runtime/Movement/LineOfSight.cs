using System.Collections.Generic;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Whether one tile can see another. Uses the integer-exact supercover line so an edge-grazing line is
    /// blocked by either flanking tile, and the answer is the same from both ends. A tile between blocks sight
    /// when it is solid (unwalkable terrain) or stands taller than both endpoints. Holes in the map (no tile)
    /// never block. Shared by teleport-with-sight now and by ranged abilities later.
    /// </summary>
    public static class LineOfSight
    {
        public static bool IsClear(TileMap map, Hex from, Hex to)
        {
            if (map == null) return false;
            if (from == to) return true;

            Tile fromTile, toTile;
            int fromHeight = map.TryGet(from, out fromTile) ? fromTile.Height : 0;
            int toHeight = map.TryGet(to, out toTile) ? toTile.Height : 0;
            int eye = fromHeight > toHeight ? fromHeight : toHeight;

            List<Hex> between = HexLine.Grazed(from, to);
            for (int i = 0; i < between.Count; i++)
            {
                Tile tile;
                if (!map.TryGet(between[i], out tile)) continue;
                if (!tile.Walkable) return false;
                if (tile.Height > eye) return false;
            }
            return true;
        }
    }
}
