using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Mimas.Client.Presentation;

namespace Mimas.Client.UI
{
    /// <summary>
    /// Where the one hover panel opens (docs/ui/language.md §6, spec H §3): to the left of a thing on the right
    /// edge (the examine plate, the boons column), above a thing on the bottom edge (an action-bar tile), and
    /// over a unit's tag (the attack preview).
    /// </summary>
    internal enum HoverPlacement
    {
        Left,
        Above,
        Over,
    }

    /// <summary>What the hover panel shows, whatever was rested on (language §6).</summary>
    internal sealed class HoverContent
    {
        public bool Unknown;
        public string Shape;
        public bool ShapeFilled;
        public string Letter;
        public string Name;
        public string Type;
        public readonly List<HoverNumber> Numbers = new List<HoverNumber>();
        public readonly List<HoverRow> Rows = new List<HoverRow>();
        public string Text;
        public string DamageLine;
        public readonly List<string> Changes = new List<string>();
        public string Conditions;
        public string Flavour;

        /// <summary>A refused shot's reason, drawn first with the struck mark (docs/ui/hud.md §5 item 4).</summary>
        public string Reason;

        /// <summary>The attack preview's total (display 38 in amount), or null for anything that is not a preview.</summary>
        public string Total;

        /// <summary>"+ ?" after the total: a boon of theirs may still move it.</summary>
        public bool TotalInexact;

        /// <summary>A refused shot: the total goes grey, because nothing is going to happen.</summary>
        public bool TotalMuted;

        /// <summary>One grey line with the closed eye: "They have not seen this yet." (language §5 state.unseen-by-them).</summary>
        public string Note;
    }

    internal readonly struct HoverNumber
    {
        public readonly string Glyph;
        public readonly string Value;
        public readonly string Label;
        public readonly bool Amount;
        public readonly bool Changed;

        public HoverNumber(string glyph, string value, string label, bool amount = false, bool changed = false)
        {
            Glyph = glyph;
            Value = value;
            Label = label;
            Amount = amount;
            Changed = changed;
        }
    }

    internal sealed class HoverRow
    {
        public string Label;
        public string Amount;
        public bool Up, Down, Unknown;
    }

    /// <summary>
    /// The one instance of <c>HoverPanel.uxml</c> (docs/ui/language.md §6): filled from a <see cref="HoverContent"/>,
    /// placed by a <see cref="HoverPlacement"/>, clamped inside the window. The examine plate, the action bar,
    /// the boons column and the attack preview all show it; whoever showed it last owns it, and only its owner
    /// hides it, so the plate redrawing cannot take down a tile's panel or the preview (ADR-040).
    /// </summary>
    internal sealed class HoverPanel
    {
        /// <summary>Between the thing and the panel: to the left of the plate, above a tile, over a tag.</summary>
        public const float LeftGap = 22f;
        public const float LeftLift = 12f;
        public const float AboveGap = 12f;
        public const float AboveInset = 8f;
        public const float OverGap = 18f;
        private const float WindowMargin = 8f;

        private readonly VisualElement _container;
        private readonly VisualElement _root;
        private readonly VisualElement _reason;
        private readonly Label _reasonText;
        private readonly Glyph _tileGlyph;
        private readonly Label _tileLetter;
        private readonly Label _name;
        private readonly Label _type;
        private readonly VisualElement _total;
        private readonly Label _totalValue;
        private readonly Label _totalUnknown;
        private readonly VisualElement _numbers;
        private readonly VisualElement _rows;
        private readonly Label _text;
        private readonly Label _damageLine;
        private readonly VisualElement _changes;
        private readonly Label _conditions;
        private readonly VisualElement _note;
        private readonly Label _noteText;
        private readonly Label _flavour;

        private object _owner;
        private HoverPlacement _placement;
        private Rect _anchor;
        private float _rightEdge;
        private Rect _window;

        public HoverPanel(VisualElement instance)
        {
            _container = instance ?? throw new ArgumentNullException(nameof(instance));
            _container.pickingMode = PickingMode.Ignore;
            _container.style.position = Position.Absolute;
            _container.style.left = 0; _container.style.top = 0; _container.style.right = 0; _container.style.bottom = 0;
            _root = Q(instance, "hp.root");
            _reason = Q(instance, "hp.reason");
            _reasonText = Q<Label>(instance, "hp.reason.text");
            _tileGlyph = Q<Glyph>(instance, "hp.tile.glyph");
            _tileLetter = Q<Label>(instance, "hp.tile.letter");
            _name = Q<Label>(instance, "hp.name");
            _type = Q<Label>(instance, "hp.type");
            _total = Q(instance, "hp.total");
            _totalValue = Q<Label>(instance, "hp.total.value");
            _totalUnknown = Q<Label>(instance, "hp.total.unknown");
            _numbers = Q(instance, "hp.numbers");
            _rows = Q(instance, "hp.rows");
            _text = Q<Label>(instance, "hp.text");
            _damageLine = Q<Label>(instance, "hp.damageline");
            _changes = Q(instance, "hp.changes");
            _conditions = Q<Label>(instance, "hp.conditions");
            _note = Q(instance, "hp.note");
            _noteText = Q<Label>(instance, "hp.note.text");
            _flavour = Q<Label>(instance, "hp.flavour");
            _root.RegisterCallback<GeometryChangedEvent>(_ => Place());
        }

