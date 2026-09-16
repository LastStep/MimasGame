using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// A stat table as authored in <c>rules.json</c> <c>baseStats</c> and <c>items/*.json</c> <c>stats</c>:
    /// an open string-keyed integer table so a new stat is JSON only. Two keys are rules-level and required
    /// in a full block (<see cref="HpKey"/>, <see cref="ApKey"/>); every other key must be
    /// <c>power.&lt;type&gt;</c> or <c>defense.&lt;type&gt;</c> for a damage type declared in
    /// <c>rules.json</c> (checked at link time, since a single file cannot know the rules on its own).
    /// A hero's numbers are the base block plus every equipped item's block (<see cref="Add"/>). Missing
    /// keys read as 0. Immutable; entries are kept sorted by ordinal key.
    /// </summary>
    public sealed class StatBlock
    {
        public const string HpKey = "hp";
        public const string ApKey = "ap";
        public const string PowerPrefix = "power.";
        public const string DefensePrefix = "defense.";

        /// <summary>An empty block: every stat reads as 0.</summary>
        public static readonly StatBlock Empty = new StatBlock(new List<KeyValuePair<string, int>>());

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

        /// <summary>Key-wise sum. Keys present in either block appear in the result.</summary>
        public StatBlock Add(StatBlock other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var merged = new List<KeyValuePair<string, int>>(_entries);
            for (int i = 0; i < other._entries.Count; i++)
            {
                string key = other._entries[i].Key;
                int value = other._entries[i].Value;
                int at = -1;
                for (int j = 0; j < merged.Count; j++) if (merged[j].Key == key) { at = j; break; }
                if (at < 0) merged.Add(new KeyValuePair<string, int>(key, value));
                else merged[at] = new KeyValuePair<string, int>(key, merged[at].Value + value);
            }
            return new StatBlock(merged);
        }

        public static string PowerKey(string damageType) => PowerPrefix + damageType;

        public static string DefenseKey(string damageType) => DefensePrefix + damageType;

        /// <summary>
        /// Parses a definition's <c>stats</c> object. Requires <c>hp</c> at least 1 and <c>ap</c> 0 or more;
        /// other keys must be integers with a <c>power.</c> or <c>defense.</c> prefix (the damage type itself
        /// is checked by the catalogue against <c>rules.json</c>).
        /// </summary>
        internal static StatBlock FromJson(JObject stats, string where)
        {
            return Parse(stats, where + ".stats", true, false);
        }

        /// <summary>
        /// Parses a partial stat object (an item's stats): same key rules as <see cref="FromJson"/> but
        /// <c>hp</c> and <c>ap</c> are optional, and every value must be 0 or more when
        /// <paramref name="nonNegative"/>.
        /// </summary>
        internal static StatBlock FromJsonPartial(JObject stats, string where, bool nonNegative)
        {
            return Parse(stats, where + ".stats", false, nonNegative);
        }

        /// <summary>
        /// Parses a full stat object that is not a definition's <c>stats</c> field, naming it by its own
        /// path in error messages (<c>rules.baseStats</c>).
        /// </summary>
        internal static StatBlock FromJsonAt(JObject stats, string path, bool nonNegative)
        {
            return Parse(stats, path, true, nonNegative);
        }

        /// <summary>The one key-validating loop behind every entry point above.</summary>
        private static StatBlock Parse(JObject stats, string path, bool requireHpAp, bool nonNegative)
        {
            if (stats == null) throw new MapLoadException($"{path} must be an object.");

            var entries = new List<KeyValuePair<string, int>>();
            foreach (var property in stats.Properties())
            {
                string key = property.Name;
                if (string.IsNullOrEmpty(key)) throw new MapLoadException($"{path} has an empty stat key.");
                if (property.Value == null || property.Value.Type != JTokenType.Integer)
                    throw new MapLoadException($"{path}['{key}'] must be an integer.");
                int value = (int)property.Value;

                if (key == HpKey)
                {
                    if (requireHpAp && value < 1) throw new MapLoadException($"{path}['hp'] must be at least 1.");
                }
                else if (key == ApKey)
                {
                    if (requireHpAp && value < 0) throw new MapLoadException($"{path}['ap'] must not be negative.");
                }
                else if (!key.StartsWith(PowerPrefix, StringComparison.Ordinal) && !key.StartsWith(DefensePrefix, StringComparison.Ordinal))
                {
                    throw new MapLoadException($"{path} has unknown stat '{key}' (expected hp, ap, power.<type> or defense.<type>).");
                }
                else if (key.Length == PowerPrefix.Length && key.StartsWith(PowerPrefix, StringComparison.Ordinal)
                      || key.Length == DefensePrefix.Length && key.StartsWith(DefensePrefix, StringComparison.Ordinal))
                {
                    throw new MapLoadException($"{path} has stat '{key}' with no damage type.");
                }

                if (nonNegative && value < 0) throw new MapLoadException($"{path}['{key}'] must not be negative.");

                entries.Add(new KeyValuePair<string, int>(key, value));
            }

            var block = new StatBlock(entries);
            if (requireHpAp)
            {
                if (!block.Has(HpKey)) throw new MapLoadException($"{path} is missing required stat 'hp'.");
                if (!block.Has(ApKey)) throw new MapLoadException($"{path} is missing required stat 'ap'.");
            }
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
