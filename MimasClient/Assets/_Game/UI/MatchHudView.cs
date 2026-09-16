using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using Mimas.Client.Presentation;
using Mimas.Core.Data;

namespace Mimas.Client.UI
{
    /// <summary>
    /// The in-match HUD (ADR-015, ADR-019): sectioned action bar with action-point dots (bottom centre),
    /// turn owner with a burning rope (top centre), examine panel (left), End Turn (bottom right), plus the
    /// world-anchored layer: one tag per unit (segmented hp bar, number, revealed passives) that fades until
    /// relevant, an attack-preview tooltip over the hovered target, damage flyovers, and the result banner.
    /// Reads everything from an <see cref="IMatchHudSource"/> and asks it for exactly three things: arm or
    /// disarm an action, end the turn, close examine. Overlays are screen-space elements repositioned every
    /// LateUpdate via <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/> (world-space UI Toolkit is
    /// not trusted on WebGL2 yet). Layout lives in MatchHud.uxml / MatchHud.uss; this class fills slots and
    /// toggles classes.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        /// <summary>Bar order and captions per category. Unknown categories are appended with their key upper-cased.</summary>
        private static readonly string[][] Groups =
        {
            new[] { AbilityCategories.Movement, "MOVE" },
            new[] { AbilityCategories.Weapon, "WEAPONS" },
            new[] { AbilityCategories.Spell, "SPELLS" },
        };

        private const int HpPerSegment = 4;
        private const float FlyoverSeconds = 2f;
        private const float FlyoverRise = 36f;

        [Tooltip("Any component implementing IMatchHudSource (today: LocalMatchSession).")]
        [SerializeField] private MonoBehaviour _sourceBehaviour;

        [Tooltip("Board input to shield from clicks that land on the HUD.")]
        [SerializeField] private BoardInputController _boardInput;

        [Tooltip("Sprites matched to ability and modifier 'icon' keys by sprite name. Missing keys fall back to a letter glyph.")]
        [SerializeField] private Sprite[] _icons;

        private readonly Dictionary<string, Sprite> _iconsByKey = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly List<VisualElement> _slots = new List<VisualElement>();
        private readonly List<VisualElement> _apDots = new List<VisualElement>();
        private readonly List<string> _groupOrder = new List<string>();
        private readonly StringBuilder _signature = new StringBuilder();
        private readonly Dictionary<int, UnitTag> _unitTags = new Dictionary<int, UnitTag>();
        private readonly List<FlyoverInstance> _flyovers = new List<FlyoverInstance>();

        private UIDocument _document;
        private IMatchHudSource _source;
        private Func<Vector2, bool> _pointerBlocker;

        private VisualElement _root;
        private VisualElement _turnPanel;
        private VisualElement _rope;
        private VisualElement _ropeFill;
        private VisualElement _ropeEmber;
        private VisualElement _actionBar;
        private VisualElement _tooltip;
        private VisualElement _examine;
        private VisualElement _examineItems;
        private VisualElement _examineAbilities;
        private VisualElement _examineModifiers;
        private VisualElement _unitLayer;
        private VisualElement _preview;
        private VisualElement _previewLines;
        private VisualElement _flyLayer;
        private Label _turnOwner;
        private Label _tooltipTitle;
        private Label _tooltipBody;
        private Label _examineTitle;
        private Label _examineSubtitle;
        private Label _examineStats;
        private Label _examineDescription;
        private Label _examineItemsCaption;
        private Label _examineModifiersCaption;
        private Label _previewTitle;
        private Label _previewTotal;
        private Label _banner;
        private Button _examineClose;
        private Button _endTurn;

        private bool _bound;
        private bool _ropeVisible;
        private string _barSignature;
        private int _hoveredSlot = -1;

        private sealed class UnitTag
        {
            public VisualElement Root;
            public VisualElement Bar;
            public Label Number;
            public VisualElement Markers;
            public readonly List<VisualElement> Segments = new List<VisualElement>();
            public int MaxHp;
            public string MarkerSignature;
        }

