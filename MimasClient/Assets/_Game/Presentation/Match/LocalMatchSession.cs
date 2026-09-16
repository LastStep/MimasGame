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

        [Header("Aiming")]
        [Tooltip("Draws the path preview. Empty falls back to the component on the board.")]
        [SerializeField] private AimPreview _aimPreview;

        [Tooltip("Draws the range band. Empty falls back to the component on the board.")]
        [SerializeField] private RangeCircles _rangeCircles;

        [Tooltip("Flies the projectile on a resolved attack. Empty falls back to the component on the board.")]
        [SerializeField] private ProjectilePlayback _projectiles;

        [Header("Playback")]
        [SerializeField] private MovePlaybackSettings _playback = MovePlaybackSettings.Default;

        [Tooltip("Height above a unit's feet where its hp bar hangs.")]
        [SerializeField] private float _overlayHeight = 1.35f;

        [Header("Props")]
        [Tooltip("Placeholder colour for props whose id is 'wall'.")]
        [SerializeField] private Color _wallColor = new Color(0.290f, 0.290f, 0.314f, 1f);

        [Tooltip("Placeholder colour for props whose id is 'pillar'.")]
        [SerializeField] private Color _pillarColor = new Color(0.784f, 0.706f, 0.541f, 1f);

        private const int LocalPlayer = 0;
        private const int BotPlayer = 1;
        private const int None = -1;

        // Rules.
        private ContentCatalog _catalog;
        private MatchState _state;
        private RandomBot _bot;
        private PlayerView _view;
        private int _localUnitId = None;

        /// <summary>
        /// The same calculator the rules use (ADR-017: preview and actual share one function). The session owns
        /// one so it can also ask what a <em>refused</em> shot would have done — the number the tooltip shows
        /// behind a "No line of sight" line. The networked session will get that number from the server instead.
        /// </summary>
        private DamageCalculator _damage;

        // Views.
        private readonly Dictionary<int, UnitView> _unitViews = new Dictionary<int, UnitView>();
        private readonly Dictionary<int, PropView> _propViews = new Dictionary<int, PropView>();
        private readonly List<HudUnit> _hudUnits = new List<HudUnit>();
        private readonly Dictionary<int, HudUnit> _hudUnitsById = new Dictionary<int, HudUnit>();
        private Transform _propRoot;

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
        private readonly List<IBody> _targetScratch = new List<IBody>();
        private int _armed = None;
        private MovementOptions _moveOptions = MovementOptions.Empty;
        private HudExamine _examine;
        private int _examinedUnitId = None;
        private int _examinedPropId = None;
        private HudPreview _preview;
        private HudUnit _previewUnit;
        private string _cursorTag;
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
        public string CursorTag => _cursorTag;
        public Vector2 CursorScreenPosition => _input != null ? _input.PointerPosition : Vector2.zero;
        public string Banner => _banner;
        public Camera WorldCamera => _input != null ? _input.ActiveCamera : Camera.main;

        public event Action StateChanged;
        public event Action<HudFlyover> Flyover;

        /// <summary>The rules state. Exposed for tests and tooling; presentation code must go through the view and events.</summary>
        public MatchState State => _state;

        private Mimas.Core.Match.UnitView LocalUnitView => _view != null && _localUnitId != None ? _view.FindUnit(_localUnitId) : null;

        private bool IsPlaying => _movingUnitId != None || Time.time < _pauseUntil || _pending.Count > 0 || ProjectileInFlight;

        private bool ProjectileInFlight => _projectiles != null && _projectiles.IsPlaying;

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
            _examinedPropId = None;
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
            if (_examinedUnitId == None && _examinedPropId == None) return;
            _examinedUnitId = None;
            _examinedPropId = None;
            RefreshView();
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

            // The aiming layer lives on the board object; taking it from there keeps the scene wiring to one field.
            if (_aimPreview == null) _aimPreview = _board.GetComponent<AimPreview>();
            if (_rangeCircles == null) _rangeCircles = _board.GetComponent<RangeCircles>();
            if (_projectiles == null) _projectiles = _board.GetComponent<ProjectilePlayback>();
        }

        private void OnEnable()
        {
            if (_board == null || _input == null) return;
            _board.BoardBuilt += HandleBoardBuilt;
            _input.Clicked += HandleClicked;
            _input.HoverChanged += HandleHoverChanged;
            _input.RightClicked += HandleRightClicked;
            if (_unit != null) _unit.Mover.Arrived += HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived += HandleArrived;
        }

        private void OnDisable()
        {
            if (_board == null || _input == null) return;
            _board.BoardBuilt -= HandleBoardBuilt;
            _input.Clicked -= HandleClicked;
            _input.HoverChanged -= HandleHoverChanged;
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
                setup = new MatchSetup(_board.MapData.Id, _settings.PlayerLoadout.ToLoadout(),
                    _settings.OpponentLoadout.ToLoadout(), _settings.FirstPlayer);
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
            _damage = new DamageCalculator(_catalog);

            _unitViews.Clear();
            _propViews.Clear();
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
            BuildBodies();
            FaceNearestEnemies();
            CollectAbilities();
            RefreshMarkers();

            Debug.Log("[LocalMatchSession] " + _settings.PlayerLoadout.Weapon + " vs " + _settings.OpponentLoadout.Weapon + " on " + _board.MapData.Id
                + ", seed " + _settings.Seed + ", " + _turnSeconds + " s per turn.");

            Enqueue(_state.Start());
            RaiseStateChanged();
        }

        /// <summary>
        /// Sizes every body on the board from the projection: heroes get their aim point and placeholder height
        /// from <c>UnitView.AimHeight</c> / <c>BodyHeight</c>, and every prop the map placed gets a view. One
        /// conversion (<see cref="BoardView.WorldPerHeightUnit"/>) turns Core's integer units into world units,
        /// so nothing here repeats a number that lives in <c>rules.json</c>.
        /// </summary>
        private void BuildBodies()
        {
            if (_view == null) return;
            float worldPerUnit = _board.WorldPerHeightUnit;

            for (int i = 0; i < _view.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = _view.Units[i];
                UnitView view;
                if (_unitViews.TryGetValue(unit.Id, out view)) view.Configure(unit.AimHeight, unit.BodyHeight, worldPerUnit);
            }

            if (_propRoot == null)
            {
                _propRoot = new GameObject("Props").transform;
                _propRoot.SetParent(_board.transform, false);
            }

            for (int i = 0; i < _view.Props.Count; i++)
            {
                Mimas.Core.Match.PropView prop = _view.Props[i];
                if (_propViews.ContainsKey(prop.Id)) continue;

                var go = new GameObject("Prop_" + prop.DefId + "_" + prop.Id);
                go.layer = _board.gameObject.layer;
                go.transform.SetParent(_propRoot, false);
                PropView view = go.AddComponent<PropView>();
                view.Configure(prop, _board, worldPerUnit, PropColor(prop.DefId));
                _propViews[prop.Id] = view;

                // Only something that can be destroyed gets a bar: a wall has no hit points to show.
                if (!prop.IsDamageable) continue;

                var hud = new HudUnit
                {
                    Id = prop.Id,
                    IsMine = false,
                    IsProp = true,
                    Hp = prop.Hp,
                    MaxHp = prop.MaxHp,
                    Anchor = view.transform,
                    AnchorOffset = new Vector3(0f, view.BodyWorldHeight + 0.25f, 0f),
                };
                _hudUnits.Add(hud);
                _hudUnitsById[prop.Id] = hud;
            }
        }

        /// <summary>Drops a body's floating tag, in place, when it leaves the board.</summary>
        private void RemoveHudUnit(int id)
        {
            HudUnit hud;
            if (!_hudUnitsById.TryGetValue(id, out hud)) return;
            _hudUnitsById.Remove(id);
            _hudUnits.Remove(hud);
            if (_previewUnit == hud) _previewUnit = null;
        }

        /// <summary>Placeholder colours until props have models: a wall is stone-dark, a pillar sandstone.</summary>
        private Color PropColor(string defId)
        {
            if (string.Equals(defId, "wall", StringComparison.Ordinal)) return _wallColor;
            if (string.Equals(defId, "pillar", StringComparison.Ordinal)) return _pillarColor;
            return _wallColor;
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
                if (_movingUnitId != None || Time.time < _pauseUntil || ProjectileInFlight) return;
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

                case PropDestroyedEvent destroyed:
                {
                    PropView prop;
                    if (_propViews.TryGetValue(destroyed.PropId, out prop))
                    {
                        _propViews.Remove(destroyed.PropId);
                        if (prop != null) prop.PlayDestroyed(null);
                    }
                    RemoveHudUnit(destroyed.PropId);
                    if (_examinedPropId == destroyed.PropId) _examinedPropId = None;
                    // Occupancy and sight are Core's: the tile is already walkable and already lets shots through.
                    RefreshView();
                    RaiseStateChanged();
                    break;
                }

                case UnitDiedEvent died:
                {
                    HudUnit hud;
                    if (_hudUnitsById.TryGetValue(died.UnitId, out hud)) hud.IsAlive = false;
                    UnitView view;
                    if (_unitViews.TryGetValue(died.UnitId, out view)) view.gameObject.SetActive(false);
                    RefreshView();
                    FaceNearestEnemies();
                    RaiseStateChanged();
                    break;
                }

                case TurnEndedEvent ended:
                    _clockRunning = false;
                    if (ended.Player == LocalPlayer && !ended.Acted && ended.Reason == EndTurnReason.Timeout)
                        _penaltyPending = _idleTurnSeconds > 0f;
                    Disarm();
                    _examinedUnitId = None;
                    _examinedPropId = None;
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
                if (hud.IsProp) continue;               // a prop's id is a body id, not a unit id, and it has no turn
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

        /// <summary>
        /// Plays one resolved attack: the projectile flies the same curve the preview drew, and only when it
        /// lands does the target's bar drop and the flyover fire. Nothing about the shot is recomputed here —
        /// the damage is the event's, the geometry is the two bodies' aim points.
        /// </summary>
        private void PlayAttack(AttackResolvedEvent attack)
        {
            string detail = DescribeSurprise(attack);
            _revealedThisBatch.Clear();

            ClearAim();

            var def = _catalog.Abilities.TryGet(attack.AbilityId, out AbilityDef ability) ? ability as AttackDef : null;
            Func<float, Vector3> curve = BuildResolvedCurve(attack, def);
            float duration = def != null ? ProjectilePlayback.DurationFor(def.Trajectory, ResolvedDistance(attack)) : 0f;

            int targetId = attack.TargetId;
            int damage = attack.Damage;
            int hpAfter = attack.TargetHpAfter;
            Vector3 impact = TargetOverlayPosition(attack);

            Action onImpact = () =>
            {
                HudUnit target;
                if (_hudUnitsById.TryGetValue(targetId, out target))
                {
                    target.Hp = hpAfter;
                    target.GhostDamage = 0;
                }

                var handler = Flyover;
                if (handler != null)
                {
                    handler(new HudFlyover
                    {
                        UnitId = targetId,
                        WorldPosition = impact,
                        Headline = "-" + damage,
                        Detail = detail,
                    });
                }

                _pauseUntil = Time.time + _settings.HitPauseSeconds;
                RefreshView();
                RaiseStateChanged();
            };

            if (_projectiles == null || curve == null || duration <= 0f)
            {
                onImpact();
                return;
            }

            _projectiles.Play(curve, duration, _projectiles.ColorFor(def.Category), onImpact);
            RaiseStateChanged();
        }

        /// <summary>The curve of an attack that has already happened: attacker aim point to victim aim point.</summary>
        private Func<float, Vector3> BuildResolvedCurve(AttackResolvedEvent attack, AttackDef def)
        {
            UnitView attacker;
            if (def == null || !_unitViews.TryGetValue(attack.AttackerId, out attacker)) return null;

            Transform target = TargetAimPoint(attack);
            if (target == null) return null;

            int fromUnits = _board.TileTopUnits(attacker.CurrentHex) + attacker.AimHeightUnits;
            int toUnits = _board.TileTopUnits(TargetHex(attack)) + TargetAimHeightUnits(attack);

            return FlightCurve.Build(def.Trajectory, attacker.AimPoint.position, target.position,
                fromUnits, toUnits, def.Apex, _board.WorldPerHeightUnit, 3 * _catalog.Rules.Heights.Body);
        }

        private Transform TargetAimPoint(AttackResolvedEvent attack)
        {
            if (attack.TargetIsProp)
            {
                PropView prop;
                return _propViews.TryGetValue(attack.TargetId, out prop) && prop != null ? prop.AimPoint : null;
            }
            UnitView unit;
            return _unitViews.TryGetValue(attack.TargetId, out unit) ? unit.AimPoint : null;
        }

        private Hex TargetHex(AttackResolvedEvent attack)
        {
            if (attack.TargetIsProp)
            {
                PropView prop;
                if (_propViews.TryGetValue(attack.TargetId, out prop) && prop != null) return prop.CurrentHex;
                return default;
            }
            UnitView unit;
            return _unitViews.TryGetValue(attack.TargetId, out unit) ? unit.CurrentHex : default;
        }

        private int TargetAimHeightUnits(AttackResolvedEvent attack)
        {
            if (attack.TargetIsProp)
            {
                PropView prop;
                if (_propViews.TryGetValue(attack.TargetId, out prop) && prop != null) return prop.AimHeightUnits;
                return _catalog.Rules.Heights.Aim;
            }
            UnitView unit;
            return _unitViews.TryGetValue(attack.TargetId, out unit) ? unit.AimHeightUnits : _catalog.Rules.Heights.Aim;
        }

        private int ResolvedDistance(AttackResolvedEvent attack)
        {
            UnitView attacker;
            if (!_unitViews.TryGetValue(attack.AttackerId, out attacker)) return 0;
            return Hex.Distance(attacker.CurrentHex, TargetHex(attack));
        }

        /// <summary>Where the damage number pops: above the body that was hit.</summary>
        private Vector3 TargetOverlayPosition(AttackResolvedEvent attack)
        {
            if (attack.TargetIsProp)
            {
                PropView prop;
                if (_propViews.TryGetValue(attack.TargetId, out prop) && prop != null)
                    return prop.transform.position + new Vector3(0f, prop.BodyWorldHeight + 0.25f, 0f);
            }
            else
            {
                UnitView unit;
                if (_unitViews.TryGetValue(attack.TargetId, out unit))
                    return unit.transform.position + new Vector3(0f, _overlayHeight, 0f);
            }
            return _board.HexToSurface(TargetHex(attack)) + new Vector3(0f, _overlayHeight, 0f);
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
                _actions.Add(new HudAction(def.Id, def.Name, def.Description, DescribeAttackRules(def as AttackDef),
                    def.Icon, def.Category, def.Cost, affordable, canAct && affordable));
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
            if (_examinedPropId != None)
            {
                _examine = ExamineProp(_examinedPropId);
                if (_examine != null) return;
                _examinedPropId = None;            // it was destroyed while the panel was open
            }

            Mimas.Core.Match.UnitView unit = _examinedUnitId == None || _view == null ? null : _view.FindUnit(_examinedUnitId);
            if (unit == null)
            {
                _examine = null;
                return;
            }

            var examine = new HudExamine
            {
                Title = "Hero",
                Subtitle = unit.IsMine ? "Your unit" : "Opponent",
                Description = null,
                Hp = unit.Hp,
                MaxHp = unit.MaxHp,
                Ap = unit.Ap,
                ApPerTurn = unit.ApPerTurn,
            };

            // Gear first, in slot order: the four items are public even when their abilities are not.
            for (int i = 0; i < unit.ItemIds.Count; i++)
            {
                ItemDef item;
                bool known = _catalog.Items.TryGet(unit.ItemIds[i], out item);
                examine.Items.Add(new HudExamineEntry
                {
                    Name = known ? item.Name : unit.ItemIds[i],
                    Description = known ? DescribeItem(item) : null,
                    Icon = known ? item.Icon : null,
                    Hidden = false,
                });
            }

            // Abilities grouped by the item that grants them, innate ones last.
            for (int i = 0; i < unit.ItemIds.Count; i++) AddAbilityEntries(examine, unit, unit.ItemIds[i]);
            AddAbilityEntries(examine, unit, null);

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

        /// <summary>
        /// The examine panel for a prop: what it is, what it is made of and what it costs to remove. Props are
        /// neutral and entirely public (design: #props), so there is nothing here to hide and no gear or
        /// abilities to list. Null when that prop is no longer on the board.
        /// </summary>
        private HudExamine ExamineProp(int propId)
        {
            if (_view == null) return null;
            Mimas.Core.Match.PropView prop = _view.FindProp(propId);
            if (prop == null) return null;

            PropDef def;
            bool known = _catalog.Props.TryGet(prop.DefId, out def);

            var examine = new HudExamine
            {
                Title = known ? def.Name : prop.DefId,
                Subtitle = "Terrain",
                Description = known ? def.Description : null,
                Hp = prop.Hp,
                MaxHp = prop.MaxHp,
            };
            examine.Modifiers.Add(new HudExamineEntry
            {
                Name = "Blocks sight and movement",
                Description = prop.IsDamageable
                    ? "Shots stop on it and nothing walks through it, until it comes down."
                    : "Shots stop on it and nothing walks through it. It cannot be destroyed.",
            });
            return examine;
        }

        /// <summary>Adds every ability the given item grants (or every innate one when <paramref name="itemId"/> is null).</summary>
        private void AddAbilityEntries(HudExamine examine, Mimas.Core.Match.UnitView unit, string itemId)
        {
            string group = "Innate";
            if (itemId != null)
            {
                ItemDef item;
                group = _catalog.Items.TryGet(itemId, out item) ? item.Name : itemId;
            }

            for (int i = 0; i < unit.Abilities.Count; i++)
            {
                KnownEntry entry = unit.Abilities[i];
                if (entry.SourceItemId != itemId) continue;

                AbilityDef def = null;
                bool known = entry.Revealed && _catalog.Abilities.TryGet(entry.Id, out def);
                examine.Abilities.Add(new HudExamineEntry
                {
                    Name = entry.Revealed ? (known ? def.Name : entry.Id) : "Unknown ability",
                    Description = entry.Revealed ? (known ? DescribeAbility(def) : null) : "Revealed once the opponent uses it.",
                    Icon = known ? def.Icon : null,
                    Hidden = !entry.Revealed,
                    Group = group,
                });
            }
        }

        /// <summary>Stat keys in the order the examine panel reads them out; anything else follows, sorted.</summary>
        private static readonly string[] StatOrder = { "hp", "ap", "power.weapon", "power.spell", "defense.weapon", "defense.spell" };

        /// <summary>The item's own line plus a one-line summary of the stats it adds.</summary>
        private static string DescribeItem(ItemDef item)
        {
            string stats = null;
            for (int order = 0; order <= StatOrder.Length; order++)
            {
                for (int i = 0; i < item.Stats.Entries.Count; i++)
                {
                    var entry = item.Stats.Entries[i];
                    if (entry.Value == 0) continue;
                    int at = System.Array.IndexOf(StatOrder, entry.Key);
                    if (at < 0) at = StatOrder.Length;          // unknown keys come last, in key order
                    if (at != order) continue;
                    string line = (entry.Value > 0 ? "+" : "") + entry.Value + " " + StatLabel(entry.Key);
                    stats = stats == null ? line : stats + ", " + line;
                }
            }
            if (string.IsNullOrEmpty(item.Description)) return stats;
            return stats == null ? item.Description : item.Description + "\n" + stats;
        }

        private static string StatLabel(string key)
        {
            switch (key)
            {
                case "hp": return "hp";
                case "ap": return "ap";
                case "power.weapon": return "strength";
                case "power.spell": return "magic";
                case "defense.weapon": return "weapon armour";
                case "defense.spell": return "spell armour";
                default: return key;
            }
        }

        /// <summary>
        /// The one rules line above an attack's description: how it travels, whether the attacker has to see the
        /// target, and the range band — the three things that decide whether this shot is even possible from here
        /// (design: #trajectories, #attacks). Null for anything that is not an attack.
        /// </summary>
        private static string DescribeAttackRules(AttackDef attack)
        {
            if (attack == null) return null;

            string travel;
            if (attack.Trajectory == Trajectories.Arc) travel = "Lobbed";
            else if (attack.Trajectory == Trajectories.Sky) travel = "From above";
            else travel = "Straight shot";

            string sight = attack.LineOfSight ? "needs sight" : "no sight needed";
            string range = attack.MinRange == attack.Range
                ? "range " + attack.Range
                : "range " + attack.MinRange + "-" + attack.Range;

            return travel + "  ·  " + sight + "  ·  " + range;
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
                ClearAim();
            }
            else
            {
                var attack = def as AttackDef;
                if (attack == null) return;

                _state.AttackTargets(_localUnitId, def.Id, _targetScratch);
                for (int i = 0; i < _targetScratch.Count; i++) _highlight.Add(_targetScratch[i].Position);
                _targetScratch.Clear();
                _board.HighlightTargets(_highlight);
                ShowRangeBand(attack);
            }
            RefreshEmphasis();

            // Arming is itself a hover: the cursor is usually already sitting on what the player wants to shoot,
            // and making them jiggle the mouse to see the line would be a silly way to find that out.
            if (_input != null) HandleHoverChanged(_input.Hover);
        }

        /// <summary>
        /// Draws the attack's range band as two circles centred on the hero. The rule is Euclidean
        /// (<c>minRange² ≤ q²+qr+r² ≤ range²</c>), and the world distance between two hex centres is exactly
        /// <c>spacing · √(q²+qr+r²)</c>, so a circle of <c>range · spacing</c> is that rule drawn — not an
        /// approximation of it, which is why the band needs no tile list to dim.
        /// </summary>
        private void ShowRangeBand(AttackDef attack)
        {
            UnitView me;
            if (_rangeCircles == null || _localUnitId == None || !_unitViews.TryGetValue(_localUnitId, out me)) return;

            float spacing = _board.Spacing;
            _rangeCircles.Show(me.transform.position, attack.MinRange * spacing, attack.Range * spacing);
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

        private void HandleClicked(BoardHover hover)
        {
            if (!_ready) return;

            if (!hover.Any)
            {
                bool changed = _armed != None || _examinedUnitId != None || _examinedPropId != None;
                Disarm();
                _examinedUnitId = None;
                _examinedPropId = None;
                if (changed) { RefreshView(); RaiseStateChanged(); }
                return;
            }

            Hex coord = hover.Hex;

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

            // Nothing armed: clicks on a body open examine, anything else closes it. A prop is worth examining
            // too — "can I bring this down, and what does it block?" is a real question (design: #props).
            Unit clicked;
            _examinedUnitId = _state.Units.TryGetUnitAt(coord, out clicked) ? clicked.Id : None;
            _examinedPropId = _examinedUnitId == None && hover.Prop != null ? hover.Prop.Id : None;
            RefreshView();
            RaiseStateChanged();
        }

        private void HandleHoverChanged(BoardHover hover)
        {
            if (!_ready) return;
            _board.SetHovered(hover.Tile);

            AbilityDef def = ArmedAbility;
            if (def == null || IsPlaying || !hover.Any)
            {
                _board.ShowPathPreview(null);
                // The band stays lit while the cursor is off the board — it belongs to the armed ability, not the
                // cursor — but there is nothing left to aim at, so the line and the tag go.
                if (_aimPreview != null) _aimPreview.Hide();
                bool changed = _cursorTag != null;
                _cursorTag = null;
                if (ClearPreview() || changed) RaiseStateChanged();
                return;
            }

            if (def is MovementDef)
            {
                MovePlan plan;
                _board.ShowPathPreview(_moveOptions.TryGet(hover.Hex, out plan) ? plan.Path : null);
                return;
            }

            var attack = def as AttackDef;
            if (attack != null) ShowAim(attack, hover);
        }

        /// <summary>
        /// The aim state machine (design: #presentation). One <see cref="MatchState.CheckTarget"/> answers what
        /// would happen, and the answer decides the line, the tooltip and the cursor tag together — so the board
        /// and the HUD can never tell the player two different stories.
        /// </summary>
        private void ShowAim(AttackDef attack, BoardHover hover)
        {
            // Aiming is local presentation: the hero turns towards whatever the cursor snapped to, and no
            // command, event or byte on the wire ever says so (design: #presentation).
            Vector3 aimPoint = SnappedAimPoint(hover);
            FaceAim(aimPoint);

            TargetCheck check = _state.CheckTarget(_localUnitId, attack.Id, hover.Hex);
            Func<float, Vector3> curve = BuildCurve(attack, hover, aimPoint);
            int samples = FlightCurve.Samples(attack.Trajectory);

            ClearPreview();
            _cursorTag = null;

            switch (check.Reason)
            {
                case TargetRejectReason.None:
                    _aimPreview.ShowClear(curve, aimPoint, samples);
                    ShowPreview(attack, check.Victim, null);
                    break;

                case TargetRejectReason.NoLineOfSight:
                case TargetRejectReason.TrajectoryBlocked:
                {
                    Vector3 blocked = check.HasBlockedAt ? _board.HexToAimPoint(check.BlockedAt, 0) : aimPoint;
                    _aimPreview.ShowBlocked(curve, blocked, samples);
                    ShowPreview(attack, check.Victim,
                        check.Reason == TargetRejectReason.NoLineOfSight ? "No line of sight" : "Trajectory blocked");
                    break;
                }

                case TargetRejectReason.OutOfRange:
                    _aimPreview.ShowOutOfRange(curve, samples);
                    _cursorTag = "Out of range";
                    ShowPreview(attack, check.Victim, "Out of range");
                    break;

                case TargetRejectReason.NotDamageable:
                    _aimPreview.ShowNotTargetable(curve, aimPoint, samples);
                    _cursorTag = "Cannot be hit";
                    break;

                case TargetRejectReason.OwnUnit:
                case TargetRejectReason.TargetDead:
                    _aimPreview.Hide();
                    break;

                default:
                    // NoBody, and anything added to the enum later: an empty tile gets the grey path and no words.
                    _aimPreview.ShowOutOfRange(curve, samples);
                    break;
            }

            RaiseStateChanged();
        }

        /// <summary>
        /// The world curve for a shot at this hover, from the attacker's aim point to the snapped one, with both
        /// ends also expressed in Core's height units because that is what the arc's shape depends on.
        /// </summary>
        private Func<float, Vector3> BuildCurve(AttackDef attack, BoardHover hover, Vector3 aimPoint)
        {
            UnitView me;
            if (_localUnitId == None || !_unitViews.TryGetValue(_localUnitId, out me)) return null;

            int fromUnits = _board.TileTopUnits(me.CurrentHex) + me.AimHeightUnits;
            int toUnits = _board.TileTopUnits(hover.Hex) + TargetAimHeightUnits(hover);

            return FlightCurve.Build(attack.Trajectory, me.AimPoint.position, aimPoint,
                fromUnits, toUnits, attack.Apex, _board.WorldPerHeightUnit, 3 * _catalog.Rules.Heights.Body);
        }

        /// <summary>The aim height of whatever the cursor snapped to, in height units above its tile top.</summary>
        private int TargetAimHeightUnits(BoardHover hover)
        {
            if (hover.Unit != null) return hover.Unit.AimHeightUnits;
            if (hover.Prop != null) return hover.Prop.AimHeightUnits;
            return _catalog.Rules.Heights.Aim;
        }

        /// <summary>
        /// Where a shot at this hover would land: the enemy's aim point, else the prop's hit mark, else the
        /// tile centre at aim height — the snap priority from the design page (#presentation). The same point
        /// the hero turns towards, the preview line ends at and the projectile flies to.
        /// </summary>
        private Vector3 SnappedAimPoint(BoardHover hover)
        {
            if (hover.Unit != null && hover.Unit.AimPoint != null) return hover.Unit.AimPoint.position;
            if (hover.Prop != null && hover.Prop.AimPoint != null) return hover.Prop.AimPoint.position;
            return _board.HexToAimPoint(hover.Hex, _catalog.Rules.Heights.Aim);
        }

        /// <summary>Turns the local hero towards a point while it is aiming.</summary>
        private void FaceAim(Vector3 point)
        {
            UnitView view;
            if (_localUnitId != None && _unitViews.TryGetValue(_localUnitId, out view)) view.Facing.FaceWorldPoint(point);
        }

        /// <summary>
        /// Nothing armed: every hero looks at the nearest living enemy, so both read as being in the fight.
        /// A hero with no enemy left keeps the yaw it had.
        /// </summary>
        private void FaceNearestEnemies()
        {
            if (_view == null) return;
            for (int i = 0; i < _view.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = _view.Units[i];
                UnitView view;
                if (!_unitViews.TryGetValue(unit.Id, out view) || !unit.IsAlive) continue;

                UnitView enemy = NearestLivingEnemyView(unit);
                if (enemy != null) view.Facing.FaceTransform(enemy.transform);
                else view.Facing.ClearTarget();
            }
        }

        private UnitView NearestLivingEnemyView(Mimas.Core.Match.UnitView of)
        {
            UnitView best = null;
            int bestDistance = int.MaxValue;
            for (int i = 0; i < _view.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView other = _view.Units[i];
                if (other.Owner == of.Owner || !other.IsAlive) continue;

                int distance = Hex.Distance(of.Position, other.Position);
                if (distance >= bestDistance) continue;

                UnitView view;
                if (!_unitViews.TryGetValue(other.Id, out view)) continue;
                bestDistance = distance;
                best = view;
            }
            return best;
        }

        /// <summary>
        /// Fills the damage tooltip for a body the cursor is on. A refused shot still shows the number it
        /// <em>would</em> do under <paramref name="blockedReason"/> — the player is choosing between shots, and
        /// "this one is worth moving for" is the decision — but the target's bar shows no ghost, because nothing
        /// is going to happen.
        /// </summary>
        private void ShowPreview(AttackDef attack, IBody target, string blockedReason)
        {
            if (target == null) return;

            DamageBreakdown breakdown = PreviewDamage(attack, target);
            if (breakdown == null) return;

            var preview = new HudPreview
            {
                TargetUnitId = target.Id,
                TargetIsProp = !(target is Unit),
                AbilityName = attack.Name,
                Total = breakdown.Total,
                IsExact = breakdown.IsExact,
                BlockedReason = blockedReason,
            };
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
            if (_previewUnit != null && blockedReason == null) _previewUnit.GhostDamage = Mathf.Min(breakdown.Total, _previewUnit.Hp);
            RefreshEmphasis();
        }

        /// <summary>
        /// What this attack would do to this body, from what the local player knows. The same
        /// <see cref="DamageCalculator"/> the rules resolve with (ADR-017), asked directly rather than through
        /// <c>MatchState.PreviewAttack</c> so that a shot which is refused can still show its number.
        /// </summary>
        private DamageBreakdown PreviewDamage(AttackDef attack, IBody target)
        {
            Unit attacker;
            if (_damage == null || target == null || !_state.Units.TryGet(_localUnitId, out attacker)) return null;
            return _damage.Compute(_state.Map, attacker, target, attack, Knowledge.For(LocalPlayer, _state));
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

        /// <summary>Drops everything the aiming layer is drawing: line, circles and the cursor tag.</summary>
        private void ClearAim()
        {
            if (_aimPreview != null) _aimPreview.Hide();
            if (_rangeCircles != null) _rangeCircles.Hide();
            _cursorTag = null;
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
            bool changed = _armed != None || _examinedUnitId != None || _examinedPropId != None;
            Disarm();
            _examinedUnitId = None;
            _examinedPropId = None;
            if (changed) { RefreshView(); RaiseStateChanged(); }
        }

        private void Disarm()
        {
            _armed = None;
            _moveOptions = MovementOptions.Empty;
            _highlight.Clear();
            ClearPreview();
            ClearAim();
            if (_board != null)
            {
                _board.ClearHighlights();
                _board.ShowPathPreview(null);
            }
            FaceNearestEnemies();
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
