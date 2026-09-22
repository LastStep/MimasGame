#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Core.Session;
using Mimas.Core.Units;
using Newtonsoft.Json;
using Xunit;

namespace Mimas.Core.Tests
{
    // The class is Mimas.Core.Session.Session (spec §10.4 names it so); from another namespace the bare
    // name finds the namespace first, so this alias inside our own namespace puts the class first.
    using Session = Mimas.Core.Session.Session;

    /// <summary>
    /// The best-of-3 as a Core state machine (design: #session, #round, #draft; spec D part 1 §8; ADR-035):
    /// rounds on the ladder, the loser first, a draft between rounds, reveals that outlive a round, and a
    /// replay that reproduces every event from one seed and the command list.
    /// </summary>
    public class SessionTests
    {
        private static SessionSetup Setup(params string[] archerBoons) =>
            new SessionSetup(CombatFixtures.ArcherWith(archerBoons), CombatFixtures.BruteWith());

        private static Session Started(ContentCatalog catalog, uint seed, out List<MatchEvent> events, SessionSetup setup = null)
        {
            var session = new Session(catalog, setup ?? Setup(), seed);
            events = new List<MatchEvent>(session.Start());
            return session;
        }

        /// <summary>
        /// Ends the round by elimination, the only way to reach a draft since P8: a resign concedes the whole
        /// series. The loser's hero is worn down to one hit with <see cref="Unit.TakeDamage"/> (no command, no
        /// events) and the winner lands the last one, so the round ends inside one <see cref="Session.Apply"/>.
        /// </summary>
        private static List<MatchEvent> EliminateRound(Session session, int loser)
        {
            int winner = 1 - loser;
            MatchState round = session.Match;
            round.Units.Get(0).MoveTo(new Hex(-1, 1));              // the archer, adjacent to the brute
            round.Units.Get(1).MoveTo(new Hex(0, 0));
            Unit victim = round.Units.Get(loser);
            victim.TakeDamage(victim.Hp - 1);
            if (round.ActivePlayer != winner) session.Apply(new EndTurnCommand(loser));
            return new List<MatchEvent>(session.Apply(new AttackCommand(winner, winner,
                winner == 0 ? "bow" : "strike", loser == 0 ? new Hex(-1, 1) : new Hex(0, 0))));
        }

        /// <summary>Picks any offer but the fixture's crippling trade Blessing (ap −5), so the hero can still act next round.</summary>
        private static void PickSafely(Session session, int player)
        {
            IReadOnlyList<string> offers = session.OffersOf(player);
            int index = 0;
            for (int i = 0; i < offers.Count; i++) if (offers[i] != "trial-trade") { index = i; break; }
            session.Apply(new DraftPickCommand(player, index));
        }

        /// <summary>The player who is not on turn resigns, so the round's winner is the active player — and, since P8, the session's.</summary>
        private static List<MatchEvent> ResignRound(Session session, int loser) => new List<MatchEvent>(session.Apply(new ResignCommand(loser)));

        private static string Log(MatchEvent e) => e is SessionEvent ? e.ToString() : Wire.Event(e).ToString(Formatting.None);

        // ---- construction -------------------------------------------------------------------------------

        [Fact]
        public void Session_AppendsTheStartingBlessing_Once()
        {
            var catalog = CombatFixtures.Catalog();
            var session = new Session(catalog, Setup("trial-might"), 1);
            Assert.Equal(new[] { "trial-might", "trial-vigour" }, session.BuildOf(0).BoonIds);
            Assert.Equal(new[] { "trial-vigour" }, session.BuildOf(1).BoonIds);

            var already = new Session(catalog, Setup("trial-vigour", "trial-might"), 1);
            Assert.Equal(new[] { "trial-vigour", "trial-might" }, already.BuildOf(0).BoonIds);

            Assert.Equal(new[] { "field-3", "props-3" }, session.Ladder);        // ladder positions 1 and 2
            Assert.Equal(2, session.RoundsToWin);
            Assert.Equal(0, session.Round);
            Assert.Equal(SessionPhase.Round, session.Phase);
            Assert.Null(session.Match);
            Assert.Null(session.LastMatch);
            Assert.Equal(-1, session.LastRoundLoser);
            Assert.Equal(CommandRejectReason.WrongPhase, session.Validate(new EndTurnCommand(0)).Reason);
        }

