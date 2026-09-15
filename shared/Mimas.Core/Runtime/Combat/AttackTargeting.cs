using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Movement;
using Mimas.Core.Units;

namespace Mimas.Core.Combat
{
    /// <summary>Why a target was refused. <see cref="None"/> means it is a legal target.</summary>
    public enum TargetRejectReason
    {
        None = 0,
        NoUnit,
        NotEnemy,
        TargetDead,
        OutOfRange,
        NoLineOfSight,
    }

    /// <summary>
    /// Who an attack may hit: a living enemy unit inside the range band with a clear line of sight (always
    /// required, see docs/design.md). <see cref="Enumerate"/> and <see cref="Check"/> share the rule so the
    /// HUD highlight, the server and the bot cannot disagree.
    /// </summary>
    public static class AttackTargeting
    {
        public static TargetRejectReason Check(TileMap map, UnitSet units, Unit attacker, AttackDef attack, Hex target, out Unit victim)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (units == null) throw new ArgumentNullException(nameof(units));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (attack == null) throw new ArgumentNullException(nameof(attack));

            if (!units.TryGetUnitAt(target, out victim)) return TargetRejectReason.NoUnit;
            if (victim.Owner == attacker.Owner) return TargetRejectReason.NotEnemy;
            if (!victim.IsAlive) return TargetRejectReason.TargetDead;
            if (!attack.InRange(Hex.Distance(attacker.Position, target))) return TargetRejectReason.OutOfRange;
            if (!LineOfSight.IsClear(map, attacker.Position, target)) return TargetRejectReason.NoLineOfSight;
            return TargetRejectReason.None;
        }

        /// <summary>Every legal target, in unit-set order.</summary>
        public static void Enumerate(TileMap map, UnitSet units, Unit attacker, AttackDef attack, List<Unit> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            for (int i = 0; i < units.All.Count; i++)
            {
                Unit candidate = units.All[i];
                Unit unused;
                if (Check(map, units, attacker, attack, candidate.Position, out unused) == TargetRejectReason.None && unused == candidate)
                    into.Add(candidate);
            }
        }
    }
}
