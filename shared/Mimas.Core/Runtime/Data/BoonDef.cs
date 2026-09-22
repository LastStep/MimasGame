using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>The three kinds of boon (design: #boons). Blessing = a passive on the character; Enchant = a change to one item; Sigil = a new ability on one item.</summary>
    public static class BoonKinds
    {
        public const string Blessing = "blessing";
        public const string Enchant = "enchant";
        public const string Sigil = "sigil";

        public static readonly string[] All = { Blessing, Enchant, Sigil };

        public static bool IsKnown(string kind) => kind == Blessing || kind == Enchant || kind == Sigil;
    }

    /// <summary>What gear an Enchant or Sigil needs: a slot, and optionally an item kind in that slot (design: #enchant, #sigil).</summary>
    public sealed class BoonRequirement
    {
        /// <summary>One of <see cref="ItemSlots"/>.</summary>
        public string Slot { get; }

        /// <summary>An item kind (bow, gun, ...) the item in <see cref="Slot"/> must have, or null for any.</summary>
        public string Kind { get; }

        public BoonRequirement(string slot, string kind = null)
        {
            if (!ItemSlots.IsKnown(slot)) throw new ArgumentException("Unknown item slot '" + slot + "'.", nameof(slot));
            Slot = slot;
            Kind = string.IsNullOrEmpty(kind) ? null : kind;
        }

        /// <summary>True when an item of this slot and kind satisfies the requirement.</summary>
        public bool IsMetBy(string slot, string kind) => slot == Slot && (Kind == null || kind == Kind);

        public override string ToString() => Kind == null ? Slot : Slot + " (" + Kind + ")";
    }

    /// <summary>The closed effect vocabulary (design: #boons rule 2; spec D part 1 §5.6). One handler each in <c>BoonOverlay</c>.</summary>
    public static class BoonEffectTypes
    {
        public const string Stat = "stat";
        public const string Modifier = "modifier";
        public const string AbilityOverride = "abilityOverride";
        public const string AddElement = "addElement";
        public const string AddTag = "addTag";
        public const string GrantAbility = "grantAbility";

        public static bool IsKnown(string type)
            => type == Stat || type == Modifier || type == AbilityOverride || type == AddElement || type == AddTag || type == GrantAbility;

        public const string KnownList = "stat, modifier, abilityOverride, addElement, addTag, grantAbility";
    }

    /// <summary>
    /// The ability fields an <c>abilityOverride</c> may name (spec D part 1 §5.6). Numeric fields take an
    /// additive <c>amount</c>; the two skeleton fields take a string <c>value</c> and fail closed at unit
    /// build (§6.7). <see cref="AppliesTo"/> is the "ignored for that ability" rule: a field that has no
    /// meaning on a particular resolved ability (an apex on a direct attack, a climb on an attack) is
    /// silently skipped there under a slot target and refused at link under an ability-id target.
    /// </summary>
    public static class AbilityFields
    {
        public const string Range = "range";
        public const string MinRange = "minRange";
        public const string Damage = "damage";
        public const string Cost = "cost";
        public const string Apex = "apex";
        public const string Hits = "hits";
        public const string Climb = "climb";
        public const string JumpHeight = "jumpHeight";
        public const string Trajectory = "trajectory";
        public const string LineOfSight = "lineOfSight";

        public const string KnownList = "range, minRange, damage, cost, apex, hits, climb, jumpHeight, trajectory, lineOfSight";

        public static bool IsKnown(string field) => IsNumeric(field) || IsValueField(field);

        /// <summary>Fields that take an integer <c>amount</c>.</summary>
        public static bool IsNumeric(string field)
            => field == Range || field == MinRange || field == Damage || field == Cost || field == Apex || field == Hits || field == Climb || field == JumpHeight;

        /// <summary>Fields that take a string <c>value</c> (both skeletons).</summary>
        public static bool IsValueField(string field) => field == Trajectory || field == LineOfSight;

        /// <summary>The skeleton fields: parse, live in Core, and throw <c>NotSupportedException</c> the moment a unit would need them (spec §6.7).</summary>
        public static bool IsSkeleton(string field) => field == Hits || field == Trajectory || field == LineOfSight;

        /// <summary>Fields that decide where an ability can reach: an override of one of these is revealed the moment a shot or move the opponent thought impossible lands (spec §6.5 (a)).</summary>
        public static bool IsAiming(string field)
            => field == Range || field == MinRange || field == Apex || field == Climb || field == JumpHeight;

        /// <summary>Whether the field means anything on this ability. Apex only on an arc; movement numbers only on movement; attack numbers only on attacks.</summary>
        public static bool AppliesTo(string field, AbilityDef ability)
        {
            if (ability == null) return false;
            if (field == Cost) return true;
            var attack = ability as AttackDef;
            if (attack != null)
            {
                switch (field)
                {
                    case Range:
                    case MinRange:
                    case Damage:
                    case Hits:
                    case Trajectory:
                    case LineOfSight:
                        return true;
                    case Apex:
                        return attack.Trajectory == Trajectories.Arc;
                    default:
                        return false;
                }
            }
            var movement = ability as Movement.MovementDef;
            if (movement != null)
            {
                switch (field)
                {
                    case Range:
                        return true;
                    case Climb:
                        return movement.Mode == Movement.MovementModes.Walk;
                    case JumpHeight:
                        return movement.Mode == Movement.MovementModes.Jump;
                    default:
                        return false;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// One effect of a boon: a flat record whose <see cref="Type"/> decides which fields are set. Loaded
    /// fail-closed: an unknown type or a field that does not belong to the type is a load error (spec §5.6).
    /// </summary>
    public sealed class BoonEffect
    {
        /// <summary>One of <see cref="BoonEffectTypes"/>.</summary>
        public string Type { get; }

        /// <summary><c>stat</c>: the stat key (hp, ap, power.&lt;lane&gt;, defense.&lt;lane&gt;).</summary>
        public string Key { get; }

        /// <summary><c>stat</c> and numeric <c>abilityOverride</c>: the signed amount. Never 0. Null on the other types and on a value override.</summary>
        public int? Amount { get; }

        /// <summary><c>modifier</c>: the modifier id.</summary>
        public string Id { get; }

        /// <summary><c>abilityOverride</c>, <c>addElement</c>, <c>addTag</c>, <c>grantAbility</c>: a slot or (for an override) an ability id.</summary>
        public string Target { get; }

        /// <summary><c>abilityOverride</c>: one of <see cref="AbilityFields"/>.</summary>
        public string Field { get; }

        /// <summary><c>abilityOverride</c> on a value field: the new value (<c>direct</c> / <c>arc</c> / <c>sky</c>, or <c>true</c> / <c>false</c>).</summary>
        public string Value { get; }

        /// <summary><c>addElement</c>: the element.</summary>
        public string Element { get; }

        /// <summary><c>addTag</c>: the tag.</summary>
        public string Tag { get; }

        /// <summary><c>grantAbility</c>: the ability id.</summary>
        public string Ability { get; }

        /// <summary>True when <see cref="Target"/> names a slot rather than an ability id.</summary>
        public bool TargetIsSlot => Target != null && ItemSlots.IsKnown(Target);

        public BoonEffect(string type, string key = null, int? amount = null, string id = null, string target = null,
            string field = null, string value = null, string element = null, string tag = null, string ability = null)
        {
            if (!BoonEffectTypes.IsKnown(type)) throw new ArgumentException("Unknown boon effect type '" + type + "'.", nameof(type));
            Type = type;
            Key = key;
            Amount = amount;
            Id = id;
            Target = target;
            Field = field;
            Value = value;
            Element = element;
            Tag = tag;
            Ability = ability;
        }

        internal static BoonEffect FromJson(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            string type = MapJson.RequireString(obj, "type", where);
            if (!BoonEffectTypes.IsKnown(type))
                throw new MapLoadException($"{where} has unknown effect type '{type}' (known: {BoonEffectTypes.KnownList}).");

            string[] allowed;
            switch (type)
            {
                case BoonEffectTypes.Stat: allowed = new[] { "key", "amount" }; break;
                case BoonEffectTypes.Modifier: allowed = new[] { "id" }; break;
                case BoonEffectTypes.AbilityOverride: allowed = new[] { "target", "field", "amount", "value" }; break;
                case BoonEffectTypes.AddElement: allowed = new[] { "target", "element" }; break;
                case BoonEffectTypes.AddTag: allowed = new[] { "target", "tag" }; break;
                default: allowed = new[] { "target", "ability" }; break;
            }
            foreach (var property in obj.Properties())
            {
                if (property.Name == "type") continue;
                if (Array.IndexOf(allowed, property.Name) < 0)
                    throw new MapLoadException($"{where} of type '{type}' has field '{property.Name}' that does not belong to it (allowed: {string.Join(", ", allowed)}).");
            }

            switch (type)
            {
                case BoonEffectTypes.Stat:
                {
                    string key = MapJson.RequireString(obj, "key", where);
                    int amount = MapJson.RequireInt(obj, "amount", where);
                    if (amount == 0) throw new MapLoadException($"{where}.amount must not be 0.");
                    if (key != StatBlock.HpKey && key != StatBlock.ApKey && StatBlock.DamageTypeOf(key) == null)
                        throw new MapLoadException($"{where}.key '{key}' is not a stat (expected hp, ap, power.<lane> or defense.<lane>).");
                    return new BoonEffect(type, key: key, amount: amount);
                }
                case BoonEffectTypes.Modifier:
                    return new BoonEffect(type, id: MapJson.RequireString(obj, "id", where));
                case BoonEffectTypes.AbilityOverride:
                {
                    string target = MapJson.RequireString(obj, "target", where);
                    string field = MapJson.RequireString(obj, "field", where);
                    if (!AbilityFields.IsKnown(field))
                        throw new MapLoadException($"{where}.field '{field}' is not an ability field (known: {AbilityFields.KnownList}).");
                    bool hasAmount = obj["amount"] != null && obj["amount"].Type != JTokenType.Null;
                    bool hasValue = obj["value"] != null && obj["value"].Type != JTokenType.Null;
                    if (AbilityFields.IsNumeric(field))
                    {
                        if (hasValue) throw new MapLoadException($"{where}.field '{field}' takes an 'amount', not a 'value'.");
                        int amount = MapJson.RequireInt(obj, "amount", where);
                        if (amount == 0) throw new MapLoadException($"{where}.amount must not be 0.");
                        return new BoonEffect(type, target: target, field: field, amount: amount);
                    }
                    if (hasAmount) throw new MapLoadException($"{where}.field '{field}' takes a 'value', not an 'amount'.");
                    string value = MapJson.RequireString(obj, "value", where);
                    if (field == AbilityFields.Trajectory && !Trajectories.IsKnown(value))
                        throw new MapLoadException($"{where}.value '{value}' is not a trajectory (known: {Trajectories.Direct}, {Trajectories.Arc}, {Trajectories.Sky}).");
                    if (field == AbilityFields.LineOfSight && value != "true" && value != "false")
                        throw new MapLoadException($"{where}.value for lineOfSight must be 'true' or 'false' (got '{value}').");
                    return new BoonEffect(type, target: target, field: field, value: value);
                }
                case BoonEffectTypes.AddElement:
                    return new BoonEffect(type, target: MapJson.RequireString(obj, "target", where), element: MapJson.RequireString(obj, "element", where));
                case BoonEffectTypes.AddTag:
                    return new BoonEffect(type, target: MapJson.RequireString(obj, "target", where), tag: MapJson.RequireString(obj, "tag", where));
                default:
                    return new BoonEffect(type, target: MapJson.RequireString(obj, "target", where), ability: MapJson.RequireString(obj, "ability", where));
            }
        }

        public override string ToString()
        {
            switch (Type)
            {
                case BoonEffectTypes.Stat: return $"stat {Key} {(Amount >= 0 ? "+" : "")}{Amount}";
                case BoonEffectTypes.Modifier: return $"modifier {Id}";
                case BoonEffectTypes.AbilityOverride: return Amount.HasValue ? $"override {Target}.{Field} {(Amount >= 0 ? "+" : "")}{Amount}" : $"override {Target}.{Field} = {Value}";
                case BoonEffectTypes.AddElement: return $"addElement {Target} {Element}";
                case BoonEffectTypes.AddTag: return $"addTag {Target} {Tag}";
                default: return $"grant {Target} {Ability}";
            }
        }
    }

    /// <summary>
    /// One boon from <c>boons/*.json</c> (design: #boons, #blessing, #enchant, #sigil): a kind, the lineage
    /// it belongs to, what gear it needs, and a list of effects from the closed vocabulary. Immutable after
    /// load; a unit folds its boons into a <c>BoonOverlay</c> and never mutates a definition (ADR-034).
    /// The single-file rules (which effect types each kind allows, targets against <c>requires.slot</c>,
    /// <c>stackable</c> against the effect list) are checked here; every cross-file rule is the catalogue's.
    /// </summary>
    public sealed class BoonDef : IContentDef
    {
        private readonly List<BoonEffect> _effects;
        private readonly List<string> _exclusiveGroups;

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>Presentation key for an icon, or null.</summary>
        public string Icon { get; }

        /// <summary>One of <see cref="BoonKinds"/>.</summary>
        public string Kind { get; }

        /// <summary>The lineage this boon belongs to (a <see cref="LineageDef"/> id).</summary>
        public string LineageId { get; }

        /// <summary>The gear an Enchant or Sigil needs. Always null on a Blessing.</summary>
        public BoonRequirement Requires { get; }

        /// <summary>The effects, in authored order. Never empty.</summary>
        public IReadOnlyList<BoonEffect> Effects => _effects;

        /// <summary>May be drafted again while owned. Only legal when every effect is <c>stat</c> or <c>abilityOverride</c>.</summary>
        public bool Stackable { get; }

        /// <summary>Exclusivity groups (design: <c>q-boons-exclusive</c>): the draft never offers a boon sharing a group with an owned one. Sorted, unique.</summary>
        public IReadOnlyList<string> ExclusiveGroups => _exclusiveGroups;

        public bool IsBlessing => Kind == BoonKinds.Blessing;
        public bool IsEnchant => Kind == BoonKinds.Enchant;
        public bool IsSigil => Kind == BoonKinds.Sigil;

        public BoonDef(string id, string name, string kind, string lineageId, BoonRequirement requires, List<BoonEffect> effects,
            bool stackable = false, List<string> exclusiveGroups = null, string description = null, string icon = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? id;
            if (!BoonKinds.IsKnown(kind)) throw new ArgumentException("Unknown boon kind '" + kind + "'.", nameof(kind));
            if (string.IsNullOrEmpty(lineageId)) throw new ArgumentException("A boon must name its lineage.", nameof(lineageId));
            if (effects == null || effects.Count == 0) throw new ArgumentException("A boon needs at least one effect.", nameof(effects));
            Kind = kind;
            LineageId = lineageId;
            Requires = requires;
            _effects = new List<BoonEffect>(effects);
            Stackable = stackable;
            _exclusiveGroups = new List<string>();
            if (exclusiveGroups != null)
            {
                foreach (string group in exclusiveGroups)
                    if (!string.IsNullOrEmpty(group) && !_exclusiveGroups.Contains(group)) _exclusiveGroups.Add(group);
            }
            _exclusiveGroups.Sort(string.CompareOrdinal);
            Description = description;
            Icon = string.IsNullOrWhiteSpace(icon) ? null : icon;
            CheckKindRules($"boon '{id}'");
        }

        public bool SharesExclusiveGroupWith(BoonDef other)
        {
            if (other == null) return false;
            for (int i = 0; i < _exclusiveGroups.Count; i++)
                if (other._exclusiveGroups.Contains(_exclusiveGroups[i])) return true;
            return false;
        }

        /// <summary>
        /// The gear predicate the catalogue and the draft share (spec §5.5, §7.2): a Blessing always; an
        /// Enchant or Sigil when the item in <c>requires.slot</c> has the required kind, every ability-id
        /// target is in <paramref name="abilityIds"/>, and no granted ability already is. The catalogue asks it
        /// per item (the item's own abilities); the draft asks it of the whole build (the unit's abilities).
        /// </summary>
        public bool IsApplicableTo(string slot, string kind, IReadOnlyList<string> abilityIds)
        {
            if (Requires == null) return true;
            if (!Requires.IsMetBy(slot, kind)) return false;
            for (int i = 0; i < _effects.Count; i++)
            {
                BoonEffect effect = _effects[i];
                if (effect.Type == BoonEffectTypes.AbilityOverride && !effect.TargetIsSlot && !Contains(abilityIds, effect.Target)) return false;
                if (effect.Type == BoonEffectTypes.GrantAbility && Contains(abilityIds, effect.Ability)) return false;
            }
            return true;
        }

        private static bool Contains(IReadOnlyList<string> ids, string id)
        {
            if (ids == null) return false;
            for (int i = 0; i < ids.Count; i++) if (ids[i] == id) return true;
            return false;
        }

        private void CheckKindRules(string where)
        {
            if (Kind == BoonKinds.Blessing && Requires != null)
                throw new MapLoadException($"{where}: a blessing must not declare 'requires'.");
            if (Kind != BoonKinds.Blessing && Requires == null)
                throw new MapLoadException($"{where}: a {Kind} must declare 'requires' with a slot.");

            bool stackableLegal = true;
            for (int i = 0; i < _effects.Count; i++)
            {
                BoonEffect effect = _effects[i];
                string at = $"{where} effects[{i}]";
                if (effect.Type != BoonEffectTypes.Stat && effect.Type != BoonEffectTypes.AbilityOverride) stackableLegal = false;
                switch (Kind)
                {
                    case BoonKinds.Blessing:
                        if (effect.Type != BoonEffectTypes.Stat && effect.Type != BoonEffectTypes.Modifier && effect.Type != BoonEffectTypes.AbilityOverride)
                            throw new MapLoadException($"{at}: a blessing may only have stat, modifier or abilityOverride effects (got '{effect.Type}').");
                        if (effect.Type == BoonEffectTypes.AbilityOverride && effect.TargetIsSlot)
                            throw new MapLoadException($"{at}: a blessing's abilityOverride must target an innate ability id, not the slot '{effect.Target}'.");
                        break;
                    case BoonKinds.Enchant:
                        if (effect.Type == BoonEffectTypes.Stat || effect.Type == BoonEffectTypes.GrantAbility)
                            throw new MapLoadException($"{at}: an enchant may only have abilityOverride, addElement, addTag or modifier effects (got '{effect.Type}').");
                        if (effect.Target != null && effect.TargetIsSlot && effect.Target != Requires.Slot)
                            throw new MapLoadException($"{at}: target '{effect.Target}' is not the enchant's slot '{Requires.Slot}'.");
                        if ((effect.Type == BoonEffectTypes.AddElement || effect.Type == BoonEffectTypes.AddTag) && !effect.TargetIsSlot)
                            throw new MapLoadException($"{at}: {effect.Type} must target the enchant's slot '{Requires.Slot}' (got '{effect.Target}').");
                        break;
                    default:
                        if (effect.Type != BoonEffectTypes.GrantAbility)
                            throw new MapLoadException($"{at}: a sigil may only have grantAbility effects (got '{effect.Type}').");
                        if (effect.Target != Requires.Slot)
                            throw new MapLoadException($"{at}: target '{effect.Target}' is not the sigil's slot '{Requires.Slot}'.");
                        break;
                }
            }
            if (Stackable && !stackableLegal)
                throw new MapLoadException($"{where}: 'stackable' is only legal when every effect is stat or abilityOverride (a modifier, element, tag or ability cannot be held twice).");
        }

        public static BoonDef FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new MapLoadException("Boon JSON is empty.");

            JObject root;
            try
            {
                root = JObject.Parse(json);
            }
            catch (JsonException e)
            {
                throw new MapLoadException("Boon JSON is malformed: " + e.Message, e);
            }

            int version = MapJson.RequireInt(root, "version", "boon");
            if (version != 1) throw new MapLoadException($"Unsupported boon version {version} (expected 1).");

            string id = MapJson.RequireString(root, "id", "boon");
            string where = $"boon '{id}'";
            string name = MapJson.OptionalString(root, "name", where) ?? id;
            string description = MapJson.OptionalString(root, "description", where);
            string icon = MapJson.OptionalString(root, "icon", where);

            string kind = MapJson.RequireString(root, "kind", where);
            if (!BoonKinds.IsKnown(kind))
                throw new MapLoadException($"{where} has unknown kind '{kind}' (known: {BoonKinds.Blessing}, {BoonKinds.Enchant}, {BoonKinds.Sigil}).");
            string lineage = MapJson.RequireString(root, "lineage", where);

            BoonRequirement requires = null;
            var requiresToken = root["requires"];
            if (requiresToken != null && requiresToken.Type != JTokenType.Null)
            {
                if (!(requiresToken is JObject req)) throw new MapLoadException($"{where} field 'requires' must be an object.");
                foreach (var property in req.Properties())
                {
                    if (property.Name != "slot" && property.Name != "kind")
                        throw new MapLoadException($"{where}.requires has unknown key '{property.Name}' (known: slot, kind).");
                }
                string slot = MapJson.RequireString(req, "slot", where + ".requires");
                if (!ItemSlots.IsKnown(slot))
                    throw new MapLoadException($"{where}.requires.slot '{slot}' is not a slot (expected {ItemSlots.Weapon}, {ItemSlots.Crown}, {ItemSlots.Boots} or {ItemSlots.Armour}).");
                requires = new BoonRequirement(slot, MapJson.OptionalString(req, "kind", where + ".requires"));
            }

            if (!(MapJson.Require(root, "effects", where) is JArray effectsArray))
                throw new MapLoadException($"{where} field 'effects' must be an array.");
            if (effectsArray.Count == 0) throw new MapLoadException($"{where}.effects must not be empty.");
            var effects = new List<BoonEffect>(effectsArray.Count);
            for (int i = 0; i < effectsArray.Count; i++)
            {
                var effectObj = effectsArray[i] as JObject;
                if (effectObj == null) throw new MapLoadException($"{where}.effects[{i}] must be an object.");
                effects.Add(BoonEffect.FromJson(effectObj, $"{where}.effects[{i}]"));
            }

            bool stackable = false;
            var stackableToken = root["stackable"];
            if (stackableToken != null && stackableToken.Type != JTokenType.Null)
            {
                if (stackableToken.Type != JTokenType.Boolean) throw new MapLoadException($"{where} field 'stackable' must be a boolean.");
                stackable = (bool)stackableToken;
            }
            var exclusive = MapJson.OptionalStringList(root, "exclusive", where);

            return new BoonDef(id, name, kind, lineage, requires, effects, stackable, exclusive, description, icon);
        }
    }
}
