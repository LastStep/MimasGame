using System;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// Everything a trajectory resolver needs to answer "does this flight get through": the board, the bodies
    /// standing on it, the height conversion, the two endpoints with their absolute aim heights, the arc's apex,
    /// and the two bodies that never count as blockers (the attacker and the target). Built fresh per query and
    /// holds no state of its own, exactly like <see cref="Mimas.Core.Movement.MovementContext"/>.
    /// </summary>
    public readonly struct TrajectoryContext
    {
        public readonly TileMap Map;
        public readonly IBodyLookup Bodies;
        public readonly HeightsDef Heights;

        public readonly Hex From;

        /// <summary>Absolute height of the muzzle in units: the origin tile's top plus the shooter's aim height.</summary>
        public readonly int FromHeight;

        public readonly Hex To;

        /// <summary>Absolute height of the impact point in units.</summary>
        public readonly int ToHeight;

        /// <summary>How far above the higher endpoint an arc peaks; ignored by the other modes.</summary>
        public readonly int Apex;

        /// <summary>Body ids that never block (the attacker and the target); -1 for none.</summary>
        public readonly int IgnoreBodyA, IgnoreBodyB;

        public TrajectoryContext(TileMap map, IBodyLookup bodies, HeightsDef heights,
            Hex from, int fromHeight, Hex to, int toHeight, int apex = 0, int ignoreBodyA = -1, int ignoreBodyB = -1)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Bodies = bodies;
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            From = from;
            FromHeight = fromHeight;
            To = to;
            ToHeight = toHeight;
            Apex = apex;
            IgnoreBodyA = ignoreBodyA;
            IgnoreBodyB = ignoreBodyB;
        }

        /// <summary>
        /// The top of the column standing at <paramref name="hex"/> in absolute units: the tile top, raised by
        /// any living body that is not ignored. <paramref name="solid"/> marks unwalkable terrain, which stops
        /// everything but <c>sky</c> whatever its height. A hole in the map returns false and never blocks.
        /// </summary>
        public bool TryColumnTop(Hex hex, out long top, out bool solid)
        {
            Tile tile;
            if (!Map.TryGet(hex, out tile))
            {
                top = 0;
                solid = false;
                return false;
            }

            long tileTop = Heights.TileTop(tile);
            solid = !tile.Walkable;
            top = tileTop;

            IBody body;
            if (Bodies != null && Bodies.TryGetBodyAt(hex, out body) && body.Id != IgnoreBodyA && body.Id != IgnoreBodyB)
            {
                long withBody = tileTop + body.BodyHeight;
                if (withBody > top) top = withBody;
            }
            return true;
        }
    }

    /// <summary>
    /// One trajectory mode's geometry. Resolvers are stateless and deterministic; which columns they sample is
    /// shared, only the height test differs (design: #trajectories).
    /// </summary>
    public interface ITrajectoryResolver
    {
        /// <summary>The <see cref="Trajectories"/> key this resolver handles.</summary>
        string Mode { get; }

        /// <summary>False when something between From and To stops the flight; <paramref name="blockedAt"/> is that hex.</summary>
        bool IsClear(in TrajectoryContext ctx, out Hex blockedAt);
    }
}
