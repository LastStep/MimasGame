using System;
using UnityEngine;
using UnityEngine.InputSystem;

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

        [SerializeField] private float _rayDistance = 500f;

        [Tooltip("Leave empty to use Camera.main (re-resolved automatically if it goes away).")]
        [SerializeField] private Camera _camera;

        private TileView _hovered;
        private Func<Vector2, bool> _pointerBlocker;

        /// <summary>Left click. Carries null when the click landed off the board.</summary>
        public event Action<TileView> TileClicked;

        /// <summary>Fires only when the tile under the cursor changes. Carries null when the cursor leaves the board.</summary>
        public event Action<TileView> TileHovered;

        /// <summary>Right click, anywhere. Conventionally "cancel".</summary>
        public event Action RightClicked;

        /// <summary>The tile currently under the cursor, or null.</summary>
        public TileView HoveredTile => _hovered;

        /// <summary>The camera the rays are cast from.</summary>
        public Camera ActiveCamera => _camera;

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
            Func<Vector2, bool> blocker = _pointerBlocker;
            TileView tile = blocker != null && blocker(screenPosition) ? null : RaycastTile(screenPosition);

            if (tile != _hovered)
            {
                _hovered = tile;
                Action<TileView> hovered = TileHovered;
                if (hovered != null) hovered(tile);
            }

            // A press on the overlay is the overlay's; the board must not also read it as "clicked off the board".
            if (tile == null && blocker != null && blocker(screenPosition)) return;

            if (mouse.leftButton.wasPressedThisFrame)
            {
                Action<TileView> clicked = TileClicked;
                if (clicked != null) clicked(tile);
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                Action rightClicked = RightClicked;
                if (rightClicked != null) rightClicked();
            }
        }

        private TileView RaycastTile(Vector2 screenPosition)
        {
            Ray ray = _camera.ScreenPointToRay(screenPosition);
            RaycastHit hit;
            if (!Physics.Raycast(ray, out hit, _rayDistance, _tileMask, QueryTriggerInteraction.Ignore)) return null;
            return hit.collider.GetComponentInParent<TileView>();
        }
    }
}
