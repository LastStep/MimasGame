using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// An attack as authored in <c>abilities/*.json</c> with <c>"type": "attack"</c>: flat base damage in one
    /// damage lane, a circular range band, free-form tags for modifiers to match, and the two independent
    /// aiming fields (design: #trajectories) — <see cref="LineOfSight"/>, must the attacker see the target, and
    /// <see cref="Trajectory"/>, how the attack travels. Damage resolution lives in
    /// <c>Mimas.Core.Combat.DamageCalculator</c>; this class is data only.
    /// </summary>
    public sealed class AttackDef : AbilityDef
    {
        private readonly List<string> _tags;
        private readonly List<string> _elements;

        /// <summary>Flat base damage before stats and modifiers. Never negative.</summary>
        public int Damage { get; }

        /// <summary>
        /// The elements this attack carries (design: #elements): the innate <c>attack.element</c> from the
        /// file, plus whatever an Enchant added once the unit's overlay is applied. Sorted, unique, may be
        /// empty. Riders, resistances and immunities match on <b>any</b> of them.
        /// </summary>
        public IReadOnlyList<string> Elements => _elements;

        /// <summary>
        /// How many times the single-target resolution repeats (<c>attack.hits</c>, default 1). <b>Skeleton</b>
        /// (spec D part 1 §6.7, E5/S3): parsed and stored, and a unit whose resolved attack has more than one
        /// throws <c>NotSupportedException</c> at build. No shipped file sets it.
        /// </summary>
        public int Hits { get; }

        /// <summary>Damage lane; picks <c>power.&lt;type&gt;</c> / <c>defense.&lt;type&gt;</c> and is a modifier condition.</summary>
        public string DamageType { get; }

        /// <summary>Minimum Euclidean centre distance in tile spacings (1 = adjacent allowed).</summary>
        public int MinRange { get; }

        /// <summary>Maximum Euclidean centre distance in tile spacings.</summary>
        public int Range { get; }

        /// <summary>How the attack travels: one of <see cref="Trajectories"/>. Never null.</summary>
        public string Trajectory { get; }

        /// <summary>How far above the higher endpoint an <c>arc</c> peaks. 0 for every other trajectory.</summary>
        public int Apex { get; }

        /// <summary>True when the attacker must see the target (design: #line-of-sight).</summary>
        public bool LineOfSight { get; }

        /// <summary>Free-form tags (elements, weapon kinds) modifiers can match. Sorted, unique.</summary>
        public IReadOnlyList<string> Tags => _tags;

        public AttackDef(
            string id, string name, string category, int cost,
            int damage, string damageType, int range, int minRange = 1,
            string trajectory = Trajectories.Direct, bool lineOfSight = true, int apex = 0,
            List<string> tags = null, string description = null, string icon = null,
            List<string> elements = null, int hits = 1)
            : base(id, name, TypeAttack, description, icon, category, cost)
        {
            if (damage < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            if (string.IsNullOrEmpty(damageType)) throw new ArgumentException("Damage type must not be empty.", nameof(damageType));
            if (range < 1) throw new ArgumentOutOfRangeException(nameof(range));
            if (minRange < 1 || minRange > range) throw new ArgumentOutOfRangeException(nameof(minRange));
            if (!Trajectories.IsKnown(trajectory)) throw new ArgumentException("Unknown trajectory '" + trajectory + "'.", nameof(trajectory));
            if (apex < 0) throw new ArgumentOutOfRangeException(nameof(apex));
            if (apex > 0 && trajectory != Trajectories.Arc) throw new ArgumentException("Only an 'arc' has an apex.", nameof(apex));
            if (hits < 1) throw new ArgumentOutOfRangeException(nameof(hits));
            Damage = damage;
            DamageType = damageType;
            Range = range;
            MinRange = minRange;
            Trajectory = trajectory;
            Apex = apex;
            LineOfSight = lineOfSight;
            Hits = hits;
            _tags = new List<string>();
            if (tags != null)
            {
                foreach (string tag in tags)
                    if (!_tags.Contains(tag)) _tags.Add(tag);
            }
            _tags.Sort(string.CompareOrdinal);
            _elements = new List<string>();
            if (elements != null)
            {
                foreach (string element in elements)
                    if (!string.IsNullOrEmpty(element) && !_elements.Contains(element)) _elements.Add(element);
            }
            _elements.Sort(string.CompareOrdinal);
        }

        public bool HasTag(string tag) => tag != null && _tags.Contains(tag);

        public bool HasElement(string element) => element != null && _elements.Contains(element);

        /// <summary>
        /// True when a squared Euclidean centre distance (<see cref="Mimas.Core.Geometry.Hex.EuclideanSquared"/>)
        /// lies inside the range band. Squared throughout so the band stays a true circle with integer maths.
        /// </summary>
        public bool InRangeSquared(int distanceSquared)
            => distanceSquared >= MinRange * MinRange && distanceSquared <= Range * Range;

        /// <summary>
        /// A copy with different aiming fields and everything else untouched: the one seam an enchant or boon
        /// would swap a trajectory through (design: #trajectories). Exercised by tests only today.
        /// </summary>
        public AttackDef WithTrajectory(string trajectory, int apex, bool lineOfSight)
            => new AttackDef(Id, Name, Category, Cost, Damage, DamageType, Range, MinRange,
                trajectory, lineOfSight, apex, new List<string>(_tags), Description, Icon, new List<string>(_elements), Hits);

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

            string trajectory = MapJson.RequireString(attack, "trajectory", attackWhere);
            if (!Trajectories.IsKnown(trajectory))
                throw new MapLoadException($"{attackWhere}.trajectory is '{trajectory}' (known: {Trajectories.Direct}, {Trajectories.Arc}, {Trajectories.Sky}).");
            bool lineOfSight = MapJson.RequireBool(attack, "lineOfSight", attackWhere);
            int apex = MapJson.OptionalInt(attack, "apex", -1, attackWhere);
            if (trajectory == Trajectories.Arc)
            {
                if (apex < 0) throw new MapLoadException($"{attackWhere}.apex is required for trajectory 'arc'.");
            }
            else if (apex >= 0)
            {
                throw new MapLoadException($"{attackWhere}.apex is only allowed for trajectory 'arc' (got '{trajectory}').");
            }
            if (apex < 0) apex = 0;

            // At most one innate element; the catalogue checks it against rules.elements at link time.
            string element = MapJson.OptionalString(attack, "element", attackWhere);
            List<string> elements = element != null ? new List<string> { element } : null;

            int hits = MapJson.OptionalInt(attack, "hits", 1, attackWhere);
            if (hits < 1) throw new MapLoadException($"{attackWhere}.hits must be at least 1 (got {hits}).");

            return new AttackDef(id, name, category, cost, damage, damageType, range, minRange,
                trajectory, lineOfSight, apex, tags, description, icon, elements, hits);
        }
    }
}