        /// <summary>Who showed the panel last, or null when it is hidden.</summary>
        public object Owner => _owner;

        /// <param name="owner">Whoever shows it; only the same owner's <see cref="Hide"/> takes it down.</param>
        /// <param name="mine">The accent: <c>you</c> for your own things, <c>them</c> for theirs.</param>
        /// <param name="anchor">The thing rested on, in panel space.</param>
        /// <param name="window">The HUD's bounds, for clamping.</param>
        /// <param name="rightEdge">Left placement only: where the panel's right edge goes (the plate's left − 22); NaN puts it <see cref="LeftGap"/> left of the anchor.</param>
        public void Show(object owner, HoverContent c, bool mine, HoverPlacement placement, Rect anchor, Rect window, float rightEdge = float.NaN)
        {
            _root.EnableInClassList("hp--theirs", !mine && !c.Unknown);
            _root.EnableInClassList("hp--unknown", c.Unknown);

            _reasonText.text = c.Reason != null ? c.Reason.ToUpperInvariant() : string.Empty;
            _reason.style.display = string.IsNullOrEmpty(c.Reason) ? DisplayStyle.None : DisplayStyle.Flex;

            bool shape = !string.IsNullOrEmpty(c.Shape) && GlyphPaths.Has(c.Shape);
            _tileGlyph.Shape = shape ? c.Shape : null;
            _tileGlyph.Filled = c.ShapeFilled;
            _tileGlyph.style.display = shape ? DisplayStyle.Flex : DisplayStyle.None;
            _tileLetter.text = shape ? string.Empty : (c.Letter ?? "?");
            _tileLetter.style.display = shape ? DisplayStyle.None : DisplayStyle.Flex;

            _name.text = (c.Name ?? string.Empty).ToUpperInvariant();
            SetText(_type, c.Type != null ? c.Type.ToUpperInvariant() : null);

            bool total = !string.IsNullOrEmpty(c.Total);
            _total.style.display = total ? DisplayStyle.Flex : DisplayStyle.None;
            _totalValue.text = c.Total ?? string.Empty;
            _totalUnknown.style.display = total && c.TotalInexact ? DisplayStyle.Flex : DisplayStyle.None;
            _total.EnableInClassList("hp-total--muted", c.TotalMuted);

            _numbers.Clear();
            for (int i = 0; i < c.Numbers.Count; i++) _numbers.Add(NumberTile(c.Numbers[i]));
            _numbers.style.display = c.Numbers.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            _rows.Clear();
            for (int i = 0; i < c.Rows.Count; i++) _rows.Add(Row(c.Rows[i]));
            _rows.style.display = c.Rows.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            SetText(_text, c.Text);
            SetText(_damageLine, c.DamageLine);

            _changes.Clear();
            for (int i = 0; i < c.Changes.Count; i++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("hp-change");
                var mark = new Glyph("enchant") { Filled = true };
                mark.AddToClassList("hp-change-glyph");
                row.Add(mark);
                var label = new Label(c.Changes[i]) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("hp-change-text");
                row.Add(label);
                _changes.Add(row);
            }
            _changes.style.display = c.Changes.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            SetText(_conditions, c.Conditions);
            _noteText.text = c.Note ?? string.Empty;
            _note.style.display = string.IsNullOrEmpty(c.Note) ? DisplayStyle.None : DisplayStyle.Flex;
            SetText(_flavour, c.Flavour);

            _owner = owner;
            _placement = placement;
            _anchor = anchor;
            _window = window;
            _rightEdge = rightEdge;
            _root.AddToClassList("hp--visible");
            Place();
        }

        /// <summary>Follows a moving anchor (the preview rides on a world-anchored tag) without refilling.</summary>
        public void Move(object owner, Rect anchor, Rect window)
        {
            if (_owner != owner || owner == null) return;
            _anchor = anchor;
            _window = window;
            Place();
        }

