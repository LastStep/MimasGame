#nullable disable

using System;
using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;
using Mimas.Core.Session;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>The draft offer as a pure seeded function (design: #draft; spec D part 1 §7).</summary>
    public class DraftTests
    {
        private static List<string> Offer(ContentCatalog catalog, PlayerBuild build, int count, uint seed)
        {
            var into = new List<string>();
            Draft.Offer(catalog, build, count, new Rng(seed), into);
            return into;
        }

        private static string KindOf(ContentCatalog catalog, string boonId) => catalog.GetBoon(boonId).Kind;

        [Fact]
        public void Draft_OffersOneOfEachKindWhenPossible()
        {
            var catalog = CombatFixtures.Catalog();
            var build = CombatFixtures.ArcherWith("trial-vigour");
            for (uint seed = 1; seed <= 25; seed++)
            {
                var offer = Offer(catalog, build, 3, seed);
                Assert.Equal(3, offer.Count);
                Assert.Equal(new[] { BoonKinds.Blessing, BoonKinds.Enchant, BoonKinds.Sigil }, offer.Select(id => KindOf(catalog, id)));
                Assert.Equal(3, offer.Distinct().Count());
            }
        }

        [Fact]
        public void Draft_KindOrderFirst_ThenTheRestOfTheShuffle()
        {
            var catalog = CombatFixtures.Catalog();
            var offer = Offer(catalog, CombatFixtures.ArcherWith("trial-vigour"), 6, 3);
            Assert.Equal(6, offer.Count);
            Assert.Equal(new[] { BoonKinds.Blessing, BoonKinds.Enchant, BoonKinds.Sigil }, offer.Take(3).Select(id => KindOf(catalog, id)));
            Assert.Equal(6, offer.Distinct().Count());

            // Count 2 stops in kind order: a Blessing and an Enchant, never a Sigil.
            var two = Offer(catalog, CombatFixtures.ArcherWith("trial-vigour"), 2, 3);
            Assert.Equal(new[] { BoonKinds.Blessing, BoonKinds.Enchant }, two.Select(id => KindOf(catalog, id)));
            Assert.Empty(Offer(catalog, CombatFixtures.ArcherWith("trial-vigour"), 0, 3));
        }

        [Fact]
        public void Draft_ExcludesEnchantTheGearCannotUse()
        {
            var catalog = CombatFixtures.Catalog();
            var brute = CombatFixtures.BruteWith("trial-vigour");
            Assert.False(Draft.IsApplicable(catalog, brute, catalog.GetBoon("trial-bowstring")));     // needs a bow
            Assert.False(Draft.IsApplicable(catalog, brute, catalog.GetBoon("trial-heavy")));
            Assert.True(Draft.IsApplicable(catalog, brute, catalog.GetBoon("trial-cheap")));          // targets the club's strike
            Assert.False(Draft.IsApplicable(catalog, CombatFixtures.ArcherWith(), catalog.GetBoon("trial-cheap")));
            Assert.True(Draft.IsApplicable(catalog, brute, catalog.GetBoon("trial-might")));          // a Blessing always

            for (uint seed = 1; seed <= 40; seed++)
            {
                var offer = Offer(catalog, brute, 3, seed);
                Assert.DoesNotContain("trial-bowstring", offer);
                Assert.DoesNotContain("trial-heavy", offer);
            }
        }

        [Fact]
        public void Draft_ExcludesOwnedUnlessStackable()
        {
            var catalog = CombatFixtures.Catalog();
            var build = CombatFixtures.ArcherWith("trial-vigour", "trial-might", "trial-trade");
            Assert.False(Draft.IsEligible(catalog, build, catalog.GetBoon("trial-might")));
            Assert.True(Draft.IsEligible(catalog, build, catalog.GetBoon("trial-trade")));           // stackable
            Assert.True(Draft.IsEligible(catalog, build, catalog.GetBoon("trial-ward")));
            for (uint seed = 1; seed <= 40; seed++) Assert.DoesNotContain("trial-might", Offer(catalog, build, 6, seed));
        }

        [Fact]
        public void Draft_ExcludesExclusiveGroupClash()
        {
            var catalog = CombatFixtures.Catalog();
            var frosted = CombatFixtures.ArcherWith("trial-vigour", "trial-frost");
            Assert.False(Draft.IsEligible(catalog, frosted, catalog.GetBoon("trial-flame")));         // crown-element is taken
            Assert.True(Draft.IsEligible(catalog, CombatFixtures.ArcherWith("trial-vigour"), catalog.GetBoon("trial-flame")));
            for (uint seed = 1; seed <= 40; seed++) Assert.DoesNotContain("trial-flame", Offer(catalog, frosted, 6, seed));
        }

        [Fact]
        public void Draft_ExcludesGrantOfAnAbilityAlreadyHeld()
        {
            var catalog = CombatFixtures.Catalog();
            Assert.False(Draft.IsApplicable(catalog, CombatFixtures.BruteWith(), catalog.GetBoon("trial-jab")));   // the club already jabs
            Assert.True(Draft.IsApplicable(catalog, CombatFixtures.ArcherWith(), catalog.GetBoon("trial-jab")));
            // Owned grants count as held: a second Sigil granting the same spell is not applicable.
            var zapped = CombatFixtures.ArcherWith("trial-zap");
            var again = new BoonDef("zap-again", "Zap Again", BoonKinds.Sigil, "trial", new BoonRequirement("crown"),
                new List<BoonEffect> { new BoonEffect(BoonEffectTypes.GrantAbility, target: "crown", ability: "zap") });
            Assert.False(Draft.IsApplicable(catalog, zapped, again));
            Assert.True(Draft.IsApplicable(catalog, CombatFixtures.ArcherWith(), again));
        }

        [Fact]
        public void Draft_EnchantOnASigilAbility_NeedsTheSigilFirst()
        {
            var catalog = CombatFixtures.Catalog();
            var longerZap = new BoonDef("long-zap", "Long Zap", BoonKinds.Enchant, "trial", new BoonRequirement("crown"),
                new List<BoonEffect> { new BoonEffect(BoonEffectTypes.AbilityOverride, target: "zap", field: AbilityFields.Range, amount: 1) });
            Assert.False(Draft.IsApplicable(catalog, CombatFixtures.ArcherWith(), longerZap));
            Assert.True(Draft.IsApplicable(catalog, CombatFixtures.ArcherWith("trial-zap"), longerZap));
        }

        [Fact]
        public void Draft_SameSeed_SameOffers()
        {
            var catalog = CombatFixtures.Catalog();
            var build = CombatFixtures.ArcherWith("trial-vigour");
            Assert.Equal(Offer(catalog, build, 3, 77), Offer(catalog, build, 3, 77));
            Assert.Equal(Offer(catalog, build, 3, 77), Offer(CombatFixtures.Catalog(), build, 3, 77));
            // Different seeds do differ somewhere in 30 tries, or the shuffle is not shuffling.
            var seen = new HashSet<string>();
            for (uint seed = 1; seed <= 30; seed++) seen.Add(string.Join(",", Offer(catalog, build, 3, seed)));
            Assert.True(seen.Count > 3, "thirty seeds gave at most three different offers");
        }

        [Fact]
        public void Draft_FewerEligibleThanCount_OffersFewer()
        {
            var catalog = CombatFixtures.Catalog();
            // Own every pool boon that is not stackable and clash out the rest: only Trade (stackable) remains.
            var lineage = catalog.GetLineage("trial");
            var everything = CombatFixtures.ArcherWith(lineage.PoolIds.Where(id => catalog.GetBoon(id).Kind != BoonKinds.Sigil || id != "trial-jab").ToArray());
            var offer = Offer(catalog, everything, 3, 5);
            Assert.True(offer.Count < 3);
            Assert.All(offer, id => Assert.True(catalog.GetBoon(id).Stackable || id == "trial-jab"));

            var all = CombatFixtures.ArcherWith(lineage.PoolIds.ToArray());
            var only = Offer(catalog, all, 3, 5);
            Assert.Equal(new[] { "trial-trade" }, only);
        }

        [Fact]
        public void Draft_StartingBlessing_IsOwned()
        {
            var catalog = CombatFixtures.Catalog();
            var fresh = CombatFixtures.ArcherWith();                                  // the lineage's Blessing, not yet in BoonIds
            Assert.False(Draft.IsEligible(catalog, fresh, catalog.GetBoon("trial-vigour")));
            Assert.True(Draft.IsApplicable(catalog, fresh, catalog.GetBoon("trial-vigour")));
            for (uint seed = 1; seed <= 20; seed++) Assert.DoesNotContain("trial-vigour", Offer(catalog, fresh, 6, seed));
        }

        [Fact]
        public void Draft_SkeletonBoons_AreNeverOffered()
        {
            var catalog = CombatFixtures.Catalog();
            for (uint seed = 1; seed <= 40; seed++)
            {
                var offer = Offer(catalog, CombatFixtures.ArcherWith("trial-vigour"), 6, seed);
                Assert.DoesNotContain("trial-double", offer);
                Assert.DoesNotContain("trial-lob", offer);
            }
        }

        [Fact]
        public void Draft_BuildWithoutLineage_Throws()
        {
            var catalog = CombatFixtures.Catalog();
            var into = new List<string>();
            Assert.Throws<ArgumentException>(() => Draft.Offer(catalog, new PlayerBuild(CombatFixtures.Archer), 3, new Rng(1), into));
            Assert.Throws<ArgumentOutOfRangeException>(() => Draft.Offer(catalog, CombatFixtures.ArcherWith(), -1, new Rng(1), into));
        }

        [Fact]
        public void Draft_UnitAbilities_AreInnateItemsThenGrants()
        {
            var catalog = CombatFixtures.Catalog();
            var into = new List<string>();
            Draft.UnitAbilities(catalog, CombatFixtures.ArcherWith("trial-zap", "trial-jab"), into);
            Assert.Equal(new[] { "move", "bow", "zap", "jab" }, into);
        }
    }
}
