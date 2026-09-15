using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// One unit on the board: identity, owner, class, where it stands and which abilities it currently has.
    /// Abilities are ids resolved against the <c>ContentCatalog</c>; the list starts as the class's starting
    /// abilities and boons or modifiers edit it later. Position changes only through <see cref="MoveTo"/>
    /// so there is a single write-site for "where is this unit" in the rules. Stats join when designed.
    /// </summary>
    public sealed class Unit
    {
        private readonly List<string> _abilityIds = new List<string>();

        public int Id { get; }

        /// <summary>Owning player index (0 or 1 in a 1v1).</summary>
        public int Owner { get; }

        /// <summary>The class this unit was created from, or null for a classless test unit.</summary>
        public string ClassId { get; }

        public Hex Position { get; private set; }

        /// <summary>Ability ids in the order they were granted. Movement rules only look at the movement ones.</summary>
        public IReadOnlyList<string> AbilityIds => _abilityIds;

        /// <summary>A unit with no class and no abilities: for tests and scaffolding.</summary>
        public Unit(int id, int owner, Hex position)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (owner < 0) throw new ArgumentOutOfRangeException(nameof(owner));
            Id = id;
            Owner = owner;
            Position = position;
        }

        /// <summary>A unit of <paramref name="cls"/>, starting with that class's abilities.</summary>
        public Unit(int id, int owner, Hex position, ClassDef cls)
            : this(id, owner, position)
        {
            if (cls == null) throw new ArgumentNullException(nameof(cls));
            ClassId = cls.Id;
            for (int i = 0; i < cls.AbilityIds.Count; i++) _abilityIds.Add(cls.AbilityIds[i]);
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

        /// <summary>Relocates the unit. Callers validate the move first; this is the state write, nothing more.</summary>
        public void MoveTo(Hex destination)
        {
            Position = destination;
        }
    }
}
