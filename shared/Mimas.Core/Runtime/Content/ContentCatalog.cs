using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Movement;

namespace Mimas.Core.Content
{
    /// <summary>
    /// Every definition the rules can reference, loaded once per process from the JSON data folder and
    /// immutable afterwards. The single place code looks up an id.
    ///
    /// Loading is two-phase. Phase 1 parses each file on its own and records errors against that file.
    /// Phase 2 links: item and innate ability ids must exist, a weapon must grant an attack and only
    /// weapon-category ones (a crown only spells), item and base stat keys must name declared damage types, attack damage types must be declared, movement terrain
    /// overrides must name real terrains, terrain and map-hex modifiers and global modifiers must exist,
    /// every map must build (symmetry, spawns, connectivity). All errors are collected and thrown together as one
    /// <see cref="ContentLoadException"/>. Unknown files are errors too: the catalogue fails closed rather
    /// than silently ignoring a misplaced definition.
    ///
    /// Tables are sorted by id, so indices and <see cref="Hash"/> are identical on every peer that loads
    /// the same files, whatever order the files arrived in.
    /// </summary>
    public sealed class ContentCatalog
    {
        public const string TerrainsFile = "terrains.json";
        public const string TimeControlsFile = "timecontrols.json";
        public const string RulesFile = "rules.json";
        public const string AbilitiesFolder = "abilities/";
        public const string ItemsFolder = "items/";
        public const string MapsFolder = "maps/";
        public const string ModifiersFolder = "modifiers/";
        public const string PropsFolder = "props/";

        public RulesDef Rules { get; }
        public TerrainSet Terrains { get; }
        public DefinitionTable<AbilityDef> Abilities { get; }
        public DefinitionTable<ItemDef> Items { get; }
        public DefinitionTable<MapData> Maps { get; }
        public DefinitionTable<TimeControlDef> TimeControls { get; }
        public DefinitionTable<ModifierDef> Modifiers { get; }

        /// <summary>The prop catalogue (<c>props/*.json</c>): what a map hex's <c>prop</c> field may name.</summary>
        public DefinitionTable<PropDef> Props { get; }

        /// <summary>The movement abilities, keyed by id, for callers that only care about movement.</summary>
        public MovementDefSet Movements { get; }

        /// <summary>SHA-256 hex of the canonical content (see <see cref="ContentHash"/>). Compare across peers.</summary>
        public string Hash { get; }

        /// <summary>Paths of every file that went into this catalogue, sorted.</summary>
        public IReadOnlyList<string> Files { get; }

        private ContentCatalog(
            RulesDef rules,
            TerrainSet terrains,
            DefinitionTable<AbilityDef> abilities,
            DefinitionTable<ItemDef> items,
            DefinitionTable<MapData> maps,
            DefinitionTable<TimeControlDef> timeControls,
            DefinitionTable<ModifierDef> modifiers,
            DefinitionTable<PropDef> props,
            MovementDefSet movements,
            string hash,
            IReadOnlyList<string> files)
        {
            Rules = rules;
            Terrains = terrains;
            Abilities = abilities;
            Items = items;
            Maps = maps;
            TimeControls = timeControls;
            Modifiers = modifiers;
            Props = props;
            Movements = movements;
            Hash = hash;
            Files = files;
        }

        /// <summary>The movement ability with this id, or null when the id is unknown or not a movement.</summary>
        public MovementDef GetMovement(string id)
        {
            AbilityDef def;
            return Abilities.TryGet(id, out def) ? def as MovementDef : null;
        }

        /// <summary>The attack ability with this id, or null when the id is unknown or not an attack.</summary>
        public AttackDef GetAttack(string id)
        {
            AbilityDef def;
            return Abilities.TryGet(id, out def) ? def as AttackDef : null;
        }

        /// <summary>The item, or throws ArgumentException naming the slot when the id is unknown or in the wrong slot.</summary>
        public ItemDef GetItemForSlot(string slot, string itemId)
        {
            ItemDef def;
            if (!Items.TryGet(itemId, out def))
                throw new ArgumentException($"Unknown item '{itemId}' for slot '{slot}'.", nameof(itemId));
            if (def.Slot != slot)
                throw new ArgumentException($"Item '{itemId}' fills slot '{def.Slot}', not '{slot}'.", nameof(itemId));
            return def;
        }

