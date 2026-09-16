using System;
using UnityEngine;
using Mimas.Core.Data;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The world-space path a shot takes, built from the same model the rules use (design: #trajectories).
    /// The preview line and the projectile are handed the <em>same</em> evaluator, so what the player was
    /// shown and what then flies are the same curve by construction.
    ///
    /// The rules are integer arithmetic in body units (<c>Mimas.Core.Combat.Ballistics</c>, spec A section 4.7);
    /// this is that arithmetic in floats and world units, for drawing only. Nothing here decides whether a
    /// shot is legal — <c>MatchState.CheckTarget</c> does, and it never sees these numbers.
    /// </summary>
    public static class FlightCurve
    {
        /// <summary>Line samples worth drawing for each trajectory: a straight shot needs two points, an arc needs a curve.</summary>
        public static int Samples(string trajectory)
        {
            if (trajectory == Trajectories.Arc) return 24;
            if (trajectory == Trajectories.Sky) return 12;
            return 2;
        }

        /// <summary>
        /// Builds the evaluator for one shot. <paramref name="from"/> and <paramref name="to"/> are the two aim
        /// points in world space; <paramref name="fromHeightUnits"/> and <paramref name="toHeightUnits"/> are the
        /// same two points in Core's absolute height units (tile top + aim height), which is what the arc's shape
        /// depends on. <paramref name="skyDropUnits"/> is how far above the target a <c>sky</c> shot starts.
        /// </summary>
        public static Func<float, Vector3> Build(string trajectory, Vector3 from, Vector3 to,
            int fromHeightUnits, int toHeightUnits, int apex, float worldPerUnit, int skyDropUnits = 18)
        {
            if (trajectory == Trajectories.Arc) return Arc(from, to, fromHeightUnits, toHeightUnits, apex, worldPerUnit);
            if (trajectory == Trajectories.Sky) return Sky(from, to, skyDropUnits, worldPerUnit);
            if (trajectory != Trajectories.Direct)
            {
                // Core throws on an unknown mode; a hover preview must not, so it draws the honest straight line
                // and says so once in the console rather than inventing a shape.
                Debug.LogWarning("[FlightCurve] Unknown trajectory '" + trajectory + "'; drawing it as a straight shot.");
            }
            return Direct(from, to);
        }

        private static Func<float, Vector3> Direct(Vector3 from, Vector3 to)
        {
            return t => Vector3.Lerp(from, to, Mathf.Clamp01(t));
        }

        /// <summary>
        /// <c>Ballistics.ArcHeightScaled</c> divided through by b², in world units:
        /// <c>y(t) = y0 + (y1 - y0) t + (4 apex + 2 |Δ|) t (1 - t)</c>, with Δ the height difference in units.
        /// Horizontal motion is linear, and both endpoints are exact, so the drawn arc touches the two aim points.
        /// </summary>
        private static Func<float, Vector3> Arc(Vector3 from, Vector3 to, int fromHeightUnits, int toHeightUnits, int apex, float worldPerUnit)
        {
            float bump = (4 * Mathf.Max(0, apex) + 2 * Mathf.Abs(toHeightUnits - fromHeightUnits)) * worldPerUnit;
            return t =>
            {
                float c = Mathf.Clamp01(t);
                Vector3 point = Vector3.Lerp(from, to, c);
                point.y += bump * c * (1f - c);
                return point;
            };
        }

        /// <summary>A cast pause at the caster, then a vertical drop onto the target. Placeholder: no ability ships <c>sky</c> yet.</summary>
        private static Func<float, Vector3> Sky(Vector3 from, Vector3 to, int skyDropUnits, float worldPerUnit)
        {
            const float castFraction = 0.15f;
            Vector3 high = to + Vector3.up * (Mathf.Max(0, skyDropUnits) * worldPerUnit);
            return t =>
            {
                float c = Mathf.Clamp01(t);
                if (c < castFraction) return from;
                return Vector3.Lerp(high, to, (c - castFraction) / (1f - castFraction));
            };
        }
    }
}
