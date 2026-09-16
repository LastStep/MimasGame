#nullable disable

using Mimas.Core.Combat;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// The integer height arithmetic under sight and trajectories. Every number here is hand-computed from the
    /// formulas in docs/data.md so a change to the maths shows up as a failing arithmetic test, not as a
    /// mysteriously different shot.
    /// </summary>
    public class BallisticsTests
    {
        [Fact]
        public void StraightClears_FlatRayOverLowerColumn_Clears()
        {
            // Two ground heroes aiming at 4; a level-1 step (3) halfway between them.
            Assert.True(Ballistics.StraightClears(4, 4, 3, 1, 2));
        }

        [Fact]
        public void StraightClears_GrazeBlocks()
        {
            // The column top touches the ray exactly: a graze blocks (design: #line-of-sight).
            Assert.False(Ballistics.StraightClears(4, 4, 4, 1, 2));
        }

        [Fact]
        public void StraightClears_UphillRay_ClearsColumnTallerThanShooter()
        {
            // Shooter aims at 4, target stands on a level-2 plateau (6 + 4 = 10); a 6-unit column halfway
            // is above the shooter's eye but under the rising ray.
            Assert.True(Ballistics.StraightClears(4, 10, 6, 1, 2));
        }

        [Fact]
        public void ArcHeightScaled_WorkedExample_MatchesTable()
        {
            // h0 = h1 = 4, apex 3, three tiles apart (b = 6): heights x36 at a = 1, 3, 5.
            Assert.Equal(204, Ballistics.ArcHeightScaled(4, 4, 3, 1, 6));
            Assert.Equal(252, Ballistics.ArcHeightScaled(4, 4, 3, 3, 6));
            Assert.Equal(204, Ballistics.ArcHeightScaled(4, 4, 3, 5, 6));
        }

        [Fact]
        public void ArcClears_PeakIsMaxEndpointPlusApex()
        {
            // Mid-flight (a/b = 1/2) the arc reaches max(h0,h1) + apex exactly, level or uphill.
            Assert.Equal(7 * 4, Ballistics.ArcHeightScaled(4, 4, 3, 1, 2));
            Assert.Equal(13 * 4, Ballistics.ArcHeightScaled(4, 10, 3, 1, 2));
        }

        [Fact]
        public void ArcClears_WallAdjacentToShooter_Blocks()
        {
            // A 6-unit wall on the first of three tiles: 216 >= 204.
            Assert.False(Ballistics.ArcClears(4, 4, 3, 6, 1, 6));
        }

        [Fact]
        public void ArcClears_SameWallMidway_Clears()
        {
            // The same wall at mid-flight: 216 < 252.
            Assert.True(Ballistics.ArcClears(4, 4, 3, 6, 3, 6));
        }

        [Fact]
        public void ArcClears_HigherApexClearsWhatLowerDoesNot()
        {
            Assert.False(Ballistics.ArcClears(4, 4, 3, 6, 1, 6));
            Assert.True(Ballistics.ArcClears(4, 4, 8, 6, 1, 6));
        }

        [Fact]
        public void Ballistics_UsesLongArithmetic_NoOverflowAtRange20Height1000()
        {
            // 20 tiles apart is b = 40 samples; columns and endpoints at 1000 units.
            const long h = 1000, apex = 1000, b = 40;
            Assert.Equal(h * b * b + 4 * apex * 39 * (b - 39), Ballistics.ArcHeightScaled(h, h, apex, 39, b));
            Assert.True(Ballistics.ArcClears(h, h, apex, 1000, 39, b));
            Assert.False(Ballistics.ArcClears(h, h, apex, 1100, 39, b));

            // A scale int arithmetic could not carry: top * b * b alone is past int.MaxValue.
            Assert.True(int.MaxValue < 2_000_000L * b * b);
            Assert.False(Ballistics.ArcClears(h, h, apex, 2_000_000, 39, b));
        }
    }
}