        /// <summary>Loads and links a content set. Throws <see cref="ContentLoadException"/> listing every problem.</summary>
        public static ContentCatalog Load(IEnumerable<ContentFile> files)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));

            var errors = new List<ContentError>();
            var sorted = new List<ContentFile>(files);
            sorted.Sort((a, b) => string.CompareOrdinal(a.Path, b.Path));

            var seenPaths = new HashSet<string>(StringComparer.Ordinal);
            var paths = new List<string>(sorted.Count);
            foreach (var f in sorted)
            {
                if (!seenPaths.Add(f.Path)) errors.Add(new ContentError(f.Path, "duplicate content path."));
                else paths.Add(f.Path);
            }

            // ---- Phase 1: parse every file independently ---------------------------------------------
            RulesDef rules = null;
            TerrainSet terrains = null;
            var abilities = new List<AbilityDef>();
            var items = new List<ItemDef>();
            var maps = new List<MapData>();
            var modifiers = new List<ModifierDef>();
            var props = new List<PropDef>();
            var timeControls = new List<TimeControlDef>();
            var abilityFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var itemFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var mapFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var modifierFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var propFiles = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var f in sorted)
            {
                string path = f.Path;
                try
                {
                    if (!path.EndsWith(".json", StringComparison.Ordinal))
                    {
                        errors.Add(new ContentError(path, "only .json files belong in the data folder."));
                    }
                    else if (string.Equals(path, TerrainsFile, StringComparison.Ordinal))
                    {
                        terrains = TerrainSet.FromJson(f.Text);
                    }
                    else if (string.Equals(path, RulesFile, StringComparison.Ordinal))
                    {
                        rules = RulesDef.FromJson(f.Text);
                    }
                    else if (string.Equals(path, TimeControlsFile, StringComparison.Ordinal))
                    {
                        timeControls.AddRange(TimeControlDef.ListFromJson(f.Text));
                    }
                    else if (path.StartsWith(AbilitiesFolder, StringComparison.Ordinal))
                    {
                        var def = AbilityDef.FromJson(f.Text);
                        if (!RegisterId(abilityFiles, def.Id, path, "ability", errors)) continue;
                        abilities.Add(def);
                    }
                    else if (path.StartsWith(ItemsFolder, StringComparison.Ordinal))
                    {
                        var def = ItemDef.FromJson(f.Text);
                        if (!RegisterId(itemFiles, def.Id, path, "item", errors)) continue;
                        items.Add(def);
                    }
                    else if (path.StartsWith(MapsFolder, StringComparison.Ordinal))
                    {
                        var def = MapData.FromJson(f.Text);
                        if (!RegisterId(mapFiles, def.Id, path, "map", errors)) continue;
                        maps.Add(def);
                    }
                    else if (path.StartsWith(ModifiersFolder, StringComparison.Ordinal))
                    {
                        var def = ModifierDef.FromJson(f.Text);
                        if (!RegisterId(modifierFiles, def.Id, path, "modifier", errors)) continue;
                        modifiers.Add(def);
                    }
                    else if (path.StartsWith(PropsFolder, StringComparison.Ordinal))
                    {
                        var def = PropDef.FromJson(f.Text);
                        if (!RegisterId(propFiles, def.Id, path, "prop", errors)) continue;
                        props.Add(def);
                    }
                    else
                    {
                        errors.Add(new ContentError(path, "unrecognised content file: expected terrains.json, rules.json, timecontrols.json, or a file under abilities/, items/, maps/, modifiers/ or props/."));
                    }
                }
                catch (MapLoadException e)
                {
                    errors.Add(new ContentError(path, e.Message));
                }
            }

            if (terrains == null) errors.Add(new ContentError(TerrainsFile, "missing: the terrain catalogue is required."));
            if (rules == null) errors.Add(new ContentError(RulesFile, "missing: the rules file is required."));

            // ---- Phase 2: link cross references -----------------------------------------------------
            var modifierIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var m in modifiers) modifierIds.Add(m.Id);

            if (terrains != null)
            {
                foreach (var ability in abilities)
                {
                    var movement = ability as MovementDef;
                    if (movement == null) continue;
                    foreach (string terrainId in movement.OverriddenTerrainIds)
                    {
                        TerrainDef unused;
                        if (!terrains.TryGet(terrainId, out unused))
                            errors.Add(new ContentError(abilityFiles[ability.Id], $"movement '{ability.Id}' overrides unknown terrain '{terrainId}'."));
                    }
                }

                foreach (var terrain in terrains.All)
                {
                    foreach (string modifierId in terrain.ModifierIds)
                    {
                        if (!modifierIds.Contains(modifierId))
                            errors.Add(new ContentError(TerrainsFile, $"terrain '{terrain.Id}' references unknown modifier '{modifierId}'."));
                    }
                }

                var propTable = new DefinitionTable<PropDef>(props);
                foreach (var map in maps)
                {
                    try
                    {
                        map.BuildTileMap(terrains);
                    }
                    catch (MapLoadException e)
                    {
                        errors.Add(new ContentError(mapFiles[map.Id], e.Message));
                    }

                    try
                    {
                        map.ValidateProps(propTable, terrains);
                    }
                    catch (MapLoadException e)
                    {
                        errors.Add(new ContentError(mapFiles[map.Id], e.Message));
                    }

                    foreach (var hex in map.Hexes)
                    {
                        if (hex.EffectId != null && !modifierIds.Contains(hex.EffectId))
                            errors.Add(new ContentError(mapFiles[map.Id], $"map '{map.Id}' hex {hex.Position} references unknown effect modifier '{hex.EffectId}'."));
                    }
                }
            }

            if (rules != null)
            {
                foreach (string modifierId in rules.GlobalModifierIds)
                {
                    if (!modifierIds.Contains(modifierId))
                        errors.Add(new ContentError(RulesFile, $"rules reference unknown global modifier '{modifierId}'."));
                }

                foreach (var ability in abilities)
                {
                    var attack = ability as AttackDef;
                    if (attack == null) continue;
                    if (!rules.IsDamageType(attack.DamageType))
                        errors.Add(new ContentError(abilityFiles[ability.Id], $"attack '{ability.Id}' uses undeclared damage type '{attack.DamageType}' (rules.json damageTypes: {string.Join(", ", rules.DamageTypes)})."));
                    foreach (string element in attack.Elements)
                    {
                        if (!rules.IsElement(element))
                            errors.Add(new ContentError(abilityFiles[ability.Id], $"attack '{ability.Id}' carries undeclared element '{element}' (rules.json elements: {string.Join(", ", rules.Elements)})."));
                    }
                }

                foreach (var item in items)
                {
                    foreach (var entry in item.Stats.Entries)
                    {
                        string type = StatBlock.DamageTypeOf(entry.Key);
                        if (type != null && !rules.IsDamageType(type))
                            errors.Add(new ContentError(itemFiles[item.Id], $"item '{item.Id}' stat '{entry.Key}' uses undeclared damage type '{type}'."));
                    }
                }

                foreach (var entry in rules.BaseStats.Entries)
                {
                    string type = StatBlock.DamageTypeOf(entry.Key);
                    if (type != null && !rules.IsDamageType(type))
                        errors.Add(new ContentError(RulesFile, $"rules.baseStats '{entry.Key}' uses undeclared damage type '{type}'."));
                }

                foreach (var prop in props)
                {
                    foreach (var entry in prop.Stats.Entries)
                    {
                        string type = StatBlock.DamageTypeOf(entry.Key);
                        if (type != null && !rules.IsDamageType(type))
                            errors.Add(new ContentError(propFiles[prop.Id], $"prop '{prop.Id}' stat '{entry.Key}' uses undeclared damage type '{type}'."));
                    }
                }

                foreach (var modifier in modifiers)
                {
                    foreach (string type in modifier.DamageTypes)
                    {
                        if (!rules.IsDamageType(type))
                            errors.Add(new ContentError(modifierFiles[modifier.Id], $"modifier '{modifier.Id}' conditions on undeclared damage type '{type}'."));
                    }
                    foreach (string element in modifier.Elements)
                    {
                        if (!rules.IsElement(element))
                            errors.Add(new ContentError(modifierFiles[modifier.Id], $"modifier '{modifier.Id}' conditions on undeclared element '{element}' (rules.json elements: {string.Join(", ", rules.Elements)})."));
                    }
                }
            }

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in abilities) abilityIds.Add(a.Id);
            var movementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in abilities) if (a is MovementDef) movementIds.Add(a.Id);
            var attacksById = new Dictionary<string, AttackDef>(StringComparer.Ordinal);
            foreach (var a in abilities) { var attack = a as AttackDef; if (attack != null) attacksById[attack.Id] = attack; }

            if (rules != null)
            {
                bool innateMovement = false;
                foreach (string abilityId in rules.InnateAbilityIds)
                {
                    if (!abilityIds.Contains(abilityId))
                        errors.Add(new ContentError(RulesFile, $"rules reference unknown innate ability '{abilityId}'."));
                    else if (movementIds.Contains(abilityId))
                        innateMovement = true;
                }
                if (!innateMovement)
                    errors.Add(new ContentError(RulesFile, "rules.innateAbilities must include at least one movement ability (usually 'move')."));
            }

            foreach (var item in items)
            {
                // A weapon's attacks are weapon-lane, a crown's are spells (design: #attacks). Other slots
                // grant no attacks today, so nothing is asserted about them.
                string requiredCategory = null;
                if (item.Slot == ItemSlots.Weapon) requiredCategory = AbilityCategories.Weapon;
                else if (item.Slot == ItemSlots.Crown) requiredCategory = AbilityCategories.Spell;

                bool hasAttack = false;
                foreach (string abilityId in item.AbilityIds)
                {
                    if (!abilityIds.Contains(abilityId))
                    {
                        errors.Add(new ContentError(itemFiles[item.Id], $"item '{item.Id}' references unknown ability '{abilityId}'."));
                        continue;
                    }
                    if (rules != null && rules.IsInnateAbility(abilityId))
                        errors.Add(new ContentError(itemFiles[item.Id], $"item '{item.Id}' grants '{abilityId}', which is already innate (rules.innateAbilities)."));

                    AttackDef attack;
                    if (!attacksById.TryGetValue(abilityId, out attack)) continue;
                    hasAttack = true;
                    if (requiredCategory != null && attack.Category != requiredCategory)
                        errors.Add(new ContentError(itemFiles[item.Id], $"item '{item.Id}' is a {item.Slot} but grants attack '{abilityId}' with category '{attack.Category}'; a {item.Slot} may only grant '{requiredCategory}' attacks."));
                }
                if (item.Slot == ItemSlots.Weapon && !hasAttack)
                    errors.Add(new ContentError(itemFiles[item.Id], $"item '{item.Id}' is a weapon but grants no attack ability."));
            }

            if (errors.Count > 0) throw new ContentLoadException(errors);

            var movements = new MovementDefSet();
            var sortedAbilities = new DefinitionTable<AbilityDef>(abilities);
            foreach (var a in sortedAbilities.All)
            {
                var m = a as MovementDef;
                if (m != null) movements.Add(m);
            }

            return new ContentCatalog(
                rules,
                terrains,
                sortedAbilities,
                new DefinitionTable<ItemDef>(items),
                new DefinitionTable<MapData>(maps),
                new DefinitionTable<TimeControlDef>(timeControls),
                new DefinitionTable<ModifierDef>(modifiers),
                new DefinitionTable<PropDef>(props),
                movements,
                ContentHash.Compute(sorted),
                paths);
        }

        private static bool RegisterId(Dictionary<string, string> filesById, string id, string path, string kind, List<ContentError> errors)
        {
            string other;
            if (filesById.TryGetValue(id, out other))
            {
                errors.Add(new ContentError(path, $"duplicate {kind} id '{id}' (also defined in {other})."));
                return false;
            }
            filesById[id] = path;
            return true;
        }
    }
}
