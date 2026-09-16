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

        [Header("Aiming")]
        [Tooltip("Where shots leave from and land on this unit. Left empty, a child named AimPoint is found or created.")]
        [SerializeField] private Transform _aimPoint;

        private readonly List<Renderer> _visualRenderers = new List<Renderer>();
        private UnitMover _mover;
        private UnitFacing _facing;
        private MaterialPropertyBlock _block;
        private Transform _placeholderBody;
        private Transform _placeholderNose;

        /// <summary>The hex this unit currently occupies for presentation purposes.</summary>
        public Hex CurrentHex { get; private set; }

        /// <summary>
        /// The point attacks aim at — Core's <c>aimHeight</c> above this unit's feet, so the drawn line and the
        /// rule's ray share an endpoint. Placed by <see cref="Configure"/>; before that it sits at the feet.
        /// </summary>
        public Transform AimPoint
        {
            get
            {
                if (_aimPoint == null) EnsureAimPoint();
                return _aimPoint;
            }
        }

        /// <summary>The yaw driver for this unit. Resolved lazily, added on demand.</summary>
        public UnitFacing Facing
        {
            get
            {
                if (_facing == null) _facing = GetComponent<UnitFacing>();
                if (_facing == null) _facing = gameObject.AddComponent<UnitFacing>();
                return _facing;
            }
        }

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

        /// <summary>Where shots leave from and land on this unit, in Core's height units above the tile top.</summary>
        public int AimHeightUnits { get; private set; }

        /// <summary>How tall this unit stands, in Core's height units above the tile top.</summary>
        public int BodyHeightUnits { get; private set; }

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();

            if (_buildPlaceholderIfEmpty && transform.childCount == 0) BuildPlaceholderVisual();

            EnsureAimPoint();
            CollectRenderers();
            ApplyTint();
        }

        /// <summary>
        /// Sizes the unit from the rules: the placeholder body becomes <paramref name="bodyHeight"/> height units
        /// tall and the aim point sits at <paramref name="aimHeight"/>, both converted with
        /// <see cref="BoardView.WorldPerHeightUnit"/>. Call it once the <see cref="Mimas.Core.Match.PlayerView"/>
        /// is known; an authored model keeps its own proportions and only gets the aim point moved.
        /// </summary>
        public void Configure(int aimHeight, int bodyHeight, float worldPerUnit)
        {
            if (worldPerUnit <= 0f) return;

            AimHeightUnits = aimHeight;
            BodyHeightUnits = bodyHeight;

            EnsureAimPoint();
            _aimPoint.localPosition = new Vector3(0f, aimHeight * worldPerUnit, 0f);

            // Only the code-built capsule may be rescaled; a real model is authored at its own size.
            if (_placeholderBody == null) return;

            _bodyHeight = bodyHeight * worldPerUnit;
            _placeholderBody.localScale = new Vector3(_bodyRadius * 2f, _bodyHeight * 0.5f, _bodyRadius * 2f);
            _placeholderBody.localPosition = new Vector3(0f, _bodyHeight * 0.5f, 0f);
            if (_placeholderNose != null)
                _placeholderNose.localPosition = new Vector3(0f, _bodyHeight * 0.75f, _bodyRadius * 1.15f);
        }

        private void EnsureAimPoint()
        {
            if (_aimPoint != null) return;
            _aimPoint = transform.Find("AimPoint");
            if (_aimPoint != null) return;
            _aimPoint = new GameObject("AimPoint").transform;
            _aimPoint.SetParent(transform, false);
            _aimPoint.localPosition = new Vector3(0f, _bodyHeight * 0.65f, 0f);
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

            // The capsule primitive is 2 units tall and 1 unit across at scale 1. Its collider stays: the body is
            // what the cursor picks (design: #presentation, hovering any part of a body selects it).
            GameObject body = CreatePrimitive(PrimitiveType.Capsule, "Body", root.transform, true);
            body.transform.localScale = new Vector3(_bodyRadius * 2f, _bodyHeight * 0.5f, _bodyRadius * 2f);
            body.transform.localPosition = new Vector3(0f, _bodyHeight * 0.5f, 0f);
            _placeholderBody = body.transform;

            GameObject nose = CreatePrimitive(PrimitiveType.Cube, "Facing", root.transform, false);
            nose.transform.localScale = new Vector3(_bodyRadius * 0.45f, _bodyRadius * 0.45f, _bodyRadius * 1.2f);
            nose.transform.localPosition = new Vector3(0f, _bodyHeight * 0.75f, _bodyRadius * 1.15f);
            _placeholderNose = nose.transform;
        }

        private GameObject CreatePrimitive(PrimitiveType type, string name, Transform parent, bool keepCollider)
        {
            GameObject go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.layer = gameObject.layer;
            go.transform.SetParent(parent, false);

            Collider collider = go.GetComponent<Collider>();
            if (collider != null && !keepCollider) Destroy(collider);

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