        [Fact]
        public void Session_BuildWithoutLineage_Throws()
        {
            Assert.Throws<ArgumentException>(() => new SessionSetup(new PlayerBuild(CombatFixtures.Archer), CombatFixtures.BruteWith()));
            var catalog = CombatFixtures.Catalog();
            Assert.Throws<ArgumentException>(() => new Session(catalog, new SessionSetup(ContentFixtures.BuildFor(CombatFixtures.Archer, "atlantean"), CombatFixtures.BruteWith()), 1));
        }

        // ---- rounds -------------------------------------------------------------------------------------

        [Fact]
        public void Session_Start_RoundOne_CoinFlipFromSeed()
        {
            var catalog = CombatFixtures.Catalog();
            var firsts = new HashSet<int>();
            for (uint seed = 1; seed <= 16; seed++)
            {
                List<MatchEvent> events;
                var session = Started(catalog, seed, out events);
                var started = Assert.IsType<RoundStartedEvent>(events[0]);
                Assert.Equal(1, started.Round);
                Assert.Equal("field-3", started.MapId);
                Assert.Equal(new Rng(seed).Range(0, 2), started.FirstPlayer);         // the first draw of the session's rng
                Assert.Equal(started.FirstPlayer, session.Match.ActivePlayer);
                Assert.Equal(started.FirstPlayer, session.Match.Setup.FirstPlayer);
                Assert.Equal(1, session.Round);
                Assert.NotNull(session.Match);
                Assert.Same(session.Match, session.LastMatch);
                firsts.Add(started.FirstPlayer);
                // The Vigour Blessings on both sides reveal at round start, after the round's own start event.
                Assert.Equal(new[] { typeof(RoundStartedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(TurnStartedEvent) }, events.Select(e => e.GetType()));
            }
            Assert.Equal(2, firsts.Count);
            Assert.Throws<InvalidOperationException>(() => { List<MatchEvent> e; Started(catalog, 1, out e).Start(); });
        }

        [Fact]
        public void Session_RoundTwo_LoserMovesFirst()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 3, out events);
            int first = session.Match.ActivePlayer;
            int loser = first;                                                      // the first mover goes down
            EliminateRound(session, loser);
            Assert.Equal(loser, session.LastRoundLoser);
            Assert.Equal(SessionPhase.Draft, session.Phase);

            session.Apply(new DraftPickCommand(0, 0));
            var started = session.Apply(new DraftPickCommand(1, 0)).OfType<RoundStartedEvent>().Single();
            Assert.Equal(2, started.Round);
            Assert.Equal("props-3", started.MapId);
            Assert.Equal(loser, started.FirstPlayer);
            Assert.Equal(loser, session.Match.ActivePlayer);
        }

