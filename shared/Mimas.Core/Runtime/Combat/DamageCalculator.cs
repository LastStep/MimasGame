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
    /// <c>base + power.&lt;type&gt; − defense.&lt;type&gt; + Σ flat modifiers</c>, floored at 0.
    /// Modifier groups are consulted in a fixed order (attacker unit, attacker tile, globals for dealing;
    /// target unit, target tile, globals for taking), each group sorted by ordinal id, so the breakdown a
    /// player sees is the same list the server computed, line for line. The same method serves the
    /// preview (a <see cref="Knowledge"/> limited to one player drops the lines they cannot know) and the
    /// actual resolution (<see cref="Knowledge.Full"/>). Pure: no state, no RNG.
    /// </summary>
    public sealed class DamageCalculator
    {
        private readonly ContentCatalog _catalog;
        private readonly List<ModifierDef> _scratch = new List<ModifierDef>();

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
            var situation = new Situation(attack, attackerTile, targetTile);

            var lines = new List<DamageLine>();
            lines.Add(new DamageLine(DamageLineKind.Base, "base", DamageLineOwner.None, -1, attack.Damage, false));

            string powerKey = StatBlock.PowerKey(attack.DamageType);
            int power = attacker.Stats.Get(powerKey);
            if (power != 0) lines.Add(new DamageLine(DamageLineKind.Power, powerKey, DamageLineOwner.Attacker, attacker.Id, power, false));

            string defenseKey = StatBlock.DefenseKey(attack.DamageType);
            int defense = target.Stats.Get(defenseKey);
            if (defense != 0) lines.Add(new DamageLine(DamageLineKind.Defense, defenseKey, DamageLineOwner.Target, target.Id, -defense, false));

            int unknown = 0;

            // Dealing: the attacker's side.
            unknown += AddUnitModifiers(lines, attacker, DamageLineOwner.Attacker, ModifierTriggers.DealDamage, situation, knowledge);
            AddTileModifiers(lines, attackerTile, DamageLineOwner.AttackerTile, ModifierTriggers.DealDamage, situation);
            AddGlobalModifiers(lines, ModifierTriggers.DealDamage, situation);

            // Taking: the target's side.
            unknown += AddUnitModifiers(lines, target, DamageLineOwner.Target, ModifierTriggers.TakeDamage, situation, knowledge);
            AddTileModifiers(lines, targetTile, DamageLineOwner.TargetTile, ModifierTriggers.TakeDamage, situation);
            AddGlobalModifiers(lines, ModifierTriggers.TakeDamage, situation);

            return new DamageBreakdown(lines, unknown);
        }

        /// <summary>Whether a modifier's conditions hold for this attack. Public so tests and tools can ask directly.</summary>
        public static bool Applies(ModifierDef modifier, AttackDef attack, Tile attackerTile, Tile targetTile)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            if (attack == null) throw new ArgumentNullException(nameof(attack));
            return new Situation(attack, attackerTile, targetTile).Satisfies(modifier);
        }

        private int AddUnitModifiers(List<DamageLine> lines, IBody unit, DamageLineOwner owner, string trigger, Situation situation, Knowledge knowledge)
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
                lines.Add(new DamageLine(DamageLineKind.Modifier, def.Id, owner, unit.Id, def.Damage, def.IsHidden));
            }
            return unknown;
        }

        private void AddTileModifiers(List<DamageLine> lines, Tile tile, DamageLineOwner owner, string trigger, Situation situation)
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
                lines.Add(new DamageLine(DamageLineKind.Modifier, def.Id, owner, -1, def.Damage, false));
            }
        }

        private void AddGlobalModifiers(List<DamageLine> lines, string trigger, Situation situation)
        {
            CollectSorted(_catalog.Rules.GlobalModifierIds, _scratch);
            for (int i = 0; i < _scratch.Count; i++)
            {
                ModifierDef def = _scratch[i];
                if (def.Trigger != trigger || !situation.Satisfies(def)) continue;
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

        private readonly struct Situation
        {
            private readonly AttackDef _attack;
            private readonly bool _heightAdvantage;

            public Situation(AttackDef attack, Tile attackerTile, Tile targetTile)
            {
                _attack = attack;
                int attackerHeight = attackerTile != null ? attackerTile.Height : 0;
                int targetHeight = targetTile != null ? targetTile.Height : 0;
                _heightAdvantage = attackerHeight > targetHeight;
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
                return true;
            }
        }
    }
}
