using System.Collections.Generic;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// A leap straight to any tile within <see cref="MovementDef.Range"/> hexes. Nothing between is entered,
    /// but the arc needs clearance: relative to the tile the unit stands on, neither the destination nor any
    /// tile the straight line grazes may rise more than <see cref="MovementDef.JumpHeight"/>. Where the line
    /// runs exactly along an edge both flanking tiles count, so a jump is never legal in one direction and
    /// illegal in the other. Unwalkable tiles between (a pit, a low pillar) are simply flown over; holes in
    /// the map too. Landing lower than the origin is always allowed.
    /// </summary>
    public sealed class JumpResolver : IMovementResolver
    {
        public string Mode => MovementModes.Jump;

        public MovementOptions Enumerate(MovementContext context)
        {
            var plans = new List<MovePlan>();
            List<Hex> candidates = Hex.Spiral(context.Origin, context.Def.Range);
            for (int i = 0; i < candidates.Count; i++)
            {
                MoveResult result = Validate(context, candidates[i]);
                if (result.Ok) plans.Add(result.Plan);
            }
            return new MovementOptions(plans);
        }

        public MoveResult Validate(MovementContext context, Hex destination)
        {
            if (destination == context.Origin) return MoveResult.Reject(MoveRejectReason.SameTile);

            int distance = Hex.Distance(context.Origin, destination);
            if (distance > context.Def.Range) return MoveResult.Reject(MoveRejectReason.OutOfRange);

            Tile tile;
            if (!context.Map.TryGet(destination, out tile)) return MoveResult.Reject(MoveRejectReason.OffMap);
            if (!context.CanEnter(tile)) return MoveResult.Reject(MoveRejectReason.NotEnterable);
            if (context.IsBlockedByUnit(destination)) return MoveResult.Reject(MoveRejectReason.Occupied);

            if (!context.Def.IgnoreHeight)
            {
                int ceiling = context.OriginHeight + context.Def.JumpHeight;
                if (tile.Height > ceiling) return MoveResult.Reject(MoveRejectReason.TooHigh);

                List<Hex> grazed = HexLine.Grazed(context.Origin, destination);
                for (int i = 0; i < grazed.Count; i++)
                {
                    Tile between;
                    if (!context.Map.TryGet(grazed[i], out between)) continue;
                    if (between.Height > ceiling) return MoveResult.Reject(MoveRejectReason.PathBlocked);
                }
            }

            return MoveResult.Accept(MovePlan.Direct(context.Origin, destination, TraversalKind.Leap, distance));
        }
    }
}
