using System;
using System.Collections.Generic;
using Mimas.Core.Data;

namespace Mimas.Core.Units
{
    /// <summary>A boon's contribution to one stat key.</summary>
    public readonly struct StatContribution
    {
        public readonly string BoonId;
        public readonly string Key;
        public readonly int Amount;

        public StatContribution(string boonId, string key, int amount)
        {
            BoonId = boonId;
            Key = key;
            Amount = amount;
        }

        public override string ToString() => $"{BoonId}: {Key} {(Amount >= 0 ? "+" : "")}{Amount}";
    }

    /// <summary>A modifier a boon attached to the unit.</summary>
    public readonly struct ModifierContribution
    {
        public readonly string BoonId;
        public readonly string ModifierId;

        public ModifierContribution(string boonId, string modifierId)
        {
            BoonId = boonId;
            ModifierId = modifierId;
        }
    }

    /// <summary>One field of one ability changed by one boon: an additive amount, or (for the skeleton fields) a value.</summary>
    public readonly struct AbilityOverride
    {
        public readonly string BoonId;
        public readonly string AbilityId;
        public readonly string Field;
        public readonly int Amount;
        public readonly string Value;

        public bool IsValue => Value != null;

        public AbilityOverride(string boonId, string abilityId, string field, int amount, string value)
        {
            BoonId = boonId;
            AbilityId = abilityId;
            Field = field;
            Amount = amount;
            Value = value;
        }

        public override string ToString() => IsValue ? $"{BoonId}: {AbilityId}.{Field} = {Value}" : $"{BoonId}: {AbilityId}.{Field} {(Amount >= 0 ? "+" : "")}{Amount}";
    }

    /// <summary>An element or a tag a boon added to one ability.</summary>
    public readonly struct AbilityAddition
    {
        public readonly string BoonId;
        public readonly string AbilityId;
        public readonly string Value;

        public AbilityAddition(string boonId, string abilityId, string value)
        {
            BoonId = boonId;
            AbilityId = abilityId;
            Value = value;
        }
    }

    /// <summary>An ability a Sigil put on an item.</summary>
    public readonly struct AbilityGrant
    {
        public readonly string BoonId;
        public readonly string ItemId;
        public readonly string AbilityId;

        public AbilityGrant(string boonId, string itemId, string abilityId)
        {
            BoonId = boonId;
            ItemId = itemId;
            AbilityId = abilityId;
        }
    }

    /// <summary>
    /// A unit's boons folded once into flat tables (ADR-034): stat contributions, attached modifiers,
    /// ability overrides resolved to concrete ability ids, added elements and tags per ability, and the
    /// abilities Sigils granted. <b>Every entry carries the id of the boon that put it there</b>, which is
    /// what reveal keys on. Definitions are never touched: rules code asks <c>MatchState.ResolveAbility</c>
    /// for a copy of an ability with this overlay applied. A plain data class with linear scans: two heroes,
    /// a handful of boons.
    /// <para>
    /// A slot target ("every ability of the weapon") expands to the item's own abilities and to every
    /// ability a Sigil grants on that item, whichever came first (design: #enchant, D6 "including later
    /// Sigils"), in grant order.
    /// </para>
    /// </summary>
    public sealed class BoonOverlay
    {
        public static readonly BoonOverlay Empty = new BoonOverlay(new List<ItemDef>(), new List<BoonDef>());

        private readonly List<StatContribution> _stats = new List<StatContribution>();
        private readonly List<ModifierContribution> _modifiers = new List<ModifierContribution>();
        private readonly List<AbilityOverride> _overrides = new List<AbilityOverride>();
        private readonly List<AbilityAddition> _elements = new List<AbilityAddition>();
        private readonly List<AbilityAddition> _tags = new List<AbilityAddition>();
        private readonly List<AbilityGrant> _grants = new List<AbilityGrant>();

        public IReadOnlyList<StatContribution> StatContributions => _stats;
        public IReadOnlyList<ModifierContribution> Modifiers => _modifiers;
        public IReadOnlyList<AbilityOverride> Overrides => _overrides;
        public IReadOnlyList<AbilityAddition> AddedElements => _elements;
        public IReadOnlyList<AbilityAddition> AddedTags => _tags;
        public IReadOnlyList<AbilityGrant> Grants => _grants;

        public bool IsEmpty => _stats.Count == 0 && _modifiers.Count == 0 && _overrides.Count == 0 && _elements.Count == 0 && _tags.Count == 0 && _grants.Count == 0;

        /// <summary>Folds <paramref name="boons"/>, in order, over the four items. Throws when a grant names a slot no item fills.</summary>
        public BoonOverlay(IReadOnlyList<ItemDef> items, IReadOnlyList<BoonDef> boons)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (boons == null) throw new ArgumentNullException(nameof(boons));

            // Grants first, so a slot target further down sees every Sigil ability on the item.
            for (int b = 0; b < boons.Count; b++)
            {
                BoonDef boon = boons[b];
                for (int e = 0; e < boon.Effects.Count; e++)
                {
                    BoonEffect effect = boon.Effects[e];
                    if (effect.Type != BoonEffectTypes.GrantAbility) continue;
                    ItemDef item = ItemInSlot(items, effect.Target);
                    if (item == null) throw new ArgumentException($"Boon '{boon.Id}' grants '{effect.Ability}' on the {effect.Target}, but no item fills that slot.", nameof(boons));
                    _grants.Add(new AbilityGrant(boon.Id, item.Id, effect.Ability));
                }
            }

