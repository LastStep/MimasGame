using System;

namespace Mimas.Core
{
    /// <summary>
    /// Deterministic PRNG (xoshiro128**). Every random decision in a match goes through the match's Rng,
    /// so server and client (and replays) produce identical results from the same seed and command sequence.
    /// Never use System.Random or Guid in rules code.
    /// </summary>
    public sealed class Rng
    {
        private uint _s0, _s1, _s2, _s3;

        public Rng(uint seed)
        {
            // SplitMix32 to expand the seed into 4 non-zero state words.
            uint x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            _s2 = SplitMix(ref x);
            _s3 = SplitMix(ref x);
            if ((_s0 | _s1 | _s2 | _s3) == 0) _s0 = 1;
        }

        private static uint SplitMix(ref uint x)
        {
            x += 0x9E3779B9u;
            uint z = x;
            z = (z ^ (z >> 16)) * 0x85EBCA6Bu;
            z = (z ^ (z >> 13)) * 0xC2B2AE35u;
            return z ^ (z >> 16);
        }

        private static uint Rotl(uint x, int k) => (x << k) | (x >> (32 - k));

        public uint NextUInt()
        {
            uint result = Rotl(_s1 * 5, 7) * 9;
            uint t = _s1 << 9;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 11);
            return result;
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentException("maxExclusive must be > minInclusive.");
            uint span = (uint)(maxExclusive - minInclusive);
            // Rejection sampling to avoid modulo bias.
            uint limit = uint.MaxValue - (uint.MaxValue % span);
            uint r;
            do r = NextUInt(); while (r >= limit);
            return minInclusive + (int)(r % span);
        }

        /// <summary>Returns true with probability percent/100 (integer percent 0..100).</summary>
        public bool Chance(int percent) => Range(0, 100) < percent;

        /// <summary>In-place Fisher–Yates shuffle.</summary>
        public void Shuffle<T>(System.Collections.Generic.IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T tmp = list[i];
                list[i] = list[j];
                list[j] = tmp;
            }
        }
    }
}
