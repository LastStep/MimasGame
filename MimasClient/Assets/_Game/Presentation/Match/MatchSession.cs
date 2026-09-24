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
using Mimas.Core.Session;
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

        [Header("Camera")]
        [Tooltip("The player's camera: framed on the arena and on this seat's end of it once the board is built. Optional.")]
        [SerializeField] private ArenaCameraRig _cameraRig;

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

        /// <summary>How long the series result sits before the way out of it appears.</summary>
        private const float BackToLobbyDelaySeconds = 3f;

        /// <summary>How long a "ROUND 2" card stays up before the board is the player's again.</summary>
        private const float RoundCardSeconds = 1.5f;

        /// <summary>How long the round's result holds the screen before the draft cards drop in (P1).</summary>
        private const float DraftRevealDelaySeconds = 2f;

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

        /// <summary>The prop currently wearing the blocked tint, or <see cref="None"/>.</summary>
        private int _blockingPropId = None;
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
        private List<HudBoon> _myBoons = new List<HudBoon>();
        private readonly List<Hex> _highlight = new List<Hex>();
        private readonly List<IBody> _targetScratch = new List<IBody>();
        private int _armed = None;
        private MovementOptions _moveOptions = MovementOptions.Empty;
        private HudExamine _examine;

        /// <summary>
        /// Per enemy unit, the boon ids in the order this client saw them revealed: the plate lists theirs in the
        /// order you learned them (E8). Presentation memory only, lost on reload, where the plate falls back to
        /// grant order (docs/ui/examine.md open question 4).
        /// </summary>
        private readonly Dictionary<int, List<string>> _revealOrder = new Dictionary<int, List<string>>();
        private int _examinedUnitId = None;
        private int _examinedPropId = None;
        private HudPreview _preview;
        private HudUnit _previewUnit;
        private string _cursorTag;
        private bool _ready;

        // The series (ADR-036): the round moments in the band, and the draft over the dimmed board.
        private HudMoment _moment;
        private HudDraft _draft;
        private float _draftOpensAt = -1f;

        /// <summary>When a round card should clear itself, or -1. A result never clears.</summary>
        private float _momentClearsAt = -1f;

        /// <summary>When the series result's way out appears, or -1.</summary>
        private float _backAt = -1f;

        private string _lastRoundHeadline;
        private MatchEndReason _lastReason = MatchEndReason.Elimination;

        private float _ropeSeconds = 10f;

        // ---- IMatchHudSource -------------------------------------------------------------------------

        public IReadOnlyList<HudAction> Actions => _actions;
        public int ActiveActionIndex => _armed;
        public bool IsMyTurn => _ready && View != null && View.IsMyTurn;
        public bool CanEndTurn => CanAct;
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
        public HudDraft Draft => _draft;
        public HudMoment Moment => _moment;
        public string MyName => _driver != null ? _driver.MyName : "You";
        public string OpponentName => _driver != null ? _driver.OpponentName : null;
        public int ScoreMine => Scores.Mine;
        public int ScoreTheirs => Scores.Theirs;
        public int RoundsToWin => _driver != null && _driver.Session != null ? _driver.Session.RoundsToWin : 0;
        public int RoundNumber => _driver != null && _driver.Session != null ? _driver.Session.Round : 0;
        public IReadOnlyList<HudBoon> MyBoons => _myBoons;

        /// <summary>
        /// What the middle of the track says. Draft while the cards are up (or the session is drafting and no
        /// result is still holding the screen), Over once the series result or the lost match is showing.
        /// </summary>
        public HudPhase Phase
        {
            get
            {
                if (_moment != null && (_moment.Kind == HudMomentKind.SeriesResult || _moment.Kind == HudMomentKind.MatchLost)) return HudPhase.Over;
                if (_draft != null) return HudPhase.Draft;
                SessionView session = _driver != null ? _driver.Session : null;
                if (session != null && session.Phase == SessionPhase.Draft && _moment == null) return HudPhase.Draft;
                return HudPhase.Round;
            }
        }

        private (int Mine, int Theirs) Scores
        {
            get
            {
                SessionView session = _driver != null ? _driver.Session : null;
                return session == null ? (0, 0) : HudModel.ScoresBySeat(session.Score0, session.Score1, LocalPlayer);
            }
        }

        /// <summary>Where the way out leads, read when it is shown: the room online (ADR-032), the lobby in practice.</summary>
        private static string BackLabelNow => NetClient.Instance != null && NetClient.Instance.PendingRoom != null
            ? "Back to room"
            : "Back to lobby";
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
        /// <summary>
        /// The local player may arm an action or pick a target right now. Rules is null between rounds and
        /// before the first one has started, and there is nothing to act on then (ADR-036).
        /// </summary>
        private bool CanAct => _ready && Rules != null && !Rules.IsOver && Rules.ActivePlayer == LocalPlayer && !IsPlaying;

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
            // Arming replaces examine; the board is now targeting. The model goes too: leaving it drawn left
            // the plate up over a board that could no longer be clicked, with nothing able to close it.
            _examinedUnitId = None;
            _examinedPropId = None;
            _examine = null;
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

            SessionView session = _driver != null ? _driver.Session : null;
            net.LastResult = new MatchResult
            {
                Won = session != null ? session.Winner == LocalPlayer : Rules != null && Rules.Winner == LocalPlayer,
                Reason = _moment != null ? _moment.Reason : null,
                OpponentName = OpponentName,
                HasScore = session != null,
                ScoreMine = ScoreMine,
                ScoreTheirs = ScoreTheirs,
            };
            net.ForgetMatch();
            SceneManager.LoadScene("Lobby");
        }

        /// <summary>
        /// Closes the plate, whatever put it up: a unit, a prop, or a model with no id behind it (the Editor
        /// preview). Asking only about the ids is what once made a plate impossible to close.
        /// </summary>
        public void CloseExamine()
        {
            if (_examinedUnitId == None && _examinedPropId == None && _examine == null) return;
            _examinedUnitId = None;
            _examinedPropId = None;
            _examine = null;
            RefreshView();
            RaiseStateChanged();
        }

        // ---- the draft (design: #draft; decided 22 Sep 2026, P1) ---------------------------------------

        public void SelectDraftCard(int index)
        {
            if (_draft == null || _draft.Picked) return;
            _draft.Selected = index >= 0 && index < _draft.Cards.Count ? index : -1;
            RaiseStateChanged();
        }

        public void ConfirmDraft()
        {
            if (_draft == null || _draft.Picked || _draft.Selected < 0) return;
            if (_driver == null || !_driver.SubmitDraftPick(_draft.Selected)) return;
            // The pick is only believed once the event comes back; the button locks meanwhile.
            _draft.Picked = true;
            _draft.Status = "Waiting for " + OpponentLabel() + "…";
            RaiseStateChanged();
        }

        private string OpponentLabel()
        {
            string name = _driver != null ? _driver.OpponentName : null;
            return string.IsNullOrEmpty(name) ? "your opponent" : name;
        }

        /// <summary>
        /// Builds the three cards from the session's own offers and dims the board behind them. Every word on
        /// a card is data: the kind, the god (the name up to its first apostrophe), the boon's own line, and
        /// the item in the slot it needs.
        /// </summary>
        private void OpenDraft()
        {
            SessionView session = _driver != null ? _driver.Session : null;
            if (session == null || session.Phase != SessionPhase.Draft) return;

            var draft = new HudDraft
            {
                Headline = _lastRoundHeadline ?? "DRAFT",
                Picked = session.IHavePicked,
                OpponentPicked = session.OpponentHasPicked,
                Selected = -1,
            };

            string nextMap = MapName(session.NextMapId);
            draft.NextRoundLine = "Choose one boon · Round " + (session.Round + 1)
                + (nextMap != null ? " on " + nextMap : "");

            for (int i = 0; i < session.MyOffers.Count; i++)
            {
                BoonDef boon;
                if (!_catalog.Boons.TryGet(session.MyOffers[i], out boon)) continue;
                LineageDef lineage = null;
                if (boon.LineageId != null) _catalog.Lineages.TryGet(boon.LineageId, out lineage);
                draft.Cards.Add(new HudDraftCard
                {
                    Id = boon.Id,
                    Name = boon.Name,
                    Kind = KindName(boon.Kind),
                    God = GodOf(boon.Name),
                    Lineage = LineageName(boon.LineageId),
                    Effect = boon.Description,
                    Attach = AttachLine(boon, session),
                    Icon = boon.Icon,
                    HueDark = lineage != null ? lineage.HueDark : null,
                    HueLight = lineage != null ? lineage.HueLight : null,
                    Slot = boon.Requires != null ? boon.Requires.Slot : null,
                });
            }

            // The opponent may have picked before the cards even dropped in: read it, do not wait for the event.
            draft.Status = draft.Picked ? "Waiting for " + OpponentLabel() + "…"
                : draft.OpponentPicked ? OpponentLabel() + " has picked"
                : null;

            _draft = draft;
            _draftOpensAt = -1f;
            _moment = null;
            _momentClearsAt = -1f;
            Disarm();
            DropExamine();
            RaiseStateChanged();
            Debug.Log("[MatchSession] draft: " + draft.Cards.Count + " cards, " + draft.NextRoundLine);
        }

        /// <summary>
        /// The board goes dark behind the cards through the HUD's own full-screen scrim (§13's default:
        /// no shader change, and it dims the heroes too), so closing the draft is just dropping the model.
        /// </summary>
        private void CloseDraft()
        {
            _draftOpensAt = -1f;
            _draft = null;
        }

        /// <summary>"On you" for a Blessing, "On your Longbow" for anything that needs a slot.</summary>
        private string AttachLine(BoonDef boon, SessionView session)
        {
            if (boon.Requires == null) return "On you";
            string slot = boon.Requires.Slot;
            string itemId = session.MyBuild != null ? session.MyBuild.Loadout.IdForSlot(slot) : null;
            ItemDef item;
            if (itemId != null && _catalog.Items.TryGet(itemId, out item)) return "On your " + item.Name;
            return "On your " + slot;
        }

        /// <summary>The god is the name up to its first apostrophe; a name without one is all god.</summary>
        private static string GodOf(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int apostrophe = name.IndexOf('\'');
            return apostrophe > 0 ? name.Substring(0, apostrophe) : name;
        }

        private static string KindName(string kind)
        {
            if (string.IsNullOrEmpty(kind)) return "";
            return char.ToUpperInvariant(kind[0]) + kind.Substring(1);
        }

        private string LineageName(string lineageId)
        {
            LineageDef lineage;
            if (lineageId == null) return null;
            return _catalog.Lineages.TryGet(lineageId, out lineage) ? lineage.Name : lineageId;
        }

        private string MapName(string mapId)
        {
            MapData map;
            if (mapId == null) return null;
            return _catalog.Maps.TryGet(mapId, out map) ? map.Name : mapId;
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
            _driver.NextRound -= HandleNextRound;
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

            // Escape closes the examine plate (§7.11); nothing else in the match listens for it.
            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame && (_examinedUnitId != None || _examinedPropId != None))
                CloseExamine();

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

            // The way out of the series result appears a beat after it, so the result can land first.
            if (_backAt >= 0f && Time.time >= _backAt)
            {
                _backAt = -1f;
                if (_moment != null)
                {
                    _moment.ShowBack = true;
                    _moment.BackLabel = BackLabelNow;
                }
                RaiseStateChanged();
            }
            // Online the room's state can arrive after the result: the label follows it (ADR-032).
            if (_moment != null && _moment.ShowBack) _moment.BackLabel = BackLabelNow;

            // A round card clears itself; the draft's cards drop in a beat after the round's result.
            if (_momentClearsAt >= 0f && Time.time >= _momentClearsAt)
            {
                _momentClearsAt = -1f;
                _moment = null;
                RaiseStateChanged();
            }
            if (_draftOpensAt >= 0f && Time.time >= _draftOpensAt) OpenDraft();
            if (_draft != null)
            {
                _draft.SecondsRemaining = _driver.TurnSecondsRemaining;
                _draft.SecondsTotal = _driver.TurnSecondsTotal;
            }
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
                    _driver = new LocalMatchDriver(_catalog, _settings, () => IsPlaying);
                    Debug.Log("[MatchSession] local practice: " + _settings.PlayerLoadout.Weapon + " (" + _settings.PlayerLineage + ") vs "
                        + _settings.OpponentLoadout.Weapon + " (" + _settings.OpponentLineage + "), round "
                        + Mathf.Max(1, _driver.Session.Round) + " on " + _driver.Session.MapId + ", seed " + _settings.Seed + ".");
                }
            }
            catch (Exception e) when (e is ArgumentException || e is KeyNotFoundException)
            {
                Debug.LogError("[MatchSession] Match setup refused: " + e.Message, this);
                return;
            }

            // The map comes from the session, not from the round: between rounds there is no round, and the
            // board still has to be built — dimmed, with the draft over it (ADR-036).
            string boardMapId = _driver.Session != null ? _driver.Session.MapId
                : _driver.Rules != null ? _driver.Rules.MapData.Id : null;
            if (boardMapId == null)
            {
                Debug.LogError("[MatchSession] The driver named no map to build.", this);
                return;
            }
            if (!_board.IsBuilt) _board.Build(boardMapId);
            if (!_board.IsBuilt || _board.MapData == null)
            {
                Debug.LogError("[MatchSession] The board could not be built for map '" + boardMapId + "'.", this);
                return;
            }

            // The camera frames the arena from this seat's end: your spawn, by seat, whatever the hero has done since.
            if (_cameraRig != null) _cameraRig.Frame(_board, LocalPlayer);

            _driver.EventsArrived += HandleEventsArrived;
            _driver.Resynced += HandleResynced;
            _driver.StatusChanged += HandleStatusChanged;
            _driver.NextRound += HandleNextRound;

            _unitViews.Clear();
            _propViews.Clear();
            _hudUnits.Clear();
            _hudUnitsById.Clear();
            // Which prefab plays which side is decided by ownership, not by seat number: online you may be
            // seat 1, and your hero should still be the one that looks like yours. Between rounds there are
            // no units to spawn: the board is built and dimmed, and the draft is drawn over it.
            for (int i = 0; View != null && i < View.Units.Count; i++)
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

            // Arriving mid-draft (a reload, or a practice scene that came up between rounds): the board is
            // already built and dimmed, and the cards go straight up (P4).
            SessionView arrived = _driver.Session;
            if (_draft == null && arrived != null && arrived.Phase == SessionPhase.Draft) OpenDraft();

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

        /// <summary>
        /// The action bar's abilities, resolved through the mirror rather than read from the catalogue
        /// (ADR-034): a Sigil's grant and an Enchant's changed cost, range or damage are on the unit's
        /// overlay, and the catalogue's base def knows nothing about either.
        /// </summary>
        private void CollectAbilities()
        {
            HudModel.CollectAbilities(Rules, _localUnitId, _abilities);
        }

        /// <summary>
        /// True when a boon on this unit changes a number the button shows. Asked of the unit's own overlay
        /// rather than of the catalogue, because the overlay is where a boon's effect lives (ADR-034).
        /// </summary>
        internal static bool IsChangedByABoon(Unit unit, string abilityId)
        {
            IReadOnlyList<AbilityOverride> overrides = unit.Overlay.Overrides;
            for (int i = 0; i < overrides.Count; i++)
            {
                if (overrides[i].AbilityId != abilityId) continue;
                string field = overrides[i].Field;
                if (field == AbilityFields.Cost || field == AbilityFields.Range
                    || field == AbilityFields.MinRange || field == AbilityFields.Damage) return true;
            }
            return false;
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

            // Between rounds there is no board to snap: the session block is all there is, and the draft
            // overlay is drawn from it. A reload lands here too — straight back into the draft, with the
            // seconds the server says are left (P4).
            if (View == null)
            {
                SessionView session = _driver.Session;
                if (session != null && session.IsOver) ShowSeriesResultFromView(session);
                else if (session != null && session.Phase == SessionPhase.Draft) OpenDraft();
                RefreshView();
                RaiseStateChanged();
                return;
            }

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

            RefreshView();
            RefreshMarkers();
            FaceNearestEnemies();
            RaiseStateChanged();
            Debug.Log("[MatchSession] resynced: turn " + View.TurnNumber + ", active player " + View.ActivePlayer);
        }

        /// <summary>
        /// A later round of the series began. Reloading the Arena is the same path a match start already
        /// takes: the fresh scene consumes the start waiting in <c>NetClient.PendingMatch</c> and builds the
        /// new round's board. Nothing on a finished round is worth waiting for (ADR-036).
        /// </summary>
        private void HandleNextRound()
        {
            Debug.Log("[MatchSession] next round: reloading the Arena");
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        private void HandleStatusChanged()
        {
            if (!_ready) return;

            // The server no longer has this match. There is no result and no winner, only a way back; say
            // that instead of leaving the player watching a board nothing can move again.
            if ((_moment == null || _moment.Kind == HudMomentKind.RoundStart) && _driver.OpponentStatus == OnlineMatchDriver.LostStatus)
            {
                _moment = HudModel.MatchLost("the server no longer has this match", BackLabelNow);
                _momentClearsAt = -1f;
                Disarm();
                DropExamine();
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

                case BoonRevealedEvent revealed:
                    PlayBoonRevealed(revealed);
                    break;

                case LineageRevealedEvent revealed:
                    PlayLineageRevealed(revealed);
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
                    Disarm();                           // examine stays open across turns (§7.11)
                    RefreshView();
                    RaiseStateChanged();
                    break;

                // A round ending is not the story any more: the RoundEndedEvent right behind it is, and the
                // SessionEndedEvent behind that. All this does is stop the clock and put the board down.
                case MatchEndedEvent _:
                    Disarm();
                    RefreshView();
                    RaiseStateChanged();
                    break;

                case RoundStartedEvent round:
                    PlayRoundStarted(round);
                    break;

                case RoundEndedEvent round:
                    PlayRoundEnded(round);
                    break;

                case DraftStartedEvent _:
                    // The round banner lands first; the cards drop in a beat later.
                    _draftOpensAt = Time.time + DraftRevealDelaySeconds;
                    RaiseStateChanged();
                    break;

                case DraftPickedEvent picked:
                    PlayDraftPicked(picked);
                    break;

                case SessionEndedEvent over:
                    ShowSeriesResult(over);
                    break;
            }
        }

        /// <summary>The round card in the band: the round, the map, who moves first. It clears itself.</summary>
        private void PlayRoundStarted(RoundStartedEvent round)
        {
            CloseDraft();

            string mapName = _board != null && _board.MapData != null ? _board.MapData.Name : MapName(round.MapId);
            _moment = HudModel.RoundStart(round.Round, mapName, round.FirstPlayer, LocalPlayer);
            _backAt = -1f;                                // a round card is not a result; there is no way out of it
            _momentClearsAt = Time.time + RoundCardSeconds;
            RaiseStateChanged();
        }

        /// <summary>
        /// The round's result, without a way out: the series goes on. When the series ended too, the
        /// SessionEndedEvent right behind this replaces the banner before anybody reads it.
        /// </summary>
        private void PlayRoundEnded(RoundEndedEvent round)
        {
            bool won = round.Winner == LocalPlayer;
            (int mine, int theirs) = HudModel.ScoresBySeat(round.Score0, round.Score1, LocalPlayer);
            string winnerName = won ? MyName : OpponentLabel();

            _moment = HudModel.RoundResult(round.Round, round.Winner, LocalPlayer, winnerName, round.Score0, round.Score1,
                ReasonLine(round.Reason, won) + " · the draft opens in a moment");
            _lastRoundHeadline = HudModel.DraftHeadline(round.Round, winnerName, mine, theirs);
            _lastReason = round.Reason;
            _momentClearsAt = -1f;
            _backAt = -1f;
            Disarm();
            DropExamine();
            RefreshView();
            RaiseStateChanged();
            Debug.Log("[MatchSession] round " + round.Round + " over: " + _lastRoundHeadline + " (" + ReasonLine(round.Reason, won) + ")");
        }

        private void PlayDraftPicked(DraftPickedEvent picked)
        {
            if (_draft == null)
            {
                RaiseStateChanged();
                return;
            }
            if (picked.Player == LocalPlayer)
            {
                _draft.Picked = true;
                _draft.Selected = IndexOfCard(picked.BoonId, _draft.Selected);
                _draft.Status = "Waiting for " + OpponentLabel() + "…";
            }
            else
            {
                _draft.OpponentPicked = true;
                _draft.Status = _draft.Picked
                    ? "Waiting for " + OpponentLabel() + "…"
                    : OpponentLabel() + " has picked";
            }
            RaiseStateChanged();
        }

        private int IndexOfCard(string boonId, int fallback)
        {
            if (boonId == null || _draft == null) return fallback;
            for (int i = 0; i < _draft.Cards.Count; i++) if (_draft.Cards[i].Id == boonId) return i;
            return fallback;
        }

        /// <summary>The series result: the only banner in a session that offers a way out.</summary>
        private void ShowSeriesResult(SessionEndedEvent over)
        {
            CloseDraft();
            bool won = over.Winner == LocalPlayer;
            _moment = HudModel.SeriesResult(over.Winner, LocalPlayer, over.Score0, over.Score1, SeriesReasonLine(over.Reason, won), BackLabelNow);
            _momentClearsAt = -1f;
            _backAt = Time.time + BackToLobbyDelaySeconds;
            Disarm();
            DropExamine();
            RefreshView();
            RaiseStateChanged();
            Debug.Log("[MatchSession] series over: " + (won ? "won " : "lost ") + _moment.ScoreMine + " – " + _moment.ScoreTheirs + " (" + _moment.Reason + ").");
        }

        private static string ReasonLine(MatchEndReason reason, bool won)
        {
            switch (reason)
            {
                case MatchEndReason.Resign: return won ? "opponent resigned" : "you resigned";
                case MatchEndReason.Forfeit: return won ? "opponent left" : "you were disconnected";
                default: return "by elimination";
            }
        }

        private static string SeriesReasonLine(SessionEndReason reason, bool won)
        {
            switch (reason)
            {
                case SessionEndReason.Resign: return won ? "opponent resigned" : "you resigned";
                case SessionEndReason.Forfeit: return won ? "opponent left" : "you were disconnected";
                default: return "by elimination";
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

            Disarm();
            RefreshView();
            RaiseStateChanged();
        }

        /// <summary>
        /// The result a reconnect found rather than watched. How it ended matters as much as who won, and the
        /// events that said so are gone — so the reason falls back to the last round's, or to elimination.
        /// </summary>
        private void ShowSeriesResultFromView(SessionView session)
        {
            if (_moment != null && (_moment.Kind == HudMomentKind.SeriesResult || _moment.Kind == HudMomentKind.MatchLost)) return;
            bool won = session.Winner == LocalPlayer;
            _moment = HudModel.SeriesResult(session.Winner, LocalPlayer, session.Score0, session.Score1, ReasonLine(_lastReason, won), BackLabelNow);
            _momentClearsAt = -1f;
            DropExamine();
            _backAt = Time.time + BackToLobbyDelaySeconds;
            Debug.Log("[MatchSession] series found over on resync: player " + session.Winner + " wins " + _moment.ScoreMine + " – " + _moment.ScoreTheirs + ".");
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

            // The attacker's own resolved def: an Enchant that lengthened the shot changed the curve it flew,
            // and the catalogue's base def would draw the wrong one (ADR-034).
            var def = ResolveFor(attack.AttackerId, attack.AbilityId) as AttackDef;
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
                        Headline = "−" + damage,
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
        /// <summary>
        /// A boon showed itself. The flyover names it over the unit it belongs to, the nameplate gains a
        /// marker, and the examine panel stops saying "Unknown boon" (design: #hidden-info).
        /// </summary>
        private void PlayBoonRevealed(BoonRevealedEvent revealed)
        {
            BoonDef boon;
            bool known = _catalog.Boons.TryGet(revealed.BoonId, out boon);
            _revealedThisBatch.Add(revealed.BoonId);
            List<string> order;
            if (!_revealOrder.TryGetValue(revealed.UnitId, out order)) _revealOrder[revealed.UnitId] = order = new List<string>();
            if (!order.Contains(revealed.BoonId)) order.Add(revealed.BoonId);
            RaiseFlyover(revealed.UnitId,
                "Revealed · " + (known ? boon.Name : revealed.BoonId),
                known ? LineageName(boon.LineageId) + " · " + KindName(boon.Kind) : null,
                known ? boon.Kind : null);
            RefreshView();
            RefreshMarkers();
            RefreshExamine();
            RaiseStateChanged();
        }

        private void PlayLineageRevealed(LineageRevealedEvent revealed)
        {
            LineageDef lineage;
            string name = _catalog.Lineages.TryGet(revealed.LineageId, out lineage) ? lineage.Name : revealed.LineageId;

            HudUnit hud;
            if (_hudUnitsById.TryGetValue(revealed.UnitId, out hud)) hud.LineageTag = name;
            RaiseFlyover(revealed.UnitId, "Lineage revealed · " + name, null, lineage != null ? ExamineModelBuilder.EmblemOf(lineage) : null);
            RefreshView();
            RefreshExamine();
            RaiseStateChanged();
        }

        private void RaiseFlyover(int unitId, string headline, string detail, string glyph)
        {
            Action<HudFlyover> handler = Flyover;
            if (handler == null) return;
            UnitView view;
            Vector3 at = _unitViews.TryGetValue(unitId, out view) && view != null
                ? view.transform.position + new Vector3(0f, _overlayHeight, 0f)
                : Vector3.zero;
            handler(new HudFlyover { UnitId = unitId, WorldPosition = at, Headline = headline, Detail = detail, Glyph = glyph });
        }

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
            RefreshMyBoons();
        }

        /// <summary>Your boons for the column down the right edge, as the plate lists them. Between rounds there is no view: keep the last.</summary>
        private void RefreshMyBoons()
        {
            if (View == null || _localUnitId == None) return;
            _myBoons = ExamineModelBuilder.BoonsOf(_catalog, View, _localUnitId, null);
        }

        private void RefreshMarkers()
        {
            if (View == null) return;
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                Mimas.Core.Match.UnitView unit = View.FindUnit(hud.Id);
                if (unit == null) continue;

                // The lineage's tag and the boon marks are read from the view rather than only from the events,
                // so a reconnect shows what was learned while we were away.
                if (unit.LineageId != null)
                {
                    LineageDef lineage;
                    hud.LineageTag = _catalog.Lineages.TryGet(unit.LineageId, out lineage) ? lineage.Name : unit.LineageId;
                }

                // Theirs carry one mark per boon, seen or not (docs/ui/hud.md §3.5).
                if (!hud.IsProp) HudModel.BoonMarks(_catalog, unit, hud.BoonMarks);
            }
        }

        /// <summary>The bar, each action carrying the plate's tile for it and its flags (spec H §7.2, ADR-040).</summary>
        private void RebuildActions()
        {
            HudModel.BuildActions(_catalog, View, Rules, _localUnitId, _abilities, CanAct, _actions, DescribeAttackRules);
        }

        private void RefreshEmphasis()
        {
            bool armed = _armed != None;
            for (int i = 0; i < _hudUnits.Count; i++)
            {
                HudUnit hud = _hudUnits[i];
                hud.Emphasised = armed || hud == _previewUnit || hud.Id == _examinedUnitId;
                hud.Examined = hud.Id == _examinedUnitId || hud.Id == _examinedPropId;
            }
        }

        /// <summary>
        /// Rebuilds the examine plate's model from the view (spec E §7). The builder makes its own mirror of the
        /// unit from the view, so the plate cannot show a boon this seat has not been shown even in practice,
        /// where <see cref="Rules"/> is the truth.
        /// </summary>
        private void RefreshExamine()
        {
            if (_examinedPropId != None)
            {
                _examine = ExamineModelBuilder.BuildProp(_catalog, View, Rules, _examinedPropId);
                if (_examine != null) return;
                _examinedPropId = None;            // it was destroyed while the panel was open
            }

            Mimas.Core.Match.UnitView unit = _examinedUnitId == None || View == null ? null : View.FindUnit(_examinedUnitId);
            if (unit == null || !unit.IsAlive)
            {
                _examinedUnitId = None;            // the plate closes itself when its unit dies (§7.11)
                _examine = null;
                return;
            }

            List<string> order;
            _revealOrder.TryGetValue(unit.Id, out order);
            _examine = ExamineModelBuilder.BuildUnit(_catalog, View, Rules, unit.Id, SeatName(unit.IsMine), order);
        }

        /// <summary>"You" and "Random Bot" in practice (§13); online, this seat's name and the opponent's.</summary>
        private string SeatName(bool mine)
        {
            if (mine) return MyName;
            string theirs = OpponentName;
            return string.IsNullOrEmpty(theirs) ? "Opponent" : theirs;
        }

        /// <summary>
        /// The plate gives way to the round's result and to the draft, which take the screen, and does not come
        /// back after them (§7.11).
        /// </summary>
        private void DropExamine()
        {
            _examinedUnitId = None;
            _examinedPropId = None;
            _examine = null;
        }

        /// <summary>One unit's ability as it really is: gear plus every boon on it (ADR-034). Null when it has none.</summary>
        private AbilityDef ResolveFor(int unitId, string abilityId)
        {
            Unit unit;
            AbilityDef def;
            if (Rules == null || abilityId == null || !Rules.Units.TryGet(unitId, out unit)) return null;
            return Rules.ResolveAbility(unit, abilityId, out def) ? def : null;
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

            // Elements come first: what a shot carries is the first thing that decides whether it lands at
            // all, against an immunity or a rider (design #elements).
            string line = travel + "  ·  " + sight + "  ·  " + range;
            for (int i = attack.Elements.Count - 1; i >= 0; i--) line = Capitalised(attack.Elements[i]) + "  ·  " + line;
            return line;
        }

        private static string Capitalised(string word)
        {
            if (string.IsNullOrEmpty(word)) return word;
            return char.ToUpperInvariant(word[0]) + word.Substring(1);
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

            // A click on the board while the plate is open only closes it (docs/ui/examine.md §2): it
            // neither selects what it hit nor examines another unit. Clicks on the HUD close it from the
            // view's side and still do their own thing.
            if (_armed == None && _examine != null)
            {
                CloseExamine();
                return;
            }

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
            _examinedUnitId = Rules != null && Rules.Units.TryGetUnitAt(coord, out clicked) ? clicked.Id : None;
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
                // cursor — but there is nothing left to aim at, so the line, the blocker and the tag go.
                if (_aimPreview != null) _aimPreview.Hide();
                ClearBlocker();
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
            ClearBlocker();            // this hover answers for itself; only a refusal puts it back
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

                    // The X marks where the shot stops; the tint marks the hex the rules blamed. They are
                    // often not the same picture: a grazing line is blocked by a pillar beside it, not on
                    // it (design #line-of-sight rule 5), which is what "refused where it looks open" was.
                    if (check.HasBlockedAt)
                    {
                        _board.SetBlocker(check.BlockedAt);
                        MarkBlockingProp(check.BlockedAt);
                    }

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

            bool isProp = !(target is Unit);
            var preview = new HudPreview
            {
                TargetUnitId = target.Id,
                TargetIsProp = isProp,
                AbilityName = attack.Name,
                AbilityIcon = attack.Icon,
                TargetName = TargetName(target),
                TrajectoryWord = HudModel.TrajectoryWord(attack.Trajectory),
                Total = breakdown.Total,
                IsExact = breakdown.IsExact,
                BlockedReason = blockedReason,
            };
            for (int i = 0; i < breakdown.Lines.Count; i++)
            {
                DamageLine line = breakdown.Lines[i];
                preview.Lines.Add(new HudPreviewLine { Label = DescribeLine(line, isProp), Amount = line.Amount });
            }
            HudModel.AddHiddenLine(preview.Lines, breakdown.UnknownCount);

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
            ClearBlocker();
            _cursorTag = null;
        }

        /// <summary>
        /// Says which hex stopped the shot, in both places it can be seen: the tile, and — when something
        /// is standing on it — the body itself, because a prop fills its hex and hides the tile under it.
        /// </summary>
        private void MarkBlockingProp(Hex hex)
        {
            foreach (KeyValuePair<int, PropView> pair in _propViews)
            {
                if (pair.Value == null || pair.Value.CurrentHex != hex) continue;
                pair.Value.SetBlocking(true);
                _blockingPropId = pair.Key;
                return;
            }
        }

        private void ClearBlocker()
        {
            if (_board != null) _board.ClearBlocker();
            if (_blockingPropId == None) return;

            PropView prop;
            if (_propViews.TryGetValue(_blockingPropId, out prop) && prop != null) prop.SetBlocking(false);
            _blockingPropId = None;
        }

        /// <summary>The seat's name for a hero, the prop's own name for a prop: the preview's "on …" line.</summary>
        private string TargetName(IBody target)
        {
            var unit = target as Unit;
            if (unit != null) return SeatName(unit.Owner == LocalPlayer);
            Mimas.Core.Match.PropView prop = View != null ? View.FindProp(target.Id) : null;
            PropDef def;
            return prop != null && _catalog.Props.TryGet(prop.DefId, out def) ? def.Name : "it";
        }

        /// <summary>
        /// The rules' own lines in the preview's words (docs/ui/hud.md §5): Base, Strength or Magic, their armour
        /// in the lane, then each known modifier by name.
        /// </summary>
        private string DescribeLine(DamageLine line, bool targetIsProp)
        {
            switch (line.Kind)
            {
                case DamageLineKind.Base: return "Base";
                case DamageLineKind.Power:
                {
                    string type = StatBlock.DamageTypeOf(line.Id);
                    return type == AbilityCategories.Weapon ? "Strength" : type == AbilityCategories.Spell ? "Magic" : Capitalise(type) + " power";
                }
                case DamageLineKind.Defense:
                    return (targetIsProp ? "Its armour · " : "Their armour · ") + StatBlock.DamageTypeOf(line.Id);

                // A Blessing's strength is its own line, and it says which god's (ADR-035).
                case DamageLineKind.BoonStat:
                {
                    BoonDef boon;
                    return _catalog.Boons.TryGet(line.Id, out boon) ? boon.Name : line.Id;
                }

                // An immunity does not reduce the damage, it cancels it: the line says which element.
                case DamageLineKind.Nullify:
                {
                    ModifierDef nullify;
                    string element = _catalog.Modifiers.TryGet(line.Id, out nullify) && nullify.Elements.Count > 0
                        ? nullify.Elements[0]
                        : null;
                    return element != null ? "Immune (" + element + ")" : "Immune";
                }

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
