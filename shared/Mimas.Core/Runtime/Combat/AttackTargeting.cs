using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Combat
{
    /// <summary>Why a target was refused. <see cref="None"/> means it is a legal target.</summary>
    public enum TargetRejectReason
    {
        None = 0,

        /// <summary>Nothing alive stands on the tile. Ground targeting is not legal (design: #attacks).</summary>
        NoBody,

        /// <summary>A body with no hit points: a wall. It blocks shots but cannot take one.</summary>
        NotDamageable,

        /// <summary>The attacker's own hero.</summary>
        OwnUnit,

        TargetDead,
        OutOfRange,

        /// <summary>The attack needs sight and the ray is blocked.</summary>
        NoLineOfSight,

        /// <summary>Sight is fine (or not needed) but the flight itself cannot get through.</summary>
        TrajectoryBlocked,
    }

    /// <summary>
    /// The outcome of one target check: why, what was hit, and — for a blocked shot — the hex that stopped it,
    /// which is what a client draws the impact marker on.
    /// </summary>
    public readonly struct TargetCheck
    {
        public readonly TargetRejectReason Reason;

        /// <summary>The body on the target hex, when there was one.</summary>
        public readonly IBody Victim;

        /// <summary>Where the ray or the flight was stopped; only meaningful when <see cref="HasBlockedAt"/>.</summary>
        public readonly Hex BlockedAt;

        public readonly bool HasBlockedAt;

        public bool Ok => Reason == TargetRejectReason.None;

        private TargetCheck(TargetRejectReason reason, IBody victim, Hex blockedAt, bool hasBlockedAt)
        {
            Reason = reason;
            Victim = victim;
            BlockedAt = blockedAt;
            HasBlockedAt = hasBlockedAt;
        }

        public static TargetCheck Accept(IBody victim) => new TargetCheck(TargetRejectReason.None, victim, default, false);

        public static TargetCheck Reject(TargetRejectReason reason, IBody victim = null)
            => new TargetCheck(reason, victim, default, false);

        public static TargetCheck Blocked(TargetRejectReason reason, IBody victim, Hex blockedAt)
            => new TargetCheck(reason, victim, blockedAt, true);

        public override string ToString() => Ok ? "Ok" : HasBlockedAt ? $"{Reason} at {BlockedAt}" : Reason.ToString();
    }

    /// <summary>
    /// Who an attack may hit: any damageable body that is not the attacker's own, inside the circular range
    /// band, with the attack's own sight requirement satisfied and its trajectory unobstructed (design:
    /// #attacks, #trajectories). <see cref="Enumerate"/> and <see cref="Check"/> share the rule so the HUD
    /// highlight, the server and the bot cannot disagree.
    /// </summary>
    public static class AttackTargeting
    {
        public static TargetCheck Check(TileMap map, BodySet bodies, HeightsDef heights, TrajectoryRegistry trajectories,
            Unit attacker, AttackDef attack, Hex target)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));
            if (heights == null) throw new ArgumentNullException(nameof(heights));
            if (trajectories == null) throw new ArgumentNullException(nameof(trajectories));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (attack == null) throw new ArgumentNullException(nameof(attack));

            IBody victim;
            if (!bodies.TryGetBodyAt(target, out victim)) return TargetCheck.Reject(TargetRejectReason.NoBody);
            if (!victim.IsDamageable) return TargetCheck.Reject(TargetRejectReason.NotDamageable, victim);
            if (victim.Owner == attacker.Owner) return TargetCheck.Reject(TargetRejectReason.OwnUnit, victim);
            if (!victim.IsAlive) return TargetCheck.Reject(TargetRejectReason.TargetDead, victim);
            if (!attack.InRangeSquared(Hex.EuclideanSquared(attacker.Position, target)))
                return TargetCheck.Reject(TargetRejectReason.OutOfRange, victim);

            int fromHeight = Sight.AimHeightAt(map, heights, attacker.Position, attacker.AimHeight);
            int toHeight = Sight.AimHeightAt(map, heights, target, victim.AimHeight);
            Hex blockedAt;

            if (attack.LineOfSight && !Sight.IsClear(map, bodies, heights, attacker.Position, fromHeight, target, toHeight, attacker.Id, victim.Id, out blockedAt))
                return TargetCheck.Blocked(TargetRejectReason.NoLineOfSight, victim, blockedAt);

            var ctx = new TrajectoryContext(map, bodies, heights, attacker.Position, fromHeight, target, toHeight,
                attack.Apex, attacker.Id, victim.Id);
            if (!trajectories.IsClear(attack.Trajectory, in ctx, out blockedAt))
                return TargetCheck.Blocked(TargetRejectReason.TrajectoryBlocked, victim, blockedAt);

            return TargetCheck.Accept(victim);
        }

        /// <summary>Every legal target, in <see cref="BodySet.All"/> order (units then props).</summary>
        public static void Enumerate(TileMap map, BodySet bodies, HeightsDef heights, TrajectoryRegistry trajectories,
            Unit attacker, AttackDef attack, List<IBody> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            IReadOnlyList<IBody> all = bodies.All;
            for (int i = 0; i < all.Count; i++)
            {
                IBody candidate = all[i];
                if (!candidate.IsAlive) continue;
                TargetCheck check = Check(map, bodies, heights, trajectories, attacker, attack, candidate.Position);
                if (check.Ok && ReferenceEquals(check.Victim, candidate)) into.Add(candidate);
            }
        }
    }
}
