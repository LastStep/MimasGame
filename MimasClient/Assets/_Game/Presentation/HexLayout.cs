using System;
using UnityEngine;
using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Pure math bridge between Core's axial <see cref="Hex"/> coordinates and Unity world space.
    /// Layout is POINTY-TOP on the XZ plane (y is always the caller's concern): a hex's flat sides
    /// face east/west, its points face north/south, +R walks towards +Z. Every entry point takes the
    /// tile size explicitly so nothing here owns board state — <see cref="BoardView"/> does.
    /// No allocation, no Unity object access; safe to call from tests and from edit mode.
    /// </summary>
    public static class HexLayout
    {
        /// <summary>Circumradius (centre to corner) used when a caller has no board to ask. Prefer passing the board's own size.</summary>
        public const float DefaultTileSize = 1f;

        private const float Sqrt3 = 1.7320508f;
        private const float Sqrt3Over3 = 0.57735026f;

        /// <summary>Smallest tile size we will divide by — keeps a misconfigured inspector from producing NaN.</summary>
        private const float MinTileSize = 1e-4f;

        /// <summary>Centre-to-centre distance between two horizontally adjacent tiles (also the flat-to-flat width).</summary>
        public static float TileWidth(float size) => Sqrt3 * size;

        /// <summary>Point-to-point height of a single tile. Rows overlap, so row spacing is 3/4 of this.</summary>
        public static float TileHeight(float size) => 2f * size;

        /// <summary>Centre-to-centre distance between two vertically adjacent rows.</summary>
        public static float RowSpacing(float size) => 1.5f * size;

        /// <summary>Axial hex to world offset on the XZ plane, y = 0.</summary>
        public static Vector3 HexToWorld(Hex hex, float size)
        {
            return new Vector3(Sqrt3 * size * (hex.Q + hex.R * 0.5f), 0f, 1.5f * size * hex.R);
        }

        /// <summary>Axial hex to world offset on the XZ plane at an explicit height.</summary>
        public static Vector3 HexToWorld(Hex hex, float size, float y)
        {
            return new Vector3(Sqrt3 * size * (hex.Q + hex.R * 0.5f), y, 1.5f * size * hex.R);
        }

        /// <summary>
        /// Inverse of <see cref="HexToWorld(Hex,float)"/>: the hex whose centre is nearest to
        /// <paramref name="world"/>. The y component is ignored. Always returns a coordinate — ask the
        /// board whether that coordinate actually exists.
        /// </summary>
        public static Hex WorldToHex(Vector3 world, float size)
        {
            double s = Mathf.Max(size, MinTileSize);
            double q = (Sqrt3Over3 * world.x - world.z / 3.0) / s;
            double r = (2.0 / 3.0 * world.z) / s;
            return Hex.Round(q, r, -q - r);
        }

        /// <summary>
        /// Offset from a tile centre to corner <paramref name="index"/> (0..5), y = 0.
        /// Corner 0 sits at -30°, so corner 2 is the northern point of the pointy-top tile.
        /// Useful for gizmos and outline meshes.
        /// </summary>
        public static Vector3 Corner(int index, float size)
        {
            if (index < 0 || index > 5) throw new ArgumentOutOfRangeException(nameof(index), "Hex corner index must be 0..5.");
            float angle = Mathf.Deg2Rad * (60f * index - 30f);
            return new Vector3(size * Mathf.Cos(angle), 0f, size * Mathf.Sin(angle));
        }
    }
}
