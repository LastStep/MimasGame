#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Core.Session;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mimas.Core.Tests
{
    using Session = Mimas.Core.Session.Session;

    /// <summary>
    /// The owner's view says what the opponent has already seen of it (design #hidden-info rule 9, ADR-039,
    /// spec 2026-09-23-hud-restyle §4, §5): <see cref="KnownEntry.SeenByOpponent"/> and
    /// <see cref="UnitView.LineageSeenByOpponent"/> are the opponent's revealed set, copied onto the viewer's
    /// own entries only; the wire carries them for the own unit and nothing new for the enemy; the mirror
    /// reproduces them.
    /// </summary>
    public class SeenByOpponentTests
    {
        private static readonly Hex ArcherAt = new Hex(-1, 1);     // flat grass, adjacent to (0,0)
        private static readonly Hex BruteAt = new Hex(0, 0);

        private static KnownEntry Ability(UnitView unit, string id) => unit.Abilities.Single(a => a.Id == id);

        private static KnownEntry Boon(UnitView unit, string id) => unit.Boons.Single(b => b.Id == id);

        private static JObject UnitJson(JObject view, bool mine) => view["units"].Cast<JObject>().Single(u => u.Value<bool>("mine") == mine);

        // ---- the view -----------------------------------------------------------------------------------

        [Fact]
        public void PlayerView_OwnAbility_NotSeenByOpponentBeforeUse()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith(), ArcherAt, BruteAt);

            UnitView mine = state.ViewFor(0).FindUnit(0);
            Assert.Equal(new[] { "move", "bow" }, mine.Abilities.Select(a => a.Id));
            Assert.All(mine.Abilities, a => Assert.False(a.SeenByOpponent));
        }

        [Fact]
        public void PlayerView_OwnAbility_SeenByOpponentAfterUse()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            state.Apply(new AttackCommand(0, 0, "bow", BruteAt));

            UnitView mine = state.ViewFor(0).FindUnit(0);
            Assert.True(Ability(mine, "bow").SeenByOpponent);
            Assert.False(Ability(mine, "move").SeenByOpponent);      // unused: still a surprise
        }

        [Fact]
        public void PlayerView_OwnSigil_UseMarksBoonAbilityAndLineageSeen()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-jab"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);

            UnitView before = state.ViewFor(0).FindUnit(0);
            Assert.False(Ability(before, "jab").SeenByOpponent);
            Assert.False(Boon(before, "trial-jab").SeenByOpponent);
            Assert.False(before.LineageSeenByOpponent);

            state.Apply(new AttackCommand(0, 0, "jab", BruteAt));

            UnitView after = state.ViewFor(0).FindUnit(0);
            Assert.True(Ability(after, "jab").SeenByOpponent);
            Assert.True(Boon(after, "trial-jab").SeenByOpponent);
            Assert.True(after.LineageSeenByOpponent);
            Assert.False(Ability(after, "bow").SeenByOpponent);
        }

        [Fact]
        public void PlayerView_OwnStatBlessing_SeenByOpponentAtRoundStart()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-might"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);

            UnitView mine = state.ViewFor(0).FindUnit(0);
            Assert.True(Boon(mine, "trial-vigour").SeenByOpponent);   // a Health Blessing is public from round start (D12)
            Assert.True(mine.LineageSeenByOpponent);                  // and brings its lineage with it
            Assert.False(Boon(mine, "trial-might").SeenByOpponent);   // a Strength Blessing waits for a result
        }

        [Fact]
        public void PlayerView_OwnPublicModifier_CountsAsSeen()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = new MatchSetup("field-3", CombatFixtures.ArcherWith(), CombatFixtures.BruteWith())
                .WithModifier(0, "bowyer")          // public
                .WithModifier(0, "flaming");        // hidden
            var state = CombatFixtures.Started(catalog, setup, new Hex(-3, 0), new Hex(3, 0));

            UnitView mine = state.ViewFor(0).FindUnit(0);
            Assert.True(mine.Modifiers.Single(m => m.Id == "bowyer").SeenByOpponent);
            Assert.False(mine.Modifiers.Single(m => m.Id == "flaming").SeenByOpponent);
        }

        [Fact]
        public void PlayerView_EnemyEntries_NeverFlaggedSeen()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-jab"), CombatFixtures.BruteWith("trial-vigour"), ArcherAt, BruteAt);
            state.Apply(new AttackCommand(0, 0, "jab", BruteAt));    // everything of the archer's that player 1 can learn from a jab
            state.Apply(new EndTurnCommand(0));
            state.Apply(new AttackCommand(1, 1, "strike", ArcherAt)); // and the brute's strike, to player 0

            foreach (int viewer in new[] { 0, 1 })
            {
                UnitView enemy = state.ViewFor(viewer).FindUnit(1 - viewer);
                Assert.Contains(enemy.Abilities, a => a.Revealed);
                Assert.All(enemy.Abilities, a => Assert.False(a.SeenByOpponent));
                Assert.All(enemy.Modifiers, m => Assert.False(m.SeenByOpponent));
                Assert.All(enemy.Boons, b => Assert.False(b.SeenByOpponent));
                Assert.NotNull(enemy.LineageId);
                Assert.False(enemy.LineageSeenByOpponent);
            }
        }

        /// <summary>
        /// Whole random-bot matches with boons on both sides: at every step, for both seats, every own entry's
        /// flag is exactly the opponent's knowledge of it and every enemy entry's flag is false.
        /// </summary>
        [Fact]
        public void PlayerView_SeenFlag_EqualsOpponentKnowledge()
        {
            var catalog = CombatFixtures.Catalog();
            int seen = 0, unseen = 0;
            for (uint seed = 1; seed <= 8; seed++)
            {
                var setup = new MatchSetup("field-3",
                    CombatFixtures.ArcherWith("trial-vigour", "trial-might", "trial-bowstring", "trial-jab", "trial-ward"),
                    CombatFixtures.BruteWith("trial-might", "trial-cheap", "trial-plating", "trial-frost", "trial-zap", "trial-hop"),
                    (int)(seed % 2));
                var state = new MatchState(catalog, setup, seed);
                var bot = new RandomBot(seed * 5 + 1);
                state.Start();

                for (int step = 0; step < 200 && !state.IsOver; step++)
                {
                    foreach (int viewer in new[] { 0, 1 })
                    {
                        int opponent = 1 - viewer;
                        PlayerView view = state.ViewFor(viewer);
                        foreach (UnitView u in view.Units)
                        {
                            if (u.Owner != viewer)
                            {
                                Assert.DoesNotContain(u.Abilities.Concat(u.Modifiers).Concat(u.Boons), e => e.SeenByOpponent);
                                Assert.False(u.LineageSeenByOpponent);
                                continue;
                            }
                            foreach (KnownEntry a in u.Abilities)
                                Assert.Equal(state.Knows(opponent, u.Id, a.Id), a.SeenByOpponent);
                            foreach (KnownEntry m in u.Modifiers)
                                Assert.Equal(!catalog.Modifiers.Get(m.Id).IsHidden || state.Knows(opponent, u.Id, m.Id), m.SeenByOpponent);
                            foreach (KnownEntry b in u.Boons)
                                Assert.Equal(state.KnowsBoon(opponent, u.Id, b.Id), b.SeenByOpponent);
                            Assert.Equal(state.KnowsLineage(opponent, u.Id, u.LineageId), u.LineageSeenByOpponent);

                            foreach (KnownEntry e in u.Abilities.Concat(u.Boons)) if (e.SeenByOpponent) seen++; else unseen++;
                        }
                    }

                    Command command = bot.Choose(state, state.ActivePlayer) ?? new EndTurnCommand(state.ActivePlayer);
                    state.Apply(command);
                }
            }
            Assert.True(seen > 50, $"only {seen} seen entries were swept");
            Assert.True(unseen > 50, $"only {unseen} unseen entries were swept");
        }

        // ---- the session ----------------------------------------------------------------------------------

        [Fact]
        public void Session_SeenByOpponent_CarriesIntoNextRound()
        {
            var catalog = CombatFixtures.Catalog();
            var session = new Session(catalog, new SessionSetup(CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith()), 11);
            session.Start();
            MatchState round1 = session.Match;
            round1.Units.Get(0).MoveTo(ArcherAt);
            round1.Units.Get(1).MoveTo(BruteAt);
            if (round1.ActivePlayer != 0) session.Apply(new EndTurnCommand(1));
            session.Apply(new AttackCommand(0, 0, "bow", BruteAt));        // the bow, and the Strength Blessing it carries
            Assert.True(Ability(round1.ViewFor(0).FindUnit(0), "bow").SeenByOpponent);

            // Round 1 ends by elimination (the archer loses), both pick, round 2 opens.
            round1.Units.Get(0).TakeDamage(round1.Units.Get(0).Hp - 1);
            if (round1.ActivePlayer != 1) session.Apply(new EndTurnCommand(0));
            session.Apply(new AttackCommand(1, 1, "strike", ArcherAt));
            Assert.Equal(SessionPhase.Draft, session.Phase);
            session.Apply(new DraftPickCommand(0, 0));
            session.Apply(new DraftPickCommand(1, 0));

            MatchState round2 = session.Match;
            Assert.NotSame(round1, round2);
            UnitView mine = round2.ViewFor(0).FindUnit(0);
            Assert.True(Ability(mine, "bow").SeenByOpponent);
            Assert.True(Boon(mine, "trial-might").SeenByOpponent);
            Assert.True(mine.LineageSeenByOpponent);
            Assert.False(Ability(mine, "move").SeenByOpponent);
        }

        // ---- the wire -------------------------------------------------------------------------------------

        [Fact]
        public void Wire_OwnSeenFlags_RoundTrip()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might", "trial-jab", "trial-ward"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            state.Apply(new AttackCommand(0, 0, "jab", BruteAt));
            PlayerView sent = state.ViewFor(0);

            PlayerView read = Wire.ReadView(JObject.Parse(Wire.View(sent).ToString()));

            UnitView a = sent.FindUnit(0), b = read.FindUnit(0);
            Assert.Equal(a.Abilities.Select(e => e.SeenByOpponent), b.Abilities.Select(e => e.SeenByOpponent));
            Assert.Equal(a.Modifiers.Select(e => e.SeenByOpponent), b.Modifiers.Select(e => e.SeenByOpponent));
            Assert.Equal(a.Boons.Select(e => e.SeenByOpponent), b.Boons.Select(e => e.SeenByOpponent));
            Assert.Equal(a.LineageSeenByOpponent, b.LineageSeenByOpponent);
            Assert.Contains(b.Abilities, e => e.SeenByOpponent);          // the jab
            Assert.Contains(b.Abilities, e => !e.SeenByOpponent);         // the walk and the bow
            Assert.True(b.LineageSeenByOpponent);
        }

        [Fact]
        public void Wire_EnemyUnit_CarriesNoSeenKeys()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-ward"), CombatFixtures.BruteWith("trial-vigour"), ArcherAt, BruteAt);
            state.Apply(new AttackCommand(0, 0, "bow", BruteAt));

            foreach (int viewer in new[] { 0, 1 })
            {
                JObject view = Wire.View(state.ViewFor(viewer));
                JObject enemy = UnitJson(view, mine: false);
                Assert.Null(enemy["lineageSeen"]);
                foreach (string list in new[] { "abilities", "modifiers", "boons" })
                    Assert.All(enemy[list].Cast<JObject>(), e => Assert.Null(e["seen"]));

                JObject own = UnitJson(view, mine: true);
                Assert.Equal(JTokenType.Boolean, own["lineageSeen"].Type);
                foreach (string list in new[] { "abilities", "modifiers", "boons" })
                    Assert.All(own[list].Cast<JObject>(), e => Assert.Equal(JTokenType.Boolean, e["seen"].Type));
            }
        }

        [Fact]
        public void Wire_OwnEntryWithoutSeen_Throws()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might", "trial-ward"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            JObject intact = Wire.View(state.ViewFor(0));
            Wire.ReadView((JObject)intact.DeepClone());                       // the intact view reads

            foreach (string list in new[] { "abilities", "modifiers", "boons" })
            {
                var broken = (JObject)intact.DeepClone();
                ((JObject)UnitJson(broken, mine: true)[list][0]).Remove("seen");
                Assert.Throws<WireException>(() => Wire.ReadView(broken));
            }
            var noLineage = (JObject)intact.DeepClone();
            UnitJson(noLineage, mine: true).Remove("lineageSeen");
            Assert.Throws<WireException>(() => Wire.ReadView(noLineage));
        }

        // ---- the mirror -----------------------------------------------------------------------------------

        [Fact]
        public void Mirror_FromView_ReproducesSeenFlags()
        {
            var catalog = CombatFixtures.Catalog();
            var truth = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-might", "trial-jab", "trial-ward"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            truth.Apply(new AttackCommand(0, 0, "jab", BruteAt));
            PlayerView view = truth.ViewFor(0);

            MatchState mirror = MatchState.FromView(catalog, view);
            UnitView again = mirror.ViewFor(0).FindUnit(0);

            Assert.True(Ability(again, "jab").SeenByOpponent);
            Assert.False(Ability(again, "bow").SeenByOpponent);
            Assert.True(Boon(again, "trial-vigour").SeenByOpponent);
            Assert.True(Boon(again, "trial-jab").SeenByOpponent);
            Assert.False(Boon(again, "trial-might").SeenByOpponent);
            Assert.True(again.LineageSeenByOpponent);
            Assert.Equal(Wire.View(view).ToString(), Wire.View(mirror.ViewFor(0)).ToString());
        }
    }
}
