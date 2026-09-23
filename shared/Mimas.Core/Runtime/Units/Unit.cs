using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;

namespace Mimas.Core.Units
{
    /// <summary>
    /// A slot in a unit's ability or modifier list that one player is not allowed to see the contents of:
    /// its position in the list, and (for an ability) the item that granted it, which is public even when
    /// what it grants is not. A mirror keeps these so it can reproduce the "?" rows of the view it was
    /// built from, in the right places (ADR-026).
    /// </summary>
    public readonly struct HiddenSlot
    {
        /// <summary>Index in the full list, counting the hidden entries.</summary>
        public readonly int Index;

        /// <summary>The item that granted the hidden ability, or null for an innate ability or a modifier.</summary>
        public readonly string SourceItemId;

        public HiddenSlot(int index, string sourceItemId)
        {
            Index = index;
            SourceItemId = sourceItemId;
        }
    }

    /// <summary>
    /// One line of how a stat is made (<see cref="Unit.StatLines"/>): the rules base, one equipped item, or
    /// one boon, with its raw amount (before any floor).
    /// </summary>
    public readonly struct StatLine
    {
        public const string BaseKind = "base";
        public const string ItemKind = "item";
        public const string BoonKind = "boon";

        /// <summary><see cref="BaseKind"/>, <see cref="ItemKind"/> or <see cref="BoonKind"/>.</summary>
        public string SourceKind { get; }

        /// <summary>Null for the base; the item id; the boon id.</summary>
        public string SourceId { get; }

        public int Amount { get; }

        public StatLine(string sourceKind, string sourceId, int amount)
        {
            SourceKind = sourceKind;
            SourceId = sourceId;
            Amount = amount;
        }

        public override string ToString() => $"{SourceKind}{(SourceId == null ? "" : ":" + SourceId)} {(Amount >= 0 ? "+" : "")}{Amount}";
    }

    /// <summary>
    /// One unit on the board: identity, owner, gear, lineage and boons, where it stands, its abilities, its
    /// modifiers, and its live numbers (hit points, action points). Abilities and modifiers are ids resolved
    /// against the <c>ContentCatalog</c>; a hero's ability list is the rules' innate abilities, then the
    /// abilities of each equipped item, then what each boon's Sigil grants, in boon order. The boons are
    /// folded once into <see cref="Overlay"/> (ADR-034) and rules code reads an ability only through
    /// <c>MatchState.ResolveAbility</c>. Every state write has one method (<see cref="MoveTo"/>,
    /// <see cref="TakeDamage"/>, <see cref="RefreshAp"/>, <see cref="SpendAp"/>) so "who changed this" is
    /// always one search away.
    /// </summary>
    public sealed class Unit : IBody
    {
        private static readonly StatBlock BareStats = new StatBlock(new[]
        {
            new KeyValuePair<string, int>(StatBlock.HpKey, 10),
            new KeyValuePair<string, int>(StatBlock.ApKey, 3),
        });

        private static readonly string[] NoItems = new string[0];
        private static readonly BoonDef[] NoBoons = new BoonDef[0];

        private readonly List<string> _abilityIds = new List<string>();

        /// <summary>Parallel to <see cref="_abilityIds"/>: the item that granted it, or null when innate.</summary>
        private readonly List<string> _abilitySources = new List<string>();

        private readonly List<string> _modifierIds = new List<string>();

        /// <summary>Parallel to <see cref="_modifierIds"/>: the boon that attached it, or null.</summary>
        private readonly List<string> _modifierBoons = new List<string>();

        private readonly List<string> _boonIds = new List<string>();
        private readonly List<string> _itemIds;

        /// <summary>Parallel to <see cref="_itemIds"/>: each item's own stat block, for <see cref="StatLines"/>.</summary>
        private readonly List<StatBlock> _itemStats;

        /// <summary>The rules base block this unit was built on (the bare block for a test unit).</summary>
        private readonly StatBlock _baseStats;
        private readonly HeightsDef _heights;
        private readonly List<HiddenSlot> _hiddenAbilitySlots = new List<HiddenSlot>();
        private readonly List<HiddenSlot> _hiddenModifierSlots = new List<HiddenSlot>();
        private readonly List<HiddenSlot> _hiddenBoonSlots = new List<HiddenSlot>();
        private readonly List<StatContribution> _statScratch = new List<StatContribution>();

        public int Id { get; }

