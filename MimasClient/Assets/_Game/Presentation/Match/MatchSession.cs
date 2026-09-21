using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using Mimas.Client.Content;
using Mimas.Client.Net;
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
    /// The presenter for one match: board, HUD, aiming, playback and facing. Where the match comes from is
    /// an <see cref="IMatchDriver"/> and nothing here knows which (ADR-028) — a practice game against a bot
    /// in this process, or a real one whose truth is on the server. Commands go in through
    /// <see cref="Submit"/>, events come back out already filtered for the local player and are played one
    /// at a time onto the board (moves animate, hits pause), and everything the HUD reads comes from the
    /// <see cref="PlayerView"/> projection or from the events, never from a state this process owns.
    ///
    /// Board interaction: arm an action from the bar; a movement paints reachable tiles and previews paths,
    /// an attack paints legal targets and previews damage; a click submits the command. After an action the
    /// same ability stays armed while it is still affordable (three steps are three clicks, not six).
    /// Nothing is armed at the start of a turn (ADR-015); clicking a unit with nothing armed examines it.
    /// The clock belongs to the driver: locally it is the fake from ADR-015 (turn cap, rope, idle penalty),
    /// online it is the server's, counted down between messages and re-anchored by every one of them. Either
    /// way a timeout is an <see cref="EndTurnCommand"/> with reason <see cref="EndTurnReason.Timeout"/>,
    /// submitted by whoever is keeping time.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MatchSession : MonoBehaviour, IMatchHudSource
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

        private const int None = -1;

        /// <summary>How long the result sits before the way out of it appears.</summary>
        private const float BackToLobbyDelaySeconds = 3f;

        // Rules.
        private ContentCatalog _catalog;
        private IMatchDriver _driver;
        private int _localUnitId = None;

        /// <summary>The seat the person at this screen is playing. 0 in practice; either seat online.</summary>
        private int LocalPlayer => _driver != null ? _driver.LocalPlayer : 0;

        /// <summary>The truth in practice, the mirror online (ADR-026). Either way, the same rules code.</summary>
        private MatchState Rules => _driver != null ? _driver.Rules : null;

        /// <summary>The latest projection for the local player.</summary>
        private PlayerView View => _driver != null ? _driver.View : null;

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
        private string _bannerDetail;
        private float _backToLobbyAt = -1f;
        private bool _ready;

        private float _ropeSeconds = 10f;
        private int _localTurnNumber;

        // ---- IMatchHudSource -------------------------------------------------------------------------

        public IReadOnlyList<HudAction> Actions => _actions;
        public int ActiveActionIndex => _armed;
        public bool IsMyTurn => _ready && View != null && View.IsMyTurn;
        public bool CanEndTurn => CanAct;
        public int TurnNumber => _localTurnNumber;
        public int ApCurrent => LocalUnitView != null ? LocalUnitView.Ap : 0;
        public int ApPerTurn => LocalUnitView != null ? LocalUnitView.ApPerTurn : 0;
        public float TurnSecondsRemaining => _driver != null ? _driver.TurnSecondsRemaining : 0f;
        public float TurnSecondsTotal => _driver != null ? _driver.TurnSecondsTotal : 0f;
        public float RopeSeconds => _ropeSeconds;
        public HudExamine Examine => _examine;
        public IReadOnlyList<HudUnit> Units => _hudUnits;
        public HudPreview Preview => _preview;
        public string CursorTag => _cursorTag;
        public Vector2 CursorScreenPosition => _input != null ? _input.PointerPosition : Vector2.zero;
        public string Banner => _banner;
        public string BannerDetail => _bannerDetail;
        public bool ShowBackToLobby => _backToLobbyAt >= 0f && Time.time >= _backToLobbyAt;

        /// <summary>Read lazily, so it is right whenever the banner happens to ask (ADR-032).</summary>
        public string BackLabel => NetClient.Instance != null && NetClient.Instance.PendingRoom != null
            ? "Back to room"
            : "Back to lobby";
        public string OpponentName => _driver != null ? _driver.OpponentName : null;
        public string OpponentStatus => _driver != null ? _driver.OpponentStatus : null;
        public bool CanResign => _driver != null && _driver.CanResign && !IsPlaying;
        public Camera WorldCamera => _input != null ? _input.ActiveCamera : Camera.main;

        public event Action StateChanged;
        public event Action<HudFlyover> Flyover;

        /// <summary>The rules state. Exposed for tests and tooling; presentation code must go through the view and events.</summary>
        public MatchState State => Rules;

        /// <summary>The driver, for tests and the Editor: which kind of match this is, and its clock.</summary>
        public IMatchDriver Driver => _driver;

        private Mimas.Core.Match.UnitView LocalUnitView => View != null && _localUnitId != None ? View.FindUnit(_localUnitId) : null;

        private bool IsPlaying => _movingUnitId != None || Time.time < _pauseUntil || _pending.Count > 0 || ProjectileInFlight;

        private bool ProjectileInFlight => _projectiles != null && _projectiles.IsPlaying;

        /// <summary>The local player may arm an action or pick a target right now.</summary>
        private bool CanAct => _ready && !Rules.IsOver && Rules.ActivePlayer == LocalPlayer && !IsPlaying;

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

        /// <summary>Concede. The HUD asks twice before it calls this.</summary>
        public void Resign()
        {
            if (_driver == null || !_driver.CanResign) return;
            Debug.Log("[MatchSession] resigning");
            _driver.Resign();
        }

        /// <summary>
        /// Leaves the result behind. Online that means the lobby, carrying the result for its one-line
        /// summary; in practice there is no lobby to go to, so the Arena simply starts again.
        /// </summary>
        public void BackToLobby()
        {
            NetClient net = NetClient.Instance;
            if (net == null)
            {
                SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
                return;
            }

            net.LastResult = new MatchResult
            {
                Won = Rules != null && Rules.Winner == LocalPlayer,
                Reason = _bannerDetail,
                OpponentName = OpponentName,
            };
            net.ForgetMatch();
            SceneManager.LoadScene("Lobby");
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
                Debug.LogError("[MatchSession] _content, _board, _input, _unit and _opponent must all be assigned.", this);
                enabled = false;
                return;
            }

            if (_settings == null)
            {
                Debug.LogWarning("[MatchSession] No MatchSettings assigned; using built-in defaults.", this);
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
            _input.Clicked += HandleClicked;
            _input.HoverChanged += HandleHoverChanged;
            _input.RightClicked += HandleRightClicked;
            if (_unit != null) _unit.Mover.Arrived += HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived += HandleArrived;
        }

        private void OnDisable()
        {
            if (_board == null || _input == null) return;
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
            if (_driver == null) return;
            _driver.EventsArrived -= HandleEventsArrived;
            _driver.Resynced -= HandleResynced;
            _driver.StatusChanged -= HandleStatusChanged;
            _driver.Dispose();
            _driver = null;
        }

        private void Start()
        {
            BeginMatch();
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

            // The clock, the bot and the opponent's countdown all belong to the driver; the presenter only
            // says how much time has passed and draws whatever comes back.
            _driver.Tick(Time.deltaTime);

            // The "Back to lobby" button appears a beat after the banner, so the result can land first.
            if (_backToLobbyAt >= 0f && Time.time >= _backToLobbyAt && Time.time - Time.deltaTime < _backToLobbyAt)
                RaiseStateChanged();
        }

        // ---- setup ----------------------------------------------------------------------------------

        /// <summary>
        /// Picks where the match comes from and gets everything else ready for it. Online the map is not known
        /// until the server has said so, so the board is built from the driver rather than from a serialized
        /// id in <c>Awake</c> — which is why <c>_buildOnAwake</c> is off on the Arena's board.
        /// </summary>
        private void BeginMatch()
        {
            if (_ready) return;

            _catalog = _content.EnsureLoaded();
            if (_catalog == null)
            {
                Debug.LogError("[MatchSession] Content failed to load; no match.", this);
                return;
            }

            NetClient net = NetClient.Instance;
            JObject start = net != null ? net.ConsumePendingMatch() : null;

            try
            {
                if (start != null)
                {
                    _driver = new OnlineMatchDriver(_catalog, net, start);
                    Debug.Log("[MatchSession] online: match " + start.Value<int>("matchId") + ", seat " + start.Value<int>("youAre")
                        + " vs " + start.Value<string>("opponentName"));
                }
                else
                {
                    string mapId = _settings != null && !string.IsNullOrEmpty(_settings.MapId) ? _settings.MapId : "arena-4";
                    _driver = new LocalMatchDriver(_catalog, _settings, mapId, () => IsPlaying);
                    Debug.Log("[MatchSession] local practice: " + _settings.PlayerLoadout.Weapon + " vs "
                        + _settings.OpponentLoadout.Weapon + " on " + mapId + ", seed " + _settings.Seed + ".");
                }
            }
            catch (Exception e) when (e is ArgumentException || e is KeyNotFoundException)
            {
                Debug.LogError("[MatchSession] Match setup refused: " + e.Message, this);
                return;
            }

            if (!_board.IsBuilt) _board.Build(_driver.Rules.MapData.Id);
            if (!_board.IsBuilt || _board.MapData == null)
            {
                Debug.LogError("[MatchSession] The board could not be built for map '" + _driver.Rules.MapData.Id + "'.", this);
                return;
            }

            _driver.EventsArrived += HandleEventsArrived;
            _driver.Resynced += HandleResynced;
            _driver.StatusChanged += HandleStatusChanged;

            _unitViews.Clear();
            _propViews.Clear();
            _hudUnits.Clear();
            _hudUnitsById.Clear();
            // Which prefab plays which side is decided by ownership, not by seat number: online you may be
            // seat 1, and your hero should still be the one that looks like yours.
            for (int i = 0; i < View.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = View.Units[i];
                UnitView view = unit.IsMine ? _unit : _opponent;
                _unitViews[unit.Id] = view;
                view.gameObject.SetActive(unit.IsAlive);
                view.SnapTo(unit.Position, _board);
                if (unit.IsMine) _localUnitId = unit.Id;

                var hud = new HudUnit
                {
                    Id = unit.Id,
                    IsMine = unit.IsMine,
                    IsAlive = unit.IsAlive,
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

            _ropeSeconds = Mathf.Min(_settings.RopeSeconds, Mathf.Max(1f, _driver.TurnSecondsTotal));

            _ready = true;
            RefreshView();
            BuildBodies();
            FaceNearestEnemies();
            CollectAbilities();
            RefreshMarkers();

            var local = _driver as LocalMatchDriver;
            if (local != null) local.Begin();
            else ((OnlineMatchDriver)_driver).Begin(start);

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
            if (View == null) return;
            float worldPerUnit = _board.WorldPerHeightUnit;

            for (int i = 0; i < View.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = View.Units[i];
                UnitView view;
                if (_unitViews.TryGetValue(unit.Id, out view)) view.Configure(unit.AimHeight, unit.BodyHeight, worldPerUnit);
            }

            if (_propRoot == null)
            {
                _propRoot = new GameObject("Props").transform;
                _propRoot.SetParent(_board.transform, false);
            }

            for (int i = 0; i < View.Props.Count; i++)
            {
                Mimas.Core.Match.PropView prop = View.Props[i];
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
            if (_localUnitId == None || !Rules.Units.TryGet(_localUnitId, out unit)) return;
            for (int i = 0; i < unit.AbilityIds.Count; i++)
            {
                AbilityDef def;
                if (_catalog.Abilities.TryGet(unit.AbilityIds[i], out def)) _abilities.Add(def);
            }
        }

        // ---- commands in, events out ----------------------------------------------------------------

        /// <summary>
        /// Offers a command to the driver. It is checked against the rules first either way — locally against
        /// the truth, online against the mirror — so an illegal click never leaves this machine.
        /// </summary>
        public bool Submit(Command command)
        {
            if (!_ready) return false;
            return _driver.Submit(command);
        }

        /// <summary>Events the driver has already filtered for this seat, queued for playback one at a time.</summary>
        private void HandleEventsArrived(IReadOnlyList<MatchEvent> events)
        {
            for (int i = 0; i < events.Count; i++) _pending.Enqueue(events[i]);
        }

        /// <summary>
        /// A whole fresh state arrived (a reconnect, a resync). Nothing is animated towards it: whatever was
        /// mid-flight is dropped and everything snaps to what the server says is true now.
        /// </summary>
        private void HandleResynced()
        {
            if (!_ready) return;

            _pending.Clear();
            _movingUnitId = None;
            _movingPlan = null;
            _pauseUntil = 0f;
            Disarm();

            for (int i = 0; i < View.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = View.Units[i];
                UnitView view;
                if (!_unitViews.TryGetValue(unit.Id, out view)) continue;
                view.gameObject.SetActive(unit.IsAlive);
                if (unit.IsAlive) view.SnapTo(unit.Position, _board);

                HudUnit hud;
                if (!_hudUnitsById.TryGetValue(unit.Id, out hud)) continue;
                hud.Hp = unit.Hp;
                hud.Ap = unit.Ap;
                hud.IsAlive = unit.IsAlive;
                hud.GhostDamage = 0;
            }

            // A prop the view no longer lists was destroyed while we were away.
            var gone = new List<int>();
            foreach (KeyValuePair<int, Mimas.Client.Presentation.PropView> pair in _propViews)
                if (View.FindProp(pair.Key) == null) gone.Add(pair.Key);
            for (int i = 0; i < gone.Count; i++)
            {
                Mimas.Client.Presentation.PropView prop = _propViews[gone[i]];
                _propViews.Remove(gone[i]);
                if (prop != null) Destroy(prop.gameObject);
                RemoveHudUnit(gone[i]);
            }
            for (int i = 0; i < View.Props.Count; i++)
            {
                HudUnit hud;
                if (_hudUnitsById.TryGetValue(View.Props[i].Id, out hud)) hud.Hp = View.Props[i].Hp;
            }

            if (View.IsOver && _banner == null) ShowResult(View.Winner, MatchEndReason.Elimination, true);

            RefreshView();
            RefreshMarkers();
            FaceNearestEnemies();
            RaiseStateChanged();
            Debug.Log("[MatchSession] resynced: turn " + View.TurnNumber + ", active player " + View.ActivePlayer);
        }

        private void HandleStatusChanged()
        {
            if (!_ready) return;

            // The server no longer has this match. There is no result and no winner, only a way back; say
            // that instead of leaving the player watching a board nothing can move again.
            if (_banner == null && _driver.OpponentStatus == OnlineMatchDriver.LostStatus)
            {
                _banner = "MATCH LOST";
                _bannerDetail = "the server no longer has this match";
                _backToLobbyAt = Time.time;
                Disarm();
                Debug.Log("[MatchSession] the match is gone from the server; offering the lobby.");
            }

            RaiseStateChanged();
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
                    Disarm();
                    _examinedUnitId = None;
                    _examinedPropId = None;
                    RefreshView();
                    RaiseStateChanged();
                    break;

                case MatchEndedEvent over:
                    ShowResult(over.Winner, over.Reason, false);
                    Disarm();
                    RefreshView();
                    RaiseStateChanged();
                    break;
            }
        }

        private void PlayTurnStarted(TurnStartedEvent started)
        {
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                if (hud.IsProp) continue;               // a prop's id is a body id, not a unit id, and it has no turn
                Unit unit;
                if (!Rules.Units.TryGet(hud.Id, out unit) || unit.Owner != started.Player) continue;
                hud.Ap = unit.IsAlive ? unit.ApPerTurn : 0;
            }

            if (started.Player == LocalPlayer) _localTurnNumber++;

            Disarm();
            RefreshView();
            RaiseStateChanged();
        }

        /// <summary>
        /// The banner and the line under it. How a match ended matters as much as who won: "you resigned" and
        /// "opponent left" are different stories about the same DEFEAT, and a player who reconnected into a
        /// finished match deserves to be told which one happened.
        /// </summary>
        private void ShowResult(int winner, MatchEndReason reason, bool fromResync)
        {
            bool won = winner == LocalPlayer;
            _banner = won ? "VICTORY" : "DEFEAT";

            switch (reason)
            {
                case MatchEndReason.Resign:
                    _bannerDetail = won ? "opponent resigned" : "you resigned";
                    break;
                case MatchEndReason.Forfeit:
                    _bannerDetail = won ? "opponent left" : "you were disconnected";
                    break;
                default:
                    _bannerDetail = "by elimination";
                    break;
            }

            _backToLobbyAt = Time.time + BackToLobbyDelaySeconds;
            Debug.Log("[MatchSession] match over: player " + winner + " wins " + _bannerDetail
                + (fromResync ? " (found on resync)" : "") + ".");
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

        /// <summary>
        /// The view is the driver's; this only rebuilds what the HUD draws from it. Called after anything that
        /// could change what the player may do.
        /// </summary>
        private void RefreshView()
        {
            RebuildActions();
            RefreshExamine();
            RefreshEmphasis();
        }

        private void RefreshMarkers()
        {
            if (View == null) return;
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                hud.Markers.Clear();
                Mimas.Core.Match.UnitView unit = View.FindUnit(hud.Id);
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

            Mimas.Core.Match.UnitView unit = _examinedUnitId == None || View == null ? null : View.FindUnit(_examinedUnitId);
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
            if (View == null) return null;
            Mimas.Core.Match.PropView prop = View.FindProp(propId);
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
                _moveOptions = Rules.MoveOptions(_localUnitId, def.Id);
                for (int i = 0; i < _moveOptions.Plans.Count; i++) _highlight.Add(_moveOptions.Plans[i].Destination);
                _board.HighlightReachable(_highlight);
                ClearAim();
            }
            else
            {
                var attack = def as AttackDef;
                if (attack == null) return;

                Rules.AttackTargets(_localUnitId, def.Id, _targetScratch);
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
                Unit me = Rules.Units.Get(_localUnitId);
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
            _examinedUnitId = Rules.Units.TryGetUnitAt(coord, out clicked) ? clicked.Id : None;
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

            TargetCheck check = Rules.CheckTarget(_localUnitId, attack.Id, hover.Hex);
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
            if (View == null) return;
            for (int i = 0; i < View.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView unit = View.Units[i];
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
            for (int i = 0; i < View.Units.Count; i++)
            {
                Mimas.Core.Match.UnitView other = View.Units[i];
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
        /// What this attack would do to this body, from what the local player knows — including when the shot
        /// is refused, which is the number behind a "No line of sight" line. On a mirror this keeps the "?"
        /// row, so online the player is never told a guess is a certainty (ADR-026).
        /// </summary>
        private DamageBreakdown PreviewDamage(AttackDef attack, IBody target)
        {
            if (Rules == null || target == null || _localUnitId == None) return null;
            return Rules.PreviewAgainst(LocalPlayer, _localUnitId, attack, target);
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

    }
}
