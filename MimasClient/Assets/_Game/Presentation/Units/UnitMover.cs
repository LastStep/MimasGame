using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Coroutine playback of a unit's movement: samples a caller-supplied path evaluator through a
    /// serialized easing curve and writes <c>transform.position</c> each frame, slerping the yaw towards
    /// the direction of travel. Knows nothing about hexes, turns or reachability — hand it world points.
    /// WebGL-safe: no threads, no allocations per frame beyond the coroutine itself.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UnitMover : MonoBehaviour, IUnitMover
    {
        [Tooltip("Normalized time remap. Identity (linear) is a straight constant-speed walk.")]
        [SerializeField] private AnimationCurve _easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [SerializeField] private bool _faceTravelDirection = true;

        [Tooltip("Higher turns the unit towards its heading faster. Exponential smoothing, frame-rate independent.")]
        [SerializeField] private float _turnSpeed = 14f;

        [Tooltip("Squared horizontal step below which the heading is considered unchanged.")]
        [SerializeField] private float _minTurnDeltaSqr = 0.0004f;

        private Coroutine _routine;

        /// <summary>True while a path is being played back.</summary>
        public bool IsMoving { get; private set; }

        /// <summary>Raised on the frame the unit lands on the final waypoint.</summary>
        public event Action Arrived;

        private void OnDisable()
        {
            Stop();
        }

        private void OnDestroy()
        {
            Arrived = null;
        }

        /// <summary>
        /// Plays <paramref name="evaluator"/> over <paramref name="duration"/> seconds. Cancels any
        /// movement already in flight. A non-positive duration, or a disabled mover, snaps to the endpoint
        /// and raises <see cref="Arrived"/> synchronously.
        /// </summary>
        public void MoveAlong(Func<float, Vector3> evaluator, float duration)
        {
            if (evaluator == null) return;

            Stop();

            if (duration <= 0f || !isActiveAndEnabled)
            {
                TeleportTo(evaluator(1f));
                RaiseArrived();
                return;
            }

            _routine = StartCoroutine(MoveRoutine(evaluator, duration));
        }

        /// <summary>Hard snap, cancelling playback. Does not raise <see cref="Arrived"/>.</summary>
        public void TeleportTo(Vector3 worldPosition)
        {
            Stop();
            transform.position = worldPosition;
        }

        /// <summary>Cancels playback without raising <see cref="Arrived"/>.</summary>
        public void Stop()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            IsMoving = false;
        }

        private IEnumerator MoveRoutine(Func<float, Vector3> evaluator, float duration)
        {
            IsMoving = true;

            Vector3 previous = evaluator(Ease(0f));
            transform.position = previous;

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                Vector3 next = evaluator(Ease(Mathf.Clamp01(elapsed / duration)));
                Face(next - previous);
                transform.position = next;
                previous = next;
                yield return null;
            }

            Vector3 end = evaluator(1f);
            Face(end - previous);
            transform.position = end;

            IsMoving = false;
            _routine = null;
            RaiseArrived();
        }

        private float Ease(float t)
        {
            float clamped = Mathf.Clamp01(t);
            if (_easing == null || _easing.length == 0) return clamped;
            return _easing.Evaluate(clamped);
        }

        private void Face(Vector3 delta)
        {
            if (!_faceTravelDirection) return;

            delta.y = 0f;
            if (delta.sqrMagnitude < _minTurnDeltaSqr) return;

            Quaternion target = Quaternion.LookRotation(delta, Vector3.up);
            float blend = 1f - Mathf.Exp(-_turnSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Slerp(transform.rotation, target, blend);
        }

        private void RaiseArrived()
        {
            Action handler = Arrived;
            if (handler != null) handler();
        }

        /// <summary>
        /// Builds an arc-length-uniform evaluator across a polyline of world points: the unit covers equal
        /// distance per unit of normalized time, so long and short segments do not change its speed.
        /// The points are copied, so the caller may reuse its list.
        /// </summary>
        public static Func<float, Vector3> BuildLinearEvaluator(IReadOnlyList<Vector3> waypoints)
        {
            if (waypoints == null || waypoints.Count == 0) return Constant(Vector3.zero);
            if (waypoints.Count == 1) return Constant(waypoints[0]);

            int count = waypoints.Count;
            Vector3[] points = new Vector3[count];
            for (int i = 0; i < count; i++) points[i] = waypoints[i];

            float[] cumulative = new float[count];
            for (int i = 1; i < count; i++)
            {
                cumulative[i] = cumulative[i - 1] + Vector3.Distance(points[i - 1], points[i]);
            }

            float total = cumulative[count - 1];
            if (total <= Mathf.Epsilon) return Constant(points[count - 1]);

            return t =>
            {
                float clamped = Mathf.Clamp01(t);
                if (clamped <= 0f) return points[0];
                if (clamped >= 1f) return points[count - 1];

                float target = clamped * total;
                int high = count - 1;
                for (int i = 1; i < count; i++)
                {
                    if (cumulative[i] >= target)
                    {
                        high = i;
                        break;
                    }
                }

                int low = high - 1;
                float span = cumulative[high] - cumulative[low];
                float fraction = span <= Mathf.Epsilon ? 0f : (target - cumulative[low]) / span;
                return Vector3.Lerp(points[low], points[high], fraction);
            };
        }

        /// <summary>
        /// A single parabolic hop from <paramref name="from"/> to <paramref name="to"/>: linear in the
        /// horizontal plane, rising <paramref name="apexHeight"/> above the straight chord at the midpoint.
        /// Endpoints are exact, so landing snaps precisely onto the destination surface.
        /// </summary>
        public static Func<float, Vector3> BuildArcEvaluator(Vector3 from, Vector3 to, float apexHeight)
        {
            float apex = apexHeight < 0f ? 0f : apexHeight;
            return t =>
            {
                float clamped = Mathf.Clamp01(t);
                Vector3 point = Vector3.LerpUnclamped(from, to, clamped);
                point.y += 4f * apex * clamped * (1f - clamped);
                return point;
            };
        }

        /// <summary>
        /// Holds at <paramref name="from"/> until <paramref name="switchAt"/> (0..1) of the duration, then sits
        /// at <paramref name="to"/>: a placeholder blink until a vanish/appear effect replaces it.
        /// </summary>
        public static Func<float, Vector3> BuildBlinkEvaluator(Vector3 from, Vector3 to, float switchAt = 0.5f)
        {
            float threshold = Mathf.Clamp01(switchAt);
            return t => t < threshold ? from : to;
        }

        private static Func<float, Vector3> Constant(Vector3 point)
        {
            return _ => point;
        }
    }
}
