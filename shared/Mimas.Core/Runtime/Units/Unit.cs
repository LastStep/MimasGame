using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// One unit on the board: identity, owner, class, where it stands, its abilities, its modifiers, and
    /// its live numbers (hit points, action points). Abilities and modifiers are ids resolved against the
    /// <c>ContentCatalog</c>; the ability list starts as the class's starting abilities and boons edit it
    /// later. Every state write has one method (<see cref="MoveTo"/>, <see cref="TakeDamage"/>,
    /// <see cref="RefreshAp"/>, <see cref="SpendAp"/>) so "who changed this" is always one search away.
    /// </summary>
    public sealed class Unit
    {
        private static readonly StatBlock ClasslessStats = new StatBlock(new[]
        {
            new KeyValuePair<string, int>(StatBlock.HpKey, 10),
            new KeyValuePair<string, int>(StatBlock.ApKey, 3),
        });

        private readonly List<string> _abilityIds = new List<string>();
        private readonly List<string> _modifierIds = new List<string>();

        public int Id { get; }

        /// <summary>Owning player index (0 or 1 in a 1v1).</summary>
        public int Owner { get; }

        /// <summary>The class this unit was created from, or null for a classless test unit.</summary>
        public string ClassId { get; }

        /// <summary>Base numbers from the class (or a 10 hp / 3 ap stand-in for classless test units).</summary>
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

        /// <summary>A unit with no class and no abilities: for tests and scaffolding.</summary>
        public Unit(int id, int owner, Hex position)
            : this(id, owner, position, null, ClasslessStats)
        {
        }

        /// <summary>A unit of <paramref name="cls"/>, starting with that class's abilities and stats, at full hp and 0 ap.</summary>
        public Unit(int id, int owner, Hex position, ClassDef cls)
            : this(id, owner, position, cls ?? throw new ArgumentNullException(nameof(cls)), cls.Stats)
        {
        }

        private Unit(int id, int owner, Hex position, ClassDef cls, StatBlock stats)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
            Id = id;
            Owner = owner;
            Position = position;
            Stats = stats;
            Hp = stats.Hp;
            Ap = 0;
            if (cls != null)
            {
                ClassId = cls.Id;
                for (int i = 0; i < cls.AbilityIds.Count; i++) _abilityIds.Add(cls.AbilityIds[i]);
            }
        }

        public bool HasAbility(string abilityId) => abilityId != null && _abilityIds.Contains(abilityId);

        /// <summary>Grants an ability (a boon, a pickup). Ignored if already present.</summary>
        public void AddAbility(string abilityId)
        {
            if (string.IsNullOrEmpty(abilityId)) throw new ArgumentException("Ability id must not be empty.", nameof(abilityId));
            if (!_abilityIds.Contains(abilityId)) _abilityIds.Add(abilityId);
        }

        /// <summary>Removes an ability. Returns false if the unit did not have it.</summary>
        public bool RemoveAbility(string abilityId) => abilityId != null && _abilityIds.Remove(abilityId);

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
    }
}
