using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
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
    /// end-to-end loop that proves the presentation layer works: spawn, select, preview a move, play it.
    /// Content comes from the catalogue, movement legality from Core (<see cref="MovementResolverRegistry"/>);
    /// this class only paints options and plays plans. Keys 1..9 pick one of the unit's movement abilities,
    /// Tab cycles. There is no turn structure, no server, no hidden information here: when the networked
    /// match controller lands this class is deleted, and only the seams it exercises survive.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SkeletonMatchController : MonoBehaviour
    {
        [Header("Scene references")]
        [SerializeField] private ContentBootstrap _content;
        [SerializeField] private BoardView _board;
        [SerializeField] private BoardInputController _input;
        [SerializeField] private UnitView _unit;

        [Tooltip("Optional second unit placed on spawn p2 so occupancy rules have something to block on.")]
        [SerializeField] private UnitView _opponent;

        [Header("Placeholder match setup (classes/*.json ids)")]
        [SerializeField] private string _playerClassId = "warrior";
        [SerializeField] private string _opponentClassId = "mage";

        [Header("Playback")]
        [SerializeField] private MovePlaybackSettings _playback = MovePlaybackSettings.Default;

        private readonly List<Hex> _destinations = new List<Hex>();
        private readonly List<MovementDef> _movements = new List<MovementDef>();

        private ContentCatalog _catalog;
        private MovementResolverRegistry _registry;
        private UnitSet _units;
        private Unit _coreUnit;
        private MovementOptions _options = MovementOptions.Empty;

        private int _activeMovement;
        private bool _boardReady;
        private bool _selected;
        private MovePlan _pending;

        /// <summary>True while the unit is selected and its options are painted.</summary>
        public bool IsSelected => _selected;

        /// <summary>The movement ability the next click will use, or null before the match is set up.</summary>
        public MovementDef ActiveMovement => _movements.Count > 0 ? _movements[_activeMovement] : null;

        private void Awake()
        {
            if (_content == null || _board == null || _input == null || _unit == null)
            {
                Debug.LogError("[SkeletonMatchController] _content, _board, _input and _unit must all be assigned.", this);
                enabled = false;
                return;
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
        }

        private void OnDisable()
        {
            if (_board == null || _input == null || _unit == null) return;

            _board.BoardBuilt -= HandleBoardBuilt;
            _input.TileClicked -= HandleTileClicked;
            _input.TileHovered -= HandleTileHovered;
            _input.RightClicked -= HandleRightClicked;
            _unit.Mover.Arrived -= HandleArrived;
        }

        private void Start()
        {
            // BoardView builds in Awake, so the event may already have fired before we subscribed.
            if (_board.IsBuilt) HandleBoardBuilt();
        }

        private void Update()
        {
            if (!_boardReady || _movements.Count == 0) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            int picked = -1;
            if (keyboard.tabKey.wasPressedThisFrame) picked = (_activeMovement + 1) % _movements.Count;
            else if (keyboard.digit1Key.wasPressedThisFrame) picked = 0;
            else if (keyboard.digit2Key.wasPressedThisFrame) picked = 1;
            else if (keyboard.digit3Key.wasPressedThisFrame) picked = 2;
            else if (keyboard.digit4Key.wasPressedThisFrame) picked = 3;
            else if (keyboard.digit5Key.wasPressedThisFrame) picked = 4;

            if (picked >= 0 && picked < _movements.Count) SetActiveMovement(picked);
        }

        /// <summary>Switches the ability used for the next move and repaints if the unit is selected.</summary>
        public void SetActiveMovement(int index)
        {
            if (index < 0 || index >= _movements.Count || index == _activeMovement) return;
            _activeMovement = index;
            Debug.Log("[SkeletonMatchController] Movement: " + Describe(ActiveMovement));
            if (_selected && !_unit.Mover.IsMoving) Select();
        }

        private static string Describe(MovementDef def) => def.Name + " (" + def.Mode + ", range " + def.Range + ")";

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

            ClassDef playerClass;
            if (!_catalog.Classes.TryGet(_playerClassId, out playerClass))
            {
                Debug.LogError("[SkeletonMatchController] Unknown class id '" + _playerClassId + "'.", this);
                return;
            }

            _boardReady = true;

            _units = new UnitSet();
            _coreUnit = new Unit(0, 0, _board.MapData.SpawnP1, playerClass);
            _units.Add(_coreUnit);
            _unit.SnapTo(_coreUnit.Position, _board);

            if (_opponent != null)
            {
                ClassDef opponentClass;
                if (!_catalog.Classes.TryGet(_opponentClassId, out opponentClass)) opponentClass = playerClass;
                var enemy = new Unit(1, 1, _board.MapData.SpawnP2, opponentClass);
                _units.Add(enemy);
                _opponent.SnapTo(enemy.Position, _board);
            }

            // The unit's movement abilities, resolved through the catalogue. Boons will change this list later.
            _movements.Clear();
            for (int i = 0; i < _coreUnit.AbilityIds.Count; i++)
            {
                MovementDef movement = _catalog.GetMovement(_coreUnit.AbilityIds[i]);
                if (movement != null) _movements.Add(movement);
            }
            _activeMovement = 0;

            if (_movements.Count == 0)
            {
                Debug.LogError("[SkeletonMatchController] Class '" + playerClass.Id + "' has no movement abilities.", this);
                return;
            }

            Debug.Log("[SkeletonMatchController] " + playerClass.Name + " with " + _movements.Count + " movement abilities. Active: "
                + Describe(ActiveMovement) + ". Keys 1-" + _movements.Count + " or Tab switch.");
            Deselect();
        }

        private MovementContext BuildContext()
        {
            return MovementContext.For(_board.Map, _board.Terrains, _units, _coreUnit, ActiveMovement);
        }

        private void HandleTileClicked(TileView tile)
        {
            if (!_boardReady || _unit.Mover.IsMoving) return;

            if (tile == null)
            {
                Deselect();
                return;
            }

            Hex coord = tile.Coord;

            if (!_selected)
            {
                if (coord == _coreUnit.Position) Select();
                return;
            }

            if (coord == _coreUnit.Position)
            {
                Deselect();
                return;
            }

            // Validate is the same code the server will run; Enumerate only painted the options.
            MoveResult result = _registry.Validate(BuildContext(), coord);
            if (!result.Ok)
            {
                Debug.Log("[SkeletonMatchController] " + ActiveMovement.Name + " to " + coord + " refused: " + result.Reason);
                Deselect();
                return;
            }

            BeginMove(result.Plan);
        }

        private void HandleTileHovered(TileView tile)
        {
            if (!_boardReady) return;

            _board.SetHovered(tile);

            MovePlan plan;
            if (!_selected || _unit.Mover.IsMoving || tile == null || !_options.TryGet(tile.Coord, out plan))
            {
                _board.ShowPathPreview(null);
                return;
            }

            _board.ShowPathPreview(plan.Path);
        }

        private void HandleRightClicked()
        {
            if (!_boardReady || _unit.Mover.IsMoving) return;
            Deselect();
        }

        private void HandleArrived()
        {
            if (_pending == null) return;

            MovePlan plan = _pending;
            _pending = null;

            _coreUnit.MoveTo(plan.Destination);        // the one rules-state write (moves into Core with MatchState)
            _unit.SnapTo(plan.Destination, _board);    // presentation follows the rules
        }

        private void Select()
        {
            _selected = true;
            _options = _registry.Enumerate(BuildContext());

            _destinations.Clear();
            for (int i = 0; i < _options.Plans.Count; i++) _destinations.Add(_options.Plans[i].Destination);

            _board.HighlightReachable(_destinations);
        }

        private void Deselect()
        {
            _selected = false;
            _options = MovementOptions.Empty;
            _destinations.Clear();
            _board.ClearHighlights();
        }

        private void BeginMove(MovePlan plan)
        {
            Deselect();
            _pending = plan;
            MovePlanPlayback.Play(_unit.Mover, _board, plan, _playback);
        }
    }
}
