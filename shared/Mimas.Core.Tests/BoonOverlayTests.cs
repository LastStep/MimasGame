#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// A unit is gear + lineage + boons through one overlay (spec D part 1 §6.2, §6.3; ADR-034): stats
    /// summed and floored, modifiers attached with their boon, overrides resolved per ability through
    /// <c>MatchState.ResolveAbility</c>, elements and tags added, Sigil abilities granted after the items'.
    /// </summary>
    public class BoonOverlayTests
    {
        private static readonly Hex ArcherAt = new Hex(-1, 0);
        private static readonly Hex BruteAt = new Hex(2, 0);

        private static AttackDef Attack(MatchState state, int unitId, string abilityId)
        {
            AbilityDef def;
            Assert.True(state.ResolveAbility(state.Units.Get(unitId), abilityId, out def));
            return Assert.IsType<AttackDef>(def);
        }

        private static MovementDef Movement(MatchState state, int unitId, string abilityId)
        {
            AbilityDef def;
            Assert.True(state.ResolveAbility(state.Units.Get(unitId), abilityId, out def));
            return Assert.IsType<MovementDef>(def);
        }

        // ---- stats ------------------------------------------------------------------------------------

        [Fact]
        public void Unit_WithBoons_StatsAreBaseItemsPlusBoons()
        {
            var catalog = CombatFixtures.Catalog();
            var hero = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-might"), Hex.Zero);

            Assert.Equal(12, hero.PublicStats.Hp);                    // base 2 + vest 10: what gear explains
            Assert.Equal(16, hero.Stats.Hp);                          // + Vigour 4
            Assert.Equal(16, hero.MaxHp);
            Assert.Equal(16, hero.Hp);
            Assert.Equal(2, hero.PublicStats.Get("power.ranged"));
            Assert.Equal(4, hero.Stats.Get("power.ranged"));          // + Might 2
            Assert.Equal("trial", hero.LineageId);
            Assert.Equal(new[] { "trial-vigour", "trial-might" }, hero.BoonIds);
            Assert.True(hero.HasBoon("trial-might"));

            var contributions = hero.BoonStatContributions("hp");
            Assert.Single(contributions);
            Assert.Equal("trial-vigour", contributions[0].BoonId);
            Assert.Equal(4, contributions[0].Amount);
            Assert.Empty(hero.BoonStatContributions("defense.melee"));
        }

        [Fact]
        public void Unit_NegativeStat_IsFlooredFromRules()
        {
            var catalog = CombatFixtures.Catalog();
            // Trade: power.melee +2, hp -30, ap -5 on a hero with hp 12 and ap 3; the fixture floors are hp 1, ap 1.
            var hero = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-trade"), Hex.Zero);
            Assert.Equal(1, hero.Stats.Hp);
            Assert.Equal(1, hero.Stats.Ap);
            Assert.Equal(1, hero.ApPerTurn);
            Assert.Equal(2, hero.Stats.Get("power.melee"));           // a key the hero did not have is added
            Assert.Equal(12, hero.PublicStats.Hp);                    // the public block is untouched

            // Stackable: twice is legal and the floor still holds.
            var twice = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-trade", "trial-trade"), Hex.Zero);
            Assert.Equal(1, twice.Stats.Hp);
            Assert.Equal(4, twice.Stats.Get("power.melee"));
            Assert.Equal(2, twice.BoonStatContributions("power.melee").Count);

            // Floors apply to the total, never to a contribution: Vigour +4 then Trade -30 is still 1, and Vigour +4 twice over Trade is 1 too.
            var mixed = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-vigour", "trial-trade"), Hex.Zero);
            Assert.Equal(1, mixed.Stats.Hp);
        }

        [Fact]
        public void Unit_NoBoons_IsExactlyTheGearUnit()
        {
            var catalog = CombatFixtures.Catalog();
            var bare = ContentFixtures.HeroFrom(catalog, CombatFixtures.Archer, Hex.Zero);
            var built = ContentFixtures.HeroFrom(catalog, new PlayerBuild(CombatFixtures.Archer), Hex.Zero);
            Assert.Equal(bare.Stats.Entries, built.Stats.Entries);
            Assert.Equal(bare.AbilityIds, built.AbilityIds);
            Assert.Null(built.LineageId);
            Assert.Empty(built.BoonIds);
            Assert.True(built.Overlay.IsEmpty);
            Assert.Same(bare.Stats, bare.PublicStats);
        }

        // ---- overrides through the resolver -----------------------------------------------------------

        [Fact]
        public void Unit_CostOverride_NeverBelowMinCost()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith("trial-cheap"), ArcherAt, BruteAt);
            Assert.Equal(1, Attack(state, 1, "strike").Cost);          // 2 - 1

            // A hand-built enchant that would take strike to -3: the floor (rules.boons.minCost 1) holds.
            var greedy = new BoonDef("greedy", "Greedy", BoonKinds.Enchant, "trial", new BoonRequirement("weapon"),
                new List<BoonEffect> { new BoonEffect(BoonEffectTypes.AbilityOverride, target: "strike", field: AbilityFields.Cost, amount: -5) });
            var items = ItemSlots.All.Select(s => catalog.GetItemForSlot(s, CombatFixtures.Brute.IdForSlot(s))).ToList();
            var brute = new Unit(1, 1, BruteAt, catalog.Rules, items, "trial", new[] { greedy });
            var overlay = brute.Overlay;
            Assert.Single(overlay.Overrides);
            Assert.Equal(-5, overlay.Overrides[0].Amount);

            // Resolve it through a state that holds that unit: the state's resolver is the only place the floor lives.
            state.Units.Get(1).RemoveModifier("nothing");
            var floored = state.Units.Get(1);
            Assert.Equal(1, Attack(state, 1, "strike").Cost);
        }

        [Fact]
        public void Unit_SlotOverride_AppliesToEveryAbilityOfTheItemIncludingSigils()
        {
            var catalog = CombatFixtures.Catalog();
            // Bowstring (+1 range on the weapon) drafted before the Jab sigil: the Sigil's jab still gets it.
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-bowstring", "trial-jab"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Assert.Equal(4, Attack(state, 0, "bow").Range);            // 3 + 1
            Assert.Equal(2, Attack(state, 0, "jab").Range);            // 1 + 1, granted by the sigil on the same weapon
            Assert.Equal(1, Movement(state, 0, "move").Range);         // the walk is not on the weapon

            var overlay = state.Units.Get(0).Overlay;
            Assert.Equal(new[] { "bow", "jab" }, overlay.Overrides.Select(o => o.AbilityId));
            Assert.All(overlay.Overrides, o => Assert.Equal("trial-bowstring", o.BoonId));
        }

        [Fact]
        public void Unit_AbilityOverride_AppliesToThatAbilityOnly()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith("trial-cheap"), ArcherAt, BruteAt);
            Assert.Equal(1, Attack(state, 1, "strike").Cost);
            Assert.Equal(1, Attack(state, 1, "jab").Cost);             // unchanged: it was 1 already, and the override names strike
            Assert.Equal(2, catalog.GetAttack("strike").Cost);         // the definition is never touched
        }

        [Fact]
        public void Unit_FieldNotApplicable_IsIgnoredForThatAbility()
        {
            var catalog = CombatFixtures.Catalog();
            // Heavy Arrows: damage +2 and apex +1 on the weapon. The archer's bow is direct, so the apex is ignored for it.
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-heavy"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            var bow = Attack(state, 0, "bow");
            Assert.Equal(0, bow.Apex);
            Assert.Equal(Trajectories.Direct, bow.Trajectory);
            Assert.Equal(5, bow.Damage);                                // damage stays on the def: it is a breakdown line (spec §6.5 (d))

            // On the hunter bow the same boon reaches an arc, and the apex applies.
            var hunter = new Loadout("hunter-bow", "bare-crown", "bare-boots", "archer-vest");
            var lobbed = CombatFixtures.StartedWith(catalog, ContentFixtures.BuildFor(hunter, "trial", "trial-heavy"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Assert.Equal(3, Attack(lobbed, 0, "lob").Apex);            // 2 + 1
            Assert.Equal(2, catalog.GetAttack("lob").Apex);
        }

        [Fact]
        public void Unit_AddElement_EveryCrownSpellCarriesIt_AndInnateStays()
        {
            var catalog = CombatFixtures.Catalog();
            // Frost on the crown, drafted before the Zap sigil: zap (innate lightning) carries both; the bow carries nothing.
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-frost", "trial-zap"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Assert.Equal(new[] { "frost", "lightning" }, Attack(state, 0, "zap").Elements);
            Assert.Empty(Attack(state, 0, "bow").Elements);
            Assert.Equal(new[] { "lightning" }, catalog.GetAttack("zap").Elements);

            // A tag the same way.
            var tagged = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-blessed"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Assert.Equal(new[] { "arrow", "blessed" }, Attack(tagged, 0, "bow").Tags);
            Assert.True(Attack(tagged, 0, "bow").HasTag("blessed"));
        }

        [Fact]
        public void Unit_Grant_OrderIsInnateItemsThenBoons()
        {
            var catalog = CombatFixtures.Catalog();
            var hero = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-zap", "trial-jab"), Hex.Zero);
            Assert.Equal(new[] { "move", "bow", "zap", "jab" }, hero.AbilityIds);
            Assert.Equal("bare-crown", hero.AbilitySourceOf("zap"));
            Assert.Equal("archer-bow", hero.AbilitySourceOf("jab"));
            Assert.Equal("trial-zap", hero.BoonOfAbility("zap"));
            Assert.Equal("trial-jab", hero.BoonOfAbility("jab"));
            Assert.Null(hero.BoonOfAbility("bow"));
            Assert.Null(hero.BoonOfAbility("move"));
            Assert.Equal(2, hero.Overlay.Grants.Count);
        }

        [Fact]
        public void Unit_GrantSourceItem_IsPublicAndBoonIsNot()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-zap"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            UnitView theirs = state.ViewFor(1).FindUnit(0);
            KnownEntry zap = theirs.Abilities[2];
            Assert.Null(zap.Id);
            Assert.Equal("bare-crown", zap.SourceItemId);              // "an extra ? under the item is a fair tell"
            Assert.Equal(3, theirs.Abilities.Count);
        }

        [Fact]
        public void Unit_Modifier_RemembersItsBoon_FirstGrantOwnsIt()
        {
            var catalog = CombatFixtures.Catalog();
            var hero = ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-ward", "trial-plating"), Hex.Zero);
            Assert.Equal(new[] { "ward", "trial-plate" }, hero.ModifierIds);
            Assert.Equal("trial-ward", hero.BoonOfModifier("ward"));
            Assert.Equal("trial-plating", hero.BoonOfModifier("trial-plate"));
            Assert.Null(hero.BoonOfModifier("nothing"));

            hero.AddModifier("forest-cover");
            Assert.Null(hero.BoonOfModifier("forest-cover"));
            Assert.True(hero.RemoveModifier("ward"));
            Assert.Null(hero.BoonOfModifier("ward"));
            Assert.Equal("trial-plating", hero.BoonOfModifier("trial-plate"));

            // Two boons attaching the same modifier: one entry, the first owns it.
            var wardTwice = new BoonDef("ward-again", "Ward Again", BoonKinds.Blessing, "trial", null,
                new List<BoonEffect> { new BoonEffect(BoonEffectTypes.Modifier, id: "ward") });
            var items = ItemSlots.All.Select(s => catalog.GetItemForSlot(s, CombatFixtures.Archer.IdForSlot(s))).ToList();
            var twice = new Unit(0, 0, Hex.Zero, catalog.Rules, items, "trial", new[] { catalog.GetBoon("trial-ward"), wardTwice });
            Assert.Equal(new[] { "ward" }, twice.ModifierIds);
            Assert.Equal("trial-ward", twice.BoonOfModifier("ward"));
        }

        [Fact]
        public void Unit_MoveOverride_ExtendsWalkRange()
        {
            var catalog = CombatFixtures.Catalog();
            var plain = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith(), CombatFixtures.BruteWith(), new Hex(0, 0), new Hex(3, 0));
            var strider = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-stride"), CombatFixtures.BruteWith(), new Hex(0, 0), new Hex(3, 0));

            Assert.Equal(1, Movement(plain, 0, "move").Range);
            Assert.Equal(2, Movement(strider, 0, "move").Range);
            Assert.True(strider.MoveOptions(0, "move").Count > plain.MoveOptions(0, "move").Count);
            Assert.True(strider.Validate(new MoveCommand(0, 0, "move", new Hex(2, 0))).Ok);
            Assert.Equal(MoveRejectReason.OutOfRange, plain.Validate(new MoveCommand(0, 0, "move", new Hex(2, 0))).MoveReason);
            Assert.Equal(1, catalog.GetMovement("move").Range);
        }

        [Fact]
        public void ResolveAbility_UnknownId_IsFalse_AndUnownedAbilityIsRaw()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-bowstring"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            AbilityDef def;
            Assert.False(state.ResolveAbility(state.Units.Get(0), "fly", out def));
            Assert.Null(def);
            // The brute does not own the bowstring, so its lookups are the catalogue's definitions unchanged.
            Assert.True(state.ResolveAbility(state.Units.Get(1), "bow", out def));
            Assert.Same(catalog.GetAttack("bow"), def);
            Assert.Equal(CommandRejectReason.UnknownAbility, state.Validate(new AttackCommand(0, 0, "fly", BruteAt)).Reason);
        }

        [Fact]
        public void ResolveAbilityKnownTo_AppliesOnlyRevealedBoons()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-bowstring"), CombatFixtures.BruteWith(), ArcherAt, BruteAt);
            Unit archer = state.Units.Get(0);
            AbilityDef mine, theirs;
            Assert.True(state.ResolveAbilityKnownTo(0, archer, "bow", out mine));
            Assert.True(state.ResolveAbilityKnownTo(1, archer, "bow", out theirs));
            Assert.Equal(4, ((AttackDef)mine).Range);                  // the owner sees the real number
            Assert.Equal(3, ((AttackDef)theirs).Range);                // the opponent still sees the base bow
            Assert.Throws<ArgumentOutOfRangeException>(() => state.ResolveAbilityKnownTo(2, archer, "bow", out theirs));
        }

        /// <summary>
        /// The claim ADR-034 rests on: nothing in <c>MatchState</c> reads a unit's ability except the
        /// resolver. A source scan, because a lookup that slips in later would silently ignore every
        /// Enchant. (Spec §6.3, §9 <c>NoRawAbilityLookupsOutsideResolver</c>.)
        /// </summary>
        [Fact]
        public void ResolveAbility_IsTheOnlyAbilityLookup()
        {
            string source = RepoData.CoreSource("Match/MatchState.cs");
            string[] lines = source.Split('\n');
            int resolverAt = Array.FindIndex(lines, l => l.Contains("private bool ResolveAbilityCore("));
            Assert.True(resolverAt >= 0, "ResolveAbilityCore is missing");

            var hits = lines.Select((l, i) => (line: l, index: i))
                .Where(x => x.line.Contains("Catalog.Abilities.TryGet") || x.line.Contains("Catalog.GetMovement(") || x.line.Contains("Catalog.GetAttack(") || x.line.Contains("Abilities.Get("))
                .ToList();
            Assert.Single(hits);
            Assert.InRange(hits[0].index, resolverAt, resolverAt + 12);
        }

        // ---- skeletons (spec §6.7) ----------------------------------------------------------------------

        [Fact]
        public void Skeleton_Hits_ParsesAndFailsClosed()
        {
            var catalog = CombatFixtures.Catalog();

            // An override on hits: the unit refuses to be built.
            var viaBoon = Assert.Throws<NotSupportedException>(() => ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-double"), Hex.Zero));
            Assert.Contains("multi-hit attacks are not implemented (spec D part 1 §6.7, E5/S3)", viaBoon.Message);
            Assert.Contains("trial-double", viaBoon.Message);

            // An attack file with hits 2: it parses and links, and a match refuses to spawn a unit carrying it.
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "abilities/bow.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"""lineOfSight"": true }", @"""lineOfSight"": true, ""hits"": 2 }"))
                : f).ToList();
            var doubled = ContentCatalog.Load(files);
            Assert.Equal(2, doubled.GetAttack("bow").Hits);
            var viaFile = Assert.Throws<NotSupportedException>(() => new MatchState(doubled, CombatFixtures.Setup(), 1));
            Assert.Contains("spec D part 1 §6.7", viaFile.Message);
        }

        [Fact]
        public void Skeleton_TrajectorySwap_ParsesAndFailsClosed()
        {
            var catalog = CombatFixtures.Catalog();
            Assert.Equal("arc", catalog.GetBoon("trial-lob").Effects[0].Value);
            var e = Assert.Throws<NotSupportedException>(() => ContentFixtures.HeroFrom(catalog, CombatFixtures.ArcherWith("trial-lob"), Hex.Zero));
            Assert.Contains("trajectory swap is not implemented (spec D part 1 §6.7, E6)", e.Message);
            Assert.Contains("trial-lob", e.Message);
        }

        // ---- the build and the setup ------------------------------------------------------------------

        [Fact]
        public void PlayerBuild_IsValueEqual_AndWithBoonAppends()
        {
            var a = new PlayerBuild(CombatFixtures.Archer, "trial", new[] { "trial-vigour" });
            var b = new PlayerBuild(CombatFixtures.Archer, "trial", new[] { "trial-vigour" });
            Assert.Equal(a, b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            Assert.NotEqual(a, a.WithBoon("trial-might"));
            Assert.Equal(new[] { "trial-vigour", "trial-might" }, a.WithBoon("trial-might").BoonIds);
            Assert.Equal(new[] { "trial-vigour" }, a.BoonIds);        // immutable
            Assert.True(a.HasBoon("trial-vigour"));
            Assert.NotEqual(a, new PlayerBuild(CombatFixtures.Archer));
            Assert.Null(new PlayerBuild(CombatFixtures.Archer).LineageId);
            Assert.Throws<ArgumentException>(() => a.WithBoon(""));
            Assert.Throws<ArgumentNullException>(() => new PlayerBuild(null));
            Assert.Contains("trial-vigour", a.ToString());
        }

        [Fact]
        public void MatchSetup_LoadoutConstructor_WrapsBareBuilds_AndRevealedSeedsTheRound()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup();
            Assert.Equal(CombatFixtures.Archer, setup.BuildOf(0).Loadout);
            Assert.Equal(CombatFixtures.Archer, setup.LoadoutOf(0));
            Assert.Null(setup.BuildOf(1).LineageId);
            Assert.Empty(setup.RevealedEntries);

            setup.WithRevealed(1, 0, "bow").WithRevealed(1, 0, "bow");
            Assert.Single(setup.RevealedEntries);
            var state = CombatFixtures.Started(catalog, setup, ArcherAt, BruteAt);
            Assert.True(state.Knows(1, 0, "bow"));
            Assert.False(state.Knows(0, 1, "jab"));
            Assert.Equal("P1 knows bow on unit 0", Assert.Single(state.RevealedEntries).ToString());
            Assert.Equal("bow", state.ViewFor(1).FindUnit(0).Abilities[1].Id);     // seeded knowledge shows in the view at once

            Assert.Throws<ArgumentException>(() => new MatchState(catalog, new MatchSetup("field-3", CombatFixtures.ArcherWith("no-such-boon"), CombatFixtures.BruteWith()), 1));
            Assert.Throws<ArgumentException>(() => new MatchState(catalog, new MatchSetup("field-3", ContentFixtures.BuildFor(CombatFixtures.Archer, "atlantean"), CombatFixtures.BruteWith()), 1));
        }
    }
}
