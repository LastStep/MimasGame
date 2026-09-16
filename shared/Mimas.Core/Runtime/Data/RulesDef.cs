using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// Match-wide knobs from <c>rules.json</c>: the damage lanes every stat key and attack must use, the
    /// stats and abilities every hero has before gear, and the modifiers that apply to every attack
    /// (height advantage lives here as data, not as a special case in the calculator). One file, loaded
    /// once, required.
    /// </summary>
    public sealed class RulesDef
    {
        private readonly List<string> _damageTypes;
        private readonly List<string> _globalModifierIds;
        private readonly List<string> _innateAbilityIds;

        /// <summary>Damage lanes in authored order (weapon, spell).</summary>
        public IReadOnlyList<string> DamageTypes => _damageTypes;

        /// <summary>Modifier ids evaluated for every attack, in authored order.</summary>
        public IReadOnlyList<string> GlobalModifierIds => _globalModifierIds;

        /// <summary>Stats every hero starts with before gear (rules.json baseStats).</summary>
        public StatBlock BaseStats { get; }

        /// <summary>How map height levels convert to the body units sight and trajectories use (rules.json heights).</summary>
        public HeightsDef Heights { get; }

        /// <summary>Ability ids every hero has regardless of gear (rules.json innateAbilities), authored order.</summary>
        public IReadOnlyList<string> InnateAbilityIds => _innateAbilityIds;

        public RulesDef(List<string> damageTypes, List<string> globalModifierIds, StatBlock baseStats, HeightsDef heights, List<string> innateAbilityIds)
        {
            if (damageTypes == null || damageTypes.Count == 0) throw new ArgumentException("At least one damage type is required.", nameof(damageTypes));
            if (innateAbilityIds == null || innateAbilityIds.Count == 0) throw new ArgumentException("At least one innate ability is required.", nameof(innateAbilityIds));
            _damageTypes = new List<string>(damageTypes);
            _globalModifierIds = globalModifierIds != null ? new List<string>(globalModifierIds) : new List<string>();
            BaseStats = baseStats ?? throw new ArgumentNullException(nameof(baseStats));
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            _innateAbilityIds = new List<string>(innateAbilityIds);
        }

        public bool IsDamageType(string type) => type != null && _damageTypes.Contains(type);

        public bool IsInnateAbility(string abilityId) => abilityId != null && _innateAbilityIds.Contains(abilityId);

        public static RulesDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Rules JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Rules JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "rules");
            if (version != 1) throw new MapLoadException($"Unsupported rules version {version} (expected 1).");

            var damageTypes = MapJson.RequireStringList(root, "damageTypes", "rules");
            if (damageTypes.Count == 0) throw new MapLoadException("rules field 'damageTypes' must list at least one damage type.");
            var globals = MapJson.OptionalStringList(root, "globalModifiers", "rules");

            if (!(MapJson.Require(root, "baseStats", "rules") is JObject baseStatsObj))
                throw new MapLoadException("rules field 'baseStats' must be an object.");
            StatBlock baseStats = StatBlock.FromJsonAt(baseStatsObj, "rules.baseStats", true);

            if (!(MapJson.Require(root, "heights", "rules") is JObject heightsObj))
                throw new MapLoadException("rules field 'heights' must be an object.");
            HeightsDef heights = HeightsDef.FromJsonAt(heightsObj, "rules.heights");

            var innate = MapJson.RequireStringList(root, "innateAbilities", "rules");
            if (innate.Count == 0) throw new MapLoadException("rules field 'innateAbilities' must list at least one ability id.");

            return new RulesDef(damageTypes, globals, baseStats, heights, innate);
        }
    }
}
