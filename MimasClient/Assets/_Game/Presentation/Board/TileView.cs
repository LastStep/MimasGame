using UnityEngine;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The view of exactly one board tile: it knows its <see cref="Hex"/> coordinate and how to paint
    /// itself for a <see cref="TileHighlight"/> state. Nothing here decides *which* state applies —
    /// <see cref="BoardView"/> owns that. Colour is pushed through a <see cref="MaterialPropertyBlock"/>
    /// on the URP Lit <c>_BaseColor</c> property so every tile shares one material instance.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [RequireComponent(typeof(MeshRenderer))]
    [RequireComponent(typeof(MeshCollider))]
    public sealed class TileView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Highlight tints")]
        [SerializeField] private Color _reachableTint = new Color(0.26f, 0.68f, 1f, 1f);
        [SerializeField] private Color _pathPreviewTint = new Color(1f, 0.64f, 0.18f, 1f);
        [SerializeField] private Color _hoveredTint = new Color(1f, 0.96f, 0.62f, 1f);

        [Header("Blend strength towards the tint")]
        [Range(0f, 1f)] [SerializeField] private float _reachableBlend = 0.45f;
        [Range(0f, 1f)] [SerializeField] private float _pathPreviewBlend = 0.70f;
        [Range(0f, 1f)] [SerializeField] private float _hoveredBlend = 0.85f;

        [Header("Runtime (set by BoardView)")]
        [SerializeField] private Color _baseColor = Color.grey;

        private MeshRenderer _renderer;
        private MaterialPropertyBlock _block;

        /// <summary>Axial coordinate this tile renders. Assigned once by <see cref="Init"/>.</summary>
        public Hex Coord { get; private set; }

        /// <summary>Current highlight state. Drive it through <see cref="SetHighlight"/>.</summary>
        public TileHighlight Highlight { get; private set; }

        /// <summary>Terrain colour before any highlight blending.</summary>
        public Color BaseColor => _baseColor;

        private void Awake()
        {
            CacheRenderer();
        }

        /// <summary>Binds this view to a coordinate and its terrain colour, and paints it unhighlighted.</summary>
        public void Init(Hex coord, Color baseColor)
        {
            Coord = coord;
            _baseColor = baseColor;
            Highlight = TileHighlight.None;
            ApplyColor();
        }

        /// <summary>Switches highlight state. A no-op when the state is unchanged, so it is cheap to call every frame.</summary>
        public void SetHighlight(TileHighlight state)
        {
            if (Highlight == state) return;
            Highlight = state;
            ApplyColor();
        }

        /// <summary>Replaces the terrain colour (e.g. when a tile effect changes) and repaints.</summary>
        public void SetBaseColor(Color baseColor)
        {
            _baseColor = baseColor;
            ApplyColor();
        }

        private void CacheRenderer()
        {
            if (_renderer == null) _renderer = GetComponent<MeshRenderer>();
        }

        private void ApplyColor()
        {
            CacheRenderer();
            if (_renderer == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();

            _renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, ResolveColor());
            _renderer.SetPropertyBlock(_block);
        }

        private Color ResolveColor()
        {
            switch (Highlight)
            {
                case TileHighlight.Reachable: return Color.Lerp(_baseColor, _reachableTint, _reachableBlend);
                case TileHighlight.PathPreview: return Color.Lerp(_baseColor, _pathPreviewTint, _pathPreviewBlend);
                case TileHighlight.Hovered: return Color.Lerp(_baseColor, _hoveredTint, _hoveredBlend);
                default: return _baseColor;
            }
        }
    }
}
