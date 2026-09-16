using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// The walk shared by every trajectory that can be stopped by what stands in the way. It visits the columns
    /// the exact hex line touches strictly between the endpoints — a grazing sample flanks two hexes, so both
    /// are tested and a shot is never legal in one direction and illegal in the other — and asks
    /// <see cref="Clears"/> at <em>both</em> ends of each tile: a tile is a prism, and the lowest point of a
    /// monotone flight over it is at one of its edges. Endpoints never block and holes never block; unwalkable
    /// terrain is solid whatever its height.
    /// </summary>
    public abstract class ColumnTrajectory : ITrajectoryResolver
    {
        public abstract string Mode { get; }

        /// <summary>True when a column of height <paramref name="top"/> does not reach the flight at t = a/b.</summary>
        protected abstract bool Clears(long h0, long h1, int apex, long top, long a, long b);

        public bool IsClear(in TrajectoryContext ctx, out Hex blockedAt)
        {
            blockedAt = default;
            int n = Hex.Distance(ctx.From, ctx.To);
            if (n <= 1) return true;                       // adjacent or the same tile: nothing lies between

            long h0 = ctx.FromHeight, h1 = ctx.ToHeight, b = 2L * n;
            List<HexLineSample> samples = HexLine.Trace(ctx.From, ctx.To);
            for (int k = 1; k < n; k++)
            {
                HexLineSample sample = samples[k];
                for (int c = 0; c < sample.Count; c++)
                {
                    Hex hex = sample[c];
                    if (hex == ctx.From || hex == ctx.To) continue;   // endpoints never block

                    long top;
                    bool solid;
                    if (!ctx.TryColumnTop(hex, out top, out solid)) continue;
                    if (solid)
                    {
                        blockedAt = hex;
                        return false;
                    }

                    if (!Clears(h0, h1, ctx.Apex, top, 2L * k - 1, b) || !Clears(h0, h1, ctx.Apex, top, 2L * k + 1, b))
                    {
                        blockedAt = hex;
                        return false;
                    }
                }
            }
            return true;
        }
    }

    /// <summary>The straight ray: the same test as sight, by construction (design: D2/D3).</summary>
    public sealed class DirectTrajectory : ColumnTrajectory
    {
        public override string Mode => Trajectories.Direct;

        protected override bool Clears(long h0, long h1, int apex, long top, long a, long b)
            => Ballistics.StraightClears(h0, h1, top, a, b);
    }

    /// <summary>An integer parabola over the same endpoints, peaking <c>apex</c> above the higher one.</summary>
    public sealed class ArcTrajectory : ColumnTrajectory
    {
        public override string Mode => Trajectories.Arc;

        protected override bool Clears(long h0, long h1, int apex, long top, long a, long b)
            => Ballistics.ArcClears(h0, h1, apex, top, a, b);
    }

    /// <summary>Comes down from above: nothing in between matters, not even a wall.</summary>
    public sealed class SkyTrajectory : ITrajectoryResolver
    {
        public string Mode => Trajectories.Sky;

        public bool IsClear(in TrajectoryContext ctx, out Hex blockedAt)
        {
            blockedAt = default;
            return true;
        }
    }
}