            var targets = new List<string>();
            for (int b = 0; b < boons.Count; b++)
            {
                BoonDef boon = boons[b];
                for (int e = 0; e < boon.Effects.Count; e++)
                {
                    BoonEffect effect = boon.Effects[e];
                    switch (effect.Type)
                    {
                        case BoonEffectTypes.Stat:
                            _stats.Add(new StatContribution(boon.Id, effect.Key, effect.Amount.Value));
                            break;
                        case BoonEffectTypes.Modifier:
                            _modifiers.Add(new ModifierContribution(boon.Id, effect.Id));
                            break;
                        case BoonEffectTypes.AbilityOverride:
                            ResolveTargets(items, effect.Target, targets);
                            for (int t = 0; t < targets.Count; t++)
                                _overrides.Add(new AbilityOverride(boon.Id, targets[t], effect.Field, effect.Amount ?? 0, effect.Value));
                            break;
                        case BoonEffectTypes.AddElement:
                            ResolveTargets(items, effect.Target, targets);
                            for (int t = 0; t < targets.Count; t++)
                                _elements.Add(new AbilityAddition(boon.Id, targets[t], effect.Element));
                            break;
                        case BoonEffectTypes.AddTag:
                            ResolveTargets(items, effect.Target, targets);
                            for (int t = 0; t < targets.Count; t++)
                                _tags.Add(new AbilityAddition(boon.Id, targets[t], effect.Tag));
                            break;
                    }
                }
            }
        }

        /// <summary>A slot expands to the item's abilities then the Sigil grants on it; anything else is one ability id.</summary>
        private void ResolveTargets(IReadOnlyList<ItemDef> items, string target, List<string> into)
        {
            into.Clear();
            if (!ItemSlots.IsKnown(target))
            {
                into.Add(target);
                return;
            }
            ItemDef item = ItemInSlot(items, target);
            if (item == null) return;
            for (int i = 0; i < item.AbilityIds.Count; i++) into.Add(item.AbilityIds[i]);
            for (int i = 0; i < _grants.Count; i++)
                if (_grants[i].ItemId == item.Id && !into.Contains(_grants[i].AbilityId)) into.Add(_grants[i].AbilityId);
        }

        private static ItemDef ItemInSlot(IReadOnlyList<ItemDef> items, string slot)
        {
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null && items[i].Slot == slot) return items[i];
            return null;
        }

        /// <summary>
        /// The first skeleton entry, if any (spec D part 1 §6.7): an override on <c>hits</c>, <c>trajectory</c>
        /// or <c>lineOfSight</c>. A unit refuses to be built with one.
        /// </summary>
        public bool TryFindSkeleton(out string message)
        {
            for (int i = 0; i < _overrides.Count; i++)
            {
                if (_overrides[i].Field == AbilityFields.Hits)
                {
                    message = $"multi-hit attacks are not implemented (spec D part 1 §6.7, E5/S3): boon '{_overrides[i].BoonId}' overrides hits on '{_overrides[i].AbilityId}'.";
                    return true;
                }
                if (AbilityFields.IsValueField(_overrides[i].Field))
                {
                    message = $"trajectory swap is not implemented (spec D part 1 §6.7, E6): boon '{_overrides[i].BoonId}' overrides {_overrides[i].Field} on '{_overrides[i].AbilityId}'.";
                    return true;
                }
            }
            message = null;
            return false;
        }

        /// <summary>The boon that attached a modifier (the first, in grant order), or null.</summary>
        public string BoonOfModifier(string modifierId)
        {
            for (int i = 0; i < _modifiers.Count; i++)
                if (_modifiers[i].ModifierId == modifierId) return _modifiers[i].BoonId;
            return null;
        }

        /// <summary>The Sigil that granted an ability, or null when no boon did.</summary>
        public string BoonOfGrant(string abilityId)
        {
            for (int i = 0; i < _grants.Count; i++)
                if (_grants[i].AbilityId == abilityId) return _grants[i].BoonId;
            return null;
        }

        /// <summary>Every contribution to one stat key, in boon order.</summary>
        public void StatContributionsFor(string key, List<StatContribution> into)
        {
            into.Clear();
            for (int i = 0; i < _stats.Count; i++)
                if (_stats[i].Key == key) into.Add(_stats[i]);
        }

        /// <summary>Every override on one ability, in boon order.</summary>
        public void OverridesFor(string abilityId, List<AbilityOverride> into)
        {
            into.Clear();
            for (int i = 0; i < _overrides.Count; i++)
                if (_overrides[i].AbilityId == abilityId) into.Add(_overrides[i]);
        }

        public void AddedElementsFor(string abilityId, List<AbilityAddition> into)
        {
            into.Clear();
            for (int i = 0; i < _elements.Count; i++)
                if (_elements[i].AbilityId == abilityId) into.Add(_elements[i]);
        }

        public void AddedTagsFor(string abilityId, List<AbilityAddition> into)
        {
            into.Clear();
            for (int i = 0; i < _tags.Count; i++)
                if (_tags[i].AbilityId == abilityId) into.Add(_tags[i]);
        }
    }
}
