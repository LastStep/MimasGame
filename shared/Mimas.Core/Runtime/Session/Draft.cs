using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;

namespace Mimas.Core.Session
{
    /// <summary>
    /// The draft between rounds as a pure, seeded function (design: #draft; spec D part 1 §7). What a
    /// build can be offered is decided by data alone: its lineage's pool, filtered by what the gear can
    /// use (<see cref="IsApplicable"/>), what is already owned and what excludes what
    /// (<see cref="IsEligible"/>); the order comes from the session's <see cref="Rng"/>, so a replay
    /// reproduces every offer.
    /// </summary>
    public static class Draft
    {
        /// <summary>
        /// The gear can use it (§7.2): a Blessing always; an Enchant or Sigil when the item in its slot has the
        /// required kind, every ability-id target is an ability the build's unit would have (items plus owned
        /// boons' grants), and no granted ability is one the unit already has. The same predicate the
        /// catalogue asks per item, asked of the whole build.
        /// </summary>
        public static bool IsApplicable(ContentCatalog catalog, PlayerBuild build, BoonDef boon)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (boon == null) throw new ArgumentNullException(nameof(boon));
            if (boon.Requires == null) return true;
            ItemDef item = catalog.GetItemForSlot(boon.Requires.Slot, build.Loadout.IdForSlot(boon.Requires.Slot));
            var abilities = new List<string>();
            UnitAbilities(catalog, build, abilities);
            return boon.IsApplicableTo(item.Slot, item.Kind, abilities);
        }

        /// <summary>Applicable, not owned (unless stackable), and sharing no exclusive group with an owned boon (§7.3). The lineage's starting Blessing counts as owned.</summary>
        public static bool IsEligible(ContentCatalog catalog, PlayerBuild build, BoonDef boon)
        {
            if (!IsApplicable(catalog, build, boon)) return false;
            if (IsOwned(catalog, build, boon.Id) && !boon.Stackable) return false;
            if (boon.ExclusiveGroups.Count > 0)
            {
                for (int i = 0; i < build.BoonIds.Count; i++)
                {
                    BoonDef owned;
                    if (catalog.Boons.TryGet(build.BoonIds[i], out owned) && owned.Id != boon.Id && boon.SharesExclusiveGroupWith(owned)) return false;
                }
                if (build.LineageId != null)
                {
                    BoonDef starting;
                    if (catalog.Boons.TryGet(catalog.GetLineage(build.LineageId).StartingBlessingId, out starting) && starting.Id != boon.Id && boon.SharesExclusiveGroupWith(starting)) return false;
                }
            }
            return true;
        }

        /// <summary>
        /// The offer (§7.4): the eligible ids of the lineage's pool in ordinal order, shuffled by <paramref name="rng"/>;
        /// then the first Blessing, the first Enchant and the first Sigil of that order (in kind order, while
        /// <paramref name="count"/> allows), then the rest of the shuffled order until <paramref name="count"/>.
        /// Fewer eligible than <paramref name="count"/> means fewer offers, possibly none. Index 0 is what a
        /// timeout picks. Same catalogue, build, count and seed give the same list.
        /// </summary>
        public static void Offer(ContentCatalog catalog, PlayerBuild build, int count, Rng rng, List<string> into)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (build == null) throw new ArgumentNullException(nameof(build));
            if (rng == null) throw new ArgumentNullException(nameof(rng));
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            if (build.LineageId == null) throw new ArgumentException("A build needs a lineage before it can draft.", nameof(build));
            into.Clear();
            if (count == 0) return;

            LineageDef lineage = catalog.GetLineage(build.LineageId);
            var eligible = new List<BoonDef>();
            for (int i = 0; i < lineage.PoolIds.Count; i++)
            {
                BoonDef boon = catalog.GetBoon(lineage.PoolIds[i]);
                if (IsEligible(catalog, build, boon)) eligible.Add(boon);
            }
            eligible.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
            rng.Shuffle(eligible);

            var taken = new List<BoonDef>();
            for (int k = 0; k < BoonKinds.All.Length && taken.Count < count; k++)
            {
                for (int i = 0; i < eligible.Count; i++)
                {
                    if (eligible[i].Kind != BoonKinds.All[k] || taken.Contains(eligible[i])) continue;
                    taken.Add(eligible[i]);
                    break;
                }
            }
            for (int i = 0; i < eligible.Count && taken.Count < count; i++)
                if (!taken.Contains(eligible[i])) taken.Add(eligible[i]);

            for (int i = 0; i < taken.Count; i++) into.Add(taken[i].Id);
        }

        /// <summary>Innate abilities, then every item's, then what every owned boon's Sigil grants: the abilities the build's unit would have.</summary>
        public static void UnitAbilities(ContentCatalog catalog, PlayerBuild build, List<string> into)
        {
            into.Clear();
            for (int i = 0; i < catalog.Rules.InnateAbilityIds.Count; i++) into.Add(catalog.Rules.InnateAbilityIds[i]);
            for (int s = 0; s < ItemSlots.All.Length; s++)
            {
                ItemDef item = catalog.GetItemForSlot(ItemSlots.All[s], build.Loadout.IdForSlot(ItemSlots.All[s]));
                for (int a = 0; a < item.AbilityIds.Count; a++) if (!into.Contains(item.AbilityIds[a])) into.Add(item.AbilityIds[a]);
            }
            for (int b = 0; b < build.BoonIds.Count; b++)
            {
                BoonDef boon;
                if (!catalog.Boons.TryGet(build.BoonIds[b], out boon)) continue;
                for (int e = 0; e < boon.Effects.Count; e++)
                {
                    BoonEffect effect = boon.Effects[e];
                    if (effect.Type == BoonEffectTypes.GrantAbility && !into.Contains(effect.Ability)) into.Add(effect.Ability);
                }
            }
        }

        private static bool IsOwned(ContentCatalog catalog, PlayerBuild build, string boonId)
        {
            if (build.HasBoon(boonId)) return true;
            return build.LineageId != null && catalog.GetLineage(build.LineageId).StartingBlessingId == boonId;
        }
    }
}
