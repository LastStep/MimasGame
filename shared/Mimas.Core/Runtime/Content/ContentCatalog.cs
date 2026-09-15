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
    /// Phase 2 links: class ability ids must exist, class stat keys must name declared damage types,
    /// attack damage types must be declared, movement terrain overrides must name real terrains, terrain
    /// and map-hex modifiers and global modifiers must exist, every map must build (symmetry, spawns,
    /// connectivity). All errors are collected and thrown together as one
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
        public const string ClassesFolder = "classes/";
        public const string MapsFolder = "maps/";
        public const string ModifiersFolder = "modifiers/";

        public RulesDef Rules { get; }
        public TerrainSet Terrains { get; }
        public DefinitionTable<AbilityDef> Abilities { get; }
        public DefinitionTable<ClassDef> Classes { get; }
        public DefinitionTable<MapData> Maps { get; }
        public DefinitionTable<TimeControlDef> TimeControls { get; }
        public DefinitionTable<ModifierDef> Modifiers { get; }

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
            DefinitionTable<ClassDef> classes,
            DefinitionTable<MapData> maps,
            DefinitionTable<TimeControlDef> timeControls,
            DefinitionTable<ModifierDef> modifiers,
            MovementDefSet movements,
            string hash,
            IReadOnlyList<string> files)
        {
            Rules = rules;
            Terrains = terrains;
            Abilities = abilities;
            Classes = classes;
            Maps = maps;
            TimeControls = timeControls;
            Modifiers = modifiers;
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
            var classes = new List<ClassDef>();
            var maps = new List<MapData>();
            var modifiers = new List<ModifierDef>();
            var timeControls = new List<TimeControlDef>();
            var abilityFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var classFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var mapFiles = new Dictionary<string, string>(StringComparer.Ordinal);
            var modifierFiles = new Dictionary<string, string>(StringComparer.Ordinal);

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
                    else if (path.StartsWith(ClassesFolder, StringComparison.Ordinal))
                    {
                        var def = ClassDef.FromJson(f.Text);
                        if (!RegisterId(classFiles, def.Id, path, "class", errors)) continue;
                        classes.Add(def);
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
                    else
                    {
                        errors.Add(new ContentError(path, "unrecognised content file: expected terrains.json, rules.json, timecontrols.json, or a file under abilities/, classes/, maps/ or modifiers/."));
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
                    if (attack != null && !rules.IsDamageType(attack.DamageType))
                        errors.Add(new ContentError(abilityFiles[ability.Id], $"attack '{ability.Id}' uses undeclared damage type '{attack.DamageType}' (rules.json damageTypes: {string.Join(", ", rules.DamageTypes)})."));
                }

                foreach (var cls in classes)
                {
                    foreach (var entry in cls.Stats.Entries)
                    {
                        string type = StatBlock.DamageTypeOf(entry.Key);
                        if (type != null && !rules.IsDamageType(type))
                            errors.Add(new ContentError(classFiles[cls.Id], $"class '{cls.Id}' stat '{entry.Key}' uses undeclared damage type '{type}'."));
                    }
                }

                foreach (var modifier in modifiers)
                {
                    foreach (string type in modifier.DamageTypes)
                    {
                        if (!rules.IsDamageType(type))
                            errors.Add(new ContentError(modifierFiles[modifier.Id], $"modifier '{modifier.Id}' conditions on undeclared damage type '{type}'."));
                    }
                }
            }

            var abilityIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in abilities) abilityIds.Add(a.Id);
            var movementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var a in abilities) if (a is MovementDef) movementIds.Add(a.Id);

            foreach (var cls in classes)
            {
                bool hasMovement = false;
                foreach (string abilityId in cls.AbilityIds)
                {
                    if (!abilityIds.Contains(abilityId))
                        errors.Add(new ContentError(classFiles[cls.Id], $"class '{cls.Id}' references unknown ability '{abilityId}'."));
                    else if (movementIds.Contains(abilityId))
                        hasMovement = true;
                }
                if (!hasMovement)
                    errors.Add(new ContentError(classFiles[cls.Id], $"class '{cls.Id}' has no movement ability; every class needs at least one (usually 'move')."));
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
                new DefinitionTable<ClassDef>(classes),
                new DefinitionTable<MapData>(maps),
                new DefinitionTable<TimeControlDef>(timeControls),
                new DefinitionTable<ModifierDef>(modifiers),
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
