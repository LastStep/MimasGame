namespace Mimas.Core.Data
{
    /// <summary>The four equipment slots, in the order a loadout lists them (design: #equipment).</summary>
    public static class ItemSlots
    {
        public const string Weapon = "weapon";
        public const string Crown = "crown";
        public const string Boots = "boots";
        public const string Armour = "armour";

        /// <summary>Slot order used everywhere a loadout is enumerated.</summary>
        public static readonly string[] All = { Weapon, Crown, Boots, Armour };

        public static bool IsKnown(string slot) => slot == Weapon || slot == Crown || slot == Boots || slot == Armour;

        /// <summary>Index into <see cref="All"/>, or -1.</summary>
        public static int IndexOf(string slot)
        {
            for (int i = 0; i < All.Length; i++) if (All[i] == slot) return i;
            return -1;
        }
    }
}