        [Fact]
        public void Session_RoundThree_WrapsToLadderPositionOne()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 5, out events);
            EliminateRound(session, 1);                                             // P0 takes round 1
            session.Apply(new DraftPickCommand(0, 0));
            session.Apply(new DraftPickCommand(1, 0));
            Assert.Equal("props-3", session.Match.MapData.Id);
            EliminateRound(session, 0);                                             // P1 takes round 2: 1-1
            Assert.Equal(1, session.Score(0));
            Assert.Equal(1, session.Score(1));
            session.Apply(new DraftPickCommand(0, 0));
            var started = session.Apply(new DraftPickCommand(1, 0)).OfType<RoundStartedEvent>().Single();
            Assert.Equal(3, started.Round);
            Assert.Equal("field-3", started.MapId);                                 // (3 - 1) mod 2 = 0: the ladder wraps
            Assert.Equal(0, started.FirstPlayer);                                   // the loser of round 2
        }

        [Fact]
        public void Session_RoundEnd_ScoresAndOffersBothPlayers()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            var ended = EliminateRound(session, 1);

            Assert.Equal(new[] { typeof(MatchEndedEvent), typeof(RoundEndedEvent), typeof(DraftStartedEvent) }, ended.Skip(ended.Count - 3).Select(e => e.GetType()));
            var round = (RoundEndedEvent)ended[ended.Count - 2];
            Assert.Equal(1, round.Round);
            Assert.Equal(0, round.Winner);
            Assert.Equal(MatchEndReason.Elimination, round.Reason);
            Assert.Equal(1, round.Score0);
            Assert.Equal(0, round.Score1);
            Assert.Equal(1, session.Score(0));
            Assert.Equal(0, session.Score(1));

            var draft = (DraftStartedEvent)ended[ended.Count - 1];
            Assert.Equal(3, draft.Offers0.Count);                                   // rules.draft.offers.winner
            Assert.Equal(3, draft.Offers1.Count);                                   // rules.draft.offers.loser
            Assert.Equal(draft.Offers0, session.OffersOf(0));
            Assert.Equal(draft.Offers1, session.OffersOf(1));
            Assert.Equal(new[] { BoonKinds.Blessing, BoonKinds.Enchant, BoonKinds.Sigil }, draft.Offers0.Select(id => catalog.GetBoon(id).Kind));
            Assert.All(draft.Offers1, id => Assert.True(Draft.IsEligible(catalog, session.BuildOf(1), catalog.GetBoon(id))));
            Assert.DoesNotContain("trial-bowstring", draft.Offers1);               // the brute has no bow
            Assert.Equal(SessionPhase.Draft, session.Phase);
            Assert.Null(session.Match);
            Assert.NotNull(session.LastMatch);
            Assert.False(session.HasPicked(0));
            Assert.False(session.HasPicked(1));
            Assert.False(session.IsOver);
            Assert.Equal(-1, session.Winner);

            var legal = new List<Command>();
            session.EnumerateLegal(0, legal);
            Assert.Equal(3, legal.Count);
            Assert.All(legal, c => Assert.IsType<DraftPickCommand>(c));
            Assert.Equal(new[] { 0, 1, 2 }, legal.Cast<DraftPickCommand>().Select(c => c.OfferIndex));
        }

        [Fact]
        public void Session_Pick_AppliesImmediately_NextRoundHasTheBoon()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            string chosen = session.OffersOf(0)[1];

            var picked = session.Apply(new DraftPickCommand(0, 1));
            var e = Assert.IsType<DraftPickedEvent>(Assert.Single(picked));
            Assert.Equal(0, e.Player);
            Assert.Equal(chosen, e.BoonId);
            Assert.Equal(DraftPickReason.Player, e.Reason);
            Assert.True(session.HasPicked(0));
            Assert.False(session.HasPicked(1));
            Assert.Equal(new[] { "trial-vigour", chosen }, session.BuildOf(0).BoonIds);
            Assert.Equal(SessionPhase.Draft, session.Phase);
            Assert.Equal(CommandRejectReason.AlreadyPicked, session.Validate(new DraftPickCommand(0, 0)).Reason);
            Assert.Equal(CommandRejectReason.BadOffer, session.Validate(new DraftPickCommand(1, 3)).Reason);
            Assert.Equal(CommandRejectReason.BadOffer, session.Validate(new DraftPickCommand(1, -1)).Reason);

            session.Apply(new DraftPickCommand(1, 0));
            Assert.True(session.Match.Units.Get(0).HasBoon(chosen));
            Assert.Equal(new[] { "trial-vigour", chosen }, session.Match.Units.Get(0).BoonIds);
            Assert.Empty(session.OffersOf(0));
        }

        [Fact]
        public void Session_Timeout_PicksFirstOffer()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            string first = session.OffersOf(1)[0];
            var picked = session.Apply(new DraftPickCommand(1, 0, DraftPickReason.Timeout));
            var e = Assert.IsType<DraftPickedEvent>(Assert.Single(picked));
            Assert.Equal(first, e.BoonId);
            Assert.Equal(DraftPickReason.Timeout, e.Reason);
            Assert.True(session.BuildOf(1).HasBoon(first));
            Assert.Equal("P1 picks offer 0 (Timeout)", new DraftPickCommand(1, 0, DraftPickReason.Timeout).ToString());
        }

        [Fact]
        public void Session_BothPicked_NextRoundStartsInSameApply()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            session.Apply(new DraftPickCommand(1, 0));
            var both = session.Apply(new DraftPickCommand(0, 0));
            Assert.Equal(typeof(DraftPickedEvent), both[0].GetType());
            Assert.Equal(typeof(RoundStartedEvent), both[1].GetType());
            Assert.Contains(both, e => e is TurnStartedEvent);
            Assert.Equal(SessionPhase.Round, session.Phase);
            Assert.Equal(2, session.Round);
            Assert.NotNull(session.Match);
        }

        [Fact]
        public void Session_TwoWins_EndsSession()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            session.Apply(new DraftPickCommand(0, 0));
            session.Apply(new DraftPickCommand(1, 0));
            var ended = EliminateRound(session, 1);

            Assert.Equal(new[] { typeof(MatchEndedEvent), typeof(RoundEndedEvent), typeof(SessionEndedEvent) }, ended.Skip(ended.Count - 3).Select(e => e.GetType()));
            var over = (SessionEndedEvent)ended[ended.Count - 1];
            Assert.Equal(0, over.Winner);
            Assert.Equal(2, over.Score0);
            Assert.Equal(0, over.Score1);
            Assert.Equal(SessionEndReason.Score, over.Reason);                      // the series was won, not conceded
            Assert.True(session.IsOver);
            Assert.Equal(0, session.Winner);
            Assert.Equal(SessionPhase.Over, session.Phase);
            Assert.Null(session.Match);
            Assert.Equal(CommandRejectReason.MatchOver, session.Validate(new DraftPickCommand(0, 0)).Reason);
            Assert.Equal(CommandRejectReason.MatchOver, session.Validate(new EndTurnCommand(0)).Reason);
            var legal = new List<Command>();
            session.EnumerateLegal(0, legal);
            Assert.Empty(legal);
        }

        /// <summary>Resign concedes the series, not the round (design: #session rule 7; decided 22 Sep 2026, P8).</summary>
        [Fact]
        public void Session_ResignInRound_EndsTheSessionWithResign()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 9, out events);
            var ended = ResignRound(session, 0);

            // The round is scored first — it did end, and the RoundEndedEvent says how — and then the series stops.
            var round = ended.OfType<RoundEndedEvent>().Single();
            Assert.Equal(1, round.Winner);
            Assert.Equal(MatchEndReason.Resign, round.Reason);
            Assert.Equal(0, round.Score0);
            Assert.Equal(1, round.Score1);
            Assert.Equal(0, session.LastRoundLoser);

            var over = ended.OfType<SessionEndedEvent>().Single();
            Assert.Equal(1, over.Winner);
            Assert.Equal(SessionEndReason.Resign, over.Reason);
            Assert.Equal(0, over.Score0);
            Assert.Equal(1, over.Score1);
            Assert.Equal("session over: P1 wins 0-1 by Resign", over.ToString());
            Assert.True(session.IsOver);
            Assert.Equal(1, session.Winner);
            Assert.Equal(SessionPhase.Over, session.Phase);
            Assert.DoesNotContain(ended, e => e is DraftStartedEvent);              // no draft: the series is over
        }

        [Fact]
        public void Session_ForfeitInRound_EndsTheSessionWithForfeit()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 9, out events);
            var ended = new List<MatchEvent>(session.Apply(new ResignCommand(0, ResignReason.Disconnect)));

            Assert.Equal(MatchEndReason.Forfeit, ended.OfType<RoundEndedEvent>().Single().Reason);
            var over = ended.OfType<SessionEndedEvent>().Single();
            Assert.Equal(SessionEndReason.Forfeit, over.Reason);
            Assert.Equal(1, over.Winner);
            Assert.True(session.IsOver);
        }

        /// <summary>A resign between rounds ends the series too, and scores nothing: a draft is not a round.</summary>
        [Fact]
        public void Session_ResignInDraft_EndsTheSessionWithoutScoring()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);                                             // P0 leads 1-0, the draft is open
            Assert.Equal(SessionPhase.Draft, session.Phase);
            Assert.True(session.Validate(new ResignCommand(0)).Ok);
            Assert.Equal(CommandRejectReason.WrongPhase, session.Validate(new EndTurnCommand(0)).Reason);

            var ended = new List<MatchEvent>(session.Apply(new ResignCommand(0)));
            var over = Assert.IsType<SessionEndedEvent>(Assert.Single(ended));      // no round ended: none was running
            Assert.Equal(1, over.Winner);
            Assert.Equal(SessionEndReason.Resign, over.Reason);
            Assert.Equal(1, over.Score0);                                           // the score stands where round 1 left it
            Assert.Equal(0, over.Score1);
            Assert.True(session.IsOver);
            Assert.Equal(1, session.Winner);
            Assert.Empty(session.OffersOf(0));
            Assert.Equal(CommandRejectReason.MatchOver, session.Validate(new ResignCommand(1)).Reason);

            // A disconnect past the grace is the same command with the other reason.
            var second = Started(catalog, 7, out events);
            EliminateRound(second, 1);
            var forfeit = Assert.IsType<SessionEndedEvent>(Assert.Single(second.Apply(new ResignCommand(1, ResignReason.Disconnect))));
            Assert.Equal(SessionEndReason.Forfeit, forfeit.Reason);
            Assert.Equal(0, forfeit.Winner);
        }

        [Fact]
        public void Session_Validate_WrongPhase_IsRejected()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            Assert.Equal(CommandRejectReason.WrongPhase, session.Validate(new DraftPickCommand(0, 0)).Reason);
            Assert.Throws<InvalidOperationException>(() => session.Apply(new DraftPickCommand(0, 0)));
            Assert.True(session.Validate(new EndTurnCommand(session.Match.ActivePlayer)).Ok);

            EliminateRound(session, 1);
            Assert.Equal(CommandRejectReason.WrongPhase, session.Validate(new EndTurnCommand(0)).Reason);
            Assert.True(session.Validate(new ResignCommand(0)).Ok);                 // a resign in a draft concedes the series (P8)
            var into = new List<MatchEvent>();
            CommandResult result;
            Assert.False(session.TryApply(new EndTurnCommand(0), into, out result));
            Assert.Empty(into);
            Assert.True(session.TryApply(new DraftPickCommand(0, 2), into, out result));
            Assert.Single(into);
        }

        // ---- reveals across rounds ---------------------------------------------------------------------

        [Fact]
        public void Session_Reveals_PersistIntoNextRound()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 11, out events, Setup("trial-might"));
            MatchState round1 = session.Match;
            round1.Units.Get(0).MoveTo(new Hex(-1, 1));
            round1.Units.Get(1).MoveTo(new Hex(0, 0));
            if (round1.ActivePlayer != 0) session.Apply(new EndTurnCommand(1));
            var shot = session.Apply(new AttackCommand(0, 0, "bow", new Hex(0, 0)));
            Assert.Contains(shot.OfType<BoonRevealedEvent>(), e => e.BoonId == "trial-might");
            Assert.True(round1.KnowsBoon(1, 0, "trial-might"));

            EliminateRound(session, 0);
            Assert.Contains(session.RevealedEntries, r => r.Viewer == 1 && r.UnitId == 0 && r.Id.EndsWith("trial-might"));
            session.Apply(new DraftPickCommand(0, 0));
            session.Apply(new DraftPickCommand(1, 0));

            MatchState round2 = session.Match;
            Assert.NotSame(round1, round2);
            Assert.True(round2.KnowsBoon(1, 0, "trial-might"));
            Assert.True(round2.KnowsLineage(1, 0, "trial"));
            Assert.True(round2.Knows(1, 0, "bow"));
            Assert.Equal("trial-might", round2.ViewFor(1).FindUnit(0).Boons.First(b => b.Id == "trial-might").Id);
            Assert.Equal("trial", round2.ViewFor(1).FindUnit(0).LineageId);

            // The session's view says the same, in a draft as well as in a round.
            var view = SessionView.For(session, 1);
            Assert.Equal("trial", view.OpponentLineageId);
            Assert.Contains(view.OpponentBoons, b => b.Id == "trial-might");
            Assert.Equal(session.BuildOf(0).BoonIds.Count, view.OpponentBoons.Count);
        }

        // ---- the filter and the view ---------------------------------------------------------------------

        [Fact]
        public void Session_Filter_OpponentSeesOnlyThatAPickWasMade()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            var ended = EliminateRound(session, 1);

            var for0 = new List<MatchEvent>();
            var for1 = new List<MatchEvent>();
            SessionEventFilter.ForPlayer(ended, 0, session, for0);
            SessionEventFilter.ForPlayer(ended, 1, session, for1);
            var draft0 = for0.OfType<DraftStartedEvent>().Single();
            var draft1 = for1.OfType<DraftStartedEvent>().Single();
            Assert.Equal(session.OffersOf(0), draft0.Offers0);
            Assert.Null(draft0.Offers1);                                            // not "empty": the field is absent, not zero offers
            Assert.Null(draft1.Offers0);
            Assert.Equal(session.OffersOf(1), draft1.Offers1);

            var picked = session.Apply(new DraftPickCommand(0, 1));
            for0.Clear(); for1.Clear();
            SessionEventFilter.ForPlayer(picked, 0, session, for0);
            SessionEventFilter.ForPlayer(picked, 1, session, for1);
            Assert.Equal(session.OffersOf(0)[1], ((DraftPickedEvent)for0.Single()).BoonId);
            var theirs = (DraftPickedEvent)for1.Single();
            Assert.Null(theirs.BoonId);
            Assert.Equal(0, theirs.Player);
            Assert.Equal("P0 picked ? (Player)", theirs.ToString());

            // The second pick starts round 2 in the same list: its start events are filtered with the new round's state.
            var both = session.Apply(new DraftPickCommand(1, 0));
            for0.Clear();
            SessionEventFilter.ForPlayer(both, 0, session, for0);
            Assert.Equal(typeof(DraftPickedEvent), for0[0].GetType());
            Assert.Null(((DraftPickedEvent)for0[0]).BoonId);
            Assert.Equal(typeof(RoundStartedEvent), for0[1].GetType());
            Assert.Contains(for0, e => e is TurnStartedEvent);
            Assert.DoesNotContain(for0, e => e is BoonRevealedEvent b && b.ToPlayer == 1);

            var view0 = SessionView.For(session, 0);
            Assert.Equal(2, view0.Round);
            Assert.Equal(SessionPhase.Round, view0.Phase);
            Assert.NotNull(view0.Match);
            Assert.Equal(0, view0.Match.Viewer);
            Assert.Empty(view0.MyOffers);
            Assert.Equal(session.BuildOf(0), view0.MyBuild);
            Assert.Equal(1, view0.Score0);
        }

        [Fact]
        public void Session_View_InADraft_ShowsOffersAndWhoHasPicked()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            session.Apply(new DraftPickCommand(1, 0));

            var view0 = SessionView.For(session, 0);
            Assert.Equal(SessionPhase.Draft, view0.Phase);
            Assert.Null(view0.Match);
            Assert.Equal(session.OffersOf(0), view0.MyOffers);
            Assert.False(view0.IHavePicked);
            Assert.True(view0.OpponentHasPicked);
            Assert.Equal(2, view0.OpponentBoons.Count);                             // Vigour (revealed at start) and the pick (hidden)
            Assert.Equal("trial-vigour", view0.OpponentBoons[0].Id);
            Assert.Null(view0.OpponentBoons[1].Id);
            Assert.Equal("trial", view0.OpponentLineageId);
            Assert.Throws<ArgumentOutOfRangeException>(() => SessionView.For(session, 2));
        }

        [Fact]
        public void Session_ZeroOffers_IsPickedAtOnce_AndBothZeroStartsTheNextRound()
        {
            // A rules file where the loser gets no offers: the loser is picked the moment the draft opens.
            var files = CombatFixtures.Files().Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"""loser"": 3", @"""loser"": 0"))
                : f).ToList();
            var catalog = ContentCatalog.Load(files);
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            var ended = EliminateRound(session, 1);
            var draft = ended.OfType<DraftStartedEvent>().Single();
            Assert.Empty(draft.Offers1);                                            // genuinely offered nothing, so an empty list, not null
            Assert.Equal(3, draft.Offers0.Count);
            Assert.True(session.HasPicked(1));
            Assert.False(session.HasPicked(0));
            Assert.True(session.Validate(new DraftPickCommand(1, 5)).Ok);           // a pick of nothing is accepted and does nothing
            Assert.Empty(session.Apply(new DraftPickCommand(1, 5)));
            Assert.Equal(new[] { "trial-vigour" }, session.BuildOf(1).BoonIds);

            // Neither side offered anything: the next round starts in the Apply that ended the previous one.
            var none = CombatFixtures.Files().Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"""winner"": 3, ""loser"": 3", @"""winner"": 0, ""loser"": 0"))
                : f).ToList();
            var noDraft = Started(ContentCatalog.Load(none), 7, out events);
            var straight = EliminateRound(noDraft, 1);
            int end = straight.FindIndex(e => e is MatchEndedEvent);
            Assert.Equal(new[] { typeof(MatchEndedEvent), typeof(RoundEndedEvent), typeof(DraftStartedEvent), typeof(RoundStartedEvent) },
                straight.Skip(end).Take(4).Select(e => e.GetType()));
            Assert.Equal(2, noDraft.Round);
            Assert.Equal(SessionPhase.Round, noDraft.Phase);
        }

        [Fact]
        public void SessionView_NextMapId_WrapsTheLadder()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 5, out events);

            // Round 1 on field-3: the board shows field-3, the next round is props-3.
            var round1 = SessionView.For(session, 0);
            Assert.Equal("field-3", round1.MapId);
            Assert.Equal("props-3", round1.NextMapId);
            Assert.Equal(2, round1.RoundsToWin);

            // In the draft there is no round: the board shows the map the next one will play.
            EliminateRound(session, 1);
            var draft = SessionView.For(session, 0);
            Assert.Equal(SessionPhase.Draft, draft.Phase);
            Assert.Equal("props-3", draft.MapId);
            Assert.Equal("props-3", draft.NextMapId);

            PickSafely(session, 0);
            PickSafely(session, 1);
            var round2 = SessionView.For(session, 0);
            Assert.Equal("props-3", round2.MapId);
            Assert.Equal("field-3", round2.NextMapId);                              // (2 mod 2) = 0: the ladder wraps

            // Over: there is no next map.
            EliminateRound(session, 1);
            var over = SessionView.For(session, 0);
            Assert.True(over.IsOver);
            Assert.Null(over.NextMapId);
            Assert.Null(over.MapId);
        }

        [Fact]
        public void SessionView_Create_RoundTripsFor()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            EliminateRound(session, 1);
            session.Apply(new DraftPickCommand(1, 0));

            SessionView original = SessionView.For(session, 0);
            SessionView copy = SessionView.Create(original.Viewer, original.Round, original.Phase, original.Score0, original.Score1,
                original.RoundsToWin, original.IsOver, original.Winner, original.MapId, original.NextMapId, original.MyBuild,
                original.OpponentLineageId, new List<KnownEntry>(original.OpponentBoons), new List<string>(original.MyOffers),
                original.IHavePicked, original.OpponentHasPicked, original.Match);

            Assert.Equal(original.Viewer, copy.Viewer);
            Assert.Equal(original.Round, copy.Round);
            Assert.Equal(original.Phase, copy.Phase);
            Assert.Equal(original.Score0, copy.Score0);
            Assert.Equal(original.Score1, copy.Score1);
            Assert.Equal(original.RoundsToWin, copy.RoundsToWin);
            Assert.Equal(original.IsOver, copy.IsOver);
            Assert.Equal(original.Winner, copy.Winner);
            Assert.Equal(original.MapId, copy.MapId);
            Assert.Equal(original.NextMapId, copy.NextMapId);
            Assert.Equal(original.MyBuild, copy.MyBuild);
            Assert.Equal(original.OpponentLineageId, copy.OpponentLineageId);
            Assert.Equal(original.MyOffers, copy.MyOffers);
            Assert.Equal(original.IHavePicked, copy.IHavePicked);
            Assert.True(copy.OpponentHasPicked);
            Assert.Null(copy.Match);

            Assert.Throws<ArgumentOutOfRangeException>(() => SessionView.Create(2, 1, SessionPhase.Round, 0, 0, 2, false, -1, "field-3", "props-3",
                session.BuildOf(0), null, null, null, false, false, null));
            Assert.Throws<ArgumentNullException>(() => SessionView.Create(0, 1, SessionPhase.Round, 0, 0, 2, false, -1, "field-3", "props-3",
                null, null, null, null, false, false, null));
        }

        // ---- bots and the golden replay ---------------------------------------------------------------------

        [Fact]
        public void RandomBot_ChooseDraft_PicksFirstOffer()
        {
            var catalog = CombatFixtures.Catalog();
            List<MatchEvent> events;
            var session = Started(catalog, 7, out events);
            var bot = new RandomBot(3);
            Assert.Null(bot.ChooseDraft(session, 0));                                // not in a draft
            EliminateRound(session, 1);
            var pick = Assert.IsType<DraftPickCommand>(bot.ChooseDraft(session, 1));
            Assert.Equal(1, pick.Player);
            Assert.Equal(0, pick.OfferIndex);
            Assert.Equal(DraftPickReason.Player, pick.Reason);
            string first = session.OffersOf(1)[0];
            session.Apply(pick);
            Assert.Null(bot.ChooseDraft(session, 1));                                // already picked
            Assert.True(session.BuildOf(1).HasBoon(first));
            Assert.Null(bot.Choose(session.LastMatch, 0));                          // the round is over
        }

        /// <summary>
        /// Determinism end to end (design: #determinism): two sessions with the same seed, random bots on
        /// both seats picking moves and drafts, produce byte-identical event logs and identical builds. The
        /// seeds and the expected event counts stay here, not in a file.
        /// </summary>
        [Fact]
        public void Session_Replay_SameSeedAndCommands_SameEvents()
        {
            var catalog = CombatFixtures.Catalog();
            // Two seeds, both of which the bots play to a 2-0 finish; the line counts are the golden values.
            var expected = new Dictionary<uint, int> { { 21, 839 }, { 34, 977 } };
            foreach (uint seed in expected.Keys)
            {
                var a = PlayOut(catalog, seed);
                var b = PlayOut(catalog, seed);
                Assert.Equal(a.log, b.log);
                Assert.Equal(a.rounds, b.rounds);
                Assert.Equal(a.build0, b.build0);
                Assert.Equal(a.build1, b.build1);
                Assert.True(a.over, $"seed {seed}: the session did not finish within the command budget");
                Assert.Equal(2, a.rounds);
                Assert.Equal(expected[seed], a.log.Count);
                Assert.Contains(a.log, l => l.StartsWith("draft after round 1"));
                Assert.Contains(a.log, l => l.StartsWith("session over"));
            }
        }

        private static (List<string> log, int rounds, PlayerBuild build0, PlayerBuild build1, bool over) PlayOut(ContentCatalog catalog, uint seed)
        {
            var session = new Session(catalog, Setup(), seed);
            var bots = new IBot[] { new RandomBot(seed * 2 + 1), new RandomBot(seed * 2 + 2) };
            var log = new List<string>();
            foreach (var e in session.Start()) log.Add(Log(e));

            int commands = 0;
            while (!session.IsOver && commands < 6000)
            {
                Command command;
                if (session.Phase == SessionPhase.Draft)
                    command = bots[0].ChooseDraft(session, 0) ?? bots[1].ChooseDraft(session, 1);
                else
                    command = bots[session.Match.ActivePlayer].Choose(session.Match, session.Match.ActivePlayer);
                Assert.NotNull(command);
                Assert.True(session.Validate(command).Ok, command.ToString());
                log.Add(command.ToString());
                foreach (var e in session.Apply(command)) log.Add(Log(e));
                commands++;
            }
            int rounds = log.Count(l => l.StartsWith("round ") && l.Contains(" ended"));
            return (log, rounds, session.BuildOf(0), session.BuildOf(1), session.IsOver);
        }
    }
}
