using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// Match-wide knobs from <c>rules.json</c>: the damage lanes every stat key and attack must use, the
    /// stats and abilities every hero has before gear, the modifiers that apply to every attack (height
    /// advantage lives here as data, not as a special case in the calculator), and the clock both sides
    /// read. One file, loaded once, required.
    /// <para>
    /// The <c>clock</c> block is required in <c>rules.json</c>. The constructor defaults it to
    /// <see cref="ClockDef.Default"/> so a test fixture can build a <see cref="RulesDef"/> by hand without
    /// naming numbers it does not care about; loading never takes that path.
    /// </para>
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

        /// <summary>The turn deadline, lag grace and reconnect grace both the server and the client read (rules.json clock).</summary>
        public ClockDef Clock { get; }

        public RulesDef(List<string> damageTypes, List<string> globalModifierIds, StatBlock baseStats, HeightsDef heights, List<string> innateAbilityIds,
            ClockDef clock = null)
        {
            if (damageTypes == null || damageTypes.Count == 0) throw new ArgumentException("At least one damage type is required.", nameof(damageTypes));
            if (innateAbilityIds == null || innateAbilityIds.Count == 0) throw new ArgumentException("At least one innate ability is required.", nameof(innateAbilityIds));
            _damageTypes = new List<string>(damageTypes);
            _globalModifierIds = globalModifierIds != null ? new List<string>(globalModifierIds) : new List<string>();
            BaseStats = baseStats ?? throw new ArgumentNullException(nameof(baseStats));
            Heights = heights ?? throw new ArgumentNullException(nameof(heights));
            _innateAbilityIds = new List<string>(innateAbilityIds);
            Clock = clock ?? ClockDef.Default();
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

            if (!(MapJson.Require(root, "clock", "rules") is JObject clockObj))
                throw new MapLoadException("rules field 'clock' must be an object (rules.json).");
            ClockDef clock = ClockDef.FromJsonAt(clockObj, "rules.clock");

            return new RulesDef(damageTypes, globals, baseStats, heights, innate, clock);
        }
    }
}
