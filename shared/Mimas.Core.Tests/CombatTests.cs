#nullable disable

using System.Collections.Generic;
using System.Linq;
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
    /// A small, fully controlled content set: a radius-3 grass field with two height-1 bumps and two forest
    /// tiles, an archer kit and a brute kit, one public terrain modifier, one global height modifier, one
    /// hidden ward. The fixture keeps the melee/ranged/magic lanes on purpose: the engine is lane-agnostic
    /// and only shipped data moved to weapon/spell.
    /// </summary>
    internal static class CombatFixtures
    {
        internal static List<ContentFile> Files()
        {
            var hexes = new List<string>();
            foreach (var h in Hex.Spiral(Hex.Zero, 3))
            {
                string terrain = (h == new Hex(0, 1) || h == new Hex(0, -1)) ? "forest" : "grass";
                int height = (h == new Hex(-1, 0) || h == new Hex(1, 0)) ? 1 : 0;
                hexes.Add($@"{{ ""q"": {h.Q}, ""r"": {h.R}, ""terrain"": ""{terrain}"", ""height"": {height} }}");
            }
            string map = $@"{{ ""version"": 1, ""id"": ""field-3"", ""name"": ""Field"", ""symmetry"": ""rotational-180"", ""ladderPosition"": 1,
                ""spawns"": {{ ""p1"": {{ ""q"": -3, ""r"": 0 }}, ""p2"": {{ ""q"": 3, ""r"": 0 }} }},
                ""hexes"": [ {string.Join(",", hexes)} ] }}";

            return new List<ContentFile>
            {
                new ContentFile("terrains.json", @"{ ""version"": 1, ""terrains"": [
                    { ""id"": ""grass"", ""walkable"": true },
                    { ""id"": ""forest"", ""walkable"": true, ""moveCost"": 2, ""modifiers"": [ ""forest-cover"" ] },
                    { ""id"": ""stone"", ""walkable"": false } ] }"),
                new ContentFile("rules.json", @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""ranged"", ""magic"" ], ""globalModifiers"": [ ""high-ground"" ],
                    ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }, ""baseStats"": { ""hp"": 2, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ] }"),
                new ContentFile("abilities/move.json", @"{ ""version"": 1, ""id"": ""move"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 1 } }"),
                new ContentFile("abilities/bow.json", @"{ ""version"": 1, ""id"": ""bow"", ""type"": ""attack"", ""category"": ""weapon"", ""cost"": 2,
                    ""attack"": { ""damage"": 5, ""damageType"": ""ranged"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true }, ""tags"": [ ""arrow"" ] }"),
                new ContentFile("abilities/strike.json", @"{ ""version"": 1, ""id"": ""strike"", ""type"": ""attack"", ""category"": ""weapon"", ""cost"": 2,
                    ""attack"": { ""damage"": 4, ""damageType"": ""melee"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"),
                new ContentFile("abilities/jab.json", @"{ ""version"": 1, ""id"": ""jab"", ""type"": ""attack"", ""category"": ""weapon"", ""cost"": 1,
                    ""attack"": { ""damage"": 2, ""damageType"": ""melee"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"),
                new ContentFile("items/archer-bow.json", @"{ ""version"": 1, ""id"": ""archer-bow"", ""slot"": ""weapon"", ""kind"": ""bow"",
                    ""stats"": { ""power.ranged"": 2, ""defense.ranged"": 1, ""defense.melee"": 0 }, ""abilities"": [ ""bow"" ] }"),
                new ContentFile("items/brute-club.json", @"{ ""version"": 1, ""id"": ""brute-club"", ""slot"": ""weapon"", ""kind"": ""club"",
                    ""stats"": { ""power.melee"": 3, ""defense.melee"": 2, ""defense.ranged"": 3 }, ""abilities"": [ ""jab"", ""strike"" ] }"),
                new ContentFile("items/bare-crown.json", @"{ ""version"": 1, ""id"": ""bare-crown"", ""slot"": ""crown"", ""kind"": ""bare"",
                    ""stats"": {}, ""abilities"": [] }"),
                new ContentFile("items/bare-boots.json", @"{ ""version"": 1, ""id"": ""bare-boots"", ""slot"": ""boots"", ""kind"": ""bare"",
                    ""stats"": {}, ""abilities"": [] }"),
                new ContentFile("items/archer-vest.json", @"{ ""version"": 1, ""id"": ""archer-vest"", ""slot"": ""armour"", ""kind"": ""vest"",
                    ""stats"": { ""hp"": 10 }, ""abilities"": [] }"),
                new ContentFile("items/brute-hide.json", @"{ ""version"": 1, ""id"": ""brute-hide"", ""slot"": ""armour"", ""kind"": ""hide"",
                    ""stats"": { ""hp"": 18 }, ""abilities"": [] }"),
                new ContentFile("modifiers/high-ground.json", @"{ ""version"": 1, ""id"": ""high-ground"", ""trigger"": ""dealDamage"",
                    ""when"": { ""heightAdvantage"": true }, ""effect"": { ""damage"": 2 } }"),
                new ContentFile("modifiers/forest-cover.json", @"{ ""version"": 1, ""id"": ""forest-cover"", ""trigger"": ""takeDamage"",
                    ""when"": { ""damageTypes"": [ ""ranged"" ] }, ""effect"": { ""damage"": -1 } }"),
                new ContentFile("modifiers/ward.json", @"{ ""version"": 1, ""id"": ""ward"", ""trigger"": ""takeDamage"", ""visibility"": ""hidden"",
                    ""when"": { ""damageTypes"": [ ""ranged"", ""magic"" ] }, ""effect"": { ""damage"": -4 } }"),
                new ContentFile("modifiers/flaming.json", @"{ ""version"": 1, ""id"": ""flaming"", ""trigger"": ""dealDamage"", ""visibility"": ""hidden"",
                    ""when"": { ""tags"": [ ""arrow"" ] }, ""effect"": { ""damage"": 1 } }"),
                new ContentFile("maps/field-3.json", map),
            };
        }

        internal static ContentCatalog Catalog() => ContentCatalog.Load(Files());

        /// <summary>Bare boots grant no movement, which shipped data would not do; the fixture only needs the sums (D6).</summary>
        internal static readonly Loadout Archer = new Loadout("archer-bow", "bare-crown", "bare-boots", "archer-vest");

        internal static readonly Loadout Brute = new Loadout("brute-club", "bare-crown", "bare-boots", "brute-hide");

        internal static MatchSetup Setup() => new MatchSetup("field-3", Archer, Brute);

        /// <summary>Archer (player 0) and brute (player 1) placed directly; the match is started so turn 1 belongs to player 0.</summary>
        internal static MatchState Started(ContentCatalog catalog, MatchSetup setup, Hex archerAt, Hex bruteAt)
        {
            var state = new MatchState(catalog, setup, 1);
            state.Units.Get(0).MoveTo(archerAt);
            state.Units.Get(1).MoveTo(bruteAt);
            state.Start();
            return state;
        }
    }

    public class DataSchemaTests
    {
        [Fact]
        public void StatBlock_RequiresHpAndAp_AndRejectsUnknownKeys()
        {
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""ap"": 3 }"));
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""hp"": 5, ""ap"": 3, ""luck"": 1 }"));
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""hp"": 0, ""ap"": 3 }"));
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""hp"": 5 }"));

            var rules = Rules(@"{ ""hp"": 7, ""ap"": 2, ""power.magic"": 4 }");
            Assert.Equal(7, rules.BaseStats.Hp);
            Assert.Equal(2, rules.BaseStats.Ap);
            Assert.Equal(4, rules.BaseStats.Get("power.magic"));
            Assert.Equal(0, rules.BaseStats.Get("defense.magic"));
        }

        /// <summary>A rules file whose only interesting part is its <c>baseStats</c> object.</summary>
        private static RulesDef Rules(string baseStats) => RulesDef.FromJson(
            @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""magic"" ], ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }, ""baseStats"": " + baseStats + @", ""innateAbilities"": [ ""move"" ] }");

        [Fact]
        public void Catalogue_RejectsStatKeysAndAttacksWithUndeclaredDamageTypes()
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("items/odd.json", @"{ ""version"": 1, ""id"": ""odd"", ""slot"": ""armour"", ""kind"": ""odd"", ""stats"": { ""power.psychic"": 1 }, ""abilities"": [] }"));
            files.Add(new ContentFile("abilities/mind.json", @"{ ""version"": 1, ""id"": ""mind"", ""type"": ""attack"", ""category"": ""spell"", ""attack"": { ""damage"": 1, ""damageType"": ""psychic"", ""range"": 2, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("items/odd.json: item 'odd' stat 'power.psychic' uses undeclared damage type 'psychic'", errors);
            Assert.Contains("abilities/mind.json: attack 'mind' uses undeclared damage type 'psychic'", errors);
        }

        [Fact]
        public void Catalogue_RejectsUnknownModifierReferences()
        {
            var files = CombatFixtures.Files();
            files.RemoveAll(f => f.Path == "modifiers/forest-cover.json");
            files = files.Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace("\"high-ground\"", "\"high-ground\", \"phantom\""))
                : f).ToList();
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("terrains.json: terrain 'forest' references unknown modifier 'forest-cover'", errors);
            Assert.Contains("rules.json: rules reference unknown global modifier 'phantom'", errors);
        }

        [Fact]
        public void Catalogue_RejectsMapEffectThatIsNotAModifier()
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "maps/field-3.json"
                ? new ContentFile(f.Path, f.Text.Replace("\"q\": 0, \"r\": 0, \"terrain\": \"grass\", \"height\": 0", "\"q\": 0, \"r\": 0, \"terrain\": \"grass\", \"height\": 0, \"effect\": \"shrine\""))
                : f).ToList();
            Assert.Contains("hex Hex(0,0) references unknown effect modifier 'shrine'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void AttackDef_ParsesRangeBandAndCost_AndRequiresCategory()
        {
            var attack = (AttackDef)AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""volley"", ""type"": ""attack"", ""category"": ""weapon"", ""cost"": 3,
                ""attack"": { ""damage"": 3, ""damageType"": ""ranged"", ""range"": 4, ""minRange"": 2, ""trajectory"": ""direct"", ""lineOfSight"": true }, ""tags"": [ ""arrow"" ] }");
            Assert.Equal(3, attack.Cost);
            Assert.Equal(2, attack.MinRange);
            Assert.Equal(4, attack.Range);
            Assert.False(attack.InRangeSquared(1));
            Assert.True(attack.InRangeSquared(16));
            Assert.Equal(new[] { "arrow" }, attack.Tags);
            Assert.Equal(AbilityCategories.Weapon, attack.Category);

            Assert.Throws<MapLoadException>(() => AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""type"": ""attack"", ""attack"": { ""damage"": 3, ""damageType"": ""ranged"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            Assert.Throws<MapLoadException>(() => AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""type"": ""attack"", ""category"": ""movement"", ""attack"": { ""damage"": 3, ""damageType"": ""ranged"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            Assert.Throws<MapLoadException>(() => AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""type"": ""attack"", ""category"": ""spell"", ""attack"": { ""damage"": 3, ""damageType"": ""ranged"", ""range"": 2, ""minRange"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
        }

        [Fact]
        public void MovementDef_CostDefaultsToOne_AndReadsCost()
        {
            Assert.Equal(1, AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""m"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 1 } }").Cost);
            Assert.Equal(2, AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""m"", ""type"": ""movement"", ""cost"": 2, ""movement"": { ""mode"": ""walk"", ""range"": 1 } }").Cost);
            Assert.Throws<MapLoadException>(() => AbilityDef.FromJson(@"{ ""version"": 1, ""id"": ""m"", ""type"": ""movement"", ""cost"": -1, ""movement"": { ""mode"": ""walk"", ""range"": 1 } }"));
        }

        [Fact]
        public void ModifierDef_FailsClosedOnUnknownConditionsAndEffects()
        {
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""when"": { ""moonPhase"": true }, ""effect"": { ""damage"": 1 } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""effect"": { ""range"": 1 } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""effect"": { ""damage"": 0 } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""onSneeze"", ""effect"": { ""damage"": 1 } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""visibility"": ""secret"", ""effect"": { ""damage"": 1 } }"));

            var def = ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""takeDamage"", ""visibility"": ""hidden"", ""when"": { ""damageTypes"": [ ""magic"" ], ""tags"": [ ""fire"" ] }, ""effect"": { ""damage"": -3 } }");
            Assert.True(def.IsHidden);
            Assert.Equal(-3, def.Damage);
            Assert.Null(def.HeightAdvantage);
        }

        [Fact]
        public void ShippedData_LoadsWithStatsAttacksAndModifiers()
        {
            var catalog = ContentFixtures.RepoCatalog();
            Assert.Equal(20, catalog.Rules.BaseStats.Hp);
            Assert.Equal(8, catalog.Items.Get("leather-jerkin").Stats.Hp);
            Assert.Equal("spell", catalog.GetAttack("fire-bolt").DamageType);
            Assert.Equal(2, catalog.GetAttack("fire-bolt").Cost);
            Assert.Equal(new[] { "high-ground" }, catalog.Rules.GlobalModifierIds);
            Assert.True(catalog.Modifiers.Get("ward-of-feathers").IsHidden);
        }
    }

    public class DamageCalculatorTests
    {
        private static readonly Hex High = new Hex(-1, 0);      // height 1
        private static readonly Hex Low = new Hex(2, 0);        // height 0, distance 3 from High
        private static readonly Hex Forest = new Hex(0, 1);

        [Fact]
        public void Compute_ListsBasePowerDefenseInFixedOrder()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(0, 0), new Hex(0, 2));
            var breakdown = state.ResolveAttackFully(0, "bow", new Hex(0, 2));

            Assert.Equal(new[] { DamageLineKind.Base, DamageLineKind.Power, DamageLineKind.Defense }, breakdown.Lines.Select(l => l.Kind));
            Assert.Equal(new[] { 5, 2, -3 }, breakdown.Lines.Select(l => l.Amount));
            Assert.Equal(4, breakdown.Total);
            Assert.True(breakdown.IsExact);
        }

        [Fact]
        public void Compute_HeightAdvantageComesFromTheGlobalModifier()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), High, Low);
            var breakdown = state.ResolveAttackFully(0, "bow", Low);

            var line = breakdown.FindModifier("high-ground");
            Assert.NotNull(line);
            Assert.Equal(DamageLineOwner.Global, line.Owner);
            Assert.Equal(2, line.Amount);
            Assert.Equal(5 + 2 - 3 + 2, breakdown.Total);

            // Uphill: no bonus, no penalty.
            var uphill = CombatFixtures.Started(catalog, CombatFixtures.Setup(), Low, High);
            Assert.Null(uphill.ResolveAttackFully(0, "bow", High).FindModifier("high-ground"));
        }

        [Fact]
        public void Compute_TerrainModifierOnTargetTileApplies_ByDamageType()
        {
            var catalog = CombatFixtures.Catalog();
            var ranged = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(0, -1), Forest);
            var line = ranged.ResolveAttackFully(0, "bow", Forest).FindModifier("forest-cover");
            Assert.NotNull(line);
            Assert.Equal(DamageLineOwner.TargetTile, line.Owner);
            Assert.Equal(-1, line.Amount);

            // Melee is not covered by the forest. Brute (player 1) attacks the archer standing in the forest.
            var melee = CombatFixtures.Started(catalog, new MatchSetup("field-3", CombatFixtures.Archer, CombatFixtures.Brute, firstPlayer: 1), Forest, new Hex(1, 0));
            Assert.Null(melee.ResolveAttackFully(1, "strike", Forest).FindModifier("forest-cover"));
        }

        [Fact]
        public void Compute_FloorsAtZero()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, new Hex(0, -1), Forest);
            var breakdown = state.ResolveAttackFully(0, "bow", Forest);
            Assert.Equal(5 + 2 - 3 - 1 - 4, breakdown.Lines.Sum(l => l.Amount));
            Assert.Equal(0, breakdown.Total);
        }

        [Fact]
        public void Preview_DropsHiddenEnemyModifier_AndCountsItAsUnknown()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, High, Low);

            var preview = state.PreviewAttack(0, 0, "bow", Low);
            var actual = state.ResolveAttackFully(0, "bow", Low);

            Assert.Null(preview.FindModifier("ward"));
            Assert.Equal(1, preview.UnknownCount);
            Assert.False(preview.IsExact);
            Assert.Equal(6, preview.Total);

            Assert.NotNull(actual.FindModifier("ward"));
            Assert.True(actual.FindModifier("ward").Hidden);
            Assert.Equal(0, actual.UnknownCount);
            Assert.Equal(2, actual.Total);
        }

        [Fact]
        public void Preview_ShowsOwnHiddenModifier_ToItsOwner()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(0, "flaming");
            var state = CombatFixtures.Started(catalog, setup, High, Low);

            var mine = state.PreviewAttack(0, 0, "bow", Low);
            Assert.NotNull(mine.FindModifier("flaming"));
            Assert.Equal(0, mine.UnknownCount);
            Assert.Equal(mine.Total, state.ResolveAttackFully(0, "bow", Low).Total);
        }

        [Fact]
        public void Preview_EqualsActual_WhenNothingIsHidden()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), High, Low);
            var preview = state.PreviewAttack(0, 0, "bow", Low);
            var actual = state.ResolveAttackFully(0, "bow", Low);
            Assert.Equal(actual.Lines.Select(l => l.ToString()), preview.Lines.Select(l => l.ToString()));
            Assert.Equal(actual.Total, preview.Total);
        }

        [Fact]
        public void Applies_MatchesTagsAndTypes()
        {
            var catalog = CombatFixtures.Catalog();
            var bow = catalog.GetAttack("bow");
            var strike = catalog.GetAttack("strike");
            Assert.True(DamageCalculator.Applies(catalog.Modifiers.Get("flaming"), bow, null, null));
            Assert.False(DamageCalculator.Applies(catalog.Modifiers.Get("flaming"), strike, null, null));
            Assert.True(DamageCalculator.Applies(catalog.Modifiers.Get("ward"), bow, null, null));
            Assert.False(DamageCalculator.Applies(catalog.Modifiers.Get("ward"), strike, null, null));
        }
    }

    public class AttackTargetingTests
    {
        [Fact]
        public void Check_RefusesOwnUnit_EmptyTile_OutOfRange_AndBlockedSight()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            var archer = state.Units.Get(0);
            var bow = catalog.GetAttack("bow");
            Unit victim;

            Assert.Equal(TargetRejectReason.NoUnit, AttackTargeting.Check(state.Map, state.Units, archer, bow, new Hex(0, 0), out victim));
            Assert.Equal(TargetRejectReason.NotEnemy, AttackTargeting.Check(state.Map, state.Units, archer, bow, new Hex(-3, 0), out victim));
            Assert.Equal(TargetRejectReason.OutOfRange, AttackTargeting.Check(state.Map, state.Units, archer, bow, new Hex(3, 0), out victim));

            // Brute at distance 2 behind the height-1 bump at (-1,0): both endpoints at 0, bump is taller than both.
            state.Units.Get(1).MoveTo(new Hex(0, 0));
            archer.MoveTo(new Hex(-2, 0));
            Assert.Equal(TargetRejectReason.NoLineOfSight, AttackTargeting.Check(state.Map, state.Units, archer, bow, new Hex(0, 0), out victim));

            // Adjacent from the other side: clear.
            archer.MoveTo(new Hex(0, 1));
            Assert.Equal(TargetRejectReason.None, AttackTargeting.Check(state.Map, state.Units, archer, bow, new Hex(0, 0), out victim));
            Assert.Same(state.Units.Get(1), victim);
        }
    }
}
