using System.Collections.Generic;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>The two ways the tilted camera frames the arena (design <c>#camera</c>).</summary>
    public enum ArenaView
    {
        /// <summary>Your end of the arena on the left of the screen, the opponent's on the right.</summary>
        SideOn = 0,

        /// <summary>Past your end of the arena, looking across at the opponent's.</summary>
        BehindYou = 1,
    }

    /// <summary>
    /// The pure maths under <see cref="ArenaCameraRig"/>: where an orbit camera sits, which way each
    /// <see cref="ArenaView"/> faces for a seat, the pan limit and the arena's extent. No Unity objects, so
    /// EditMode tests can pin the framing without a scene.
    /// </summary>
    public static class CameraRigMath
    {
        /// <summary>Squared length under which two ground points count as the same place.</summary>
        private const float Epsilon = 1e-6f;

        /// <summary>Rotation of an orbit camera: <paramref name="yaw"/> about +Y, <paramref name="pitch"/> down from the horizon.</summary>
        public static Quaternion OrbitRotation(float yaw, float pitch) => Quaternion.Euler(pitch, yaw, 0f);

        /// <summary>Position of a camera looking at <paramref name="pivot"/> from <paramref name="distance"/> away.</summary>
        public static Vector3 OrbitPosition(Vector3 pivot, float yaw, float pitch, float distance)
            => pivot - OrbitRotation(yaw, pitch) * Vector3.forward * distance;

        /// <summary>
        /// The yaw that frames the arena as <paramref name="view"/> for the player whose spawn is
        /// <paramref name="home"/>. Behind You looks from home towards <paramref name="away"/>; Side On is a quarter
        /// turn left of that, which puts home on the left of the screen whichever seat it belongs to.
        /// </summary>
        public static float ViewYaw(ArenaView view, Vector3 home, Vector3 away)
        {
            float dx = away.x - home.x;
            float dz = away.z - home.z;
            float behind = dx * dx + dz * dz < Epsilon ? 90f : Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
            return view == ArenaView.BehindYou ? behind : behind - 90f;
        }

        /// <summary>
        /// <paramref name="point"/> pulled onto the disc of <paramref name="radius"/> round <paramref name="centre"/>
        /// on the ground plane. Points already inside come back unchanged; y is always kept.
        /// </summary>
        public static Vector3 ClampToDisc(Vector3 point, Vector3 centre, float radius)
        {
            float r = Mathf.Max(0f, radius);
            float dx = point.x - centre.x;
            float dz = point.z - centre.z;
            float squared = dx * dx + dz * dz;
            if (squared <= r * r) return point;

            float scale = r / Mathf.Sqrt(squared);
            return new Vector3(centre.x + dx * scale, point.y, centre.z + dz * scale);
        }

        /// <summary>
        /// Ground direction for pan input (x = right, y = forward) seen from a camera at <paramref name="yaw"/>.
        /// Diagonals are no faster than straight lines.
        /// </summary>
        public static Vector3 PanDirection(Vector2 input, float yaw)
        {
            Vector3 direction = Quaternion.Euler(0f, yaw, 0f) * new Vector3(input.x, 0f, input.y);
            return direction.sqrMagnitude > 1f ? direction.normalized : direction;
        }

        /// <summary>How far one frame closes the gap to a target: 1 − e^(−sharpness·dt). Sharpness 0 or less snaps.</summary>
        public static float Smoothing(float sharpness, float deltaTime)
            => sharpness <= 0f ? 1f : 1f - Mathf.Exp(-sharpness * Mathf.Max(0f, deltaTime));

        /// <summary>
        /// The arena's centre (the mean of its tile centres) and its radius (the furthest tile centre from it).
        /// An empty list is a point at the origin.
        /// </summary>
        public static void Extent(IReadOnlyList<Vector3> tileCentres, out Vector3 centre, out float radius)
        {
            centre = Vector3.zero;
            radius = 0f;
            if (tileCentres == null || tileCentres.Count == 0) return;

            for (int i = 0; i < tileCentres.Count; i++) centre += tileCentres[i];
            centre /= tileCentres.Count;

            float furthest = 0f;
            for (int i = 0; i < tileCentres.Count; i++)
            {
                float dx = tileCentres[i].x - centre.x;
                float dz = tileCentres[i].z - centre.z;
                furthest = Mathf.Max(furthest, dx * dx + dz * dz);
            }
            radius = Mathf.Sqrt(furthest);
        }
    }
}
