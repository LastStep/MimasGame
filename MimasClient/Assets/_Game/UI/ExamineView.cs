using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Mimas.Client.Presentation;

namespace Mimas.Client.UI
{
    /// <summary>
    /// Binds <c>Examine.uxml</c> to a <see cref="HudExamine"/> and owns the one hover panel
    /// (<c>HoverPanel.uxml</c>), the scrim and the open/close slide (docs/ui/examine.md, spec E §7.3, §7.10,
    /// §7.11). Not a MonoBehaviour: <see cref="MatchHudView"/> keeps the document and makes one of these from
    /// the two template instances in <c>MatchHud.uxml</c>. It formats numbers and picks classes; every word
    /// it shows was composed by the presenter, and every colour is a token (the painting's hues are data).
    /// </summary>
    public sealed class ExamineView
    {
        private const float HoverDelayMs = 120f;
        private const float HoverGap = 22f;
        private const float HoverLift = 12f;
        private const float TallWindow = 900f;

        private readonly VisualElement _hudRoot;
        private readonly VisualElement _root;
        private readonly VisualElement _scrim;
        private readonly VisualElement _wash;
        private readonly VisualElement _plate;
        private readonly VisualElement _portrait;
        private readonly Glyph _emblem;
        private readonly Label _name;
        private readonly Label _lineage;
        private readonly VisualElement _close;
        private readonly ScrollView _body;
        private readonly VisualElement _vitalsHp;
        private readonly Label _hpValue;
        private readonly Label _hpMax;
        private readonly VisualElement _hpFill;
        private readonly VisualElement _vitalsAp;
        private readonly Label _apValue;
        private readonly Label _apMax;
        private readonly VisualElement _eggs;
        private readonly Label _heightValue;
        private readonly Label _description;
        private readonly VisualElement _boons;
        private readonly VisualElement _equipment;
        private readonly Dictionary<string, VisualElement> _statCells = new Dictionary<string, VisualElement>(StringComparer.Ordinal);

        private readonly Action _close_;
        private readonly HoverPanel _hover;
        private readonly StringBuilder _signature = new StringBuilder();

        private HudExamine _model;
        private string _renderedSignature;
        private int _closedByScrimFrame = -10;
        private int _openedFrame = -10;
        private IVisualElementScheduledItem _pendingHover;

        private readonly Dictionary<string, Texture2D> _paintings = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> _squares = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private Texture2D _washTexture;

        public bool IsOpen => _model != null;

        /// <summary>The plate's width as laid out (<c>--mimas-plate-w</c>), for the HUD to make room.</summary>
        public float PlateWidth
        {
            get
            {
                float width = _plate.resolvedStyle.width;
                return float.IsNaN(width) || width <= 0f ? 380f : width;
            }
        }

