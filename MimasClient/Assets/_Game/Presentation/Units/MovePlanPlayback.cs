using System;
using System.Collections.Generic;
using UnityEngine;
using Mimas.Core.Movement;

namespace Mimas.Client.Presentation
{
    /// <summary>Tunables for how each <see cref="TraversalKind"/> looks. Serializable so a controller can expose them.</summary>
    [Serializable]
    public struct MovePlaybackSettings
    {
        [Tooltip("Seconds a ground walk spends per tile of its path.")]
        [Min(0f)] public float SecondsPerTile;

        [Tooltip("Total seconds for a leap, regardless of distance.")]
        [Min(0f)] public float LeapSeconds;

        [Tooltip("How far above the straight chord the leap's midpoint rises, in world units.")]
        [Min(0f)] public float LeapApex;

        [Tooltip("Total seconds for a blink (vanish, reappear).")]
        [Min(0f)] public float BlinkSeconds;

        public static MovePlaybackSettings Default => new MovePlaybackSettings
        {
            SecondsPerTile = 0.35f,
            LeapSeconds = 0.55f,
            LeapApex = 1.2f,
            BlinkSeconds = 0.3f,
        };
    }

    /// <summary>
    /// Turns a rules-level <see cref="MovePlan"/> into mover playback. The plan says <em>what</em> happened;
    /// this decides how it looks: walks follow the path tile by tile at constant speed, leaps arc from
    /// origin to destination, blinks vanish and reappear. Any controller (skeleton, networked, replay) calls
    /// this and subscribes to <see cref="UnitMover.Arrived"/>; no rules knowledge lives here.
    /// </summary>
    public static class MovePlanPlayback
    {
        private static readonly List<Vector3> Waypoints = new List<Vector3>(16);

        /// <summary>Starts playback of <paramref name="plan"/> on <paramref name="mover"/>. Returns the duration in seconds.</summary>
        public static float Play(UnitMover mover, BoardView board, MovePlan plan, MovePlaybackSettings settings)
        {
            if (mover == null) throw new ArgumentNullException(nameof(mover));
            if (board == null) throw new ArgumentNullException(nameof(board));
            if (plan == null) throw new ArgumentNullException(nameof(plan));

            Vector3 from = board.HexToSurface(plan.Origin);
            Vector3 to = board.HexToSurface(plan.Destination);

            switch (plan.Traversal)
            {
                case TraversalKind.Leap:
                {
                    float duration = settings.LeapSeconds;
                    mover.MoveAlong(UnitMover.BuildArcEvaluator(from, to, settings.LeapApex), duration);
                    return duration;
                }
                case TraversalKind.Blink:
                {
                    float duration = settings.BlinkSeconds;
                    mover.MoveAlong(UnitMover.BuildBlinkEvaluator(from, to), duration);
                    return duration;
                }
                default:
                {
                    Waypoints.Clear();
                    for (int i = 0; i < plan.Path.Count; i++) Waypoints.Add(board.HexToSurface(plan.Path[i]));
                    float duration = settings.SecondsPerTile * (plan.Path.Count - 1);
                    mover.MoveAlong(UnitMover.BuildLinearEvaluator(Waypoints), duration);
                    return duration;
                }
            }
        }
    }
}
