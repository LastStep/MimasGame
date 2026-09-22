#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// The catalogue's link rules for everything the boons system added (spec D part 1 §5): elements on
    /// attacks and modifiers must be declared in <c>rules.elements</c>; boons and lineages load, link and
    /// fail closed; the shipped lineages cover every item.
    /// </summary>
    public class BoonContentTests
    {
        private static List<ContentFile> Replace(List<ContentFile> files, string path, string from, string to)
            => files.Select(f => f.Path == path ? new ContentFile(f.Path, f.Text.Replace(from, to)) : f).ToList();

        [Fact]
        public void Attack_UnknownElement_IsAnError()
        {
            var files = CombatFixtures.Files();
            files = Replace(files, "abilities/bow.json", @"""lineOfSight"": true }", @"""lineOfSight"": true, ""element"": ""acid"" }");
            Assert.Contains("abilities/bow.json: attack 'bow' carries undeclared element 'acid' (rules.json elements: fire, frost, lightning)", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Attack_DeclaredElement_Links()
        {
            var files = CombatFixtures.Files();
            files = Replace(files, "abilities/bow.json", @"""lineOfSight"": true }", @"""lineOfSight"": true, ""element"": ""frost"" }");
            var catalog = ContentCatalog.Load(files);
            Assert.Equal(new[] { "frost" }, catalog.GetAttack("bow").Elements);
        }

        [Fact]
        public void Modifier_UnknownElement_IsAnError()
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("modifiers/acid-skin.json", @"{ ""version"": 1, ""id"": ""acid-skin"", ""trigger"": ""takeDamage"",
                ""when"": { ""elements"": [ ""acid"" ] }, ""effect"": { ""nullify"": true } }"));
            Assert.Contains("modifiers/acid-skin.json: modifier 'acid-skin' conditions on undeclared element 'acid'", ContentFixtures.ErrorsOf(files));
        }

        // ---- boons and lineages load ----------------------------------------------------------------

        [Fact]
        public void Catalogue_LoadsTheFixtureLineageAndBoons()
        {
            var catalog = CombatFixtures.Catalog();
            Assert.Equal(1, catalog.Lineages.Count);
            var trial = catalog.GetLineage("trial");
            Assert.Equal("trial-vigour", trial.StartingBlessingId);
            Assert.Equal(15, trial.PoolIds.Count);
            Assert.Equal(18, catalog.Boons.Count);       // the 15 in the pool, the starting Blessing, the two skeletons

            var frost = catalog.GetBoon("trial-frost");
            Assert.True(frost.IsEnchant);
            Assert.Equal("trial", frost.LineageId);
            Assert.Equal(ItemSlots.Crown, frost.Requires.Slot);
            Assert.Null(frost.Requires.Kind);
            Assert.Equal(new[] { "crown-element" }, frost.ExclusiveGroups);
            Assert.Equal(2, frost.Effects.Count);
            Assert.Equal(BoonEffectTypes.AddElement, frost.Effects[0].Type);
            Assert.Equal("frost", frost.Effects[0].Element);
            Assert.True(frost.Effects[0].TargetIsSlot);
            Assert.Equal("trial-chill", frost.Effects[1].Id);
            Assert.True(frost.SharesExclusiveGroupWith(catalog.GetBoon("trial-flame")));
            Assert.False(frost.SharesExclusiveGroupWith(catalog.GetBoon("trial-zap")));

            var bowstring = catalog.GetBoon("trial-bowstring");
            Assert.Equal("bow", bowstring.Requires.Kind);
            Assert.Equal(1, bowstring.Effects[0].Amount);
            Assert.True(catalog.GetBoon("trial-trade").Stackable);
            Assert.False(catalog.GetBoon("trial-vigour").Stackable);
            Assert.Null(catalog.GetBoon("trial-vigour").Requires);
            Assert.Throws<System.ArgumentException>(() => catalog.GetBoon("nope"));
            Assert.Throws<System.ArgumentException>(() => catalog.GetLineage("nope"));
        }

        [Fact]
        public void Boon_SkeletonFields_ParseAndLink()
        {
            var catalog = CombatFixtures.Catalog();
            var lob = catalog.GetBoon("trial-lob");
            Assert.Equal("arc", lob.Effects[0].Value);
            Assert.Null(lob.Effects[0].Amount);
            Assert.Equal("false", lob.Effects[1].Value);
            Assert.Equal(1, catalog.GetBoon("trial-double").Effects[0].Amount);
            Assert.Equal(AbilityFields.Hits, catalog.GetBoon("trial-double").Effects[0].Field);
        }

        [Fact]
        public void Lineage_UnrecognisedFolder_StillFailsClosed()
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("gods/zeus.json", "{}"));
            Assert.Contains("gods/zeus.json: unrecognised content file", ContentFixtures.ErrorsOf(files));
        }

        // ---- single-file boon rules (BoonDef.FromJson) ----------------------------------------------

        private static MapLoadException BoonThrows(string json) => Assert.Throws<MapLoadException>(() => BoonDef.FromJson(json));

        [Fact]
        public void Boon_UnknownEffectType_FailsClosed()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""heal"", ""amount"": 2 } ] }");
            Assert.Contains("unknown effect type 'heal'", e.Message);
        }

        [Fact]
        public void Boon_FieldNotOfItsType_IsAnError()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 2, ""element"": ""fire"" } ] }");
            Assert.Contains("field 'element' that does not belong to it", e.Message);
            Assert.Contains("amount must not be 0", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 0 } ] }").Message);
            Assert.Contains("is not a stat", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""stat"", ""key"": ""luck"", ""amount"": 1 } ] }").Message);
            Assert.Contains("is not an ability field", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""move"", ""field"": ""colour"", ""amount"": 1 } ] }").Message);
            Assert.Contains("takes an 'amount', not a 'value'", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""move"", ""field"": ""range"", ""value"": ""2"" } ] }").Message);
            Assert.Contains("takes a 'value', not an 'amount'", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""trajectory"", ""amount"": 1 } ] }").Message);
            Assert.Contains("is not a trajectory", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""weapon"", ""field"": ""trajectory"", ""value"": ""lob"" } ] }").Message);
            Assert.Contains("effects must not be empty", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [] }").Message);
            Assert.Contains("unknown kind 'curse'", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""curse"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 1 } ] }").Message);
        }

        [Fact]
        public void Boon_BlessingWithRequires_IsAnError()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 2 } ] }");
            Assert.Contains("a blessing must not declare 'requires'", e.Message);
            Assert.Contains("must declare 'requires'", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""fire"" } ] }").Message);
            Assert.Contains("a blessing may only have", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""fire"" } ] }").Message);
            Assert.Contains("must target an innate ability id, not the slot", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""boots"", ""field"": ""range"", ""amount"": 1 } ] }").Message);
        }

        [Fact]
        public void Boon_SigilWithoutGrant_IsAnError()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""sigil"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""fire"" } ] }");
            Assert.Contains("a sigil may only have grantAbility effects", e.Message);
            Assert.Contains("is not the sigil's slot", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""sigil"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""boots"", ""ability"": ""hop"" } ] }").Message);
        }

        [Fact]
        public void Boon_EnchantTargetNotItsSlot_IsAnError()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""fire"" } ] }");
            Assert.Contains("target 'crown' is not the enchant's slot 'weapon'", e.Message);
            Assert.Contains("must target the enchant's slot", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""addTag"", ""target"": ""bow"", ""tag"": ""x"" } ] }").Message);
            Assert.Contains("an enchant may only have", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 1 } ] }").Message);
            Assert.Contains("is not a slot", BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""hat"" }, ""effects"": [ { ""type"": ""addTag"", ""target"": ""hat"", ""tag"": ""x"" } ] }").Message);
        }

        [Fact]
        public void Boon_StackableWithModifier_IsAnError()
        {
            var e = BoonThrows(@"{ ""version"": 1, ""id"": ""x"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""stackable"": true, ""effects"": [ { ""type"": ""modifier"", ""id"": ""ward"" } ] }");
            Assert.Contains("'stackable' is only legal when every effect is stat or abilityOverride", e.Message);
        }

        // ---- cross-file boon rules (the catalogue) -----------------------------------------------------

        private static string BoonErrors(string file, string json)
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile(file, json));
            return ContentFixtures.ErrorsOf(files);
        }

        [Fact]
        public void Boon_GrantedSpellOnWeapon_IsAnError()
        {
            string errors = BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""sigil"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""weapon"", ""ability"": ""zap"" } ] }");
            Assert.Contains("boons/bad.json: boon 'bad' effects[0]: a sigil on the weapon may only grant 'weapon' attacks, but 'zap' is 'spell'", errors);
        }

        [Fact]
        public void Boon_UnknownLineage_IsAnError()
        {
            string errors = BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""blessing"", ""lineage"": ""atlantean"", ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 1 } ] }");
            Assert.Contains("boons/bad.json: boon 'bad' belongs to unknown lineage 'atlantean'", errors);
        }

        [Fact]
        public void Boon_UnknownModifierAbilityOrElement_IsAnError()
        {
            Assert.Contains("boon 'bad' effects[0]: unknown modifier 'phantom'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""modifier"", ""id"": ""phantom"" } ] }"));
            Assert.Contains("boon 'bad' effects[0]: unknown ability 'fly'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""sigil"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""boots"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""boots"", ""ability"": ""fly"" } ] }"));
            Assert.Contains("boon 'bad' effects[0]: undeclared element 'acid'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""addElement"", ""target"": ""crown"", ""element"": ""acid"" } ] }"));
            Assert.Contains("grants 'move', which is already innate", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""sigil"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""boots"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""boots"", ""ability"": ""move"" } ] }"));
            Assert.Contains("stat 'power.psychic' uses undeclared damage type 'psychic'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""stat"", ""key"": ""power.psychic"", ""amount"": 1 } ] }"));
            Assert.Contains("requires a weapon of kind 'wand', which no item is", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"", ""kind"": ""wand"" }, ""effects"": [ { ""type"": ""addTag"", ""target"": ""weapon"", ""tag"": ""x"" } ] }"));
        }

        [Fact]
        public void Boon_BlessingOverride_MustTargetAnInnateAbility()
        {
            Assert.Contains("a blessing may only override an innate ability, and 'bow' is not one", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""bow"", ""field"": ""range"", ""amount"": 1 } ] }"));
            Assert.Contains("field 'apex' does not apply to 'move'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""blessing"", ""lineage"": ""trial"", ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""move"", ""field"": ""apex"", ""amount"": 1 } ] }"));
        }

        [Fact]
        public void Boon_EnchantAbilityTarget_MustBeGrantedBySlotOrSigil()
        {
            // zap is granted only by the trial-zap sigil on the crown, so an enchant on the crown may name it...
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("boons/ok.json", @"{ ""version"": 1, ""id"": ""ok"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""crown"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""zap"", ""field"": ""range"", ""amount"": 1 } ] }"));
            Assert.NotNull(ContentCatalog.Load(files).GetBoon("ok"));

            // ...but an enchant on the weapon may not, and nothing on the boots has a damage.
            Assert.Contains("'zap' is not granted by any weapon item or by a sigil of lineage 'trial' on that slot", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""zap"", ""field"": ""range"", ""amount"": 1 } ] }"));
            Assert.Contains("field 'damage' applies to no ability the boots can carry", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""boots"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""boots"", ""field"": ""damage"", ""amount"": 1 } ] }"));
            Assert.Contains("field 'climb' does not apply to 'strike'", BoonErrors("boons/bad.json", @"{ ""version"": 1, ""id"": ""bad"", ""kind"": ""enchant"", ""lineage"": ""trial"", ""requires"": { ""slot"": ""weapon"" }, ""effects"": [ { ""type"": ""abilityOverride"", ""target"": ""strike"", ""field"": ""climb"", ""amount"": 1 } ] }"));
        }

        // ---- lineage rules -------------------------------------------------------------------------

        private static string LineageErrors(string pool, string starting = "trial-vigour")
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "lineages/trial.json"
                ? new ContentFile(f.Path, @"{ ""version"": 1, ""id"": ""trial"", ""startingBlessing"": """ + starting + @""", ""pool"": [ " + pool + " ] }")
                : f).ToList();
            return ContentFixtures.ErrorsOf(files);
        }

        private const string FullPool = @"""trial-might"", ""trial-ward"", ""trial-frostproof"", ""trial-stride"", ""trial-trade"", ""trial-bowstring"", ""trial-cheap"", ""trial-heavy"", ""trial-frost"", ""trial-flame"", ""trial-blessed"", ""trial-plating"", ""trial-zap"", ""trial-hop"", ""trial-jab""";

        [Fact]
        public void Lineage_PoolMissingAKind_IsAnError()
        {
            string noSigil = @"""trial-might"", ""trial-bowstring"", ""trial-cheap"", ""trial-frost"", ""trial-plating""";
            Assert.Contains("lineages/trial.json: lineage 'trial' pool has no sigil; every kind must be offerable", LineageErrors(noSigil));
        }

        [Fact]
        public void Lineage_PoolLeavesAnItemUncovered_IsAnError()
        {
            // Without the boots sigil nothing in the pool can attach to bare-boots.
            string noBoots = @"""trial-might"", ""trial-bowstring"", ""trial-cheap"", ""trial-frost"", ""trial-plating"", ""trial-zap""";
            Assert.Contains("lineages/trial.json: lineage 'trial' pool has no enchant or sigil applicable to item 'bare-boots' (boots, bare)", LineageErrors(noBoots));
            // The kind filter counts: a bow-only enchant does not cover the club.
            string bowOnly = @"""trial-might"", ""trial-bowstring"", ""trial-frost"", ""trial-plating"", ""trial-zap"", ""trial-hop""";
            Assert.Contains("applicable to item 'brute-club' (weapon, club)", LineageErrors(bowOnly));
        }

        [Fact]
        public void Lineage_StartingBlessingOfOtherLineage_IsAnError()
        {
            var files = CombatFixtures.Files();
            files.Add(new ContentFile("boons/other-vigour.json", @"{ ""version"": 1, ""id"": ""other-vigour"", ""kind"": ""blessing"", ""lineage"": ""other"", ""effects"": [ { ""type"": ""stat"", ""key"": ""hp"", ""amount"": 1 } ] }"));
            files.Add(new ContentFile("boons/other-hop.json", @"{ ""version"": 1, ""id"": ""other-hop"", ""kind"": ""sigil"", ""lineage"": ""other"", ""requires"": { ""slot"": ""boots"" }, ""effects"": [ { ""type"": ""grantAbility"", ""target"": ""boots"", ""ability"": ""hop"" } ] }"));
            files.Add(new ContentFile("lineages/other.json", @"{ ""version"": 1, ""id"": ""other"", ""startingBlessing"": ""trial-vigour"", ""pool"": [ ""other-vigour"", ""other-hop"", ""trial-frost"" ] }"));
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("lineages/other.json: lineage 'other' starting Blessing 'trial-vigour' belongs to lineage 'trial'", errors);
            Assert.Contains("lineage 'other' pool names 'trial-frost', which belongs to lineage 'trial'", errors);
            Assert.Contains("lineage 'other' pool has no enchant", errors);
        }

        [Fact]
        public void Lineage_StartingBlessingMissingOrNotABlessing_IsAnError()
        {
            Assert.Contains("starting Blessing 'trial-nothing' does not exist", LineageErrors(FullPool, "trial-nothing"));
            Assert.Contains("starting Blessing 'trial-zap' is a sigil, not a blessing", LineageErrors(FullPool, "trial-zap"));
            Assert.Contains("pool names unknown boon 'trial-nothing'", LineageErrors(FullPool + @", ""trial-nothing"""));
            Assert.Throws<MapLoadException>(() => LineageDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""startingBlessing"": ""a"", ""pool"": [ ""b"", ""b"" ] }"));
        }

        [Fact]
        public void Boon_IsApplicableTo_ReadsSlotKindTargetsAndGrants()
        {
            var catalog = CombatFixtures.Catalog();
            var bowstring = catalog.GetBoon("trial-bowstring");
            Assert.True(bowstring.IsApplicableTo("weapon", "bow", new[] { "bow" }));
            Assert.False(bowstring.IsApplicableTo("weapon", "club", new[] { "jab", "strike" }));
            Assert.False(bowstring.IsApplicableTo("crown", "bow", new[] { "bow" }));

            var cheap = catalog.GetBoon("trial-cheap");           // targets 'strike' by id
            Assert.True(cheap.IsApplicableTo("weapon", "club", new[] { "jab", "strike" }));
            Assert.False(cheap.IsApplicableTo("weapon", "bow", new[] { "bow" }));

            var jab = catalog.GetBoon("trial-jab");               // grants 'jab'
            Assert.True(jab.IsApplicableTo("weapon", "bow", new[] { "bow" }));
            Assert.False(jab.IsApplicableTo("weapon", "club", new[] { "jab", "strike" }));

            Assert.True(catalog.GetBoon("trial-might").IsApplicableTo("armour", "vest", new string[0]));
        }
    }
}
