using System;
using System.Collections.Generic;
using UnityEngine;
using Mimas.Client.Content;
using Mimas.Core.Bots;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Units;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// A match played locally against a bot: the one place on the client that owns a <see cref="MatchState"/>.
    /// It is deliberately shaped like the networked session that replaces it in M2: commands go in through
    /// <see cref="Submit"/>, events come back out, get filtered for the local player (<see cref="EventFilter"/>)
    /// and are played one at a time onto the board (moves animate, hits pause), and everything the HUD
    /// reads comes from the <see cref="PlayerView"/> projection or from the events, never from the state
    /// directly. So the HUD and the board interaction already behave as if the truth lived elsewhere.
    ///
    /// Board interaction: arm an action from the bar; a movement paints reachable tiles and previews paths,
    /// an attack paints legal targets and previews damage; a click submits the command. After an action the
    /// same ability stays armed while it is still affordable (three steps are three clicks, not six).
    /// Nothing is armed at the start of a turn (ADR-015); clicking a unit with nothing armed examines it.
    /// The clock is the local fake from ADR-015 (turn cap, rope, idle penalty); a timeout becomes an
    /// <see cref="EndTurnCommand"/> with reason <see cref="EndTurnReason.Timeout"/>, exactly as the server will do.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalMatchSession : MonoBehaviour, IMatchHudSource
    {
        [Header("Match setup")]
        [Tooltip("Map, classes, clock, bot and passive knobs. Assets/_Game/Settings/DefaultMatchSettings.asset.")]
        [SerializeField] private MatchSettings _settings;

        [Header("Scene references")]
        [SerializeField] private ContentBootstrap _content;
        [SerializeField] private BoardView _board;
        [SerializeField] private BoardInputController _input;

        [Tooltip("View for the local player's unit (player 0).")]
        [SerializeField] private UnitView _unit;

        [Tooltip("View for the bot's unit (player 1).")]
        [SerializeField] private UnitView _opponent;

        [Header("Playback")]
        [SerializeField] private MovePlaybackSettings _playback = MovePlaybackSettings.Default;

        [Tooltip("Height above a unit's feet where its hp bar hangs.")]
        [SerializeField] private float _overlayHeight = 1.35f;

        private const int LocalPlayer = 0;
        private const int BotPlayer = 1;
        private const int None = -1;

        // Rules.
        private ContentCatalog _catalog;
        private MatchState _state;
        private RandomBot _bot;
        private PlayerView _view;
        private int _localUnitId = None;

        // Views.
        private readonly Dictionary<int, UnitView> _unitViews = new Dictionary<int, UnitView>();
        private readonly List<HudUnit> _hudUnits = new List<HudUnit>();
        private readonly Dictionary<int, HudUnit> _hudUnitsById = new Dictionary<int, HudUnit>();

        // Event playback.
        private readonly Queue<MatchEvent> _pending = new Queue<MatchEvent>();
        private readonly List<MatchEvent> _scratchEvents = new List<MatchEvent>();
        private readonly HashSet<string> _revealedThisBatch = new HashSet<string>(StringComparer.Ordinal);
        private int _movingUnitId = None;
        private MovePlan _movingPlan;
        private float _pauseUntil;
        private bool _wasPlaying;

        // HUD state.
        private readonly List<HudAction> _actions = new List<HudAction>();
        private readonly List<AbilityDef> _abilities = new List<AbilityDef>();
        private readonly List<Hex> _highlight = new List<Hex>();
        private readonly List<Unit> _targetScratch = new List<Unit>();
        private int _armed = None;
        private MovementOptions _moveOptions = MovementOptions.Empty;
        private HudExamine _examine;
        private int _examinedUnitId = None;
        private HudPreview _preview;
        private HudUnit _previewUnit;
        private string _banner;
        private bool _ready;

        // Clock (local fake, see ADR-015).
        private float _turnSeconds = 30f;
        private float _ropeSeconds = 10f;
        private float _idleTurnSeconds = 7f;
        private float _turnTotal;
        private float _turnRemaining;
        private bool _penaltyPending;
        private bool _penalised;
        private bool _clockRunning;
        private int _localTurnNumber;
        private float _botTimer;

        // ---- IMatchHudSource -------------------------------------------------------------------------

        public IReadOnlyList<HudAction> Actions => _actions;
        public int ActiveActionIndex => _armed;
        public bool IsMyTurn => _ready && _view != null && _view.IsMyTurn;
        public bool CanEndTurn => CanAct;
        public int TurnNumber => _localTurnNumber;
        public int ApCurrent => LocalUnitView != null ? LocalUnitView.Ap : 0;
        public int ApPerTurn => LocalUnitView != null ? LocalUnitView.ApPerTurn : 0;
        public float TurnSecondsRemaining => _turnRemaining > 0f ? _turnRemaining : 0f;
        public float TurnSecondsTotal => _turnTotal;
        public float RopeSeconds => _ropeSeconds;
        public HudExamine Examine => _examine;
        public IReadOnlyList<HudUnit> Units => _hudUnits;
        public HudPreview Preview => _preview;
        public string Banner => _banner;
        public Camera WorldCamera => _input != null ? _input.ActiveCamera : Camera.main;

        public event Action StateChanged;
        public event Action<HudFlyover> Flyover;

        /// <summary>The rules state. Exposed for tests and tooling; presentation code must go through the view and events.</summary>
        public MatchState State => _state;

        private Mimas.Core.Match.UnitView LocalUnitView => _view != null && _localUnitId != None ? _view.FindUnit(_localUnitId) : null;

        private bool IsPlaying => _movingUnitId != None || Time.time < _pauseUntil || _pending.Count > 0;

        /// <summary>The local player may arm an action or pick a target right now.</summary>
        private bool CanAct => _ready && !_state.IsOver && _state.ActivePlayer == LocalPlayer && !IsPlaying;

        public void SelectAction(int index)
        {
            if (!CanAct) return;
            if (index < 0 || index >= _abilities.Count || index == _armed || !_actions[index].Enabled)
            {
                Disarm();
                RaiseStateChanged();
                return;
            }

            _armed = index;
            _examinedUnitId = None;                      // arming replaces examine; the board is now targeting
            PaintOptions();
            RaiseStateChanged();
        }

        public void EndTurn()
        {
            if (!CanEndTurn) return;
            Submit(new EndTurnCommand(LocalPlayer));
        }

        public void CloseExamine()
        {
            if (_examinedUnitId == None) return;
            _examinedUnitId = None;
            RaiseStateChanged();
        }

        // ---- lifecycle ------------------------------------------------------------------------------

        private void Awake()
        {
            if (_content == null || _board == null || _input == null || _unit == null || _opponent == null)
            {
                Debug.LogError("[LocalMatchSession] _content, _board, _input, _unit and _opponent must all be assigned.", this);
                enabled = false;
                return;
            }

            if (_settings == null)
            {
                Debug.LogWarning("[LocalMatchSession] No MatchSettings assigned; using built-in defaults.", this);
                _settings = ScriptableObject.CreateInstance<MatchSettings>();
            }
        }

        private void OnEnable()
        {
            if (_board == null || _input == null) return;
            _board.BoardBuilt += HandleBoardBuilt;
            _input.TileClicked += HandleTileClicked;
            _input.TileHovered += HandleTileHovered;
            _input.RightClicked += HandleRightClicked;
            if (_unit != null) _unit.Mover.Arrived += HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived += HandleArrived;
        }

        private void OnDisable()
        {
            if (_board == null || _input == null) return;
            _board.BoardBuilt -= HandleBoardBuilt;
            _input.TileClicked -= HandleTileClicked;
            _input.TileHovered -= HandleTileHovered;
            _input.RightClicked -= HandleRightClicked;
            if (_unit != null) _unit.Mover.Arrived -= HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived -= HandleArrived;
        }

        private void OnDestroy()
        {
            StateChanged = null;
            Flyover = null;
        }

        private void Start()
        {
            // BoardView builds in Awake, so the event may already have fired before we subscribed.
            if (_board.IsBuilt) HandleBoardBuilt();
        }

        private void Update()
        {
            if (!_ready) return;

            PlayPendingEvents();

            // Playback just finished (a move landed, a hit pause elapsed): the board is interactive again.
            bool playing = IsPlaying;
            if (_wasPlaying && !playing)
            {
                RefreshView();
                RearmIfPossible();
                RaiseStateChanged();
            }
            _wasPlaying = playing;

            if (_state.IsOver) return;

            if (_clockRunning) TickClock();

            if (_state.ActivePlayer == BotPlayer && !IsPlaying)
            {
                _botTimer -= Time.deltaTime;
                if (_botTimer <= 0f)
                {
                    _botTimer = _settings.OpponentThinkSeconds;
                    Command choice = _bot.Choose(_state, BotPlayer);
                    if (choice != null) Submit(choice);
                }
            }
        }

        // ---- setup ----------------------------------------------------------------------------------

        private void HandleBoardBuilt()
        {
            if (_ready) return;
            if (_board.MapData == null)
            {
                Debug.LogError("[LocalMatchSession] Board reported built but has no map data.", this);
                return;
            }

            _catalog = _content.EnsureLoaded();
            if (_catalog == null) return;

            MatchSetup setup;
            try
            {
                setup = new MatchSetup(_board.MapData.Id, _settings.PlayerClassId, _settings.OpponentClassId, _settings.FirstPlayer);
                foreach (string id in _settings.PlayerModifierIds) if (!string.IsNullOrEmpty(id)) setup.WithModifier(LocalPlayer, id);
                foreach (string id in _settings.OpponentModifierIds) if (!string.IsNullOrEmpty(id)) setup.WithModifier(BotPlayer, id);
                _state = new MatchState(_catalog, setup, _settings.Seed);
            }
            catch (Exception e) when (e is ArgumentException || e is KeyNotFoundException)
            {
                Debug.LogError("[LocalMatchSession] Match setup refused: " + e.Message, this);
                return;
            }

            _bot = new RandomBot(_settings.Seed ^ 0x9E3779B9u);

            _unitViews.Clear();
            _hudUnits.Clear();
            _hudUnitsById.Clear();
            for (int i = 0; i < _state.Units.All.Count; i++)
            {
                Unit unit = _state.Units.All[i];
                UnitView view = unit.Owner == LocalPlayer ? _unit : _opponent;
                _unitViews[unit.Id] = view;
                view.gameObject.SetActive(true);
                view.SnapTo(unit.Position, _board);
                if (unit.Owner == LocalPlayer) _localUnitId = unit.Id;

                var hud = new HudUnit
                {
                    Id = unit.Id,
                    IsMine = unit.Owner == LocalPlayer,
                    Hp = unit.Hp,
                    MaxHp = unit.MaxHp,
                    Ap = unit.Ap,
                    ApPerTurn = unit.ApPerTurn,
                    Anchor = view.transform,
                    AnchorOffset = new Vector3(0f, _overlayHeight, 0f),
                };
                _hudUnits.Add(hud);
                _hudUnitsById[unit.Id] = hud;
            }

            _turnSeconds = _settings.ResolveTurnSeconds(_catalog);
            _ropeSeconds = Mathf.Min(_settings.RopeSeconds, _turnSeconds);
            _idleTurnSeconds = _settings.IdleTurnSeconds;

            _ready = true;
            RefreshView();
            CollectAbilities();
            RefreshMarkers();

            Debug.Log("[LocalMatchSession] " + _settings.PlayerClassId + " vs " + _settings.OpponentClassId + " on " + _board.MapData.Id
                + ", seed " + _settings.Seed + ", " + _turnSeconds + " s per turn.");

            Enqueue(_state.Start());
            RaiseStateChanged();
        }

        private void CollectAbilities()
        {
            _abilities.Clear();
            Unit unit;
            if (_localUnitId == None || !_state.Units.TryGet(_localUnitId, out unit)) return;
            for (int i = 0; i < unit.AbilityIds.Count; i++)
            {
                AbilityDef def;
                if (_catalog.Abilities.TryGet(unit.AbilityIds[i], out def)) _abilities.Add(def);
            }
        }

        // ---- commands in, events out ----------------------------------------------------------------

        /// <summary>Validates and applies a command, then queues its events (filtered for the local player) for playback.</summary>
        public bool Submit(Command command)
        {
            if (!_ready) return false;
            CommandResult check = _state.Validate(command);
            if (!check.Ok)
            {
                Debug.Log("[LocalMatchSession] " + command + " refused: " + check);
                return false;
            }

            IReadOnlyList<MatchEvent> events = _state.Apply(command);
            Enqueue(events);
            return true;
        }

        private void Enqueue(IReadOnlyList<MatchEvent> events)
        {
            _scratchEvents.Clear();
            EventFilter.ForPlayer(events, LocalPlayer, _state, _scratchEvents);
            for (int i = 0; i < _scratchEvents.Count; i++) _pending.Enqueue(_scratchEvents[i]);
            _scratchEvents.Clear();
        }

        private void PlayPendingEvents()
        {
            while (_pending.Count > 0)
            {
                if (_movingUnitId != None || Time.time < _pauseUntil) return;
                Play(_pending.Dequeue());
            }
        }

        private void Play(MatchEvent e)
        {
            switch (e)
            {
                case TurnStartedEvent started:
                    PlayTurnStarted(started);
                    break;

                case UnitMovedEvent moved:
                    PlayMove(moved);
                    break;

                case ApSpentEvent ap:
                {
                    HudUnit hud;
                    if (_hudUnitsById.TryGetValue(ap.UnitId, out hud)) hud.Ap = ap.Remaining;
                    RefreshView();
                    RaiseStateChanged();
                    break;
                }

                case AbilityRevealedEvent _:
                    RefreshView();
                    RaiseStateChanged();
                    break;

                case ModifierRevealedEvent revealed:
                    _revealedThisBatch.Add(revealed.ModifierId);
                    RefreshView();
                    RefreshMarkers();
                    RaiseStateChanged();
                    break;

                case AttackResolvedEvent attack:
                    PlayAttack(attack);
                    break;

                case UnitDiedEvent died:
                {
                    HudUnit hud;
                    if (_hudUnitsById.TryGetValue(died.UnitId, out hud)) hud.IsAlive = false;
                    UnitView view;
                    if (_unitViews.TryGetValue(died.UnitId, out view)) view.gameObject.SetActive(false);
                    RefreshView();
                    RaiseStateChanged();
                    break;
                }

                case TurnEndedEvent ended:
                    _clockRunning = false;
                    if (ended.Player == LocalPlayer && !ended.Acted && ended.Reason == EndTurnReason.Timeout)
                        _penaltyPending = _idleTurnSeconds > 0f;
                    Disarm();
                    _examinedUnitId = None;
                    RefreshView();
                    RaiseStateChanged();
                    break;

                case MatchEndedEvent over:
                    _clockRunning = false;
                    _banner = over.Winner == LocalPlayer ? "VICTORY" : "DEFEAT";
                    Disarm();
                    RefreshView();
                    RaiseStateChanged();
                    Debug.Log("[LocalMatchSession] Match over: player " + over.Winner + " wins by " + over.Reason + ".");
                    break;
            }
        }

        private void PlayTurnStarted(TurnStartedEvent started)
        {
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                Unit unit = _state.Units.Get(hud.Id);
                if (unit.Owner == started.Player) hud.Ap = unit.IsAlive ? unit.ApPerTurn : 0;
            }

            if (started.Player == LocalPlayer)
            {
                _localTurnNumber++;
                _penalised = _penaltyPending;
                _penaltyPending = false;
                _turnTotal = _penalised ? _idleTurnSeconds : _turnSeconds;
                if (_penalised) Debug.Log("[LocalMatchSession] Idle penalty: turn " + _localTurnNumber + " starts with " + _idleTurnSeconds + " s until you act.");
            }
            else
            {
                _penalised = false;
                _turnTotal = _turnSeconds;
                _botTimer = _settings.OpponentThinkSeconds;
            }
            _turnRemaining = _turnTotal;
            _clockRunning = true;

            Disarm();
            RefreshView();
            RaiseStateChanged();
        }

        private void PlayMove(UnitMovedEvent moved)
        {
            UnitView view;
            if (!_unitViews.TryGetValue(moved.UnitId, out view))
            {
                RefreshView();
                return;
            }
            _movingUnitId = moved.UnitId;
            _movingPlan = moved.Plan;
            ClearPreview();
            _board.ClearHighlights();
            MovePlanPlayback.Play(view.Mover, _board, moved.Plan, _playback);
            RaiseStateChanged();
        }

        private void HandleArrived()
        {
            if (_movingUnitId == None) return;
            UnitView view;
            if (_unitViews.TryGetValue(_movingUnitId, out view)) view.SnapTo(_movingPlan.Destination, _board);
            _movingUnitId = None;
            _movingPlan = null;
        }

        private void PlayAttack(AttackResolvedEvent attack)
        {
            HudUnit target;
            if (_hudUnitsById.TryGetValue(attack.TargetId, out target))
            {
                target.Hp = attack.TargetHpAfter;
                target.GhostDamage = 0;
            }

            string detail = DescribeSurprise(attack);
            _revealedThisBatch.Clear();

            UnitView view;
            if (_unitViews.TryGetValue(attack.TargetId, out view))
            {
                var handler = Flyover;
                if (handler != null)
                {
                    handler(new HudFlyover
                    {
                        UnitId = attack.TargetId,
                        WorldPosition = view.transform.position + new Vector3(0f, _overlayHeight, 0f),
                        Headline = "-" + attack.Damage,
                        Detail = detail,
                    });
                }
            }

            _pauseUntil = Time.time + _settings.HitPauseSeconds;
            RefreshView();
            RaiseStateChanged();
        }

        /// <summary>"BLOCKED 4 · WARD OF FEATHERS REVEALED" when a hidden line changed the number, else null.</summary>
        private string DescribeSurprise(AttackResolvedEvent attack)
        {
            DamageBreakdown breakdown = attack.Breakdown;
            int surprise = 0;
            string name = null;
            for (int i = 0; i < breakdown.Lines.Count; i++)
            {
                DamageLine line = breakdown.Lines[i];
                if (!line.Hidden || !_revealedThisBatch.Contains(line.Id)) continue;
                surprise += line.Amount;
                ModifierDef def;
                string label = _catalog.Modifiers.TryGet(line.Id, out def) ? def.Name : line.Id;
                name = name == null ? label : name + ", " + label;
            }
            if (name == null) return null;
            string verb = surprise < 0 ? "BLOCKED " + (-surprise) : "EXTRA " + surprise;
            return verb + "  ·  " + name.ToUpperInvariant() + " REVEALED";
        }

        // ---- projection -> HUD ----------------------------------------------------------------------

        private void RefreshView()
        {
            _view = _state.ViewFor(LocalPlayer);
            RebuildActions();
            RefreshExamine();
            RefreshEmphasis();
        }

        private void RefreshMarkers()
        {
            if (_view == null) return;
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                hud.Markers.Clear();
                Mimas.Core.Match.UnitView unit = _view.FindUnit(hud.Id);
                if (unit == null) continue;
                for (int m = 0; m < unit.Modifiers.Count; m++)
                {
                    KnownEntry entry = unit.Modifiers[m];
                    if (!entry.Revealed) continue;
                    ModifierDef def;
                    if (!_catalog.Modifiers.TryGet(entry.Id, out def)) continue;
                    hud.Markers.Add(new HudMarker { Id = def.Id, Name = def.Name, Icon = def.Icon });
                }
            }
        }

        private void RebuildActions()
        {
            _actions.Clear();
            Mimas.Core.Match.UnitView me = LocalUnitView;
            bool canAct = CanAct;
            for (int i = 0; i < _abilities.Count; i++)
            {
                AbilityDef def = _abilities[i];
                bool affordable = me != null && me.Ap >= def.Cost;
                _actions.Add(new HudAction(def.Id, def.Name, def.Description, def.Icon, def.Category, def.Cost, affordable, canAct && affordable));
            }
        }

        private void RefreshEmphasis()
        {
            bool armed = _armed != None;
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                hud.Emphasised = armed || hud == _previewUnit || hud.Id == _examinedUnitId;
            }
        }

        private void RefreshExamine()
        {
            Mimas.Core.Match.UnitView unit = _examinedUnitId == None || _view == null ? null : _view.FindUnit(_examinedUnitId);
            if (unit == null)
            {
                _examine = null;
                return;
            }

            ClassDef cls;
            _catalog.Classes.TryGet(unit.ClassId, out cls);
            var examine = new HudExamine
            {
                Title = cls != null ? cls.Name : unit.ClassId,
                Subtitle = unit.IsMine ? "Your unit" : "Opponent",
                Description = cls != null ? cls.Description : null,
                Hp = unit.Hp,
                MaxHp = unit.MaxHp,
                Ap = unit.Ap,
                ApPerTurn = unit.ApPerTurn,
            };

            for (int i = 0; i < unit.Abilities.Count; i++)
            {
                KnownEntry entry = unit.Abilities[i];
                AbilityDef def = null;
                bool known = entry.Revealed && _catalog.Abilities.TryGet(entry.Id, out def);
                examine.Abilities.Add(new HudExamineEntry
                {
                    Name = entry.Revealed ? (known ? def.Name : entry.Id) : "Unknown ability",
                    Description = entry.Revealed ? (known ? DescribeAbility(def) : null) : "Revealed once the opponent uses it.",
                    Icon = known ? def.Icon : null,
                    Hidden = !entry.Revealed,
                });
            }

            for (int i = 0; i < unit.Modifiers.Count; i++)
            {
                KnownEntry entry = unit.Modifiers[i];
                ModifierDef def = null;
                bool known = entry.Revealed && _catalog.Modifiers.TryGet(entry.Id, out def);
                examine.Modifiers.Add(new HudExamineEntry
                {
                    Name = entry.Revealed ? (known ? def.Name : entry.Id) : "Unknown passive",
                    Description = entry.Revealed ? (known ? def.Description : null) : "Revealed the first time it changes a result.",
                    Icon = known ? def.Icon : null,
                    Hidden = !entry.Revealed,
                });
            }

            _examine = examine;
        }

        private static string DescribeAbility(AbilityDef def)
        {
            var attack = def as AttackDef;
            string cost = def.Cost + " AP";
            if (attack != null)
            {
                string range = attack.MinRange == attack.Range ? "range " + attack.Range : "range " + attack.MinRange + "-" + attack.Range;
                return cost + " · " + attack.Damage + " " + attack.DamageType + " · " + range + (string.IsNullOrEmpty(def.Description) ? "" : "\n" + def.Description);
            }
            return cost + (string.IsNullOrEmpty(def.Description) ? "" : "\n" + def.Description);
        }

        private void RaiseStateChanged()
        {
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        // ---- board interaction ----------------------------------------------------------------------

        private AbilityDef ArmedAbility => _armed >= 0 && _armed < _abilities.Count ? _abilities[_armed] : null;

        private void PaintOptions()
        {
            AbilityDef def = ArmedAbility;
            _highlight.Clear();
            _moveOptions = MovementOptions.Empty;
            if (def == null || _localUnitId == None)
            {
                _board.ClearHighlights();
                return;
            }

            if (def is MovementDef)
            {
                _moveOptions = _state.MoveOptions(_localUnitId, def.Id);
                for (int i = 0; i < _moveOptions.Plans.Count; i++) _highlight.Add(_moveOptions.Plans[i].Destination);
                _board.HighlightReachable(_highlight);
            }
            else if (def is AttackDef)
            {
                _state.AttackTargets(_localUnitId, def.Id, _targetScratch);
                for (int i = 0; i < _targetScratch.Count; i++) _highlight.Add(_targetScratch[i].Position);
                _targetScratch.Clear();
                _board.HighlightTargets(_highlight);
            }
            RefreshEmphasis();
        }

        /// <summary>After an action, keep the same ability armed while it is still usable (three steps are three clicks).</summary>
        private void RearmIfPossible()
        {
            if (_armed == None) return;
            RebuildActions();
            if (!CanAct || _armed >= _actions.Count || !_actions[_armed].Enabled)
            {
                Disarm();
                return;
            }
            PaintOptions();
        }

        private void HandleTileClicked(TileView tile)
        {
            if (!_ready) return;

            if (tile == null)
            {
                bool changed = _armed != None || _examinedUnitId != None;
                Disarm();
                _examinedUnitId = None;
                if (changed) { RefreshView(); RaiseStateChanged(); }
                return;
            }

            Hex coord = tile.Coord;

            if (_armed != None)
            {
                if (!CanAct) return;
                AbilityDef def = ArmedAbility;
                Unit me = _state.Units.Get(_localUnitId);
                if (coord == me.Position)
                {
                    Disarm();
                    RefreshView();
                    RaiseStateChanged();
                    return;
                }

                if (def is MovementDef)
                {
                    var command = new MoveCommand(LocalPlayer, _localUnitId, def.Id, coord);
                    if (!Submit(command)) return;              // stay armed; the player just missed
                    return;
                }

                if (def is AttackDef)
                {
                    ClearPreview();
                    var command = new AttackCommand(LocalPlayer, _localUnitId, def.Id, coord);
                    if (!Submit(command)) return;
                    return;
                }
                return;
            }

            // Nothing armed: clicks on units open examine, anything else closes it.
            Unit clicked;
            _examinedUnitId = _state.Units.TryGetUnitAt(coord, out clicked) ? clicked.Id : None;
            RefreshView();
            RaiseStateChanged();
        }

        private void HandleTileHovered(TileView tile)
        {
            if (!_ready) return;
            _board.SetHovered(tile);

            AbilityDef def = ArmedAbility;
            if (def == null || IsPlaying || tile == null)
            {
                _board.ShowPathPreview(null);
                if (ClearPreview()) RaiseStateChanged();
                return;
            }

            if (def is MovementDef)
            {
                MovePlan plan;
                _board.ShowPathPreview(_moveOptions.TryGet(tile.Coord, out plan) ? plan.Path : null);
                return;
            }

            if (def is AttackDef)
            {
                DamageBreakdown breakdown = _state.PreviewAttack(LocalPlayer, _localUnitId, def.Id, tile.Coord);
                Unit target;
                if (breakdown == null || !_state.Units.TryGetUnitAt(tile.Coord, out target))
                {
                    if (ClearPreview()) RaiseStateChanged();
                    return;
                }
                ShowPreview(def, target, breakdown);
                RaiseStateChanged();
            }
        }

        private void ShowPreview(AbilityDef def, Unit target, DamageBreakdown breakdown)
        {
            var preview = new HudPreview { TargetUnitId = target.Id, AbilityName = def.Name, Total = breakdown.Total, IsExact = breakdown.IsExact };
            for (int i = 0; i < breakdown.Lines.Count; i++)
            {
                DamageLine line = breakdown.Lines[i];
                preview.Lines.Add(new HudPreviewLine { Label = DescribeLine(line), Amount = line.Amount });
            }
            if (breakdown.UnknownCount > 0)
                preview.Lines.Add(new HudPreviewLine { Label = breakdown.UnknownCount == 1 ? "Unrevealed passive" : breakdown.UnknownCount + " unrevealed passives", Unknown = true });

            _preview = preview;
            HudUnit hud;
            _previewUnit = _hudUnitsById.TryGetValue(target.Id, out hud) ? hud : null;
            if (_previewUnit != null) _previewUnit.GhostDamage = Mathf.Min(breakdown.Total, _previewUnit.Hp);
            RefreshEmphasis();
        }

        private bool ClearPreview()
        {
            if (_preview == null && _previewUnit == null) return false;
            if (_previewUnit != null) _previewUnit.GhostDamage = 0;
            _preview = null;
            _previewUnit = null;
            RefreshEmphasis();
            return true;
        }

        private string DescribeLine(DamageLine line)
        {
            switch (line.Kind)
            {
                case DamageLineKind.Base: return "Base";
                case DamageLineKind.Power: return Capitalise(StatBlock.DamageTypeOf(line.Id)) + " power";
                case DamageLineKind.Defense: return Capitalise(StatBlock.DamageTypeOf(line.Id)) + " defense";
                default:
                {
                    ModifierDef def;
                    return _catalog.Modifiers.TryGet(line.Id, out def) ? def.Name : line.Id;
                }
            }
        }

        private static string Capitalise(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private void HandleRightClicked()
        {
            if (!_ready) return;
            bool changed = _armed != None || _examinedUnitId != None;
            Disarm();
            _examinedUnitId = None;
            if (changed) { RefreshView(); RaiseStateChanged(); }
        }

        private void Disarm()
        {
            _armed = None;
            _moveOptions = MovementOptions.Empty;
            _highlight.Clear();
            ClearPreview();
            if (_board != null)
            {
                _board.ClearHighlights();
                _board.ShowPathPreview(null);
            }
            RefreshEmphasis();
        }

        // ---- clock ----------------------------------------------------------------------------------

        private void TickClock()
        {
            _turnRemaining -= Time.deltaTime;
            if (_turnRemaining > 0f) return;
            if (IsPlaying) { _turnRemaining = 0f; return; }   // let the animation finish; the turn passes right after

            int active = _state.ActivePlayer;
            _clockRunning = false;
            Debug.Log("[LocalMatchSession] Turn of player " + active + " ended by the clock" + (_state.ActedThisTurn ? "." : " before any action."));
            Submit(new EndTurnCommand(active, EndTurnReason.Timeout));
        }

        /// <summary>Acting during a penalty turn restores the full turn length (Hearthstone rule).</summary>
        private void LiftPenalty()
        {
            if (!_penalised) return;
            _penalised = false;
            _turnTotal = _turnSeconds;
            _turnRemaining = _turnSeconds;
        }

        private void LateUpdate()
        {
            if (_ready && _penalised && _state.ActedThisTurn) LiftPenalty();
        }
    }
}
