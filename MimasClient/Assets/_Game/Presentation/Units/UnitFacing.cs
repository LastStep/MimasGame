using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Turns a unit to look at something: the snapped aim point while an attack is armed, the nearest living
    /// enemy otherwise (design: #presentation). Facing is <b>local presentation only</b> — it never enters a
    /// command, an event or the wire, so it tells the opponent nothing about what this player is considering.
    /// While <see cref="UnitMover"/> is playing a path it owns the yaw; this component stands back and picks
    /// the new heading up from wherever the walk finished.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitFacing : MonoBehaviour
    {
        [Tooltip("Turn rate. High enough that the hero reads as snapping to the cursor without teleporting.")]
        [SerializeField] private float _degreesPerSecond = 540f;

        /// <summary>Heading changes smaller than this are ignored, so a jittery cursor does not re-aim every frame.</summary>
        private const float MinAngle = 0.5f;

        private UnitMover _mover;
        private Transform _follow;
        private Quaternion _target;
        private bool _hasTarget;

        private void Awake()
        {
            _mover = GetComponent<UnitMover>();
            _target = transform.rotation;
        }

        /// <summary>Faces a fixed world point once. Replaces any transform being followed.</summary>
        public void FaceWorldPoint(Vector3 point)
        {
            _follow = null;
            SetTargetFromPoint(point);
        }

        /// <summary>Keeps facing <paramref name="target"/> as it moves, until another call replaces it.</summary>
        public void FaceTransform(Transform target)
        {
            _follow = target;
            if (target != null) SetTargetFromPoint(target.position);
        }

        /// <summary>Stops turning and keeps the current yaw (nothing left to look at).</summary>
        public void ClearTarget()
        {
            _follow = null;
            _hasTarget = false;
        }

        private void Update()
        {
            // The mover slerps towards the direction of travel; two drivers fighting over one yaw looks broken.
            if (_mover != null && _mover.IsMoving)
            {
                _target = transform.rotation;
                return;
            }

            if (_follow != null) SetTargetFromPoint(_follow.position);
            if (!_hasTarget) return;

            transform.rotation = Quaternion.RotateTowards(transform.rotation, _target, _degreesPerSecond * Time.deltaTime);
        }

        private void SetTargetFromPoint(Vector3 point)
        {
            Vector3 delta = point - transform.position;
            delta.y = 0f;
            if (delta.sqrMagnitude < 1e-6f) return;

            Quaternion want = Quaternion.LookRotation(delta, Vector3.up);
            if (_hasTarget && Quaternion.Angle(_target, want) < MinAngle) return;
            _target = want;
            _hasTarget = true;
        }
    }
}
