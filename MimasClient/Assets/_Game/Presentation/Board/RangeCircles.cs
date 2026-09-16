using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Draws the armed attack's range band on the board: two true circles with the tiles outside them dimmed
    /// (design: #presentation, #attacks). Ranges are Euclidean, so the band is an exact annulus and the only
    /// honest way to draw it is a circle — which is why it is distance math in the tile shader
    /// (<c>Mimas/HexTile</c>) rather than a decal or a ring mesh: it conforms to steps and plateaus by
    /// construction and needs nothing WebGL2 cannot do.
    /// This component only pushes uniforms; it owns no state the rules care about.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RangeCircles : MonoBehaviour
    {
        private static readonly int CenterId = Shader.PropertyToID("_RangeCenter");
        private static readonly int MinId = Shader.PropertyToID("_RangeMin");
        private static readonly int MaxId = Shader.PropertyToID("_RangeMax");
        private static readonly int LineId = Shader.PropertyToID("_RangeLine");
        private static readonly int ColorId = Shader.PropertyToID("_RangeColor");
        private static readonly int DimId = Shader.PropertyToID("_RangeDim");

        [Tooltip("The board whose tile material carries the circles. Empty uses the BoardView on this object.")]
        [SerializeField] private BoardView _board;

        [Tooltip("Ring colour. The targetable red, desaturated, so the lines read without shouting.")]
        [SerializeField] private Color _ringColor = new Color(1f, 0.6902f, 0.6588f, 1f);

        [Tooltip("Half width of a ring line in world units.")]
        [SerializeField] private float _lineHalfWidth = 0.05f;

        [Tooltip("Brightness multiplier for tiles outside the band.")]
        [Range(0f, 1f)] [SerializeField] private float _dim = 0.55f;

        private bool _shown;

        private void Awake()
        {
            if (_board == null) _board = GetComponent<BoardView>();
        }

        private void OnDisable()
        {
            Hide();
        }

        /// <summary>
        /// Lights the band between the two radii, centred on the attacker. Pass 0 for
        /// <paramref name="minRadius"/> to draw only the outer circle.
        /// </summary>
        public void Show(Vector3 worldCentre, float minRadius, float maxRadius)
        {
            Material material = Material;
            if (material == null) return;

            material.SetVector(CenterId, new Vector4(worldCentre.x, worldCentre.y, worldCentre.z, 1f));
            material.SetFloat(MinId, minRadius);
            material.SetFloat(MaxId, maxRadius);
            material.SetFloat(LineId, _lineHalfWidth);
            material.SetColor(ColorId, _ringColor);
            material.SetFloat(DimId, _dim);
            _shown = true;
        }

        /// <summary>Clears the band. Cheap and idempotent — the session may call it on every disarm.</summary>
        public void Hide()
        {
            if (!_shown) return;
            _shown = false;

            Material material = Material;
            if (material == null) return;
            material.SetVector(CenterId, Vector4.zero);
        }

        /// <summary>The runtime tile material the board built its tiles with, or null before the board is built.</summary>
        private Material Material => _board != null ? _board.TileMaterial : null;
    }
}
