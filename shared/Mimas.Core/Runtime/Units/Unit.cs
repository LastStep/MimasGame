using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// One unit on the board: identity, owner, gear, where it stands, its abilities, its modifiers, and
    /// its live numbers (hit points, action points). Abilities and modifiers are ids resolved against the
    /// <c>ContentCatalog</c>; a hero's ability list starts as the rules' innate abilities followed by the
    /// abilities of each equipped item, and boons edit it later. Every state write has one method
    /// (<see cref="MoveTo"/>, <see cref="TakeDamage"/>, <see cref="RefreshAp"/>, <see cref="SpendAp"/>) so
    /// "who changed this" is always one search away.
    /// </summary>
    public sealed class Unit
    {
        private static readonly StatBlock BareStats = new StatBlock(new[]
        {
            new KeyValuePair<string, int>(StatBlock.HpKey, 10),
            new KeyValuePair<string, int>(StatBlock.ApKey, 3),
        });

        private static readonly string[] NoItems = new string[0];

        private readonly List<string> _abilityIds = new List<string>();

        /// <summary>Parallel to <see cref="_abilityIds"/>: the item that granted it, or null when innate.</summary>
        private readonly List<string> _abilitySources = new List<string>();

        private readonly List<string> _modifierIds = new List<string>();
        private readonly List<string> _itemIds;

        public int Id { get; }

        /// <summary>Owning player index (0 or 1 in a 1v1).</summary>
        public int Owner { get; }

        /// <summary>Equipped item ids in slot order (weapon, crown, boots, armour); empty for a bare test unit.</summary>
        public IReadOnlyList<string> ItemIds => _itemIds;

        /// <summary>Base stats plus every item's stats (or a 10 hp / 3 ap stand-in for bare test units).</summary>
        public StatBlock Stats { get; }

        public Hex Position { get; private set; }

        public int MaxHp => Stats.Hp;

        /// <summary>Current hit points, 0..MaxHp. 0 means dead.</summary>
        public int Hp { get; private set; }

        /// <summary>Action points left this turn.</summary>
        public int Ap { get; private set; }

        /// <summary>Action points granted at the start of each of the owner's turns.</summary>
        public int ApPerTurn => Stats.Ap;

        public bool IsAlive => Hp > 0;

        /// <summary>Ability ids in the order they were granted. Movement rules only look at the movement ones.</summary>
        public IReadOnlyList<string> AbilityIds => _abilityIds;

        /// <summary>Modifier ids this unit carries (boons, pickups), in the order they were granted.</summary>
        public IReadOnlyList<string> ModifierIds => _modifierIds;

        /// <summary>A unit with no gear and no abilities: for tests and scaffolding.</summary>
        public Unit(int id, int owner, Hex position)
        {
            CheckIds(id, owner);
            Id = id;
            Owner = owner;
            Position = position;
            Stats = BareStats;
            Hp = Stats.Hp;
            Ap = 0;
            _itemIds = new List<string>(NoItems);
        }

        /// <summary>A hero built from the rules' base stats and innate abilities plus four items in slot order.</summary>
        public Unit(int id, int owner, Hex position, RulesDef rules, IReadOnlyList<ItemDef> items)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (items == null) throw new ArgumentNullException(nameof(items));
            CheckIds(id, owner);
            Id = id;
            Owner = owner;
            Position = position;

            StatBlock stats = rules.BaseStats;
            _itemIds = new List<string>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ItemDef item = items[i];
                if (item == null) throw new ArgumentException($"Item {i} is null.", nameof(items));
                stats = stats.Add(item.Stats);
                _itemIds.Add(item.Id);
            }
            Stats = stats;
            Hp = Stats.Hp;
            Ap = 0;

            for (int i = 0; i < rules.InnateAbilityIds.Count; i++) Grant(rules.InnateAbilityIds[i], null);
            for (int i = 0; i < items.Count; i++)
            {
                ItemDef item = items[i];
                for (int a = 0; a < item.AbilityIds.Count; a++) Grant(item.AbilityIds[a], item.Id);
            }
        }

        public bool HasAbility(string abilityId) => abilityId != null && _abilityIds.Contains(abilityId);

        /// <summary>The item that granted an ability, or null for an innate ability or an unknown id.</summary>
        public string AbilitySourceOf(string abilityId)
        {
            if (abilityId == null) return null;
            int index = _abilityIds.IndexOf(abilityId);
            return index < 0 ? null : _abilitySources[index];
        }

        /// <summary>Grants an ability (a boon, a pickup). Ignored if already present.</summary>
        public void AddAbility(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) throw new ArgumentException("Ability id must not be empty.", nameof(abilityId));
            if (_abilityIds.Contains(abilityId)) return;
            _abilityIds.Add(abilityId);
            _abilitySources.Add(null);
        }

        /// <summary>Removes an ability. Returns false if the unit did not have it.</summary>
        public bool RemoveAbility(string abilityId)
        {
            if (abilityId == null) return false;
            int index = _abilityIds.IndexOf(abilityId);
            if (index < 0) return false;
            _abilityIds.RemoveAt(index);
            _abilitySources.RemoveAt(index);
            return true;
        }

        public bool HasModifier(string modifierId) => modifierId != null && _modifierIds.Contains(modifierId);

        /// <summary>Grants a modifier. Same id twice does not stack (set semantics until data says otherwise).</summary>
        public void AddModifier(string modifierId)
        {
            if (string.IsNullOrEmpty(modifierId)) throw new ArgumentException("Modifier id must not be empty.", nameof(modifierId));
            if (!_modifierIds.Contains(modifierId)) _modifierIds.Add(modifierId);
        }

        public bool RemoveModifier(string modifierId) => modifierId != null && _modifierIds.Remove(modifierId);

        /// <summary>Relocates the unit. Callers validate the move first; this is the state write, nothing more.</summary>
        public void MoveTo(Hex destination)
        {
            Position = destination;
        }

        /// <summary>Applies damage (never below 0 hp). Returns the hit points actually lost.</summary>
        public int TakeDamage(int amount)
        {
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int before = Hp;
            Hp = amount >= Hp ? 0 : Hp - amount;
            return before - Hp;
        }

        /// <summary>Start of the owner's turn: action points back to the per-turn allowance (no carry-over).</summary>
        public void RefreshAp()
        {
            Ap = ApPerTurn;
        }

        /// <summary>End of the owner's turn: unspent points are lost.</summary>
        public void ClearAp()
        {
            Ap = 0;
        }

        public bool CanAfford(int cost) => cost >= 0 && cost <= Ap;

        /// <summary>Spends action points. Callers check <see cref="CanAfford"/> first.</summary>
        public void SpendAp(int cost)
        {
            if (!CanAfford(cost)) throw new InvalidOperationException($"Unit {Id} cannot spend {cost} ap (has {Ap}).");
            Ap -= cost;
        }

        private void Grant(string abilityId, string sourceItemId)
        {
            if (_abilityIds.Contains(abilityId)) throw new ArgumentException($"Ability '{abilityId}' is granted twice.");
            _abilityIds.Add(abilityId);
            _abilitySources.Add(sourceItemId);
        }

        private static void CheckIds(int id, int owner)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
        }
    }
}
