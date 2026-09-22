using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// Resolves an attack's damage as a <see cref="DamageBreakdown"/>:
    /// <c>base + power.&lt;type&gt; − defense.&lt;type&gt; + Σ boon stats + Σ flat modifiers</c>, floored at 0,
    /// then taken to 0 by an immunity if one applied. Power and defence come from the bodies' public stats;
    /// every boon's contribution to those keys, and every damage override on the attack, is its own hidden
    /// <see cref="DamageLineKind.BoonStat"/> line so that reveal has something to key on (spec D part 1
    /// §6.4). Modifier groups are consulted in a fixed order (attacker unit, attacker tile, globals for
    /// dealing; target unit, target tile, globals for taking), each group sorted by ordinal id, so the
    /// breakdown a player sees is the same list the server computed, line for line. The same method serves
    /// the preview (a <see cref="Knowledge"/> limited to one player drops the lines they cannot know) and
    /// the actual resolution (<see cref="Knowledge.Full"/>). Pure: no state, no RNG.
    /// <para>The <paramref name="attack"/> it receives is the <b>resolved</b> definition (elements and tags added).</para>
    /// </summary>
    public sealed class DamageCalculator
    {
        private readonly ContentCatalog _catalog;
        private readonly List<ModifierDef> _scratch = new List<ModifierDef>();
        private readonly List<AbilityOverride> _overrideScratch = new List<AbilityOverride>();

        public DamageCalculator(ContentCatalog catalog)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        }

        public DamageBreakdown Compute(TileMap map, Unit attacker, IBody target, AttackDef attack, Knowledge knowledge)
        {
            if (map == null) throw new ArgumentNullException(nameof(map));
            if (attacker == null) throw new ArgumentNullException(nameof(attacker));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (attack == null) throw new ArgumentNullException(nameof(attack));

            Tile attackerTile, targetTile;
            map.TryGet(attacker.Position, out attackerTile);
            map.TryGet(target.Position, out targetTile);
            var situation = new Situation(attack, attackerTile, targetTile, SourceItemKindOf(attacker, attack));

            var lines = new List<DamageLine>();
            lines.Add(new DamageLine(DamageLineKind.Base, "base", DamageLineOwner.None, -1, attack.Damage, false));

            // Every boon the viewer has not been shown is one unknown, whatever it does — exactly as a hidden
            // modifier counts whether or not it applies — so a mirror, which does not hold hidden boons at
            // all, can add the same count back (ADR-026).
            int unknown = HiddenBoons(attacker, knowledge) + HiddenBoons(target as Unit, knowledge);

            string powerKey = StatBlock.PowerKey(attack.DamageType);
            int power = attacker.PublicStats.Get(powerKey);
            if (power != 0) lines.Add(new DamageLine(DamageLineKind.Power, powerKey, DamageLineOwner.Attacker, attacker.Id, power, false));
            AddBoonStats(lines, attacker, DamageLineOwner.Attacker, powerKey, 1, knowledge);

            string defenseKey = StatBlock.DefenseKey(attack.DamageType);
            int defense = target.PublicStats.Get(defenseKey);
            if (defense != 0) lines.Add(new DamageLine(DamageLineKind.Defense, defenseKey, DamageLineOwner.Target, target.Id, -defense, false));
            AddBoonStats(lines, target, DamageLineOwner.Target, defenseKey, -1, knowledge);

            // An Enchant's damage override on this attack: a line, never a change to the base (spec §6.5 (d)).
            attacker.Overlay.OverridesFor(attack.Id, _overrideScratch);
            for (int i = 0; i < _overrideScratch.Count; i++)
            {
                AbilityOverride o = _overrideScratch[i];
                if (o.Field != AbilityFields.Damage) continue;
                if (!knowledge.CanSeeBoon(attacker.Owner, attacker.Id, o.BoonId)) continue;
                lines.Add(new DamageLine(DamageLineKind.BoonStat, o.BoonId, DamageLineOwner.Attacker, attacker.Id, o.Amount, true));
            }

            Immunity immunity = default;

            // Dealing: the attacker's side.
            unknown += AddUnitModifiers(lines, attacker, DamageLineOwner.Attacker, ModifierTriggers.DealDamage, situation, knowledge, ref immunity);
            AddTileModifiers(lines, attackerTile, DamageLineOwner.AttackerTile, ModifierTriggers.DealDamage, situation, ref immunity);
            AddGlobalModifiers(lines, ModifierTriggers.DealDamage, situation, ref immunity);

            // Taking: the target's side.
            unknown += AddUnitModifiers(lines, target, DamageLineOwner.Target, ModifierTriggers.TakeDamage, situation, knowledge, ref immunity);
            AddTileModifiers(lines, targetTile, DamageLineOwner.TargetTile, ModifierTriggers.TakeDamage, situation, ref immunity);
            AddGlobalModifiers(lines, ModifierTriggers.TakeDamage, situation, ref immunity);

            // Immunity last: all flat, floor, then nullify (design: #modifiers rule 2). One line, taking the total to 0.
            if (immunity.Found)
            {
                int sum = 0;
                for (int i = 0; i < lines.Count; i++) sum += lines[i].Amount;
                if (sum < 0) sum = 0;
                lines.Add(new DamageLine(DamageLineKind.Nullify, immunity.ModifierId, immunity.Owner, immunity.OwnerUnitId, -sum, immunity.Hidden));
            }

            return new DamageBreakdown(lines, unknown);
        }

        /// <summary>One hidden line per boon contribution to <paramref name="key"/> the viewer can see.</summary>
        private static void AddBoonStats(List<DamageLine> lines, IBody body, DamageLineOwner owner, string key, int sign, Knowledge knowledge)
        {
            IReadOnlyList<StatContribution> contributions = body.BoonStatContributions(key);
            for (int i = 0; i < contributions.Count; i++)
            {
                StatContribution c = contributions[i];
                if (!knowledge.CanSeeBoon(body.Owner, body.Id, c.BoonId)) continue;
                lines.Add(new DamageLine(DamageLineKind.BoonStat, c.BoonId, owner, body.Id, sign * c.Amount, true));
            }
        }

        /// <summary>How many of a unit's boons the viewer has not been shown. 0 for a prop or full knowledge.</summary>
        private static int HiddenBoons(Unit unit, Knowledge knowledge)
        {
            if (unit == null || knowledge.IsFull) return 0;
            int hidden = 0;
            for (int i = 0; i < unit.BoonIds.Count; i++)
                if (!knowledge.CanSeeBoon(unit.Owner, unit.Id, unit.BoonIds[i])) hidden++;
            return hidden;
        }

        /// <summary>The first immunity that applied, in the fixed evaluation order.</summary>
        private struct Immunity
        {
            public bool Found;
            public string ModifierId;
            public DamageLineOwner Owner;
            public int OwnerUnitId;
            public bool Hidden;

            public void Take(ModifierDef def, DamageLineOwner owner, int ownerUnitId)
            {
                if (Found) return;
                Found = true;
                ModifierId = def.Id;
                Owner = owner;
                OwnerUnitId = ownerUnitId;
                Hidden = def.IsHidden;
            }
        }

        /// <summary>Whether a modifier's conditions hold for this attack, as if it were innate (no granting item). Public so tests and tools can ask directly.</summary>
        public static bool Applies(ModifierDef modifier, AttackDef attack, Tile attackerTile, Tile targetTile)
            => Applies(modifier, attack, attackerTile, targetTile, null);

        /// <summary>Whether a modifier's conditions hold for this attack when it was granted by an item of <paramref name="sourceItemKind"/> (null = innate).</summary>
        public static bool Applies(ModifierDef modifier, AttackDef attack, Tile attackerTile, Tile targetTile, string sourceItemKind)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (attack == null) throw new ArgumentNullException(nameof(attack));
            return new Situation(attack, attackerTile, targetTile, sourceItemKind).Satisfies(modifier);
        }

        /// <summary>The kind of the item that granted the attack to this unit, through the catalogue; null for an innate ability or an unknown item.</summary>
        private string SourceItemKindOf(Unit attacker, AttackDef attack)
        {
            string itemId = attacker.AbilitySourceOf(attack.Id);
            ItemDef item;
            return itemId != null && _catalog.Items.TryGet(itemId, out item) ? item.Kind : null;
        }

        private int AddUnitModifiers(List<DamageLine> lines, IBody unit, DamageLineOwner owner, string trigger, Situation situation, Knowledge knowledge, ref Immunity immunity)
        {
            int unknown = 0;
            CollectSorted(unit.ModifierIds, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                ModifierDef def = _scratch[i];
                bool visible = !def.IsHidden || knowledge.CanSee(unit.Owner, unit.Id, def.Id);
                if (!visible) unknown++;
                if (def.Trigger != trigger || !situation.Satisfies(def)) continue;
                if (!visible) continue;
                if (def.Nullify) { immunity.Take(def, owner, unit.Id); continue; }
                lines.Add(new DamageLine(DamageLineKind.Modifier, def.Id, owner, unit.Id, def.Damage, def.IsHidden));
            }
            return unknown;
        }

        private void AddTileModifiers(List<DamageLine> lines, Tile tile, DamageLineOwner owner, string trigger, Situation situation, ref Immunity immunity)
        {
            if (tile == null) return;
            var ids = new List<string>();
            TerrainDef terrain;
            if (_catalog.Terrains.TryGet(tile.Terrain, out terrain))
                for (int i = 0; i < terrain.ModifierIds.Count; i++) ids.Add(terrain.ModifierIds[i]);
            if (tile.EffectId != null && !ids.Contains(tile.EffectId)) ids.Add(tile.EffectId);
            CollectSorted(ids, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                ModifierDef def = _scratch[i];
                if (def.Trigger != trigger || !situation.Satisfies(def)) continue;
                if (def.Nullify) { immunity.Take(def, owner, -1); continue; }
                lines.Add(new DamageLine(DamageLineKind.Modifier, def.Id, owner, -1, def.Damage, false));
            }
        }

        private void AddGlobalModifiers(List<DamageLine> lines, string trigger, Situation situation, ref Immunity immunity)
        {
            CollectSorted(_catalog.Rules.GlobalModifierIds, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                ModifierDef def = _scratch[i];
                if (def.Trigger != trigger || !situation.Satisfies(def)) continue;
                if (def.Nullify) { immunity.Take(def, DamageLineOwner.Global, -1); continue; }
                lines.Add(new DamageLine(DamageLineKind.Modifier, def.Id, DamageLineOwner.Global, -1, def.Damage, false));
            }
        }

        /// <summary>Resolves ids to definitions (unknown ids are skipped: the catalogue already refused them) in ordinal id order.</summary>
        private void CollectSorted(IReadOnlyList<string> ids, List<ModifierDef> into)
        {
            into.Clear();
            for (int i = 0; i < ids.Count; i++)
            {
                ModifierDef def;
                if (_catalog.Modifiers.TryGet(ids[i], out def)) into.Add(def);
            }
            into.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>
        /// The facts a condition can ask about: the (resolved) attack, whether the attacker stands higher,
        /// and the kind of the item that granted the attack (null when innate). Every <c>when</c> key is one
        /// check here; a condition that names a list matches on <b>any</b> entry.
        /// </summary>
        private readonly struct Situation
        {
            private readonly AttackDef _attack;
            private readonly bool _heightAdvantage;
            private readonly string _sourceItemKind;

            public Situation(AttackDef attack, Tile attackerTile, Tile targetTile, string sourceItemKind)
            {
                _attack = attack;
                int attackerHeight = attackerTile != null ? attackerTile.Height : 0;
                int targetHeight = targetTile != null ? targetTile.Height : 0;
                _heightAdvantage = attackerHeight > targetHeight;
                _sourceItemKind = sourceItemKind;
            }

            public bool Satisfies(ModifierDef modifier)
            {
                if (modifier.HeightAdvantage.HasValue && modifier.HeightAdvantage.Value != _heightAdvantage) return false;
                if (modifier.DamageTypes.Count > 0)
                {
                    bool typeMatches = false;
                    for (int i = 0; i < modifier.DamageTypes.Count; i++)
                        if (modifier.DamageTypes[i] == _attack.DamageType) { typeMatches = true; break; }
                    if (!typeMatches) return false;
                }
                if (modifier.Tags.Count > 0)
                {
                    bool any = false;
                    for (int i = 0; i < modifier.Tags.Count; i++)
                        if (_attack.HasTag(modifier.Tags[i])) { any = true; break; }
                    if (!any) return false;
                }
                if (modifier.Elements.Count > 0)
                {
                    bool any = false;
                    for (int i = 0; i < modifier.Elements.Count; i++)
                        if (_attack.HasElement(modifier.Elements[i])) { any = true; break; }
                    if (!any) return false;
                }
                if (modifier.ItemKinds.Count > 0)
                {
                    // An innate ability has no item, so it never satisfies an item-kind condition.
                    if (_sourceItemKind == null) return false;
                    bool any = false;
                    for (int i = 0; i < modifier.ItemKinds.Count; i++)
                        if (modifier.ItemKinds[i] == _sourceItemKind) { any = true; break; }
                    if (!any) return false;
                }
                return true;
            }
        }
    }
}
