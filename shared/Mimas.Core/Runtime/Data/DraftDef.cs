using System;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// The draft between rounds (<c>rules.json</c> <c>draft</c>, design: #draft): how many boons each role is
    /// offered and how long the host waits before it picks the first offer on a player's behalf. The counts
    /// are data so a comeback draft is a number change (design: <c>q-draft-comeback</c>, closed 21 Sep 2026
    /// as symmetric, 3 / 3). Core never reads a clock: <c>timeoutMs</c> is for the host that submits the
    /// timeout command.
    /// </summary>
    public sealed class DraftDef
    {
        /// <summary>Offers for the player who won the previous round. 0 or more.</summary>
        public int OffersWinner { get; }

        /// <summary>Offers for the player who lost the previous round. 0 or more.</summary>
        public int OffersLoser { get; }

        /// <summary>How long a host waits for a pick before submitting a timeout pick. 0 or more.</summary>
        public int TimeoutMs { get; }

        public DraftDef(int offersWinner, int offersLoser, int timeoutMs)
        {
            if (offersWinner < 0) throw new ArgumentOutOfRangeException(nameof(offersWinner), "draft.offers.winner must not be negative.");
            if (offersLoser < 0) throw new ArgumentOutOfRangeException(nameof(offersLoser), "draft.offers.loser must not be negative.");
            if (timeoutMs < 0) throw new ArgumentOutOfRangeException(nameof(timeoutMs), "draft.timeoutMs must not be negative.");
            OffersWinner = offersWinner;
            OffersLoser = offersLoser;
            TimeoutMs = timeoutMs;
        }

        /// <summary>The offer count for a role.</summary>
        public int OffersFor(bool wonPreviousRound) => wonPreviousRound ? OffersWinner : OffersLoser;

        /// <summary>Three offers each and a 20 s timer: the fallback for fixtures that build a <see cref="RulesDef"/> by hand.</summary>
        public static DraftDef Default() => new DraftDef(3, 3, 20000);

        internal static DraftDef FromJsonAt(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            if (!(MapJson.Require(obj, "offers", where) is JObject offers))
                throw new MapLoadException($"{where}.offers must be an object with 'winner' and 'loser'.");
            int winner = MapJson.RequireInt(offers, "winner", where + ".offers");
            int loser = MapJson.RequireInt(offers, "loser", where + ".offers");
            int timeoutMs = MapJson.RequireInt(obj, "timeoutMs", where);
            if (winner < 0) throw new MapLoadException($"{where}.offers.winner must not be negative.");
            if (loser < 0) throw new MapLoadException($"{where}.offers.loser must not be negative.");
            if (timeoutMs < 0) throw new MapLoadException($"{where}.timeoutMs must not be negative.");
            return new DraftDef(winner, loser, timeoutMs);
        }

        public override string ToString() => $"offers {OffersWinner} / {OffersLoser}, timeout {TimeoutMs} ms";
    }
}
