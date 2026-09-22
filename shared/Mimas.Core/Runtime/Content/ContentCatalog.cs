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
        public const string BoonsFolder = "boons/";
        public const string LineagesFolder = "lineages/";

        public RulesDef Rules { get; }
        public TerrainSet Terrains { get; }
        public DefinitionTable<AbilityDef> Abilities { get; }
        public DefinitionTable<ItemDef> Items { get; }
        public DefinitionTable<MapData> Maps { get; }
        public DefinitionTable<TimeControlDef> TimeControls { get; }
        public DefinitionTable<ModifierDef> Modifiers { get; }

        /// <summary>The prop catalogue (<c>props/*.json</c>): what a map hex's <c>prop</c> field may name.</summary>
        public DefinitionTable<PropDef> Props { get; }

        /// <summary>Every boon (<c>boons/*.json</c>, design: #boons).</summary>
        public DefinitionTable<BoonDef> Boons { get; }

        /// <summary>Every lineage (<c>lineages/*.json</c>, design: #lineage).</summary>
        public DefinitionTable<LineageDef> Lineages { get; }

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
            DefinitionTable<BoonDef> boons,
            DefinitionTable<LineageDef> lineages,
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
            Boons = boons;
            Lineages = lineages;
            Movements = movements;
            Hash = hash;
            Files = files;
        }

        /// <summary>The boon, or throws ArgumentException when the id is unknown.</summary>
        public BoonDef GetBoon(string boonId)
        {
            BoonDef def;
            if (!Boons.TryGet(boonId, out def)) throw new ArgumentException($"Unknown boon '{boonId}'.", nameof(boonId));
            return def;
        }

        /// <summary>The lineage, or throws ArgumentException when the id is unknown.</summary>
        public LineageDef GetLineage(string lineageId)
        {
            LineageDef def;
            if (!Lineages.TryGet(lineageId, out def)) throw new ArgumentException($"Unknown lineage '{lineageId}'.", nameof(lineageId));
            return def;
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
            var boons = new List<BoonDef>();
            var lineages = new List<LineageDef>();
            var timeControls = new List<TimeControlDef>();
            var abilityFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var itemFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var mapFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var modifierFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var propFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var boonFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var lineageFiles = new Dictionary<string, string>(StringComparer.Ordinal);

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
                    else if (path.StartsWith(BoonsFolder, StringComparison.Ordinal))
                    {
                        var def = BoonDef.FromJson(f.Text);
                        if (!RegisterId(boonFiles, def.Id, path, "boon", errors)) continue;
                        boons.Add(def);
                    }
                    else if (path.StartsWith(LineagesFolder, StringComparison.Ordinal))
                    {
                        var def = LineageDef.FromJson(f.Text);
                        if (!RegisterId(lineageFiles, def.Id, path, "lineage", errors)) continue;
                        lineages.Add(def);
                    }
                    else
                    {
                        errors.Add(new ContentError(path, "unrecognised content file: expected terrains.json, rules.json, timecontrols.json, or a file under abilities/, items/, maps/, modifiers/, props/, boons/ or lineages/."));
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

            LinkBoonsAndLineages(rules, abilities, items, modifierIds, boons, lineages, boonFiles, lineageFiles, errors);

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
                new DefinitionTable<BoonDef>(boons),
                new DefinitionTable<LineageDef>(lineages),
                movements,
                ContentHash.Compute(sorted),
                paths);
        }

        /// <summary>
        /// The link rules of spec D part 1 §5.4–5.6 (design: #boons, #lineage). Every error names the file.
        /// Ability-id targets and grants are checked against the abilities the target slot can name: the
        /// abilities of every item in that slot (of the required kind, if any) plus what a Sigil of the same
        /// lineage grants on that slot, so an Enchant on a Sigil's spell is legal content.
        /// </summary>
        private static void LinkBoonsAndLineages(RulesDef rules, List<AbilityDef> abilities, List<ItemDef> items, HashSet<string> modifierIds,
            List<BoonDef> boons, List<LineageDef> lineages, Dictionary<string, string> boonFiles, Dictionary<string, string> lineageFiles, List<ContentError> errors)
        {
            var abilitiesById = new Dictionary<string, AbilityDef>(StringComparer.Ordinal);
            foreach (var a in abilities) abilitiesById[a.Id] = a;
            var boonsById = new Dictionary<string, BoonDef>(StringComparer.Ordinal);
            foreach (var b in boons) boonsById[b.Id] = b;
            var lineageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var l in lineages) lineageIds.Add(l.Id);

            foreach (var boon in boons)
            {
                string file = boonFiles[boon.Id];
                if (!lineageIds.Contains(boon.LineageId))
                    errors.Add(new ContentError(file, $"boon '{boon.Id}' belongs to unknown lineage '{boon.LineageId}'."));

                if (boon.Requires != null && boon.Requires.Kind != null)
                {
                    bool anyItem = false;
                    foreach (var item in items)
                        if (boon.Requires.IsMetBy(item.Slot, item.Kind)) { anyItem = true; break; }
                    if (!anyItem)
                        errors.Add(new ContentError(file, $"boon '{boon.Id}' requires a {boon.Requires.Slot} of kind '{boon.Requires.Kind}', which no item is."));
                }

                for (int i = 0; i < boon.Effects.Count; i++)
                {
                    BoonEffect effect = boon.Effects[i];
                    string at = $"boon '{boon.Id}' effects[{i}]";
                    switch (effect.Type)
                    {
                        case BoonEffectTypes.Stat:
                        {
                            string lane = StatBlock.DamageTypeOf(effect.Key);
                            if (rules != null && lane != null && !rules.IsDamageType(lane))
                                errors.Add(new ContentError(file, $"{at}: stat '{effect.Key}' uses undeclared damage type '{lane}'."));
                            break;
                        }
                        case BoonEffectTypes.Modifier:
                            if (!modifierIds.Contains(effect.Id))
                                errors.Add(new ContentError(file, $"{at}: unknown modifier '{effect.Id}'."));
                            break;
                        case BoonEffectTypes.AddElement:
                            if (rules != null && !rules.IsElement(effect.Element))
                                errors.Add(new ContentError(file, $"{at}: undeclared element '{effect.Element}' (rules.json elements: {string.Join(", ", rules.Elements)})."));
                            break;
                        case BoonEffectTypes.AddTag:
                            break;
                        case BoonEffectTypes.GrantAbility:
                        {
                            AbilityDef granted;
                            if (!abilitiesById.TryGetValue(effect.Ability, out granted))
                            {
                                errors.Add(new ContentError(file, $"{at}: unknown ability '{effect.Ability}'."));
                                break;
                            }
                            if (rules != null && rules.IsInnateAbility(effect.Ability))
                                errors.Add(new ContentError(file, $"{at}: grants '{effect.Ability}', which is already innate (rules.innateAbilities)."));
                            string requiredCategory = RequiredAttackCategory(effect.Target);
                            var attack = granted as AttackDef;
                            if (attack != null && requiredCategory != null && attack.Category != requiredCategory)
                                errors.Add(new ContentError(file, $"{at}: a sigil on the {effect.Target} may only grant '{requiredCategory}' attacks, but '{effect.Ability}' is '{attack.Category}'."));
                            break;
                        }
                        default: // abilityOverride
                        {
                            if (boon.IsBlessing)
                            {
                                AbilityDef innate;
                                if (!abilitiesById.TryGetValue(effect.Target, out innate))
                                    errors.Add(new ContentError(file, $"{at}: unknown ability '{effect.Target}'."));
                                else if (rules != null && !rules.IsInnateAbility(effect.Target))
                                    errors.Add(new ContentError(file, $"{at}: a blessing may only override an innate ability, and '{effect.Target}' is not one (rules.innateAbilities)."));
                                else if (AbilityFields.IsNumeric(effect.Field) && !AbilityFields.AppliesTo(effect.Field, innate))
                                    errors.Add(new ContentError(file, $"{at}: field '{effect.Field}' does not apply to '{effect.Target}'."));
                                break;
                            }

                            // An enchant: the target is its slot, or one ability that slot can name.
                            var reachable = new List<AbilityDef>();
                            AbilitiesOfSlot(boon.Requires, boon.LineageId, items, boons, abilitiesById, reachable);
                            if (effect.TargetIsSlot)
                            {
                                bool anyApplies = false;
                                for (int k = 0; k < reachable.Count; k++)
                                    if (AbilityFields.AppliesTo(effect.Field, reachable[k])) { anyApplies = true; break; }
                                if (!anyApplies)
                                    errors.Add(new ContentError(file, $"{at}: field '{effect.Field}' applies to no ability the {boon.Requires} can carry."));
                                break;
                            }

                            AbilityDef named;
                            if (!abilitiesById.TryGetValue(effect.Target, out named))
                            {
                                errors.Add(new ContentError(file, $"{at}: unknown ability '{effect.Target}'."));
                                break;
                            }
                            bool granted = false;
                            for (int k = 0; k < reachable.Count; k++) if (reachable[k].Id == named.Id) { granted = true; break; }
                            if (!granted)
                                errors.Add(new ContentError(file, $"{at}: '{effect.Target}' is not granted by any {boon.Requires} item or by a sigil of lineage '{boon.LineageId}' on that slot."));
                            else if (!AbilityFields.AppliesTo(effect.Field, named))
                                errors.Add(new ContentError(file, $"{at}: field '{effect.Field}' does not apply to '{effect.Target}'."));
                            break;
                        }
                    }
                }
            }

            foreach (var lineage in lineages)
            {
                string file = lineageFiles[lineage.Id];
                BoonDef starting;
                if (!boonsById.TryGetValue(lineage.StartingBlessingId, out starting))
                    errors.Add(new ContentError(file, $"lineage '{lineage.Id}' starting Blessing '{lineage.StartingBlessingId}' does not exist."));
                else if (!starting.IsBlessing)
                    errors.Add(new ContentError(file, $"lineage '{lineage.Id}' starting Blessing '{lineage.StartingBlessingId}' is a {starting.Kind}, not a blessing."));
                else if (starting.LineageId != lineage.Id)
                    errors.Add(new ContentError(file, $"lineage '{lineage.Id}' starting Blessing '{lineage.StartingBlessingId}' belongs to lineage '{starting.LineageId}'."));

                var pool = new List<BoonDef>();
                foreach (string boonId in lineage.PoolIds)
                {
                    BoonDef boon;
                    if (!boonsById.TryGetValue(boonId, out boon))
                    {
                        errors.Add(new ContentError(file, $"lineage '{lineage.Id}' pool names unknown boon '{boonId}'."));
                        continue;
                    }
                    if (boon.LineageId != lineage.Id)
                        errors.Add(new ContentError(file, $"lineage '{lineage.Id}' pool names '{boonId}', which belongs to lineage '{boon.LineageId}'."));
                    pool.Add(boon);
                }

                foreach (string kind in BoonKinds.All)
                {
                    bool any = false;
                    foreach (var boon in pool) if (boon.Kind == kind) { any = true; break; }
                    if (!any) errors.Add(new ContentError(file, $"lineage '{lineage.Id}' pool has no {kind}; every kind must be offerable."));
                }

                // "No gear choice makes a lineage empty", read per item (design: #lineage rule 2).
                foreach (var item in items)
                {
                    bool covered = false;
                    foreach (var boon in pool)
                    {
                        if (boon.IsBlessing) continue;
                        if (boon.IsApplicableTo(item.Slot, item.Kind, item.AbilityIds)) { covered = true; break; }
                    }
                    if (!covered)
                        errors.Add(new ContentError(file, $"lineage '{lineage.Id}' pool has no enchant or sigil applicable to item '{item.Id}' ({item.Slot}, {item.Kind})."));
                }
            }
        }

        /// <summary>The lane rule the item loader enforces, for a granted attack: weapon slot → weapon attacks, crown → spells, other slots → any.</summary>
        private static string RequiredAttackCategory(string slot)
        {
            if (slot == ItemSlots.Weapon) return AbilityCategories.Weapon;
            if (slot == ItemSlots.Crown) return AbilityCategories.Spell;
            return null;
        }

        /// <summary>Every ability an item meeting <paramref name="requires"/> grants, plus what a sigil of <paramref name="lineageId"/> grants on that slot.</summary>
        private static void AbilitiesOfSlot(BoonRequirement requires, string lineageId, List<ItemDef> items, List<BoonDef> boons,
            Dictionary<string, AbilityDef> abilitiesById, List<AbilityDef> into)
        {
            foreach (var item in items)
            {
                if (!requires.IsMetBy(item.Slot, item.Kind)) continue;
                foreach (string abilityId in item.AbilityIds)
                {
                    AbilityDef def;
                    if (abilitiesById.TryGetValue(abilityId, out def) && !into.Contains(def)) into.Add(def);
                }
            }
            foreach (var boon in boons)
            {
                if (!boon.IsSigil || boon.LineageId != lineageId || boon.Requires.Slot != requires.Slot) continue;
                foreach (var effect in boon.Effects)
                {
                    AbilityDef def;
                    if (effect.Type == BoonEffectTypes.GrantAbility && abilitiesById.TryGetValue(effect.Ability, out def) && !into.Contains(def)) into.Add(def);
                }
            }
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