        /// <summary>Hides the panel if <paramref name="owner"/> is the one showing it.</summary>
        public void Hide(object owner)
        {
            if (_owner != owner) return;
            _owner = null;
            _root.RemoveFromClassList("hp--visible");
        }

        private void Place()
        {
            if (!_root.ClassListContains("hp--visible")) return;
            float width = _root.resolvedStyle.width;
            float height = _root.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 0f) width = 340f;
            if (float.IsNaN(height)) height = 0f;

            float x, y;
            switch (_placement)
            {
                case HoverPlacement.Above:
                    x = _anchor.xMin - AboveInset;
                    y = _anchor.yMin - AboveGap - height;
                    break;
                case HoverPlacement.Over:
                    x = _anchor.center.x - width * 0.5f;
                    y = _anchor.yMin - OverGap - height;
                    // Above the tag unless that leaves the window; then under it (spec H §13).
                    if (_window.height > 0f && y < _window.yMin + WindowMargin) y = _anchor.yMax + OverGap;
                    break;
                default:
                    x = (float.IsNaN(_rightEdge) ? _anchor.xMin - LeftGap : _rightEdge) - width;
                    y = _anchor.yMin - LeftLift;
                    break;
            }

            if (_window.height > 0f)
            {
                y = Mathf.Min(y, _window.yMax - height - WindowMargin);
                y = Mathf.Max(y, _window.yMin + WindowMargin);
                x = Mathf.Min(x, _window.xMax - width - WindowMargin);
                x = Mathf.Max(x, _window.xMin + WindowMargin);
            }

            Vector2 origin = _container.worldBound.position;
            _root.style.left = x - origin.x;
            _root.style.top = y - origin.y;
        }

