#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Combat;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// Hand-built boards for the aiming rules: everything is a straight row along +q unless a test says
    /// otherwise, so the sample parameters in the comments can be checked by hand against docs/data.md.
    /// Heights are map <em>levels</em>; the fixtures convert through <see cref="TestHeights.Default"/>
    /// (1 level = 3 units, hero body 6, aim 4).
    /// </summary>
    internal static class AimFixtures
    {
        internal static readonly HeightsDef H = TestHeights.Default;

        internal static readonly PropDef Wall = new PropDef("wall", "Wall", null, null, 6, 3, StatBlock.Empty);

        internal static readonly PropDef Pillar = new PropDef("pillar", "Stone Pillar", null, null, 6, 3,
            new StatBlock(new[] { new KeyValuePair<string, int>(StatBlock.HpKey, 10) }));

        /// <summary>A row of grass tiles (0,0)..(n-1,0) at the given levels.</summary>
        internal static TileMap Row(params int[] levels)
        {
            var map = new TileMap();
            for (int i = 0; i < levels.Length; i++) map.Add(new Tile(new Hex(i, 0), "grass", levels[i]));
            return map;
        }

        /// <summary>The same row with one tile replaced by unwalkable stone.</summary>
        internal static TileMap RowWithStone(int length, int stoneAt)
        {
            var map = new TileMap();
            for (int i = 0; i < length; i++)
            {
                var tile = new Tile(new Hex(i, 0), i == stoneAt ? "stone" : "grass");
                tile.Walkable = i != stoneAt;
                map.Add(tile);
            }
            return map;
        }

        /// <summary>An open grass field of the given radius, every tile at level 0.</summary>
        internal static TileMap Field(int radius)
        {
            var map = new TileMap();
            foreach (var h in Hex.Spiral(Hex.Zero, radius)) map.Add(new Tile(h, "grass"));
            return map;
        }

        internal static BodySet Bodies(params Unit[] units)
        {
            var set = new UnitSet();
            for (int i = 0; i < units.Length; i++) set.Add(units[i]);
            return new BodySet(set);
        }

        internal static Unit Hero(int id, int owner, Hex at) => new Unit(id, owner, at, H);

        /// <summary>Absolute aim height at a hex for a body of the default proportions.</summary>
        internal static int Aim(TileMap map, Hex hex) => Sight.AimHeightAt(map, H, hex, H.Aim);

        /// <summary>The ray between two hexes, both occupied by heroes whose own bodies are ignored.</summary>
        internal static bool Sees(TileMap map, BodySet bodies, Hex from, Hex to, int ignoreA, int ignoreB, out Hex blockedAt)
            => Sight.IsClear(map, bodies, H, from, Aim(map, from), to, Aim(map, to), ignoreA, ignoreB, out blockedAt);

        internal static bool Sees(TileMap map, BodySet bodies, Hex from, Hex to)
        {
            Hex unused;
            IBody a, b;
            int ia = bodies.TryGetBodyAt(from, out a) ? a.Id : -1;
            int ib = bodies.TryGetBodyAt(to, out b) ? b.Id : -1;
            return Sees(map, bodies, from, to, ia, ib, out unused);
        }

        internal static TrajectoryContext Context(TileMap map, BodySet bodies, Hex from, Hex to, int apex = 0)
        {
            IBody a, b;
            int ia = bodies != null && bodies.TryGetBodyAt(from, out a) ? a.Id : -1;
            int ib = bodies != null && bodies.TryGetBodyAt(to, out b) ? b.Id : -1;
            return new TrajectoryContext(map, bodies, H, from, Aim(map, from), to, Aim(map, to), apex, ia, ib);
        }
    }

    public class SightTests
    {
        [Fact]
        public void Sight_LevelOneStepBetweenGroundUnits_Clear()
        {
            // Two ground heroes aim at 4; the step's top is 3, under the ray the whole way.
            var map = AimFixtures.Row(0, 1, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0)));
        }

        [Fact]
        public void Sight_LevelTwoBetweenGroundUnits_Blocked_ReportsHex()
        {
            var map = AimFixtures.Row(0, 2, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out blockedAt));
            Assert.Equal(new Hex(1, 0), blockedAt);
        }

        [Fact]
        public void Sight_FromLevelOne_SeesOverWallAtShortRange()
        {
            // A wall (body 6) on the tile right next to the shooter. From the ground the ray is stopped;
            // from a level-1 step (aim 7) the same ray passes over it to a target five tiles out.
            var ground = AimFixtures.Row(0, 0, 0, 0, 0, 0);
            var step = AimFixtures.Row(1, 0, 0, 0, 0, 0);
            var shooter = new Hex(0, 0);
            var target = new Hex(5, 0);

            Assert.False(AimFixtures.Sees(ground, WithWall(ground, shooter, target), shooter, target));
            Assert.True(AimFixtures.Sees(step, WithWall(step, shooter, target), shooter, target));
        }

        private static BodySet WithWall(TileMap map, Hex shooter, Hex target)
        {
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, shooter), AimFixtures.Hero(1, 1, target));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(1, 0)));
            return bodies;
        }

        [Fact]
        public void Sight_IsSymmetricBetweenHeroes()
        {
            var map = AimFixtures.Row(0, 1, 2, 1, 0, 2, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(6, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(4, 0)));

            for (int a = 0; a < 7; a++)
            {
                for (int b = 0; b < 7; b++)
                {
                    var from = new Hex(a, 0);
                    var to = new Hex(b, 0);
                    Assert.Equal(AimFixtures.Sees(map, bodies, from, to, -1, -1, out _),
                                 AimFixtures.Sees(map, bodies, to, from, -1, -1, out _));
                }
            }
        }

        [Fact]
        public void Sight_EndpointTilesNeverBlock()
        {
            // The shooter stands on a level-6 tower and shoots down at a hero on the ground: its own tile is
            // far taller than the ray but is an endpoint, so it never blocks.
            var tower = AimFixtures.Row(6, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            Assert.True(AimFixtures.Sees(tower, bodies, new Hex(0, 0), new Hex(2, 0)));

            // The very same height one tile along, where it is no longer an endpoint, does block.
            var ridge = AimFixtures.Row(6, 6, 0);
            Assert.False(AimFixtures.Sees(ridge, bodies, new Hex(0, 0), new Hex(2, 0)));
        }

        [Fact]
        public void Sight_UnwalkableTerrainIsSolid()
        {
            // Stone at level 0 is under the ray but solid: it blocks whatever its height.
            var map = AimFixtures.RowWithStone(3, 1);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out blockedAt));
            Assert.Equal(new Hex(1, 0), blockedAt);
        }

        [Fact]
        public void Sight_HoleNeverBlocks()
        {
            var map = new TileMap();
            map.Add(new Tile(new Hex(0, 0), "grass"));
            map.Add(new Tile(new Hex(2, 0), "grass"));
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0)));
        }

        [Fact]
        public void Sight_LivingUnitBetween_Blocks()
        {
            var map = AimFixtures.Row(0, 0, 0);
            var bodies = AimFixtures.Bodies(
                AimFixtures.Hero(0, 0, new Hex(0, 0)),
                AimFixtures.Hero(1, 1, new Hex(1, 0)),
                AimFixtures.Hero(2, 1, new Hex(2, 0)));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 2, out blockedAt));
            Assert.Equal(new Hex(1, 0), blockedAt);
        }

        [Fact]
        public void Sight_DeadUnitBetween_DoesNotBlock()
        {
            var map = AimFixtures.Row(0, 0, 0);
            var middle = AimFixtures.Hero(1, 1, new Hex(1, 0));
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), middle, AimFixtures.Hero(2, 1, new Hex(2, 0)));
            middle.TakeDamage(middle.MaxHp);
            Assert.False(middle.IsAlive);
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 2, out _));
        }

        [Fact]
        public void Sight_WallProp_Blocks()
        {
            var map = AimFixtures.Row(0, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(1, 0)));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out blockedAt));
            Assert.Equal(new Hex(1, 0), blockedAt);
        }

        [Fact]
        public void Sight_DestroyedPillar_DoesNotBlock()
        {
            var map = AimFixtures.Row(0, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(2, 0)));
            var pillar = new Prop(2, AimFixtures.Pillar, new Hex(1, 0));
            bodies.AddProp(pillar);
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out _));

            pillar.TakeDamage(pillar.MaxHp);
            Assert.True(pillar.IsDestroyed);
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out _));
        }

        [Fact]
        public void Sight_AttackerAndTargetBodiesIgnored()
        {
            // The two ignored ids are simply not counted as columns: pointing them at the blocker proves it.
            var map = AimFixtures.Row(0, 0, 0);
            var bodies = AimFixtures.Bodies(
                AimFixtures.Hero(0, 0, new Hex(0, 0)),
                AimFixtures.Hero(1, 1, new Hex(1, 0)),
                AimFixtures.Hero(2, 1, new Hex(2, 0)));

            Assert.False(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 2, out _));
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 0, 1, out _));
            Assert.True(AimFixtures.Sees(map, bodies, new Hex(0, 0), new Hex(2, 0), 1, 2, out _));
        }

        [Fact]
        public void Arena4_SpawnsCannotSeeEachOther()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var data = catalog.Maps.Get("arena-4");
            var map = data.BuildTileMap(catalog.Terrains);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, data.SpawnP1), AimFixtures.Hero(1, 1, data.SpawnP2));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, data.SpawnP1, data.SpawnP2, 0, 1, out blockedAt));
            Assert.Equal(2, map[blockedAt].Height);                      // the central plateau does it
        }

        [Fact]
        public void Board3_Unchanged_StoneStillBlocksSight()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var data = catalog.Maps.Get("board-3");
            var map = data.BuildTileMap(catalog.Terrains);
            Assert.False(map[Hex.Zero].Walkable);

            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(-1, 0)), AimFixtures.Hero(1, 1, new Hex(1, 0)));
            Hex blockedAt;
            Assert.False(AimFixtures.Sees(map, bodies, new Hex(-1, 0), new Hex(1, 0), 0, 1, out blockedAt));
            Assert.Equal(Hex.Zero, blockedAt);
        }
    }

    public class TrajectoryResolverTests
    {
        private static readonly DirectTrajectory Direct = new DirectTrajectory();
        private static readonly ArcTrajectory Arc = new ArcTrajectory();
        private static readonly SkyTrajectory Sky = new SkyTrajectory();

        [Fact]
        public void Direct_EqualsSight()
        {
            var map = AimFixtures.Row(0, 1, 2, 0, 1, 0, 3);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)));
            bodies.AddProp(new Prop(1, AimFixtures.Wall, new Hex(3, 0)));

            for (int a = 0; a < 7; a++)
            {
                for (int b = 0; b < 7; b++)
                {
                    var from = new Hex(a, 0);
                    var to = new Hex(b, 0);
                    var ctx = AimFixtures.Context(map, bodies, from, to);
                    Assert.Equal(AimFixtures.Sees(map, bodies, from, to, ctx.IgnoreBodyA, ctx.IgnoreBodyB, out _),
                                 Direct.IsClear(in ctx, out _));
                }
            }
        }

        [Fact]
        public void Arc_ClearsWallTwoTilesAway()
        {
            // Five tiles apart (b = 8), a 6-unit wall on tile 2: the arc is at 436/64 against 384/64.
            var map = AimFixtures.Row(0, 0, 0, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(4, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(2, 0)));

            var ctx = AimFixtures.Context(map, bodies, new Hex(0, 0), new Hex(4, 0), apex: 3);
            Assert.True(Arc.IsClear(in ctx, out _));
            Assert.False(Direct.IsClear(in ctx, out _));     // the same wall stops a straight shot
        }

        [Fact]
        public void Arc_BlockedByAdjacentWall()
        {
            var map = AimFixtures.Row(0, 0, 0, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(4, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(1, 0)));

            var ctx = AimFixtures.Context(map, bodies, new Hex(0, 0), new Hex(4, 0), apex: 3);
            Hex blockedAt;
            Assert.False(Arc.IsClear(in ctx, out blockedAt));
            Assert.Equal(new Hex(1, 0), blockedAt);
        }

        [Fact]
        public void Arc_HigherApexClearsWhatLowerDoesNot()
        {
            var map = AimFixtures.Row(0, 0, 0, 0, 0);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(4, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(1, 0)));

            var low = AimFixtures.Context(map, bodies, new Hex(0, 0), new Hex(4, 0), apex: 3);
            var high = AimFixtures.Context(map, bodies, new Hex(0, 0), new Hex(4, 0), apex: 8);
            Assert.False(Arc.IsClear(in low, out _));
            Assert.True(Arc.IsClear(in high, out _));
        }

        [Fact]
        public void Sky_IgnoresEverythingBetween()
        {
            var map = AimFixtures.RowWithStone(5, 2);
            var bodies = AimFixtures.Bodies(AimFixtures.Hero(0, 0, new Hex(0, 0)), AimFixtures.Hero(1, 1, new Hex(4, 0)));
            bodies.AddProp(new Prop(2, AimFixtures.Wall, new Hex(1, 0)));

            var ctx = AimFixtures.Context(map, bodies, new Hex(0, 0), new Hex(4, 0));
            Assert.True(Sky.IsClear(in ctx, out _));
            Assert.False(Direct.IsClear(in ctx, out _));
        }

        [Fact]
        public void Registry_Default_HasThreeModes()
        {
            var registry = TrajectoryRegistry.Default();
            Assert.Equal(new[] { "direct", "arc", "sky" }, registry.All.Select(r => r.Mode).ToArray());
            Assert.True(registry.Supports(Trajectories.Sky));
            Assert.False(registry.Supports("beam"));
        }

        [Fact]
        public void Registry_UnknownMode_Throws()
        {
            var registry = TrajectoryRegistry.Default();
            var map = AimFixtures.Row(0, 0, 0);
            var ctx = AimFixtures.Context(map, AimFixtures.Bodies(), new Hex(0, 0), new Hex(2, 0));
            Assert.Throws<System.InvalidOperationException>(() => registry.IsClear("beam", in ctx, out _));
            Assert.Throws<System.InvalidOperationException>(() => registry.Register(new DirectTrajectory()));
        }
    }
}
