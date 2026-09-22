using System;
using System.Collections.Generic;
using Mimas.Core.Combat;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Protocol
{
    /// <summary>Thrown when a frame is not the shape the protocol says it is: an unknown type, a missing field, a wrong kind of token.</summary>
    public sealed class WireException : Exception
    {
        public WireException(string message) : base(message)
        {
        }

        public WireException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// The hand-written codec for everything that crosses the wire (ADR-027): commands, events and player
    /// views. Hand-written on purpose — no attributes, no reflection, no <c>TypeNameHandling</c> — because
    /// the server must never be able to deserialise its way into running client-supplied types, and because
    /// a field-by-field codec is the only version of this that a test can prove round-trips.
    /// <para>
    /// Rules: enums are lower-camel strings and are read case-sensitively (a bare integer is a malformed
    /// frame, not a silent default); a <see cref="Hex"/> is <c>[q, r]</c>; a hidden ability or modifier id is
    /// <c>null</c> and stays <c>null</c>; encoding the same object twice produces byte-identical JSON.
    /// </para>
    /// </summary>
    public static class Wire
    {
        // ---- primitives ------------------------------------------------------------------------------

        public static JArray Hex(Hex h) => new JArray(h.Q, h.R);

        public static Hex ReadHex(JToken t)
        {
            var a = t as JArray;
            if (a == null || a.Count != 2) throw new WireException("A hex must be an array [q, r].");
            return new Hex(Int(a[0], "hex.q"), Int(a[1], "hex.r"));
        }

        private static JArray Hexes(IReadOnlyList<Hex> hexes)
        {
            var a = new JArray();
            for (int i = 0; i < hexes.Count; i++) a.Add(Hex(hexes[i]));
            return a;
        }

        private static List<Hex> ReadHexes(JToken t, string where)
        {
            var a = t as JArray;
            if (a == null) throw new WireException($"{where} must be an array of hexes.");
            var list = new List<Hex>(a.Count);
            for (int i = 0; i < a.Count; i++) list.Add(ReadHex(a[i]));
            return list;
        }

        // ---- commands --------------------------------------------------------------------------------

        public static JObject Command(Command c)
        {
            if (c == null) throw new ArgumentNullException(nameof(c));
            switch (c)
            {
                case MoveCommand m:
                {
                    var o = new JObject { ["type"] = "move", ["player"] = m.Player, ["unitId"] = m.UnitId, ["abilityId"] = m.AbilityId };
                    o["to"] = Hex(m.Destination);
                    return o;
                }
                case AttackCommand a:
                {
                    var o = new JObject { ["type"] = "attack", ["player"] = a.Player, ["unitId"] = a.UnitId, ["abilityId"] = a.AbilityId };
                    o["target"] = Hex(a.Target);
                    return o;
                }
                case EndTurnCommand e:
                    return new JObject { ["type"] = "endTurn", ["player"] = e.Player, ["reason"] = EndTurnReasonName(e.Reason) };
                case ResignCommand r:
                    return new JObject { ["type"] = "resign", ["player"] = r.Player, ["reason"] = ResignReasonName(r.Reason) };
                default:
                    throw new WireException("Cannot encode command type " + c.GetType().Name + ".");
            }
        }

        public static Command ReadCommand(JObject o)
        {
            string type = Str(o, "type", "command");
            int player = Int(o, "player", "command");
            switch (type)
            {
                case "move":
                    return new MoveCommand(player, Int(o, "unitId", "move"), Str(o, "abilityId", "move"), ReadHex(Require(o, "to", "move")));
                case "attack":
                    return new AttackCommand(player, Int(o, "unitId", "attack"), Str(o, "abilityId", "attack"), ReadHex(Require(o, "target", "attack")));
                case "endTurn":
                    return new EndTurnCommand(player, ReadEndTurnReason(Str(o, "reason", "endTurn")));
                case "resign":
                    return new ResignCommand(player, ReadResignReason(Str(o, "reason", "resign")));
                default:
                    throw new WireException($"Unknown command type '{type}'.");
            }
        }

        // ---- events ----------------------------------------------------------------------------------

        public static JObject Event(MatchEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            switch (e)
            {
                case TurnStartedEvent x:
                    return new JObject { ["type"] = "turnStarted", ["player"] = x.Player, ["turnNumber"] = x.TurnNumber };
                case UnitMovedEvent x:
                {
                    var o = new JObject { ["type"] = "unitMoved", ["unitId"] = x.UnitId, ["movementId"] = x.MovementId };
                    o["plan"] = MovePlan(x.Plan);
                    return o;
                }
                case ApSpentEvent x:
                    return new JObject { ["type"] = "apSpent", ["unitId"] = x.UnitId, ["abilityId"] = x.AbilityId, ["amount"] = x.Amount, ["remaining"] = x.Remaining };
                case AttackResolvedEvent x:
                {
                    var o = new JObject { ["type"] = "attackResolved", ["attackerId"] = x.AttackerId, ["targetId"] = x.TargetId, ["abilityId"] = x.AbilityId };
                    o["breakdown"] = Breakdown(x.Breakdown);
                    o["damage"] = x.Damage;
                    o["targetHpAfter"] = x.TargetHpAfter;
                    o["targetIsProp"] = x.TargetIsProp;
                    return o;
                }
                case AbilityRevealedEvent x:
                    return new JObject { ["type"] = "abilityRevealed", ["unitId"] = x.UnitId, ["abilityId"] = x.AbilityId, ["toPlayer"] = x.ToPlayer };
                case ModifierRevealedEvent x:
                    return new JObject { ["type"] = "modifierRevealed", ["unitId"] = x.UnitId, ["modifierId"] = x.ModifierId, ["toPlayer"] = x.ToPlayer };
                case PropDestroyedEvent x:
                    return new JObject { ["type"] = "propDestroyed", ["propId"] = x.PropId };
                case UnitDiedEvent x:
                    return new JObject { ["type"] = "unitDied", ["unitId"] = x.UnitId };
                case TurnEndedEvent x:
                    return new JObject
                    {
                        ["type"] = "turnEnded",
                        ["player"] = x.Player,
                        ["turnNumber"] = x.TurnNumber,
                        ["reason"] = EndTurnReasonName(x.Reason),
                        ["acted"] = x.Acted,
                    };
                case MatchEndedEvent x:
                    return new JObject { ["type"] = "matchEnded", ["winner"] = x.Winner, ["reason"] = MatchEndReasonName(x.Reason) };
                default:
                    throw new WireException("Cannot encode event type " + e.GetType().Name + ".");
            }
        }

        public static MatchEvent ReadEvent(JObject o)
        {
            string type = Str(o, "type", "event");
            switch (type)
            {
                case "turnStarted":
                    return new TurnStartedEvent(Int(o, "player", type), Int(o, "turnNumber", type));
                case "unitMoved":
                    return new UnitMovedEvent(Int(o, "unitId", type), NullableStr(o, "movementId", type), ReadMovePlan(Obj(o, "plan", type)));
                case "apSpent":
                    return new ApSpentEvent(Int(o, "unitId", type), Str(o, "abilityId", type), Int(o, "amount", type), Int(o, "remaining", type));
                case "attackResolved":
                    return new AttackResolvedEvent(Int(o, "attackerId", type), Int(o, "targetId", type), Str(o, "abilityId", type),
                        ReadBreakdown(Obj(o, "breakdown", type)), Int(o, "damage", type), Int(o, "targetHpAfter", type), Bool(o, "targetIsProp", type));
                case "abilityRevealed":
                    return new AbilityRevealedEvent(Int(o, "unitId", type), Str(o, "abilityId", type), Int(o, "toPlayer", type));
                case "modifierRevealed":
                    return new ModifierRevealedEvent(Int(o, "unitId", type), Str(o, "modifierId", type), Int(o, "toPlayer", type));
                case "propDestroyed":
                    return new PropDestroyedEvent(Int(o, "propId", type));
                case "unitDied":
                    return new UnitDiedEvent(Int(o, "unitId", type));
                case "turnEnded":
                    return new TurnEndedEvent(Int(o, "player", type), Int(o, "turnNumber", type), ReadEndTurnReason(Str(o, "reason", type)), Bool(o, "acted", type));
                case "matchEnded":
                    return new MatchEndedEvent(Int(o, "winner", type), ReadMatchEndReason(Str(o, "reason", type)));
                default:
                    throw new WireException($"Unknown event type '{type}'.");
            }
        }

        public static JArray Events(IReadOnlyList<MatchEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var a = new JArray();
            for (int i = 0; i < events.Count; i++) a.Add(Event(events[i]));
            return a;
        }

        public static List<MatchEvent> ReadEvents(JArray a)
        {
            if (a == null) throw new WireException("events must be an array.");
            var list = new List<MatchEvent>(a.Count);
            for (int i = 0; i < a.Count; i++)
            {
                var o = a[i] as JObject;
                if (o == null) throw new WireException($"events[{i}] must be an object.");
                list.Add(ReadEvent(o));
            }
            return list;
        }

        // ---- damage ----------------------------------------------------------------------------------

        public static JObject Breakdown(DamageBreakdown b)
        {
            if (b == null) throw new ArgumentNullException(nameof(b));
            var lines = new JArray();
            for (int i = 0; i < b.Lines.Count; i++)
            {
                DamageLine line = b.Lines[i];
                lines.Add(new JObject
                {
                    ["kind"] = DamageLineKindName(line.Kind),
                    ["id"] = line.Id,
                    ["owner"] = DamageLineOwnerName(line.Owner),
                    ["ownerUnitId"] = line.OwnerUnitId,
                    ["amount"] = line.Amount,
                    ["hidden"] = line.Hidden,
                });
            }
            return new JObject { ["total"] = b.Total, ["unknown"] = b.UnknownCount, ["lines"] = lines };
        }

        public static DamageBreakdown ReadBreakdown(JObject o)
        {
            int total = Int(o, "total", "breakdown");
            int unknown = Int(o, "unknown", "breakdown");
            var arr = Require(o, "lines", "breakdown") as JArray;
            if (arr == null) throw new WireException("breakdown.lines must be an array.");
            var lines = new List<DamageLine>(arr.Count);
            for (int i = 0; i < arr.Count; i++)
            {
                var l = arr[i] as JObject;
                if (l == null) throw new WireException($"breakdown.lines[{i}] must be an object.");
                lines.Add(new DamageLine(ReadDamageLineKind(Str(l, "kind", "line")), Str(l, "id", "line"),
                    ReadDamageLineOwner(Str(l, "owner", "line")), Int(l, "ownerUnitId", "line"), Int(l, "amount", "line"), Bool(l, "hidden", "line")));
            }
            var breakdown = new DamageBreakdown(lines, unknown);
            // The total is the sum of the visible lines floored at zero; disagreement means a corrupt frame.
            if (breakdown.Total != total) throw new WireException($"breakdown.total {total} does not match its lines ({breakdown.Total}).");
            return breakdown;
        }

        // ---- movement --------------------------------------------------------------------------------

        public static JObject MovePlan(MovePlan p)
        {
            if (p == null) throw new ArgumentNullException(nameof(p));
            var o = new JObject();
            o["origin"] = Hex(p.Origin);
            o["destination"] = Hex(p.Destination);
            o["path"] = Hexes(p.Path);
            o["entered"] = Hexes(p.EnteredTiles);
            o["traversal"] = TraversalName(p.Traversal);
            o["cost"] = p.Cost;
            return o;
        }

        public static MovePlan ReadMovePlan(JObject o)
        {
            Hex origin = ReadHex(Require(o, "origin", "movePlan"));
            Hex destination = ReadHex(Require(o, "destination", "movePlan"));
            List<Hex> path = ReadHexes(Require(o, "path", "movePlan"), "movePlan.path");
            List<Hex> entered = ReadHexes(Require(o, "entered", "movePlan"), "movePlan.entered");
            TraversalKind traversal = ReadTraversal(Str(o, "traversal", "movePlan"));
            int cost = Int(o, "cost", "movePlan");
            try
            {
                return new MovePlan(origin, destination, path, entered, traversal, cost);
            }
            catch (ArgumentException e)
            {
                throw new WireException("movePlan is not a legal plan: " + e.Message, e);
            }
        }

        // ---- views -----------------------------------------------------------------------------------

        public static JObject View(PlayerView v)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            var units = new JArray();
            for (int i = 0; i < v.Units.Count; i++)
            {
                UnitView u = v.Units[i];
                var items = new JArray();
                for (int k = 0; k < u.ItemIds.Count; k++) items.Add(u.ItemIds[k]);

                var abilities = new JArray();
                for (int a = 0; a < u.Abilities.Count; a++)
                    abilities.Add(new JObject { ["id"] = u.Abilities[a].Id, ["source"] = u.Abilities[a].SourceItemId });

                var modifiers = new JArray();
                for (int m = 0; m < u.Modifiers.Count; m++)
                    modifiers.Add(new JObject { ["id"] = u.Modifiers[m].Id });

                var uo = new JObject { ["id"] = u.Id, ["owner"] = u.Owner };
                uo["items"] = items;
                uo["pos"] = Hex(u.Position);
                uo["hp"] = u.Hp;
                uo["maxHp"] = u.MaxHp;
                uo["ap"] = u.Ap;
                uo["apPerTurn"] = u.ApPerTurn;
                uo["body"] = u.BodyHeight;
                uo["aim"] = u.AimHeight;
                uo["mine"] = u.IsMine;
                uo["abilities"] = abilities;
                uo["modifiers"] = modifiers;
                units.Add(uo);
            }

            var props = new JArray();
            for (int i = 0; i < v.Props.Count; i++)
            {
                PropView p = v.Props[i];
                var po = new JObject { ["id"] = p.Id, ["def"] = p.DefId };
                po["pos"] = Hex(p.Position);
                po["hp"] = p.Hp;
                po["maxHp"] = p.MaxHp;
                po["alive"] = p.IsAlive;
                po["body"] = p.BodyHeight;
                po["aim"] = p.AimHeight;
                po["damageable"] = p.IsDamageable;
                props.Add(po);
            }

            var o = new JObject
            {
                ["viewer"] = v.Viewer,
                ["activePlayer"] = v.ActivePlayer,
                ["turnNumber"] = v.TurnNumber,
                ["acted"] = v.ActedThisTurn,
                ["over"] = v.IsOver,
                ["winner"] = v.Winner,
                ["mapId"] = v.MapId,
            };
            o["units"] = units;
            o["props"] = props;
            return o;
        }

        public static PlayerView ReadView(JObject o)
        {
            int viewer = Int(o, "viewer", "view");
            var unitArr = Require(o, "units", "view") as JArray;
            if (unitArr == null) throw new WireException("view.units must be an array.");
            var units = new List<UnitView>(unitArr.Count);
            for (int i = 0; i < unitArr.Count; i++)
            {
                var u = unitArr[i] as JObject;
                if (u == null) throw new WireException($"view.units[{i}] must be an object.");

                var itemArr = Require(u, "items", "unit") as JArray;
                if (itemArr == null) throw new WireException("unit.items must be an array.");
                var items = new List<string>(itemArr.Count);
                for (int k = 0; k < itemArr.Count; k++) items.Add(itemArr[k].Value<string>());

                var abilityArr = Require(u, "abilities", "unit") as JArray;
                if (abilityArr == null) throw new WireException("unit.abilities must be an array.");
                var abilities = new List<KnownEntry>(abilityArr.Count);
                for (int a = 0; a < abilityArr.Count; a++)
                {
                    var entry = abilityArr[a] as JObject;
                    if (entry == null) throw new WireException($"unit.abilities[{a}] must be an object.");
                    abilities.Add(new KnownEntry(NullableStr(entry, "id", "ability"), NullableStr(entry, "source", "ability")));
                }

                var modifierArr = Require(u, "modifiers", "unit") as JArray;
                if (modifierArr == null) throw new WireException("unit.modifiers must be an array.");
                var modifiers = new List<KnownEntry>(modifierArr.Count);
                for (int m = 0; m < modifierArr.Count; m++)
                {
                    var entry = modifierArr[m] as JObject;
                    if (entry == null) throw new WireException($"unit.modifiers[{m}] must be an object.");
                    modifiers.Add(new KnownEntry(NullableStr(entry, "id", "modifier")));
                }

                units.Add(new UnitView(Int(u, "id", "unit"), Int(u, "owner", "unit"), items, ReadHex(Require(u, "pos", "unit")),
                    Int(u, "hp", "unit"), Int(u, "maxHp", "unit"), Int(u, "ap", "unit"), Int(u, "apPerTurn", "unit"), Bool(u, "mine", "unit"),
                    abilities, modifiers, Int(u, "body", "unit"), Int(u, "aim", "unit")));
            }

            var propArr = Require(o, "props", "view") as JArray;
            if (propArr == null) throw new WireException("view.props must be an array.");
            var props = new List<PropView>(propArr.Count);
            for (int i = 0; i < propArr.Count; i++)
            {
                var p = propArr[i] as JObject;
                if (p == null) throw new WireException($"view.props[{i}] must be an object.");
                props.Add(new PropView(Int(p, "id", "prop"), Str(p, "def", "prop"), ReadHex(Require(p, "pos", "prop")),
                    Int(p, "hp", "prop"), Int(p, "maxHp", "prop"), Bool(p, "alive", "prop"),
                    Int(p, "body", "prop"), Int(p, "aim", "prop"), Bool(p, "damageable", "prop")));
            }

            return PlayerView.Create(viewer, Int(o, "activePlayer", "view"), Int(o, "turnNumber", "view"), Bool(o, "acted", "view"),
                Bool(o, "over", "view"), Int(o, "winner", "view"), Str(o, "mapId", "view"), units, props);
        }

        // ---- enum names ------------------------------------------------------------------------------
        // Hand-written both ways: an unknown string is a malformed frame, never a silent default, and no
        // enum's integer value is ever load-bearing on the wire.

        private static string TraversalName(TraversalKind k)
        {
            switch (k)
            {
                case TraversalKind.Ground: return "ground";
                case TraversalKind.Leap: return "leap";
                case TraversalKind.Blink: return "blink";
                default: throw new WireException("Unknown traversal " + k + ".");
            }
        }

        private static TraversalKind ReadTraversal(string s)
        {
            switch (s)
            {
                case "ground": return TraversalKind.Ground;
                case "leap": return TraversalKind.Leap;
                case "blink": return TraversalKind.Blink;
                default: throw new WireException($"Unknown traversal '{s}'.");
            }
        }

        private static string DamageLineKindName(DamageLineKind k)
        {
            switch (k)
            {
                case DamageLineKind.Base: return "base";
                case DamageLineKind.Power: return "power";
                case DamageLineKind.Defense: return "defense";
                case DamageLineKind.Modifier: return "modifier";
                case DamageLineKind.BoonStat: return "boonStat";
                case DamageLineKind.Nullify: return "nullify";
                default: throw new WireException("Unknown damage line kind " + k + ".");
            }
        }

        private static DamageLineKind ReadDamageLineKind(string s)
        {
            switch (s)
            {
                case "base": return DamageLineKind.Base;
                case "power": return DamageLineKind.Power;
                case "defense": return DamageLineKind.Defense;
                case "modifier": return DamageLineKind.Modifier;
                case "boonStat": return DamageLineKind.BoonStat;
                case "nullify": return DamageLineKind.Nullify;
                default: throw new WireException($"Unknown damage line kind '{s}'.");
            }
        }

        private static string DamageLineOwnerName(DamageLineOwner o)
        {
            switch (o)
            {
                case DamageLineOwner.None: return "none";
                case DamageLineOwner.Attacker: return "attacker";
                case DamageLineOwner.AttackerTile: return "attackerTile";
                case DamageLineOwner.Target: return "target";
                case DamageLineOwner.TargetTile: return "targetTile";
                case DamageLineOwner.Global: return "global";
                default: throw new WireException("Unknown damage line owner " + o + ".");
            }
        }

        private static DamageLineOwner ReadDamageLineOwner(string s)
        {
            switch (s)
            {
                case "none": return DamageLineOwner.None;
                case "attacker": return DamageLineOwner.Attacker;
                case "attackerTile": return DamageLineOwner.AttackerTile;
                case "target": return DamageLineOwner.Target;
                case "targetTile": return DamageLineOwner.TargetTile;
                case "global": return DamageLineOwner.Global;
                default: throw new WireException($"Unknown damage line owner '{s}'.");
            }
        }

        private static string EndTurnReasonName(EndTurnReason r)
        {
            switch (r)
            {
                case EndTurnReason.Player: return "player";
                case EndTurnReason.Timeout: return "timeout";
                default: throw new WireException("Unknown end turn reason " + r + ".");
            }
        }

        private static EndTurnReason ReadEndTurnReason(string s)
        {
            switch (s)
            {
                case "player": return EndTurnReason.Player;
                case "timeout": return EndTurnReason.Timeout;
                default: throw new WireException($"Unknown end turn reason '{s}'.");
            }
        }

        private static string ResignReasonName(ResignReason r)
        {
            switch (r)
            {
                case ResignReason.Player: return "player";
                case ResignReason.Disconnect: return "disconnect";
                default: throw new WireException("Unknown resign reason " + r + ".");
            }
        }

        private static ResignReason ReadResignReason(string s)
        {
            switch (s)
            {
                case "player": return ResignReason.Player;
                case "disconnect": return ResignReason.Disconnect;
                default: throw new WireException($"Unknown resign reason '{s}'.");
            }
        }

        private static string MatchEndReasonName(MatchEndReason r)
        {
            switch (r)
            {
                case MatchEndReason.Elimination: return "elimination";
                case MatchEndReason.Resign: return "resign";
                case MatchEndReason.Forfeit: return "forfeit";
                default: throw new WireException("Unknown match end reason " + r + ".");
            }
        }

        private static MatchEndReason ReadMatchEndReason(string s)
        {
            switch (s)
            {
                case "elimination": return MatchEndReason.Elimination;
                case "resign": return MatchEndReason.Resign;
                case "forfeit": return MatchEndReason.Forfeit;
                default: throw new WireException($"Unknown match end reason '{s}'.");
            }
        }

        // ---- field access ----------------------------------------------------------------------------

        private static JToken Require(JObject o, string field, string where)
        {
            if (o == null) throw new WireException($"{where} must be an object.");
            JToken t = o[field];
            if (t == null || t.Type == JTokenType.Undefined) throw new WireException($"{where} is missing field '{field}'.");
            return t;
        }

        private static JObject Obj(JObject o, string field, string where)
        {
            var v = Require(o, field, where) as JObject;
            if (v == null) throw new WireException($"{where}.{field} must be an object.");
            return v;
        }

        private static string Str(JObject o, string field, string where)
        {
            JToken t = Require(o, field, where);
            if (t.Type != JTokenType.String) throw new WireException($"{where}.{field} must be a string.");
            return t.Value<string>();
        }

        /// <summary>A field that is a string or an explicit null: a hidden ability id, an innate ability's source, a forced move's id.</summary>
        private static string NullableStr(JObject o, string field, string where)
        {
            JToken t = Require(o, field, where);
            if (t.Type == JTokenType.Null) return null;
            if (t.Type != JTokenType.String) throw new WireException($"{where}.{field} must be a string or null.");
            return t.Value<string>();
        }

        private static int Int(JObject o, string field, string where) => Int(Require(o, field, where), where + "." + field);

        private static int Int(JToken t, string where)
        {
            if (t.Type != JTokenType.Integer) throw new WireException($"{where} must be an integer.");
            return t.Value<int>();
        }

        private static bool Bool(JObject o, string field, string where)
        {
            JToken t = Require(o, field, where);
            if (t.Type != JTokenType.Boolean) throw new WireException($"{where}.{field} must be a boolean.");
            return t.Value<bool>();
        }
    }
}
