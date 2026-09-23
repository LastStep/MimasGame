using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Grid;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Units;
using CoreUnitView = Mimas.Core.Match.UnitView;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Builds the examine plate's model (<see cref="HudExamine"/>, docs/ui/examine.md §3, spec E §7) from what
    /// the local seat is allowed to know: its <see cref="PlayerView"/>, the catalogue, and the public map.
    /// Static and scene-free so EditMode tests can drive it.
    /// <para>
    /// <b>The unit is always rebuilt from the view</b> (<see cref="Unit.FromView"/>, ADR-026) rather than read
    /// from the driver's <c>MatchState</c>: online that state is already a mirror, but in practice it is the
    /// truth, and the truth's <c>Stats</c> include boons the viewer has not been shown. A unit built from the
    /// view holds only revealed boons, so <c>Stats − PublicStats</c> is exactly "the net of what you know" and
    /// cannot leak, whichever driver is running.
    /// </para>
    /// </summary>
    public static class ExamineModelBuilder
    {
        /// <summary>The six stats, in inventory order: key, UXML suffix, label, glyph.</summary>
        private static readonly string[][] StatRows =
        {
            new[] { StatBlock.HpKey, "hp", "Health", "heart" },
            new[] { StatBlock.ApKey, "ap", "Actions", "bolt" },
            new[] { "power.weapon", "strength", "Strength", "sword" },
            new[] { "power.spell", "magic", "Magic", "spark" },
            new[] { "defense.weapon", "armour-weapon", "Armour · weapon", "shield" },
            new[] { "defense.spell", "armour-spell", "Armour · spell", "shield" },
        };

        private const string UnknownAbilitySentence = "One more ability on this item. You will see it the first time it is used.";
        private const string UnknownBoonSentence = "A Blessing shows itself when it changes a result; an Enchant when a number contradicts what you know; a Sigil on first use.";

        /// <summary>
        /// The plate for one hero, or null when the view has no such unit.
        /// </summary>
        /// <param name="rules">The driver's state: read for the map (public) and the ability resolver only.</param>
        /// <param name="revealOrder">Theirs only: boon ids in the order this client saw them revealed; null or empty after a reload.</param>
        public static HudExamine BuildUnit(ContentCatalog catalog, PlayerView view, MatchState rules, int unitId, string seatName, IReadOnlyList<string> revealOrder)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (view == null) return null;
            CoreUnitView seen = view.FindUnit(unitId);
            if (seen == null) return null;

            Unit known = Unit.FromView(seen, catalog);
            LineageDef lineage = null;
            if (seen.LineageId != null) catalog.Lineages.TryGet(seen.LineageId, out lineage);

            var model = new HudExamine
            {
                UnitId = seen.Id,
                Name = seatName,
                IsMine = seen.IsMine,
                LineageName = lineage != null ? lineage.Name : seen.LineageId,
                LineageIcon = lineage != null ? EmblemOf(lineage) : null,
                HueDark = lineage != null ? lineage.HueDark : null,
                HueLight = lineage != null ? lineage.HueLight : null,
                Hp = seen.Hp,
                MaxHp = seen.MaxHp,
                Ap = seen.Ap,
                ApPerTurn = seen.ApPerTurn,
                Height = HeightAt(rules, seen.Position),
            };
            model.LineageLine = seen.LineageId == null
                ? (seen.IsMine ? "your hero" : "Unknown lineage")
                : model.LineageName + " · " + (seen.IsMine ? "your hero" : "the enemy");

            AddStats(model, catalog, seen, known);
            AddBoons(model, catalog, seen, lineage, revealOrder);
            AddItems(model, catalog, seen, known, rules);
            return model;
        }

        /// <summary>
        /// Every revealed ability of one unit as the plate draws it, keyed by ability id, with the name of the item
        /// that grants it (none for the innate walk). The action bar reads its hover panel from these (spec H §3).
        /// </summary>
        public static void ActionTiles(ContentCatalog catalog, PlayerView view, MatchState rules, int unitId, Dictionary<string, HudTile> into)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            into.Clear();
            CoreUnitView seen = view != null ? view.FindUnit(unitId) : null;
            if (seen == null) return;
            Unit known = Unit.FromView(seen, catalog);
            for (int i = 0; i < seen.Abilities.Count; i++)
            {
                KnownEntry entry = seen.Abilities[i];
                if (!entry.Revealed || into.ContainsKey(entry.Id)) continue;
                ItemDef item = null;
                if (entry.SourceItemId != null) catalog.Items.TryGet(entry.SourceItemId, out item);
                string itemName = entry.SourceItemId == null ? null : item != null ? item.Name : entry.SourceItemId;
                into[entry.Id] = Tile(catalog, rules, seen, known, entry, itemName);
            }
        }

        /// <summary>One unit's boons as the plate lists them (oldest to latest for your own): the boons column's circles.</summary>
        public static List<HudBoon> BoonsOf(ContentCatalog catalog, PlayerView view, int unitId, IReadOnlyList<string> revealOrder)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            var model = new HudExamine();
            CoreUnitView seen = view != null ? view.FindUnit(unitId) : null;
            if (seen == null) return model.Boons;
            LineageDef lineage = null;
            if (seen.LineageId != null) catalog.Lineages.TryGet(seen.LineageId, out lineage);
            AddBoons(model, catalog, seen, lineage, revealOrder);
            return model.Boons;
        }

        /// <summary>The reduced plate for a prop: name, height, health (spec E §13). Null when it is gone.</summary>
        public static HudExamine BuildProp(ContentCatalog catalog, PlayerView view, MatchState rules, int propId)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (view == null) return null;
            Mimas.Core.Match.PropView prop = view.FindProp(propId);
            if (prop == null) return null;

            PropDef def;
            bool known = catalog.Props.TryGet(prop.DefId, out def);
            return new HudExamine
            {
                UnitId = prop.Id,
                IsProp = true,
                Name = known ? def.Name : prop.DefId,
                LineageLine = "Terrain",
                Description = prop.IsDamageable
                    ? "Shots stop on it and nothing walks through it, until it comes down."
                    : "Shots stop on it and nothing walks through it. It cannot be destroyed.",
                Hp = prop.Hp,
                MaxHp = prop.MaxHp,
                Height = HeightAt(rules, prop.Position),
            };
        }

        // ---- stats (E4, spec §7.5) --------------------------------------------------------------------

        private static void AddStats(HudExamine model, ContentCatalog catalog, CoreUnitView seen, Unit known)
        {
            var lines = new List<StatLine>();
            bool someHidden = !seen.IsMine && seen.UnrevealedBoonCount > 0;
            for (int r = 0; r < StatRows.Length; r++)
            {
                string key = StatRows[r][0];
                int publicValue = known.PublicStats.Get(key);
                var stat = new HudStat
                {
                    Id = key,
                    Element = StatRows[r][1],
                    Label = StatRows[r][2],
                    Icon = StatRows[r][3],
                    Base = publicValue,
                    Net = known.Stats.Get(key) - publicValue,
                    // Every hp and AP Blessing is public from round start (design #stats), so those two never hide.
                    Hidden = someHidden && key != StatBlock.HpKey && key != StatBlock.ApKey,
                };

                known.StatLines(key, lines);
                for (int i = 0; i < lines.Count; i++)
                    stat.Lines.Add(new HudStatLine { Label = LineLabel(catalog, lines[i]), Amount = lines[i].Amount });
                if (stat.Hidden) stat.Lines.Add(new HudStatLine { Label = "unrevealed boons", Unknown = true });
                model.Stats.Add(stat);
            }
        }

        private static string LineLabel(ContentCatalog catalog, StatLine line)
        {
            if (line.SourceKind == StatLine.ItemKind)
            {
                ItemDef item;
                return catalog.Items.TryGet(line.SourceId, out item) ? item.Name : line.SourceId;
            }
            if (line.SourceKind == StatLine.BoonKind)
            {
                BoonDef boon;
                return catalog.Boons.TryGet(line.SourceId, out boon) ? boon.Name : line.SourceId;
            }
            return "base";
        }

        // ---- boons (E7, E8, spec §7.6) ------------------------------------------------------------------

        private static void AddBoons(HudExamine model, ContentCatalog catalog, CoreUnitView seen, LineageDef lineage, IReadOnlyList<string> revealOrder)
        {
            // Grant order is oldest to latest for your own. For theirs, revealed boons go in the order this
            // client learned them, any it did not see (a reload) after those in grant order, unknown last.
            var order = new List<int>(seen.Boons.Count);
            for (int i = 0; i < seen.Boons.Count; i++) order.Add(i);
            if (!seen.IsMine)
            {
                order.Sort((a, b) =>
                {
                    int ka = RevealRank(seen.Boons[a], a, revealOrder, seen.Boons.Count);
                    int kb = RevealRank(seen.Boons[b], b, revealOrder, seen.Boons.Count);
                    return ka != kb ? ka.CompareTo(kb) : a.CompareTo(b);
                });
            }

            for (int n = 0; n < order.Count; n++)
            {
                int grantIndex = order[n];
                KnownEntry entry = seen.Boons[grantIndex];
                BoonDef def = null;
                if (!entry.Revealed || !catalog.Boons.TryGet(entry.Id, out def))
                {
                    model.Boons.Add(new HudBoon
                    {
                        Id = entry.Id,
                        Name = entry.Revealed ? entry.Id : "Unrevealed",
                        Revealed = entry.Revealed,
                        TypeLine = "drafted · not yet revealed",
                        Description = UnknownBoonSentence,
                    });
                    continue;
                }

                bool starting = grantIndex == 0 && lineage != null && def.Id == lineage.StartingBlessingId;
                if (seen.IsMine && grantIndex == 0) starting = true;
                string god = GodOf(def.Name);
                string lineageName = LineageName(catalog, def.LineageId);
                string onItem = ItemNameInSlot(catalog, seen, def.Requires != null ? def.Requires.Slot : null);
                model.Boons.Add(new HudBoon
                {
                    Id = def.Id,
                    Name = def.Name,
                    Kind = KindName(def.Kind),
                    God = god,
                    LineageName = lineageName,
                    OnItemName = onItem,
                    Description = def.Description,
                    Badge = starting ? "starting Blessing" : "drafted",
                    TypeLine = KindName(def.Kind) + " · " + lineageName + " · on " + (onItem ?? (seen.IsMine ? "you" : "them")),
                    Flavour = god + " answers those who pray to the " + lineageName + " gods.",
                    Revealed = true,
                    Starting = starting,
                });
            }
        }

        private static int RevealRank(KnownEntry entry, int grantIndex, IReadOnlyList<string> revealOrder, int count)
        {
            if (!entry.Revealed) return 2 * count + grantIndex;
            if (revealOrder != null)
            {
                for (int i = 0; i < revealOrder.Count; i++)
                    if (revealOrder[i] == entry.Id) return i - count;           // seen: before everything else
            }
            return grantIndex;
        }

        // ---- equipment and tiles (E6, E7, spec §7.7) ------------------------------------------------------

        private static void AddItems(HudExamine model, ContentCatalog catalog, CoreUnitView seen, Unit known, MatchState rules)
        {
            for (int s = 0; s < ItemSlots.All.Length && s < seen.ItemIds.Count; s++)
            {
                string slot = ItemSlots.All[s];
                string itemId = seen.ItemIds[s];
                ItemDef def;
                bool knownItem = catalog.Items.TryGet(itemId, out def);
                string itemName = knownItem ? def.Name : itemId;

                var item = new HudItem
                {
                    Id = itemId,
                    Name = itemName,
                    Slot = slot,
                    Kind = knownItem ? def.Kind : null,
                    Quick = slot + (knownItem && !string.IsNullOrEmpty(def.Kind) ? " · " + def.Kind : ""),
                    Description = knownItem ? def.Description : null,
                    StatLine = knownItem ? ItemStatLine(def.Stats) : null,
                    Note = slot == ItemSlots.Armour && knownItem ? ArmourNote(def.Stats) : null,
                };

                // Under the boots the innate walk comes first; then the item's own, then what a Sigil added.
                var own = new List<HudTile>();
                var added = new List<HudTile>();
                int innateAt = 0;
                for (int i = 0; i < seen.Abilities.Count; i++)
                {
                    KnownEntry entry = seen.Abilities[i];
                    // No granting item means innate (hidden or not: an unseen walk is still the boots' first tile).
                    bool isInnate = entry.SourceItemId == null;
                    if (slot == ItemSlots.Boots && isInnate)
                    {
                        own.Insert(innateAt++, Tile(catalog, rules, seen, known, entry, null));
                        continue;
                    }
                    if (entry.SourceItemId != itemId) continue;
                    HudTile tile = Tile(catalog, rules, seen, known, entry, itemName);
                    (tile.Added ? added : own).Add(tile);
                }
                item.Tiles.AddRange(own);
                item.Tiles.AddRange(added);
                int innateCount = 0;
                if (slot == ItemSlots.Boots)
                    for (int i = 0; i < seen.Abilities.Count; i++) if (seen.Abilities[i].SourceItemId == null) innateCount++;

                int ownTotal = 0, ownSeen = 0;
                for (int i = 0; i < item.Tiles.Count; i++)
                {
                    if (slot == ItemSlots.Boots && i < innateCount) continue;
                    ownTotal++;
                    if (item.Tiles[i].Revealed) ownSeen++;
                }
                if (!seen.IsMine && ownTotal > 0)
                    item.SeenLine = "You have seen " + ownSeen + " of its " + ownTotal + (ownTotal == 1 ? " ability." : " abilities.");

                AddBoonNamesOn(item, catalog, known, itemId, slot);
                model.Items.Add(item);
            }
        }

        /// <summary>
        /// One action tile: the plate's and, through <see cref="ActionTiles"/>, the action bar's (ADR-040). Your own
        /// revealed tiles also say whether the opponent has seen them yet (ADR-039).
        /// </summary>
        internal static HudTile Tile(ContentCatalog catalog, MatchState rules, CoreUnitView seen, Unit known, KnownEntry entry, string itemName)
        {
            if (!entry.Revealed)
            {
                return new HudTile
                {
                    Revealed = false,
                    Name = "unseen",
                    Letter = "?",
                    TypeLine = "ability · " + (itemName ?? "innate"),
                    Description = UnknownAbilitySentence,
                };
            }

            AbilityDef def = null;
            if (rules != null) rules.ResolveAbility(known, entry.Id, out def);
            AbilityDef raw;
            catalog.Abilities.TryGet(entry.Id, out raw);
            if (def == null) def = raw;

            string name = def != null ? def.Name : entry.Id;
            var tile = new HudTile
            {
                Id = entry.Id,
                Name = name,
                Icon = def != null ? def.Icon : null,
                Letter = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant(),
                Description = def != null ? def.Description : null,
                Revealed = true,
                Changed = MatchSession.IsChangedByABoon(known, entry.Id),
                Added = known.BoonOfAbility(entry.Id) != null,
                UnseenByThem = seen.IsMine && !entry.SeenByOpponent,
                IsMovement = def is MovementDef,
            };
            if (def == null) return tile;

            var overrides = new List<AbilityOverride>();
            known.Overlay.OverridesFor(entry.Id, overrides);
            int damageBonus = 0;
            var numbers = new HudTileNumbers { Cost = def.Cost };
            for (int i = 0; i < overrides.Count; i++)
            {
                if (!AbilityFields.AppliesTo(overrides[i].Field, def)) continue;
                if (overrides[i].Field == AbilityFields.Damage) { damageBonus += overrides[i].Amount; numbers.DamageChanged = true; }
                if (overrides[i].Field == AbilityFields.Range || overrides[i].Field == AbilityFields.MinRange) numbers.RangeChanged = true;
                if (overrides[i].Field == AbilityFields.Cost) numbers.CostChanged = true;
            }

            var attack = def as AttackDef;
            var movement = def as MovementDef;
            if (attack != null)
            {
                numbers.Damage = attack.Damage + damageBonus;
                numbers.RangeMin = attack.MinRange;
                numbers.RangeMax = attack.Range;
                if (attack.Trajectory == Trajectories.Arc) numbers.Apex = attack.Apex;
                numbers.Element = attack.Elements.Count > 0 ? attack.Elements[0] : null;
                tile.TypeLine = (attack.Category == AbilityCategories.Spell ? "spell" : "weapon attack") + " · " + (itemName ?? "innate") + " · action";
                tile.Conditions.Add(attack.Trajectory == Trajectories.Arc ? "lobbed, clears cover"
                    : attack.Trajectory == Trajectories.Direct ? "straight line" : "from above");
                tile.Conditions.Add(attack.LineOfSight ? "needs line of sight" : "no sight needed");
                tile.Conditions.Add("one target");
                tile.DamageLine = DamageLine(attack, numbers.Damage.Value, seen, known);
            }
            else if (movement != null)
            {
                numbers.RangeMax = movement.Range;
                tile.TypeLine = "movement · " + (itemName ?? "innate");
                tile.Conditions.Add(movement.Range + (movement.Range == 1 ? " hex" : " hexes"));
                if (movement.Mode == MovementModes.Jump)
                {
                    tile.Conditions.Add("line");
                    tile.Conditions.Add("clears " + movement.JumpHeight);
                }
                else if (movement.Mode == MovementModes.Teleport)
                {
                    tile.Conditions.Add(movement.RequiresLineOfSight ? "needs line of sight" : "no sight needed");
                }
                else
                {
                    tile.Conditions.Add("climbs " + movement.MaxClimb);
                }
            }
            tile.Numbers = numbers;

            AddChanges(tile, catalog, known, entry.Id, raw, overrides);
            return tile;
        }

        /// <summary>One line per override or grant, the boon named: "reach 5 → 6 · Apollo's Bowstring".</summary>
        private static void AddChanges(HudTile tile, ContentCatalog catalog, Unit known, string abilityId, AbilityDef raw, List<AbilityOverride> overrides)
        {
            var running = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < overrides.Count; i++)
            {
                AbilityOverride o = overrides[i];
                if (o.IsValue || raw == null || !AbilityFields.AppliesTo(o.Field, raw)) continue;
                int before;
                if (!running.TryGetValue(o.Field, out before)) before = FieldValue(raw, o.Field);
                int after = before + o.Amount;
                running[o.Field] = after;
                tile.Changes.Add(FieldLabel(o.Field) + " " + before + " → " + after + " · " + BoonName(catalog, o.BoonId));
            }

            var additions = new List<AbilityAddition>();
            known.Overlay.AddedElementsFor(abilityId, additions);
            for (int i = 0; i < additions.Count; i++)
                tile.Changes.Add(additions[i].Value + " · " + BoonName(catalog, additions[i].BoonId));

            string grant = known.BoonOfAbility(abilityId);
            if (grant != null) tile.Changes.Add("granted by " + BoonName(catalog, grant));
        }

        /// <summary>The rules' damage formula written out (#damage), not a preview: there is no target.</summary>
        private static string DamageLine(AttackDef attack, int damage, CoreUnitView seen, Unit known)
        {
            string lane = attack.DamageType == "spell" ? "Magic" : "Strength";
            int power = known.Stats.Get(StatBlock.PowerKey(attack.DamageType));
            bool hidden = !seen.IsMine && seen.UnrevealedBoonCount > 0;
            return damage + " base + " + power + " " + lane + (hidden ? " + ?" : "")
                + " − " + (seen.IsMine ? "their" : "your") + " armour";
        }

        private static void AddBoonNamesOn(HudItem item, ContentCatalog catalog, Unit known, string itemId, string slot)
        {
            for (int i = 0; i < known.BoonIds.Count; i++)
            {
                BoonDef boon;
                if (!catalog.Boons.TryGet(known.BoonIds[i], out boon)) continue;
                if (boon.Requires == null || boon.Requires.Slot != slot) continue;
                item.BoonNames.Add(boon.Name);
            }
        }

        // ---- words --------------------------------------------------------------------------------------

        private static string ItemStatLine(StatBlock stats)
        {
            int hp = stats.Get(StatBlock.HpKey), ap = stats.Get(StatBlock.ApKey);
            int str = stats.Get("power.weapon"), mag = stats.Get("power.spell");
            int dw = stats.Get("defense.weapon"), ds = stats.Get("defense.spell");
            var parts = new List<string>();
            if (hp != 0) parts.Add(Signed(hp) + " Health");
            if (ap != 0) parts.Add(Signed(ap) + " Actions");
            if (str != 0) parts.Add(Signed(str) + " Strength");
            if (mag != 0) parts.Add(Signed(mag) + " Magic");
            if (dw != 0 && dw == ds) parts.Add(Signed(dw) + " Armour");
            else
            {
                if (dw != 0) parts.Add(Signed(dw) + " Armour · weapon");
                if (ds != 0) parts.Add(Signed(ds) + " Armour · spell");
            }
            return parts.Count == 0 ? null : string.Join(" · ", parts);
        }

        private static string ArmourNote(StatBlock stats)
        {
            int dw = stats.Get("defense.weapon"), ds = stats.Get("defense.spell");
            if (dw == 0 && ds == 0) return null;
            if (dw == ds) return "turns " + dw + " of every blow";
            if (ds == 0) return "turns " + dw + " of every weapon blow";
            if (dw == 0) return "turns " + ds + " of every spell";
            return "turns " + dw + " of every weapon blow, " + ds + " of every spell";
        }

        private static string Signed(int value) => (value > 0 ? "+" : "") + value;

        private static int FieldValue(AbilityDef def, string field)
        {
            var attack = def as AttackDef;
            var movement = def as MovementDef;
            switch (field)
            {
                case AbilityFields.Cost: return def.Cost;
                case AbilityFields.Range: return attack != null ? attack.Range : movement != null ? movement.Range : 0;
                case AbilityFields.MinRange: return attack != null ? attack.MinRange : 0;
                case AbilityFields.Damage: return attack != null ? attack.Damage : 0;
                case AbilityFields.Apex: return attack != null ? attack.Apex : 0;
                case AbilityFields.Climb: return movement != null ? movement.MaxClimb : 0;
                case AbilityFields.JumpHeight: return movement != null ? movement.JumpHeight : 0;
                default: return 0;
            }
        }

        private static string FieldLabel(string field)
        {
            switch (field)
            {
                case AbilityFields.Range: return "reach";
                case AbilityFields.MinRange: return "min reach";
                case AbilityFields.JumpHeight: return "clears";
                default: return field;
            }
        }

        private static int HeightAt(MatchState rules, Mimas.Core.Geometry.Hex position)
        {
            Tile tile;
            return rules != null && rules.Map.TryGet(position, out tile) ? tile.Height : 0;
        }

        private static string ItemNameInSlot(ContentCatalog catalog, CoreUnitView seen, string slot)
        {
            if (slot == null) return null;
            int index = ItemSlots.IndexOf(slot);
            if (index < 0 || index >= seen.ItemIds.Count) return null;
            ItemDef item;
            return catalog.Items.TryGet(seen.ItemIds[index], out item) ? item.Name : seen.ItemIds[index];
        }

        private static string BoonName(ContentCatalog catalog, string boonId)
        {
            BoonDef boon;
            return boonId != null && catalog.Boons.TryGet(boonId, out boon) ? boon.Name : boonId;
        }

        private static string LineageName(ContentCatalog catalog, string lineageId)
        {
            LineageDef lineage;
            if (lineageId == null) return null;
            return catalog.Lineages.TryGet(lineageId, out lineage) ? lineage.Name : lineageId;
        }

        /// <summary>The lineage's emblem glyph. The data's <c>icon</c> key names the lineage; the glyph table names the shape.</summary>
        internal static string EmblemOf(LineageDef lineage)
        {
            switch (lineage.Icon ?? lineage.Id)
            {
                case "greek": return "laurel";
                case "norse": return "hammer";
                case "hindu": return "lotus";
                default: return lineage.Icon;
            }
        }

        /// <summary>The god is the name up to its first apostrophe; a name without one is all god (the rule T-0010 used).</summary>
        internal static string GodOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int apostrophe = name.IndexOf('\'');
            return apostrophe > 0 ? name.Substring(0, apostrophe) : name;
        }

        internal static string KindName(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            return char.ToUpperInvariant(kind[0]) + kind.Substring(1);
        }
    }
}
