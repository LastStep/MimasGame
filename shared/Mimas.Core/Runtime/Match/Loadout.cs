using System;
using System.Collections.Generic;
using Mimas.Core.Data;

namespace Mimas.Core.Match
{
    /// <summary>
    /// One item id per slot, the whole of a player's gear for a session (design: #character, #equipment).
    /// Ids only; the catalogue resolves them and checks that each one fills the slot it was put in.
    /// Immutable, value-equal.
    /// </summary>
    public sealed class Loadout : IEquatable<Loadout>
    {
        private readonly List<string> _itemIds;

        public string WeaponId { get; }
        public string CrownId { get; }
        public string BootsId { get; }
        public string ArmourId { get; }

        /// <summary>Ids in <see cref="ItemSlots.All"/> order.</summary>
        public IReadOnlyList<string> ItemIds => _itemIds;

        public Loadout(string weaponId, string crownId, string bootsId, string armourId)
        {
            WeaponId = Check(weaponId, ItemSlots.Weapon);
            CrownId = Check(crownId, ItemSlots.Crown);
            BootsId = Check(bootsId, ItemSlots.Boots);
            ArmourId = Check(armourId, ItemSlots.Armour);
            _itemIds = new List<string> { WeaponId, CrownId, BootsId, ArmourId };
        }

        /// <summary>The id worn in that slot.</summary>
        public string IdForSlot(string slot)
        {
            int index = ItemSlots.IndexOf(slot);
            if (index < 0) throw new ArgumentException($"Unknown item slot '{slot}'.", nameof(slot));
            return _itemIds[index];
        }

        public bool Equals(Loadout other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            return string.Equals(WeaponId, other.WeaponId, StringComparison.Ordinal)
                && string.Equals(CrownId, other.CrownId, StringComparison.Ordinal)
                && string.Equals(BootsId, other.BootsId, StringComparison.Ordinal)
                && string.Equals(ArmourId, other.ArmourId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj) => Equals(obj as Loadout);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                for (int i = 0; i < _itemIds.Count; i++)
                    hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_itemIds[i]);
                return hash;
            }
        }

        public override string ToString() => string.Join(" / ", _itemIds);

        private static string Check(string itemId, string slot)
        {
            if (string.IsNullOrEmpty(itemId)) throw new ArgumentException($"Loadout slot '{slot}' must name an item.", slot);
            return itemId;
        }
    }
}
