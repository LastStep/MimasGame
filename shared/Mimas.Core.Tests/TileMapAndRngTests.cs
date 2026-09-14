using System.Linq;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Xunit;

namespace Mimas.Core.Tests
{
    public class TileMapTests
    {
        private static TileMap Board(int radius)
        {
            var map = new TileMap();
            foreach (var h in Hex.Spiral(Hex.Zero, radius)) map.Add(new Tile(h, "grass"));
            return map;
        }

        [Fact]
        public void ReachableWithin_OnOpenBoard_MatchesHexDistance()
        {
            var map = Board(3);
            var reach = map.ReachableWithin(Hex.Zero, 2);
            Assert.Equal(19, reach.Count);                 // spiral radius 2 = 1 + 6 + 12
            Assert.All(reach, kv => Assert.Equal(kv.Key.Length, kv.Value));
        }

        [Fact]
        public void ReachableWithin_BlockedTiles_AreNotEntered()
        {
            var map = Board(2);
            foreach (var h in Hex.Ring(Hex.Zero, 1)) map[h].Walkable = false;   // wall around the center
            var reach = map.ReachableWithin(Hex.Zero, 5);
            Assert.Single(reach);
            Assert.True(reach.ContainsKey(Hex.Zero));
        }

        [Fact]
        public void RingMap_WithHole_IsRotationallySymmetric()
        {
            var map = new TileMap();
            foreach (var h in Hex.Ring(Hex.Zero, 3)) map.Add(new Tile(h, "stone"));
            Assert.True(map.IsRotationallySymmetric());
            Assert.Equal(18, map.Count);
        }

        [Fact]
        public void Spawns_OnOppositeRingEdges_AreAtMaxDistance()
        {
            var map = new TileMap();
            foreach (var h in Hex.Spiral(Hex.Zero, 3)) map.Add(new Tile(h, "grass"));
            var p1 = new Hex(-3, 0);
            var p2 = p1.Rotate180();
            int max = map.Tiles.Max(t => Hex.Distance(p1, t.Position));
            Assert.Equal(max, Hex.Distance(p1, p2));
        }
    }

    public class RngTests
    {
        [Fact]
        public void SameSeed_ProducesSameSequence()
        {
            var a = new Rng(12345);
            var b = new Rng(12345);
            for (int i = 0; i < 1000; i++) Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void DifferentSeeds_Differ()
        {
            var a = new Rng(1);
            var b = new Rng(2);
            Assert.NotEqual(a.NextUInt(), b.NextUInt());
        }

        [Fact]
        public void Range_StaysWithinBounds()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 10000; i++)
            {
                int v = rng.Range(-3, 4);
                Assert.InRange(v, -3, 3);
            }
        }

        [Fact]
        public void Shuffle_IsAPermutation_AndDeterministic()
        {
            var list1 = Enumerable.Range(0, 20).ToList();
            var list2 = Enumerable.Range(0, 20).ToList();
            new Rng(7).Shuffle(list1);
            new Rng(7).Shuffle(list2);
            Assert.Equal(list1, list2);
            Assert.Equal(Enumerable.Range(0, 20), list1.OrderBy(x => x));
        }
    }
}
