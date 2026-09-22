using System;
using System.Collections.Generic;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;
using Mimas.Core.Movement;

namespace Mimas.Core.Session
{
    /// <summary>What a session starts from: two builds, each with a lineage. Boons may be pre-owned (tests, and a later reconnect path).</summary>
    public sealed class SessionSetup
    {
        private readonly PlayerBuild[] _builds = new PlayerBuild[MatchSetup.PlayerCount];

        public SessionSetup(PlayerBuild player0, PlayerBuild player1)
        {
            _builds[0] = player0 ?? throw new ArgumentNullException(nameof(player0));
            _builds[1] = player1 ?? throw new ArgumentNullException(nameof(player1));
            for (int p = 0; p < MatchSetup.PlayerCount; p++)
                if (_builds[p].LineageId == null) throw new ArgumentException($"Player {p}'s build has no lineage; a session needs one for the draft.", p == 0 ? nameof(player0) : nameof(player1));
        }

        public PlayerBuild BuildOf(int player)
        {
            if (player < 0 || player >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
            return _builds[player];
        }
    }

    public enum SessionPhase
    {
        /// <summary>A round is being played (or, before <see cref="Session.Start"/>, is about to be).</summary>
        Round = 0,

        /// <summary>Between rounds: each player picks one boon.</summary>
        Draft = 1,

        /// <summary>Someone won the majority of rounds.</summary>
        Over = 2,
    }

    /// <summary>
    /// A best-of-N session as a pure state machine (design: #session, #round, #draft; ADR-035): it owns the
    /// two builds, the score, the ladder, the revealed knowledge that outlives a round, and the draft
    /// between rounds; it constructs one <see cref="MatchState"/> per round with a seed drawn from its own
    /// <see cref="Rng"/>, imports what each player already knows, and reads it back when the round ends.
    /// Commands go through <see cref="Apply"/>; events come out as one list that
    /// <see cref="SessionEventFilter"/> trims per viewer. Same catalogue, setup, seed and commands give the
    /// same events on every peer; the host (part 2) submits timeouts as commands and never lets Core see a clock.
    /// </summary>
    public sealed class Session
    {
        private readonly ContentCatalog _catalog;
        private readonly MovementResolverRegistry _resolvers;
        private readonly TrajectoryRegistry _trajectories;
        private readonly PlayerBuild[] _builds = new PlayerBuild[MatchSetup.PlayerCount];
        private readonly int[] _score = new int[MatchSetup.PlayerCount];
        private readonly List<string>[] _offers = { new List<string>(), new List<string>() };
        private readonly bool[] _picked = new bool[MatchSetup.PlayerCount];
        private readonly List<RevealedEntry> _revealed = new List<RevealedEntry>();
        private readonly List<string> _ladder = new List<string>();
        private readonly List<Command> _legalScratch = new List<Command>();

        public ContentCatalog Catalog => _catalog;

        /// <summary>The session's own generator: the coin flip, every round's seed, every draft's shuffle.</summary>
        public Rng Rng { get; }

        public SessionPhase Phase { get; private set; }

        /// <summary>1-based; 0 before <see cref="Start"/>.</summary>
        public int Round { get; private set; }

        /// <summary>The current round's state; null in <see cref="SessionPhase.Draft"/> and <see cref="SessionPhase.Over"/>.</summary>
        public MatchState Match => Phase == SessionPhase.Round ? LastMatch : null;

        /// <summary>The most recent round's state, kept after the round ends so its events can still be filtered with its knowledge. Null before the first round.</summary>
        public MatchState LastMatch { get; private set; }

        /// <summary>Map ids in ladder order: by <c>ladderPosition</c>, then id. Round n plays position <c>((n − 1) mod count) + 1</c>: the ladder wraps (D17).</summary>
        public IReadOnlyList<string> Ladder => _ladder;

        /// <summary><c>bestOf / 2 + 1</c>, from <c>rules.series</c>.</summary>
        public int RoundsToWin { get; }

        /// <summary>Who lost the previous round and therefore moves first in the next (D13); -1 before a round has ended.</summary>
        public int LastRoundLoser { get; private set; } = -1;

        public bool IsOver => Phase == SessionPhase.Over;

        /// <summary>The session's winner, or -1 while it runs.</summary>
        public int Winner { get; private set; } = -1;