        /// <summary>Owning player index (0 or 1 in a 1v1).</summary>
        public int Owner { get; }

        /// <summary>Equipped item ids in slot order (weapon, crown, boots, armour); empty for a bare test unit.</summary>
        public IReadOnlyList<string> ItemIds => _itemIds;

        /// <summary>The lineage prayed to, or null (design: #lineage). Hidden from the opponent until a boon of it is revealed.</summary>
        public string LineageId { get; }

        /// <summary>Boon ids in grant order (the starting Blessing first, then each draft pick).</summary>
        public IReadOnlyList<string> BoonIds => _boonIds;

        /// <summary>The boons folded into tables; empty on a unit with none.</summary>
        public BoonOverlay Overlay { get; }

        /// <summary>Base stats plus every item's stats: what gear explains, and therefore public (design: #hidden-info).</summary>
        public StatBlock PublicStats { get; }

        /// <summary>
        /// <see cref="PublicStats"/> plus every boon's stat contribution, floored by <c>rules.boons</c>
        /// (hp and ap at their floors, everything else at 0). What the rules read.
        /// </summary>
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

        /// <summary>A hero is always a legal target for anything that can reach it.</summary>
        public bool IsDamageable => true;

        /// <summary>Body height in units above the tile top (rules.heights.body): what this hero blocks with.</summary>
        public int BodyHeight => _heights.Body;

        /// <summary>Where this hero's attacks leave from and where shots at it land (rules.heights.aim).</summary>
        public int AimHeight => _heights.Aim;

        /// <summary>Ability ids in the order they were granted. Movement rules only look at the movement ones.</summary>
        public IReadOnlyList<string> AbilityIds => _abilityIds;

        /// <summary>Modifier ids this unit carries (boons, pickups), in the order they were granted.</summary>
        public IReadOnlyList<string> ModifierIds => _modifierIds;

        /// <summary>
        /// Abilities this unit has that the mirror's viewer may not see, by position and granting item.
        /// Always empty on a unit that belongs to the truth (ADR-026).
        /// </summary>
        public IReadOnlyList<HiddenSlot> HiddenAbilitySlots => _hiddenAbilitySlots;

        /// <summary>Modifiers this unit has that the mirror's viewer may not see, by position. Always empty on the truth.</summary>
        public IReadOnlyList<HiddenSlot> HiddenModifierSlots => _hiddenModifierSlots;

        /// <summary>Boons this unit has that the mirror's viewer may not see, by position. Always empty on the truth.</summary>
        public IReadOnlyList<HiddenSlot> HiddenBoonSlots => _hiddenBoonSlots;

        /// <summary>How many of this unit's abilities the mirror's viewer cannot see. 0 on a truth unit.</summary>
        public int HiddenAbilityCount => _hiddenAbilitySlots.Count;

        /// <summary>How many of this unit's modifiers the mirror's viewer cannot see. 0 on a truth unit; what a "?" damage row counts.</summary>
        public int HiddenModifierCount => _hiddenModifierSlots.Count;

        /// <summary>How many of this unit's boons the mirror's viewer cannot see. 0 on a truth unit; the count of picks is public, their identity is not.</summary>
        public int HiddenBoonCount => _hiddenBoonSlots.Count;

        /// <summary>A unit with no gear and no abilities: for tests and scaffolding.</summary>
        public Unit(int id, int owner, Hex position, HeightsDef heights)
        {
            CheckIds(id, owner);
            Id = id;
            Owner = owner;
            Position = position;
            _heights = heights ?? throw new ArgumentNullException(nameof(heights));
            _baseStats = BareStats;
            PublicStats = BareStats;
            Stats = BareStats;
            Overlay = BoonOverlay.Empty;
            Hp = Stats.Hp;
            Ap = 0;
            _itemIds = new List<string>(NoItems);
            _itemStats = new List<StatBlock>();
        }

        /// <summary>A hero built from the rules' base stats and innate abilities plus four items in slot order, with no lineage and no boons.</summary>
        public Unit(int id, int owner, Hex position, RulesDef rules, IReadOnlyList<ItemDef> items)
            : this(id, owner, position, rules, items, null, NoBoons)
        {
        }

