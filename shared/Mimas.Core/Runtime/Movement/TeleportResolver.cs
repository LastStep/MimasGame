using System.Collections.Generic;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Instant relocation to any enterable, unoccupied tile within <see cref="MovementDef.Range"/> hexes.
    /// Height and whatever lies between are ignored unless <see cref="MovementDef.RequiresLineOfSight"/> is
    /// set, in which case the destination must be visible from the origin along the same ray attacks use
    /// (<see cref="LineOfSight"/>, D16). Portals and other mechanics that relocate a unit call
    /// <see cref="Validate"/> with their fixed exit tile and reuse this rule unchanged.
    /// </summary>
    public sealed class TeleportResolver : IMovementResolver
    {
        public string Mode => MovementModes.Teleport;

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

            if (context.Def.RequiresLineOfSight)
            {
                if (context.Heights == null)
                    throw new System.InvalidOperationException(
                        $"Movement '{context.Def.Id}' requires line of sight but the context carries no heights; build it with MovementContext.For(map, terrains, bodies, unit, def, heights).");
                if (!LineOfSight.IsClear(context.Map, context.Bodies, context.Heights, context.Origin, destination, context.MoverId))
                    return MoveResult.Reject(MoveRejectReason.NoLineOfSight);
            }

            return MoveResult.Accept(MovePlan.Direct(context.Origin, destination, TraversalKind.Blink, distance));
        }
    }
}
