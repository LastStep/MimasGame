#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    public class ItemParsingTests
    {
        [Fact]
        public void ItemDef_FromJson_ParsesAllFields()
        {
            var item = ItemDef.FromJson(@"{ ""version"": 1, ""id"": ""longbow"", ""name"": ""Longbow"", ""slot"": ""weapon"", ""kind"": ""bow"",
                ""description"": ""A tall yew bow."", ""stats"": { ""power.weapon"": 2 }, ""abilities"": [ ""arrow-shot"", ""aimed-shot"" ],
                ""tags"": [ ""ranged"", ""bow"" ], ""icon"": ""longbow"" }");

            Assert.Equal("longbow", item.Id);
            Assert.Equal("Longbow", item.Name);
            Assert.Equal(ItemSlots.Weapon, item.Slot);
            Assert.Equal("bow", item.Kind);
            Assert.Equal("A tall yew bow.", item.Description);
            Assert.Equal(2, item.Stats.Get("power.weapon"));
            Assert.Equal(new[] { "arrow-shot", "aimed-shot" }, item.AbilityIds);
            Assert.Equal(new[] { "bow", "ranged" }, item.Tags);
            Assert.True(item.HasTag("bow"));
            Assert.Equal("longbow", item.Icon);
        }

        [Fact]
        public void ItemDef_FromJson_UnknownSlot_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => ItemDef.FromJson(
                @"{ ""version"": 1, ""id"": ""x"", ""slot"": ""hat"", ""kind"": ""felt"", ""stats"": {}, ""abilities"": [] }"));
            Assert.Contains("unknown slot 'hat'", e.Message);
        }

        [Fact]
        public void ItemDef_FromJson_NegativeStat_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => ItemDef.FromJson(
                @"{ ""version"": 1, ""id"": ""x"", ""slot"": ""armour"", ""kind"": ""rag"", ""stats"": { ""hp"": -1 }, ""abilities"": [] }"));
            Assert.Contains("item 'x'.stats['hp'] must not be negative.", e.Message);
        }

        [Fact]
        public void ItemDef_FromJson_MissingStats_Throws()
        {
            Assert.Throws<MapLoadException>(() => ItemDef.FromJson(
                @"{ ""version"": 1, ""id"": ""x"", ""slot"": ""armour"", ""kind"": ""rag"", ""abilities"": [] }"));
        }

        [Fact]
        public void ItemDef_FromJson_EmptyStats_IsAllowed()
        {
            var item = ItemDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""slot"": ""crown"", ""kind"": ""bare"", ""stats"": {}, ""abilities"": [] }");
            Assert.Empty(item.Stats.Entries);
            Assert.Empty(item.AbilityIds);
            Assert.Equal("x", item.Name);
        }

        [Fact]
        public void RulesDef_FromJson_RequiresBaseStatsAndInnateAbilities()
        {
            Assert.Throws<MapLoadException>(() => RulesDef.FromJson(
                @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""innateAbilities"": [ ""move"" ] }"));
            Assert.Throws<MapLoadException>(() => RulesDef.FromJson(
                @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""baseStats"": { ""hp"": 20, ""ap"": 3 } }"));

            var negative = Assert.Throws<MapLoadException>(() => RulesDef.FromJson(
                @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""baseStats"": { ""hp"": 20, ""ap"": 3, ""power.weapon"": -1 },
                    ""innateAbilities"": [ ""move"" ] }"));
            Assert.Contains("rules.baseStats['power.weapon'] must not be negative.", negative.Message);

            Assert.Throws<MapLoadException>(() => RulesDef.FromJson(
                @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""baseStats"": { ""hp"": 20, ""ap"": 3 },
                    ""innateAbilities"": [ ""move"", ""move"" ] }"));

            var rules = RulesDef.FromJson(
                @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""baseStats"": { ""hp"": 20, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ] }");
            Assert.Equal(20, rules.BaseStats.Hp);
            Assert.Equal(new[] { "move" }, rules.InnateAbilityIds);
            Assert.True(rules.IsInnateAbility("move"));
        }

        [Fact]
        public void StatBlock_Add_SumsByKey()
        {
            var a = new StatBlock(new[] { new KeyValuePair<string, int>("hp", 2) });
            var b = new StatBlock(new[]
            {
                new KeyValuePair<string, int>("hp", 8),
                new KeyValuePair<string, int>("defense.weapon", 2),
            });

            var sum = a.Add(b);
            Assert.Equal(10, sum.Get("hp"));
            Assert.Equal(2, sum.Get("defense.weapon"));
            Assert.Equal(new[] { "defense.weapon", "hp" }, sum.Entries.Select(e => e.Key));
            Assert.Equal(2, a.Get("hp"));                       // the operands are untouched
            Assert.Empty(StatBlock.Empty.Entries);
        }
    }

    public class LoadoutTests
    {
        [Fact]
        public void Loadout_Equality_IsByValue()
        {
            var a = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");
            var b = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");
            var other = new Loadout("flintlock", "ember-circlet", "leaping-boots", "leather-jerkin");

            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, other);
            Assert.Equal(new[] { "longbow", "ember-circlet", "leaping-boots", "leather-jerkin" }, a.ItemIds);
            Assert.Equal("leaping-boots", a.IdForSlot(ItemSlots.Boots));
            Assert.Equal("longbow / ember-circlet / leaping-boots / leather-jerkin", a.ToString());
        }

        [Fact]
        public void Loadout_EmptyId_Throws()
        {
            Assert.Throws<ArgumentException>(() => new Loadout("", "ember-circlet", "leaping-boots", "leather-jerkin"));
            Assert.Throws<ArgumentException>(() => new Loadout("longbow", "ember-circlet", null, "leather-jerkin"));
            var kit = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");
            Assert.Throws<ArgumentException>(() => kit.IdForSlot("hat"));
        }
    }

    public class HeroFromLoadoutTests
    {
        [Fact]
        public void Unit_FromLoadout_StatsAreBasePlusItems()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var hero = ContentFixtures.HeroFrom(catalog, ContentFixtures.BowKit, Hex.Zero);

            Assert.Equal(28, hero.Stats.Hp);
            Assert.Equal(3, hero.Stats.Ap);
            Assert.Equal(3, hero.Stats.Get("power.weapon"));
            Assert.Equal(4, hero.Stats.Get("power.spell"));
            Assert.Equal(2, hero.Stats.Get("defense.weapon"));
            Assert.Equal(2, hero.Stats.Get("defense.spell"));
            Assert.Equal(28, hero.Hp);
        }

        [Fact]
        public void Unit_FromLoadout_DuplicateAbilityAcrossItems_Throws()
        {
            var catalog = CombatFixtures.Catalog();
            var items = new List<ItemDef>
            {
                catalog.Items.Get("archer-bow"),
                catalog.Items.Get("bare-crown"),
                catalog.Items.Get("bare-boots"),
                ItemDef.FromJson(@"{ ""version"": 1, ""id"": ""spare-bow"", ""slot"": ""armour"", ""kind"": ""vest"",
                    ""stats"": { ""hp"": 10 }, ""abilities"": [ ""bow"" ] }"),
            };

            var e = Assert.Throws<ArgumentException>(() => new Unit(0, 0, Hex.Zero, catalog.Rules, items));
            Assert.Contains("Ability 'bow' is granted twice.", e.Message);
        }

        [Fact]
        public void MatchState_LoadoutSlotMismatch_Throws()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = new MatchSetup("field-3", new Loadout("bare-crown", "bare-crown", "bare-boots", "archer-vest"), CombatFixtures.Brute);
            var e = Assert.Throws<ArgumentException>(() => new MatchState(catalog, setup, 1));
            Assert.Contains("weapon", e.Message);
        }

        [Fact]
        public void MatchState_UnknownItem_Throws()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = new MatchSetup("field-3", new Loadout("no-such-bow", "bare-crown", "bare-boots", "archer-vest"), CombatFixtures.Brute);
            var e = Assert.Throws<ArgumentException>(() => new MatchState(catalog, setup, 1));
            Assert.Contains("no-such-bow", e.Message);
        }
    }

    public class LoadoutPlayerViewTests
    {
        private static readonly Hex ArcherAt = new Hex(-1, 0);
        private static readonly Hex BruteAt = new Hex(2, 0);

        [Fact]
        public void PlayerView_EnemyItems_AreVisible()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), ArcherAt, BruteAt);
            var theirs = state.ViewFor(0).FindUnit(1);
            Assert.Equal(new[] { "brute-club", "bare-crown", "bare-boots", "brute-hide" }, theirs.ItemIds);
        }

        [Fact]
        public void PlayerView_EnemyHiddenAbility_KeepsSourceItem()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), ArcherAt, BruteAt);
            var theirs = state.ViewFor(0).FindUnit(1);

            KnownEntry walk = theirs.Abilities[0];
            Assert.False(walk.Revealed);
            Assert.Null(walk.SourceItemId);

            KnownEntry jab = theirs.Abilities[1];
            Assert.Null(jab.Id);
            Assert.Equal("brute-club", jab.SourceItemId);
            Assert.Equal(3, theirs.Abilities.Count);
        }

        [Fact]
        public void PlayerView_OwnAbilities_AreRevealedWithSources()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), ArcherAt, BruteAt);
            var mine = state.ViewFor(0).FindUnit(0);

            Assert.Equal(new[] { "move", "bow" }, mine.Abilities.Select(a => a.Id));
            Assert.Null(mine.Abilities[0].SourceItemId);
            Assert.Equal("archer-bow", mine.Abilities[1].SourceItemId);
        }
    }

    public class LaneDamageTests
    {
        private static readonly Hex Attacker = new Hex(-4, 3);
        private static readonly Hex Target = new Hex(-1, 3);

        /// <summary>Bow kit (player 0) against gun kit (player 1) on flat, height-0 ground of the shipped arena.</summary>
        private static MatchState Kits(ContentCatalog catalog)
        {
            var setup = new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit);
            var state = new MatchState(catalog, setup, 1);
            state.Units.Get(0).MoveTo(Attacker);
            state.Units.Get(1).MoveTo(Target);
            state.Start();
            return state;
        }

        [Fact]
        public void DamageCalculator_WeaponLane_UsesStrengthAndWeaponDefense()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Kits(catalog);
            var breakdown = state.PreviewAttack(0, 0, "arrow-shot", Target);

            Assert.Equal(new[] { 3, 3, -2 }, breakdown.Lines.Select(l => l.Amount));
            Assert.Equal(4, breakdown.Total);
            Assert.True(breakdown.IsExact);
        }

        [Fact]
        public void DamageCalculator_SpellLane_UsesMagicAndSpellDefense()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Kits(catalog);
            var breakdown = state.PreviewAttack(0, 0, "fire-bolt", Target);

            Assert.Equal(new[] { 6, 4, -2 }, breakdown.Lines.Select(l => l.Amount));
            Assert.Equal(8, breakdown.Total);
        }

        [Fact]
        public void RandomBot_TwoDifferentKits_PlaysToCompletion_Deterministically()
        {
            var first = PlayKits(11);
            var second = PlayKits(11);

            Assert.True(first.over, "the bow kit versus the gun kit did not finish within the command budget");
            Assert.Equal(first.log, second.log);
            Assert.Equal(first.winner, second.winner);
        }

        private static (bool over, int winner, List<string> log) PlayKits(uint seed)
        {
            var catalog = ContentFixtures.RepoCatalog();
            var setup = new MatchSetup("board-3", ContentFixtures.BowKit, ContentFixtures.GunKit);
            var state = new MatchState(catalog, setup, seed);
            var log = new List<string>();
            foreach (var e in state.Start()) log.Add(e.ToString());

            var bots = new IBot[] { new RandomBot(seed * 2 + 1), new RandomBot(seed * 2 + 2) };
            int commands = 0;
            while (!state.IsOver && commands < 4000)
            {
                Command command = bots[state.ActivePlayer].Choose(state, state.ActivePlayer);
                Assert.NotNull(command);
                log.Add(command.ToString());
                foreach (var e in state.Apply(command)) log.Add(e.ToString());
                commands++;
            }
            return (state.IsOver, state.Winner, log);
        }
    }

    /// <summary>
    /// Conventions the shipped catalogue keeps but the loader does not enforce (design: #equipment rule 6,
    /// #boots). A one-ability weapon or an empty crown is legal content; it just never ships.
    /// </summary>
    public class ShippedItemConventionTests
    {
        [Fact]
        public void ShippedItems_WeaponsAndCrownsHaveExactlyTwoAbilities()
        {
            var catalog = ContentFixtures.RepoCatalog();
            foreach (var item in catalog.Items.All)
            {
                if (item.Slot != ItemSlots.Weapon && item.Slot != ItemSlots.Crown) continue;
                Assert.Equal(2, item.AbilityIds.Count);
            }
        }

        [Fact]
        public void ShippedItems_BootsGrantExactlyOneMovementAbility()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var boots = catalog.Items.All.Where(i => i.Slot == ItemSlots.Boots).ToList();
            Assert.NotEmpty(boots);
            foreach (var item in boots)
            {
                Assert.Equal(1, item.AbilityIds.Count);
                Assert.NotNull(catalog.GetMovement(item.AbilityIds[0]));
            }
        }

        [Fact]
        public void ShippedItems_EveryAbilityLaneIsWeaponOrSpell()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var lanes = new[] { "weapon", "spell" };
            Assert.Equal(lanes, catalog.Rules.DamageTypes);

            foreach (var ability in catalog.Abilities.All)
            {
                var attack = ability as AttackDef;
                if (attack != null) Assert.Contains(attack.DamageType, lanes);
            }
            foreach (var entry in catalog.Rules.BaseStats.Entries)
            {
                string lane = StatBlock.DamageTypeOf(entry.Key);
                if (lane != null) Assert.Contains(lane, lanes);
            }
            foreach (var item in catalog.Items.All)
            {
                foreach (var entry in item.Stats.Entries)
                {
                    string lane = StatBlock.DamageTypeOf(entry.Key);
                    if (lane != null) Assert.Contains(lane, lanes);
                }
            }
        }

        [Fact]
        public void ShippedItems_NoItemHasNegativeStats()
        {
            var catalog = ContentFixtures.RepoCatalog();
            foreach (var item in catalog.Items.All)
                foreach (var entry in item.Stats.Entries)
                    Assert.True(entry.Value >= 0, item.Id + " " + entry.Key);
        }
    }
}
