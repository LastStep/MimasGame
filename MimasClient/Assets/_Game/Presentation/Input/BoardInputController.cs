using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Turns raw pointer input into board vocabulary. Every frame it raycasts the cursor against the
    /// tile layer and resolves the hit collider to its <see cref="TileView"/>, then publishes plain C#
    /// events. It holds no selection state and never touches the rules — consumers decide what a click
    /// means. Input System 1.19 polling (no action assets) keeps the WebGL build free of extra plumbing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardInputController : MonoBehaviour
    {
        [Tooltip("Only colliders on these layers are considered board tiles.")]
        [SerializeField] private LayerMask _tileMask = ~0;

        [Tooltip("Colliders on these layers are unit and prop bodies. They share the tile layer today; the component decides, not the layer.")]
        [SerializeField] private LayerMask _bodyMask = ~0;

        [SerializeField] private float _rayDistance = 500f;

        [Tooltip("Leave empty to use Camera.main (re-resolved automatically if it goes away).")]
        [SerializeField] private Camera _camera;

        [Tooltip("The board, used to find the tile a hovered body stands on. Without it only tiles resolve.")]
        [SerializeField] private BoardView _board;

        private BoardHover _hovered;
        private Func<Vector2, bool> _pointerBlocker;

        /// <summary>Left click. Carries null when the click landed off the board.</summary>
        public event Action<TileView> TileClicked;

        /// <summary>Fires only when the tile under the cursor changes. Carries null when the cursor leaves the board.</summary>
        public event Action<TileView> TileHovered;

        /// <summary>Fires when the resolved tile <em>or body</em> under the cursor changes. Carries a default hover off the board.</summary>
        public event Action<BoardHover> HoverChanged;

        /// <summary>Left click, carrying the full resolution (which body was clicked, not just which tile).</summary>
        public event Action<BoardHover> Clicked;

        /// <summary>Right click, anywhere. Conventionally "cancel".</summary>
        public event Action RightClicked;

        /// <summary>The tile currently under the cursor, or null.</summary>
        public TileView HoveredTile => _hovered.Tile;

        /// <summary>What the cursor resolved to this frame: tile plus the body standing on it, if any.</summary>
        public BoardHover Hover => _hovered;

        /// <summary>Cursor position in screen pixels, updated every frame. Zero when there is no mouse.</summary>
        public Vector2 PointerPosition { get; private set; }

        /// <summary>The camera the rays are cast from.</summary>
        public Camera ActiveCamera => _camera;

        /// <summary>True when the overlay claimed the pointer this frame (see <see cref="SetPointerBlocker"/>).</summary>
        public bool PointerOverOverlay { get; private set; }

        /// <summary>
        /// Lets an overlay (the HUD) claim the pointer: while the predicate returns true for the current
        /// screen position no tile is hovered or clicked. Pass null to clear. Presentation cannot reference
        /// the UI assembly, so the overlay installs itself here rather than the other way round.
        /// </summary>
        public void SetPointerBlocker(Func<Vector2, bool> isBlocked)
        {
            _pointerBlocker = isBlocked;
        }

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
        }

        private void OnDestroy()
        {
            TileClicked = null;
            TileHovered = null;
            HoverChanged = null;
            Clicked = null;
            RightClicked = null;
            _pointerBlocker = null;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null) return;

            if (_camera == null)
            {
                _camera = Camera.main;
                if (_camera == null) return;
            }

            Vector2 screenPosition = mouse.position.ReadValue();
            PointerPosition = screenPosition;
            Func<Vector2, bool> blocker = _pointerBlocker;
            bool blocked = blocker != null && blocker(screenPosition);
            PointerOverOverlay = blocked;
            BoardHover hover = blocked ? default : Resolve(screenPosition);

            if (!hover.Same(_hovered))
            {
                _hovered = hover;
                Action<TileView> hovered = TileHovered;
                if (hovered != null) hovered(hover.Tile);
                Action<BoardHover> hoverChanged = HoverChanged;
                if (hoverChanged != null) hoverChanged(hover);
            }

            // A press on the overlay is the overlay's; the board must not also read it as "clicked off the board".
            if (!hover.Any && blocked) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                Action<TileView> clicked = TileClicked;
                if (clicked != null) clicked(hover.Tile);
                Action<BoardHover> clickedHover = Clicked;
                if (clickedHover != null) clickedHover(hover);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                Action rightClicked = RightClicked;
                if (rightClicked != null) rightClicked();
            }
        }

        /// <summary>
        /// One ray, nearest hit, bodies before tiles. Bodies stand on top of the tiles they occupy, so the
        /// nearest collider already is what the player is pointing at; the component on it decides whether that
        /// was a hero, a prop or bare ground. The tile of a hovered body is the tile that body stands on.
        /// </summary>
        private BoardHover Resolve(Vector2 screenPosition)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, _rayDistance, _tileMask | _bodyMask, QueryTriggerInteraction.Ignore)) return default;

            Collider collider = hit.collider;

            UnitView unit = collider.GetComponentInParent<UnitView>();
            if (unit != null)
            {
                TileView under = TileAt(unit.CurrentHex);
                if (under != null) return new BoardHover(under, unit, null);
            }

            PropView prop = collider.GetComponentInParent<PropView>();
            if (prop != null)
            {
                TileView under = TileAt(prop.CurrentHex);
                if (under != null) return new BoardHover(under, null, prop);
            }

            return new BoardHover(collider.GetComponentInParent<TileView>(), null, null);
        }

        private TileView TileAt(Hex hex)
        {
            TileView view;
            return _board != null && _board.TryGetTileView(hex, out view) ? view : null;
        }
    }
}
