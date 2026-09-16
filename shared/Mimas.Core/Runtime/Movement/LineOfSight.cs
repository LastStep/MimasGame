using Mimas.Core.Combat;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Whether a mover can see a destination tile. One rule, shared with attacks: the ray of
    /// <see cref="Sight"/>, from the mover's aim point to the aim height of the destination, ignoring the
    /// mover's own body (design: #movement, D16). Used by teleports that require sight.
    /// </summary>
    public static class LineOfSight
    {
        public static bool IsClear(TileMap map, IBodyLookup bodies, HeightsDef heights, Hex from, Hex to, int ignoreBodyId)
        {
            if (map == null || heights == null) return false;
            if (from == to) return true;
            return Sight.IsClear(map, bodies, heights,
                from, Sight.AimHeightAt(map, heights, from, heights.Aim),
                to, Sight.AimHeightAt(map, heights, to, heights.Aim),
                ignoreBodyId, -1);
        }
    }
}