        /// <summary>
        /// A hero built from gear, a lineage and boons (spec D part 1 §6.2): stats are base + items + boon
        /// stats, floored; abilities are innate, then each item's, then each Sigil's grant in boon order;
        /// every boon modifier is attached and remembers its boon. Throws <see cref="NotSupportedException"/>
        /// when a boon uses a skeleton field (§6.7).
        /// </summary>
        public Unit(int id, int owner, Hex position, RulesDef rules, IReadOnlyList<ItemDef> items, string lineageId, IReadOnlyList<BoonDef> boons)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (boons == null) throw new ArgumentNullException(nameof(boons));
            CheckIds(id, owner);
            Id = id;
            Owner = owner;
            Position = position;
            _heights = rules.Heights;
            LineageId = string.IsNullOrEmpty(lineageId) ? null : lineageId;

            _baseStats = rules.BaseStats;
            StatBlock stats = rules.BaseStats;
            _itemIds = new List<string>(items.Count);
            _itemStats = new List<StatBlock>(items.Count);
            for (int i = 0; i < items.Count; i++)
            {
                ItemDef item = items[i];
                if (item == null) throw new ArgumentException($"Item {i} is null.", nameof(items));
                stats = stats.Add(item.Stats);
                _itemIds.Add(item.Id);
                _itemStats.Add(item.Stats);
            }
            PublicStats = stats;

            for (int i = 0; i < boons.Count; i++)
            {
                if (boons[i] == null) throw new ArgumentException($"Boon {i} is null.", nameof(boons));
                _boonIds.Add(boons[i].Id);
            }
            Overlay = boons.Count == 0 ? BoonOverlay.Empty : new BoonOverlay(items, boons);

            string skeleton;
            if (Overlay.TryFindSkeleton(out skeleton)) throw new NotSupportedException(skeleton);

            Stats = FloorStats(stats, Overlay, rules.Boons);
            Hp = Stats.Hp;
            Ap = 0;

            for (int i = 0; i < rules.InnateAbilityIds.Count; i++) Grant(rules.InnateAbilityIds[i], null);
            for (int i = 0; i < items.Count; i++)
            {
                ItemDef item = items[i];
                for (int a = 0; a < item.AbilityIds.Count; a++) Grant(item.AbilityIds[a], item.Id);
            }
            for (int g = 0; g < Overlay.Grants.Count; g++)
                Grant(Overlay.Grants[g].AbilityId, Overlay.Grants[g].ItemId);

            // Set semantics for modifiers: the first boon in grant order owns a modifier two boons both attach.
            for (int m = 0; m < Overlay.Modifiers.Count; m++)
                AddModifier(Overlay.Modifiers[m].ModifierId, Overlay.Modifiers[m].BoonId);
        }

        /// <summary>Base + items + every boon contribution, then the floors: hp and ap at <c>rules.boons.floors</c>, everything else at 0 (spec §6.2).</summary>
        private static StatBlock FloorStats(StatBlock publicStats, BoonOverlay overlay, BoonRulesDef floors)
        {
            if (overlay.StatContributions.Count == 0) return publicStats;
            var entries = new List<KeyValuePair<string, int>>();
            for (int i = 0; i < overlay.StatContributions.Count; i++)
                entries.Add(new KeyValuePair<string, int>(overlay.StatContributions[i].Key, overlay.StatContributions[i].Amount));
            StatBlock summed = publicStats;
            for (int i = 0; i < entries.Count; i++)
                summed = summed.Add(new StatBlock(new[] { entries[i] }));

            var floored = new List<KeyValuePair<string, int>>(summed.Entries.Count);
            for (int i = 0; i < summed.Entries.Count; i++)
            {
                string key = summed.Entries[i].Key;
                int value = summed.Entries[i].Value;
                int floor = key == StatBlock.HpKey ? floors.HpFloor : key == StatBlock.ApKey ? floors.ApFloor : 0;
                floored.Add(new KeyValuePair<string, int>(key, value < floor ? floor : value));
            }
            return new StatBlock(floored);
        }

        public bool HasAbility(string abilityId) => abilityId != null && _abilityIds.Contains(abilityId);

        /// <summary>The Sigil that granted an ability, or null for an innate or item ability (design: #hidden-info rule 5: the item is public, the boon is not).</summary>
        public string BoonOfAbility(string abilityId) => abilityId != null && _abilityIds.Contains(abilityId) ? Overlay.BoonOfGrant(abilityId) : null;

        /// <summary>The boon that attached a modifier, or null when it came from elsewhere (a setup knob, a pickup).</summary>
        public string BoonOfModifier(string modifierId)
        {
            if (modifierId == null) return null;
            int index = _modifierIds.IndexOf(modifierId);
            return index < 0 ? null : _modifierBoons[index];
        }

