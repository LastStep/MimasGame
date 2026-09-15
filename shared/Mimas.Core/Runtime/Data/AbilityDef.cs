using System;
using Mimas.Core.Content;
using Mimas.Core.Movement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// How the HUD groups abilities. Presentation reads it; rules never branch on it. Movement abilities are
    /// always <see cref="Movement"/>; other types declare <c>category</c> in JSON.
    /// </summary>
    public static class AbilityCategories
    {
        public const string Movement = "movement";
        public const string Weapon = "weapon";
        public const string Spell = "spell";

        public static bool IsKnown(string category)
            => category == Movement || category == Weapon || category == Spell;
    }

    /// <summary>
    /// Anything a unit can do, authored in <c>abilities/*.json</c>. The <c>type</c> field picks the concrete
    /// subclass; today only <c>"movement"</c> exists. Attacks, buffs and the rest will add subclasses and a
    /// line in <see cref="FromJson"/>, nothing else.
    /// </summary>
    public abstract class AbilityDef : IContentDef
    {
        public const string TypeMovement = "movement";

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>
        /// Presentation key for the ability's icon (the optional <c>icon</c> field), or null. Core never
        /// interprets it; the client maps it to a sprite and falls back to a glyph when nothing matches.
        /// </summary>
        public string Icon { get; }

        /// <summary>HUD grouping key, one of <see cref="AbilityCategories"/>. Never null.</summary>
        public string Category { get; }

        /// <summary>The <c>type</c> field: which subclass this is.</summary>
        public string Type { get; }

        protected AbilityDef(string id, string name, string type, string description, string icon = null, string category = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Description = description;
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;

            if (type == TypeMovement)
            {
                if (category != null && category != AbilityCategories.Movement)
                    throw new ArgumentException("Movement abilities are always category '" + AbilityCategories.Movement + "'.", nameof(category));
                Category = AbilityCategories.Movement;
            }
            else
            {
                if (!AbilityCategories.IsKnown(category))
                    throw new ArgumentException("Unknown ability category '" + category + "'.", nameof(category));
                Category = category;
            }
        }

        /// <summary>Parses one ability file, dispatching on <c>type</c>. Throws <see cref="MapLoadException"/> on schema violations.</summary>
        public static AbilityDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Ability JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Ability JSON is malformed: " + e.Message, e);
            }

            string id = MapJson.RequireString(root, "id", "ability");
            string type = MapJson.RequireString(root, "type", $"ability '{id}'");
            switch (type)
            {
                case TypeMovement:
                    return MovementDef.FromJson(json);
                default:
                    throw new MapLoadException($"ability '{id}' has unsupported type '{type}' (known: {TypeMovement}).");
            }
        }
    }
}
