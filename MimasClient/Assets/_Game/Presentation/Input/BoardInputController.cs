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

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
        }

        private void OnDestroy()
        {
            TileClicked = null;
            TileHovered = null;
            RightClicked = null;
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

            TileView tile = RaycastTile(mouse.position.ReadValue());

            if (tile != _hovered)
            {
                _hovered = tile;
                Action<TileView> hovered = TileHovered;
                if (hovered != null) hovered(tile);
            }

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