        /// <summary>Everything each player has learned so far, in the order it was learned. What each round imports.</summary>
        public IReadOnlyList<RevealedEntry> RevealedEntries => _revealed;

        public Session(ContentCatalog catalog, SessionSetup setup, uint seed, MovementResolverRegistry resolvers = null, TrajectoryRegistry trajectories = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            if (setup == null) throw new ArgumentNullException(nameof(setup));
            _resolvers = resolvers;
            _trajectories = trajectories;
            Rng = new Rng(seed);
            RoundsToWin = catalog.Rules.Series.RoundsToWin;

            for (int p = 0; p < MatchSetup.PlayerCount; p++)
            {
                PlayerBuild build = setup.BuildOf(p);
                LineageDef lineage = catalog.GetLineage(build.LineageId);
                // The starting Blessing is an ordinary Blessing granted at select (design: #boons rule 4); appended once.
                _builds[p] = build.HasBoon(lineage.StartingBlessingId) ? build : build.WithBoon(lineage.StartingBlessingId);
                for (int b = 0; b < _builds[p].BoonIds.Count; b++) catalog.GetBoon(_builds[p].BoonIds[b]);
            }

            var maps = new List<MapData>(catalog.Maps.All);
            maps.Sort((a, b) => a.LadderPosition != b.LadderPosition ? a.LadderPosition.CompareTo(b.LadderPosition) : string.CompareOrdinal(a.Id, b.Id));
            if (maps.Count == 0) throw new ArgumentException("The catalogue has no maps to make a ladder from.", nameof(catalog));
            for (int i = 0; i < maps.Count; i++) _ladder.Add(maps[i].Id);

            Phase = SessionPhase.Round;
        }

        public int Score(int player) => _score[CheckPlayer(player)];

        /// <summary>The player's build as it stands: the starting Blessing first, then every pick.</summary>
        public PlayerBuild BuildOf(int player) => _builds[CheckPlayer(player)];

        /// <summary>The player's current offers; empty outside a draft. Index 0 is what a timeout picks.</summary>
        public IReadOnlyList<string> OffersOf(int player) => _offers[CheckPlayer(player)];

        public bool HasPicked(int player) => _picked[CheckPlayer(player)];

        /// <summary>Starts round 1. Call exactly once.</summary>
        public IReadOnlyList<MatchEvent> Start()
        {
            if (Round != 0) throw new InvalidOperationException("The session has already started.");
            var events = new List<MatchEvent>();
            StartRound(events);
            return events;
        }

        private void StartRound(List<MatchEvent> events)
        {
            Round++;
            string mapId = _ladder[(Round - 1) % _ladder.Count];
            int first = Round == 1 ? Rng.Range(0, MatchSetup.PlayerCount) : LastRoundLoser;
            uint seed = Rng.NextUInt();

            var setup = new MatchSetup(mapId, _builds[0], _builds[1], first);
            for (int i = 0; i < _revealed.Count; i++) setup.WithRevealed(_revealed[i].Viewer, _revealed[i].UnitId, _revealed[i].Id);

            LastMatch = new MatchState(_catalog, setup, seed, _resolvers, _trajectories);
            Phase = SessionPhase.Round;
            for (int p = 0; p < MatchSetup.PlayerCount; p++) { _offers[p].Clear(); _picked[p] = false; }

            events.Add(new RoundStartedEvent(Round, mapId, first));
            events.AddRange(LastMatch.Start());
        }

        // ---- validation ----------------------------------------------------------------------------

        public CommandResult Validate(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            var pick = command as DraftPickCommand;
            switch (Phase)
            {
                case SessionPhase.Over:
                    return CommandResult.Reject(CommandRejectReason.MatchOver);
                case SessionPhase.Round:
                    if (pick != null || Round == 0) return CommandResult.Reject(CommandRejectReason.WrongPhase);
                    return LastMatch.Validate(command);
                default:
                    if (pick == null) return CommandResult.Reject(CommandRejectReason.WrongPhase);
                    if (pick.Player >= MatchSetup.PlayerCount) return CommandResult.Reject(CommandRejectReason.WrongPhase);
                    if (_offers[pick.Player].Count == 0) return CommandResult.Accepted;      // a pick of nothing (§13)
                    if (_picked[pick.Player]) return CommandResult.Reject(CommandRejectReason.AlreadyPicked);
                    if (pick.OfferIndex < 0 || pick.OfferIndex >= _offers[pick.Player].Count) return CommandResult.Reject(CommandRejectReason.BadOffer);
                    return CommandResult.Accepted;
            }
        }

