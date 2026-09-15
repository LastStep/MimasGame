using System;
using System.Collections.Generic;
using UnityEngine;
using Mimas.Client.Content;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Movement;
using Mimas.Core.Units;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// SKELETON scaffolding, not the real match loop. It wires board + input + two units into the smallest
    /// end-to-end loop that proves the presentation layer works: arm an action, preview, play it, pass the
    /// turn, examine a unit. Content comes from the catalogue, movement legality from Core
    /// (<see cref="MovementResolverRegistry"/>); this class only paints options and plays plans.
    /// It feeds the HUD through <see cref="IMatchHudSource"/> with a local fake of the alternating turn
    /// cycle (ADR-015): a turn length from <see cref="MatchSettings"/>, a rope for the last seconds, an
    /// idle-timeout penalty, End Turn to pass early, and an opponent that picks a random legal move.
    /// Nothing is armed by default; the board only targets while an action is armed. There is no server
    /// and no real hidden information here (the opponent's used abilities count as revealed): when the
    /// networked match controller lands this class is deleted and only the seams it exercises survive.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkeletonMatchController : MonoBehaviour, IMatchHudSource
    {
        [Header("Match setup")]
        [Tooltip("Map, classes, clock and opponent knobs. Assets/_Game/Settings/DefaultMatchSettings.asset.")]
        [SerializeField] private MatchSettings _settings;

        [Header("Scene references")]
        [SerializeField] private ContentBootstrap _content;
        [SerializeField] private BoardView _board;
        [SerializeField] private BoardInputController _input;
        [SerializeField] private UnitView _unit;

        [Tooltip("Optional second unit placed on spawn p2. It takes random legal moves on its turn.")]
        [SerializeField] private UnitView _opponent;

        [Header("Playback")]
        [SerializeField] private MovePlaybackSettings _playback = MovePlaybackSettings.Default;

        private const int None = -1;

        private readonly List<Hex> _destinations = new List<Hex>();
        private readonly List<MovementDef> _movements = new List<MovementDef>();
        private readonly List<MovementDef> _opponentMovements = new List<MovementDef>();
        private readonly List<HudAction> _actions = new List<HudAction>();
        private readonly List<MovePlan> _scratchPlans = new List<MovePlan>();
        private readonly HashSet<string> _revealedOpponentAbilities = new HashSet<string>(StringComparer.Ordinal);

        private ContentCatalog _catalog;
        private MovementResolverRegistry _registry;
        private UnitSet _units;
        private Unit _coreUnit;
        private Unit _coreOpponent;
        private ClassDef _playerClass;
        private ClassDef _opponentClass;
        private MovementOptions _options = MovementOptions.Empty;

        private int _armed = None;
        private bool _boardReady;
        private MovePlan _pending;
        private MovePlan _opponentPending;
        private HudExamine _examine;
        private Unit _examinedUnit;

        // Fake turn cycle state.
        private float _turnSeconds = 30f;
        private float _ropeSeconds = 10f;
        private float _idleTurnSeconds = 7f;
        private float _opponentThinkSeconds = 1.5f;
        private float _turnTotal;
        private float _turnRemaining;
        private bool _isMyTurn;
        private bool _movedThisTurn;
        private bool _penaltyPending;
        private bool _penalised;
        private int _turnNumber;
        private float _opponentPhaseTimer;
        private bool _opponentActed;

        /// <summary>The movement ability the board is targeting, or null when nothing is armed.</summary>
        public MovementDef ArmedMovement => _armed >= 0 && _armed < _movements.Count ? _movements[_armed] : null;

        /// <summary>The local player may arm an action or pick a destination right now.</summary>
        private bool CanAct => _boardReady && _isMyTurn && !_movedThisTurn && !_unit.Mover.IsMoving;

        // ---- IMatchHudSource -------------------------------------------------------------------------

        public IReadOnlyList<HudAction> Actions => _actions;
        public int ActiveActionIndex => _armed;
        public bool IsMyTurn => _isMyTurn;
        public bool CanEndTurn => _boardReady && _isMyTurn && !_unit.Mover.IsMoving;
        public int TurnNumber => _turnNumber;
        public float TurnSecondsRemaining => _turnRemaining > 0f ? _turnRemaining : 0f;
        public float TurnSecondsTotal => _turnTotal;
        public float RopeSeconds => _ropeSeconds;
        public HudExamine Examine => _examine;

        public event Action StateChanged;

        public void SelectAction(int index)
        {
            if (!CanAct) return;
            if (index < 0 || index >= _movements.Count || index == _armed)
            {
                Disarm();
                RaiseStateChanged();
                return;
            }

            _armed = index;
            _examinedUnit = null;                        // arming replaces examine; the board is now targeting
            Debug.Log("[SkeletonMatchController] Armed " + Describe(ArmedMovement));
            PaintOptions();
            RaiseStateChanged();
        }

        public void EndTurn()
        {
            if (!CanEndTurn) return;
            Debug.Log("[SkeletonMatchController] Turn " + _turnNumber + " ended by the player.");
            PassTurn();
        }

        public void CloseExamine()
        {
            if (_examinedUnit == null) return;
            _examinedUnit = null;
            RaiseStateChanged();
        }

        // ---- lifecycle ------------------------------------------------------------------------------

        private void Awake()
        {
            if (_content == null || _board == null || _input == null || _unit == null)
            {
                Debug.LogError("[SkeletonMatchController] _content, _board, _input and _unit must all be assigned.", this);
                enabled = false;
                return;
            }

            if (_settings == null)
            {
                Debug.LogWarning("[SkeletonMatchController] No MatchSettings assigned; using built-in defaults.", this);
                _settings = ScriptableObject.CreateInstance<MatchSettings>();
            }

            _registry = MovementResolverRegistry.CreateDefault();
        }

        private void OnEnable()
        {
            if (_board == null || _input == null || _unit == null) return;

            _board.BoardBuilt += HandleBoardBuilt;
            _input.TileClicked += HandleTileClicked;
            _input.TileHovered += HandleTileHovered;
            _input.RightClicked += HandleRightClicked;
            _unit.Mover.Arrived += HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived += HandleOpponentArrived;
        }

        private void OnDisable()
        {
            if (_board == null || _input == null || _unit == null) return;

            _board.BoardBuilt -= HandleBoardBuilt;
            _input.TileClicked -= HandleTileClicked;
            _input.TileHovered -= HandleTileHovered;
            _input.RightClicked -= HandleRightClicked;
            _unit.Mover.Arrived -= HandleArrived;
            if (_opponent != null) _opponent.Mover.Arrived -= HandleOpponentArrived;
        }

        private void OnDestroy()
        {
            StateChanged = null;
        }

        private void Start()
        {
            // BoardView builds in Awake, so the event may already have fired before we subscribed.
            if (_board.IsBuilt) HandleBoardBuilt();
        }

        private void Update()
        {
            if (!_boardReady) return;

            _turnRemaining -= Time.deltaTime;

            if (_isMyTurn)
            {
                if (_turnRemaining > 0f) return;
                if (_unit.Mover.IsMoving)
                {
                    // A move that started in time may finish; the turn passes on arrival.
                    _turnRemaining = 0f;
                    return;
                }
                if (_movedThisTurn)
                {
                    Debug.Log("[SkeletonMatchController] Turn " + _turnNumber + " ended by the clock.");
                }
                else
                {
                    Debug.Log("[SkeletonMatchController] Turn " + _turnNumber + " skipped: the clock ran out before any action.");
                    _penaltyPending = _idleTurnSeconds > 0f;
                }
                PassTurn();
                return;
            }

            TickOpponentTurn();
        }

        // ---- setup ----------------------------------------------------------------------------------

        private void HandleBoardBuilt()
        {
            if (_boardReady) return;
            if (_board.MapData == null)
            {
                Debug.LogError("[SkeletonMatchController] Board reported built but has no map data.", this);
                return;
            }

            _catalog = _content.EnsureLoaded();
            if (_catalog == null) return;

            if (!_catalog.Classes.TryGet(_settings.PlayerClassId, out _playerClass))
            {
                Debug.LogError("[SkeletonMatchController] Unknown class id '" + _settings.PlayerClassId + "'.", this);
                return;
            }

            _boardReady = true;

            _units = new UnitSet();
            _coreUnit = new Unit(0, 0, _board.MapData.SpawnP1, _playerClass);
            _units.Add(_coreUnit);
            _unit.SnapTo(_coreUnit.Position, _board);

            if (_opponent != null)
            {
                if (!_catalog.Classes.TryGet(_settings.OpponentClassId, out _opponentClass)) _opponentClass = _playerClass;
                _coreOpponent = new Unit(1, 1, _board.MapData.SpawnP2, _opponentClass);
                _units.Add(_coreOpponent);
                _opponent.SnapTo(_coreOpponent.Position, _board);
                CollectMovements(_coreOpponent, _opponentMovements);
            }

            // The unit's movement abilities, resolved through the catalogue. Boons will change this list later.
            CollectMovements(_coreUnit, _movements);
            _armed = None;

            if (_movements.Count == 0)
            {
                Debug.LogError("[SkeletonMatchController] Class '" + _playerClass.Id + "' has no movement abilities.", this);
                return;
            }

            _turnSeconds = _settings.ResolveTurnSeconds(_catalog);
            _ropeSeconds = Mathf.Min(_settings.RopeSeconds, _turnSeconds);
            _idleTurnSeconds = _settings.IdleTurnSeconds;
            _opponentThinkSeconds = _settings.OpponentThinkSeconds;

            Debug.Log("[SkeletonMatchController] " + _playerClass.Name + " with " + _movements.Count + " movement abilities, "
                + _turnSeconds + " s per turn, rope at " + _ropeSeconds + " s.");

            StartMyTurn();
        }

        private void CollectMovements(Unit unit, List<MovementDef> into)
        {
            into.Clear();
            for (int i = 0; i < unit.AbilityIds.Count; i++)
            {
                MovementDef movement = _catalog.GetMovement(unit.AbilityIds[i]);
                if (movement != null) into.Add(movement);
            }
        }

        private static string Describe(MovementDef def) => def.Name + " (" + def.Mode + ", range " + def.Range + ")";

        // ---- turn cycle (fake, local) ---------------------------------------------------------------

        private void StartMyTurn()
        {
            _turnNumber++;
            _isMyTurn = true;
            _movedThisTurn = false;
            _penalised = _penaltyPending;
            _penaltyPending = false;
            _turnTotal = _penalised ? _idleTurnSeconds : _turnSeconds;
            _turnRemaining = _turnTotal;
            if (_penalised) Debug.Log("[SkeletonMatchController] Idle penalty: turn " + _turnNumber + " starts with " + _idleTurnSeconds + " s until you act.");
            Disarm();
            RaiseStateChanged();
        }

        private void PassTurn()
        {
            Disarm();
            _examinedUnit = null;
            _isMyTurn = false;
            _penalised = false;
            _turnTotal = _turnSeconds;
            _turnRemaining = _turnTotal;
            _opponentPhaseTimer = _opponentThinkSeconds;
            _opponentActed = false;
            RaiseStateChanged();
        }

        private void TickOpponentTurn()
        {
            bool opponentMoving = _opponent != null && _opponent.Mover.IsMoving;

            if (_turnRemaining <= 0f)
            {
                if (!opponentMoving) StartMyTurn();
                return;
            }

            if (opponentMoving) return;

            _opponentPhaseTimer -= Time.deltaTime;
            if (_opponentPhaseTimer > 0f) return;

            if (!_opponentActed)
            {
                _opponentActed = true;
                _opponentPhaseTimer = _opponentThinkSeconds;
                OpponentMove();
                return;
            }

            StartMyTurn();
        }

        /// <summary>A random legal move. Client-side placeholder only; the real bot runs on the server through Core's Rng.</summary>
        private void OpponentMove()
        {
            if (_opponent == null || _coreOpponent == null) return;

            _scratchPlans.Clear();
            MovementDef chosenDef = null;
            for (int d = 0; d < _opponentMovements.Count; d++)
            {
                MovementOptions options = _registry.Enumerate(
                    MovementContext.For(_board.Map, _board.Terrains, _units, _coreOpponent, _opponentMovements[d]));
                if (options.Plans.Count == 0) continue;
                // The first ability with any option wins; good enough for a placeholder that only has to move.
                chosenDef = _opponentMovements[d];
                for (int i = 0; i < options.Plans.Count; i++) _scratchPlans.Add(options.Plans[i]);
                break;
            }

            if (_scratchPlans.Count == 0)
            {
                Debug.Log("[SkeletonMatchController] Opponent has no legal move and passes.");
                return;
            }

            MovePlan plan = _scratchPlans[UnityEngine.Random.Range(0, _scratchPlans.Count)];
            _scratchPlans.Clear();
            Debug.Log("[SkeletonMatchController] Opponent uses " + chosenDef.Name + " to " + plan.Destination + ".");
            bool newlyRevealed = _revealedOpponentAbilities.Add(chosenDef.Id);   // hidden info: seen once, known forever
            _opponentPending = plan;
            MovePlanPlayback.Play(_opponent.Mover, _board, plan, _playback);
            if (newlyRevealed && _examine != null) RaiseStateChanged();
        }

        private void HandleOpponentArrived()
        {
            if (_opponentPending == null) return;
            MovePlan plan = _opponentPending;
            _opponentPending = null;
            _coreOpponent.MoveTo(plan.Destination);
            _opponent.SnapTo(plan.Destination, _board);
        }

        private void RaiseStateChanged()
        {
            RebuildActions();
            RefreshExamine();
            Action handler = StateChanged;
            if (handler != null) handler();
        }

        private void RebuildActions()
        {
            _actions.Clear();
            bool enabled = CanAct;
            for (int i = 0; i < _movements.Count; i++)
            {
                MovementDef def = _movements[i];
                _actions.Add(new HudAction(def.Id, def.Name, def.Description, def.Icon, def.Category, enabled));
            }
        }

        // ---- examine --------------------------------------------------------------------------------

        private void ExamineUnit(Unit unit)
        {
            _examinedUnit = unit;
        }

        /// <summary>Rebuilds the examine payload from current state so newly revealed abilities show up.</summary>
        private void RefreshExamine()
        {
            if (_examinedUnit == null)
            {
                _examine = null;
                return;
            }

            bool mine = _examinedUnit == _coreUnit;
            ClassDef cls = mine ? _playerClass : _opponentClass;
            var examine = new HudExamine
            {
                Title = cls != null ? cls.Name : _examinedUnit.ClassId,
                Subtitle = mine ? "Your unit" : "Opponent",
                Description = cls != null ? cls.Description : null,
            };

            for (int i = 0; i < _examinedUnit.AbilityIds.Count; i++)
            {
                string id = _examinedUnit.AbilityIds[i];
                bool hidden = !mine && !_revealedOpponentAbilities.Contains(id);
                AbilityDef def;
                bool known = _catalog.Abilities.TryGet(id, out def);
                examine.Abilities.Add(new HudExamineEntry
                {
                    Name = hidden ? "Unknown ability" : (known ? def.Name : id),
                    Description = hidden ? "Revealed once the opponent uses it." : (known ? def.Description : null),
                    Icon = hidden || !known ? null : def.Icon,
                    Hidden = hidden,
                });
            }

            _examine = examine;
        }

        // ---- local player input ---------------------------------------------------------------------

        private MovementContext BuildContext()
        {
            return MovementContext.For(_board.Map, _board.Terrains, _units, _coreUnit, ArmedMovement);
        }

        private void HandleTileClicked(TileView tile)
        {
            if (!_boardReady) return;

            if (tile == null)
            {
                bool changed = _armed != None || _examinedUnit != null;
                Disarm();
                _examinedUnit = null;
                if (changed) RaiseStateChanged();
                return;
            }

            Hex coord = tile.Coord;

            if (_armed != None)
            {
                if (!CanAct) return;
                if (coord == _coreUnit.Position)
                {
                    Disarm();
                    RaiseStateChanged();
                    return;
                }

                // Validate is the same code the server will run; Enumerate only painted the options.
                MoveResult result = _registry.Validate(BuildContext(), coord);
                if (!result.Ok)
                {
                    Debug.Log("[SkeletonMatchController] " + ArmedMovement.Name + " to " + coord + " refused: " + result.Reason);
                    return;                            // stay armed; the player just missed
                }

                BeginMove(result.Plan);
                return;
            }

            // Nothing armed: clicks on units open examine, anything else closes it.
            if (coord == _coreUnit.Position) ExamineUnit(_coreUnit);
            else if (_coreOpponent != null && coord == _coreOpponent.Position) ExamineUnit(_coreOpponent);
            else _examinedUnit = null;
            RaiseStateChanged();
        }

        private void HandleTileHovered(TileView tile)
        {
            if (!_boardReady) return;

            _board.SetHovered(tile);

            MovePlan plan;
            if (_armed == None || _unit.Mover.IsMoving || tile == null || !_options.TryGet(tile.Coord, out plan))
            {
                _board.ShowPathPreview(null);
                return;
            }

            _board.ShowPathPreview(plan.Path);
        }

        private void HandleRightClicked()
        {
            if (!_boardReady) return;
            bool changed = _armed != None || _examinedUnit != null;
            Disarm();
            _examinedUnit = null;
            if (changed) RaiseStateChanged();
        }

        private void HandleArrived()
        {
            if (_pending == null) return;

            MovePlan plan = _pending;
            _pending = null;

            _coreUnit.MoveTo(plan.Destination);        // the one rules-state write (moves into Core with MatchState)
            _unit.SnapTo(plan.Destination, _board);    // presentation follows the rules

            if (_isMyTurn && _turnRemaining <= 0f)
            {
                Debug.Log("[SkeletonMatchController] Turn " + _turnNumber + " ended by the clock.");
                PassTurn();
                return;
            }
            RaiseStateChanged();
        }

        private void PaintOptions()
        {
            _options = _registry.Enumerate(BuildContext());

            _destinations.Clear();
            for (int i = 0; i < _options.Plans.Count; i++) _destinations.Add(_options.Plans[i].Destination);

            _board.HighlightReachable(_destinations);
        }

        private void Disarm()
        {
            _armed = None;
            _options = MovementOptions.Empty;
            _destinations.Clear();
            _board.ClearHighlights();
            _board.ShowPathPreview(null);
        }

        private void BeginMove(MovePlan plan)
        {
            Disarm();
            _examinedUnit = null;
            _pending = plan;
            _movedThisTurn = true;                       // one move per turn until the action economy is decided

            if (_penalised)
            {
                // Hearthstone rule: acting during a penalty turn restores the full turn length.
                _penalised = false;
                _turnTotal = _turnSeconds;
                _turnRemaining = _turnSeconds;
            }

            MovePlanPlayback.Play(_unit.Mover, _board, plan, _playback);
            RaiseStateChanged();
        }
    }
}
