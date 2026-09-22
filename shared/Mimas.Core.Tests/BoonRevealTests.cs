#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// When the opponent learns a boon (spec D part 1 §6.5; design #hidden-info, #boons rule 3): a Health
    /// Blessing at round start, a stat or modifier Blessing when its line changes a result, an Enchant on
    /// the observation that contradicts what was known (aim, cost, element), a Sigil on first use; the
    /// lineage with the first boon of it; and nothing before then.
    /// </summary>
    public class BoonRevealTests
    {
        private static readonly Hex ArcherAt = new Hex(-1, 1);     // flat grass, adjacent to (0,0)
        private static readonly Hex BruteAt = new Hex(0, 0);

        private static IEnumerable<Type> Kinds(IEnumerable<MatchEvent> events) => events.Select(e => e.GetType());

        // ---- R2: a Health or AP Blessing is public from round start (D12) ----------------------------

        [Fact]
        public void Start_HpBlessing_RevealsBoonAndLineageBeforeTurnStarted()
        {
            var catalog = CombatFixtures.Catalog();
            var state = new MatchState(catalog, new MatchSetup("field-3", CombatFixtures.ArcherWith("trial-vigour"), CombatFixtures.BruteWith()), 1);
            var events = state.Start();

            Assert.Equal(new[] { typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(TurnStartedEvent) }, Kinds(events));
            var boon = (BoonRevealedEvent)events[0];
            Assert.Equal("trial-vigour", boon.BoonId);
            Assert.Equal(0, boon.UnitId);
            Assert.Equal(1, boon.ToPlayer);
            var lineage = (LineageRevealedEvent)events[1];
            Assert.Equal("trial", lineage.LineageId);
            Assert.Equal(1, lineage.ToPlayer);

            Assert.True(state.KnowsBoon(1, 0, "trial-vigour"));
            Assert.True(state.KnowsLineage(1, 0, "trial"));
            Assert.False(state.KnowsBoon(0, 0, "trial-vigour"));      // the owner has nothing to learn
            UnitView theirs = state.ViewFor(1).FindUnit(0);
            Assert.Equal("trial-vigour", Assert.Single(theirs.Boons).Id);
            Assert.Equal("trial", theirs.LineageId);
            Assert.Equal(16, theirs.MaxHp);
        }

        [Fact]
        public void Start_StrengthBlessing_StaysHidden()
        {
            var catalog = CombatFixtures.Catalog();
            var state = new MatchState(catalog, new MatchSetup("field-3", CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith()), 1);
            var events = state.Start();
            Assert.Equal(new[] { typeof(TurnStartedEvent) }, Kinds(events));
            UnitView theirs = state.ViewFor(1).FindUnit(0);
            Assert.Null(Assert.Single(theirs.Boons).Id);
            Assert.Null(theirs.LineageId);
            Assert.Equal(1, theirs.UnrevealedBoonCount);
        }

        // ---- R1, R3: a Blessing reveals when it changes a result ----------------------------------------

        [Fact]
        public void Attack_StrengthBlessing_RevealsOnFirstHit()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            var events = state.Apply(new AttackCommand(0, 0, "bow", BruteAt));

            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("trial-might", ((BoonRevealedEvent)events[1]).BoonId);
            Assert.True(state.KnowsBoon(1, 0, "trial-might"));
            Assert.NotNull(events.OfType<AttackResolvedEvent>().Single().Breakdown.FindBoon("trial-might"));

            // The defender now sees the line in what they receive, and the boon in their view.
            var forDefender = new List<MatchEvent>();
            EventFilter.ForPlayer(events, 1, state, forDefender);
            Assert.NotNull(forDefender.OfType<AttackResolvedEvent>().Single().Breakdown.FindBoon("trial-might"));
            Assert.Equal("trial-might", state.ViewFor(1).FindUnit(0).Boons[0].Id);

            // Using it again reveals nothing new.
            state.Apply(new EndTurnCommand(0));
            state.Apply(new EndTurnCommand(1));
            Assert.DoesNotContain(state.Apply(new AttackCommand(0, 0, "bow", BruteAt)), e => e is BoonRevealedEvent || e is LineageRevealedEvent);
        }

        [Fact]
        public void Attack_BlessingModifier_RevealsModifierThenBoonThenLineage()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith("trial-ward"), ArcherAt, BruteAt);
            var events = state.Apply(new AttackCommand(0, 0, "bow", BruteAt));

            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(ModifierRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("ward", ((ModifierRevealedEvent)events[1]).ModifierId);
            Assert.Equal(0, ((ModifierRevealedEvent)events[1]).ToPlayer);
            Assert.Equal("trial-ward", ((BoonRevealedEvent)events[2]).BoonId);
            Assert.Equal(0, ((BoonRevealedEvent)events[2]).ToPlayer);
            Assert.Equal("trial", ((LineageRevealedEvent)events[3]).LineageId);
            Assert.True(state.Knows(0, 1, "ward"));
            Assert.True(state.KnowsBoon(0, 1, "trial-ward"));
        }

        [Fact]
        public void Attack_Immunity_RevealsModifierAndItsBoon()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-frost", "trial-zap"), CombatFixtures.BruteWith("trial-frostproof"), ArcherAt, BruteAt);
            var events = state.Apply(new AttackCommand(0, 0, "zap", BruteAt));
            Assert.Contains(events.OfType<ModifierRevealedEvent>(), e => e.ModifierId == "trial-frostproof" && e.ToPlayer == 0);
            Assert.Contains(events.OfType<BoonRevealedEvent>(), e => e.BoonId == "trial-frostproof" && e.ToPlayer == 0);
            Assert.True(state.KnowsBoon(0, 1, "trial-frostproof"));
            int nullifyAt = events.ToList().FindIndex(e => e is ModifierRevealedEvent m && m.ModifierId == "trial-frostproof");
            int resolvedAt = events.ToList().FindIndex(e => e is AttackResolvedEvent);
            Assert.True(nullifyAt < resolvedAt);
        }

        // ---- R4: an Enchant reveals on the observation that contradicts what the opponent knows (D11) --

        [Fact]
        public void Attack_RangeEnchant_RevealsWhenShotBeyondKnownRange()
        {
            var catalog = CombatFixtures.Catalog();
            // Bowstring takes the bow from 3 to 4; the brute stands 4 away.
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-bowstring"), CombatFixtures.BruteWith(), new Hex(-2, 0), new Hex(2, 0));
            Assert.True(state.Validate(new AttackCommand(0, 0, "bow", new Hex(2, 0))).Ok);
            var events = state.Apply(new AttackCommand(0, 0, "bow", new Hex(2, 0)));

            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("trial-bowstring", ((BoonRevealedEvent)events[1]).BoonId);
            Assert.True(state.KnowsBoon(1, 0, "trial-bowstring"));

            // From now on the opponent's version of the bow has the range too.
            AbilityDef known;
            state.ResolveAbilityKnownTo(1, state.Units.Get(0), "bow", out known);
            Assert.Equal(4, ((AttackDef)known).Range);
        }

        [Fact]
        public void Attack_RangeEnchant_StaysHiddenWhenShotWithinKnownRange()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-bowstring"), CombatFixtures.BruteWith(), new Hex(-1, 0), new Hex(1, 0));
            var events = state.Apply(new AttackCommand(0, 0, "bow", new Hex(1, 0)));
            Assert.DoesNotContain(events, e => e is BoonRevealedEvent || e is LineageRevealedEvent);
            Assert.False(state.KnowsBoon(1, 0, "trial-bowstring"));
            Assert.Null(state.ViewFor(1).FindUnit(0).Boons[0].Id);
        }

        [Fact]
        public void Attack_CostEnchant_RevealsOnUse()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith("trial-cheap"), new Hex(0, 0), new Hex(1, 0), firstPlayer: 1);
            var events = state.Apply(new AttackCommand(1, 1, "strike", new Hex(0, 0)));

            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("trial-cheap", ((BoonRevealedEvent)events[1]).BoonId);
            Assert.Equal(0, ((BoonRevealedEvent)events[1]).ToPlayer);
            Assert.Equal(1, events.OfType<ApSpentEvent>().Single().Amount);
            Assert.Equal(2, state.Units.Get(1).Ap);

            // The jab costs 1 with or without the enchant: using it reveals nothing.
            var jab = state.Apply(new AttackCommand(1, 1, "jab", new Hex(0, 0)));
            Assert.DoesNotContain(jab, e => e is BoonRevealedEvent);
        }

        [Fact]
        public void Attack_AddedElement_RevealsOnFirstUse()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-frost", "trial-zap"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            var events = state.Apply(new AttackCommand(0, 0, "zap", BruteAt));

            // The Sigil reveals with its ability, the element Enchant with the flight, then the lineage once; the frost rider's line follows.
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(BoonRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("trial-zap", ((BoonRevealedEvent)events[1]).BoonId);
            Assert.Equal("trial-frost", ((BoonRevealedEvent)events[3]).BoonId);
            Assert.True(state.KnowsBoon(1, 0, "trial-frost"));
            // Revealing trial-frost reveals its whole definition: the rider it attaches is known without its own event.
            Assert.True(state.Knows(1, 0, "trial-chill"));
            Assert.DoesNotContain(events.OfType<ModifierRevealedEvent>(), e => e.ModifierId == "trial-chill");
            Assert.Equal("trial-chill", state.ViewFor(1).FindUnit(0).Modifiers[0].Id);

            // The bow carries no frost: using it reveals nothing more.
            var bow = state.Apply(new AttackCommand(0, 0, "bow", BruteAt));
            Assert.DoesNotContain(bow, e => e is BoonRevealedEvent);
        }

        [Fact]
        public void Move_WalkRangeBlessing_RevealsWhenDestinationBeyondKnownRange()
        {
            var catalog = CombatFixtures.Catalog();
            var near = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-stride"), CombatFixtures.BruteWith(), new Hex(0, 0), new Hex(3, 0));
            var one = near.Apply(new MoveCommand(0, 0, "move", new Hex(1, 0)));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(UnitMovedEvent), typeof(ApSpentEvent) }, Kinds(one));
            Assert.False(near.KnowsBoon(1, 0, "trial-stride"));

            var far = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-stride"), CombatFixtures.BruteWith(), new Hex(0, 0), new Hex(3, 0));
            var two = far.Apply(new MoveCommand(0, 0, "move", new Hex(2, 0)));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(UnitMovedEvent), typeof(ApSpentEvent) }, Kinds(two));
            Assert.Equal("trial-stride", ((BoonRevealedEvent)two[1]).BoonId);
            Assert.True(far.KnowsBoon(1, 0, "trial-stride"));
        }

        // ---- R5: a Sigil reveals on first use -------------------------------------------------------------

        [Fact]
        public void Attack_Sigil_RevealsBoonOnFirstUse()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-jab"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            UnitView before = state.ViewFor(1).FindUnit(0);
            Assert.Null(before.Abilities[2].Id);
            Assert.Equal("archer-bow", before.Abilities[2].SourceItemId);

            var events = state.Apply(new AttackCommand(0, 0, "jab", BruteAt));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(events));
            Assert.Equal("jab", ((AbilityRevealedEvent)events[0]).AbilityId);
            Assert.Equal("trial-jab", ((BoonRevealedEvent)events[1]).BoonId);
            UnitView after = state.ViewFor(1).FindUnit(0);
            Assert.Equal("jab", after.Abilities[2].Id);
            Assert.Equal("trial-jab", after.Boons[0].Id);
        }

        // ---- R6, R7: the lineage, once ----------------------------------------------------------------------

        [Fact]
        public void Reveal_SecondBoonOfSameLineage_DoesNotRepeatLineageEvent()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might", "trial-jab"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            var first = state.Apply(new AttackCommand(0, 0, "bow", BruteAt));          // Might reveals, and the lineage with it
            Assert.Single(first.OfType<LineageRevealedEvent>());
            var second = state.Apply(new AttackCommand(0, 0, "jab", BruteAt));         // the Sigil reveals; the lineage is already known
            Assert.Contains(second, e => e is BoonRevealedEvent b && b.BoonId == "trial-jab");
            Assert.DoesNotContain(second, e => e is LineageRevealedEvent);
            Assert.Equal(2, state.ViewFor(1).FindUnit(0).Boons.Count(b => b.Revealed));
        }

        [Fact]
        public void Filter_RevealEvents_GoOnlyToThatPlayer()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            var events = state.Apply(new AttackCommand(0, 0, "bow", BruteAt));
            var forAttacker = new List<MatchEvent>();
            var forDefender = new List<MatchEvent>();
            EventFilter.ForPlayer(events, 0, state, forAttacker);
            EventFilter.ForPlayer(events, 1, state, forDefender);
            Assert.Equal(new[] { typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(forAttacker));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(BoonRevealedEvent), typeof(LineageRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, Kinds(forDefender));
        }

        [Fact]
        public void RevealedEntries_CarryBoonsAndLineages_IntoTheNextRound()
        {
            var catalog = CombatFixtures.Catalog();
            var first = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            first.Apply(new AttackCommand(0, 0, "bow", BruteAt));
            var setup = new MatchSetup("field-3", CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith());
            foreach (var entry in first.RevealedEntries) setup.WithRevealed(entry.Viewer, entry.UnitId, entry.Id);

            var second = CombatFixtures.Started(catalog, setup, ArcherAt, BruteAt);
            Assert.True(second.KnowsBoon(1, 0, "trial-might"));
            Assert.True(second.KnowsLineage(1, 0, "trial"));
            Assert.True(second.Knows(1, 0, "bow"));
            Assert.Equal("trial-might", second.ViewFor(1).FindUnit(0).Boons[0].Id);
            Assert.DoesNotContain(second.Apply(new AttackCommand(0, 0, "bow", BruteAt)), e => e is BoonRevealedEvent || e is LineageRevealedEvent || e is AbilityRevealedEvent);
        }

        // ---- the view, the mirror, the sweep (spec §6.6) --------------------------------------------------

        [Fact]
        public void View_EnemyBoons_AreCountedButNotNamed_UntilRevealed()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might", "trial-bowstring", "trial-jab"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            UnitView mine = state.ViewFor(0).FindUnit(0);
            Assert.Equal(new[] { "trial-might", "trial-bowstring", "trial-jab" }, mine.Boons.Select(b => b.Id));
            Assert.Equal("trial", mine.LineageId);

            UnitView theirs = state.ViewFor(1).FindUnit(0);
            Assert.Equal(3, theirs.Boons.Count);
            Assert.All(theirs.Boons, b => Assert.Null(b.Id));
            Assert.Equal(3, theirs.UnrevealedBoonCount);
            Assert.Null(theirs.LineageId);

            state.Apply(new AttackCommand(0, 0, "jab", BruteAt));
            theirs = state.ViewFor(1).FindUnit(0);
            Assert.Equal(new string[] { null, null, "trial-jab" }, theirs.Boons.Select(b => b.Id));
            Assert.Equal("trial", theirs.LineageId);
        }

        [Fact]
        public void Mirror_RebuildsRevealedBoonsAndKeepsHiddenSlots()
        {
            var catalog = CombatFixtures.Catalog();
            var truth = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might", "trial-bowstring", "trial-jab", "trial-ward"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            truth.Apply(new AttackCommand(0, 0, "jab", BruteAt));          // reveals the Sigil and the lineage to player 1

            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(1));
            Unit enemy = mirror.Units.Get(0);
            Assert.Equal(new[] { "trial-jab" }, enemy.BoonIds);
            Assert.Equal(3, enemy.HiddenBoonCount);
            Assert.Equal(new[] { 0, 1, 3 }, enemy.HiddenBoonSlots.Select(s => s.Index));
            Assert.Equal("trial", enemy.LineageId);
            Assert.Equal(new[] { "jab" }, enemy.AbilityIds);                  // the unused walk and bow are "?"s; the Sigil's jab is known and rebuilt as a grant
            Assert.Equal(new[] { 0, 1 }, enemy.HiddenAbilitySlots.Select(s => s.Index));
            Assert.Equal(new[] { null, "archer-bow" }, enemy.HiddenAbilitySlots.Select(s => s.SourceItemId));
            Assert.Equal("archer-bow", enemy.AbilitySourceOf("jab"));
            Assert.Equal("trial-jab", enemy.BoonOfAbility("jab"));
            Assert.Equal(1, enemy.HiddenModifierCount);                       // Ward's modifier, not yet seen
            Assert.Empty(enemy.ModifierIds);
            Assert.True(mirror.KnowsBoon(1, 0, "trial-jab"));
            Assert.True(mirror.KnowsLineage(1, 0, "trial"));
            Assert.False(mirror.KnowsBoon(1, 0, "trial-might"));

            // The view survives the round trip, "?" rows and all.
            Assert.Equal(Newtonsoft.Json.Linq.JToken.FromObject(Protocol.Wire.View(truth.ViewFor(1))).ToString(),
                Newtonsoft.Json.Linq.JToken.FromObject(Protocol.Wire.View(mirror.ViewFor(1))).ToString());

            // And once a ward reveals (the archer's arrow is ranged, which the ward cuts), the mirror rebuilt
            // from the new view carries the boon and its modifier, with the modifier owned by the boon.
            var warded = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith("trial-ward"), ArcherAt, BruteAt);
            warded.Apply(new AttackCommand(0, 0, "bow", BruteAt));
            MatchState again = MatchState.FromView(catalog, warded.ViewFor(0));
            Assert.Equal(new[] { "trial-ward" }, again.Units.Get(1).BoonIds);
            Assert.Equal(new[] { "ward" }, again.Units.Get(1).ModifierIds);
            Assert.Equal("trial-ward", again.Units.Get(1).BoonOfModifier("ward"));
            Assert.Equal(0, again.Units.Get(1).HiddenModifierCount);
            Assert.Equal(0, again.Units.Get(1).HiddenBoonCount);
        }

        [Fact]
        public void Mirror_MaxHpMatchesView_BecauseHpBoonsRevealAtStart()
        {
            var catalog = CombatFixtures.Catalog();
            var truth = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-trade"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Assert.Equal(1, truth.Units.Get(0).MaxHp);                       // 12 + 4 - 30, floored
            Assert.Equal(1, truth.Units.Get(0).ApPerTurn);

            foreach (int viewer in new[] { 0, 1 })
            {
                MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(viewer));
                Assert.Equal(1, mirror.Units.Get(0).MaxHp);
                Assert.Equal(1, mirror.Units.Get(0).ApPerTurn);
                Assert.Equal(new[] { "trial-vigour", "trial-trade" }, mirror.Units.Get(0).BoonIds);
            }

            // A view that lies about max hp is refused: the mirror never guesses a hidden stat.
            var view = truth.ViewFor(1);
            UnitView u = view.FindUnit(0);
            var lying = new UnitView(u.Id, u.Owner, u.ItemIds, u.Position, u.Hp, u.MaxHp + 5, u.Ap, u.ApPerTurn, u.IsMine,
                u.Abilities.ToList(), u.Modifiers.ToList(), u.BodyHeight, u.AimHeight, u.LineageId, u.Boons.ToList());
            var e = Assert.Throws<ArgumentException>(() => Unit.FromView(lying, catalog));
            Assert.Contains("max hp", e.Message);
        }

        [Fact]
        public void Mirror_HiddenBoons_CountInThePreview_LikeHiddenModifiers()
        {
            var catalog = CombatFixtures.Catalog();
            var truth = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith("trial-plating", "trial-ward"), ArcherAt, BruteAt);
            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(0));
            DamageBreakdown a = truth.PreviewAttack(0, 0, "bow", BruteAt);
            DamageBreakdown b = mirror.PreviewAttack(0, 0, "bow", BruteAt);
            Assert.Equal(4, a.UnknownCount);                                  // two hidden boons and the two hidden modifiers they attach
            Assert.Equal(a.UnknownCount, b.UnknownCount);
            Assert.Equal(a.Total, b.Total);
            Assert.NotNull(b.FindBoon("trial-might"));                          // the owner's own Blessing is on the mirror
        }

        /// <summary>
        /// The whole-match sweep with boons on both sides: no hidden boon line, no boon id and no lineage
        /// reaches a player before its reveal, and the mirror rebuilt from every view agrees with the truth.
        /// </summary>
        [Fact]
        public void Sweep_NoHiddenBoonLineReachesTheOpponentBeforeItsReveal()
        {
            var catalog = CombatFixtures.Catalog();
            int attacksSeen = 0, revealsSeen = 0;
            for (uint seed = 1; seed <= 12; seed++)
            {
                var setup = new MatchSetup("field-3",
                    CombatFixtures.ArcherWith("trial-vigour", "trial-might", "trial-bowstring", "trial-jab", "trial-heavy"),
                    CombatFixtures.BruteWith("trial-vigour", "trial-ward", "trial-cheap", "trial-plating", "trial-frost", "trial-zap", "trial-hop"),
                    (int)(seed % 2));
                var state = new MatchState(catalog, setup, seed);
                var bot = new RandomBot(seed * 7 + 3);
                var events = new List<MatchEvent>(state.Start());

                for (int step = 0; step < 300 && !state.IsOver; step++)
                {
                    foreach (int viewer in new[] { 0, 1 })
                    {
                        var filtered = new List<MatchEvent>();
                        EventFilter.ForPlayer(events, viewer, state, filtered);
                        foreach (MatchEvent e in filtered)
                        {
                            var attack = e as AttackResolvedEvent;
                            if (attack == null) continue;
                            attacksSeen++;
                            foreach (DamageLine line in attack.Breakdown.Lines)
                            {
                                if (!line.Hidden) continue;
                                Unit owner = state.Units.Get(line.OwnerUnitId);
                                if (owner.Owner == viewer) continue;
                                bool known = line.Kind == DamageLineKind.BoonStat ? state.KnowsBoon(viewer, owner.Id, line.Id) : state.Knows(viewer, owner.Id, line.Id);
                                Assert.True(known, $"seed {seed} step {step}: {line} reached player {viewer} before its reveal");
                            }
                        }
                        revealsSeen += filtered.Count(e => e is BoonRevealedEvent);

                        // The view never names an unrevealed boon or lineage, and the mirror agrees with the truth.
                        PlayerView view = state.ViewFor(viewer);
                        foreach (UnitView u in view.Units)
                        {
                            if (u.Owner == viewer) continue;
                            foreach (KnownEntry b in u.Boons) if (b.Revealed) Assert.True(state.KnowsBoon(viewer, u.Id, b.Id));
                            if (u.LineageId != null) Assert.True(state.KnowsLineage(viewer, u.Id, u.LineageId));
                        }
                        MatchState mirror = MatchState.FromView(catalog, view);
                        Assert.Equal(Protocol.Wire.View(view).ToString(), Protocol.Wire.View(mirror.ViewFor(viewer)).ToString());
                        if (state.ActivePlayer == viewer)
                        {
                            var fromTruth = new List<Command>();
                            var fromMirror = new List<Command>();
                            state.EnumerateLegal(viewer, fromTruth);
                            mirror.EnumerateLegal(viewer, fromMirror);
                            Assert.Equal(fromTruth.Select(c => c.ToString()), fromMirror.Select(c => c.ToString()));
                        }
                    }

                    Command command = bot.Choose(state, state.ActivePlayer) ?? new EndTurnCommand(state.ActivePlayer);
                    events = new List<MatchEvent>(state.Apply(command));
                }
            }
            Assert.True(attacksSeen > 20, $"only {attacksSeen} attacks were swept");
            Assert.True(revealsSeen > 5, $"only {revealsSeen} boon reveals were seen");
        }
    }
}
