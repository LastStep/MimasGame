using System;
using System.Collections;
using UnityEngine;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The view of one prop — a wall or a pillar (design: #props). Like <see cref="UnitView"/> it is a
    /// code-built placeholder: a coloured box <c>bodyHeight</c> units tall with a collider so the cursor can
    /// pick it, and an <c>AimPoint</c> child at the prop's aim height so a shot snaps to the same point the
    /// rules aim at. Destructible props also get a thin ring at that point, which is the hit mark the player
    /// sees. Props are created from the <see cref="Mimas.Core.Match.PlayerView"/> projection and destroyed
    /// when Core says so; this class owns no rules state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PropView : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("Box footprint as a fraction of the tile's circumradius. Placeholder art; a model replaces the box.")]
        [SerializeField] private float _footprint = 0.62f;

        [Tooltip("Seconds the shrink-to-nothing takes when the prop is destroyed.")]
        [SerializeField] private float _destroySeconds = 0.25f;

        private Transform _visual;
        private MaterialPropertyBlock _block;

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

        private void BuildPlaceholder(float tileSize, float bodyHeight, float aimHeight, Color color)
        {
            float width = Mathf.Max(0.05f, tileSize * _footprint);

            var root = new GameObject("PropVisual");
            root.layer = gameObject.layer;
            root.transform.SetParent(transform, false);
            _visual = root.transform;

            // The cube primitive is 1 unit across at scale 1 and is centred on its own origin.
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.layer = gameObject.layer;
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(width, bodyHeight, width);
            body.transform.localPosition = new Vector3(0f, bodyHeight * 0.5f, 0f);
            Tint(body, color);

            AimPoint = new GameObject("AimPoint").transform;
            AimPoint.SetParent(transform, false);
            AimPoint.localPosition = new Vector3(0f, aimHeight, 0f);

            if (!IsDamageable) return;

            // The hit mark: a thin band around the prop at exactly the height shots land on.
            GameObject ring = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ring.name = "HitMark";
            ring.layer = gameObject.layer;
            ring.transform.SetParent(root.transform, false);
            ring.transform.localScale = new Vector3(width * 1.45f, 0.02f, width * 1.45f);
            ring.transform.localPosition = new Vector3(0f, aimHeight, 0f);
            Collider ringCollider = ring.GetComponent<Collider>();
            if (ringCollider != null) Destroy(ringCollider);
            Tint(ring, new Color(1f, 0.86f, 0.62f, 1f));
        }

        private void Tint(GameObject go, Color color)
        {
            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            if (_block == null) _block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            renderer.SetPropertyBlock(_block);
        }
    }
}
