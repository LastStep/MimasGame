using Mimas.Core.Match;

namespace Mimas.Core.Session
{
    public enum DraftPickReason
    {
        /// <summary>The player chose.</summary>
        Player = 0,

        /// <summary>The draft timer ran out; the host submitted offer 0 on the player's behalf (design: #draft rule 4). Core never reads a clock.</summary>
        Timeout = 1,
    }

    /// <summary>
    /// Keep one of the boons on offer (design: #draft). A timeout is the same command with a different
    /// reason, submitted by the host, so a replay of the command list reproduces the session without a
    /// clock. Legal only in the <see cref="SessionPhase.Draft"/> phase, once per player.
    /// </summary>
    public sealed class DraftPickCommand : Command
    {
        /// <summary>Index into the player's offer list. Any value is accepted when the player has no offers (a pick of nothing).</summary>
        public int OfferIndex { get; }

        public DraftPickReason Reason { get; }

        public DraftPickCommand(int player, int offerIndex, DraftPickReason reason = DraftPickReason.Player) : base(player)
        {
            OfferIndex = offerIndex;
            Reason = reason;
        }

        public override string ToString() => $"P{Player} picks offer {OfferIndex} ({Reason})";
    }
}