        /// <param name="hudRoot">The HUD's root: the window the hover panel is clamped inside and the scrim covers.</param>
        /// <param name="examine">The instance of Examine.uxml.</param>
        /// <param name="hoverPanel">The instance of HoverPanel.uxml.</param>
        /// <param name="close">What ✕, the scrim and Escape call: the presenter's CloseExamine.</param>
        public ExamineView(VisualElement hudRoot, VisualElement examine, VisualElement hoverPanel, Action close)
        {
            _hudRoot = hudRoot ?? throw new ArgumentNullException(nameof(hudRoot));
            if (examine == null) throw new ArgumentNullException(nameof(examine));
            _close_ = close ?? throw new ArgumentNullException(nameof(close));

            examine.pickingMode = PickingMode.Ignore;
            examine.style.position = Position.Absolute;
            examine.style.left = 0; examine.style.top = 0; examine.style.right = 0; examine.style.bottom = 0;

            _root = Require(examine, "ex.root");
            _scrim = Require(examine, "ex.scrim");
            _wash = Require(examine, "ex.wash");
            _plate = Require(examine, "ex.plate");
            _portrait = Require(examine, "ex.portrait");
            _emblem = Require<Glyph>(examine, "ex.emblem");
            _name = Require<Label>(examine, "ex.name");
            _lineage = Require<Label>(examine, "ex.lineage");
            _close = Require(examine, "ex.close");
            _body = Require<ScrollView>(examine, "ex.body");
            _vitalsHp = Require(examine, "ex.vitals.hp");
            _hpValue = Require<Label>(examine, "ex.vitals.hp.value");
            _hpMax = Require<Label>(examine, "ex.vitals.hp.max");
            _hpFill = Require(examine, "ex.vitals.hp.fill");
            _vitalsAp = Require(examine, "ex.vitals.ap");
            _apValue = Require<Label>(examine, "ex.vitals.ap.value");
            _apMax = Require<Label>(examine, "ex.vitals.ap.max");
            _eggs = Require(examine, "ex.vitals.ap.eggs");
            _heightValue = Require<Label>(examine, "ex.vitals.height.value");
            _description = Require<Label>(examine, "ex.description");
            _boons = Require(examine, "ex.boons");
            _equipment = Require(examine, "ex.equipment");
            foreach (string element in new[] { "hp", "ap", "strength", "magic", "armour-weapon", "armour-spell" })
                _statCells[element] = Require(examine, "ex.stat." + element);

            _hover = new HoverPanel(hoverPanel ?? throw new ArgumentNullException(nameof(hoverPanel)));

            // The scrim swallows the off-plate click: it closes and does nothing else (E10). Stopping it here
            // keeps it from UI Toolkit; SwallowsBoardPointer keeps it from the board's own raycast.
            _scrim.RegisterCallback<PointerDownEvent>(evt =>
            {
                // The press that opened the plate is read by the board in its Update and then, later in the
                // same frame (or the next, on the Web), dispatched by UI Toolkit onto the scrim it just
                // revealed. That press opened the plate; it must not also close it.
                if (Time.frameCount - _openedFrame <= 2)
                {
                    evt.StopPropagation();
                    return;
                }
                _closedByScrimFrame = Time.frameCount;
                evt.StopPropagation();
                _close_();
            });
            _close.RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); _close_(); });

            _hudRoot.RegisterCallback<GeometryChangedEvent>(evt => ApplyWindowHeight(evt.newRect.height));
            _wash.style.backgroundImage = new StyleBackground(WashTexture());

            RegisterHover(_vitalsHp, () => StatContent(_model != null ? _model.FindStat("hp") : null));
            RegisterHover(_vitalsAp, () => StatContent(_model != null ? _model.FindStat("ap") : null));
        }

        /// <summary>
        /// True for the frame the scrim closed the plate on and the next: the board reads the mouse in its own
        /// Update, which may run after UI Toolkit has already removed the scrim from under the pointer.
        /// </summary>
        public bool SwallowsBoardPointer => Time.frameCount - _closedByScrimFrame <= 1;

        /// <summary>Draws the model, or slides the plate out when it is null.</summary>
        public void Render(HudExamine model)
        {
            _model = model;
            if (model == null)
            {
                _root.RemoveFromClassList("ex--open");
                _renderedSignature = null;
                HideHover();
                return;
            }

            if (!_root.ClassListContains("ex--open")) _openedFrame = Time.frameCount;
            string signature = Signature(model);
            if (signature != _renderedSignature)
            {
                HideHover();
                Fill(model);
                _renderedSignature = signature;
            }
            _root.AddToClassList("ex--open");
        }

        public void Dispose()
        {
            HideHover();
            foreach (Texture2D texture in _paintings.Values) UnityEngine.Object.Destroy(texture);
            foreach (Texture2D texture in _squares.Values) UnityEngine.Object.Destroy(texture);
            if (_washTexture != null) UnityEngine.Object.Destroy(_washTexture);
            _paintings.Clear();
            _squares.Clear();
        }

        private void ApplyWindowHeight(float height)
        {
            _root.EnableInClassList("ex--short", height > 0f && height < TallWindow);
        }

        // ---- filling ------------------------------------------------------------------------------------

        private void Fill(HudExamine model)
        {
            _root.EnableInClassList("ex--mine", model.IsMine);
            _root.EnableInClassList("ex--theirs", !model.IsMine && !model.IsProp);
            _root.EnableInClassList("ex--prop", model.IsProp);

            // The painting: the lineage's two hues fading into paper; grey when the lineage is unknown.
            Color paper = _plate.resolvedStyle.backgroundColor;
            if (paper.a <= 0f) paper = new Color(0.914f, 0.886f, 0.824f);
            Color dark, light;
            bool known = TryHue(model.HueDark, out dark) & TryHue(model.HueLight, out light);
            if (!known)
            {
                // Neutral: the paper's own ink at two strengths (docs/ui/examine.md §3.1 "neutral grey wash").
                dark = Color.Lerp(paper, new Color(0.086f, 0.082f, 0.102f), 0.55f);
                light = Color.Lerp(paper, new Color(0.086f, 0.082f, 0.102f), 0.25f);
            }
            _portrait.style.backgroundImage = new StyleBackground(Painting(dark, light, paper));
            _emblem.Shape = model.LineageIcon;
            _emblem.style.display = string.IsNullOrEmpty(model.LineageIcon) ? DisplayStyle.None : DisplayStyle.Flex;
            _emblem.Tint = light;

            _name.text = model.Name ?? string.Empty;
            _lineage.text = model.LineageLine ?? string.Empty;

            _hpValue.text = model.Hp.ToString();
            _hpMax.text = "/ " + model.MaxHp;
            _hpFill.style.width = Length.Percent(model.MaxHp > 0 ? 100f * Mathf.Clamp01((float)model.Hp / model.MaxHp) : 0f);
            _apValue.text = model.Ap.ToString();
            _apMax.text = "/ " + model.ApPerTurn;
            _eggs.Clear();
            for (int i = 0; i < model.ApPerTurn; i++)
            {
                var egg = new Glyph("egg") { Filled = i < model.Ap };
                egg.AddToClassList("ex-egg");
                _eggs.Add(egg);
            }
            _heightValue.text = model.Height.ToString();
            _description.text = model.Description ?? string.Empty;

            if (model.IsProp) return;

            for (int i = 0; i < model.Stats.Count; i++)
            {
                HudStat stat = model.Stats[i];
                VisualElement cell;
                if (!_statCells.TryGetValue(stat.Element, out cell)) continue;
                FillStat(cell, stat);
            }

            _boons.Clear();
            for (int i = 0; i < model.Boons.Count; i++) _boons.Add(BoonRow(model.Boons[i], i));

            _equipment.Clear();
            for (int i = 0; i < model.Items.Count; i++) _equipment.Add(ItemGroup(model.Items[i], dark, light));
        }

        private void FillStat(VisualElement cell, HudStat stat)
        {
            cell.Clear();
            var numbers = new VisualElement { pickingMode = PickingMode.Ignore };
            numbers.AddToClassList("ex-stat-numbers");
            numbers.Add(Text(stat.Base.ToString(), "ex-stat-base"));
            // What you know, then a grey "?" while a boon of theirs may still be moving it (E4).
            if (stat.Net > 0) numbers.Add(Text("+" + stat.Net, "ex-stat-net", "ex-stat-net--up"));
            else if (stat.Net < 0) numbers.Add(Text("−" + (-stat.Net), "ex-stat-net", "ex-stat-net--down"));
            if (stat.Hidden) numbers.Add(Text("?", "ex-stat-net", "ex-stat-net--hidden"));
            cell.Add(numbers);

            var label = new VisualElement { pickingMode = PickingMode.Ignore };
            label.AddToClassList("ex-stat-label");
            var glyph = new Glyph(stat.Icon);
            glyph.AddToClassList("ex-stat-glyph");
            label.Add(glyph);
            label.Add(Text((stat.Label ?? "").ToUpperInvariant(), "ex-stat-caption"));
            cell.Add(label);

            HudStat captured = stat;
            RegisterHover(cell, () => StatContent(captured));
        }

        private VisualElement BoonRow(HudBoon boon, int index)
        {
            var row = new VisualElement { name = "ex.boon[" + index + "]" };
            row.AddToClassList("ex-boon");
            row.AddToClassList("ex-row");
            row.EnableInClassList("ex-boon--unrevealed", !boon.Revealed);

            var glyph = new Glyph(boon.Revealed ? KindShape(boon.Kind) : GlyphPaths.Unknown) { Filled = boon.Starting };
            glyph.AddToClassList("ex-boon-glyph");
            row.Add(glyph);
            row.Add(Text(boon.Revealed ? boon.Name : "Unrevealed", "ex-boon-name"));
            if (!string.IsNullOrEmpty(boon.OnItemName)) row.Add(Text(boon.OnItemName, "ex-boon-on"));

            HudBoon captured = boon;
            RegisterHover(row, () => BoonContent(captured));
            return row;
        }

        private VisualElement ItemGroup(HudItem item, Color dark, Color light)
        {
            var group = new VisualElement { name = "ex.item[" + item.Slot + "]" };
            group.AddToClassList("ex-item");

            var head = new VisualElement();
            head.AddToClassList("ex-item-head");
            head.AddToClassList("ex-row");
            var square = new VisualElement { pickingMode = PickingMode.Ignore };
            square.AddToClassList("ex-item-square");
            square.style.backgroundImage = new StyleBackground(Square(dark, light));
            var slotGlyph = new Glyph(SlotShape(item.Slot));
            slotGlyph.AddToClassList("ex-item-slot-glyph");
            square.Add(slotGlyph);
            head.Add(square);
            head.Add(Text(item.Name, "ex-item-name"));
            if (!string.IsNullOrEmpty(item.StatLine)) head.Add(Text(item.StatLine, "ex-item-stat"));
            HudItem captured = item;
            RegisterHover(head, () => ItemContent(captured));
            group.Add(head);

            if (item.Tiles.Count == 0)
            {
                if (!string.IsNullOrEmpty(item.Note)) group.Add(Text(item.Note, "ex-item-note"));
                return group;
            }

            var tiles = new VisualElement { pickingMode = PickingMode.Ignore };
            tiles.AddToClassList("ex-tiles");
            for (int j = 0; j < item.Tiles.Count; j++) tiles.Add(TileElement(item, item.Tiles[j], j));
            group.Add(tiles);
            return group;
        }

        private VisualElement TileElement(HudItem item, HudTile tile, int index)
        {
            var root = new VisualElement { name = "ex.item[" + item.Slot + "].tile[" + index + "]" };
            root.AddToClassList("ex-tile");
            root.EnableInClassList("ex-tile--changed", tile.Revealed && tile.Changed && !tile.Added);
            root.EnableInClassList("ex-tile--added", tile.Revealed && tile.Added);
            root.EnableInClassList("ex-tile--unseen", !tile.Revealed);
            // The icon key rides on a class, so an art pass can bind a sprite to it (§7.8).
            if (!string.IsNullOrEmpty(tile.Icon)) root.AddToClassList("icon--" + tile.Icon);

            var box = new VisualElement { pickingMode = PickingMode.Ignore };
            box.AddToClassList("ex-tile-box");
            if (!tile.Revealed)
            {
                var dash = new DashedFrame();
                dash.AddToClassList("ex-tile-dash");
                box.Add(dash);
            }
            box.Add(Text(tile.Revealed ? tile.Letter : "?", "ex-tile-letter"));
            if (tile.Revealed && (tile.Added || tile.Changed))
            {
                var mark = new Glyph(tile.Added ? "sigil" : "enchant") { Filled = true };
                mark.AddToClassList("ex-tile-mark");
                box.Add(mark);
            }
            root.Add(box);
            root.Add(Text((tile.Revealed ? tile.Name : "unseen").ToUpperInvariant(), "ex-tile-name"));

            HudTile captured = tile;
            RegisterHover(root, () => TileContent(captured));
            return root;
        }

        // ---- the hover panel ------------------------------------------------------------------------------

        private void RegisterHover(VisualElement element, Func<HoverContent> content)
        {
            element.pickingMode = PickingMode.Position;
            element.RegisterCallback<PointerEnterEvent>(evt =>
            {
                CancelPendingHover();
                _pendingHover = element.schedule.Execute(() =>
                {
                    _pendingHover = null;
                    HoverContent c = content();
                    if (c == null || _model == null) return;
                    _hover.Show(c, _model.IsMine, element.worldBound, _plate.worldBound.xMin - HoverGap, _hudRoot.worldBound, HoverLift);
                }).StartingIn((long)HoverDelayMs);
            });
            element.RegisterCallback<PointerLeaveEvent>(evt => HideHover());
        }

        private void CancelPendingHover()
        {
            if (_pendingHover == null) return;
            _pendingHover.Pause();
            _pendingHover = null;
        }

        private void HideHover()
        {
            CancelPendingHover();
            _hover.Hide();
        }

        private static HoverContent TileContent(HudTile tile)
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

        private static HoverContent BoonContent(HudBoon boon)
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

        private HoverContent ItemContent(HudItem item)
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

        private static HoverContent StatContent(HudStat stat)
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

        // ---- small helpers ------------------------------------------------------------------------------

        private static string Signed(int value) => value > 0 ? "+" + value : value < 0 ? "−" + (-value) : "0";

        private static string KindShape(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return GlyphPaths.Unknown;
            switch (kind.ToLowerInvariant())
            {
                case "enchant": return "enchant";
                case "sigil": return "sigil";
                default: return "blessing";
            }
        }

        private static string SlotShape(string slot)
        {
            switch (slot)
            {
                case "weapon": return "sword";
                case "crown": return "crown";
                case "boots": return "boot";
                default: return "shield";
            }
        }

        private static Label Text(string text, string cls, string cls2 = null)
        {
            var label = new Label(text ?? string.Empty) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            if (cls2 != null) label.AddToClassList(cls2);
            return label;
        }

        private static bool TryHue(string hex, out Color color)
        {
            color = default;
            return !string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out color);
        }

        private string Signature(HudExamine m)
        {
            _signature.Length = 0;
            _signature.Append(m.UnitId).Append('|').Append(m.IsProp).Append('|').Append(m.Name).Append('|').Append(m.LineageLine)
                .Append('|').Append(m.Hp).Append('/').Append(m.MaxHp).Append('|').Append(m.Ap).Append('/').Append(m.ApPerTurn)
                .Append('|').Append(m.Height);
            for (int i = 0; i < m.Stats.Count; i++) _signature.Append('|').Append(m.Stats[i].Base).Append(',').Append(m.Stats[i].Net).Append(m.Stats[i].Hidden ? "?" : "");
            for (int i = 0; i < m.Boons.Count; i++) _signature.Append('|').Append(m.Boons[i].Id ?? "?");
            for (int i = 0; i < m.Items.Count; i++)
            {
                _signature.Append('|').Append(m.Items[i].Id);
                for (int j = 0; j < m.Items[i].Tiles.Count; j++)
                {
                    HudTile t = m.Items[i].Tiles[j];
                    _signature.Append(',').Append(t.Id ?? "?").Append(t.Changed ? "c" : "").Append(t.Added ? "a" : "");
                    if (t.Numbers != null) _signature.Append(t.Numbers.Cost).Append(t.Numbers.RangeMax).Append(t.Numbers.Damage);
                }
            }
            return _signature.ToString();
        }

        // ---- textures: the painting, the item squares and the wash (USS has no gradients) --------------

        private Texture2D Painting(Color dark, Color light, Color paper)
        {
            string key = ColorUtility.ToHtmlStringRGB(dark) + ColorUtility.ToHtmlStringRGB(light) + ColorUtility.ToHtmlStringRGB(paper);
            Texture2D texture;
            if (_paintings.TryGetValue(key, out texture) && texture != null) return texture;

            const int w = 96, h = 64;
            texture = NewTexture(w, h);
            var pixels = new Color[w * h];
            for (int y = 0; y < h; y++)
            {
                // Texture rows run bottom-up; v is 0 at the top of the painting.
                float v = 1f - (y + 0.5f) / h;
                for (int x = 0; x < w; x++)
                {
                    float u = (x + 0.5f) / w;
                    // Three layered radial washes: a light glow left of centre, a dark pool top right, a second
                    // softer glow low left; then the lower 60% fades into paper.
                    float glow = Radial(u, v, 0.36f, 0.30f, 0.62f);
                    float pool = Radial(u, v, 0.92f, 0.05f, 0.55f);
                    float low = Radial(u, v, 0.10f, 0.72f, 0.45f) * 0.5f;
                    Color c = Color.Lerp(dark, light, Mathf.Clamp01(glow * 0.85f + low));
                    c = Color.Lerp(c, dark * 0.85f, pool * 0.6f);
                    float fade = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.40f, 1.0f, v));
                    c = Color.Lerp(c, paper, fade);
                    c.a = 1f;
                    pixels[y * w + x] = c;
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _paintings[key] = texture;
            return texture;
        }

        private Texture2D Square(Color dark, Color light)
        {
            string key = ColorUtility.ToHtmlStringRGB(dark) + ColorUtility.ToHtmlStringRGB(light);
            Texture2D texture;
            if (_squares.TryGetValue(key, out texture) && texture != null) return texture;

            const int size = 16;
            texture = NewTexture(size, size);
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float t = Mathf.Clamp01(((float)x / (size - 1) * 0.5f) + ((float)y / (size - 1) * 0.5f));
                    Color c = Color.Lerp(dark, light, t * 0.8f);
                    c.a = 1f;
                    pixels[y * size + x] = c;
                }
            texture.SetPixels(pixels);
            texture.Apply(false, true);
            _squares[key] = texture;
            return texture;
        }

        /// <summary>White with alpha rising to 1 at the plate's edge; USS tints it to --mimas-wash.</summary>
        private Texture2D WashTexture()
        {
            if (_washTexture != null) return _washTexture;
            const int w = 64;
            _washTexture = NewTexture(w, 1);
            var pixels = new Color[w];
            for (int x = 0; x < w; x++)
            {
                float t = (x + 0.5f) / w;
                pixels[x] = new Color(1f, 1f, 1f, t);
            }
            _washTexture.SetPixels(pixels);
            _washTexture.Apply(false, true);
            return _washTexture;
        }

        private static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        private static float Radial(float u, float v, float cu, float cv, float radius)
        {
            float du = u - cu, dv = (v - cv) * 0.66f;
            float d = Mathf.Sqrt(du * du + dv * dv) / radius;
            return Mathf.Clamp01(1f - d * d);
        }

        private static VisualElement Require(VisualElement root, string name) => Require<VisualElement>(root, name);

        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T found = root.Q<T>(name);
            if (found == null) throw new InvalidOperationException("Examine.uxml is missing '" + name + "'.");
            return found;
        }
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

    /// <summary>The one instance of <c>HoverPanel.uxml</c>: filled, placed to the left of the thing, clamped to the window.</summary>
    internal sealed class HoverPanel
    {
        private readonly VisualElement _container;
        private readonly VisualElement _root;
        private readonly Glyph _tileGlyph;
        private readonly Label _tileLetter;
        private readonly Label _name;
        private readonly Label _type;
        private readonly VisualElement _numbers;
        private readonly VisualElement _rows;
        private readonly Label _text;
        private readonly Label _damageLine;
        private readonly VisualElement _changes;
        private readonly Label _conditions;
        private readonly Label _flavour;

        private Rect _anchor;
        private float _right;
        private Rect _window;
        private float _lift;

        public HoverPanel(VisualElement instance)
        {
            _container = instance;
            _container.pickingMode = PickingMode.Ignore;
            _container.style.position = Position.Absolute;
            _container.style.left = 0; _container.style.top = 0; _container.style.right = 0; _container.style.bottom = 0;
            _root = Q(instance, "hp.root");
            _tileGlyph = instance.Q<Glyph>("hp.tile.glyph");
            _tileLetter = instance.Q<Label>("hp.tile.letter");
            _name = instance.Q<Label>("hp.name");
            _type = instance.Q<Label>("hp.type");
            _numbers = Q(instance, "hp.numbers");
            _rows = Q(instance, "hp.rows");
            _text = instance.Q<Label>("hp.text");
            _damageLine = instance.Q<Label>("hp.damageline");
            _changes = Q(instance, "hp.changes");
            _conditions = instance.Q<Label>("hp.conditions");
            _flavour = instance.Q<Label>("hp.flavour");
            _root.RegisterCallback<GeometryChangedEvent>(_ => Place());
        }

        public void Show(HoverContent c, bool mine, Rect anchor, float rightEdge, Rect window, float lift)
        {
            _root.EnableInClassList("hp--theirs", !mine && !c.Unknown);
            _root.EnableInClassList("hp--unknown", c.Unknown);

            bool shape = !string.IsNullOrEmpty(c.Shape) && GlyphPaths.Has(c.Shape);
            _tileGlyph.Shape = shape ? c.Shape : null;
            _tileGlyph.Filled = c.ShapeFilled;
            _tileGlyph.style.display = shape ? DisplayStyle.Flex : DisplayStyle.None;
            _tileLetter.text = shape ? string.Empty : (c.Letter ?? "?");
            _tileLetter.style.display = shape ? DisplayStyle.None : DisplayStyle.Flex;

            _name.text = (c.Name ?? string.Empty).ToUpperInvariant();
            SetText(_type, c.Type != null ? c.Type.ToUpperInvariant() : null);

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
            SetText(_flavour, c.Flavour);

            _anchor = anchor;
            _right = rightEdge;
            _window = window;
            _lift = lift;
            _root.AddToClassList("hp--visible");
            Place();
        }

        public void Hide()
        {
            _root.RemoveFromClassList("hp--visible");
        }

        /// <summary>x = the plate's left − 22 − width; y = the row's top − 12; clamped inside the window.</summary>
        private void Place()
        {
            if (!_root.ClassListContains("hp--visible")) return;
            float width = _root.resolvedStyle.width;
            float height = _root.resolvedStyle.height;
            if (float.IsNaN(width) || width <= 0f) width = 300f;
            if (float.IsNaN(height)) height = 0f;

            Vector2 origin = _container.worldBound.position;
            float x = _right - width;
            float y = _anchor.yMin - _lift;
            if (_window.height > 0f)
            {
                y = Mathf.Min(y, _window.yMax - height - 8f);
                y = Mathf.Max(y, _window.yMin + 8f);
                x = Mathf.Max(x, _window.xMin + 8f);
            }
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
            var label = new Label(r.Label) { pickingMode = PickingMode.Ignore };
            label.AddToClassList("hp-row-label");
            row.Add(label);
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

        private static VisualElement Q(VisualElement root, string name)
        {
            VisualElement found = root.Q<VisualElement>(name);
            if (found == null) throw new InvalidOperationException("HoverPanel.uxml is missing '" + name + "'.");
            return found;
        }
    }
}