        // ---- application ---------------------------------------------------------------------------

        /// <summary>Applies a command. Throws <see cref="InvalidOperationException"/> when it does not validate; call <see cref="Validate"/> first.</summary>
        public IReadOnlyList<MatchEvent> Apply(Command command)
        {
            CommandResult check = Validate(command);
            if (!check.Ok) throw new InvalidOperationException($"Command {command} rejected: {check}");

            var events = new List<MatchEvent>();
            if (Phase == SessionPhase.Round)
            {
                events.AddRange(LastMatch.Apply(command));
                MatchEndedEvent ended = null;
                for (int i = 0; i < events.Count; i++) if (events[i] is MatchEndedEvent e) ended = e;
                if (ended != null) EndRound(ended, events);
                return events;
            }

            var pick = (DraftPickCommand)command;
            if (_offers[pick.Player].Count > 0)
            {
                string boonId = _offers[pick.Player][pick.OfferIndex];
                _builds[pick.Player] = _builds[pick.Player].WithBoon(boonId);
                _picked[pick.Player] = true;
                events.Add(new DraftPickedEvent(pick.Player, boonId, pick.Reason));
            }
            if (_picked[0] && _picked[1]) StartRound(events);
            return events;
        }

        /// <summary>Validates then applies; returns false (no events, no change) when the command is illegal.</summary>
        public bool TryApply(Command command, List<MatchEvent> into, out CommandResult result)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            result = Validate(command);
            if (!result.Ok) return false;
            into.AddRange(Apply(command));
            return true;
        }

        private void EndRound(MatchEndedEvent ended, List<MatchEvent> events)
        {
            _score[ended.Winner]++;
            LastRoundLoser = 1 - ended.Winner;
            ImportRevealed(LastMatch.RevealedEntries);
            events.Add(new RoundEndedEvent(Round, ended.Winner, ended.Reason, _score[0], _score[1]));

            if (_score[ended.Winner] >= RoundsToWin)
            {
                Phase = SessionPhase.Over;
                Winner = ended.Winner;
                events.Add(new SessionEndedEvent(Winner, _score[0], _score[1]));
                return;
            }

            // The draft: player 0's offer is drawn first, then player 1's, so the Rng sequence is fixed.
            Phase = SessionPhase.Draft;
            DraftDef draft = _catalog.Rules.Draft;
            for (int p = 0; p < MatchSetup.PlayerCount; p++)
            {
                Draft.Offer(_catalog, _builds[p], draft.OffersFor(p == ended.Winner), Rng, _offers[p]);
                _picked[p] = _offers[p].Count == 0;
            }
            events.Add(new DraftStartedEvent(Round, _offers[0], _offers[1]));
            if (_picked[0] && _picked[1]) StartRound(events);
        }

        private void ImportRevealed(IReadOnlyList<RevealedEntry> entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                bool known = false;
                for (int j = 0; j < _revealed.Count; j++)
                    if (_revealed[j].Viewer == entries[i].Viewer && _revealed[j].UnitId == entries[i].UnitId && _revealed[j].Id == entries[i].Id) { known = true; break; }
                if (!known) _revealed.Add(entries[i]);
            }
        }

        // ---- queries -------------------------------------------------------------------------------

        /// <summary>Every legal command for <paramref name="player"/>: the round's in a round, one pick per offer in a draft they have not picked in, nothing otherwise.</summary>
        public void EnumerateLegal(int player, List<Command> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            CheckPlayer(player);
            switch (Phase)
            {
                case SessionPhase.Round:
                    if (Round > 0) LastMatch.EnumerateLegal(player, into);
                    return;
                case SessionPhase.Draft:
                    if (_picked[player]) return;
                    for (int i = 0; i < _offers[player].Count; i++) into.Add(new DraftPickCommand(player, i));
                    return;
                default:
                    return;
            }
        }

        private static int CheckPlayer(int player)
        {
            if (player < 0 || player >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
            return player;
        }
    }
}
