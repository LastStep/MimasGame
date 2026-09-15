using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// An attack as authored in <c>abilities/*.json</c> with <c>"type": "attack"</c>: flat base damage in one
    /// damage lane, a hex range band, and free-form tags for modifiers to match. Line of sight is always
    /// required (design decision, 15 Sep 2026), so there is no flag for it. Damage resolution lives in
    /// <c>Mimas.Core.Combat.DamageCalculator</c>; this class is data only.
    /// </summary>
    public sealed class AttackDef : AbilityDef
    {
        private readonly List<string> _tags;

        /// <summary>Flat base damage before stats and modifiers. Never negative.</summary>
        public int Damage { get; }

        /// <summary>Damage lane; picks <c>power.&lt;type&gt;</c> / <c>defense.&lt;type&gt;</c> and is a modifier condition.</summary>
        public string DamageType { get; }

        /// <summary>Minimum hex distance to the target (1 = adjacent allowed).</summary>
        public int MinRange { get; }

        /// <summary>Maximum hex distance to the target.</summary>
        public int Range { get; }

        /// <summary>Free-form tags (elements, weapon kinds) modifiers can match. Sorted, unique.</summary>
        public IReadOnlyList<string> Tags => _tags;

        public AttackDef(
            string id, string name, string category, int cost,
            int damage, string damageType, int range, int minRange = 1,
            List<string> tags = null, string description = null, string icon = null)
            : base(id, name, TypeAttack, description, icon, category, cost)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (string.IsNullOrEmpty(damageType)) throw new ArgumentException("Damage type must not be empty.", nameof(damageType));
            if (range < 1) throw new ArgumentOutOfRangeException(nameof(range));
            if (minRange < 1 || minRange > range) throw new ArgumentOutOfRangeException(nameof(minRange));
            Damage = damage;
            DamageType = damageType;
            Range = range;
            MinRange = minRange;
            _tags = new List<string>();
            if (tags != null)
            {
                foreach (string tag in tags)
                    if (!_tags.Contains(tag)) _tags.Add(tag);
            }
            _tags.Sort(string.CompareOrdinal);
        }

        public bool HasTag(string tag) => tag != null && _tags.Contains(tag);

        /// <summary>True when <paramref name="distance"/> lies inside the range band.</summary>
        public bool InRange(int distance) => distance >= MinRange && distance <= Range;

        public static AttackDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Attack JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Attack JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "ability");
            if (version != 1) throw new MapLoadException($"Unsupported ability version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "ability");
            string where = $"ability '{id}'";
            string type = MapJson.RequireString(root, "type", where);
            if (!string.Equals(type, TypeAttack, StringComparison.Ordinal))
                throw new MapLoadException($"{where} has type '{type}'; AttackDef only loads type '{TypeAttack}'.");

            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);
            string category = MapJson.OptionalString(root, "category", where);
            if (!AbilityCategories.IsKnown(category) || category == AbilityCategories.Movement)
                throw new MapLoadException($"{where} must declare 'category' as '{AbilityCategories.Weapon}' or '{AbilityCategories.Spell}' (got '{category}').");
            int cost = ReadCost(root, where);
            var tags = MapJson.OptionalStringList(root, "tags", where);

            if (!(MapJson.Require(root, "attack", where) is JObject attack))
                throw new MapLoadException($"{where} field 'attack' must be an object.");
            string attackWhere = $"{where}.attack";
            int damage = MapJson.RequireInt(attack, "damage", attackWhere);
            if (damage < 0) throw new MapLoadException($"{attackWhere}.damage must not be negative.");
            string damageType = MapJson.RequireString(attack, "damageType", attackWhere);
            int range = MapJson.RequireInt(attack, "range", attackWhere);
            if (range < 1) throw new MapLoadException($"{attackWhere}.range must be at least 1.");
            int minRange = MapJson.OptionalInt(attack, "minRange", 1, attackWhere);
            if (minRange < 1 || minRange > range) throw new MapLoadException($"{attackWhere}.minRange must be between 1 and range ({range}).");

            return new AttackDef(id, name, category, cost, damage, damageType, range, minRange, tags, description, icon);
        }
    }
}
