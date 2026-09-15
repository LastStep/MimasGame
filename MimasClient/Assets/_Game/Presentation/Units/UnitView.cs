using System.Collections.Generic;
using UnityEngine;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The view of one unit on the board: where it stands (<see cref="CurrentHex"/>), what it looks like,
    /// and the <see cref="UnitMover"/> that animates it. The visual is a code-built placeholder — a capsule
    /// plus a forward nose so facing is readable — created only when the GameObject has no authored child.
    /// Drop a rigged model in as a child and <see cref="BuildPlaceholderVisual"/> never runs; nothing else
    /// in this class has to change.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UnitMover))]
    public sealed class UnitView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Tint")]
        [SerializeField] private Color _tint = new Color(0.85f, 0.28f, 0.24f, 1f);

        [Tooltip("Optional override material for the placeholder. Leave empty to use Unity's default.")]
        [SerializeField] private Material _visualMaterial;

        [Header("Placeholder shape")]
        [SerializeField] private bool _buildPlaceholderIfEmpty = true;
        [SerializeField] private float _bodyHeight = 0.9f;
        [SerializeField] private float _bodyRadius = 0.32f;

        private readonly List<Renderer> _visualRenderers = new List<Renderer>();
        private UnitMover _mover;
        private MaterialPropertyBlock _block;

        /// <summary>The hex this unit currently occupies for presentation purposes.</summary>
        public Hex CurrentHex { get; private set; }

        /// <summary>The mover that animates this unit. Resolved lazily so it is safe to touch before Awake.</summary>
        public UnitMover Mover
        {
            get
            {
                if (_mover == null) _mover = GetComponent<UnitMover>();
                return _mover;
            }
        }

        /// <summary>Current tint applied to the placeholder renderers.</summary>
        public Color Tint => _tint;

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();

            if (_buildPlaceholderIfEmpty && transform.childCount == 0) BuildPlaceholderVisual();

            CollectRenderers();
            ApplyTint();
        }

        /// <summary>Teleports the unit onto the top face of a tile and records the coordinate.</summary>
        public void SnapTo(Hex hex, BoardView board)
        {
            CurrentHex = hex;
            if (board == null) return;
            Mover.TeleportTo(board.HexToSurface(hex));
        }

        /// <summary>Records the coordinate without moving the transform — use after an animated move landed.</summary>
        public void SetCurrentHex(Hex hex)
        {
            CurrentHex = hex;
        }

        /// <summary>Recolours the placeholder (team colour, selection feedback).</summary>
        public void SetTint(Color tint)
        {
            _tint = tint;
            ApplyTint();
        }

        private void BuildPlaceholderVisual()
        {
            var root = new GameObject("PlaceholderVisual");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);

            // The capsule primitive is 2 units tall and 1 unit across at scale 1.
            GameObject body = CreateUncollidablePrimitive(PrimitiveType.Capsule, "Body", root.transform);
            body.transform.localScale = new Vector3(_bodyRadius * 2f, _bodyHeight * 0.5f, _bodyRadius * 2f);
            body.transform.localPosition = new Vector3(0f, _bodyHeight * 0.5f, 0f);

            GameObject nose = CreateUncollidablePrimitive(PrimitiveType.Cube, "Facing", root.transform);
            nose.transform.localScale = new Vector3(_bodyRadius * 0.45f, _bodyRadius * 0.45f, _bodyRadius * 1.2f);
            nose.transform.localPosition = new Vector3(0f, _bodyHeight * 0.75f, _bodyRadius * 1.15f);
        }

        private GameObject CreateUncollidablePrimitive(PrimitiveType type, string name, Transform parent)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            if (_visualMaterial != null)
            {
                Renderer renderer = go.GetComponent<Renderer>();
                if (renderer != null) renderer.sharedMaterial = _visualMaterial;
            }

            return go;
        }

        private void CollectRenderers()
        {
            _visualRenderers.Clear();
            GetComponentsInChildren(true, _visualRenderers);
        }

        private void ApplyTint()
        {
            if (_block == null) _block = new MaterialPropertyBlock();

            for (int i = 0; i < _visualRenderers.Count; i++)
            {
                Renderer renderer = _visualRenderers[i];
                if (renderer == null) continue;

                renderer.GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, _tint);
                renderer.SetPropertyBlock(_block);
            }
        }
    }
}
