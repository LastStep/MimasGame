using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// A non-unit body a map can place on a hex (<c>props/*.json</c>, design: #props): a pillar, a wall. It
    /// occupies its tile, blocks sight and trajectories with <see cref="BodyHeight"/>, and — when it has
    /// <c>stats.hp</c> — is a legal target with its own <see cref="AimHeight"/>. A prop without stats is a
    /// permanent wall that cannot be hit. Props are neutral and public; nothing about them is hidden.
    /// </summary>
    public sealed class PropDef : IContentDef
    {
        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Presentation key for the prop's icon, or null. Core never interprets it.</summary>
        public string Icon { get; }

        /// <summary>Height in units above the tile top. At least 1.</summary>
        public int BodyHeight { get; }

        /// <summary>Where attacks land on it, in units above the tile top. 1..<see cref="BodyHeight"/>.</summary>
        public int AimHeight { get; }

        /// <summary>Hit points and per-lane defence; empty for a wall.</summary>
        public StatBlock Stats { get; }

        /// <summary>True when the prop has hit points, i.e. it can be targeted and destroyed.</summary>
        public bool IsDestructible => Stats.Hp > 0;

        public PropDef(string id, string name, string description, string icon, int bodyHeight, int aimHeight, StatBlock stats)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Description = description;
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
            if (bodyHeight < 1) throw new ArgumentOutOfRangeException(nameof(bodyHeight));
            if (aimHeight < 1 || aimHeight > bodyHeight) throw new ArgumentOutOfRangeException(nameof(aimHeight));
            BodyHeight = bodyHeight;
            AimHeight = aimHeight;
            Stats = stats ?? StatBlock.Empty;
        }

        public static PropDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Prop JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Prop JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "prop");
            if (version != 1) throw new MapLoadException($"Unsupported prop version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "prop");
            string where = $"prop '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);

            int bodyHeight = MapJson.RequireInt(root, "bodyHeight", where);
            if (bodyHeight < 1) throw new MapLoadException($"{where} bodyHeight must be at least 1.");
            int aimHeight = MapJson.RequireInt(root, "aimHeight", where);
            if (aimHeight < 1 || aimHeight > bodyHeight)
                throw new MapLoadException($"{where} aimHeight must be between 1 and bodyHeight ({bodyHeight}).");

            StatBlock stats = ReadStats(root["stats"], $"{where}.stats");
            return new PropDef(id, name, description, icon, bodyHeight, aimHeight, stats);
        }

        /// <summary>
        /// A prop's stat table, parsed by hand: <c>hp</c> (at least 1 when present) and <c>defense.&lt;lane&gt;</c>
        /// only. <c>ap</c> and <c>power.*</c> are meaningless on a prop and are errors rather than silent noise.
        /// <see cref="StatBlock.FromJsonAt"/> is not reusable here: it always requires hp and ap.
        /// </summary>
        private static StatBlock ReadStats(JToken token, string path)
        {
            if (token == null || token.Type == JTokenType.Null) return StatBlock.Empty;
            if (!(token is JObject stats)) throw new MapLoadException($"{path} must be an object.");

            var entries = new List<KeyValuePair<string, int>>();
            foreach (var property in stats.Properties())
            {
                string key = property.Name;
                if (property.Value == null || property.Value.Type != JTokenType.Integer)
                    throw new MapLoadException($"{path}['{key}'] must be an integer.");
                int value = (int)property.Value;

                if (key == StatBlock.HpKey)
                {
                    if (value < 1) throw new MapLoadException($"{path}['hp'] must be at least 1.");
                }
                else if (key.StartsWith(StatBlock.DefensePrefix, StringComparison.Ordinal))
                {
                    if (key.Length == StatBlock.DefensePrefix.Length)
                        throw new MapLoadException($"{path} has stat '{key}' with no damage type.");
                    if (value < 0) throw new MapLoadException($"{path}['{key}'] must not be negative.");
                }
                else
                {
                    throw new MapLoadException($"{path} has unknown stat '{key}' (a prop may only carry hp and defense.<type>).");
                }

                entries.Add(new KeyValuePair<string, int>(key, value));
            }
            return new StatBlock(entries);
        }
    }
}
