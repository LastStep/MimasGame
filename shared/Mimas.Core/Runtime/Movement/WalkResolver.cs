using System.Collections.Generic;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Ground movement: a uniform-cost search over adjacent tiles spending <see cref="MovementDef.Range"/>
    /// movement points, each step paying the entered tile's cost. A step may rise at most
    /// <see cref="MovementDef.MaxClimb"/>; descending is free. Occupied tiles cannot be entered or crossed.
    /// Costs are small integers so a bucket queue (one FIFO per cost) is both the fastest structure and
    /// trivially deterministic: ties resolve by discovery order, and neighbours are always expanded in
    /// <see cref="Hex.Directions"/> order.
    /// </summary>
    public sealed class WalkResolver : IMovementResolver
    {
        public string Mode => MovementModes.Walk;

        public MovementOptions Enumerate(MovementContext context)
        {
            Dictionary<Hex, int> cost;
            Dictionary<Hex, Hex> cameFrom;
            Search(context, out cost, out cameFrom);

            // Every entered tile costs at least 1, so nothing beyond Range hexes is reachable; the spiral gives
            // a stable, distance-major ordering that does not depend on dictionary iteration.
            var plans = new List<MovePlan>(cost.Count);
            List<Hex> candidates = Hex.Spiral(context.Origin, context.Def.Range);
            for (int i = 0; i < candidates.Count; i++)
            {
                Hex hex = candidates[i];
                if (hex == context.Origin) continue;
                int total;
                if (!cost.TryGetValue(hex, out total)) continue;
                plans.Add(BuildPlan(context.Origin, hex, total, cameFrom));
            }
            return new MovementOptions(plans);
        }

        public MoveResult Validate(MovementContext context, Hex destination)
        {
            if (destination == context.Origin) return MoveResult.Reject(MoveRejectReason.SameTile);

            Tile tile;
            if (!context.Map.TryGet(destination, out tile)) return MoveResult.Reject(MoveRejectReason.OffMap);
            if (!context.CanEnter(tile)) return MoveResult.Reject(MoveRejectReason.NotEnterable);
            if (context.IsBlockedByUnit(destination)) return MoveResult.Reject(MoveRejectReason.Occupied);
            if (Hex.Distance(context.Origin, destination) > context.Def.Range) return MoveResult.Reject(MoveRejectReason.OutOfRange);

            Dictionary<Hex, int> cost;
            Dictionary<Hex, Hex> cameFrom;
            Search(context, out cost, out cameFrom);

            int total;
            if (!cost.TryGetValue(destination, out total)) return MoveResult.Reject(MoveRejectReason.Unreachable);
            return MoveResult.Accept(BuildPlan(context.Origin, destination, total, cameFrom));
        }

        private static void Search(MovementContext context, out Dictionary<Hex, int> cost, out Dictionary<Hex, Hex> cameFrom)
        {
            cost = new Dictionary<Hex, int>();
            cameFrom = new Dictionary<Hex, Hex>();

            Tile originTile;
            if (!context.Map.TryGet(context.Origin, out originTile)) return;

            int range = context.Def.Range;
            bool ignoreHeight = context.Def.IgnoreHeight;
            int maxClimb = context.Def.MaxClimb;

            var buckets = new List<Hex>[range + 1];
            for (int i = 0; i <= range; i++) buckets[i] = new List<Hex>();
            var closed = new HashSet<Hex>();

            cost[context.Origin] = 0;
            buckets[0].Add(context.Origin);

            for (int c = 0; c <= range; c++)
            {
                List<Hex> bucket = buckets[c];
                // Index loop: entry costs are at least 1, so nothing is appended to the bucket being drained,
                // but this stays correct even if that invariant is ever relaxed.
                for (int b = 0; b < bucket.Count; b++)
                {
                    Hex cur = bucket[b];
                    if (cost[cur] != c || !closed.Add(cur)) continue;   // stale entry or already expanded

                    Tile curTile = context.Map[cur];
                    for (int d = 0; d < 6; d++)
                    {
                        Hex next = cur.Neighbor(d);
                        Tile nextTile;
                        if (!context.Map.TryGet(next, out nextTile)) continue;
                        if (!context.CanEnter(nextTile)) continue;
                        if (context.IsBlockedByUnit(next)) continue;
                        if (!ignoreHeight && nextTile.Height - curTile.Height > maxClimb) continue;

                        int nextCost = c + context.EntryCost(nextTile);
                        if (nextCost > range) continue;

                        int known;
                        if (cost.TryGetValue(next, out known) && known <= nextCost) continue;   // keep the first equal-cost route

                        cost[next] = nextCost;
                        cameFrom[next] = cur;
                        buckets[nextCost].Add(next);
                    }
                }
            }
        }

        private static MovePlan BuildPlan(Hex origin, Hex destination, int cost, Dictionary<Hex, Hex> cameFrom)
        {
            var path = new List<Hex>();
            Hex cur = destination;
            while (cur != origin)
            {
                path.Add(cur);
                cur = cameFrom[cur];
            }
            path.Add(origin);
            path.Reverse();

            var entered = new List<Hex>(path.Count - 1);
            for (int i = 1; i < path.Count; i++) entered.Add(path[i]);

            return new MovePlan(origin, destination, path, entered, TraversalKind.Ground, cost);
        }
    }
}
