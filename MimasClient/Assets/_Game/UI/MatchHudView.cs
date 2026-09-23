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
    /// The in-match HUD in the ink language (docs/ui/hud.md, docs/ui/between-rounds.md; spec H §7): the turn track
    /// top centre, action points and three captioned lane rows bottom left, your boons down the right edge,
    /// Resign above End Turn bottom right, a tag over every body, the attack preview as the one hover panel over
    /// the target, flyovers, the draft over the dimmed board and the round moments in the band. The examine plate
    /// is <see cref="ExamineView"/>'s, a layer over all of it; the hover panel is shared by everything
    /// (<see cref="HoverPanel"/>). Package D's behaviour is kept (ADR-019): nothing armed by default, a click arms,
    /// the armed tile disarms, an unaffordable tile stays on the bar and does nothing.
    /// <para>
    /// Reads everything from an <see cref="IMatchHudSource"/> and never touches Core: every word, number and flag
    /// was composed by the presenter (<c>MatchSession</c>, <c>HudModel</c>); this class picks classes, formats and
    /// places. Overlays are screen-space elements repositioned every LateUpdate with
    /// <see cref="RuntimePanelUtils.CameraTransformWorldToPanel"/> (world-space UI Toolkit is not trusted on
    /// WebGL2 yet).
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class MatchHudView : MonoBehaviour
    {
        /// <summary>Lane order and captions (hud.md §3.2). An unknown category is appended with its key upper-cased.</summary>
        private static readonly string[][] Lanes =
        {
            new[] { AbilityCategories.Movement, "MOVEMENT" },
            new[] { AbilityCategories.Weapon, "WEAPON" },
            new[] { AbilityCategories.Spell, "SPELL" },
        };

        /// <summary>How long the resign button stays armed before it goes back to asking.</summary>
        private const float ResignConfirmSeconds = 3f;

        /// <summary>Longest name either end of the track shows before it truncates.</summary>
        private const int MaxNameLength = 16;

        private const float FlyoverSeconds = 2f;
        private const float FlyoverRise = 36f;
        private const long HoverDelayMs = 120;

        /// <summary>The card's ground before its style resolves: language §1 <c>ink-2</c>; and bone for an unknown lineage's wash.</summary>
        private static readonly Color InkTwo = new Color32(18, 18, 23, 255);
        private static readonly Color Bone = new Color32(239, 233, 220, 255);

        [Tooltip("Any component implementing IMatchHudSource (MatchSession).")]
        [SerializeField] private MonoBehaviour _sourceBehaviour;

        [Tooltip("Board input to shield from clicks that land on the HUD.")]
        [SerializeField] private BoardInputController _boardInput;

        [Tooltip("Sprites matched to ability 'icon' keys by sprite name. A key with no sprite shows the action's letter.")]
        [SerializeField] private Sprite[] _icons;

        private readonly Dictionary<string, Sprite> _iconsByKey = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private readonly StringBuilder _signature = new StringBuilder();

        // The one hover panel has one owner at a time: the plate, a tile, a boon or the preview.
        private readonly object _tileHover = new object();
        private readonly object _boonHover = new object();
        private readonly object _previewHover = new object();

        private UIDocument _document;
        private IMatchHudSource _source;
        private Func<Vector2, bool> _pointerBlocker;
        private bool _bound;

        private VisualElement _root;
        private HoverPanel _hover;
        private ExamineView _examineView;
        private IVisualElementScheduledItem _pendingHover;

        // The track.
        private VisualElement _track;
        private Label _youName, _themName, _round;
        private VisualElement _youPips, _themPips, _trackFill, _trackEmber;
        private VisualElement _status, _picked;
        private Label _statusText;
        private int _pipCount = -1;

        // The bar.
        private VisualElement _bar, _eggs, _lanes;
        private Label _apCurrent, _apMax;
        private readonly Dictionary<string, VisualElement> _laneTiles = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, VisualElement> _laneRows = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly List<VisualElement> _extraLanes = new List<VisualElement>();
        private readonly List<TileView> _tiles = new List<TileView>();
        private readonly List<VisualElement> _eggViews = new List<VisualElement>();
        private string _barSignature;
        private int _hoveredTile = -1;

        // Boons, the buttons, the tags, the flyovers, the cursor.
        private VisualElement _boons;
        private string _boonsSignature;
        private Button _resign, _end;
        private Label _endLabel;
        private float _resignArmedUntil;
        private VisualElement _unitLayer, _flyLayer;
        private Label _cursor;
        private readonly Dictionary<int, TagView> _tags = new Dictionary<int, TagView>();
        private readonly List<int> _staleTags = new List<int>();
        private readonly List<FlyoverInstance> _flyovers = new List<FlyoverInstance>();

        // The draft.
        private VisualElement _drRoot, _drCards, _drTimerFill, _drTimerEmber;
        private Label _drHeadline, _drNext, _drStatus;
        private Button _drConfirm;
        private readonly List<Button> _cards = new List<Button>();
        private readonly List<Glyph> _cardKinds = new List<Glyph>();
        private string _draftSignature;

        // The moments.
        private VisualElement _moRoot, _moBand, _moScore;
        private Label _moKicker, _moTitle, _moMine, _moTheirs, _moSub;
        private Button _moBack;

        private HudPreview _shownPreview;

        private sealed class TileView
        {
            public VisualElement Root;
            public Label Letter;
            public VisualElement Icon;
            public VisualElement Cost;
            public Glyph Mark;
            public int Dots = -1;
        }

        private sealed class TagView
        {
            public VisualElement Root;
            public Label Hp;
            public Label Max;
            public VisualElement Fill;
            public VisualElement Ghost;
            public VisualElement Marks;
            public Label Lineage;
            public string MarkSignature;
        }

        private sealed class FlyoverInstance
        {
            public VisualElement Root;
            public Vector3 WorldPosition;
            public float StartTime;
        }

        // ---- lifecycle ------------------------------------------------------------------------------

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
                for (int i = 0; i < _icons.Length; i++)
                    if (_icons[i] != null) _iconsByKey[_icons[i].name] = _icons[i];

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

        private void OnDestroy()
        {
            // The generated fades are shared by everything on this HUD; the next scene makes its own.
            Ramps.Release();
        }

        private void Update()
        {
            if (!_bound && !TryBind()) return;
            UpdateTrack();
            UpdateDraftTimer();

            // Time passing rather than anything changing: the armed resign forgets, and the way out of a result
            // turns up a beat after it (the presenter flips the flag; the label can follow the room).
            if (_resignArmedUntil > 0f && Time.time >= _resignArmedUntil) DisarmResign();
            HudMoment moment = _source.Moment;
            bool back = moment != null && moment.ShowBack;
            SetDisplay(_moBack, back);
            if (back)
            {
                string label = (moment.BackLabel ?? "Back to lobby").ToUpperInvariant();
                if (_moBack.text != label) _moBack.text = label;
            }
        }

        private void LateUpdate()
        {
            if (!_bound) return;
            UpdateTagPositions();
            UpdatePreviewPosition();
            UpdateCursor();
            UpdateFlyovers();
        }

        // ---- binding --------------------------------------------------------------------------------

        private bool TryBind()
        {
            VisualElement root = _document.rootVisualElement;
            if (root == null) return false;

            // The theme's fonts are loaded with the document: let their letter-spacing apply to every pair.
            FontSpacing.RepairLoaded();

            var missing = new List<string>();
            _root = Find<VisualElement>(root, "hud-root", missing);
            VisualElement floor = Find<VisualElement>(root, "hud.floor", missing);
            VisualElement floorTop = Find<VisualElement>(root, "hud.floor.top", missing);
            _unitLayer = Find<VisualElement>(root, "unit-layer", missing);
            _flyLayer = Find<VisualElement>(root, "fly-layer", missing);
            _cursor = Find<Label>(root, "hud.cursor", missing);

            _track = Find<VisualElement>(root, "hud.track", missing);
            _youName = Find<Label>(root, "hud.track.you.name", missing);
            _themName = Find<Label>(root, "hud.track.them.name", missing);
            _youPips = Find<VisualElement>(root, "hud.track.you.pips", missing);
            _themPips = Find<VisualElement>(root, "hud.track.them.pips", missing);
            _round = Find<Label>(root, "hud.track.round", missing);
            Find<VisualElement>(root, "hud.track.line", missing);
            _trackFill = Find<VisualElement>(root, "hud.track.fill", missing);
            Find<VisualElement>(root, "hud.track.marker", missing);
            _trackEmber = Find<VisualElement>(root, "hud.track.ember", missing);
            _status = Find<VisualElement>(root, "hud.track.status", missing);
            _statusText = Find<Label>(root, "hud.track.status.text", missing);
            _picked = Find<VisualElement>(root, "hud.track.picked", missing);

            _bar = Find<VisualElement>(root, "hud.bar", missing);
            Find<VisualElement>(root, "hud.ap", missing);
            Find<Label>(root, "hud.ap.caption", missing);
            Find<VisualElement>(root, "hud.ap.value", missing);
            _apCurrent = Find<Label>(root, "hud.ap.value.current", missing);
            _apMax = Find<Label>(root, "hud.ap.value.max", missing);
            _eggs = Find<VisualElement>(root, "hud.ap.eggs", missing);
            _lanes = Find<VisualElement>(root, "hud.lanes", missing);
            for (int i = 0; i < Lanes.Length; i++)
            {
                string category = Lanes[i][0];
                _laneRows[category] = Find<VisualElement>(root, "hud.lane." + category, missing);
                _laneTiles[category] = Find<VisualElement>(root, "hud.lane." + category + ".tiles", missing);
            }

            _boons = Find<VisualElement>(root, "hud.boons", missing);
            _resign = Find<Button>(root, "hud.resign", missing);
            _end = Find<Button>(root, "hud.end", missing);
            _endLabel = Find<Label>(root, "hud.end.label", missing);

            _drRoot = Find<VisualElement>(root, "dr.root", missing);
            Find<VisualElement>(root, "dr.scrim", missing);
            _drHeadline = Find<Label>(root, "dr.headline", missing);
            _drNext = Find<Label>(root, "dr.next", missing);
            _drCards = Find<VisualElement>(root, "dr.cards", missing);
            Find<VisualElement>(root, "dr.timer", missing);
            _drTimerFill = Find<VisualElement>(root, "dr.timer.fill", missing);
            _drTimerEmber = Find<VisualElement>(root, "dr.timer.ember", missing);
            _drConfirm = Find<Button>(root, "dr.confirm", missing);
            _drStatus = Find<Label>(root, "dr.status", missing);

            _moRoot = Find<VisualElement>(root, "mo.root", missing);
            _moBand = Find<VisualElement>(root, "mo.band", missing);
            _moKicker = Find<Label>(root, "mo.kicker", missing);
            _moTitle = Find<Label>(root, "mo.title", missing);
            _moScore = Find<VisualElement>(root, "mo.score", missing);
            _moMine = Find<Label>(root, "mo.score.mine", missing);
            _moTheirs = Find<Label>(root, "mo.score.theirs", missing);
            _moSub = Find<Label>(root, "mo.sub", missing);
            _moBack = Find<Button>(root, "mo.back", missing);

            VisualElement examineMount = Find<VisualElement>(root, "examine-mount", missing);
            VisualElement hoverMount = Find<VisualElement>(root, "hover-mount", missing);

            if (missing.Count > 0)
            {
                Debug.LogError("[MatchHudView] MatchHud.uxml is missing: " + string.Join(", ", missing), this);
                enabled = false;
                return false;
            }

            // The fades USS cannot draw, tinted by their tokens in MatchHud.uss (spec H §3).
            floor.style.backgroundImage = new StyleBackground(Ramps.Vertical(false, 0.58f));
            floorTop.style.backgroundImage = new StyleBackground(Ramps.Vertical(true, 0.5f));
            _moBand.style.backgroundImage = new StyleBackground(Ramps.BothEnds());

            _hover = new HoverPanel(hoverMount);
            _examineView = new ExamineView(_root, examineMount, _hover, HandleExamineClose);

            _end.clicked += HandleEndClicked;
            _resign.clicked += HandleResignClicked;
            _drConfirm.clicked += HandleConfirmClicked;
            _moBack.clicked += HandleBackClicked;
            _bound = true;
            // A rebind (the document reloading) gets a fresh tree: everything built in code is built again.
            _barSignature = null;
            _boonsSignature = null;
            _pipCount = -1;
            _shownPreview = null;
            Refresh();
            UpdateTrack();
            return true;
        }

        private void Unbind()
        {
            if (!_bound) return;
            _end.clicked -= HandleEndClicked;
            _resign.clicked -= HandleResignClicked;
            _drConfirm.clicked -= HandleConfirmClicked;
            _moBack.clicked -= HandleBackClicked;
            if (_examineView != null) { _examineView.Dispose(); _examineView = null; }
            CancelPendingHover();
            _hover = null;
            ClearBar();
            ClearCards();
            foreach (TagView tag in _tags.Values) tag.Root.RemoveFromHierarchy();
            _tags.Clear();
            for (int i = 0; i < _flyovers.Count; i++) _flyovers[i].Root.RemoveFromHierarchy();
            _flyovers.Clear();
            _bound = false;
        }

        // ---- state -> visuals ---------------------------------------------------------------------------

        /// <summary>Everything but the track's burn, the timers and the world-anchored layer, which tick every frame.</summary>
        private void Refresh()
        {
            if (!_bound) return;

            // Between rounds, and while any moment holds the screen, the bar, the boons and the buttons step aside
            // (the round card's boards and the results' draw only the track and the band).
            _root.EnableInClassList("hud--between", _source.Phase != HudPhase.Round || _source.Moment != null);

            RefreshTrack();
            RefreshBar();
            RefreshBoons();
            RefreshButtons();
            _examineView.Render(_source.Examine);
            RefreshPreview();
            RefreshDraft();
            RefreshMoment();
            SyncTags();
        }

        // ---- the turn track (hud.md §3.1) ------------------------------------------------------------

        private void RefreshTrack()
        {
            HudPhase phase = _source.Phase;
            _youName.text = Trim(_source.MyName, "YOU");
            _themName.text = Trim(_source.OpponentName, "OPPONENT");
            _round.text = phase == HudPhase.Draft ? "DRAFT"
                : phase == HudPhase.Over ? "SERIES"
                : _source.RoundNumber > 0 ? "ROUND " + _source.RoundNumber : string.Empty;

            int perSide = Mathf.Max(0, _source.RoundsToWin);
            if (perSide != _pipCount)
            {
                BuildPips(_youPips, perSide);
                BuildPips(_themPips, perSide);
                _pipCount = perSide;
            }
            FillPips(_youPips, _source.ScoreMine);
            FillPips(_themPips, _source.ScoreTheirs);

            // Under their end: their connection, or — between rounds — that they have chosen.
            string status = _source.OpponentStatus;
            bool hasStatus = !string.IsNullOrEmpty(status);
            _statusText.text = hasStatus ? status.ToUpperInvariant() : string.Empty;
            _status.EnableInClassList("hud-track__note--visible", hasStatus);
            HudDraft draft = _source.Draft;
            _picked.EnableInClassList("hud-track__note--visible", draft != null && draft.OpponentPicked);
        }

        private static void BuildPips(VisualElement container, int count)
        {
            container.Clear();
            for (int i = 0; i < count; i++)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList("hud-pip");
                container.Add(pip);
            }
        }

        private static void FillPips(VisualElement container, int won)
        {
            for (int i = 0; i < container.childCount; i++) container[i].EnableInClassList("hud-pip--won", i < won);
        }

        /// <summary>
        /// Lights the active half, and in the rope's last seconds burns it towards the centre with the ember at
        /// its outer end (hud.md §3.1; spec H §7.5). Nothing is lit between rounds or while a result holds.
        /// </summary>
        private void UpdateTrack()
        {
            if (_source == null || _track == null) return;
            HudMoment moment = _source.Moment;
            bool lit = _source.Phase == HudPhase.Round && (moment == null || moment.Kind == HudMomentKind.RoundStart);
            bool mine = _source.IsMyTurn;
            _track.EnableInClassList("hud-track--lit", lit);
            _track.EnableInClassList("hud-track--mine", lit && mine);
            _track.EnableInClassList("hud-track--theirs", lit && !mine);

            float remaining = _source.TurnSecondsRemaining;
            float rope = _source.RopeSeconds;
            bool burning = lit && HudModel.IsBurning(remaining, rope, _source.TurnSecondsTotal);
            _track.EnableInClassList("hud-track--burning", burning);
            if (!lit) return;

            float half = 50f * (burning ? HudModel.LitFraction(remaining, rope) : 1f);
            _trackFill.style.left = Length.Percent(mine ? 50f - half : 50f);
            _trackFill.style.width = Length.Percent(half);
            if (!burning) return;

            _trackEmber.style.left = Length.Percent(mine ? 50f - half : 50f + half);
            // The ember flickers faster as the rope gets short.
            float f = half / 50f;
            float pulse = 0.85f + 0.3f * Mathf.PingPong(Time.unscaledTime * (2f + (1f - f) * 4f), 1f);
            _trackEmber.style.scale = new Scale(new Vector2(pulse, pulse));
        }

        // ---- the bar (hud.md §3.2) -------------------------------------------------------------------

        private void RefreshBar()
        {
            IReadOnlyList<HudAction> actions = _source.Actions;

            // Rebuild only when the set of actions changes; otherwise restyle in place, so a hover survives a repaint.
            _signature.Length = 0;
            _signature.Append("ap").Append(_source.ApPerTurn).Append('|');
            for (int i = 0; i < actions.Count; i++) _signature.Append(actions[i].Category).Append(':').Append(actions[i].Id).Append('|');
            string signature = _signature.ToString();
            if (signature != _barSignature)
            {
                BuildBar(actions);
                _barSignature = signature;
            }

            int armed = _source.ActiveActionIndex;
            for (int i = 0; i < actions.Count && i < _tiles.Count; i++)
            {
                HudAction action = actions[i];
                TileView tile = _tiles[i];
                tile.Root.EnableInClassList("hud-tile--armed", i == armed);
                tile.Root.EnableInClassList("hud-tile--unaffordable", !action.Affordable);
                tile.Root.EnableInClassList("hud-tile--added", action.Added);
                tile.Root.EnableInClassList("hud-tile--changed", action.Modified && !action.Added);
                tile.Root.EnableInClassList("hud-tile--unseen", action.UnseenByThem);
                tile.Mark.Shape = action.Added ? "sigil" : "enchant";
                ApplyIcon(tile, action);
                if (tile.Dots != action.Cost) BuildDots(tile, action.Cost);
            }

            _bar.EnableInClassList("hud-bar--theirs", !_source.IsMyTurn);
            _apCurrent.text = _source.ApCurrent.ToString();
            _apMax.text = "/ " + _source.ApPerTurn;
            RefreshEggs();
        }

        private void BuildBar(IReadOnlyList<HudAction> actions)
        {
            ClearBar();

            for (int i = 0; i < _source.ApPerTurn; i++)
            {
                var egg = new VisualElement { pickingMode = PickingMode.Ignore };
                egg.AddToClassList("hud-egg");
                _eggs.Add(egg);
                _eggViews.Add(egg);
            }

            // Tiles in action order, so _tiles[i] is actions[i]; each into its lane's row.
            for (int i = 0; i < actions.Count; i++)
            {
                string category = actions[i].Category ?? string.Empty;
                VisualElement row;
                if (!_laneTiles.TryGetValue(category, out row)) row = AddLane(category);
                TileView tile = CreateTile(actions[i], i);
                row.Add(tile.Root);
                _tiles.Add(tile);
            }
            foreach (KeyValuePair<string, VisualElement> pair in _laneTiles)
                _laneRows[pair.Key].EnableInClassList("hud-lane--empty", pair.Value.childCount == 0);
        }

        /// <summary>A category the book does not name gets its own row, captioned with its key upper-cased.</summary>
        private VisualElement AddLane(string category)
        {
            var row = new VisualElement { name = "hud.lane." + category, pickingMode = PickingMode.Ignore };
            row.AddToClassList("hud-lane");
            var caption = new Label(string.IsNullOrEmpty(category) ? "OTHER" : category.ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
            caption.AddToClassList("hud-lane__caption");
            row.Add(caption);
            var tiles = new VisualElement { name = "hud.lane." + category + ".tiles", pickingMode = PickingMode.Ignore };
            tiles.AddToClassList("hud-lane__tiles");
            row.Add(tiles);
            _lanes.Add(row);
            _extraLanes.Add(row);
            _laneRows[category] = row;
            _laneTiles[category] = tiles;
            return tiles;
        }

        private TileView CreateTile(HudAction action, int index)
        {
            var tile = new TileView
            {
                Root = new VisualElement { name = "hud.tile[" + action.Id + "]", pickingMode = PickingMode.Position, userData = index },
            };
            tile.Root.AddToClassList("hud-tile");
            // The icon key rides on a class, so an art pass can bind a sprite to it (hud.md §7).
            if (!string.IsNullOrEmpty(action.Icon)) tile.Root.AddToClassList("icon--" + action.Icon);

            tile.Icon = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.Icon.AddToClassList("hud-tile__icon");
            tile.Root.Add(tile.Icon);
            tile.Letter = new Label { pickingMode = PickingMode.Ignore };
            tile.Letter.AddToClassList("hud-tile__letter");
            tile.Root.Add(tile.Letter);
            tile.Cost = new VisualElement { pickingMode = PickingMode.Ignore };
            tile.Cost.AddToClassList("hud-tile__cost");
            tile.Root.Add(tile.Cost);
            tile.Mark = new Glyph("enchant") { Filled = true };
            tile.Mark.AddToClassList("hud-tile__mark");
            tile.Root.Add(tile.Mark);
            var eye = new Glyph("eye-closed");
            eye.AddToClassList("hud-tile__eye");
            tile.Root.Add(eye);

            tile.Root.RegisterCallback<ClickEvent>(HandleTileClicked);
            tile.Root.RegisterCallback<PointerEnterEvent>(HandleTileEnter);
            tile.Root.RegisterCallback<PointerLeaveEvent>(HandleTileLeave);
            return tile;
        }

        private static void BuildDots(TileView tile, int cost)
        {
            tile.Cost.Clear();
            for (int d = 0; d < cost; d++)
            {
                var dot = new VisualElement { pickingMode = PickingMode.Ignore };
                dot.AddToClassList("hud-tile__dot");
                tile.Cost.Add(dot);
            }
            tile.Dots = cost;
        }

        private void ApplyIcon(TileView tile, HudAction action)
        {
            Sprite sprite = null;
            bool art = action.Icon != null && _iconsByKey.TryGetValue(action.Icon, out sprite);
            tile.Icon.style.backgroundImage = art ? new StyleBackground(sprite) : new StyleBackground(StyleKeyword.None);
            tile.Letter.style.display = art ? DisplayStyle.None : DisplayStyle.Flex;
            string name = action.Name ?? action.Id;
            tile.Letter.text = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpperInvariant();
        }

        private void ClearBar()
        {
            for (int i = 0; i < _tiles.Count; i++) _tiles[i].Root.RemoveFromHierarchy();
            _tiles.Clear();
            for (int i = 0; i < _extraLanes.Count; i++)
            {
                VisualElement row = _extraLanes[i];
                string key = row.name.Substring("hud.lane.".Length);
                _laneRows.Remove(key);
                _laneTiles.Remove(key);
                row.RemoveFromHierarchy();
            }
            _extraLanes.Clear();
            if (_eggs != null) _eggs.Clear();
            _eggViews.Clear();
            _hoveredTile = -1;
            if (_hover != null) _hover.Hide(_tileHover);
        }

        /// <summary>Held eggs in <c>you</c>; the ones the hovered or armed action would spend, from the right of the held ones (state.spends).</summary>
        private void RefreshEggs()
        {
            int current = _source.ApCurrent;
            IReadOnlyList<HudAction> actions = _source.Actions;
            int focus = _hoveredTile >= 0 ? _hoveredTile : _source.ActiveActionIndex;
            int spends = focus >= 0 && focus < actions.Count && actions[focus].Affordable ? actions[focus].Cost : 0;
            for (int i = 0; i < _eggViews.Count; i++)
            {
                bool held = i < current;
                bool spending = held && i >= current - spends;
                _eggViews[i].EnableInClassList("hud-egg--held", held && !spending);
                _eggViews[i].EnableInClassList("hud-egg--spends", spending);
            }
        }

        private void HandleTileClicked(ClickEvent evt)
        {
            var tile = evt.currentTarget as VisualElement;
            if (tile == null || !(tile.userData is int index)) return;
            _source.SelectAction(index);
        }

        private void HandleTileEnter(PointerEnterEvent evt)
        {
            var element = evt.currentTarget as VisualElement;
            if (element == null || !(element.userData is int index)) return;
            IReadOnlyList<HudAction> actions = _source.Actions;
            if (index < 0 || index >= actions.Count) return;

            _hoveredTile = index;
            RefreshEggs();
            CancelPendingHover();
            _pendingHover = element.schedule.Execute(() =>
            {
                _pendingHover = null;
                IReadOnlyList<HudAction> now = _source.Actions;
                if (index >= now.Count || _hover == null) return;
                _hover.Show(_tileHover, TileHover(now[index]), true, HoverPlacement.Above, element.worldBound, _root.worldBound);
            }).StartingIn(HoverDelayMs);
        }

        private void HandleTileLeave(PointerLeaveEvent evt)
        {
            _hoveredTile = -1;
            CancelPendingHover();
            if (_hover != null) _hover.Hide(_tileHover);
            RefreshEggs();
            RefreshPreview();
        }

        /// <summary>The plate's panel for the same ability (ADR-040), plus one line when the opponent has not seen it.</summary>
        private static HoverContent TileHover(HudAction action)
        {
            HoverContent c = action.Hover != null
                ? HoverContents.Tile(action.Hover)
                : new HoverContent { Letter = string.IsNullOrEmpty(action.Name) ? "?" : action.Name.Substring(0, 1), Name = action.Name, Text = action.Description };
            if (action.UnseenByThem) c.Note = HoverContents.UnseenByThemNote;
            return c;
        }

        private void CancelPendingHover()
        {
            if (_pendingHover == null) return;
            _pendingHover.Pause();
            _pendingHover = null;
        }

        // ---- your boons (hud.md §3.3) ----------------------------------------------------------------

        private void RefreshBoons()
        {
            IReadOnlyList<HudBoon> boons = _source.MyBoons;
            _signature.Length = 0;
            for (int i = 0; i < boons.Count; i++) _signature.Append(boons[i].Id).Append(boons[i].Starting ? "*" : "").Append('|');
            string signature = _signature.ToString();
            if (signature == _boonsSignature) return;
            _boonsSignature = signature;

            if (_hover != null) _hover.Hide(_boonHover);
            _boons.Clear();
            for (int i = 0; i < boons.Count; i++)
            {
                HudBoon boon = boons[i];
                var circle = new VisualElement { name = "hud.boon[" + i + "]", pickingMode = PickingMode.Position };
                circle.AddToClassList("hud-boon");
                var glyph = new Glyph(HoverContents.KindShape(boon.Kind)) { Filled = boon.Starting };
                glyph.AddToClassList("hud-boon__glyph");
                circle.Add(glyph);

                HudBoon captured = boon;
                circle.RegisterCallback<PointerEnterEvent>(evt =>
                {
                    CancelPendingHover();
                    _pendingHover = circle.schedule.Execute(() =>
                    {
                        _pendingHover = null;
                        if (_hover != null)
                            _hover.Show(_boonHover, HoverContents.Boon(captured), true, HoverPlacement.Left, circle.worldBound, _root.worldBound);
                    }).StartingIn(HoverDelayMs);
                });
                circle.RegisterCallback<PointerLeaveEvent>(evt =>
                {
                    CancelPendingHover();
                    if (_hover != null) _hover.Hide(_boonHover);
                });
                _boons.Add(circle);
            }
        }

        // ---- End Turn and Resign (hud.md §3.4) ---------------------------------------------------------

        private void RefreshButtons()
        {
            bool mine = _source.IsMyTurn;
            _end.SetEnabled(_source.CanEndTurn);
            _endLabel.text = mine ? "END TURN" : "THEIR TURN";
            _end.EnableInClassList("hud-end--theirs", !mine);
            _end.EnableInClassList("hud-end--done", mine && _source.CanEndTurn && HudModel.IsDone(true, _source.Actions));

            bool can = _source.CanResign;
            _resign.EnableInClassList("hud-resign--visible", can);
            _resign.SetEnabled(can);
            if (!can) DisarmResign();
        }

        /// <summary>
        /// First click arms it and says so, in their colour; the second concedes. It disarms itself after a few
        /// seconds, because a resign button that stays armed is one you press by accident.
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
            _resign.text = "CLICK AGAIN TO RESIGN";
            _resign.AddToClassList("hud-resign--armed");
        }

        private void DisarmResign()
        {
            if (_resignArmedUntil <= 0f) return;
            _resignArmedUntil = 0f;
            _resign.text = "RESIGN";
            _resign.RemoveFromClassList("hud-resign--armed");
        }

        private void HandleEndClicked() => _source.EndTurn();

        private void HandleExamineClose() => _source.CloseExamine();

        // ---- tags (hud.md §3.5) ----------------------------------------------------------------------

        private void SyncTags()
        {
            IReadOnlyList<HudUnit> units = _source.Units;
            for (int i = 0; i < units.Count; i++)
            {
                HudUnit unit = units[i];
                TagView tag;
                if (!_tags.TryGetValue(unit.Id, out tag))
                {
                    tag = CreateTag(unit);
                    _tags[unit.Id] = tag;
                }

                int max = Mathf.Max(1, unit.MaxHp);
                int hp = Mathf.Clamp(unit.Hp, 0, max);
                int ghost = Mathf.Clamp(unit.GhostDamage, 0, hp);
                tag.Hp.text = unit.Hp.ToString();
                tag.Max.text = "/ " + unit.MaxHp;
                tag.Fill.style.width = Length.Percent(100f * (hp - ghost) / max);
                tag.Ghost.style.width = Length.Percent(100f * ghost / max);
                tag.Root.EnableInClassList("hud-tag--emphasised", unit.Emphasised || ghost > 0);
                tag.Root.EnableInClassList("hud-tag--dead", !unit.IsAlive);
                tag.Root.EnableInClassList("hud-tag--examined", unit.Examined);

                bool hasLineage = !string.IsNullOrEmpty(unit.LineageTag) && !unit.IsMine && !unit.IsProp;
                tag.Lineage.text = hasLineage ? unit.LineageTag.ToUpperInvariant() : string.Empty;
                SetDisplay(tag.Lineage, hasLineage);
                SyncMarks(tag, unit);
            }

            // A destroyed prop leaves the list entirely (a dead hero only hides), so drop its tag with it.
            if (_tags.Count == units.Count) return;
            _staleTags.Clear();
            foreach (KeyValuePair<int, TagView> pair in _tags)
            {
                bool present = false;
                for (int i = 0; i < units.Count && !present; i++) present = units[i].Id == pair.Key;
                if (!present) _staleTags.Add(pair.Key);
            }
            for (int i = 0; i < _staleTags.Count; i++)
            {
                _tags[_staleTags[i]].Root.RemoveFromHierarchy();
                _tags.Remove(_staleTags[i]);
            }
            _staleTags.Clear();
        }

        private TagView CreateTag(HudUnit unit)
        {
            string id = "hud.tag[" + unit.Id + "]";
            var tag = new TagView { Root = new VisualElement { name = id, pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform } };
            tag.Root.AddToClassList("hud-tag");
            tag.Root.EnableInClassList("hud-tag--theirs", !unit.IsMine && !unit.IsProp);
            tag.Root.EnableInClassList("hud-tag--prop", unit.IsProp);

            var number = new VisualElement { name = id + ".number", pickingMode = PickingMode.Ignore };
            number.AddToClassList("hud-tag__number");
            tag.Hp = new Label { pickingMode = PickingMode.Ignore };
            tag.Hp.AddToClassList("hud-tag__hp");
            number.Add(tag.Hp);
            tag.Max = new Label { pickingMode = PickingMode.Ignore };
            tag.Max.AddToClassList("hud-tag__max");
            number.Add(tag.Max);
            tag.Root.Add(number);

            var bar = new VisualElement { name = id + ".bar", pickingMode = PickingMode.Ignore };
            bar.AddToClassList("hud-tag__bar");
            tag.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.Fill.AddToClassList("hud-tag__fill");
            bar.Add(tag.Fill);
            tag.Ghost = new VisualElement { pickingMode = PickingMode.Ignore };
            tag.Ghost.AddToClassList("hud-tag__ghost");
            bar.Add(tag.Ghost);
            tag.Root.Add(bar);

            tag.Marks = new VisualElement { name = id + ".marks", pickingMode = PickingMode.Ignore };
            tag.Marks.AddToClassList("hud-tag__marks");
            tag.Root.Add(tag.Marks);

            tag.Lineage = new Label { name = id + ".lineage", pickingMode = PickingMode.Ignore };
            tag.Lineage.AddToClassList("hud-tag__lineage");
            tag.Root.Add(tag.Lineage);

            // ex.ring (docs/ui/examine.md §3.5): the owner's ring while this unit is examined.
            var ring = new VisualElement { name = "ex.ring", pickingMode = PickingMode.Ignore };
            ring.AddToClassList("ex-ring");
            tag.Root.Add(ring);

            _unitLayer.Add(tag.Root);
            return tag;
        }

        /// <summary>Theirs only: a filled kind glyph per revealed boon, a dashed "?" per unrevealed one, in grant order.</summary>
        private void SyncMarks(TagView tag, HudUnit unit)
        {
            _signature.Length = 0;
            for (int i = 0; i < unit.BoonMarks.Count; i++) _signature.Append(unit.BoonMarks[i].Kind ?? "?").Append('|');
            string signature = _signature.ToString();
            if (signature == tag.MarkSignature) return;
            tag.MarkSignature = signature;

            tag.Marks.Clear();
            for (int i = 0; i < unit.BoonMarks.Count; i++)
            {
                string kind = unit.BoonMarks[i].Kind;
                var mark = new Glyph(kind != null ? HoverContents.KindShape(kind) : GlyphPaths.Unknown) { Filled = kind != null };
                mark.AddToClassList("hud-tag__mark");
                if (kind == null) mark.AddToClassList("hud-tag__mark--unknown");
                tag.Marks.Add(mark);
            }
            SetDisplay(tag.Marks, unit.BoonMarks.Count > 0);
        }

        private void UpdateTagPositions()
        {
            Camera camera = _source.WorldCamera;
            if (camera == null || _root.panel == null) return;
            IReadOnlyList<HudUnit> units = _source.Units;
            for (int i = 0; i < units.Count; i++)
            {
                HudUnit unit = units[i];
                TagView tag;
                if (!_tags.TryGetValue(unit.Id, out tag) || unit.Anchor == null) continue;
                Vector2 panel = RuntimePanelUtils.CameraTransformWorldToPanel(_root.panel, unit.Anchor.position + unit.AnchorOffset, camera);
                tag.Root.style.left = panel.x;
                tag.Root.style.top = panel.y;
            }
        }

        // ---- the attack preview (hud.md §5) ----------------------------------------------------------

        /// <summary>The one hover panel over the target's tag: the head, the total, the rules' lines, one "?" row; a refused shot leads with why.</summary>
        private void RefreshPreview()
        {
            HudPreview preview = _source.Preview;
            if (preview == null)
            {
                _shownPreview = null;
                if (_hover != null) _hover.Hide(_previewHover);
                return;
            }
            TagView tag;
            if (_hover == null || !_tags.TryGetValue(preview.TargetUnitId, out tag)) return;
            // A tile's own panel wins while the pointer is on the bar.
            if (_hover.Owner == _tileHover) return;
            if (preview == _shownPreview && _hover.Owner == _previewHover) return;

            bool blocked = !string.IsNullOrEmpty(preview.BlockedReason);
            string name = preview.AbilityName ?? string.Empty;
            var c = new HoverContent
            {
                Letter = name.Length > 0 ? name.Substring(0, 1) : "?",
                Name = name,
                Type = "on " + (preview.TargetName ?? "them") + " · " + (preview.TrajectoryWord ?? "straight"),
                Reason = blocked ? preview.BlockedReason : null,
                Total = preview.Total.ToString(),
                TotalInexact = !preview.IsExact,
                TotalMuted = blocked,
            };
            for (int i = 0; i < preview.Lines.Count; i++)
            {
                HudPreviewLine line = preview.Lines[i];
                c.Rows.Add(new HoverRow
                {
                    Label = line.Label,
                    Amount = line.Unknown ? "?" : i == 0 ? line.Amount.ToString() : HoverContents.Signed(line.Amount),
                    Unknown = line.Unknown,
                });
            }
            _hover.Show(_previewHover, c, true, HoverPlacement.Over, tag.Root.worldBound, _root.worldBound);
            _shownPreview = preview;
        }

        private void UpdatePreviewPosition()
        {
            HudPreview preview = _source.Preview;
            if (_hover == null) return;
            if (preview == null)
            {
                if (_hover.Owner == _previewHover) _hover.Hide(_previewHover);
                return;
            }
            // The tile's panel went away while the target is still under the pointer: the preview comes back.
            if (_hover.Owner == null) RefreshPreview();
            TagView tag;
            if (_hover.Owner == _previewHover && _tags.TryGetValue(preview.TargetUnitId, out tag))
                _hover.Move(_previewHover, tag.Root.worldBound, _root.worldBound);
        }

        // ---- flyovers and the cursor tag (hud.md §3.6) --------------------------------------------------

        private void UpdateCursor()
        {
            string text = _source.CursorTag;
            bool visible = !string.IsNullOrEmpty(text);
            _cursor.EnableInClassList("hud-cursor--visible", visible);
            if (!visible || _root.panel == null) return;
            string upper = text.ToUpperInvariant();
            if (_cursor.text != upper) _cursor.text = upper;
            // Screen pixels are bottom-left origin and the panel is top-left, so the y flips through the helper.
            Vector2 position = RuntimePanelUtils.ScreenToPanel(_root.panel, _source.CursorScreenPosition);
            _cursor.style.left = position.x + 16f;
            _cursor.style.top = position.y + 16f;
        }

        private void SpawnFlyover(HudFlyover flyover)
        {
            if (!_bound || flyover == null) return;
            var root = new VisualElement { name = "hud.fly", pickingMode = PickingMode.Ignore, usageHints = UsageHints.DynamicTransform };
            root.AddToClassList("hud-fly");
            if (string.IsNullOrEmpty(flyover.Glyph))
            {
                // Damage: the number, in amber, and when a hidden line changed it, the line that did.
                root.Add(Text(flyover.Headline, "hud-fly__amount"));
                if (!string.IsNullOrEmpty(flyover.Detail)) root.Add(Text(flyover.Detail.ToUpperInvariant(), "hud-fly__detail"));
            }
            else
            {
                // A reveal: their kind glyph or emblem, the name, the lineage and kind.
                var head = new VisualElement { pickingMode = PickingMode.Ignore };
                head.AddToClassList("hud-fly__head");
                var glyph = new Glyph(GlyphPaths.Has(flyover.Glyph) ? flyover.Glyph : HoverContents.KindShape(flyover.Glyph)) { Filled = true };
                glyph.AddToClassList("hud-fly__glyph");
                head.Add(glyph);
                head.Add(Text((flyover.Headline ?? string.Empty).ToUpperInvariant(), "hud-fly__headline"));
                root.Add(head);
                if (!string.IsNullOrEmpty(flyover.Detail)) root.Add(Text(flyover.Detail.ToUpperInvariant(), "hud-fly__detail"));
            }
            _flyLayer.Add(root);
            _flyovers.Add(new FlyoverInstance { Root = root, WorldPosition = flyover.WorldPosition, StartTime = Time.time });
        }

        /// <summary>Scale-pop on spawn, drift upwards, fade out; a match has a handful of them, so nothing is pooled.</summary>
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

        // ---- the draft (between-rounds.md §2) --------------------------------------------------------

        /// <summary>The cards are built once per offer set and then only restyled, so a hover never rebuilds them under the pointer.</summary>
        private void RefreshDraft()
        {
            HudDraft draft = _source.Draft;
            bool open = draft != null;
            if (open != _drRoot.ClassListContains("dr-root--open"))
            {
                _drRoot.EnableInClassList("dr-root--open", open);
                if (open) _drRoot.schedule.Execute(() => _drRoot.AddToClassList("dr-root--shown")).StartingIn(16);
                else _drRoot.RemoveFromClassList("dr-root--shown");
            }
            if (!open)
            {
                if (_draftSignature != null) ClearCards();
                return;
            }

            _drHeadline.text = (draft.Headline ?? string.Empty).ToUpperInvariant();
            _drNext.text = (draft.NextRoundLine ?? string.Empty).ToUpperInvariant();

            _signature.Length = 0;
            for (int i = 0; i < draft.Cards.Count; i++) _signature.Append(draft.Cards[i].Id).Append('|');
            string signature = _signature.ToString();
            if (signature != _draftSignature)
            {
                BuildCards(draft);
                _draftSignature = signature;
            }

            for (int i = 0; i < _cards.Count; i++)
            {
                bool selected = i == draft.Selected;
                _cards[i].EnableInClassList("dr-card--selected", selected);
                _cards[i].EnableInClassList("dr-card--dim", draft.Picked && !selected);
                // None clickable after the pick — without disabling them, whose theme style would fade the kept card too.
                _cards[i].pickingMode = draft.Picked ? PickingMode.Ignore : PickingMode.Position;
                _cards[i].focusable = !draft.Picked;
                _cardKinds[i].Filled = selected;
            }

            _drConfirm.text = draft.Picked ? "KEPT" : "CONFIRM";
            _drConfirm.SetEnabled(!draft.Picked && draft.Selected >= 0);
            bool status = draft.Picked && !string.IsNullOrEmpty(draft.Status);
            _drStatus.text = status ? draft.Status : string.Empty;
            _drStatus.EnableInClassList("dr-status--visible", status);
        }

        private void BuildCards(HudDraft draft)
        {
            ClearCards();
            for (int i = 0; i < draft.Cards.Count; i++)
            {
                HudDraftCard card = draft.Cards[i];
                int index = i;
                string id = "dr.card[" + i + "]";

                var root = new Button { name = id, text = string.Empty };
                root.AddToClassList("dr-card");
                root.clicked += () => _source.SelectDraftCard(index);

                var shadow = new VisualElement { pickingMode = PickingMode.Ignore };
                shadow.AddToClassList("dr-card__shadow");
                root.Add(shadow);

                var face = new VisualElement { pickingMode = PickingMode.Ignore };
                face.AddToClassList("dr-card__face");
                root.Add(face);

                // The top 130: the lineage's painting, fading into the card's ink-2.
                var wash = new VisualElement { name = id + ".wash", pickingMode = PickingMode.Ignore };
                wash.AddToClassList("dr-card__wash");
                wash.style.backgroundImage = new StyleBackground(CardPainting(card));
                var kind = new VisualElement { name = id + ".kind", pickingMode = PickingMode.Ignore };
                kind.AddToClassList("dr-card__kind");
                var kindGlyph = new Glyph(HoverContents.KindShape(card.Kind));
                kindGlyph.AddToClassList("dr-card__kind-glyph");
                kind.Add(kindGlyph);
                kind.Add(Text((card.Kind ?? string.Empty).ToUpperInvariant(), "dr-card__kind-text"));
                wash.Add(kind);
                face.Add(wash);

                var body = new VisualElement { pickingMode = PickingMode.Ignore };
                body.AddToClassList("dr-card__body");
                body.Add(Named(Text((card.Name ?? string.Empty).ToUpperInvariant(), "dr-card__name"), id + ".name"));
                string god = card.God + (string.IsNullOrEmpty(card.Lineage) ? string.Empty : " · " + card.Lineage);
                body.Add(Named(Text((god ?? string.Empty).ToUpperInvariant(), "dr-card__god"), id + ".god"));
                body.Add(Named(Text(card.Effect, "dr-card__text"), id + ".text"));
                face.Add(body);

                // At the foot: where it lands, with the slot's glyph (the Blessing circle when it lands on you).
                var on = new VisualElement { name = id + ".on", pickingMode = PickingMode.Ignore };
                on.AddToClassList("dr-card__on");
                var slot = new Glyph(card.Slot != null ? HoverContents.SlotShape(card.Slot) : "blessing");
                slot.AddToClassList("dr-card__on-glyph");
                on.Add(slot);
                on.Add(Text((card.Attach ?? string.Empty).ToUpperInvariant(), "dr-card__on-text"));
                face.Add(on);

                _drCards.Add(root);
                _cards.Add(root);
                _cardKinds.Add(kindGlyph);
            }
        }

        private static Texture2D CardPainting(HudDraftCard card)
        {
            Color dark, light;
            bool known = ColorUtility.TryParseHtmlString(card.HueDark ?? string.Empty, out dark)
                & ColorUtility.TryParseHtmlString(card.HueLight ?? string.Empty, out light);
            if (!known)
            {
                dark = Color.Lerp(InkTwo, Bone, 0.05f);
                light = Color.Lerp(InkTwo, Bone, 0.10f);
            }
            return Ramps.Painting(dark, light, InkTwo);
        }

        private void ClearCards()
        {
            for (int i = 0; i < _cards.Count; i++) _cards[i].RemoveFromHierarchy();
            _cards.Clear();
            _cardKinds.Clear();
            _draftSignature = null;
        }

        /// <summary>The draft's one deadline, as the rope: the line burns from the right with the ember at its end.</summary>
        private void UpdateDraftTimer()
        {
            HudDraft draft = _source != null ? _source.Draft : null;
            if (draft == null || _drTimerFill == null) return;
            float total = draft.SecondsTotal > 0f ? draft.SecondsTotal : 1f;
            float fraction = Mathf.Clamp01(draft.SecondsRemaining / total);
            _drTimerFill.style.width = Length.Percent(fraction * 100f);
            _drTimerEmber.style.left = Length.Percent(fraction * 100f);
        }

        private void HandleConfirmClicked() => _source.ConfirmDraft();

        // ---- the round moments (between-rounds.md §3) ----------------------------------------------

        /// <summary>One band, four kinds, coloured by who won or moves first — never by the words.</summary>
        private void RefreshMoment()
        {
            HudMoment m = _source.Moment;
            _moRoot.EnableInClassList("mo-root--visible", m != null);
            if (m == null) return;

            const string you = "mo--you", them = "mo--them";
            switch (m.Kind)
            {
                case HudMomentKind.RoundStart:
                    Show(_moKicker, "ROUND " + m.Round, null);
                    _moKicker.RemoveFromClassList("mo-kicker--result");
                    Show(_moTitle, (m.MapName ?? string.Empty).ToUpperInvariant(), null);
                    SetDisplay(_moScore, false);
                    Show(_moSub, m.IMoveFirst ? "YOU MOVE FIRST" : "THEY MOVE FIRST", m.IMoveFirst ? you : them);
                    _moSub.RemoveFromClassList("mo-sub--body");
                    break;

                case HudMomentKind.RoundResult:
                    Show(_moKicker, "ROUND " + m.Round + " TO " + (m.WinnerName ?? string.Empty).ToUpperInvariant(), m.IWon ? you : them);
                    _moKicker.AddToClassList("mo-kicker--result");
                    SetDisplay(_moTitle, false);
                    SetDisplay(_moScore, true);
                    _moMine.text = m.ScoreMine.ToString();
                    _moTheirs.text = m.ScoreTheirs.ToString();
                    Show(_moSub, m.Reason, null);
                    _moSub.AddToClassList("mo-sub--body");
                    break;

                case HudMomentKind.SeriesResult:
                    SetDisplay(_moKicker, false);
                    Show(_moTitle, m.IWon ? "VICTORY" : "DEFEAT", m.IWon ? you : them);
                    SetDisplay(_moScore, false);
                    Show(_moSub, ("series " + m.ScoreMine + " – " + m.ScoreTheirs + " · " + m.Reason).ToUpperInvariant(), null);
                    _moSub.RemoveFromClassList("mo-sub--body");
                    break;

                default:
                    SetDisplay(_moKicker, false);
                    Show(_moTitle, "MATCH LOST", null);
                    SetDisplay(_moScore, false);
                    Show(_moSub, m.Reason, null);
                    _moSub.AddToClassList("mo-sub--body");
                    break;
            }
            SetDisplay(_moBack, m.ShowBack);
            if (m.ShowBack) _moBack.text = (m.BackLabel ?? "Back to lobby").ToUpperInvariant();
        }

        private static void Show(Label label, string text, string colourClass)
        {
            label.text = text ?? string.Empty;
            label.EnableInClassList("mo--you", colourClass == "mo--you");
            label.EnableInClassList("mo--them", colourClass == "mo--them");
            SetDisplay(label, !string.IsNullOrEmpty(text));
        }

        private void HandleBackClicked() => _source.BackToLobby();

        // ---- helpers --------------------------------------------------------------------------------

        private static string Trim(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            if (name.Length > MaxNameLength) name = name.Substring(0, MaxNameLength - 1) + "…";
            return name.ToUpperInvariant();
        }

        private static void SetDisplay(VisualElement element, bool visible)
        {
            var want = new StyleEnum<DisplayStyle>(visible ? DisplayStyle.Flex : DisplayStyle.None);
            if (element.style.display != want) element.style.display = want;
        }

        private static Label Text(string text, string cls)
        {
            var label = new Label(text ?? string.Empty) { pickingMode = PickingMode.Ignore };
            label.AddToClassList(cls);
            return label;
        }

        private static T Named<T>(T element, string name) where T : VisualElement
        {
            element.name = name;
            return element;
        }

        private static T Find<T>(VisualElement root, string name, List<string> missing) where T : VisualElement
        {
            T found = root.Q<T>(name);
            if (found == null) missing.Add(name);
            return found;
        }

        /// <summary>True when a pickable HUD element sits under the given screen position (bottom-left origin).</summary>
        private bool IsPointerOverHud(Vector2 screenPosition)
        {
            if (!_bound || _root == null) return false;
            IPanel panel = _root.panel;
            if (panel == null) return false;
            return panel.Pick(RuntimePanelUtils.ScreenToPanel(panel, screenPosition)) != null;
        }
    }
}