        private static VisualElement NumberTile(HoverNumber n)
        {
            var tile = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.AddToClassList("hp-number");
            tile.EnableInClassList("hp-number--amount", n.Amount);
            tile.EnableInClassList("hp-number--changed", n.Changed && !n.Amount);
            if (!string.IsNullOrEmpty(n.Glyph))
            {
                var glyph = new Glyph(n.Glyph);
                glyph.AddToClassList("hp-number-glyph");
                tile.Add(glyph);
            }
            var value = new Label(n.Value) { pickingMode = PickingMode.Ignore };
            value.AddToClassList("hp-number-value");
            tile.Add(value);
            var label = new Label((n.Label ?? string.Empty).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hp-number-label");
            tile.Add(label);
            return tile;
        }

        private static VisualElement Row(HoverRow r)
        {
            var row = new VisualElement { pickingMode = PickingMode.Ignore };
            row.AddToClassList("hp-row");
            row.EnableInClassList("hp-row--up", r.Up);
            row.EnableInClassList("hp-row--down", r.Down);
            row.EnableInClassList("hp-row--unknown", r.Unknown);

            var left = new VisualElement { pickingMode = PickingMode.Ignore };
            left.AddToClassList("hp-row-left");
            // What you have not seen is a dashed "?", on the plate's stat rows and in the preview alike.
            if (r.Unknown)
            {
                var question = new Glyph(GlyphPaths.Unknown);
                question.AddToClassList("hp-row-glyph");
                left.Add(question);
            }
            var label = new Label(r.Label) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hp-row-label");
            left.Add(label);
            row.Add(left);

            var amount = new Label(r.Amount) { pickingMode = PickingMode.Ignore };
            amount.AddToClassList("hp-row-amount");
            row.Add(amount);
            return row;
        }

        private static void SetText(Label label, string text)
        {
            label.text = text ?? string.Empty;
            label.style.display = string.IsNullOrEmpty(text) ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private static VisualElement Q(VisualElement root, string name) => Q<VisualElement>(root, name);

        private static T Q<T>(VisualElement root, string name) where T : VisualElement
        {
            T found = root.Q<T>(name);
            if (found == null) throw new InvalidOperationException("HoverPanel.uxml is missing '" + name + "'.");
            return found;
        }
    }

    /// <summary>
    /// The hover panel's content for each kind of thing (language §6, docs/ui/examine.md §4), built from the
    /// presenter's models. Shared by the plate and the HUD, so a tile says the same thing on the action bar
    /// as on the plate (ADR-040).
    /// </summary>
    internal static class HoverContents
    {
        public const string UnseenByThemNote = "They have not seen this yet.";

        public static HoverContent Tile(HudTile tile)
        {
            if (!tile.Revealed)
            {
                return new HoverContent
                {
                    Unknown = true,
                    Shape = GlyphPaths.Unknown,
                    Name = "Unseen",
                    Type = tile.TypeLine,
                    Text = tile.Description,
                };
            }

            var c = new HoverContent
            {
                Letter = tile.Letter,
                Name = tile.Name,
                Type = tile.TypeLine,
                Text = tile.Description,
                DamageLine = tile.DamageLine,
                Conditions = tile.Conditions.Count > 0 ? string.Join(" · ", tile.Conditions) : null,
            };
            c.Changes.AddRange(tile.Changes);

            HudTileNumbers n = tile.Numbers;
            if (n != null)
            {
                c.Numbers.Add(new HoverNumber("bolt", n.Cost.ToString(), "cost", changed: n.CostChanged));
                if (n.Damage.HasValue) c.Numbers.Add(new HoverNumber("sword", n.Damage.Value.ToString(), "damage", amount: true, changed: n.DamageChanged));
                if (n.RangeMax.HasValue)
                {
                    string reach = n.RangeMin.HasValue && n.RangeMin.Value > 1 && n.RangeMin.Value != n.RangeMax.Value
                        ? n.RangeMin.Value + "–" + n.RangeMax.Value
                        : n.RangeMax.Value.ToString();
                    c.Numbers.Add(new HoverNumber(tile.IsMovement ? "walk" : "straight", reach, tile.IsMovement ? "hexes" : "reach", changed: n.RangeChanged));
                }
                if (n.Apex.HasValue) c.Numbers.Add(new HoverNumber("arc", n.Apex.Value.ToString(), "apex"));
                if (!string.IsNullOrEmpty(n.Element)) c.Numbers.Add(new HoverNumber(n.Element == "fire" ? "fire" : "spark", n.Element, "element"));
            }
            return c;
        }

        public static HoverContent Boon(HudBoon boon)
        {
            if (!boon.Revealed)
            {
                return new HoverContent
                {
                    Unknown = true,
                    Shape = GlyphPaths.Unknown,
                    Name = "Unrevealed",
                    Type = boon.TypeLine,
                    Text = boon.Description,
                };
            }
            return new HoverContent
            {
                Shape = KindShape(boon.Kind),
                ShapeFilled = boon.Starting,
                Name = boon.Name,
                Type = boon.TypeLine,
                Text = boon.Description,
                Conditions = boon.Badge,
                Flavour = boon.Flavour,
            };
        }

        public static HoverContent Item(HudItem item)
        {
            var c = new HoverContent
            {
                Shape = SlotShape(item.Slot),
                Name = item.Name,
                Type = item.Quick,
                Text = string.IsNullOrEmpty(item.SeenLine) ? item.Description
                    : string.IsNullOrEmpty(item.Description) ? item.SeenLine : item.Description + " " + item.SeenLine,
                Conditions = item.Note,
            };
            if (!string.IsNullOrEmpty(item.StatLine))
            {
                string[] parts = item.StatLine.Split(new[] { " · " }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < parts.Length; i++)
                {
                    int space = parts[i].IndexOf(' ');
                    string value = space > 0 ? parts[i].Substring(0, space) : parts[i];
                    string label = space > 0 ? parts[i].Substring(space + 1) : "";
                    c.Numbers.Add(new HoverNumber(null, value, label));
                }
            }
            for (int i = 0; i < item.BoonNames.Count; i++) c.Changes.Add(item.BoonNames[i] + " on this item");
            return c;
        }

        public static HoverContent Stat(HudStat stat)
        {
            if (stat == null) return null;
            var c = new HoverContent
            {
                Shape = stat.Icon,
                Name = stat.Label,
                Type = "stat · base, then every change",
            };
            for (int i = 0; i < stat.Lines.Count; i++)
            {
                HudStatLine line = stat.Lines[i];
                c.Rows.Add(new HoverRow
                {
                    Label = line.Label,
                    Amount = line.Unknown ? "?" : i == 0 ? line.Amount.ToString() : Signed(line.Amount),
                    Unknown = line.Unknown,
                    Up = !line.Unknown && i > 0 && line.Amount > 0,
                    Down = !line.Unknown && line.Amount < 0,
                });
            }
            return c;
        }

        public static string Signed(int value) => value > 0 ? "+" + value : value < 0 ? "−" + (-value) : "0";

        public static string KindShape(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return GlyphPaths.Unknown;
            switch (kind.ToLowerInvariant())
            {
                case "enchant": return "enchant";
                case "sigil": return "sigil";
                default: return "blessing";
            }
        }

        public static string SlotShape(string slot)
        {
            switch (slot)
            {
                case "weapon": return "sword";
                case "crown": return "crown";
                case "boots": return "boot";
                default: return "shield";
            }
        }
    }
}
