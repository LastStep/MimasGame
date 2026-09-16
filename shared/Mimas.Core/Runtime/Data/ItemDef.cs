using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// One piece of gear from <c>items/*.json</c>: the slot it fills, the stats it adds to the hero's base
    /// block and the abilities it grants. A hero is nothing but the base stats plus four of these
    /// (design: #equipment). Ability ids are strings resolved against the catalogue at link time, so an
    /// item file can never reference a missing ability; stat keys are checked against <c>rules.json</c>
    /// the same way. Item stats are pure additions and never negative.
    /// </summary>
    public sealed class ItemDef : IContentDef
    {
        private readonly List<string> _abilityIds;
        private readonly List<string> _tags;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>One of <see cref="ItemSlots"/>.</summary>
        public string Slot { get; }

        /// <summary>Free string naming the family (bow, gun, circlet, leaping, blink, leather); boons match on it.</summary>
        public string Kind { get; }

        /// <summary>Stats this item adds. Never null; may be empty.</summary>
        public StatBlock Stats { get; }

        /// <summary>Ability ids granted, in authored order, unique.</summary>
        public IReadOnlyList<string> AbilityIds => _abilityIds;

        /// <summary>Free-form tags modifiers can match. Sorted, unique.</summary>
        public IReadOnlyList<string> Tags => _tags;

        /// <summary>Presentation key the client resolves to a sprite, or null.</summary>
        public string Icon { get; }

        public ItemDef(string id, string name, string description, string slot, string kind, StatBlock stats,
            List<string> abilityIds, List<string> tags, string icon)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Description = description;
            if (string.IsNullOrEmpty(slot)) throw new ArgumentException("Slot must not be empty.", nameof(slot));
            if (string.IsNullOrEmpty(kind)) throw new ArgumentException("Kind must not be empty.", nameof(kind));
            Slot = slot;
            Kind = kind;
            Stats = stats ?? throw new ArgumentNullException(nameof(stats));
            _abilityIds = abilityIds != null ? new List<string>(abilityIds) : new List<string>();
            _tags = new List<string>();
            if (tags != null)
            {
                foreach (string tag in tags)
                    if (!_tags.Contains(tag)) _tags.Add(tag);
            }
            _tags.Sort(string.CompareOrdinal);
            Icon = icon;
        }

        public bool HasTag(string tag) => tag != null && _tags.Contains(tag);

        public static ItemDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Item JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Item JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "item");
            if (version != 1) throw new MapLoadException($"Unsupported item version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "item");
            string where = $"item '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);

            string slot = MapJson.RequireString(root, "slot", where);
            if (!ItemSlots.IsKnown(slot))
                throw new MapLoadException($"{where} has unknown slot '{slot}' (expected {ItemSlots.Weapon}, {ItemSlots.Crown}, {ItemSlots.Boots} or {ItemSlots.Armour}).");
            string kind = MapJson.RequireString(root, "kind", where);

            if (!(MapJson.Require(root, "abilities", where) is JArray array))
                throw new MapLoadException($"{where} field 'abilities' must be an array of ability ids.");

            var ids = new List<string>(array.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i].Type != JTokenType.String || string.IsNullOrEmpty((string)array[i]))
                    throw new MapLoadException($"{where} abilities[{i}] must be a non-empty string.");
                string abilityId = (string)array[i];
                if (!seen.Add(abilityId))
                    throw new MapLoadException($"{where} lists ability '{abilityId}' twice.");
                ids.Add(abilityId);
            }

            if (!(MapJson.Require(root, "stats", where) is JObject statsObj))
                throw new MapLoadException($"{where} field 'stats' must be an object.");
            StatBlock stats = StatBlock.FromJsonPartial(statsObj, where, true);

            var tags = MapJson.OptionalStringList(root, "tags", where);

            return new ItemDef(id, name, description, slot, kind, stats, ids, tags, icon);
        }
    }
}
