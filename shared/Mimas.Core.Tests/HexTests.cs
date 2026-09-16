using System.Linq;
using Mimas.Core.Geometry;
using Xunit;

namespace Mimas.Core.Tests
{
    public class HexTests
    {
        [Fact]
        public void CubeConstraint_QPlusRPlusS_IsZero()
        {
            var h = new Hex(3, -5);
            Assert.Equal(0, h.Q + h.R + h.S);
        }

        [Theory]
        [InlineData(0, 0, 0, 0, 0)]
        [InlineData(0, 0, 1, 0, 1)]
        [InlineData(0, 0, 3, -3, 3)]
        [InlineData(-2, 1, 2, -1, 4)]
        [InlineData(1, 1, -1, -1, 4)]
        public void Distance_MatchesCubeDistance(int aq, int ar, int bq, int br, int expected)
        {
            Assert.Equal(expected, Hex.Distance(new Hex(aq, ar), new Hex(bq, br)));
        }

        [Fact]
        public void Neighbors_AreSixDistinctHexesAtDistanceOne()
        {
            var center = new Hex(2, -1);
            var n = center.Neighbors().ToList();
            Assert.Equal(6, n.Count);
            Assert.Equal(6, n.Distinct().Count());
            Assert.All(n, h => Assert.Equal(1, Hex.Distance(center, h)));
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(1, 6)]
        [InlineData(2, 12)]
        [InlineData(3, 18)]
        public void Ring_HasSixTimesRadiusHexes(int radius, int expectedCount)
        {
            var ring = Hex.Ring(Hex.Zero, radius);
            Assert.Equal(expectedCount, ring.Count);
            Assert.Equal(expectedCount, ring.Distinct().Count());
            Assert.All(ring, h => Assert.Equal(radius, h.Length));
        }

        [Fact]
        public void Spiral_Radius3_Has37Hexes()
        {
            Assert.Equal(37, Hex.Spiral(Hex.Zero, 3).Count);
        }

        [Fact]
        public void RotateRight_SixTimes_ReturnsToStart()
        {
            var h = new Hex(3, -1);
            var r = h;
            for (int i = 0; i < 6; i++) r = r.RotateRight();
            Assert.Equal(h, r);
        }

        [Fact]
        public void Rotate180_IsRotateRightThreeTimes()
        {
            var h = new Hex(2, 1);
            Assert.Equal(h.RotateRight().RotateRight().RotateRight(), h.Rotate180());
        }

        [Fact]
        public void Line_EndpointsIncluded_LengthIsDistancePlusOne()
        {
            var a = new Hex(-3, 0);
            var b = new Hex(3, -2);
            var line = Hex.Line(a, b);
            Assert.Equal(Hex.Distance(a, b) + 1, line.Count);
            Assert.Equal(a, line.First());
            Assert.Equal(b, line.Last());
            for (int i = 1; i < line.Count; i++)
                Assert.Equal(1, Hex.Distance(line[i - 1], line[i]));
        }

        [Fact]
        public void EuclideanSquared_StraightFive_Is25()
        {
            Assert.Equal(25, Hex.EuclideanSquared(Hex.Zero, new Hex(5, 0)));
        }

        [Fact]
        public void EuclideanSquared_Diagonal33_Is27()
        {
            // Hex distance 6, but the centres are sqrt(27) spacings apart: a circular band reaches further here.
            var far = new Hex(3, 3);
            Assert.Equal(6, Hex.Distance(Hex.Zero, far));
            Assert.Equal(27, Hex.EuclideanSquared(Hex.Zero, far));
        }

        [Fact]
        public void EuclideanSquared_IsSymmetric()
        {
            var a = new Hex(-2, 3);
            foreach (var b in Hex.Spiral(Hex.Zero, 4))
                Assert.Equal(Hex.EuclideanSquared(a, b), Hex.EuclideanSquared(b, a));
        }

        [Fact]
        public void Equality_And_HashCode_AreValueBased()
        {
            Assert.Equal(new Hex(1, 2), new Hex(1, 2));
            Assert.NotEqual(new Hex(1, 2), new Hex(2, 1));
            Assert.Equal(new Hex(1, 2).GetHashCode(), new Hex(1, 2).GetHashCode());
        }
    }
}
