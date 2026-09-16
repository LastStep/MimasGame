using System;
using System.Collections.Generic;

namespace Mimas.Core.Geometry
{
    /// <summary>
    /// Axial hex coordinate (q, r) with implicit s = -q - r (cube constraint q + r + s == 0).
    /// Pointy-top or flat-top is a presentation concern; Core only deals in coordinates.
    /// Reference: https://www.redblobgames.com/grids/hexagons/
    /// </summary>
    public readonly struct Hex : IEquatable<Hex>
    {
        public readonly int Q;
        public readonly int R;
        public int S => -Q - R;

        public Hex(int q, int r)
        {
            Q = q;
            R = r;
        }

        public static Hex FromCube(int q, int r, int s)
        {
            if (q + r + s != 0) throw new ArgumentException("Cube coordinates must sum to 0.");
            return new Hex(q, r);
        }

        public static readonly Hex Zero = new Hex(0, 0);

        /// <summary>The six neighbour directions, in Red Blob's order (index 0 = +q).</summary>
        public static readonly Hex[] Directions =
        {
            new Hex(1, 0), new Hex(1, -1), new Hex(0, -1),
            new Hex(-1, 0), new Hex(-1, 1), new Hex(0, 1),
        };

        public static Hex Direction(int i)
        {
            if (i < 0 || i > 5) throw new ArgumentOutOfRangeException(nameof(i), "Direction index must be 0..5.");
            return Directions[i];
        }

        public Hex Neighbor(int direction) => this + Direction(direction);

        public IEnumerable<Hex> Neighbors()
        {
            for (int i = 0; i < 6; i++) yield return this + Directions[i];
        }

        public int Length => (Math.Abs(Q) + Math.Abs(R) + Math.Abs(S)) / 2;

        public static int Distance(Hex a, Hex b) => (a - b).Length;

        public int DistanceTo(Hex other) => Distance(this, other);

        /// <summary>
        /// Squared Euclidean distance between two hex centres, in units of the centre-to-centre spacing.
        /// For a pointy-top layout the world offset of (dq, dr) has |v|^2 = 3 s^2 (dq^2 + dq dr + dr^2), so this
        /// integer is exact and layout-independent. Used for circular range bands (design: #attacks).
        /// </summary>
        public static int EuclideanSquared(Hex a, Hex b)
        {
            int dq = b.Q - a.Q, dr = b.R - a.R;
            return dq * dq + dq * dr + dr * dr;
        }

        /// <summary>Rotate 60° clockwise around the origin.</summary>
        public Hex RotateRight() => new Hex(-S, -Q);

        /// <summary>Rotate 60° counter-clockwise around the origin.</summary>
        public Hex RotateLeft() => new Hex(-R, -S);

        /// <summary>Rotate 180° around the origin (used for rotationally symmetric maps).</summary>
        public Hex Rotate180() => new Hex(-Q, -R);

        /// <summary>All hexes at exactly <paramref name="radius"/> from <paramref name="center"/>. Radius 0 yields the center.</summary>
        public static List<Hex> Ring(Hex center, int radius)
        {
            if (radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            var results = new List<Hex>();
            if (radius == 0)
            {
                results.Add(center);
                return results;
            }

            Hex hex = center + Directions[4] * radius;
            for (int side = 0; side < 6; side++)
            {
                for (int step = 0; step < radius; step++)
                {
                    results.Add(hex);
                    hex = hex.Neighbor(side);
                }
            }
            return results;
        }

        /// <summary>All hexes within <paramref name="radius"/> of <paramref name="center"/> (inclusive), ordered by ring.</summary>
        public static List<Hex> Spiral(Hex center, int radius)
        {
            var results = new List<Hex>();
            for (int k = 0; k <= radius; k++) results.AddRange(Ring(center, k));
            return results;
        }

        /// <summary>
        /// Hexes on the straight line from a to b (inclusive). Uses floating-point lerp + rounding
        /// per Red Blob; only use for line-of-sight style queries, never for rules that must be integer-exact.
        /// </summary>
        public static List<Hex> Line(Hex a, Hex b)
        {
            int n = Distance(a, b);
            var results = new List<Hex>(n + 1);
            if (n == 0)
            {
                results.Add(a);
                return results;
            }

            // Nudge to avoid landing exactly on edges (Red Blob).
            double aq = a.Q + 1e-6, ar = a.R + 1e-6, as_ = a.S - 2e-6;
            double bq = b.Q + 1e-6, br = b.R + 1e-6, bs = b.S - 2e-6;
            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                results.Add(Round(
                    aq + (bq - aq) * t,
                    ar + (br - ar) * t,
                    as_ + (bs - as_) * t));
            }
            return results;
        }

        public static Hex Round(double q, double r, double s)
        {
            int rq = (int)Math.Round(q, MidpointRounding.AwayFromZero);
            int rr = (int)Math.Round(r, MidpointRounding.AwayFromZero);
            int rs = (int)Math.Round(s, MidpointRounding.AwayFromZero);
            double dq = Math.Abs(rq - q), dr = Math.Abs(rr - r), ds = Math.Abs(rs - s);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new Hex(rq, rr);
        }

        public static Hex operator +(Hex a, Hex b) => new Hex(a.Q + b.Q, a.R + b.R);
        public static Hex operator -(Hex a, Hex b) => new Hex(a.Q - b.Q, a.R - b.R);
        public static Hex operator *(Hex a, int k) => new Hex(a.Q * k, a.R * k);
        public static bool operator ==(Hex a, Hex b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(Hex a, Hex b) => !(a == b);

        public bool Equals(Hex other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is Hex h && Equals(h);
        public override int GetHashCode() => unchecked(Q * 397 ^ R);
        public override string ToString() => $"Hex({Q},{R})";
    }
}
