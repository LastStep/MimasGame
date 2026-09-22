#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// The wire codec (ADR-027). Everything that crosses a socket must survive a round trip field for
    /// field, and nothing hidden may become visible on the way: these are the tests that make the client
    /// mirror's "the view is the whole truth I have" claim checkable.
    /// </summary>
    public class WireTests
    {
        private static string Json(JToken t) => t.ToString(Formatting.None);

        // ---- commands --------------------------------------------------------------------------------

        [Fact]
        public void Wire_Hex_RoundTrip()
        {
            foreach (var h in new[] { new Hex(0, 0), new Hex(3, -2), new Hex(-4, 7) })
            {
                Assert.Equal(h, Wire.ReadHex(Wire.Hex(h)));
                Assert.Equal($"[{h.Q},{h.R}]", Json(Wire.Hex(h)));
            }
        }

        [Fact]
        public void Wire_MoveCommand_RoundTrip()
        {
            var original = new MoveCommand(1, 4, "leap", new Hex(2, -3));
            var back = Assert.IsType<MoveCommand>(Wire.ReadCommand(Wire.Command(original)));
            Assert.Equal(original.Player, back.Player);
            Assert.Equal(original.UnitId, back.UnitId);
            Assert.Equal(original.AbilityId, back.AbilityId);
            Assert.Equal(original.Destination, back.Destination);
        }

        [Fact]
        public void Wire_AttackCommand_RoundTrip()
        {
            var original = new AttackCommand(0, 2, "fire-bolt", new Hex(-1, 5));
            var back = Assert.IsType<AttackCommand>(Wire.ReadCommand(Wire.Command(original)));
            Assert.Equal(original.Player, back.Player);
            Assert.Equal(original.UnitId, back.UnitId);
            Assert.Equal(original.AbilityId, back.AbilityId);
            Assert.Equal(original.Target, back.Target);
        }

        [Fact]
        public void Wire_EndTurnTimeout_RoundTrip()
        {
            var back = Assert.IsType<EndTurnCommand>(Wire.ReadCommand(Wire.Command(new EndTurnCommand(1, EndTurnReason.Timeout))));
            Assert.Equal(1, back.Player);
            Assert.Equal(EndTurnReason.Timeout, back.Reason);
            Assert.Equal("timeout", Wire.Command(new EndTurnCommand(1, EndTurnReason.Timeout)).Value<string>("reason"));
        }

        [Fact]
        public void Wire_ResignDisconnect_RoundTrip()
        {
            var back = Assert.IsType<ResignCommand>(Wire.ReadCommand(Wire.Command(new ResignCommand(0, ResignReason.Disconnect))));
            Assert.Equal(0, back.Player);
            Assert.Equal(ResignReason.Disconnect, back.Reason);
        }

        // ---- events ----------------------------------------------------------------------------------

        [Fact]
        public void Wire_EveryEventType_RoundTrip()
        {
            List<MatchEvent> events = EveryEventType();

            // All ten types the protocol knows must have shown up, or this test proves less than it claims.
            var kinds = events.Select(e => e.GetType().Name).Distinct().ToList();
            Assert.Equal(10, kinds.Count);

            foreach (MatchEvent e in events)
            {
                JObject encoded = Wire.Event(e);
                MatchEvent back = Wire.ReadEvent(encoded);
                Assert.Equal(e.GetType(), back.GetType());
                Assert.Equal(Json(encoded), Json(Wire.Event(back)));
                AssertEventEqual(e, back);
            }
        }

        [Fact]
        public void Wire_AttackResolved_KeepsTrimmedBreakdown()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, new Hex(-1, 0), new Hex(0, 0));

            var events = new List<MatchEvent>(state.Apply(new AttackCommand(0, 0, "bow", new Hex(0, 0))));
            var filtered = new List<MatchEvent>();
            EventFilter.ForPlayer(events, 0, state, filtered);

            var attack = filtered.OfType<AttackResolvedEvent>().Single();
            AttackResolvedEvent back = (AttackResolvedEvent)Wire.ReadEvent(Wire.Event(attack));

            Assert.Equal(attack.Breakdown.UnknownCount, back.Breakdown.UnknownCount);
            Assert.Equal(attack.Breakdown.Lines.Count, back.Breakdown.Lines.Count);
            Assert.Equal(attack.Breakdown.Total, back.Breakdown.Total);
            Assert.Equal(attack.Breakdown.ToString(), back.Breakdown.ToString());
            Assert.Equal(attack.Damage, back.Damage);
            Assert.Equal(attack.TargetHpAfter, back.TargetHpAfter);
        }

        /// <summary>The two line kinds boons added (spec D part 1 §6.4) cross the wire by name and come back whole.</summary>
        [Fact]
        public void Wire_Breakdown_RoundTripsBoonStatAndNullify()
        {
            var lines = new List<DamageLine>
            {
                new DamageLine(DamageLineKind.Base, "base", DamageLineOwner.None, -1, 5, false),
                new DamageLine(DamageLineKind.BoonStat, "trial-might", DamageLineOwner.Attacker, 0, 2, true),
                new DamageLine(DamageLineKind.Nullify, "trial-frostproof", DamageLineOwner.Target, 1, -7, true),
            };
            var breakdown = new DamageBreakdown(lines, 0);
            Assert.True(breakdown.Nullified);
            Assert.Equal(0, breakdown.Total);

            JObject encoded = Wire.Breakdown(breakdown);
            Assert.Equal("boonStat", encoded["lines"][1]["kind"].Value<string>());
            Assert.Equal("nullify", encoded["lines"][2]["kind"].Value<string>());

            DamageBreakdown back = Wire.ReadBreakdown(encoded);
            Assert.Equal(breakdown.ToString(), back.ToString());
            Assert.True(back.Nullified);
            Assert.Equal(2, back.FindBoon("trial-might").Amount);
            Assert.Equal("trial-frostproof", back.FindNullify().Id);
            Assert.Throws<WireException>(() => Wire.ReadBreakdown(JObject.Parse(@"{ ""total"": 0, ""unknown"": 0, ""lines"": [ { ""kind"": ""immune"", ""id"": ""x"", ""owner"": ""target"", ""ownerUnitId"": 1, ""amount"": 0, ""hidden"": true } ] }")));
        }

        // ---- views -----------------------------------------------------------------------------------

        [Fact]
        public void Wire_View_RoundTrip_HiddenEntriesStayNull()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var setup = new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit)
                .WithModifier(1, "ward-of-feathers")
                .WithModifier(1, "stone-skin");
            var state = new MatchState(catalog, setup, 11);
            state.Start();

            PlayerView view = state.ViewFor(0);
            JObject encoded = Wire.View(view);
            PlayerView back = Wire.ReadView(encoded);

            Assert.Equal(Json(encoded), Json(Wire.View(back)));

            UnitView enemyBefore = view.Units.Single(u => u.Owner == 1);
            UnitView enemyAfter = back.Units.Single(u => u.Owner == 1);

            Assert.Equal(2, enemyBefore.UnrevealedModifierCount);
            Assert.Equal(enemyBefore.UnrevealedModifierCount, enemyAfter.UnrevealedModifierCount);
            Assert.All(enemyAfter.Modifiers, m => Assert.Null(m.Id));

            // Gear is public even when what it grants is not: the source survives beside a null id.
            Assert.Equal(enemyBefore.ItemIds, enemyAfter.ItemIds);
            for (int i = 0; i < enemyBefore.Abilities.Count; i++)
            {
                Assert.Equal(enemyBefore.Abilities[i].Id, enemyAfter.Abilities[i].Id);
                Assert.Equal(enemyBefore.Abilities[i].SourceItemId, enemyAfter.Abilities[i].SourceItemId);
            }
            Assert.Contains(enemyAfter.Abilities, a => a.Id == null);

            UnitView mineAfter = back.Units.Single(u => u.Owner == 0);
            Assert.True(mineAfter.IsMine);
            Assert.All(mineAfter.Abilities, a => Assert.NotNull(a.Id));
            Assert.Equal(view.Props.Count, back.Props.Count);
            Assert.Equal(view.MapId, back.MapId);
            Assert.Equal(view.Viewer, back.Viewer);
        }

        // ---- malformed frames -------------------------------------------------------------------------

        [Fact]
        public void Wire_UnknownType_ThrowsWireException()
        {
            Assert.Throws<WireException>(() => Wire.ReadCommand(JObject.Parse(@"{ ""type"": ""teleport"", ""player"": 0 }")));
            Assert.Throws<WireException>(() => Wire.ReadEvent(JObject.Parse(@"{ ""type"": ""somethingHappened"" }")));
        }

        [Fact]
        public void Wire_MissingField_ThrowsWireException()
        {
            var e = Assert.Throws<WireException>(() => Wire.ReadCommand(JObject.Parse(@"{ ""type"": ""move"", ""player"": 0, ""unitId"": 0 }")));
            Assert.Contains("abilityId", e.Message);

            Assert.Throws<WireException>(() => Wire.ReadEvent(JObject.Parse(@"{ ""type"": ""turnStarted"", ""player"": 0 }")));
            Assert.Throws<WireException>(() => Wire.ReadView(JObject.Parse(@"{ ""viewer"": 0 }")));
            Assert.Throws<WireException>(() => Wire.ReadHex(JArray.Parse("[1]")));
        }

        [Fact]
        public void Wire_Encode_IsDeterministic()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = new MatchState(catalog, new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit), 5);
            var events = new List<MatchEvent>(state.Start());
            events.AddRange(state.Apply(new EndTurnCommand(0)));

            Assert.Equal(Json(Wire.View(state.ViewFor(0))), Json(Wire.View(state.ViewFor(0))));
            Assert.Equal(Json(Wire.Events(events)), Json(Wire.Events(events)));
            Assert.Equal(Json(Wire.Command(new MoveCommand(0, 0, "move", new Hex(1, 1)))),
                Json(Wire.Command(new MoveCommand(0, 0, "move", new Hex(1, 1)))));
        }

        [Fact]
        public void Wire_Enums_AreStrings()
        {
            List<MatchEvent> events = EveryEventType();
            foreach (JToken token in Wire.Events(events))
            {
                var o = (JObject)token;
                foreach (string field in new[] { "traversal", "kind", "owner", "reason" })
                    AssertNoIntegerAnywhere(o, field);
            }

            var moved = events.OfType<UnitMovedEvent>().First();
            Assert.Equal(JTokenType.String, Wire.Event(moved)["plan"]["traversal"].Type);

            var resolved = events.OfType<AttackResolvedEvent>().First();
            JToken line = Wire.Event(resolved)["breakdown"]["lines"][0];
            Assert.Equal(JTokenType.String, line["kind"].Type);
            Assert.Equal(JTokenType.String, line["owner"].Type);

            Assert.Equal(JTokenType.String, Wire.Event(events.OfType<TurnEndedEvent>().First())["reason"].Type);
            Assert.Equal(JTokenType.String, Wire.Event(events.OfType<MatchEndedEvent>().First())["reason"].Type);
        }

        // ---- helpers ---------------------------------------------------------------------------------

        private static void AssertNoIntegerAnywhere(JToken token, string field)
        {
            foreach (JToken match in token.SelectTokens("$.." + field))
                Assert.NotEqual(JTokenType.Integer, match.Type);
        }

        /// <summary>
        /// Drives a seeded random-bot game on the shipped map until all ten event types have appeared.
        /// Props on arena-4 give <c>propDestroyed</c>, the bot's hidden modifiers give
        /// <c>modifierRevealed</c>, and playing to the end gives <c>unitDied</c> and <c>matchEnded</c>.
        /// </summary>
        private static List<MatchEvent> EveryEventType()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var seen = new List<MatchEvent>();

            for (uint seed = 1; seed <= 200; seed++)
            {
                var setup = new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit)
                    .WithModifier(0, "ward-of-feathers")
                    .WithModifier(1, "stone-skin");
                var state = new MatchState(catalog, setup, seed);
                var bot = new RandomBot(seed * 31 + 7);
                var run = new List<MatchEvent>(state.Start());

                for (int step = 0; step < 400 && !state.IsOver; step++)
                {
                    Command command = bot.Choose(state, state.ActivePlayer) ?? new EndTurnCommand(state.ActivePlayer);
                    run.AddRange(state.Apply(command));
                }

                foreach (MatchEvent e in run)
                    if (!seen.Any(s => s.GetType() == e.GetType())) seen.Add(e);

                if (seen.Select(e => e.GetType()).Distinct().Count() == 10) break;
            }

            return seen;
        }

        private static void AssertEventEqual(MatchEvent expected, MatchEvent actual)
        {
            switch (expected)
            {
                case TurnStartedEvent x:
                {
                    var y = (TurnStartedEvent)actual;
                    Assert.Equal(x.Player, y.Player);
                    Assert.Equal(x.TurnNumber, y.TurnNumber);
                    break;
                }
                case UnitMovedEvent x:
                {
                    var y = (UnitMovedEvent)actual;
                    Assert.Equal(x.UnitId, y.UnitId);
                    Assert.Equal(x.MovementId, y.MovementId);
                    Assert.Equal(x.Plan.Origin, y.Plan.Origin);
                    Assert.Equal(x.Plan.Destination, y.Plan.Destination);
                    Assert.Equal(x.Plan.Traversal, y.Plan.Traversal);
                    Assert.Equal(x.Plan.Cost, y.Plan.Cost);
                    Assert.Equal(x.Plan.Path, y.Plan.Path);
                    Assert.Equal(x.Plan.EnteredTiles, y.Plan.EnteredTiles);
                    break;
                }
                case ApSpentEvent x:
                {
                    var y = (ApSpentEvent)actual;
                    Assert.Equal(x.UnitId, y.UnitId);
                    Assert.Equal(x.AbilityId, y.AbilityId);
                    Assert.Equal(x.Amount, y.Amount);
                    Assert.Equal(x.Remaining, y.Remaining);
                    break;
                }
                case AttackResolvedEvent x:
                {
                    var y = (AttackResolvedEvent)actual;
                    Assert.Equal(x.AttackerId, y.AttackerId);
                    Assert.Equal(x.TargetId, y.TargetId);
                    Assert.Equal(x.AbilityId, y.AbilityId);
                    Assert.Equal(x.Damage, y.Damage);
                    Assert.Equal(x.TargetHpAfter, y.TargetHpAfter);
                    Assert.Equal(x.TargetIsProp, y.TargetIsProp);
                    Assert.Equal(x.Breakdown.ToString(), y.Breakdown.ToString());
                    break;
                }
                case AbilityRevealedEvent x:
                {
                    var y = (AbilityRevealedEvent)actual;
                    Assert.Equal(x.UnitId, y.UnitId);
                    Assert.Equal(x.AbilityId, y.AbilityId);
                    Assert.Equal(x.ToPlayer, y.ToPlayer);
                    break;
                }
                case ModifierRevealedEvent x:
                {
                    var y = (ModifierRevealedEvent)actual;
                    Assert.Equal(x.UnitId, y.UnitId);
                    Assert.Equal(x.ModifierId, y.ModifierId);
                    Assert.Equal(x.ToPlayer, y.ToPlayer);
                    break;
                }
                case PropDestroyedEvent x:
                    Assert.Equal(x.PropId, ((PropDestroyedEvent)actual).PropId);
                    break;
                case UnitDiedEvent x:
                    Assert.Equal(x.UnitId, ((UnitDiedEvent)actual).UnitId);
                    break;
                case TurnEndedEvent x:
                {
                    var y = (TurnEndedEvent)actual;
                    Assert.Equal(x.Player, y.Player);
                    Assert.Equal(x.TurnNumber, y.TurnNumber);
                    Assert.Equal(x.Reason, y.Reason);
                    Assert.Equal(x.Acted, y.Acted);
                    break;
                }
                case MatchEndedEvent x:
                {
                    var y = (MatchEndedEvent)actual;
                    Assert.Equal(x.Winner, y.Winner);
                    Assert.Equal(x.Reason, y.Reason);
                    break;
                }
                default:
                    throw new InvalidOperationException("Unhandled event type " + expected.GetType().Name);
            }
        }
    }
}
