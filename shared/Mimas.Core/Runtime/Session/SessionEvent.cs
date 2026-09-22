using System;
using System.Collections.Generic;
using Mimas.Core.Match;

namespace Mimas.Core.Session
{
    /// <summary>
    /// What happened at the session level (design: #session, #draft). Subclasses of <see cref="MatchEvent"/>
    /// so one list and one filter carry a round's events and the session's together. Part 2 puts them on
    /// the wire; until then <see cref="object.ToString"/> is what a replay log compares.
    /// </summary>
    public abstract class SessionEvent : MatchEvent
    {
    }

    /// <summary>A round began on a ladder map; the match's own start events follow.</summary>
    public sealed class RoundStartedEvent : SessionEvent
    {
        /// <summary>1-based.</summary>
        public int Round { get; }
        public string MapId { get; }
        public int FirstPlayer { get; }

        public RoundStartedEvent(int round, string mapId, int firstPlayer)
        {
            Round = round;
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            FirstPlayer = firstPlayer;
        }

        public override string ToString() => $"round {Round} started on {MapId}, P{FirstPlayer} first";
    }

    /// <summary>A round ended and was scored. Immediately after the <see cref="MatchEndedEvent"/> that ended it.</summary>
    public sealed class RoundEndedEvent : SessionEvent
    {
        public int Round { get; }
        public int Winner { get; }
        public MatchEndReason Reason { get; }
        public int Score0 { get; }
        public int Score1 { get; }

        public RoundEndedEvent(int round, int winner, MatchEndReason reason, int score0, int score1)
        {
            Round = round;
            Winner = winner;
            Reason = reason;
            Score0 = score0;
            Score1 = score1;
        }

        public override string ToString() => $"round {Round} ended: P{Winner} wins by {Reason}, score {Score0}-{Score1}";
    }

    /// <summary>
    /// The draft opened with each player's offers. The filter leaves a viewer only their own list and passes
    /// null for the other, and null it stays all the way onto the wire: "not yours" and "you were offered
    /// nothing" are different facts, and only the second one is a player's business.
    /// </summary>
    public sealed class DraftStartedEvent : SessionEvent
    {
        private readonly List<string> _offers0;
        private readonly List<string> _offers1;

        /// <summary>The round that just ended.</summary>
        public int Round { get; }

        /// <summary>Player 0's offers, or null when this copy was filtered for player 1.</summary>
        public IReadOnlyList<string> Offers0 => _offers0;

        /// <summary>Player 1's offers, or null when this copy was filtered for player 0.</summary>
        public IReadOnlyList<string> Offers1 => _offers1;

        public DraftStartedEvent(int round, IReadOnlyList<string> offers0, IReadOnlyList<string> offers1)
        {
            Round = round;
            _offers0 = offers0 != null ? new List<string>(offers0) : null;
            _offers1 = offers1 != null ? new List<string>(offers1) : null;
        }

        public IReadOnlyList<string> OffersOf(int player)
        {
            if (player < 0 || player >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
            return player == 0 ? _offers0 : _offers1;
        }

        public override string ToString() => $"draft after round {Round}: P0 [{Names(_offers0)}] P1 [{Names(_offers1)}]";

        private static string Names(List<string> offers) => offers != null ? string.Join(", ", offers) : "hidden";
    }

    /// <summary>A player picked (or timed out onto offer 0, or had nothing to pick). <see cref="BoonId"/> is null for the opponent's copy, and for a pick of nothing.</summary>
    public sealed class DraftPickedEvent : SessionEvent
    {
        public int Player { get; }
        public string BoonId { get; }
        public DraftPickReason Reason { get; }

        public DraftPickedEvent(int player, string boonId, DraftPickReason reason)
        {
            Player = player;
            BoonId = boonId;
            Reason = reason;
        }

        public override string ToString() => $"P{Player} picked {BoonId ?? "?"} ({Reason})";
    }

    /// <summary>Why a session stopped (design: #session rule 7; decided 22 Sep 2026, P8).</summary>
    public enum SessionEndReason
    {
        /// <summary>Someone won the rounds needed.</summary>
        Score = 0,

        /// <summary>A player resigned, in a round or in a draft: the whole series is conceded.</summary>
        Resign = 1,

        /// <summary>A player left and did not come back within the grace period.</summary>
        Forfeit = 2,
    }

    /// <summary>A player reached the rounds needed to win, or the other one conceded the series. The last event of a session.</summary>
    public sealed class SessionEndedEvent : SessionEvent
    {
        public int Winner { get; }
        public int Score0 { get; }
        public int Score1 { get; }
        public SessionEndReason Reason { get; }

        public SessionEndedEvent(int winner, int score0, int score1, SessionEndReason reason = SessionEndReason.Score)
        {
            Winner = winner;
            Score0 = score0;
            Score1 = score1;
            Reason = reason;
        }

        public override string ToString() => $"session over: P{Winner} wins {Score0}-{Score1} by {Reason}";
    }
}
