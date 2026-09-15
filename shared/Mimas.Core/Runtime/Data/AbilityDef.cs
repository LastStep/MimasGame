using System;
using Mimas.Core.Content;
using Mimas.Core.Movement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
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

        /// <summary>The <c>type</c> field: which subclass this is.</summary>
        public string Type { get; }

        protected AbilityDef(string id, string name, string type, string description)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            Type = type ?? throw new ArgumentNullException(nameof(type));
            Description = description;
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
