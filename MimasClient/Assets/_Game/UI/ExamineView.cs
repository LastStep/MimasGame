using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Mimas.Client.Presentation;

namespace Mimas.Client.UI
{
    /// <summary>
    /// Binds <c>Examine.uxml</c> to a <see cref="HudExamine"/> and owns the close-on-any-outside-press rule
    /// (docs/ui/examine.md §2, spec E §7.3, §7.11). The hover panel is the HUD's one <see cref="HoverPanel"/>,
    /// shared with the action bar, the boons column and the attack preview (spec H §3); the plate opens it
    /// to its left. The plate is a layer over the whole HUD: it appears and goes without a slide and moves
    /// nothing else. Not a MonoBehaviour: <see cref="MatchHudView"/> keeps the document and makes one of these from
    /// the template instance in <c>MatchHud.uxml</c>. It formats numbers and picks classes; every word
    /// it shows was composed by the presenter, and every colour is a token (the painting's hues are data).
    /// </summary>
    public sealed class ExamineView
    {
        private const float HoverDelayMs = 120f;
        private const float TallWindow = 900f;

        private readonly VisualElement _hudRoot;
        private readonly VisualElement _root;
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
        private int _openedFrame = -10;
        private readonly EventCallback<PointerDownEvent> _onAnyPress;
        private IVisualElementScheduledItem _pendingHover;

        private readonly Dictionary<string, Texture2D> _squares = new Dictionary<string, Texture2D>(StringComparer.Ordinal);

        public bool IsOpen => _model != null;

        /// <param name="hudRoot">The HUD's root: every press on the HUD passes through it, and the hover panel is clamped inside it.</param>
        /// <param name="examine">The instance of Examine.uxml.</param>
        /// <param name="hover">The HUD's one hover panel.</param>
        /// <param name="close">What ✕ and a press outside the plate call: the presenter's CloseExamine.</param>
        internal ExamineView(VisualElement hudRoot, VisualElement examine, HoverPanel hover, Action close)
        {
            _hudRoot = hudRoot ?? throw new ArgumentNullException(nameof(hudRoot));
            if (examine == null) throw new ArgumentNullException(nameof(examine));
            _close_ = close ?? throw new ArgumentNullException(nameof(close));

            examine.pickingMode = PickingMode.Ignore;
            examine.style.position = Position.Absolute;
            examine.style.left = 0; examine.style.top = 0; examine.style.right = 0; examine.style.bottom = 0;

            _root = Require(examine, "ex.root");
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

            _hover = hover ?? throw new ArgumentNullException(nameof(hover));

            // Any press on the HUD outside the plate closes it, and then carries on to whatever it hit: the
            // action bar and End Turn stay usable with the plate open (Rohan, 23 Sep 2026). Trickle-down, so
            // it is seen on the way to the button and nothing can stop it first. A press on the board never
            // reaches UI Toolkit; the presenter closes the plate for that one.
            _onAnyPress = OnAnyPress;
            _hudRoot.RegisterCallback(_onAnyPress, TrickleDown.TrickleDown);
            _close.RegisterCallback<ClickEvent>(evt => { evt.StopPropagation(); _close_(); });

            _hudRoot.RegisterCallback<GeometryChangedEvent>(evt => ApplyWindowHeight(evt.newRect.height));
            _wash.style.backgroundImage = new StyleBackground(Ramps.LeftToRight());

            RegisterHover(_vitalsHp, () => HoverContents.Stat(_model != null ? _model.FindStat("hp") : null));
            RegisterHover(_vitalsAp, () => HoverContents.Stat(_model != null ? _model.FindStat("ap") : null));
        }

        private void OnAnyPress(PointerDownEvent evt)
        {
            if (_model == null) return;
            // The press that opened the plate (read by the board in its Update) can reach UI Toolkit a moment
            // later, on the Web a frame later. It opened the plate; it must not also close it.
            if (Time.frameCount - _openedFrame <= 2) return;
            var target = evt.target as VisualElement;
            if (target != null && (target == _plate || _plate.Contains(target))) return;
            _close_();
        }

        /// <summary>Draws the model, or hides the plate when it is null.</summary>
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
            _hudRoot.UnregisterCallback(_onAnyPress, TrickleDown.TrickleDown);
            HideHover();
            foreach (Texture2D texture in _squares.Values) UnityEngine.Object.Destroy(texture);
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
            _portrait.style.backgroundImage = new StyleBackground(Ramps.Painting(dark, light, paper));
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
            RegisterHover(cell, () => HoverContents.Stat(captured));
        }

        private VisualElement BoonRow(HudBoon boon, int index)
        {
            var row = new VisualElement { name = "ex.boon[" + index + "]" };
            row.AddToClassList("ex-boon");
            row.AddToClassList("ex-row");
            row.EnableInClassList("ex-boon--unrevealed", !boon.Revealed);

            var glyph = new Glyph(boon.Revealed ? HoverContents.KindShape(boon.Kind) : GlyphPaths.Unknown) { Filled = boon.Starting };
            glyph.AddToClassList("ex-boon-glyph");
            row.Add(glyph);
            row.Add(Text(boon.Revealed ? boon.Name : "Unrevealed", "ex-boon-name"));
            if (!string.IsNullOrEmpty(boon.OnItemName)) row.Add(Text(boon.OnItemName, "ex-boon-on"));

            HudBoon captured = boon;
            RegisterHover(row, () => HoverContents.Boon(captured));
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
            var slotGlyph = new Glyph(HoverContents.SlotShape(item.Slot));
            slotGlyph.AddToClassList("ex-item-slot-glyph");
            square.Add(slotGlyph);
            head.Add(square);
            head.Add(Text(item.Name, "ex-item-name"));
            if (!string.IsNullOrEmpty(item.StatLine)) head.Add(Text(item.StatLine, "ex-item-stat"));
            HudItem captured = item;
            RegisterHover(head, () => HoverContents.Item(captured));
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
            RegisterHover(root, () => HoverContents.Tile(captured));
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
                    _hover.Show(this, c, _model.IsMine, HoverPlacement.Left, element.worldBound, _hudRoot.worldBound,
                        _plate.worldBound.xMin - HoverPanel.LeftGap);
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
            _hover.Hide(this);
        }

        // ---- small helpers ------------------------------------------------------------------------------

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

        // ---- the item squares (the painting and the wash are Ramps') -----------------------------------

        private Texture2D Square(Color dark, Color light)
        {
            string key = ColorUtility.ToHtmlStringRGB(dark) + ColorUtility.ToHtmlStringRGB(light);
            Texture2D texture;
            if (_squares.TryGetValue(key, out texture) && texture != null) return texture;

            const int size = 48;
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

        private static Texture2D NewTexture(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
        }

        private static VisualElement Require(VisualElement root, string name) => Require<VisualElement>(root, name);

        private static T Require<T>(VisualElement root, string name) where T : VisualElement
        {
            T found = root.Q<T>(name);
            if (found == null) throw new InvalidOperationException("Examine.uxml is missing '" + name + "'.");
            return found;
        }
    }
}
