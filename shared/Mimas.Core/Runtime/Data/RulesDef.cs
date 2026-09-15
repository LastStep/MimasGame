using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// Match-wide knobs from <c>rules.json</c>: the damage lanes every stat key and attack must use, and
    /// the modifiers that apply to every attack (height advantage lives here as data, not as a special
    /// case in the calculator). One file, loaded once, required.
    /// </summary>
    public sealed class RulesDef
    {
        private readonly List<string> _damageTypes;
        private readonly List<string> _globalModifierIds;

        /// <summary>Damage lanes in authored order (e.g. melee, ranged, magic).</summary>
        public IReadOnlyList<string> DamageTypes => _damageTypes;

        /// <summary>Modifier ids evaluated for every attack, in authored order.</summary>
        public IReadOnlyList<string> GlobalModifierIds => _globalModifierIds;

        public RulesDef(List<string> damageTypes, List<string> globalModifierIds)
        {
            if (damageTypes == null || damageTypes.Count == 0) throw new ArgumentException("At least one damage type is required.", nameof(damageTypes));
            _damageTypes = new List<string>(damageTypes);
            _globalModifierIds = globalModifierIds != null ? new List<string>(globalModifierIds) : new List<string>();
        }

        public bool IsDamageType(string type) => type != null && _damageTypes.Contains(type);

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
            return new RulesDef(damageTypes, globals);
        }
    }
}
