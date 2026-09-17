using System;
using Mimas.Core.Geometry;

namespace Mimas.Core.Match
{
    /// <summary>
    /// A player's intent, the only way state changes (ADR-010). Commands are validated by
    /// <see cref="MatchState.Validate"/> and applied by <see cref="MatchState.Apply"/>; the same
    /// objects are what a bot enumerates and what the server receives over the wire.
    /// </summary>
    public abstract class Command
    {
        /// <summary>The player acting.</summary>
        public int Player { get; }

        protected Command(int player)
        {
            if (player < 0) throw new ArgumentOutOfRangeException(nameof(player));
            Player = player;
        }
    }

    /// <summary>Move a unit with one of its movement abilities.</summary>
    public sealed class MoveCommand : Command
    {
        public int UnitId { get; }
        public string AbilityId { get; }
        public Hex Destination { get; }

        public MoveCommand(int player, int unitId, string abilityId, Hex destination) : base(player)
        {
            UnitId = unitId;
            AbilityId = abilityId ?? throw new ArgumentNullException(nameof(abilityId));
            Destination = destination;
        }

        public override string ToString() => $"P{Player} unit {UnitId} {AbilityId} -> {Destination}";
    }

    /// <summary>Attack the unit standing on a hex with one of the unit's attack abilities.</summary>
    public sealed class AttackCommand : Command
    {
        public int UnitId { get; }
        public string AbilityId { get; }
        public Hex Target { get; }

        public AttackCommand(int player, int unitId, string abilityId, Hex target) : base(player)
        {
            UnitId = unitId;
            AbilityId = abilityId ?? throw new ArgumentNullException(nameof(abilityId));
            Target = target;
        }

        public override string ToString() => $"P{Player} unit {UnitId} {AbilityId} @ {Target}";
    }

    public enum EndTurnReason
    {
        /// <summary>The player pressed End Turn.</summary>
        Player = 0,

        /// <summary>The clock ran out; the server submitted the command on the player's behalf.</summary>
        Timeout = 1,
    }

    /// <summary>
    /// Pass the turn. A timeout is the same command with a different reason, submitted by the server, so a
    /// replay of the command list reproduces the match without any clock.
    /// </summary>
    public sealed class EndTurnCommand : Command
    {
        public EndTurnReason Reason { get; }

        public EndTurnCommand(int player, EndTurnReason reason = EndTurnReason.Player) : base(player)
        {
            Reason = reason;
        }

        public override string ToString() => $"P{Player} end turn ({Reason})";
    }

    public enum ResignReason
    {
        /// <summary>The player pressed Resign.</summary>
        Player = 0,

        /// <summary>The player did not come back inside the reconnect grace; the server submitted it for them.</summary>
        Disconnect = 1,
    }

    /// <summary>
    /// Concede the match. Legal for either player at any moment while the match runs, including off turn
    /// (design: #win-conditions, #online). A disconnect forfeit is the same command with a different reason,
    /// submitted by the server, so a replay of the command list reproduces the match without any sockets.
    /// Never enumerated for a bot.
    /// </summary>
    public sealed class ResignCommand : Command
    {
        public ResignReason Reason { get; }

        public ResignCommand(int player, ResignReason reason = ResignReason.Player) : base(player)
        {
            Reason = reason;
        }

        public override string ToString() => $"P{Player} resigns ({Reason})";
    }
}
