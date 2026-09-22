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

            // props-3: a flat radius-3 field with a wall pair and a pillar pair, for body-blocking and
            // prop-targeting tests. Nothing on it has height, so only the bodies can block.
            var propHexes = new List<string>();
            foreach (var h in Hex.Spiral(Hex.Zero, 3))
            {
                string prop = (h == new Hex(1, 0) || h == new Hex(-1, 0)) ? @", ""prop"": ""wall"""
                    : (h == new Hex(0, 1) || h == new Hex(0, -1)) ? @", ""prop"": ""pillar""" : "";
                propHexes.Add($@"{{ ""q"": {h.Q}, ""r"": {h.R}, ""terrain"": ""grass""{prop} }}");
            }
            string propsMap = $@"{{ ""version"": 1, ""id"": ""props-3"", ""name"": ""Props"", ""symmetry"": ""rotational-180"", ""ladderPosition"": 2,
                ""spawns"": {{ ""p1"": {{ ""q"": -3, ""r"": 0 }}, ""p2"": {{ ""q"": 3, ""r"": 0 }} }},
                ""hexes"": [ {string.Join(",", propHexes)} ] }}";

            return new List<ContentFile>
            {
                new ContentFile("terrains.json", @"{ ""version"": 1, ""terrains"": [
                    { ""id"": ""grass"", ""walkable"": true },
                    { ""id"": ""forest"", ""walkable"": true, ""moveCost"": 2, ""modifiers"": [ ""forest-cover"" ] },
                    { ""id"": ""stone"", ""walkable"": false } ] }"),
                new ContentFile("rules.json", @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""ranged"", ""magic"" ], ""globalModifiers"": [ ""high-ground"" ],
                    ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }, ""baseStats"": { ""hp"": 2, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ], ""clock"": { ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 },
                    ""elements"": [ ""fire"", ""frost"", ""lightning"" ], ""series"": { ""bestOf"": 3 }, ""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }, ""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 } }"),
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
                // bowyer: +1 on any attack a bow-kind item granted (spec D part 1 §5.3 itemKinds). Unattached until a test attaches it.
                new ContentFile("modifiers/bowyer.json", @"{ ""version"": 1, ""id"": ""bowyer"", ""trigger"": ""dealDamage"",
                    ""when"": { ""itemKinds"": [ ""bow"" ] }, ""effect"": { ""damage"": 1 } }"),

                // ---- the boons fixture (spec D part 1 §9): one lineage, a boon of each kind and each effect type, two skeletons ----
                // A spell for the crown sigil and a jump for the boots sigil, so every fixture slot can be covered.
                new ContentFile("abilities/zap.json", @"{ ""version"": 1, ""id"": ""zap"", ""type"": ""attack"", ""category"": ""spell"", ""cost"": 1,
                    ""attack"": { ""damage"": 3, ""damageType"": ""magic"", ""range"": 2, ""trajectory"": ""direct"", ""lineOfSight"": true, ""element"": ""lightning"" } }"),
                new ContentFile("abilities/hop.json", @"{ ""version"": 1, ""id"": ""hop"", ""type"": ""movement"", ""cost"": 1, ""movement"": { ""mode"": ""jump"", ""range"": 2, ""jumpHeight"": 1 } }"),
                // A second bow whose shot is an arc, so a slot-wide 'apex' override has one ability it applies to and one it is ignored for.
                new ContentFile("abilities/lob.json", @"{ ""version"": 1, ""id"": ""lob"", ""type"": ""attack"", ""category"": ""weapon"", ""cost"": 1,
                    ""attack"": { ""damage"": 4, ""damageType"": ""ranged"", ""range"": 4, ""trajectory"": ""arc"", ""apex"": 2, ""lineOfSight"": false } }"),
                new ContentFile("items/hunter-bow.json", @"{ ""version"": 1, ""id"": ""hunter-bow"", ""slot"": ""weapon"", ""kind"": ""bow"",
                    ""stats"": { ""power.ranged"": 1 }, ""abilities"": [ ""lob"" ] }"),
                // Modifiers the fixture boons attach: a hidden armour plate, a hidden frost immunity, a hidden frost rider.
                new ContentFile("modifiers/trial-plate.json", @"{ ""version"": 1, ""id"": ""trial-plate"", ""trigger"": ""takeDamage"", ""visibility"": ""hidden"",
                    ""effect"": { ""damage"": -1 } }"),
                new ContentFile("modifiers/trial-frostproof.json", @"{ ""version"": 1, ""id"": ""trial-frostproof"", ""trigger"": ""takeDamage"", ""visibility"": ""hidden"",
                    ""when"": { ""elements"": [ ""frost"" ] }, ""effect"": { ""nullify"": true } }"),
                new ContentFile("modifiers/trial-chill.json", @"{ ""version"": 1, ""id"": ""trial-chill"", ""trigger"": ""dealDamage"", ""visibility"": ""hidden"",
                    ""when"": { ""elements"": [ ""frost"" ] }, ""effect"": { ""damage"": 2 } }"),
                // Blessings.
                new ContentFile("boons/trial-vigour.json", @"{ ""version"": 1, ""id"": ""trial-vigour"", ""name"": ""Trial's Vigour"", ""kind"": ""blessing"", ""lineage"": ""trial"",
                    ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 4 } ] }"),
                new ContentFile("boons/trial-might.json", @"{ ""version"": 1, ""id"": ""trial-might"", ""name"": ""Trial's Might"", ""kind"": ""blessing"", ""lineage"": ""trial"",
                    ""effects"": [ { ""type"": ""stat"", ""key"": ""power.ranged"", ""amount"": 2 } ] }"),
                new ContentFile("boons/trial-ward.json", @"{ ""version"": 1, ""id"": ""trial-ward"", ""name"": ""Trial's Ward"", ""kind"": ""blessing"", ""lineage"": ""trial"",
                    ""effects"": [ { ""type"": ""modifier"", ""id"": ""ward"" } ] }"),
                new ContentFile("boons/trial-frostproof.json", @"{ ""version"": 1, ""id"": ""trial-frostproof"", ""name"": ""Trial's Frostproof"", ""kind"": ""blessing"", ""lineage"": ""trial"",
                    ""effects"": [ { ""type"": ""modifier"", ""id"": ""trial-frostproof"" } ] }"),
                new ContentFile("boons/trial-stride.json", @"{ ""version"": 1, ""id"": ""trial-stride"", ""name"": ""Trial's Stride"", ""kind"": ""blessing"", ""lineage"": ""trial"",
                    ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""move"", ""field"": ""range"", ""amount"": 1 } ] }"),
                new ContentFile("boons/trial-trade.json", @"{ ""version"": 1, ""id"": ""trial-trade"", ""name"": ""Trial's Trade"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""stackable"": true,
                    ""effects"": [ { ""type"": ""stat"", ""key"": ""power.melee"", ""amount"": 2 }, { ""type"": ""stat"", ""key"": ""hp"", ""amount"": -30 }, { ""type"": ""stat"", ""key"": ""ap"", ""amount"": -5 } ] }"),
                // Enchants.
                new ContentFile("boons/trial-bowstring.json", @"{ ""version"": 1, ""id"": ""trial-bowstring"", ""name"": ""Trial's Bowstring"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"", ""kind"": ""bow"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""range"", ""amount"": 1 } ] }"),
                new ContentFile("boons/trial-cheap.json", @"{ ""version"": 1, ""id"": ""trial-cheap"", ""name"": ""Trial's Cheap Strike"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""strike"", ""field"": ""cost"", ""amount"": -1 } ] }"),
                new ContentFile("boons/trial-heavy.json", @"{ ""version"": 1, ""id"": ""trial-heavy"", ""name"": ""Trial's Heavy Arrows"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"", ""kind"": ""bow"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""damage"", ""amount"": 2 }, { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""apex"", ""amount"": 1 } ] }"),
                new ContentFile("boons/trial-frost.json", @"{ ""version"": 1, ""id"": ""trial-frost"", ""name"": ""Trial's Frost"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""exclusive"": [ ""crown-element"" ],
                    ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""frost"" }, { ""type"": ""modifier"", ""id"": ""trial-chill"" } ] }"),
                new ContentFile("boons/trial-flame.json", @"{ ""version"": 1, ""id"": ""trial-flame"", ""name"": ""Trial's Flame"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""exclusive"": [ ""crown-element"" ],
                    ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""fire"" } ] }"),
                new ContentFile("boons/trial-blessed.json", @"{ ""version"": 1, ""id"": ""trial-blessed"", ""name"": ""Trial's Blessed Arrows"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""addTag"", ""target"": ""weapon"", ""tag"": ""blessed"" } ] }"),
                new ContentFile("boons/trial-plating.json", @"{ ""version"": 1, ""id"": ""trial-plating"", ""name"": ""Trial's Plating"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""armour"" }, ""effects"": [ { ""type"": ""modifier"", ""id"": ""trial-plate"" } ] }"),
                // Sigils.
                new ContentFile("boons/trial-zap.json", @"{ ""version"": 1, ""id"": ""trial-zap"", ""name"": ""Trial's Zap"", ""kind"": ""sigil"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""crown"", ""ability"": ""zap"" } ] }"),
                new ContentFile("boons/trial-hop.json", @"{ ""version"": 1, ""id"": ""trial-hop"", ""name"": ""Trial's Hop"", ""kind"": ""sigil"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""boots"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""boots"", ""ability"": ""hop"" } ] }"),
                new ContentFile("boons/trial-jab.json", @"{ ""version"": 1, ""id"": ""trial-jab"", ""name"": ""Trial's Jab"", ""kind"": ""sigil"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""weapon"", ""ability"": ""jab"" } ] }"),
                // Skeletons (spec §6.7): parse and link, fail closed at unit build. Kept out of the pool so the draft never offers them.
                new ContentFile("boons/trial-double.json", @"{ ""version"": 1, ""id"": ""trial-double"", ""name"": ""Trial's Double"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""hits"", ""amount"": 1 } ] }"),
                new ContentFile("boons/trial-lob.json", @"{ ""version"": 1, ""id"": ""trial-lob"", ""name"": ""Trial's Lob"", ""kind"": ""enchant"", ""lineage"": ""trial"",
                    ""requires"": { ""slot"": ""weapon"", ""kind"": ""bow"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""bow"", ""field"": ""trajectory"", ""value"": ""arc"" }, { ""type"": ""abilityOverride"", ""target"": ""bow"", ""field"": ""lineOfSight"", ""value"": ""false"" } ] }"),
                // The lineage: starting Blessing trial-vigour; the pool is every non-skeleton boon except the starting one.
                new ContentFile("lineages/trial.json", @"{ ""version"": 1, ""id"": ""trial"", ""name"": ""Trial"", ""description"": ""A lineage for the tests."", ""startingBlessing"": ""trial-vigour"",
                    ""pool"": [ ""trial-might"", ""trial-ward"", ""trial-frostproof"", ""trial-stride"", ""trial-trade"", ""trial-bowstring"", ""trial-cheap"", ""trial-heavy"", ""trial-frost"", ""trial-flame"", ""trial-blessed"", ""trial-plating"", ""trial-zap"", ""trial-hop"", ""trial-jab"" ] }"),
                new ContentFile("props/pillar.json", @"{ ""version"": 1, ""id"": ""pillar"", ""name"": ""Stone Pillar"",
                    ""bodyHeight"": 6, ""aimHeight"": 3, ""stats"": { ""hp"": 10, ""defense.melee"": 0, ""defense.ranged"": 0 } }"),
                new ContentFile("props/wall.json", @"{ ""version"": 1, ""id"": ""wall"", ""name"": ""Wall"", ""bodyHeight"": 6, ""aimHeight"": 3 }"),
                new ContentFile("maps/field-3.json", map),
                new ContentFile("maps/props-3.json", propsMap),
            };
        }

        internal static ContentCatalog Catalog() => ContentCatalog.Load(Files());

        /// <summary>Bare boots grant no movement, which shipped data would not do; the fixture only needs the sums (D6).</summary>
        internal static readonly Loadout Archer = new Loadout("archer-bow", "bare-crown", "bare-boots", "archer-vest");

        internal static readonly Loadout Brute = new Loadout("brute-club", "bare-crown", "bare-boots", "brute-hide");

        internal static MatchSetup Setup() => new MatchSetup("field-3", Archer, Brute);

        /// <summary>The archer with the trial lineage and these boons (the starting Blessing is not implied; list it if wanted).</summary>
        internal static PlayerBuild ArcherWith(params string[] boons) => ContentFixtures.BuildFor(Archer, "trial", boons);

        internal static PlayerBuild BruteWith(params string[] boons) => ContentFixtures.BuildFor(Brute, "trial", boons);

        /// <summary>Archer (player 0) and brute (player 1) placed directly; the match is started so turn 1 belongs to player 0.</summary>
        internal static MatchState Started(ContentCatalog catalog, MatchSetup setup, Hex archerAt, Hex bruteAt)
        {
            var state = new MatchState(catalog, setup, 1);
            state.Units.Get(0).MoveTo(archerAt);
            state.Units.Get(1).MoveTo(bruteAt);
            state.Start();
            return state;
        }

        /// <summary>Two builds on field-3, placed and started.</summary>
        internal static MatchState StartedWith(ContentCatalog catalog, PlayerBuild player0, PlayerBuild player1, Hex at0, Hex at1, int firstPlayer = 0)
            => Started(catalog, new MatchSetup("field-3", player0, player1, firstPlayer), at0, at1);
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
            @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""magic"" ], ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }, ""baseStats"": " + baseStats + @", ""innateAbilities"": [ ""move"" ], ""clock"": { ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 },
                ""elements"": [ ""fire"", ""frost"", ""lightning"" ], ""series"": { ""bestOf"": 3 }, ""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }, ""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 } }");

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

        /// <summary>An item-kind condition asks who granted the attack, not what it is: the same bow shot matches from a bow and never when innate (spec D part 1 §5.3).</summary>
        [Fact]
        public void Damage_ItemKindCondition_MatchesGrantingItem_NotInnate()
        {
            var catalog = CombatFixtures.Catalog();
            var bowyer = catalog.Modifiers.Get("bowyer");
            var bow = catalog.GetAttack("bow");
            Assert.True(DamageCalculator.Applies(bowyer, bow, null, null, "bow"));
            Assert.False(DamageCalculator.Applies(bowyer, bow, null, null, "club"));
            Assert.False(DamageCalculator.Applies(bowyer, bow, null, null, null));
            Assert.False(DamageCalculator.Applies(bowyer, bow, null, null));

            // Through a match: the archer's bow comes from archer-bow (kind bow), so the line appears; the
            // brute's jab comes from brute-club, so it does not.
            var setup = CombatFixtures.Setup().WithModifier(0, "bowyer").WithModifier(1, "bowyer");
            var state = CombatFixtures.Started(catalog, setup, new Hex(0, 0), new Hex(1, 0));
            Assert.Equal(1, state.ResolveAttackFully(0, "bow", new Hex(1, 0)).FindModifier("bowyer").Amount);

            state.Apply(new EndTurnCommand(0));
            Assert.Null(state.ResolveAttackFully(1, "jab", new Hex(0, 0)).FindModifier("bowyer"));
        }
    }

    public class AttackTargetingTests
    {
        private static TargetCheck Check(MatchState state, Unit attacker, AttackDef attack, Hex target)
            => AttackTargeting.Check(state.Map, state.Bodies, state.Catalog.Rules.Heights, state.Trajectories, attacker, attack, target);

        [Fact]
        public void Check_EmptyTile_NoBody()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            Assert.Equal(TargetRejectReason.NoBody, Check(state, state.Units.Get(0), catalog.GetAttack("bow"), new Hex(0, 0)).Reason);
        }

        [Fact]
        public void Check_OwnUnit_Rejected()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            Assert.Equal(TargetRejectReason.OwnUnit, Check(state, state.Units.Get(0), catalog.GetAttack("bow"), new Hex(-3, 0)).Reason);
        }

        [Fact]
        public void Check_OutOfRange_UsesEuclidean_Diagonal44InGunRange()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Arena(catalog, ContentFixtures.GunKit);
            var shooter = state.Units.Get(0);
            var gun = catalog.GetAttack("quick-shot");           // range 7, so a band of 1..49 squared

            // (4,4) is eight hexes away but only sqrt(48) spacings: a circle reaches it, a hex band would not.
            var diagonal = new Hex(1, 3);
            shooter.MoveTo(new Hex(-3, -1));
            state.Units.Get(1).MoveTo(diagonal);
            Assert.Equal(8, Hex.Distance(shooter.Position, diagonal));
            Assert.Equal(48, Hex.EuclideanSquared(shooter.Position, diagonal));
            Assert.NotEqual(TargetRejectReason.OutOfRange, Check(state, shooter, gun, diagonal).Reason);

            // Eight hexes straight along an axis is 64: outside the same circle.
            shooter.MoveTo(state.MapData.SpawnP1);
            state.Units.Get(1).MoveTo(state.MapData.SpawnP2);
            Assert.Equal(TargetRejectReason.OutOfRange, Check(state, shooter, gun, state.MapData.SpawnP2).Reason);
        }

        [Fact]
        public void Check_DirectWithSight_BlockedReportsNoLineOfSight()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Arena(catalog, ContentFixtures.GunKit);
            var shooter = state.Units.Get(0);
            shooter.MoveTo(Across.From);
            state.Units.Get(1).MoveTo(Across.To);

            var check = Check(state, shooter, catalog.GetAttack("quick-shot"), Across.To);
            Assert.Equal(TargetRejectReason.NoLineOfSight, check.Reason);
            Assert.True(check.HasBlockedAt);
            Assert.Equal(new Hex(0, 0), check.BlockedAt);
            Assert.Equal(2, state.Map[check.BlockedAt].Height);
        }

        [Fact]
        public void Check_ArcWithoutSight_HitsUnseenTarget()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Arena(catalog, ContentFixtures.BowKit);
            var archer = state.Units.Get(0);
            archer.MoveTo(Across.From);
            state.Units.Get(1).MoveTo(Across.To);

            // A straight shot cannot see across the plateau; the lobbed arrow does not need to.
            Assert.Equal(TargetRejectReason.NoLineOfSight, Check(state, archer, catalog.GetAttack("quick-shot"), Across.To).Reason);
            Assert.True(Check(state, archer, catalog.GetAttack("arrow-shot"), Across.To).Ok);
        }

        [Fact]
        public void Check_WallProp_NotDamageable()
        {
            var catalog = CombatFixtures.Catalog();
            var state = PropsMatch(catalog);
            state.Units.Get(0).MoveTo(new Hex(-2, 0));
            var check = Check(state, state.Units.Get(0), catalog.GetAttack("bow"), new Hex(-1, 0));
            Assert.Equal(TargetRejectReason.NotDamageable, check.Reason);
            Assert.IsType<Prop>(check.Victim);
        }

        [Fact]
        public void Check_PillarProp_IsLegalTarget()
        {
            var catalog = CombatFixtures.Catalog();
            var state = PropsMatch(catalog);
            state.Units.Get(0).MoveTo(new Hex(0, -2));
            var check = Check(state, state.Units.Get(0), catalog.GetAttack("bow"), new Hex(0, -1));
            Assert.True(check.Ok);
            Assert.Equal("pillar", Assert.IsType<Prop>(check.Victim).Def.Id);
        }

        [Fact]
        public void WithTrajectory_CloneOfGunAsArc_ClearsOverPlateau()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Arena(catalog, ContentFixtures.GunKit);
            var shooter = state.Units.Get(0);
            shooter.MoveTo(Across.From);
            state.Units.Get(1).MoveTo(Across.To);

            var gun = catalog.GetAttack("quick-shot");
            Assert.Equal(TargetRejectReason.NoLineOfSight, Check(state, shooter, gun, Across.To).Reason);

            // The one seam an enchant would swap: same id, same numbers, a different flight.
            var lobbed = gun.WithTrajectory(Trajectories.Arc, 5, false);
            Assert.True(Check(state, shooter, lobbed, Across.To).Ok);
            Assert.Equal(gun.Id, lobbed.Id);
        }

        [Fact]
        public void Sky_StillNeedsSightWhenFlagged()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = Arena(catalog, ContentFixtures.GunKit);
            var shooter = state.Units.Get(0);
            shooter.MoveTo(Across.From);
            state.Units.Get(1).MoveTo(Across.To);
            var gun = catalog.GetAttack("heavy-shot");

            // sky ignores the plateau, but the two fields are independent: a sighted sky shot still needs sight.
            Assert.Equal(TargetRejectReason.NoLineOfSight,
                Check(state, shooter, gun.WithTrajectory(Trajectories.Sky, 0, true), Across.To).Reason);
            Assert.True(Check(state, shooter, gun.WithTrajectory(Trajectories.Sky, 0, false), Across.To).Ok);
        }

        /// <summary>Two ground tiles either side of arena-4's level-2 plateau, two hexes apart through (0,0).</summary>
        private static readonly (Hex From, Hex To) Across = (new Hex(0, -1), new Hex(0, 1));

        private static MatchState Arena(ContentCatalog catalog, Loadout kit)
        {
            var state = new MatchState(catalog, new MatchSetup("arena-4", kit, kit), 1);
            state.Start();
            return state;
        }

        /// <summary>The props-3 fixture: walls at (±1,0), pillars at (0,±1), both heroes parked out of the way.</summary>
        private static MatchState PropsMatch(ContentCatalog catalog)
        {
            var state = new MatchState(catalog, new MatchSetup("props-3", CombatFixtures.Archer, CombatFixtures.Brute), 1);
            state.Start();
            return state;
        }
    }
}
