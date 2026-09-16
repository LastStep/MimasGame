using System;
using Mimas.Core.Combat;
using Mimas.Core.Movement;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Something that happened in a match. Commands go in, events come out (ADR-010); the client animates
    /// events and the server filters them per player (<see cref="EventFilter"/>). Events carry the
    /// numbers a client needs to stay in sync (hp after, ap remaining) so it never recomputes rules.
    /// </summary>
    public abstract class MatchEvent
    {
    }

    /// <summary>A new turn began; every unit of <see cref="Player"/> has its action points refreshed.</summary>
    public sealed class TurnStartedEvent : MatchEvent
    {
        public int Player { get; }

        /// <summary>1-based, counts every turn of both players.</summary>
        public int TurnNumber { get; }

        public TurnStartedEvent(int player, int turnNumber)
        {
            Player = player;
            TurnNumber = turnNumber;
        }
    }

    /// <summary>A unit travelled along an already-validated <see cref="MovePlan"/>.</summary>
    public sealed class UnitMovedEvent : MatchEvent
    {
        public int UnitId { get; }

        /// <summary>Which movement ability was used (a <see cref="MovementDef.Id"/>), or null for forced movement.</summary>
        public string MovementId { get; }

        public MovePlan Plan { get; }

        public UnitMovedEvent(int unitId, string movementId, MovePlan plan)
        {
            UnitId = unitId;
            MovementId = movementId;
            Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        }
    }

    /// <summary>Action points were spent on an ability.</summary>
    public sealed class ApSpentEvent : MatchEvent
    {
        public int UnitId { get; }
        public string AbilityId { get; }
        public int Amount { get; }
        public int Remaining { get; }

        public ApSpentEvent(int unitId, string abilityId, int amount, int remaining)
        {
            UnitId = unitId;
            AbilityId = abilityId;
            Amount = amount;
            Remaining = remaining;
        }
    }

    /// <summary>
    /// An attack landed. <see cref="Breakdown"/> is the full arithmetic as the server saw it; the event
    /// filter trims lines a recipient may not know. <see cref="Damage"/> is the hit points actually lost.
    /// </summary>
    public sealed class AttackResolvedEvent : MatchEvent
    {
        public int AttackerId { get; }
        public int TargetId { get; }
        public string AbilityId { get; }
        public DamageBreakdown Breakdown { get; }
        public int Damage { get; }
        public int TargetHpAfter { get; }

        /// <summary>True when <see cref="TargetId"/> names a prop rather than a hero.</summary>
        public bool TargetIsProp { get; }

        public AttackResolvedEvent(int attackerId, int targetId, string abilityId, DamageBreakdown breakdown, int damage, int targetHpAfter, bool targetIsProp = false)
        {
            AttackerId = attackerId;
            TargetId = targetId;
            AbilityId = abilityId ?? throw new ArgumentNullException(nameof(abilityId));
            Breakdown = breakdown ?? throw new ArgumentNullException(nameof(breakdown));
            Damage = damage;
            TargetHpAfter = targetHpAfter;
            TargetIsProp = targetIsProp;
        }
    }

    /// <summary>A hidden ability was used for the first time in front of <see cref="ToPlayer"/>.</summary>
    public sealed class AbilityRevealedEvent : MatchEvent
    {
        public int UnitId { get; }
        public string AbilityId { get; }
        public int ToPlayer { get; }

        public AbilityRevealedEvent(int unitId, string abilityId, int toPlayer)
        {
            UnitId = unitId;
            AbilityId = abilityId ?? throw new ArgumentNullException(nameof(abilityId));
            ToPlayer = toPlayer;
        }
    }

    /// <summary>A hidden modifier changed a result for the first time in front of <see cref="ToPlayer"/>.</summary>
    public sealed class ModifierRevealedEvent : MatchEvent
    {
        public int UnitId { get; }
        public string ModifierId { get; }
        public int ToPlayer { get; }

        public ModifierRevealedEvent(int unitId, string modifierId, int toPlayer)
        {
            UnitId = unitId;
            ModifierId = modifierId ?? throw new ArgumentNullException(nameof(modifierId));
            ToPlayer = toPlayer;
        }
    }

    /// <summary>A prop ran out of hit points and was removed; its tile is enterable again (design: #props).</summary>
    public sealed class PropDestroyedEvent : MatchEvent
    {
        public int PropId { get; }

        public PropDestroyedEvent(int propId)
        {
            PropId = propId;
        }
    }

    public sealed class UnitDiedEvent : MatchEvent
    {
        public int UnitId { get; }

        public UnitDiedEvent(int unitId)
        {
            UnitId = unitId;
        }
    }

    /// <summary>The turn passed. <see cref="Acted"/> is false when nothing was done (a skip: the client's idle penalty keys off it).</summary>
    public sealed class TurnEndedEvent : MatchEvent
    {
        public int Player { get; }
        public int TurnNumber { get; }
        public EndTurnReason Reason { get; }
        public bool Acted { get; }

        public TurnEndedEvent(int player, int turnNumber, EndTurnReason reason, bool acted)
        {
            Player = player;
            TurnNumber = turnNumber;
            Reason = reason;
            Acted = acted;
        }
    }

    public enum MatchEndReason
    {
        /// <summary>Every unit of the loser is dead.</summary>
        Elimination = 0,
    }

    public sealed class MatchEndedEvent : MatchEvent
    {
        public int Winner { get; }
        public MatchEndReason Reason { get; }

        public MatchEndedEvent(int winner, MatchEndReason reason)
        {
            Winner = winner;
            Reason = reason;
        }
    }
}
