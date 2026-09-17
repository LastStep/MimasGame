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
using Mimas.Core.Movement;
using Mimas.Core.Protocol;
using Mimas.Core.Units;
using Newtonsoft.Json;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// ADR-026: the client never holds the truth. It rebuilds a <see cref="MatchState"/> from the
    /// <see cref="PlayerView"/> that comes with every server message and asks that mirror every preview
    /// question, so an illegal click is refused instantly and no second implementation of the rules exists
    /// to drift.
    /// <para>
    /// The claim these tests have to earn is strong: for every step of a real game and for both players,
    /// the mirror must answer <em>exactly</em> what the truth would answer that player, and must refuse to
    /// be advanced or to speak for anyone else. Anything less and the online game is a different game.
    /// </para>
    /// </summary>
    public class MirrorTests
    {
        private const int Games = 20;
        private const int MaxSteps = 300;

        private static ContentCatalog Catalog() => ContentFixtures.RepoCatalog();

        private static MatchSetup Setup() =>
            new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit)
                .WithModifier(0, "ward-of-feathers")
                .WithModifier(1, "stone-skin");

        /// <summary>Every step of <see cref="Games"/> seeded random-bot games, with both players' mirrors beside the truth.</summary>
        private static void OverEveryStep(Action<MatchState, MatchState[], int> check) => OverEveryStep(Games, check);

        private static void OverEveryStep(int games, Action<MatchState, MatchState[], int> check)
        {
            ContentCatalog catalog = Catalog();
            for (uint seed = 1; seed <= games; seed++)
            {
                var state = new MatchState(catalog, Setup(), seed);
                var bot = new RandomBot(seed * 7 + 3);
                state.Start();

                for (int step = 0; step < MaxSteps; step++)
                {
                    var mirrors = new MatchState[MatchSetup.PlayerCount];
                    for (int v = 0; v < MatchSetup.PlayerCount; v++)
                        mirrors[v] = MatchState.FromView(catalog, state.ViewFor(v));

                    for (int v = 0; v < MatchSetup.PlayerCount; v++) check(state, mirrors, v);

                    if (state.IsOver) break;
                    Command command = bot.Choose(state, state.ActivePlayer) ?? new EndTurnCommand(state.ActivePlayer);
                    state.Apply(command);
                }
            }
        }

        private static string Json(PlayerView v) => Wire.View(v).ToString(Formatting.None);

        // ---- the round trip ---------------------------------------------------------------------------

        [Fact]
        public void Mirror_ViewFor_ReproducesSourceView()
        {
            OverEveryStep((truth, mirrors, viewer) =>
                Assert.Equal(Json(truth.ViewFor(viewer)), Json(mirrors[viewer].ViewFor(viewer))));
        }

        // ---- previews ---------------------------------------------------------------------------------

        [Fact]
        public void Mirror_MoveOptions_MatchTruth()
        {
            OverEveryStep((truth, mirrors, viewer) =>
            {
                MatchState mirror = mirrors[viewer];
                Unit unit = truth.Units.All.First(u => u.Owner == viewer);
                foreach (string abilityId in unit.AbilityIds.ToList())
                {
                    if (!(truth.Catalog.Abilities.TryGet(abilityId, out var ability) && ability is MovementDef)) continue;
                    Assert.Equal(Destinations(truth.MoveOptions(unit.Id, abilityId)), Destinations(mirror.MoveOptions(unit.Id, abilityId)));
                }
            });
        }

        [Fact]
        public void Mirror_CheckTarget_MatchesTruth()
        {
            // Every hex of the map, for every attack the viewer's hero owns, at every step: the widest
            // sweep here, so it runs over fewer games than the rest.
            OverEveryStep(6, (truth, mirrors, viewer) =>
            {
                MatchState mirror = mirrors[viewer];
                Unit unit = truth.Units.All.First(u => u.Owner == viewer);
                foreach (string abilityId in unit.AbilityIds.ToList())
                {
                    if (!(truth.Catalog.Abilities.TryGet(abilityId, out var ability) && ability is AttackDef)) continue;
                    foreach (MapHex hex in truth.MapData.Hexes)
                    {
                        TargetCheck a = truth.CheckTarget(unit.Id, abilityId, hex.Position);
                        TargetCheck b = mirror.CheckTarget(unit.Id, abilityId, hex.Position);
                        Assert.Equal(a.Ok, b.Ok);
                        Assert.Equal(a.Reason, b.Reason);
                        Assert.Equal(a.BlockedAt, b.BlockedAt);
                    }
                }
            });
        }

        [Fact]
        public void Mirror_PreviewAttack_MatchesTruthWithViewerKnowledge()
        {
            int previewsChecked = 0;
            var targets = new List<IBody>();

            OverEveryStep((truth, mirrors, viewer) =>
            {
                if (truth.IsOver || truth.ActivePlayer != viewer) return;
                MatchState mirror = mirrors[viewer];
                Unit unit = truth.Units.All.First(u => u.Owner == viewer);

                foreach (string abilityId in unit.AbilityIds.ToList())
                {
                    if (!(truth.Catalog.Abilities.TryGet(abilityId, out var ability) && ability is AttackDef)) continue;
                    truth.AttackTargets(unit.Id, abilityId, targets);
                    foreach (IBody target in targets.ToList())
                    {
                        DamageBreakdown a = truth.PreviewAttack(viewer, unit.Id, abilityId, target.Position);
                        DamageBreakdown b = mirror.PreviewAttack(viewer, unit.Id, abilityId, target.Position);
                        if (a == null) { Assert.Null(b); continue; }

                        Assert.NotNull(b);
                        Assert.Equal(a.Total, b.Total);
                        Assert.Equal(a.UnknownCount, b.UnknownCount);
                        Assert.Equal(a.IsExact, b.IsExact);
                        Assert.Equal(a.Lines.Select(l => l.Id + ":" + l.Amount + ":" + l.Owner), b.Lines.Select(l => l.Id + ":" + l.Amount + ":" + l.Owner));
                        previewsChecked++;
                    }
                }
            });

            Assert.True(previewsChecked > 50, $"Only {previewsChecked} previews were compared; the test is not exercising anything.");
        }

        // ---- legality ---------------------------------------------------------------------------------

        [Fact]
        public void Mirror_EnumerateLegal_MatchesTruth()
        {
            var fromTruth = new List<Command>();
            var fromMirror = new List<Command>();

            OverEveryStep((truth, mirrors, viewer) =>
            {
                // A mirror knows its own player's abilities in full; it deliberately does not know the
                // opponent's, so this is only a claim about the viewer's own options.
                if (truth.ActivePlayer != viewer) return;
                truth.EnumerateLegal(viewer, fromTruth);
                mirrors[viewer].EnumerateLegal(viewer, fromMirror);
                Assert.Equal(fromTruth.Select(c => c.ToString()).OrderBy(s => s, StringComparer.Ordinal),
                    fromMirror.Select(c => c.ToString()).OrderBy(s => s, StringComparer.Ordinal));
            });
        }

        [Fact]
        public void Mirror_Validate_MatchesTruth()
        {
            var legal = new List<Command>();

            OverEveryStep((truth, mirrors, viewer) =>
            {
                MatchState mirror = mirrors[viewer];

                if (truth.ActivePlayer == viewer)
                {
                    truth.EnumerateLegal(viewer, legal);
                    foreach (Command command in legal.ToList())
                        Assert.Equal(truth.Validate(command).ToString(), mirror.Validate(command).ToString());
                }

                foreach (Command command in Illegal(truth, viewer))
                    Assert.Equal(truth.Validate(command).ToString(), mirror.Validate(command).ToString());
            });
        }

        /// <summary>Three refusals per step, one of each shape the HUD can produce by accident.</summary>
        private static IEnumerable<Command> Illegal(MatchState truth, int viewer)
        {
            Unit mine = truth.Units.All.First(u => u.Owner == viewer);
            Unit theirs = truth.Units.All.First(u => u.Owner != viewer);
            yield return new MoveCommand(viewer, mine.Id, "move", new Hex(99, -99));
            yield return new MoveCommand(viewer, theirs.Id, "move", theirs.Position);
            yield return new AttackCommand(viewer, mine.Id, "longbow-shot", new Hex(-99, 99));
        }

        // ---- a mirror is not the truth ----------------------------------------------------------------

        [Fact]
        public void Mirror_Apply_Throws()
        {
            ContentCatalog catalog = Catalog();
            var truth = new MatchState(catalog, Setup(), 4);
            truth.Start();
            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(0));

            Assert.True(mirror.IsMirror);
            Assert.False(truth.IsMirror);

            var legal = new List<Command>();
            mirror.EnumerateLegal(0, legal);
            Command command = legal.First();

            Assert.True(mirror.Validate(command).Ok);
            Assert.Throws<InvalidOperationException>(() => mirror.Apply(command));
            Assert.Throws<InvalidOperationException>(() => mirror.TryApply(command, new List<MatchEvent>(), out _));
            Assert.Throws<InvalidOperationException>(() => mirror.Start());
            Assert.Throws<InvalidOperationException>(() => mirror.ResolveAttackFully(0, "longbow-shot", new Hex(0, 0)));
        }

        [Fact]
        public void Mirror_ViewForOtherPlayer_Throws()
        {
            ContentCatalog catalog = Catalog();
            var truth = new MatchState(catalog, Setup(), 4);
            truth.Start();
            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(1));

            Assert.Equal(1, mirror.MirrorViewer);
            Assert.NotNull(mirror.ViewFor(1));
            Assert.Throws<InvalidOperationException>(() => mirror.ViewFor(0));
            Assert.Throws<InvalidOperationException>(() => mirror.PreviewAttack(0, 1, "flintlock-shot", new Hex(0, 0)));
        }

        // ---- the "?" rows -----------------------------------------------------------------------------

        [Fact]
        public void Mirror_HiddenCounts_ProduceQuestionMarkSlots()
        {
            ContentCatalog catalog = Catalog();
            var truth = new MatchState(catalog, Setup(), 9);
            truth.Start();

            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(0));
            Unit enemy = mirror.Units.All.Single(u => u.Owner == 1);
            Unit mine = mirror.Units.All.Single(u => u.Owner == 0);

            // Player 1 carries exactly one hidden modifier (stone-skin) and nothing of theirs has been used.
            Assert.Equal(1, enemy.HiddenModifierCount);
            Assert.Empty(enemy.ModifierIds);
            Assert.Equal(0, mine.HiddenModifierCount);
            Assert.Equal(0, mine.HiddenAbilityCount);

            // Every ability the opponent's gear grants is a "?" until it is used, and each one still names
            // the item it came from, because gear is public.
            UnitView enemyView = truth.ViewFor(0).Units.Single(u => u.Owner == 1);
            int hiddenAbilities = enemyView.Abilities.Count(a => !a.Revealed);
            Assert.Equal(hiddenAbilities, enemy.HiddenAbilityCount);
            Assert.True(hiddenAbilities > 0);
            Assert.Equal(enemyView.Abilities.Where(a => !a.Revealed).Select(a => a.SourceItemId),
                enemy.HiddenAbilitySlots.Select(s => s.SourceItemId));

            // And the slots come back in the right places, not merely in the right number.
            UnitView rebuilt = mirror.ViewFor(0).Units.Single(u => u.Owner == 1);
            Assert.Equal(enemyView.Abilities.Select(a => a.Id), rebuilt.Abilities.Select(a => a.Id));
            Assert.Equal(enemyView.Abilities.Select(a => a.SourceItemId), rebuilt.Abilities.Select(a => a.SourceItemId));
            Assert.Equal(enemyView.Modifiers.Select(m => m.Id), rebuilt.Modifiers.Select(m => m.Id));
        }

        [Fact]
        public void Mirror_PreviewAttack_KeepsTheUnknownRow()
        {
            ContentCatalog catalog = Catalog();
            var truth = new MatchState(catalog, Setup(), 2);

            // Spawns on arena-4 are eight hexes apart, so put the heroes next to each other before the
            // first turn: this test is about the "?" row, not about walking there.
            Unit mine = truth.Units.All.Single(u => u.Owner == 0);
            Unit theirs = truth.Units.All.Single(u => u.Owner == 1);
            mine.MoveTo(new Hex(3, 0));
            truth.Start();

            Assert.Equal(1, Hex.Distance(mine.Position, theirs.Position));
            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(0));

            var targets = new List<IBody>();
            bool checkedOne = false;
            foreach (string abilityId in mine.AbilityIds.ToList())
            {
                if (!(catalog.Abilities.TryGet(abilityId, out var ability) && ability is AttackDef)) continue;
                truth.AttackTargets(mine.Id, abilityId, targets);
                foreach (IBody target in targets.ToList())
                {
                    if (target.Id != theirs.Id) continue;
                    DamageBreakdown a = truth.PreviewAttack(0, mine.Id, abilityId, target.Position);
                    DamageBreakdown b = mirror.PreviewAttack(0, mine.Id, abilityId, target.Position);

                    // stone-skin sits on player 1 and has never been used, so neither breakdown may be
                    // exact and both must say so by the same number.
                    Assert.Equal(1, a.UnknownCount);
                    Assert.Equal(a.UnknownCount, b.UnknownCount);
                    Assert.Equal(a.Total, b.Total);
                    Assert.False(b.IsExact);
                    checkedOne = true;
                }
            }

            Assert.True(checkedOne, "Player 0 could not attack player 1 from an adjacent hex.");
        }

        /// <summary>
        /// A destroyed prop is simply absent from a view, so it must be absent from the mirror too: the hole
        /// in the wall the player can see is a hole they can walk through, and the mirror is what answers
        /// "can I walk there?" before anything is sent.
        /// </summary>
        [Fact]
        public void Mirror_DestroyedProp_IsAbsentAndItsTileIsFree()
        {
            ContentCatalog catalog = Catalog();

            for (uint seed = 1; seed <= 60; seed++)
            {
                var truth = new MatchState(catalog, Setup(), seed);
                var bot = new RandomBot(seed * 13 + 1);
                var events = new List<MatchEvent>(truth.Start());

                for (int step = 0; step < MaxSteps && !truth.IsOver; step++)
                {
                    Command command = bot.Choose(truth, truth.ActivePlayer) ?? new EndTurnCommand(truth.ActivePlayer);
                    events.AddRange(truth.Apply(command));

                    var destroyed = events.OfType<PropDestroyedEvent>().FirstOrDefault();
                    if (destroyed == null) continue;

                    Prop gone = truth.Props.Single(x => x.Id == destroyed.PropId);
                    MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(0));

                    Assert.DoesNotContain(mirror.Props, x => x.Id == destroyed.PropId);
                    Assert.Equal(truth.Props.Count(x => !x.IsDestroyed), mirror.Props.Count);
                    Assert.False(mirror.Bodies.IsOccupied(gone.Position));
                    Assert.All(mirror.Props, x => Assert.Equal(truth.Props.Single(t => t.Id == x.Id).Hp, x.Hp));
                    return;
                }
            }

            Assert.Fail("No prop was destroyed in 60 seeded games; this test is not exercising anything.");
        }

        /// <summary>
        /// The mirror is thrown away and rebuilt on every message, so anything the player has <em>earned</em>
        /// must survive that: an ability they watched the opponent use may never go back to being a "?".
        /// </summary>
        [Fact]
        public void Mirror_RevealedAbility_SurvivesTheRebuild()
        {
            ContentCatalog catalog = Catalog();
            var truth = new MatchState(catalog, Setup(), 2);

            Unit mine = truth.Units.All.Single(u => u.Owner == 0);
            mine.MoveTo(new Hex(3, 0));
            truth.Start();

            string used = mine.AbilityIds.First(id => catalog.Abilities.TryGet(id, out var a) && a is AttackDef);
            Unit theirs = truth.Units.All.Single(u => u.Owner == 1);

            // Before player 1 has seen anything, that ability is a "?" to them.
            MatchState before = MatchState.FromView(catalog, truth.ViewFor(1));
            Assert.DoesNotContain(used, before.Units.Get(mine.Id).AbilityIds);
            Assert.True(before.Units.Get(mine.Id).HiddenAbilityCount > 0);

            var events = new List<MatchEvent>(truth.Apply(new AttackCommand(0, mine.Id, used, theirs.Position)));
            Assert.Contains(events.OfType<AbilityRevealedEvent>(), e => e.AbilityId == used && e.ToPlayer == 1);

            MatchState after = MatchState.FromView(catalog, truth.ViewFor(1));
            Assert.Contains(used, after.Units.Get(mine.Id).AbilityIds);
            Assert.True(after.Knows(1, mine.Id, used));
            Assert.Equal(before.Units.Get(mine.Id).HiddenAbilityCount - 1, after.Units.Get(mine.Id).HiddenAbilityCount);

            // And it is still in the same slot of the view, not appended to the end.
            Assert.Equal(truth.ViewFor(1).Units.Single(u => u.Owner == 0).Abilities.Select(a => a.Id),
                after.ViewFor(1).Units.Single(u => u.Owner == 0).Abilities.Select(a => a.Id));
        }

        private static IEnumerable<string> Destinations(MovementOptions options) =>
            options.Plans.Select(p => p.Destination.ToString()).OrderBy(s => s, StringComparer.Ordinal).ToList();
    }
}
