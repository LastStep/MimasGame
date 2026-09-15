using System;
using Mimas.Core.Movement;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Something that happened in a match. Commands go in, events come out (ADR-010); the client animates
    /// events and the server filters them per player. The full event vocabulary arrives with the turn
    /// structure; movement is the first member so its shape is settled early.
    /// </summary>
    public abstract class MatchEvent
    {
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
}
