using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>When a modifier is consulted. Mirrors Slay the Spire's give/receive split.</summary>
    public static class ModifierTriggers
    {
        /// <summary>Evaluated for the attacker's side: its unit, its tile, and the global list.</summary>
        public const string DealDamage = "dealDamage";

        /// <summary>Evaluated for the defender's side: its unit, its tile, and the global list.</summary>
        public const string TakeDamage = "takeDamage";

        public static bool IsKnown(string trigger) => trigger == DealDamage || trigger == TakeDamage;
    }

    public static class ModifierVisibility
    {
        /// <summary>Always visible to both players (terrain, tiles, height).</summary>
        public const string Public = "public";

        /// <summary>Unknown to the opponent until it first changes a result (boons).</summary>
        public const string Hidden = "hidden";

        public static bool IsKnown(string visibility) => visibility == Public || visibility == Hidden;
    }

    /// <summary>
    /// One data-driven modifier from <c>modifiers/*.json</c>: a trigger, a conjunction of conditions and one
    /// flat effect. Who it belongs to is not in the definition: a terrain, a map hex, a boon or
    /// <c>rules.json</c> attaches it, and the calculator resolves the owner at evaluation time. No
    /// percentages, no ordering rules in data (ADR-014 spirit); everything is a signed integer.
    /// </summary>
    public sealed class ModifierDef : IContentDef
    {
        private readonly List<string> _damageTypes;
        private readonly List<string> _tags;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Presentation key for an icon, or null.</summary>
        public string Icon { get; }

        /// <summary>One of <see cref="ModifierTriggers"/>.</summary>
        public string Trigger { get; }

        /// <summary>One of <see cref="ModifierVisibility"/>.</summary>
        public string Visibility { get; }

        public bool IsHidden => Visibility == ModifierVisibility.Hidden;

        /// <summary>Condition: the attack's damage type must be one of these. Empty = any.</summary>
        public IReadOnlyList<string> DamageTypes => _damageTypes;

        /// <summary>Condition: the attack must carry at least one of these tags. Empty = any.</summary>
        public IReadOnlyList<string> Tags => _tags;

        /// <summary>Condition: the attacker's tile must be higher than the target's. Null = not checked.</summary>
        public bool? HeightAdvantage { get; }

        /// <summary>Effect: flat amount added to damage (may be negative). Never 0.</summary>
        public int Damage { get; }

        public ModifierDef(
            string id, string name, string trigger, int damage,
            string visibility = ModifierVisibility.Public,
            List<string> damageTypes = null, List<string> tags = null, bool? heightAdvantage = null,
            string description = null, string icon = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            if (!ModifierTriggers.IsKnown(trigger)) throw new ArgumentException("Unknown modifier trigger '" + trigger + "'.", nameof(trigger));
            if (!ModifierVisibility.IsKnown(visibility)) throw new ArgumentException("Unknown modifier visibility '" + visibility + "'.", nameof(visibility));
            if (damage == 0) throw new ArgumentException("A modifier must change damage by a non-zero amount.", nameof(damage));
            Trigger = trigger;
            Visibility = visibility;
            Damage = damage;
            _damageTypes = damageTypes != null ? new List<string>(damageTypes) : new List<string>();
            _tags = tags != null ? new List<string>(tags) : new List<string>();
            HeightAdvantage = heightAdvantage;
            Description = description;
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
        }

        public static ModifierDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Modifier JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Modifier JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "modifier");
            if (version != 1) throw new MapLoadException($"Unsupported modifier version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "modifier");
            string where = $"modifier '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);

            string trigger = MapJson.RequireString(root, "trigger", where);
            if (!ModifierTriggers.IsKnown(trigger))
                throw new MapLoadException($"{where} has unknown trigger '{trigger}' (known: {ModifierTriggers.DealDamage}, {ModifierTriggers.TakeDamage}).");

            string visibility = MapJson.OptionalString(root, "visibility", where) ?? ModifierVisibility.Public;
            if (!ModifierVisibility.IsKnown(visibility))
                throw new MapLoadException($"{where} has unknown visibility '{visibility}' (known: {ModifierVisibility.Public}, {ModifierVisibility.Hidden}).");

            List<string> damageTypes = null;
            List<string> tags = null;
            bool? heightAdvantage = null;
            var whenToken = root["when"];
            if (whenToken != null && whenToken.Type != JTokenType.Null)
            {
                if (!(whenToken is JObject when)) throw new MapLoadException($"{where} field 'when' must be an object.");
                foreach (var property in when.Properties())
                {
                    switch (property.Name)
                    {
                        case "damageTypes":
                            damageTypes = MapJson.RequireStringList(when, "damageTypes", $"{where}.when");
                            if (damageTypes.Count == 0) throw new MapLoadException($"{where}.when.damageTypes must not be empty (omit it for any type).");
                            break;
                        case "tags":
                            tags = MapJson.RequireStringList(when, "tags", $"{where}.when");
                            if (tags.Count == 0) throw new MapLoadException($"{where}.when.tags must not be empty (omit it for any tag).");
                            break;
                        case "heightAdvantage":
                            if (property.Value.Type != JTokenType.Boolean) throw new MapLoadException($"{where}.when.heightAdvantage must be a boolean.");
                            heightAdvantage = (bool)property.Value;
                            break;
                        default:
                            throw new MapLoadException($"{where}.when has unknown condition '{property.Name}' (known: damageTypes, tags, heightAdvantage).");
                    }
                }
            }

            if (!(MapJson.Require(root, "effect", where) is JObject effect))
                throw new MapLoadException($"{where} field 'effect' must be an object.");
            int damage = 0;
            bool hasDamage = false;
            foreach (var property in effect.Properties())
            {
                switch (property.Name)
                {
                    case "damage":
                        if (property.Value.Type != JTokenType.Integer) throw new MapLoadException($"{where}.effect.damage must be an integer.");
                        damage = (int)property.Value;
                        hasDamage = true;
                        break;
                    default:
                        throw new MapLoadException($"{where}.effect has unknown key '{property.Name}' (known: damage).");
                }
            }
            if (!hasDamage) throw new MapLoadException($"{where}.effect must set 'damage'.");
            if (damage == 0) throw new MapLoadException($"{where}.effect.damage must not be 0.");

            return new ModifierDef(id, name, trigger, damage, visibility, damageTypes, tags, heightAdvantage, description, icon);
        }
    }
}