        public bool HasBoon(string boonId) => boonId != null && _boonIds.Contains(boonId);

        /// <summary>Every boon contribution to one stat key, in boon order (what the calculator turns into lines). A fresh list each call.</summary>
        public IReadOnlyList<StatContribution> BoonStatContributions(string key)
        {
            Overlay.StatContributionsFor(key, _statScratch);
            return new List<StatContribution>(_statScratch);
        }

        /// <summary>
        /// How a stat is made, in a fixed order: the rules base, then each equipped item in slot order that
        /// contributes to the key, then each boon on the overlay in grant order with a stat effect on the key.
        /// Zero contributions are omitted. The amounts sum to <see cref="Stats"/>[key] unless a floor applied
        /// (the lines stay raw); the base and item lines sum to <see cref="PublicStats"/>[key]. On a mirror
        /// the overlay holds only revealed boons, so a hidden boon is never listed (ADR-026). Read-only; a
        /// presenter's question (design: #stats rule 3, docs/ui/examine.md §5 gap 1). Clears
        /// <paramref name="into"/> first.
        /// </summary>
        public void StatLines(string statKey, List<StatLine> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (statKey == null) return;

            int baseAmount = _baseStats.Get(statKey);
            if (baseAmount != 0) into.Add(new StatLine(StatLine.BaseKind, null, baseAmount));
            for (int i = 0; i < _itemStats.Count; i++)
            {
                int amount = _itemStats[i].Get(statKey);
                if (amount != 0) into.Add(new StatLine(StatLine.ItemKind, _itemIds[i], amount));
            }
            for (int i = 0; i < Overlay.StatContributions.Count; i++)
            {
                StatContribution c = Overlay.StatContributions[i];
                if (c.Key == statKey && c.Amount != 0) into.Add(new StatLine(StatLine.BoonKind, c.BoonId, c.Amount));
            }
        }

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

        /// <summary>Grants a modifier from no boon (a setup knob, a pickup). Same id twice does not stack (set semantics until data says otherwise).</summary>
        public void AddModifier(string modifierId) => AddModifier(modifierId, null);

        /// <summary>Grants a modifier and remembers the boon that did. Same id twice does not stack; the first grant owns it.</summary>
        public void AddModifier(string modifierId, string boonId)
        {
            if (string.IsNullOrEmpty(modifierId)) throw new ArgumentException("Modifier id must not be empty.", nameof(modifierId));
            if (_modifierIds.Contains(modifierId)) return;
            _modifierIds.Add(modifierId);
            _modifierBoons.Add(boonId);
        }

        public bool RemoveModifier(string modifierId)
        {
            if (modifierId == null) return false;
            int index = _modifierIds.IndexOf(modifierId);
            if (index < 0) return false;
            _modifierIds.RemoveAt(index);
            _modifierBoons.RemoveAt(index);
            return true;
        }

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

        /// <summary>
        /// Puts live numbers back without replaying how they got there: for a mirror rebuilt from a
        /// <see cref="PlayerView"/>, and for the replays that will read a command log. Not a rules operation —
        /// nothing that plays a match should call it.
        /// </summary>
        public void Restore(int hp, int ap)
        {
            if (hp < 0 || hp > MaxHp) throw new ArgumentOutOfRangeException(nameof(hp), $"Unit {Id} hp must be 0..{MaxHp}, was {hp}.");
            if (ap < 0) throw new ArgumentOutOfRangeException(nameof(ap), $"Unit {Id} ap must not be negative, was {ap}.");
            Hp = hp;
            Ap = ap;
        }

        /// <summary>
        /// The mirror's unit (ADR-026): built from the public half of a <see cref="UnitView"/> — its gear, which
        /// fixes every public stat exactly as the truth computes it — plus the boons the viewer has been shown,
        /// and then stripped of everything else. What is left is what that player is entitled to reason with,
        /// and <see cref="HiddenAbilitySlots"/> / <see cref="HiddenModifierSlots"/> / <see cref="HiddenBoonSlots"/>
        /// remember where the gaps were so the view can be reproduced with its "?" rows intact.
        /// <para>
        /// The ability list is aligned with the view: innate and item abilities come first in both and a hidden
        /// one is simply cut from the built list; a Sigil's ability follows, and a hidden one is a "?" the built
        /// list never had. Maximum health and action points must agree with the view, because every hp and AP
        /// Blessing is revealed at round start (D12); a mismatch is a bug, not a hidden value.
        /// </para>
        /// </summary>
        public static Unit FromView(UnitView view, Content.ContentCatalog catalog)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (view.ItemIds.Count != ItemSlots.All.Length)
                throw new ArgumentException($"A hero view needs {ItemSlots.All.Length} items, had {view.ItemIds.Count}.", nameof(view));

