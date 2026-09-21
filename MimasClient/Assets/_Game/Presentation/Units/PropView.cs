using System;
using System.Collections;
using UnityEngine;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The view of one prop — a wall or a pillar (design: #props). Like <see cref="UnitView"/> it is a
    /// code-built placeholder: a coloured <b>hex prism the size of the hex it blocks</b>, <c>bodyHeight</c>
    /// units tall, with a collider so the cursor can pick it, and an <c>AimPoint</c> child at the prop's aim
    /// height so a shot snaps to the same point the rules aim at. Destructible props also get a thin ring at that point, which is the hit mark the player
    /// sees. Props are created from the <see cref="Mimas.Core.Match.PlayerView"/> projection and destroyed
    /// when Core says so; this class owns no rules state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("How far past the hex's corners the hit mark reads, as a fraction of the tile's circumradius.")]
        [SerializeField] private float _hitMarkOvershoot = 1.06f;

        [Tooltip("Seconds the shrink-to-nothing takes when the prop is destroyed.")]
        [SerializeField] private float _destroySeconds = 0.25f;

        private static readonly Color BlockedColor = new Color(1f, 0.353f, 0.290f, 1f);

        private Transform _visual;
        private MaterialPropertyBlock _block;
        private Mesh _bodyMesh;
        private Mesh _hitMarkMesh;
        private GameObject _body;
        private Color _baseColor;
        private bool _blocking;

        /// <summary>Body id from Core — unique across every unit and prop in the match.</summary>
        public int Id { get; private set; }

        /// <summary>The <c>props/*.json</c> id this prop was built from.</summary>
        public string DefId { get; private set; }

        /// <summary>The hex this prop stands on. Props never move.</summary>
        public Hex CurrentHex { get; private set; }

        /// <summary>Where shots land on this prop: the snap point for aiming and the anchor for its hp tag.</summary>
        public Transform AimPoint { get; private set; }

        /// <summary>False for a wall: it blocks shots but can never be targeted.</summary>
        public bool IsDamageable { get; private set; }

        /// <summary>Height of the placeholder body in world units, for anything that wants to sit above it.</summary>
        public float BodyWorldHeight { get; private set; }

        /// <summary>Where shots land on this prop, in Core's height units above the tile top.</summary>
        public int AimHeightUnits { get; private set; }

        /// <summary>How tall this prop stands, in Core's height units above the tile top.</summary>
        public int BodyHeightUnits { get; private set; }

        /// <summary>
        /// Places and builds the placeholder from the projection. Safe to call once per prop, right after the
        /// board is built; <paramref name="worldPerUnit"/> comes from <see cref="BoardView.WorldPerHeightUnit"/>
        /// so one number converts every height on the board.
        /// </summary>
        public void Configure(Mimas.Core.Match.PropView view, BoardView board, float worldPerUnit, Color color)
        {
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (board == null) throw new ArgumentNullException(nameof(board));

            Id = view.Id;
            DefId = view.DefId;
            CurrentHex = view.Position;
            IsDamageable = view.IsDamageable;
            AimHeightUnits = view.AimHeight;
            BodyHeightUnits = view.BodyHeight;
            BodyWorldHeight = view.BodyHeight * worldPerUnit;

            transform.position = board.HexToSurface(view.Position);
            transform.rotation = Quaternion.identity;

            BuildPlaceholder(board.TileSize, BodyWorldHeight, view.AimHeight * worldPerUnit, color);
        }

        /// <summary>
        /// Marks this prop as the thing that stopped the shot currently being aimed, in the aim line's own
        /// blocked colour (T-0006). The tile underneath carries the same mark, but a prop now fills its hex
        /// exactly, so from above the tile is not visible at all: the body has to say it itself.
        /// </summary>
        public void SetBlocking(bool blocking)
        {
            if (_blocking == blocking || _body == null) return;
            _blocking = blocking;
            Tint(_body, blocking ? Color.Lerp(_baseColor, BlockedColor, 0.75f) : _baseColor);
        }

        /// <summary>Shrinks the prop away and destroys it, then calls back. The tile is walkable again the moment Core says so, not when this finishes.</summary>
        public void PlayDestroyed(Action onDone)
        {
            if (!isActiveAndEnabled || _destroySeconds <= 0f)
            {
                if (onDone != null) onDone();
                Destroy(gameObject);
                return;
            }
            StartCoroutine(DestroyRoutine(onDone));
        }

        private IEnumerator DestroyRoutine(Action onDone)
        {
            Collider box = GetComponentInChildren<Collider>();
            if (box != null) box.enabled = false;         // stop picking it the frame it starts falling apart

            Vector3 from = transform.localScale;
            float elapsed = 0f;
            while (elapsed < _destroySeconds)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _destroySeconds);
                transform.localScale = Vector3.Lerp(from, Vector3.zero, t);
                yield return null;
            }

            if (onDone != null) onDone();
            Destroy(gameObject);
        }

        /// <summary>
        /// The placeholder is a hexagonal prism with the tile's own footprint, <c>bodyHeight</c> tall
        /// (T-0006). It was a box at 62 % of the tile, and that was the lie: the rules block a shot with
        /// the whole hex column, so a shot could be refused by a pillar that looked comfortably narrower
        /// than the gap beside it. The same <see cref="HexMeshFactory"/> the board is built from draws it,
        /// so the thing standing on the hex is exactly the hex.
        /// </summary>
        private void BuildPlaceholder(float tileSize, float bodyHeight, float aimHeight, Color color)
        {
            float radius = Mathf.Max(0.05f, tileSize);

            var root = new GameObject("PropVisual");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            _visual = root.transform;

            // The prism's base sits at y = 0 and its top at y = height, so it needs no offset at all.
            _bodyMesh = HexMeshFactory.CreatePrism(radius, bodyHeight, "PropPrism");

            _body = new GameObject("Body");
            _body.layer = gameObject.layer;
            _body.transform.SetParent(root.transform, false);
            _body.AddComponent<MeshFilter>().sharedMesh = _bodyMesh;
            _body.AddComponent<MeshRenderer>();
            _body.AddComponent<MeshCollider>().sharedMesh = _bodyMesh;   // the cursor picks the prop by this
            _baseColor = color;
            Tint(_body, color);

            AimPoint = new GameObject("AimPoint").transform;
            AimPoint.SetParent(transform, false);
            AimPoint.localPosition = new Vector3(0f, aimHeight, 0f);

            if (!IsDamageable) return;

            // The hit mark: a thin band of the same shape, a little wider, at exactly the height shots
            // land on. A cylinder would have said "round" about something that is not.
            const float MarkHeight = 0.03f;
            _hitMarkMesh = HexMeshFactory.CreatePrism(radius * Mathf.Max(1f, _hitMarkOvershoot), MarkHeight, "PropHitMark");

            var mark = new GameObject("HitMark");
            mark.layer = gameObject.layer;
            mark.transform.SetParent(root.transform, false);
            mark.transform.localPosition = new Vector3(0f, aimHeight - (MarkHeight * 0.5f), 0f);
            mark.AddComponent<MeshFilter>().sharedMesh = _hitMarkMesh;
            mark.AddComponent<MeshRenderer>();
            Tint(mark, new Color(1f, 0.86f, 0.62f, 1f));
        }

        private void OnDestroy()
        {
            // The factory caches nothing and the caller owns what it makes.
            if (_bodyMesh != null) Destroy(_bodyMesh);
            if (_hitMarkMesh != null) Destroy(_hitMarkMesh);
        }

        private void Tint(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;

            // A bare MeshRenderer has no material at all, and CreatePrimitive leaves a Built-in-pipeline
            // one behind; either way it draws magenta under URP without this.
            PlaceholderMaterial.Apply(go);
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
