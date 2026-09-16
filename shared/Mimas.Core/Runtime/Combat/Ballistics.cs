using System;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// The height arithmetic behind sight and trajectories (design: #line-of-sight, #trajectories). Every method
    /// is integer-only and cross-multiplied: a flight from <c>h0</c> at t = 0 to <c>h1</c> at t = 1 is sampled at
    /// the rational parameter <c>a / b</c> and compared with a column top by multiplying out the denominator, so
    /// server and client agree bit for bit and no float ever touches a rule. Intermediates are <c>long</c>
    /// because the arc scales by <c>b²</c>.
    /// </summary>
    public static class Ballistics
    {
        /// <summary>Height of the straight line from h0 (t=0) to h1 (t=1) at t = a/b, scaled by b.</summary>
        public static long StraightHeightScaled(long h0, long h1, long a, long b) => h0 * (b - a) + h1 * a;

        /// <summary>True when a column of height top does NOT reach the straight line at t = a/b (a graze blocks).</summary>
        public static bool StraightClears(long h0, long h1, long top, long a, long b)
            => top * b < StraightHeightScaled(h0, h1, a, b);

        /// <summary>
        /// Height of the arc at t = a/b, scaled by b². The arc is the straight line plus a symmetric bump that is
        /// zero at both ends and peaks so the flight reaches max(h0,h1) + apex at t = 1/2:
        ///   height(t) = h0 + (h1-h0) t + (4 apex + 2 |h1-h0|) t (1-t).
        /// </summary>
        public static long ArcHeightScaled(long h0, long h1, long apex, long a, long b)
            => h0 * b * b + (h1 - h0) * a * b + (4 * apex + 2 * Math.Abs(h1 - h0)) * a * (b - a);

        /// <summary>True when a column of height top does NOT reach the arc at t = a/b (a graze blocks).</summary>
        public static bool ArcClears(long h0, long h1, long apex, long top, long a, long b)
            => top * b * b < ArcHeightScaled(h0, h1, apex, a, b);
    }
}
