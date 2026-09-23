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
    /// turn owner with a burning rope (top centre), the examine plate (right, drawn by <see cref="ExamineView"/>
    /// from its own templates), End Turn (bottom right), plus the
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

        /// <summary>How long the resign button stays armed before it goes back to asking.</summary>
        private const float ResignConfirmSeconds = 3f;

        /// <summary>Longest opponent name the turn banner will show before it truncates.</summary>
        private const int MaxOpponentNameLength = 16;

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
        private readonly List<int> _staleTags = new List<int>();
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
        private ExamineView _examineView;
        private VisualElement _unitLayer;
        private VisualElement _preview;
        private VisualElement _previewLines;
        private VisualElement _flyLayer;
        private Label _turnOwner;
        private Label _tooltipTitle;
        private Label _tooltipDetail;
        private Label _tooltipBody;
        private Label _previewTitle;
        private Label _previewTotal;
        private Label _previewBlocked;
        private Label _cursorTag;
        private VisualElement _bannerPanel;
        private Label _banner;
        private Label _bannerDetail;
        private Button _bannerButton;
        private Label _statusLine;
        private Button _resign;

        // The series line and the draft over the dimmed board (P1, P2).
        private Label _seriesLine;
        private VisualElement _draftPanel;
        private Label _draftHeadline;
        private Label _draftNext;
        private VisualElement _draftCards;
        private VisualElement _draftTimerFill;
        private VisualElement _draftDot;
        private Label _draftStatus;
        private Button _draftConfirm;

        /// <summary>The card elements currently in the panel, in offer order.</summary>
        private readonly List<VisualElement> _draftSlots = new List<VisualElement>();

        private readonly System.Text.StringBuilder _draftKeys = new System.Text.StringBuilder();

        /// <summary>The ids the cards were built from, so a repaint does not rebuild them.</summary>
        private string _draftSignature;

        /// <summary>When the armed resign button gives up and goes back to asking.</summary>
        private float _resignArmedUntil;
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
            public Label Lineage;

            /// <summary>ex.ring: the 1px ring in the owner's colour while this unit is examined.</summary>
            public VisualElement Ring;
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

            // The armed resign button forgets on its own, and the way out of the result turns up a beat
            // after it. Both are time passing rather than anything changing, so they are polled.
            if (_resignArmedUntil > 0f && Time.time >= _resignArmedUntil) DisarmResign();

            bool showBack = !string.IsNullOrEmpty(_source.Banner) && _source.ShowBackToLobby;
            if (showBack != _bannerButton.ClassListContains("banner-button--visible"))
                _bannerButton.EnableInClassList("banner-button--visible", showBack);

            UpdateDraftTimer();
        }

        /// <summary>
        /// One bar for the draft's one deadline. A late message can leave it below zero: show nothing left
        /// and wait for the server's timeout pick, which is on its way (§13).
        /// </summary>
        private void UpdateDraftTimer()
        {
            HudDraft draft = _source.Draft;
            if (draft == null) return;
            float total = draft.SecondsTotal > 0f ? draft.SecondsTotal : 1f;
            float fraction = Mathf.Clamp01(draft.SecondsRemaining / total);
            _draftTimerFill.style.width = Length.Percent(fraction * 100f);
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            UpdateUnitTags();
            UpdatePreviewPosition();
            UpdateCursorTag();
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
            _tooltipDetail = root.Q<Label>("tooltip-detail");
            _tooltipBody = root.Q<Label>("tooltip-body");
            VisualElement examineMount = root.Q<VisualElement>("examine-mount");
            VisualElement hoverMount = root.Q<VisualElement>("hover-mount");
            _endTurn = root.Q<Button>("end-turn");
            _unitLayer = root.Q<VisualElement>("unit-layer");
            _preview = root.Q<VisualElement>("preview");
            _previewTitle = root.Q<Label>("preview-title");
            _previewTotal = root.Q<Label>("preview-total");
            _previewBlocked = root.Q<Label>("preview-blocked");
            _previewLines = root.Q<VisualElement>("preview-lines");
            _cursorTag = root.Q<Label>("cursor-tag");
            _flyLayer = root.Q<VisualElement>("fly-layer");
            _bannerPanel = root.Q<VisualElement>("banner-panel");
            _banner = root.Q<Label>("banner");
            _bannerDetail = root.Q<Label>("banner-detail");
            _bannerButton = root.Q<Button>("banner-button");
            _statusLine = root.Q<Label>("status-line");
            _resign = root.Q<Button>("resign");
            _seriesLine = root.Q<Label>("series-line");
            _draftPanel = root.Q<VisualElement>("draft-panel");
            _draftHeadline = root.Q<Label>("draft-headline");
            _draftNext = root.Q<Label>("draft-next");
            _draftCards = root.Q<VisualElement>("draft-cards");
            _draftTimerFill = root.Q<VisualElement>("draft-timer-fill");
            _draftDot = root.Q<VisualElement>("draft-dot");
            _draftStatus = root.Q<Label>("draft-status");
            _draftConfirm = root.Q<Button>("draft-confirm");

            if (_root == null || _turnPanel == null || _turnOwner == null || _rope == null || _ropeFill == null || _ropeEmber == null
                || _actionBar == null || _tooltip == null || _tooltipTitle == null || _tooltipDetail == null || _tooltipBody == null
                || examineMount == null || hoverMount == null || _endTurn == null
                || _unitLayer == null || _preview == null || _previewTitle == null || _previewTotal == null
                || _previewBlocked == null || _previewLines == null || _cursorTag == null
                || _flyLayer == null || _banner == null || _bannerPanel == null || _bannerDetail == null || _bannerButton == null
                || _statusLine == null || _resign == null
                || _seriesLine == null || _draftPanel == null || _draftHeadline == null || _draftNext == null
                || _draftCards == null || _draftTimerFill == null || _draftDot == null || _draftStatus == null || _draftConfirm == null)
            {
                Debug.LogError("[MatchHudView] MatchHud.uxml is missing one of the named elements.", this);
                enabled = false;
                return false;
            }

            _examineView = new ExamineView(_root, examineMount, hoverMount, HandleExamineClose);
            _actionBar.RegisterCallback<GeometryChangedEvent>(HandleActionBarGeometry);

            _draftConfirm.clicked += HandleDraftConfirmClicked;
            _endTurn.clicked += HandleEndTurnClicked;
            _resign.clicked += HandleResignClicked;
            _bannerButton.clicked += HandleBackToLobbyClicked;
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
            if (_examineView != null) { _examineView.Dispose(); _examineView = null; }
            _resign.clicked -= HandleResignClicked;
            _bannerButton.clicked -= HandleBackToLobbyClicked;
            _draftConfirm.clicked -= HandleDraftConfirmClicked;
            ClearDraftCards();
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
            _turnOwner.text = over ? "MATCH OVER" : (mine ? "YOUR TURN  ·  " + _source.TurnNumber : OpponentTurnLabel());
            _turnPanel.EnableInClassList("turn-panel--theirs", !mine && !over);
            _endTurn.SetEnabled(_source.CanEndTurn);
            RefreshStatusLine();
            RefreshResign();

            RefreshActionBar();
            RefreshExamine();
            RefreshPreview();
            RefreshBanner();
            RefreshSeriesLine();
            RefreshDraft();
            SyncUnitTags();
        }

        private void RefreshSeriesLine()
        {
            string line = _source.SeriesLine;
            bool visible = !string.IsNullOrEmpty(line);
            _seriesLine.text = line ?? string.Empty;
            _seriesLine.EnableInClassList("series-line--visible", visible);
        }

        /// <summary>
        /// The draft over the dimmed board. The cards are built once per offer set and then only restyled,
        /// so hovering one and selecting another does not rebuild the panel under the pointer.
        /// </summary>
        private void RefreshDraft()
        {
            HudDraft draft = _source.Draft;
            bool visible = draft != null;
            _draftPanel.EnableInClassList("draft-panel--visible", visible);
            if (!visible)
            {
                if (_draftSignature != null) ClearDraftCards();
                return;
            }

            _draftHeadline.text = draft.Headline ?? string.Empty;
            _draftNext.text = draft.NextRoundLine ?? string.Empty;

            _draftKeys.Length = 0;
            for (int i = 0; i < draft.Cards.Count; i++) _draftKeys.Append(draft.Cards[i].Id).Append('|');
            string signature = _draftKeys.ToString();
            if (signature != _draftSignature)
            {
                BuildDraftCards(draft);
                _draftSignature = signature;
            }

            for (int i = 0; i < _draftSlots.Count; i++)
            {
                _draftSlots[i].EnableInClassList("draft-card--selected", i == draft.Selected);
                _draftSlots[i].SetEnabled(!draft.Picked);
            }

            _draftDot.EnableInClassList("draft-dot--visible", draft.OpponentPicked);
            _draftStatus.text = draft.Status ?? string.Empty;
            _draftConfirm.text = draft.Picked ? "Kept" : "Confirm";
            _draftConfirm.SetEnabled(!draft.Picked && draft.Selected >= 0);
        }

        private void BuildDraftCards(HudDraft draft)
        {
            ClearDraftCards();
            for (int i = 0; i < draft.Cards.Count; i++)
            {
                HudDraftCard card = draft.Cards[i];
                int index = i;

                var root = new VisualElement { name = "draft-card-" + i };
                root.AddToClassList("draft-card");

                var kind = new Label(card.Kind != null ? card.Kind.ToUpperInvariant() : "") { name = "card-kind" };
                kind.AddToClassList("card-kind");
                if (!string.IsNullOrEmpty(card.Kind)) kind.AddToClassList("kind--" + card.Kind.ToLowerInvariant());
                kind.pickingMode = PickingMode.Ignore;
                root.Add(kind);

                var name = new Label(card.Name ?? "") { name = "card-name" };
                name.AddToClassList("card-name");
                name.pickingMode = PickingMode.Ignore;
                root.Add(name);

                string godLine = card.God;
                if (!string.IsNullOrEmpty(card.Lineage)) godLine = godLine + " · " + card.Lineage;
                var god = new Label(godLine ?? "") { name = "card-god" };
                god.AddToClassList("card-god");
                god.pickingMode = PickingMode.Ignore;
                root.Add(god);

                var effect = new Label(card.Effect ?? "") { name = "card-effect" };
                effect.AddToClassList("card-effect");
                effect.pickingMode = PickingMode.Ignore;
                root.Add(effect);

                var attach = new Label(card.Attach ?? "") { name = "card-attach" };
                attach.AddToClassList("card-attach");
                attach.pickingMode = PickingMode.Ignore;
                root.Add(attach);

                root.RegisterCallback<ClickEvent>(_ => _source.SelectDraftCard(index));
                _draftCards.Add(root);
                _draftSlots.Add(root);
            }
        }

        private void ClearDraftCards()
        {
            for (int i = 0; i < _draftSlots.Count; i++) _draftSlots[i].RemoveFromHierarchy();
            _draftSlots.Clear();
            _draftSignature = null;
        }

        private void HandleDraftConfirmClicked()
        {
            _source.ConfirmDraft();
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
                slot.EnableInClassList("action--modified", action.Modified);
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

        /// <summary>The plate is ExamineView's; the HUD only makes room for it (End Turn steps left, the bar re-centres).</summary>
        private void RefreshExamine()
        {
            HudExamine examine = _source.Examine;
            _examineView.Render(examine);
            _root.EnableInClassList("hud--examining", examine != null);
            CentreActionBar();
        }

        private void HandleActionBarGeometry(GeometryChangedEvent evt) => CentreActionBar();

        /// <summary>
        /// Centres the bar in pixels from its measured width. MatchHud.uss centres it with <c>translate: -50%</c>,
        /// which UI Toolkit resolved once, while the bar held only its action-point group, and never again as
        /// the sections were added: in the browser it sat ~220px right of centre. While the examine plate is
        /// open the bar centres in the board left of the plate, so it and End Turn both clear it at 1280×720.
        /// </summary>
        private void CentreActionBar()
        {
            if (_actionBar == null || _examineView == null) return;
            float width = _actionBar.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;
            float shift = _examineView.IsOpen ? -_examineView.PlateWidth * 0.5f : 0f;
            _actionBar.style.translate = new Translate(shift - width * 0.5f, 0f);
        }

        private void RefreshPreview()
        {
            HudPreview preview = _source.Preview;
            if (preview == null)
            {
                _preview.RemoveFromClassList("preview--visible");
                return;
            }

            bool blocked = !string.IsNullOrEmpty(preview.BlockedReason);
            _previewTitle.text = preview.AbilityName ?? string.Empty;
            _previewTotal.text = preview.IsExact ? preview.Total.ToString() : preview.Total + "?";
            _preview.EnableInClassList("preview--blocked", blocked);
            _previewBlocked.text = preview.BlockedReason ?? string.Empty;
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
            _bannerPanel.EnableInClassList("banner-panel--visible", visible);
            _banner.EnableInClassList("banner--lost", visible && banner != "VICTORY");

            string detail = _source.BannerDetail;
            _bannerDetail.text = string.IsNullOrEmpty(detail) ? string.Empty : detail.ToUpperInvariant();
            _bannerButton.EnableInClassList("banner-button--visible", visible && _source.ShowBackToLobby);

            // Where it leads is not always the lobby any more: online it goes back to the room this match
            // was played in, with the same code and the same two seats (ADR-032).
            if (visible) _bannerButton.text = _source.BackLabel;
        }

        /// <summary>The opponent's connection, when there is anything to say about it. Offline there never is.</summary>
        private void RefreshStatusLine()
        {
            string status = _source.OpponentStatus;
            bool visible = !string.IsNullOrEmpty(status);
            _statusLine.text = status ?? string.Empty;
            _statusLine.EnableInClassList("status-line--visible", visible);
        }

        private void RefreshResign()
        {
            bool can = _source.CanResign;
            _resign.EnableInClassList("resign--visible", can);
            _resign.SetEnabled(can);
            if (!can) DisarmResign();
        }

        /// <summary>"ROHAN'S TURN", trimmed to something that fits. A long name must not push the panel about.</summary>
        private string OpponentTurnLabel()
        {
            string name = _source.OpponentName;
            if (string.IsNullOrEmpty(name)) return "OPPONENT'S TURN";
            if (name.Length > MaxOpponentNameLength) name = name.Substring(0, MaxOpponentNameLength - 1) + "…";
            return name.ToUpperInvariant() + "'S TURN";
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
                tag.Root.EnableInClassList("unit-tag--examined", unit.Examined);

                // The god, once something has revealed it (design #lineage rule 3).
                bool hasLineage = !string.IsNullOrEmpty(unit.LineageTag);
                if (hasLineage) tag.Lineage.text = unit.LineageTag;
                tag.Lineage.style.display = hasLineage ? DisplayStyle.Flex : DisplayStyle.None;

                SyncMarkers(tag, unit);
            }

            // A destroyed prop leaves the list entirely (a dead hero only goes grey), so drop its tag with it.
            if (_unitTags.Count == units.Count) return;
            _staleTags.Clear();
            foreach (KeyValuePair<int, UnitTag> pair in _unitTags)
            {
                bool present = false;
                for (int i = 0; i < units.Count; i++)
                {
                    if (units[i].Id != pair.Key) continue;
                    present = true;
                    break;
                }
                if (!present) _staleTags.Add(pair.Key);
            }
            for (int i = 0; i < _staleTags.Count; i++)
            {
                UnitTag tag;
                if (!_unitTags.TryGetValue(_staleTags[i], out tag)) continue;
                tag.Root.RemoveFromHierarchy();
                _unitTags.Remove(_staleTags[i]);
            }
            _staleTags.Clear();
        }

        private UnitTag CreateUnitTag(HudUnit unit)
        {
            var tag = new UnitTag();
            tag.Root = new VisualElement { name = "unit-" + unit.Id, pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            tag.Root.AddToClassList("unit-tag");
            tag.Root.EnableInClassList("unit-tag--theirs", !unit.IsMine && !unit.IsProp);
            tag.Root.EnableInClassList("unit-tag--prop", unit.IsProp);

            tag.Bar = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.Bar.AddToClassList("unit-bar");
            tag.Root.Add(tag.Bar);

            tag.Ring = new VisualElement { name = "ex.ring", pickingMode = PickingMode.Ignore };
            tag.Ring.AddToClassList("ex-ring");
            tag.Root.Add(tag.Ring);

            tag.Number = new Label { pickingMode = PickingMode.Ignore };
            tag.Number.AddToClassList("unit-number");
            tag.Root.Add(tag.Number);

            tag.Lineage = new Label { name = "tag-lineage", pickingMode = PickingMode.Ignore };
            tag.Lineage.AddToClassList("tag-lineage");
            tag.Lineage.style.display = DisplayStyle.None;
            tag.Root.Add(tag.Lineage);

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

        /// <summary>
        /// Pins the refusal label just past the pointer. Screen pixels are bottom-left origin and the panel is
        /// top-left, so the y flips through the same helper the flyovers use.
        /// </summary>
        private void UpdateCursorTag()
        {
            string text = _source.CursorTag;
            bool visible = !string.IsNullOrEmpty(text);
            _cursorTag.EnableInClassList("cursor-tag--visible", visible);
            if (!visible) return;

            IPanel panel = _root.panel;
            if (panel == null) return;

            _cursorTag.text = text;
            Vector2 position = RuntimePanelUtils.ScreenToPanel(panel, _source.CursorScreenPosition);
            _cursorTag.style.left = position.x + 16f;
            _cursorTag.style.top = position.y + 16f;
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
            _tooltipDetail.text = action.Detail ?? string.Empty;
            _tooltipDetail.style.display = string.IsNullOrEmpty(action.Detail) ? DisplayStyle.None : DisplayStyle.Flex;
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

        /// <summary>
        /// First click arms it and says so; the second concedes. It disarms itself after a few seconds,
        /// because a resign button that stays armed is a resign button you press by accident.
        /// </summary>
        private void HandleResignClicked()
        {
            if (!_source.CanResign) return;

            if (Time.time < _resignArmedUntil)
            {
                DisarmResign();
                _source.Resign();
                return;
            }

            _resignArmedUntil = Time.time + ResignConfirmSeconds;
            _resign.text = "CONFIRM RESIGN";
            _resign.AddToClassList("resign--confirm");
        }

        private void DisarmResign()
        {
            if (_resignArmedUntil <= 0f) return;
            _resignArmedUntil = 0f;
            _resign.text = "RESIGN";
            _resign.RemoveFromClassList("resign--confirm");
        }

        private void HandleBackToLobbyClicked()
        {
            _source.BackToLobby();
        }

        private void HandleEndTurnClicked() => _source.EndTurn();

        private void HandleExamineClose() => _source.CloseExamine();

        /// <summary>True when a pickable HUD element sits under the given screen position (bottom-left origin).</summary>
        private bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (!_bound || _root == null) return false;
            // The off-plate click that closed examine is the plate's, even if the scrim is already gone (E10).
            if (_examineView != null && _examineView.SwallowsBoardPointer) return true;
            IPanel panel = _root.panel;
            if (panel == null) return false;
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(panel, screenPosition);
            return panel.Pick(panelPosition) != null;
        }
    }
}
