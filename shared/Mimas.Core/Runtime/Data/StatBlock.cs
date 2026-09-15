using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// A unit's base numbers as authored in <c>classes/*.json</c> <c>stats</c>: an open string-keyed integer
    /// table so a new stat is JSON only. Two keys are rules-level and required (<see cref="HpKey"/>,
    /// <see cref="ApKey"/>); every other key must be <c>power.&lt;type&gt;</c> or <c>defense.&lt;type&gt;</c>
    /// for a damage type declared in <c>rules.json</c> (checked at link time, since the class file cannot
    /// know the rules on its own). Missing keys read as 0. Immutable; entries are kept sorted by ordinal key.
    /// </summary>
    public sealed class StatBlock
    {
        public const string HpKey = "hp";
        public const string ApKey = "ap";
        public const string PowerPrefix = "power.";
        public const string DefensePrefix = "defense.";

        private readonly List<KeyValuePair<string, int>> _entries;

        /// <summary>Entries sorted by ordinal key.</summary>
        public IReadOnlyList<KeyValuePair<string, int>> Entries => _entries;

        public int Hp => Get(HpKey);
        public int Ap => Get(ApKey);

        public StatBlock(IEnumerable<KeyValuePair<string, int>> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            _entries = new List<KeyValuePair<string, int>>(entries);
            _entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
            for (int i = 1; i < _entries.Count; i++)
            {
                if (_entries[i - 1].Key == _entries[i].Key)
                    throw new ArgumentException($"Duplicate stat key '{_entries[i].Key}'.", nameof(entries));
            }
        }

        /// <summary>The stat, or 0 when the key is absent.</summary>
        public int Get(string key)
        {
            if (key == null) return 0;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Key == key) return _entries[i].Value;
            return 0;
        }

        public bool Has(string key)
        {
            if (key == null) return false;
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].Key == key) return true;
            return false;
        }

        public static string PowerKey(string damageType) => PowerPrefix + damageType;

        public static string DefenseKey(string damageType) => DefensePrefix + damageType;

        /// <summary>
        /// Parses the <c>stats</c> object. Requires <c>hp</c> ≥ 1 and <c>ap</c> ≥ 0; other keys must be
        /// integers with a <c>power.</c> or <c>defense.</c> prefix (the damage type itself is checked by the
        /// catalogue against <c>rules.json</c>).
        /// </summary>
        internal static StatBlock FromJson(JObject stats, string where)
        {
            if (stats == null) throw new MapLoadException($"{where} field 'stats' must be an object.");

            var entries = new List<KeyValuePair<string, int>>();
            foreach (var property in stats.Properties())
            {
                string key = property.Name;
                if (string.IsNullOrEmpty(key)) throw new MapLoadException($"{where}.stats has an empty stat key.");
                if (property.Value == null || property.Value.Type != JTokenType.Integer)
                    throw new MapLoadException($"{where}.stats['{key}'] must be an integer.");
                int value = (int)property.Value;

                if (key == HpKey)
                {
                    if (value < 1) throw new MapLoadException($"{where}.stats.hp must be at least 1.");
                }
                else if (key == ApKey)
                {
                    if (value < 0) throw new MapLoadException($"{where}.stats.ap must not be negative.");
                }
                else if (!key.StartsWith(PowerPrefix, StringComparison.Ordinal) && !key.StartsWith(DefensePrefix, StringComparison.Ordinal))
                {
                    throw new MapLoadException($"{where}.stats has unknown stat '{key}' (expected hp, ap, power.<type> or defense.<type>).");
                }
                else if (key.Length == PowerPrefix.Length && key.StartsWith(PowerPrefix, StringComparison.Ordinal)
                      || key.Length == DefensePrefix.Length && key.StartsWith(DefensePrefix, StringComparison.Ordinal))
                {
                    throw new MapLoadException($"{where}.stats has stat '{key}' with no damage type.");
                }

                entries.Add(new KeyValuePair<string, int>(key, value));
            }

            var block = new StatBlock(entries);
            if (!block.Has(HpKey)) throw new MapLoadException($"{where}.stats is missing required stat 'hp'.");
            if (!block.Has(ApKey)) throw new MapLoadException($"{where}.stats is missing required stat 'ap'.");
            return block;
        }

        /// <summary>The damage type a <c>power.</c>/<c>defense.</c> key refers to, or null for hp/ap.</summary>
        public static string DamageTypeOf(string key)
        {
            if (key == null) return null;
            if (key.StartsWith(PowerPrefix, StringComparison.Ordinal)) return key.Substring(PowerPrefix.Length);
            if (key.StartsWith(DefensePrefix, StringComparison.Ordinal)) return key.Substring(DefensePrefix.Length);
            return null;
        }
    }
}