        private sealed class FlyoverInstance
        {
            public VisualElement Root;
            public Vector3 WorldPosition;
            public float StartTime;
        }

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            _source = _sourceBehaviour as IMatchHudSource;
            if (_source == null)
            {
                Debug.LogError("[MatchHudView] _sourceBehaviour must implement IMatchHudSource.", this);
                enabled = false;
                return;
            }

            _iconsByKey.Clear();
            if (_icons != null)
            {
                for (int i = 0; i < _icons.Length; i++)
                {
                    Sprite sprite = _icons[i];
                    if (sprite == null) continue;
                    _iconsByKey[sprite.name] = sprite;
                }
            }

            _pointerBlocker = IsPointerOverHud;
        }

        private void OnEnable()
        {
            if (_source == null) return;
            _source.StateChanged += Refresh;
            _source.Flyover += SpawnFlyover;
            if (_boardInput != null) _boardInput.SetPointerBlocker(_pointerBlocker);
            TryBind();
        }

        private void OnDisable()
        {
            if (_source != null)
            {
                _source.StateChanged -= Refresh;
                _source.Flyover -= SpawnFlyover;
            }
            if (_boardInput != null) _boardInput.SetPointerBlocker(null);
            Unbind();
        }

        private void Update()
        {
            if (!_bound && !TryBind()) return;
            UpdateRope();
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            UpdateUnitTags();
            UpdatePreviewPosition();
            UpdateFlyovers();
        }

        // ---- binding --------------------------------------------------------------------------------

        private bool TryBind()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null) return false;

            _root = root.Q<VisualElement>("hud-root");
            _turnPanel = root.Q<VisualElement>("turn-panel");
            _turnOwner = root.Q<Label>("turn-owner");
            _rope = root.Q<VisualElement>("rope");
            _ropeFill = root.Q<VisualElement>("rope-fill");
            _ropeEmber = root.Q<VisualElement>("rope-ember");
            _actionBar = root.Q<VisualElement>("action-bar");
            _tooltip = root.Q<VisualElement>("tooltip");
            _tooltipTitle = root.Q<Label>("tooltip-title");
            _tooltipBody = root.Q<Label>("tooltip-body");
            _examine = root.Q<VisualElement>("examine");
            _examineTitle = root.Q<Label>("examine-title");
            _examineSubtitle = root.Q<Label>("examine-subtitle");
            _examineStats = root.Q<Label>("examine-stats");
            _examineDescription = root.Q<Label>("examine-description");
            _examineItemsCaption = root.Q<Label>("examine-items-caption");
            _examineItems = root.Q<VisualElement>("examine-items");
            _examineAbilities = root.Q<VisualElement>("examine-abilities");
            _examineModifiersCaption = root.Q<Label>("examine-modifiers-caption");
            _examineModifiers = root.Q<VisualElement>("examine-modifiers");
            _examineClose = root.Q<Button>("examine-close");
            _endTurn = root.Q<Button>("end-turn");
            _unitLayer = root.Q<VisualElement>("unit-layer");
            _preview = root.Q<VisualElement>("preview");
            _previewTitle = root.Q<Label>("preview-title");
            _previewTotal = root.Q<Label>("preview-total");
            _previewLines = root.Q<VisualElement>("preview-lines");
            _flyLayer = root.Q<VisualElement>("fly-layer");
            _banner = root.Q<Label>("banner");

            if (_root == null || _turnPanel == null || _turnOwner == null || _rope == null || _ropeFill == null || _ropeEmber == null
                || _actionBar == null || _tooltip == null || _tooltipTitle == null || _tooltipBody == null
                || _examine == null || _examineTitle == null || _examineSubtitle == null || _examineStats == null || _examineDescription == null
                || _examineItemsCaption == null || _examineItems == null
                || _examineAbilities == null || _examineModifiersCaption == null || _examineModifiers == null || _examineClose == null || _endTurn == null
                || _unitLayer == null || _preview == null || _previewTitle == null || _previewTotal == null || _previewLines == null
                || _flyLayer == null || _banner == null)
            {
                Debug.LogError("[MatchHudView] MatchHud.uxml is missing one of the named elements.", this);
                enabled = false;
                return false;
            }

            _endTurn.clicked += HandleEndTurnClicked;
            _examineClose.clicked += HandleExamineCloseClicked;
            _bound = true;
            _barSignature = null;
            _ropeVisible = true;      // force the first UpdateRope to apply the real state
            Refresh();
            UpdateRope();
            return true;
        }

        private void Unbind()
        {
            if (!_bound) return;
            _endTurn.clicked -= HandleEndTurnClicked;
            _examineClose.clicked -= HandleExamineCloseClicked;
            ClearSlots();
            foreach (UnitTag tag in _unitTags.Values) tag.Root.RemoveFromHierarchy();
            _unitTags.Clear();
            for (int i = 0; i < _flyovers.Count; i++) _flyovers[i].Root.RemoveFromHierarchy();
            _flyovers.Clear();
            _bound = false;
        }

        // ---- state → visuals ------------------------------------------------------------------------

        /// <summary>Full repaint of everything except the rope and the world-anchored layer, which tick every frame.</summary>
        private void Refresh()
        {
            if (!_bound) return;

            bool mine = _source.IsMyTurn;
            bool over = !string.IsNullOrEmpty(_source.Banner);
            _turnOwner.text = over ? "MATCH OVER" : (mine ? "YOUR TURN  ·  " + _source.TurnNumber : "OPPONENT'S TURN");
            _turnPanel.EnableInClassList("turn-panel--theirs", !mine && !over);
            _endTurn.SetEnabled(_source.CanEndTurn);

            RefreshActionBar();
            RefreshExamine();
            RefreshPreview();
            RefreshBanner();
            SyncUnitTags();
        }

        private void RefreshActionBar()
        {
            IReadOnlyList<HudAction> actions = _source.Actions;

            // Rebuild the sections only when the set of actions changes; otherwise update slots in place
            // so hover state and the tooltip survive a repaint.
            _signature.Length = 0;
            _signature.Append("ap").Append(_source.ApPerTurn).Append('|');
            for (int i = 0; i < actions.Count; i++) _signature.Append(actions[i].Category).Append(':').Append(actions[i].Id).Append('|');
            string signature = _signature.ToString();
            if (signature != _barSignature)
            {
                BuildSections(actions);
                _barSignature = signature;
            }

            int active = _source.ActiveActionIndex;
            for (int i = 0; i < actions.Count && i < _slots.Count; i++)
            {
                HudAction action = actions[i];
                VisualElement slot = _slots[i];
                slot.EnableInClassList("action-slot--active", i == active);
                slot.EnableInClassList("action-slot--disabled", !action.Enabled && action.Affordable);
                slot.EnableInClassList("action-slot--unaffordable", !action.Affordable);
                ApplyIcon(slot.Q<VisualElement>("icon"), slot.Q<Label>("glyph"), action.Icon, action.Name);
                slot.Q<Label>("cost").text = action.Cost.ToString();
            }

            RefreshApDots();
        }

        /// <summary>Filled dots for remaining ap; the dots a hovered ability would spend turn red (Divinity-style reservation).</summary>
        private void RefreshApDots()
        {
            int current = _source.ApCurrent;
            int reserve = 0;
            IReadOnlyList<HudAction> actions = _source.Actions;
            if (_hoveredSlot >= 0 && _hoveredSlot < actions.Count && actions[_hoveredSlot].Affordable) reserve = actions[_hoveredSlot].Cost;

            for (int i = 0; i < _apDots.Count; i++)
            {
                bool filled = i < current;
                bool reserved = filled && i >= current - reserve;
                _apDots[i].EnableInClassList("ap-dot--empty", !filled);
                _apDots[i].EnableInClassList("ap-dot--reserved", reserved);
            }
        }

        private void RefreshExamine()
        {
            HudExamine examine = _source.Examine;
            if (examine == null)
            {
                _examine.RemoveFromClassList("examine--visible");
                return;
            }

            _examineTitle.text = examine.Title ?? string.Empty;
            _examineSubtitle.text = (examine.Subtitle ?? string.Empty).ToUpperInvariant();
            _examineStats.text = "HP " + examine.Hp + " / " + examine.MaxHp + "     AP " + examine.Ap + " / " + examine.ApPerTurn;
            _examineDescription.text = examine.Description ?? string.Empty;
            _examineDescription.style.display = string.IsNullOrEmpty(examine.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            _examine.EnableInClassList("examine--theirs", examine.Subtitle != null && examine.Subtitle.StartsWith("Opp", StringComparison.Ordinal));

            FillEntries(_examineItems, examine.Items);
            bool anyItems = examine.Items.Count > 0;
            _examineItemsCaption.style.display = anyItems ? DisplayStyle.Flex : DisplayStyle.None;
            _examineItems.style.display = anyItems ? DisplayStyle.Flex : DisplayStyle.None;

            FillEntries(_examineAbilities, examine.Abilities);
            FillEntries(_examineModifiers, examine.Modifiers);
            bool anyModifiers = examine.Modifiers.Count > 0;
            _examineModifiersCaption.style.display = anyModifiers ? DisplayStyle.Flex : DisplayStyle.None;
            _examineModifiers.style.display = anyModifiers ? DisplayStyle.Flex : DisplayStyle.None;

            _examine.AddToClassList("examine--visible");
        }

        private void FillEntries(VisualElement container, List<HudExamineEntry> entries)
        {
            container.Clear();
            string group = null;
            for (int i = 0; i < entries.Count; i++)
            {
                HudExamineEntry entry = entries[i];

                // A new source (the item that grants the next abilities) gets its own small caption row.
                if (!string.IsNullOrEmpty(entry.Group) && entry.Group != group)
                {
                    group = entry.Group;
                    var caption = new Label { text = group, pickingMode = PickingMode.Ignore };
                    caption.AddToClassList("examine-group");
                    container.Add(caption);
                }

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("examine-entry");
                row.EnableInClassList("examine-entry--hidden", entry.Hidden);

                var icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
                icon.AddToClassList("examine-entry-icon");
                var glyph = new Label { name = "glyph", pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("examine-entry-glyph");
                icon.Add(glyph);
                row.Add(icon);
                if (entry.Hidden) { icon.style.backgroundImage = StyleKeyword.None; glyph.text = "?"; }
                else ApplyIcon(icon, glyph, entry.Icon, entry.Name);

                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("examine-entry-text");
                var name = new Label { text = entry.Name ?? string.Empty, pickingMode = PickingMode.Ignore };
                name.AddToClassList("examine-entry-name");
                text.Add(name);
                if (!string.IsNullOrEmpty(entry.Description))
                {
                    var desc = new Label { text = entry.Description, pickingMode = PickingMode.Ignore };
                    desc.AddToClassList("examine-entry-desc");
                    text.Add(desc);
                }
                row.Add(text);

                container.Add(row);
            }
        }

        private void RefreshPreview()
        {
            HudPreview preview = _source.Preview;
            if (preview == null)
            {
                _preview.RemoveFromClassList("preview--visible");
                return;
            }

            _previewTitle.text = preview.AbilityName ?? string.Empty;
            _previewTotal.text = preview.IsExact ? preview.Total.ToString() : preview.Total + "?";
            _previewLines.Clear();
            for (int i = 0; i < preview.Lines.Count; i++)
            {
                HudPreviewLine line = preview.Lines[i];
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("preview-line");
                row.EnableInClassList("preview-line--unknown", line.Unknown);
                row.EnableInClassList("preview-line--plus", !line.Unknown && i > 0 && line.Amount > 0);
                row.EnableInClassList("preview-line--minus", !line.Unknown && line.Amount < 0);

                var label = new Label { text = line.Label ?? string.Empty, pickingMode = PickingMode.Ignore };
                label.AddToClassList("preview-line-label");
                row.Add(label);

                string amount = line.Unknown ? "?" : (i == 0 ? line.Amount.ToString() : (line.Amount > 0 ? "+" + line.Amount : line.Amount.ToString()));
                var value = new Label { text = amount, pickingMode = PickingMode.Ignore };
                value.AddToClassList("preview-line-amount");
                row.Add(value);

                _previewLines.Add(row);
            }
            _preview.AddToClassList("preview--visible");
            UpdatePreviewPosition();
        }

        private void RefreshBanner()
        {
            string banner = _source.Banner;
            bool visible = !string.IsNullOrEmpty(banner);
            _banner.text = banner ?? string.Empty;
            _banner.EnableInClassList("banner--visible", visible);
            _banner.EnableInClassList("banner--lost", visible && banner != "VICTORY");
        }

        private void UpdateRope()
        {
            float remaining = _source.TurnSecondsRemaining;
            float ropeSeconds = _source.RopeSeconds;
            if (ropeSeconds < 0.001f) ropeSeconds = 0.001f;

            bool visible = _source.TurnSecondsTotal > 0f && remaining > 0f && remaining <= ropeSeconds;
            if (visible != _ropeVisible)
            {
                _ropeVisible = visible;
                _rope.EnableInClassList("rope--visible", visible);
            }
            if (!visible) return;

            float fraction = Mathf.Clamp01(remaining / ropeSeconds);
            _ropeFill.style.width = Length.Percent(fraction * 100f);
            _ropeEmber.style.left = Length.Percent(fraction * 100f);

            // The ember flickers faster as the rope gets short.
            float pulse = 0.85f + 0.3f * Mathf.PingPong(Time.unscaledTime * (2f + (1f - fraction) * 4f), 1f);
            _ropeEmber.style.scale = new Scale(new Vector2(pulse, pulse));
        }

        // ---- world-anchored layer -------------------------------------------------------------------

        /// <summary>Creates or updates one tag per unit; numbers and markers change rarely, positions every frame.</summary>
        private void SyncUnitTags()
        {
            IReadOnlyList<HudUnit> units = _source.Units;
            for (int i = 0; i < units.Count; i++)
            {
                HudUnit unit = units[i];
                UnitTag tag;
                if (!_unitTags.TryGetValue(unit.Id, out tag))
                {
                    tag = CreateUnitTag(unit);
                    _unitTags[unit.Id] = tag;
                }

                if (tag.MaxHp != unit.MaxHp) BuildSegments(tag, unit.MaxHp);

                int shown = unit.Hp;
                int ghost = Mathf.Clamp(unit.GhostDamage, 0, unit.Hp);
                for (int s = 0; s < tag.Segments.Count; s++)
                {
                    int lo = s * HpPerSegment;
                    bool filled = shown > lo;
                    bool ghosted = filled && shown - ghost <= lo;
                    tag.Segments[s].EnableInClassList("unit-seg--empty", !filled);
                    tag.Segments[s].EnableInClassList("unit-seg--ghost", ghosted);
                }

                tag.Number.text = ghost > 0 ? (unit.Hp - ghost) + " ← " + unit.Hp : unit.Hp + " / " + unit.MaxHp;
                tag.Number.EnableInClassList("unit-number--ghost", ghost > 0);
                tag.Root.EnableInClassList("unit-tag--emphasised", unit.Emphasised || ghost > 0);
                tag.Root.EnableInClassList("unit-tag--dead", !unit.IsAlive);

                SyncMarkers(tag, unit);
            }
        }

        private UnitTag CreateUnitTag(HudUnit unit)
        {
            var tag = new UnitTag();
            tag.Root = new VisualElement { name = "unit-" + unit.Id, pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            tag.Root.AddToClassList("unit-tag");
            tag.Root.EnableInClassList("unit-tag--theirs", !unit.IsMine);

            tag.Bar = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.Bar.AddToClassList("unit-bar");
            tag.Root.Add(tag.Bar);

            tag.Number = new Label { pickingMode = PickingMode.Ignore };
            tag.Number.AddToClassList("unit-number");
            tag.Root.Add(tag.Number);

            tag.Markers = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.Markers.AddToClassList("unit-markers");
            tag.Root.Add(tag.Markers);

            _unitLayer.Add(tag.Root);
            return tag;
        }

        private static void BuildSegments(UnitTag tag, int maxHp)
        {
            tag.Bar.Clear();
            tag.Segments.Clear();
            int count = Mathf.Max(1, (maxHp + HpPerSegment - 1) / HpPerSegment);
            for (int i = 0; i < count; i++)
            {
                var seg = new VisualElement { pickingMode = PickingMode.Ignore };
                seg.AddToClassList("unit-seg");
                tag.Bar.Add(seg);
                tag.Segments.Add(seg);
            }
            tag.MaxHp = maxHp;
        }

        private void SyncMarkers(UnitTag tag, HudUnit unit)
        {
            _signature.Length = 0;
            for (int i = 0; i < unit.Markers.Count; i++) _signature.Append(unit.Markers[i].Id).Append('|');
            string signature = _signature.ToString();
            if (signature == tag.MarkerSignature) return;
            tag.MarkerSignature = signature;

            tag.Markers.Clear();
            for (int i = 0; i < unit.Markers.Count; i++)
            {
                HudMarker marker = unit.Markers[i];
                var dot = new VisualElement { pickingMode = PickingMode.Ignore, tooltip = marker.Name };
                dot.AddToClassList("unit-marker");
                var glyph = new Label { pickingMode = PickingMode.Ignore };
                glyph.AddToClassList("unit-marker-glyph");
                dot.Add(glyph);
                ApplyIcon(dot, glyph, marker.Icon, marker.Name);
                tag.Markers.Add(dot);
            }
        }

        private void UpdateUnitTags()
        {
            Camera camera = _source.WorldCamera;
            if (camera == null || _root.panel == null) return;
            IReadOnlyList<HudUnit> units = _source.Units;
            for (int i = 0; i < units.Count; i++)
            {
                HudUnit unit = units[i];
                UnitTag tag;
                if (!_unitTags.TryGetValue(unit.Id, out tag) || unit.Anchor == null) continue;
                Vector3 world = unit.Anchor.position + unit.AnchorOffset;
                Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, world, camera);
                tag.Root.style.left = panel.x;
                tag.Root.style.top = panel.y;
            }
        }

        private void UpdatePreviewPosition()
        {
            HudPreview preview = _source.Preview;
            if (preview == null) return;
            UnitTag tag;
            if (!_unitTags.TryGetValue(preview.TargetUnitId, out tag)) return;
            // Sit above the target's tag: same anchor, lifted by the tag's own height plus a gap.
            _preview.style.left = tag.Root.style.left;
            float top = tag.Root.style.top.value.value - tag.Root.resolvedStyle.height - 10f;
            _preview.style.top = top;
        }

        private void SpawnFlyover(HudFlyover flyover)
        {
            if (!_bound || flyover == null) return;
            var root = new VisualElement { pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            root.AddToClassList("fly");
            var headline = new Label { text = flyover.Headline ?? string.Empty, pickingMode = PickingMode.Ignore };
            headline.AddToClassList("fly-headline");
            root.Add(headline);
            if (!string.IsNullOrEmpty(flyover.Detail))
            {
                var detail = new Label { text = flyover.Detail, pickingMode = PickingMode.Ignore };
                detail.AddToClassList("fly-detail");
                root.Add(detail);
            }
            _flyLayer.Add(root);
            _flyovers.Add(new FlyoverInstance { Root = root, WorldPosition = flyover.WorldPosition, StartTime = Time.time });
        }

        /// <summary>Scale-pop on spawn, drift upwards, fade out; pooled nowhere because a match has a handful of hits.</summary>
        private void UpdateFlyovers()
        {
            if (_flyovers.Count == 0) return;
            Camera camera = _source.WorldCamera;
            if (camera == null || _root.panel == null) return;

            for (int i = _flyovers.Count - 1; i >= 0; i--)
            {
                FlyoverInstance fly = _flyovers[i];
                float t = (Time.time - fly.StartTime) / FlyoverSeconds;
                if (t >= 1f)
                {
                    fly.Root.RemoveFromHierarchy();
                    _flyovers.RemoveAt(i);
                    continue;
                }

                Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, fly.WorldPosition, camera);
                float rise = FlyoverRise * Mathf.SmoothStep(0f, 1f, t);
                float pop = t < 0.15f ? 1f + 0.6f * (1f - t / 0.15f) : 1f;
                float alpha = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                fly.Root.style.left = panel.x;
                fly.Root.style.top = panel.y - 28f - rise;
                fly.Root.style.scale = new Scale(new Vector2(pop, pop));
                fly.Root.style.opacity = alpha;
            }
        }

        // ---- action bar sections --------------------------------------------------------------------

        private void BuildSections(IReadOnlyList<HudAction> actions)
        {
            ClearSlots();
            _actionBar.Clear();
            _apDots.Clear();

            // Action points first.
            var apGroup = new VisualElement { name = "group-ap", pickingMode = PickingMode.Ignore };
            apGroup.AddToClassList("action-group");
            var apCaption = new Label { text = "ACTION POINTS", pickingMode = PickingMode.Ignore };
            apCaption.AddToClassList("action-group-caption");
            apGroup.Add(apCaption);
            var apRow = new VisualElement { pickingMode = PickingMode.Ignore };
            apRow.AddToClassList("ap-row");
            for (int i = 0; i < _source.ApPerTurn; i++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("ap-dot");
                apRow.Add(dot);
                _apDots.Add(dot);
            }
            apGroup.Add(apRow);
            _actionBar.Add(apGroup);

            _groupOrder.Clear();
            for (int g = 0; g < Groups.Length; g++) _groupOrder.Add(Groups[g][0]);
            for (int i = 0; i < actions.Count; i++)
            {
                string category = actions[i].Category ?? string.Empty;
                if (!_groupOrder.Contains(category)) _groupOrder.Add(category);
            }

            // Slots are created in action order so _slots[i] always matches actions[i].
            var slotsByIndex = new VisualElement[actions.Count];
            for (int g = 0; g < _groupOrder.Count; g++)
            {
                string category = _groupOrder[g];
                bool any = false;
                for (int i = 0; i < actions.Count; i++) if ((actions[i].Category ?? string.Empty) == category) { any = true; break; }
                if (!any) continue;

                var divider = new VisualElement { pickingMode = PickingMode.Ignore };
                divider.AddToClassList("action-group-divider");
                _actionBar.Add(divider);

                var group = new VisualElement { name = "group-" + category, pickingMode = PickingMode.Ignore };
                group.AddToClassList("action-group");

                var caption = new Label { text = CaptionFor(category), pickingMode = PickingMode.Ignore };
                caption.AddToClassList("action-group-caption");
                group.Add(caption);

                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("action-group-row");
                group.Add(row);

                for (int i = 0; i < actions.Count; i++)
                {
                    if ((actions[i].Category ?? string.Empty) != category) continue;
                    VisualElement slot = CreateSlot(i);
                    row.Add(slot);
                    slotsByIndex[i] = slot;
                }

                _actionBar.Add(group);
            }

            for (int i = 0; i < slotsByIndex.Length; i++) _slots.Add(slotsByIndex[i]);
        }

        private static string CaptionFor(string category)
        {
            for (int g = 0; g < Groups.Length; g++) if (Groups[g][0] == category) return Groups[g][1];
            return string.IsNullOrEmpty(category) ? "OTHER" : category.ToUpperInvariant();
        }

        private VisualElement CreateSlot(int index)
        {
            var slot = new VisualElement { name = "slot-" + index, pickingMode = PickingMode.Position, userData = index };
            slot.AddToClassList("action-slot");

            var icon = new VisualElement { name = "icon", pickingMode = PickingMode.Ignore };
            icon.AddToClassList("action-icon");
            slot.Add(icon);

            var glyph = new Label { name = "glyph", pickingMode = PickingMode.Ignore };
            glyph.AddToClassList("action-glyph");
            slot.Add(glyph);

            var cost = new Label { name = "cost", pickingMode = PickingMode.Ignore };
            cost.AddToClassList("action-cost");
            slot.Add(cost);

            slot.RegisterCallback<ClickEvent>(HandleSlotClicked);
            slot.RegisterCallback<PointerEnterEvent>(HandleSlotEnter);
            slot.RegisterCallback<PointerLeaveEvent>(HandleSlotLeave);
            return slot;
        }

        private void ClearSlots()
        {
            for (int i = 0; i < _slots.Count; i++) if (_slots[i] != null) _slots[i].RemoveFromHierarchy();
            _slots.Clear();
            _hoveredSlot = -1;
            HideTooltip();
        }

        private void ApplyIcon(VisualElement icon, Label glyph, string iconKey, string fallbackName)
        {
            Sprite sprite;
            if (iconKey != null && _iconsByKey.TryGetValue(iconKey, out sprite))
            {
                icon.style.backgroundImage = new StyleBackground(sprite);
                glyph.style.display = DisplayStyle.None;
            }
            else
            {
                icon.style.backgroundImage = StyleKeyword.None;
                glyph.style.display = DisplayStyle.Flex;
                glyph.text = string.IsNullOrEmpty(fallbackName) ? "?" : fallbackName.Substring(0, 1).ToUpperInvariant();
            }
        }

        // ---- events ---------------------------------------------------------------------------------

        private void HandleSlotClicked(ClickEvent evt)
        {
            var slot = evt.currentTarget as VisualElement;
            if (slot == null || !(slot.userData is int index)) return;
            _source.SelectAction(index);
        }

        private void HandleSlotEnter(PointerEnterEvent evt)
        {
            var slot = evt.currentTarget as VisualElement;
            if (slot == null || !(slot.userData is int index)) return;

            IReadOnlyList<HudAction> actions = _source.Actions;
            if (index < 0 || index >= actions.Count) return;

            HudAction action = actions[index];
            _hoveredSlot = index;
            _tooltipTitle.text = (action.Name ?? action.Id) + "   ·   " + action.Cost + " AP";
            _tooltipBody.text = action.Description ?? string.Empty;
            _tooltipBody.style.display = string.IsNullOrEmpty(action.Description) ? DisplayStyle.None : DisplayStyle.Flex;
            _tooltip.AddToClassList("tooltip--visible");
            RefreshApDots();
        }

        private void HandleSlotLeave(PointerLeaveEvent evt)
        {
            _hoveredSlot = -1;
            HideTooltip();
            RefreshApDots();
        }

        private void HideTooltip()
        {
            if (_tooltip != null) _tooltip.RemoveFromClassList("tooltip--visible");
        }

        private void HandleEndTurnClicked() => _source.EndTurn();

        private void HandleExamineCloseClicked() => _source.CloseExamine();

        /// <summary>True when a pickable HUD element sits under the given screen position (bottom-left origin).</summary>
        private bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (!_bound || _root == null) return false;
            IPanel panel = _root.panel;
            if (panel == null) return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            return panel.Pick(panelPosition) != null;
        }
    }
}
