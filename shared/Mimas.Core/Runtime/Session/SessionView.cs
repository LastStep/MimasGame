using System;
using System.Collections.Generic;
using Mimas.Core.Match;

namespace Mimas.Core.Session
{
    /// <summary>
    /// The session as one player may see it (design: #session, #hidden-info): the score, the phase, their
    /// own build, the opponent's lineage and boons as far as revealed, their own offers, who has picked,
    /// and the round's <see cref="PlayerView"/> while a round runs. Part 2 puts it on the wire.
    /// </summary>
    public sealed class SessionView
    {
        private readonly List<string> _myOffers;
        private readonly List<KnownEntry> _opponentBoons;

        public int Viewer { get; }
        public int Round { get; }
        public SessionPhase Phase { get; }
        public int Score0 { get; }
        public int Score1 { get; }
        public bool IsOver { get; }
        public int Winner { get; }

        /// <summary><c>bestOf / 2 + 1</c>: how many rounds the score line is counting towards.</summary>
        public int RoundsToWin { get; }

        /// <summary>The map the board shows: the running round's, or — between rounds — the next round's. Null once the session is over.</summary>
        public string MapId { get; }

        /// <summary>The map the next round will play, null when the session is over. What a reconnect during a draft builds its dimmed board from.</summary>
        public string NextMapId { get; }

        public PlayerBuild MyBuild { get; }

        /// <summary>The opponent's lineage once revealed, else null.</summary>
        public string OpponentLineageId { get; }

        /// <summary>The opponent's boons in grant order; hidden ones have a null id, so the count is public.</summary>
        public IReadOnlyList<KnownEntry> OpponentBoons => _opponentBoons;

        /// <summary>The viewer's offers in the current draft; empty otherwise.</summary>
        public IReadOnlyList<string> MyOffers => _myOffers;

        public bool IHavePicked { get; }
        public bool OpponentHasPicked { get; }

        /// <summary>The round's view, or null between rounds and after the end.</summary>
        public PlayerView Match { get; }

        private SessionView(int viewer, int round, SessionPhase phase, int score0, int score1, int roundsToWin, bool isOver, int winner,
            string mapId, string nextMapId, PlayerBuild myBuild,
            string opponentLineageId, List<KnownEntry> opponentBoons, List<string> myOffers, bool iHavePicked, bool opponentHasPicked, PlayerView match)
        {
            Viewer = viewer;
            Round = round;
            Phase = phase;
            Score0 = score0;
            Score1 = score1;
            RoundsToWin = roundsToWin;
            IsOver = isOver;
            Winner = winner;
            MapId = mapId;
            NextMapId = nextMapId;
            MyBuild = myBuild;
            OpponentLineageId = opponentLineageId;
            _opponentBoons = opponentBoons;
            _myOffers = myOffers;
            IHavePicked = iHavePicked;
            OpponentHasPicked = opponentHasPicked;
            Match = match;
        }

        public static SessionView For(Session session, int viewer)
        {
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (viewer < 0 || viewer >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            int opponent = 1 - viewer;
            PlayerBuild theirs = session.BuildOf(opponent);

            // The most recent round's state knows everything learned so far: it imported every earlier entry.
            MatchState knowledge = session.LastMatch;
            var boons = new List<KnownEntry>(theirs.BoonIds.Count);
            for (int i = 0; i < theirs.BoonIds.Count; i++)
            {
                bool known = knowledge != null && knowledge.KnowsBoon(viewer, opponent, theirs.BoonIds[i]);
                boons.Add(new KnownEntry(known ? theirs.BoonIds[i] : null));
            }
            string lineage = knowledge != null && knowledge.KnowsLineage(viewer, opponent, theirs.LineageId) ? theirs.LineageId : null;

            // Round n plays ladder position ((n − 1) mod count) + 1, so round n + 1 plays index n mod count (D17).
            string nextMapId = session.IsOver ? null : session.Ladder[session.Round % session.Ladder.Count];
            string mapId = session.Match != null ? session.Match.MapData.Id : nextMapId;

            return new SessionView(viewer, session.Round, session.Phase, session.Score(0), session.Score(1), session.RoundsToWin, session.IsOver, session.Winner,
                mapId, nextMapId, session.BuildOf(viewer), lineage, boons, new List<string>(session.OffersOf(viewer)),
                session.HasPicked(viewer), session.HasPicked(opponent), session.Match != null ? session.Match.ViewFor(viewer) : null);
        }

        /// <summary>
        /// A session view assembled from parts — the twin of <see cref="PlayerView.Create"/>, and what
        /// <c>Wire.ReadSession</c> calls. A client never builds one any other way;
        /// <see cref="For"/> is the only path from a live <see cref="Session"/>.
        /// </summary>
        public static SessionView Create(int viewer, int round, SessionPhase phase, int score0, int score1, int roundsToWin, bool isOver, int winner,
            string mapId, string nextMapId, PlayerBuild myBuild, string opponentLineageId, List<KnownEntry> opponentBoons,
            List<string> myOffers, bool iHavePicked, bool opponentHasPicked, PlayerView match)
        {
            if (viewer < 0 || viewer >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            if (myBuild == null) throw new ArgumentNullException(nameof(myBuild));
            return new SessionView(viewer, round, phase, score0, score1, roundsToWin, isOver, winner, mapId, nextMapId, myBuild,
                opponentLineageId, opponentBoons ?? new List<KnownEntry>(), myOffers ?? new List<string>(), iHavePicked, opponentHasPicked, match);
        }
    }
}
