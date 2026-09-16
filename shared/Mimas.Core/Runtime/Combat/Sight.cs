using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// Whether one point on the board can see another: a straight ray from the viewer's aim point to the
    /// target's, blocked by any intervening column that reaches it — tall terrain, a wall, a pillar or another
    /// hero's body — and by unwalkable terrain whatever its height (design: #line-of-sight, D2–D4). A graze
    /// blocks; the endpoints never block; a hole in the map never blocks. Sight and the <c>direct</c> trajectory
    /// are the same test by construction: this runs <see cref="DirectTrajectory"/>.
    /// </summary>
    public static class Sight
    {
        private static readonly DirectTrajectory Ray = new DirectTrajectory();

        public static bool IsClear(TileMap map, IBodyLookup bodies, HeightsDef heights,
            Hex from, int fromHeight, Hex to, int toHeight, int ignoreA, int ignoreB, out Hex blockedAt)
        {
            var ctx = new TrajectoryContext(map, bodies, heights, from, fromHeight, to, toHeight, 0, ignoreA, ignoreB);
            return Ray.IsClear(in ctx, out blockedAt);
        }

        /// <summary>The same ray with no blocked hex wanted.</summary>
        public static bool IsClear(TileMap map, IBodyLookup bodies, HeightsDef heights,
            Hex from, int fromHeight, Hex to, int toHeight, int ignoreA, int ignoreB)
        {
            Hex unused;
            return IsClear(map, bodies, heights, from, fromHeight, to, toHeight, ignoreA, ignoreB, out unused);
        }

        /// <summary>Absolute aim height at a hex: the tile top plus <paramref name="aimHeight"/>, or just the aim height off the map.</summary>
        public static int AimHeightAt(TileMap map, HeightsDef heights, Hex hex, int aimHeight)
        {
            Tile tile;
            return (map.TryGet(hex, out tile) ? heights.TileTop(tile) : 0) + aimHeight;
        }
    }
}
