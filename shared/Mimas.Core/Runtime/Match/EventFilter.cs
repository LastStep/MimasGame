using System;
using System.Collections.Generic;
using Mimas.Core.Combat;
using Mimas.Core.Units;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Trims an event stream to what one player may receive (ADR-010). Reveal events go only to the player
    /// they inform; damage breakdowns lose the hidden lines that player has not been shown. Everything else
    /// is public by construction (positions and hit points are visible to both sides). Apply this on the
    /// server before sending and on a local session before handing events to presentation, so the client
    /// never sees more than the network version would deliver.
    /// </summary>
    public static class EventFilter
    {
        /// <summary>The event as <paramref name="viewer"/> receives it, or null when they receive nothing.</summary>
        public static MatchEvent ForPlayer(MatchEvent e, int viewer, MatchState state)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (state == null) throw new ArgumentNullException(nameof(state));

            switch (e)
            {
                case AbilityRevealedEvent ability:
                    return ability.ToPlayer == viewer ? e : null;
                case ModifierRevealedEvent modifier:
                    return modifier.ToPlayer == viewer ? e : null;
                case BoonRevealedEvent boon:
                    return boon.ToPlayer == viewer ? e : null;
                case LineageRevealedEvent lineage:
                    return lineage.ToPlayer == viewer ? e : null;
                case AttackResolvedEvent attack:
                    return new AttackResolvedEvent(attack.AttackerId, attack.TargetId, attack.AbilityId,
                        Trim(attack.Breakdown, viewer, state), attack.Damage, attack.TargetHpAfter, attack.TargetIsProp);
                default:
                    return e;
            }
        }

        /// <summary>Filters a whole list, dropping events the viewer does not receive.</summary>
        public static void ForPlayer(IReadOnlyList<MatchEvent> events, int viewer, MatchState state, List<MatchEvent> into)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (into == null) throw new ArgumentNullException(nameof(into));
            for (int i = 0; i < events.Count; i++)
            {
                MatchEvent filtered = ForPlayer(events[i], viewer, state);
                if (filtered != null) into.Add(filtered);
            }
        }

        /// <summary>Drops hidden lines the viewer has not been shown. Hidden lines that applied are revealed first by <see cref="MatchState.Apply"/>, so normally nothing is dropped.</summary>
        private static DamageBreakdown Trim(DamageBreakdown breakdown, int viewer, MatchState state)
        {
            var lines = new List<DamageLine>(breakdown.Lines.Count);
            int unknown = breakdown.UnknownCount;
            for (int i = 0; i < breakdown.Lines.Count; i++)
            {
                DamageLine line = breakdown.Lines[i];
                if (line.Hidden)
                {
                    // Only a unit can own a hidden line; a prop's defence is public like the prop itself.
                    // A boon's line is keyed by the boon, a modifier's or an immunity's by the modifier.
                    Unit owner;
                    bool visible = state.Units.TryGet(line.OwnerUnitId, out owner)
                        && (owner.Owner == viewer
                            || (line.Kind == DamageLineKind.BoonStat ? state.KnowsBoon(viewer, owner.Id, line.Id) : state.Knows(viewer, owner.Id, line.Id)));
                    if (!visible)
                    {
                        unknown++;
                        continue;
                    }
                }
                lines.Add(line);
            }
            return new DamageBreakdown(lines, unknown);
        }
    }
}
