using System;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Contract for time-bounded movement of a unit along a caller-supplied path. The curve maths stay
    /// out of the mover: the caller injects a position evaluator (normalized <c>0..1</c> -&gt; world point),
    /// so a straight tile-to-tile walk, an arc-jump and a dash all reuse one implementation.
    /// Purely cosmetic — the authoritative position always comes from Core / the server.
    /// </summary>
    public interface IUnitMover
    {
        /// <summary>True while a path is being played back.</summary>
        bool IsMoving { get; }

        /// <summary>Raised once the mover reaches the end of its path (also after a zero-duration teleport).</summary>
        event Action Arrived;

        /// <summary>
        /// Plays <paramref name="evaluator"/> (param <c>0..1</c> -&gt; world point) over
        /// <paramref name="duration"/> seconds. A duration &lt;= 0 snaps to the endpoint immediately.
        /// </summary>
        void MoveAlong(Func<float, Vector3> evaluator, float duration);

        /// <summary>Hard snap to a world position, cancelling any playback.</summary>
        void TeleportTo(Vector3 worldPosition);

        /// <summary>Cancels in-flight movement without raising <see cref="Arrived"/>.</summary>
        void Stop();
    }
}