            var items = new List<ItemDef>(ItemSlots.All.Length);
            for (int i = 0; i < ItemSlots.All.Length; i++) items.Add(catalog.GetItemForSlot(ItemSlots.All[i], view.ItemIds[i]));

            var boons = new List<BoonDef>();
            for (int i = 0; i < view.Boons.Count; i++)
                if (view.Boons[i].Revealed) boons.Add(catalog.GetBoon(view.Boons[i].Id));

            var unit = new Unit(view.Id, view.Owner, view.Position, catalog.Rules, items, view.LineageId, boons);

            // Abilities: the built list is innate + items + revealed grants; the view is innate + items + every grant.
            int gearCount = unit._abilityIds.Count - unit.Overlay.Grants.Count;
            var keptIds = new List<string>(unit._abilityIds.Count);
            var keptSources = new List<string>(unit._abilityIds.Count);
            int next = 0;
            for (int i = 0; i < view.Abilities.Count; i++)
            {
                KnownEntry entry = view.Abilities[i];
                if (entry.Revealed)
                {
                    if (next >= unit._abilityIds.Count || unit._abilityIds[next] != entry.Id)
                        throw new ArgumentException($"Unit {view.Id}'s view lists ability '{entry.Id}' at {i}, which its gear and revealed boons do not grant there.", nameof(view));
                    keptIds.Add(unit._abilityIds[next]);
                    keptSources.Add(unit._abilitySources[next]);
                    next++;
                    continue;
                }
                unit._hiddenAbilitySlots.Add(new HiddenSlot(i, entry.SourceItemId));
                if (i < gearCount) next++;                       // a hidden gear ability: cut it from the built list
            }
            if (next != unit._abilityIds.Count)
                throw new ArgumentException($"Unit {view.Id}'s view lists {view.Abilities.Count} abilities; its gear and revealed boons grant {unit._abilityIds.Count}.", nameof(view));
            unit._abilityIds.Clear();
            unit._abilityIds.AddRange(keptIds);
            unit._abilitySources.Clear();
            unit._abilitySources.AddRange(keptSources);

            // Modifiers: the built list holds the revealed boons' modifiers, in boon order, at the front of the
            // view's list; a revealed entry that is not the next built one came from elsewhere (a setup knob).
            int builtModifiers = unit._modifierIds.Count;
            int nextModifier = 0;
            for (int i = 0; i < view.Modifiers.Count; i++)
            {
                KnownEntry entry = view.Modifiers[i];
                if (!entry.Revealed)
                {
                    unit._hiddenModifierSlots.Add(new HiddenSlot(i, null));
                    continue;
                }
                if (nextModifier < builtModifiers && unit._modifierIds[nextModifier] == entry.Id) { nextModifier++; continue; }
                unit.AddModifier(entry.Id, null);
            }
            if (nextModifier != builtModifiers)
                throw new ArgumentException($"Unit {view.Id}'s view does not list every modifier its revealed boons attach.", nameof(view));

            // Boons: the built list is exactly the revealed entries, in order.
            for (int i = 0; i < view.Boons.Count; i++)
                if (!view.Boons[i].Revealed) unit._hiddenBoonSlots.Add(new HiddenSlot(i, null));

            if (unit.MaxHp != view.MaxHp)
                throw new ArgumentException($"Unit {view.Id}'s view says max hp {view.MaxHp}; gear and revealed boons give {unit.MaxHp}. Every hp Blessing is revealed at round start, so this is a bug.", nameof(view));
            if (unit.ApPerTurn != view.ApPerTurn)
                throw new ArgumentException($"Unit {view.Id}'s view says {view.ApPerTurn} ap per turn; gear and revealed boons give {unit.ApPerTurn}. Every AP Blessing is revealed at round start, so this is a bug.", nameof(view));

            unit.Restore(view.Hp, view.Ap);
            return unit;
        }

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
