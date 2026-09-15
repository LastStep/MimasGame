using System;
using System.Collections.Generic;

namespace Mimas.Core.Geometry
{
    /// <summary>
    /// One sample of an exact hex line: the hex the sample rounds to, plus any other hexes the sample sits
    /// exactly on the boundary of (an edge yields two candidates, a corner three). <see cref="Primary"/> is
    /// chosen by a fixed component order so the same point always yields the same hex.
    /// </summary>
    public readonly struct HexLineSample
    {
        public readonly Hex Primary;
        public readonly Hex Second;
        public readonly Hex Third;

        /// <summary>1, 2 or 3: how many of the candidates are meaningful.</summary>
        public readonly int Count;

        public HexLineSample(Hex primary)
        {
            Primary = primary;
            Second = primary;
            Third = primary;
            Count = 1;
        }

        public HexLineSample(Hex primary, Hex second)
        {
            Primary = primary;
            Second = second;
            Third = primary;
            Count = 2;
        }

        public HexLineSample(Hex primary, Hex second, Hex third)
        {
            Primary = primary;
            Second = second;
            Third = third;
            Count = 3;
        }

        /// <summary>True when the sample lies exactly on a boundary between hexes.</summary>
        public bool IsAmbiguous => Count > 1;

        public Hex this[int index]
        {
            get
            {
                if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
                return index == 0 ? Primary : index == 1 ? Second : Third;
            }
        }
    }

    /// <summary>
    /// Integer-exact straight lines between hex centres, for rules that must agree bit-for-bit on server and
    /// client (jump clearance, line of sight). <see cref="Hex.Line"/> uses doubles and an epsilon nudge and is
    /// only fit for presentation. Here every sample is the exact rational point
    /// <c>(a*(N-i) + b*i) / N</c>, so boundary hits are detected exactly instead of nudged away, and
    /// <c>Trace(a, b)</c> visits the same points as <c>Trace(b, a)</c> in reverse, so line queries are symmetric.
    /// </summary>
    public static class HexLine
    {
        /// <summary>
        /// Every sample from <paramref name="a"/> to <paramref name="b"/> inclusive (<c>Distance + 1</c> samples).
        /// </summary>
        public static List<HexLineSample> Trace(Hex a, Hex b)
        {
            int n = Hex.Distance(a, b);
            var samples = new List<HexLineSample>(n + 1);
            if (n == 0)
            {
                samples.Add(new HexLineSample(a));
                return samples;
            }

            for (int i = 0; i <= n; i++)
            {
                // Exact point scaled by n: coordinates are integers, the true value is x / n.
                long q = (long)a.Q * (n - i) + (long)b.Q * i;
                long r = (long)a.R * (n - i) + (long)b.R * i;
                long s = -q - r;

                int rq = RoundDiv(q, n);
                int rr = RoundDiv(r, n);
                int rs = RoundDiv(s, n);

                long dq = Math.Abs((long)rq * n - q);
                long dr = Math.Abs((long)rr * n - r);
                long ds = Math.Abs((long)rs * n - s);

                long max = Math.Max(dq, Math.Max(dr, ds));
                if (max == 0)
                {
                    // Exactly on a hex centre.
                    samples.Add(new HexLineSample(new Hex(rq, rr)));
                    continue;
                }

                // Every component tied for the largest error is a valid candidate to "reset"; the fixed
                // q, r, s order makes the primary deterministic and independent of line direction.
                bool tq = dq == max, tr = dr == max, ts = ds == max;
                Hex fromQ = new Hex(-rr - rs, rr);
                Hex fromR = new Hex(rq, -rq - rs);
                Hex fromS = new Hex(rq, rr);

                int count = 0;
                Hex c0 = Hex.Zero, c1 = Hex.Zero, c2 = Hex.Zero;
                if (tq) AddCandidate(fromQ, ref count, ref c0, ref c1, ref c2);
                if (tr) AddCandidate(fromR, ref count, ref c0, ref c1, ref c2);
                if (ts) AddCandidate(fromS, ref count, ref c0, ref c1, ref c2);

                samples.Add(count == 1 ? new HexLineSample(c0)
                    : count == 2 ? new HexLineSample(c0, c1)
                    : new HexLineSample(c0, c1, c2));
            }
            return samples;
        }

        /// <summary>The primary hex of every sample, inclusive of both ends: the deterministic single-width line.</summary>
        public static List<Hex> Exact(Hex a, Hex b)
        {
            var samples = Trace(a, b);
            var result = new List<Hex>(samples.Count);
            for (int i = 0; i < samples.Count; i++) AddUnique(result, samples[i].Primary);
            return result;
        }

        /// <summary>
        /// Every hex the line touches, inclusive of both ends: where a sample sits on an edge or corner all the
        /// flanking hexes are included. Use for "does anything in the way block this" checks.
        /// </summary>
        public static List<Hex> Supercover(Hex a, Hex b)
        {
            var samples = Trace(a, b);
            var result = new List<Hex>(samples.Count + 2);
            for (int i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                for (int c = 0; c < sample.Count; c++) AddUnique(result, sample[c]);
            }
            return result;
        }

        /// <summary>Every hex the line touches strictly between the endpoints (supercover minus <paramref name="a"/> and <paramref name="b"/>).</summary>
        public static List<Hex> Grazed(Hex a, Hex b)
        {
            var all = Supercover(a, b);
            var result = new List<Hex>(all.Count);
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] == a || all[i] == b) continue;
                result.Add(all[i]);
            }
            return result;
        }

        /// <summary>Nearest integer to <c>x / n</c> (n &gt; 0); exact halves round towards +infinity.</summary>
        private static int RoundDiv(long x, int n)
        {
            long num = 2 * x + n;
            long den = 2L * n;
            long floor = num >= 0 ? num / den : -((-num + den - 1) / den);
            return (int)floor;
        }

        private static void AddCandidate(Hex h, ref int count, ref Hex c0, ref Hex c1, ref Hex c2)
        {
            if (count > 0 && c0 == h) return;
            if (count > 1 && c1 == h) return;
            if (count == 0) c0 = h;
            else if (count == 1) c1 = h;
            else c2 = h;
            count++;
        }

        private static void AddUnique(List<Hex> list, Hex h)
        {
            for (int i = 0; i < list.Count; i++) if (list[i] == h) return;
            list.Add(h);
        }
    }
}
