#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Movement;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>Shared fixtures: small hand-built boards and movement definitions.</summary>
    internal static class MoveFixtures
    {
        internal const string TerrainsWithMud = @"{ ""version"": 1, ""terrains"": [
            { ""id"": ""grass"", ""walkable"": true, ""moveCost"": 1 },
            { ""id"": ""mud"",   ""walkable"": true, ""moveCost"": 2 },
            { ""id"": ""stone"", ""walkable"": false, ""moveCost"": 0 } ] }";

        internal static readonly TerrainSet Terrains = TerrainSet.FromJson(TerrainsWithMud);

        /// <summary>Open grass board of the given radius, every tile at height 0.</summary>
        internal static TileMap Board(int radius)
        {
            var map = new TileMap();
            foreach (var h in Hex.Spiral(Hex.Zero, radius)) map.Add(new Tile(h, "grass"));
            return map;
        }

        /// <summary>Replaces one tile (TileMap has no remove, so we rebuild around it).</summary>
        internal static TileMap With(this TileMap map, Hex at, string terrain, int height = 0)
        {
            var rebuilt = new TileMap();
            foreach (var t in map.Tiles.OrderBy(t => t.Position.Q).ThenBy(t => t.Position.R))
            {
                Tile copy = t.Position == at ? new Tile(at, terrain, height) : new Tile(t.Position, t.Terrain, t.Height);
                copy.Walkable = Terrains.IsWalkable(copy.Terrain);
                rebuilt.Add(copy);
            }
            return rebuilt;
        }

        internal static MovementDef Walk(int range, int maxClimb = 1, bool ignoreHeight = false, Dictionary<string, int> costs = null)
            => new MovementDef("walk-test", "Walk", MovementModes.Walk, range, maxClimb, 1, ignoreHeight, false, costs);

        internal static MovementDef Jump(int range, int jumpHeight = 1, bool ignoreHeight = false)
            => new MovementDef("jump-test", "Jump", MovementModes.Jump, range, 1, jumpHeight, ignoreHeight, false, null);

        internal static MovementDef Teleport(int range, bool los = false)
            => new MovementDef("teleport-test", "Teleport", MovementModes.Teleport, range, 1, 1, false, los, null);

        internal static MovementContext Context(TileMap map, MovementDef def, Hex origin, params Hex[] others)
        {
            var units = new UnitSet();
            units.Add(new Unit(0, 0, origin));
            for (int i = 0; i < others.Length; i++) units.Add(new Unit(i + 1, 1, others[i]));
            return new MovementContext(map, Terrains, units, origin, def);
        }

        internal static readonly MovementResolverRegistry Registry = MovementResolverRegistry.CreateDefault();
    }

    public class HexLineTests
    {
        [Fact]
        public void Exact_AlongAnAxis_VisitsEachHexOnce()
        {
            var line = HexLine.Exact(Hex.Zero, new Hex(3, 0));
            Assert.Equal(new[] { new Hex(0, 0), new Hex(1, 0), new Hex(2, 0), new Hex(3, 0) }, line);
        }

        [Fact]
        public void Exact_ZeroLength_IsJustTheHex()
        {
            var line = HexLine.Exact(new Hex(2, -1), new Hex(2, -1));
            Assert.Equal(new[] { new Hex(2, -1) }, line);
        }

        [Fact]
        public void Exact_HasDistancePlusOneHexes_AndConsecutiveHexesAreAdjacent()
        {
            foreach (var b in Hex.Spiral(Hex.Zero, 5))
            {
                var line = HexLine.Exact(Hex.Zero, b);
                Assert.Equal(Hex.Distance(Hex.Zero, b) + 1, line.Count);
                for (int i = 1; i < line.Count; i++) Assert.Equal(1, Hex.Distance(line[i - 1], line[i]));
            }
        }

        [Fact]
        public void Exact_IsSymmetric_ReversedLineIsTheSameHexes()
        {
            var a = new Hex(-2, 3);
            foreach (var b in Hex.Spiral(Hex.Zero, 4))
            {
                var forward = HexLine.Exact(a, b);
                var backward = HexLine.Exact(b, a);
                backward.Reverse();
                Assert.Equal(forward, backward);
            }
        }

        [Fact]
        public void Supercover_EdgeGrazingLine_IncludesBothFlankingHexes()
        {
            // (0,0) -> (2,-1) passes exactly along the edge shared by (1,-1) and (1,0).
            var grazed = HexLine.Grazed(Hex.Zero, new Hex(2, -1));
            Assert.Equal(2, grazed.Count);
            Assert.Contains(new Hex(1, -1), grazed);
            Assert.Contains(new Hex(1, 0), grazed);
        }

        [Fact]
        public void Trace_EdgeGrazingSample_IsAmbiguous_AxisSampleIsNot()
        {
            Assert.True(HexLine.Trace(Hex.Zero, new Hex(2, -1))[1].IsAmbiguous);
            Assert.All(HexLine.Trace(Hex.Zero, new Hex(3, 0)), s => Assert.False(s.IsAmbiguous));
        }

        [Fact]
        public void Grazed_IsSymmetric_AsASet()
        {
            var a = new Hex(1, -3);
            foreach (var b in Hex.Spiral(Hex.Zero, 4))
            {
                var forward = new HashSet<Hex>(HexLine.Grazed(a, b));
                var backward = new HashSet<Hex>(HexLine.Grazed(b, a));
                Assert.True(forward.SetEquals(backward), $"Grazed({a},{b}) differs from Grazed({b},{a})");
            }
        }

        [Fact]
        public void Grazed_ExcludesEndpoints()
        {
            foreach (var b in Hex.Spiral(Hex.Zero, 4))
            {
                var grazed = HexLine.Grazed(Hex.Zero, b);
                Assert.DoesNotContain(Hex.Zero, grazed);
                Assert.DoesNotContain(b, grazed);
            }
        }
    }

    public class MovementDefTests
    {
        [Fact]
        public void FromJson_RepoMoveFile_IsWalkWithRangeOne()
        {
            var def = MovementDef.FromJson(RepoData.Read("abilities/move.json"));
            Assert.Equal("move", def.Id);
            Assert.Equal(MovementModes.Walk, def.Mode);
            Assert.Equal(1, def.Range);
            Assert.Equal(1, def.MaxClimb);
            Assert.False(def.IgnoreHeight);
        }

        [Fact]
        public void FromJson_RepoAbilityFolder_LoadsAllThreeModes()
        {
            var set = MovementDefSet.FromJson(new[]
            {
                RepoData.Read("abilities/move.json"),
                RepoData.Read("abilities/jump.json"),
                RepoData.Read("abilities/teleport.json"),
            });
            Assert.Equal(new[] { "move", "jump", "teleport" }, set.All.Select(d => d.Id).ToArray());
            Assert.All(set.All, d => Assert.True(MoveFixtures.Registry.Supports(d.Mode), d.Mode));
        }

        [Fact]
        public void FromJson_TerrainCostNull_MeansImpassable()
        {
            const string json = @"{ ""version"": 1, ""id"": ""fly"", ""type"": ""movement"",
                ""movement"": { ""mode"": ""walk"", ""range"": 3, ""terrainCosts"": { ""stone"": 1, ""grass"": null } } }";
            var def = MovementDef.FromJson(json);
            var stone = new Tile(Hex.Zero, "stone") { Walkable = false };
            var grass = new Tile(Hex.Zero, "grass") { Walkable = true };
            Assert.True(def.CanEnter(stone));
            Assert.False(def.CanEnter(grass));
            Assert.Equal(1, def.EntryCost(stone, MoveFixtures.Terrains));
        }

        [Fact]
        public void FromJson_WrongType_Throws()
        {
            const string json = @"{ ""version"": 1, ""id"": ""fire-bolt"", ""type"": ""attack"", ""movement"": { ""mode"": ""walk"", ""range"": 1 } }";
            Assert.Throws<MapLoadException>(() => MovementDef.FromJson(json));
        }

        [Fact]
        public void FromJson_TerrainCostBelowOne_Throws()
        {
            const string json = @"{ ""version"": 1, ""id"": ""x"", ""type"": ""movement"",
                ""movement"": { ""mode"": ""walk"", ""range"": 1, ""terrainCosts"": { ""grass"": 0 } } }";
            Assert.Throws<MapLoadException>(() => MovementDef.FromJson(json));
        }

        [Fact]
        public void FromJson_MissingMovementBlock_Throws()
        {
            const string json = @"{ ""version"": 1, ""id"": ""x"", ""type"": ""movement"" }";
            Assert.Throws<MapLoadException>(() => MovementDef.FromJson(json));
        }

        [Fact]
        public void EntryCost_FallsBackToTerrainCatalogue()
        {
            var def = MoveFixtures.Walk(3);
            var mud = new Tile(Hex.Zero, "mud");
            Assert.Equal(2, def.EntryCost(mud, MoveFixtures.Terrains));
        }

        [Fact]
        public void DefSet_DuplicateId_Throws()
        {
            var set = new MovementDefSet();
            set.Add(MoveFixtures.Walk(1));
            Assert.Throws<MapLoadException>(() => set.Add(MoveFixtures.Walk(2)));
        }
    }

    public class WalkResolverTests
    {
        [Fact]
        public void Enumerate_RangeOneOnOpenBoard_IsTheSixNeighbours()
        {
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(MoveFixtures.Board(3), MoveFixtures.Walk(1), Hex.Zero));
            Assert.Equal(6, options.Count);
            Assert.All(options.Plans, p => Assert.Equal(1, Hex.Distance(Hex.Zero, p.Destination)));
            Assert.All(options.Plans, p => Assert.Equal(TraversalKind.Ground, p.Traversal));
        }

        [Fact]
        public void Enumerate_MudCostsTwo_NotAffordableWithOnePoint()
        {
            var map = MoveFixtures.Board(2).With(new Hex(1, 0), "mud");
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(1), Hex.Zero));
            Assert.Equal(5, options.Count);
            Assert.False(options.Contains(new Hex(1, 0)));
        }

        [Fact]
        public void Enumerate_MudCostsTwo_PlanCostAccumulatesAlongPath()
        {
            var map = MoveFixtures.Board(3).With(new Hex(1, 0), "mud");
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(3), Hex.Zero));
            MovePlan viaMud;
            Assert.True(options.TryGet(new Hex(1, 0), out viaMud));
            Assert.Equal(2, viaMud.Cost);
            // (2,0) is 2 steps away; going round the mud costs 3, through it costs 3 too: either way cost 3.
            MovePlan beyond;
            Assert.True(options.TryGet(new Hex(2, 0), out beyond));
            Assert.Equal(3, beyond.Cost);
        }

        [Fact]
        public void Enumerate_StepUpAboveMaxClimb_IsExcluded_StepDownIsFree()
        {
            var map = MoveFixtures.Board(2)
                .With(new Hex(1, 0), "grass", height: 2)     // cliff
                .With(new Hex(-1, 0), "grass", height: -3);  // pit
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(1, maxClimb: 1), Hex.Zero));
            Assert.False(options.Contains(new Hex(1, 0)));
            Assert.True(options.Contains(new Hex(-1, 0)));
        }

        [Fact]
        public void Enumerate_IgnoreHeight_ClimbsAnything()
        {
            var map = MoveFixtures.Board(2).With(new Hex(1, 0), "grass", height: 9);
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(1, ignoreHeight: true), Hex.Zero));
            Assert.True(options.Contains(new Hex(1, 0)));
        }

        [Fact]
        public void Enumerate_ClimbIsPerStep_RampReachesPlateau()
        {
            var map = MoveFixtures.Board(3)
                .With(new Hex(1, 0), "grass", height: 1)
                .With(new Hex(2, 0), "grass", height: 2);
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(2), Hex.Zero));
            MovePlan plan;
            Assert.True(options.TryGet(new Hex(2, 0), out plan));
            Assert.Equal(new[] { Hex.Zero, new Hex(1, 0), new Hex(2, 0) }, plan.Path);
        }

        [Fact]
        public void Enumerate_OccupiedTile_IsNeitherEnteredNorCrossed()
        {
            // Corridor: only (1,0) connects (0,0) to (2,0); an enemy stands on it.
            var map = new TileMap();
            map.Add(new Tile(Hex.Zero, "grass"));
            map.Add(new Tile(new Hex(1, 0), "grass"));
            map.Add(new Tile(new Hex(2, 0), "grass"));
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(3), Hex.Zero, new Hex(1, 0)));
            Assert.Equal(0, options.Count);
        }

        [Fact]
        public void Enumerate_TerrainOverride_EntersUnwalkableStone()
        {
            var map = MoveFixtures.Board(2).With(new Hex(1, 0), "stone");
            var costs = new Dictionary<string, int> { ["stone"] = 1 };
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(map, MoveFixtures.Walk(1, costs: costs), Hex.Zero));
            Assert.True(options.Contains(new Hex(1, 0)));
        }

        [Fact]
        public void Enumerate_TerrainOverrideImpassable_BlocksWalkableGrass()
        {
            var costs = new Dictionary<string, int> { ["grass"] = MovementDef.Impassable };
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(MoveFixtures.Board(2), MoveFixtures.Walk(2, costs: costs), Hex.Zero));
            Assert.Equal(0, options.Count);
        }

        [Fact]
        public void Plan_EnteredTilesIsPathWithoutOrigin()
        {
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(MoveFixtures.Board(3), MoveFixtures.Walk(3), Hex.Zero));
            foreach (var plan in options.Plans)
            {
                Assert.Equal(Hex.Zero, plan.Path[0]);
                Assert.Equal(plan.Path.Skip(1), plan.EnteredTiles);
                for (int i = 1; i < plan.Path.Count; i++) Assert.Equal(1, Hex.Distance(plan.Path[i - 1], plan.Path[i]));
            }
        }

        [Theory]
        [InlineData(0, 0, MoveRejectReason.SameTile)]
        [InlineData(9, 9, MoveRejectReason.OffMap)]
        [InlineData(1, 0, MoveRejectReason.NotEnterable)]
        [InlineData(0, 1, MoveRejectReason.Occupied)]
        [InlineData(-2, 0, MoveRejectReason.OutOfRange)]
        [InlineData(-1, 0, MoveRejectReason.Unreachable)]
        public void Validate_ReportsTheSpecificReason(int q, int r, MoveRejectReason expected)
        {
            var map = MoveFixtures.Board(2)
                .With(new Hex(1, 0), "stone")
                .With(new Hex(-1, 0), "grass", height: 5);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Walk(1), Hex.Zero, new Hex(0, 1)), new Hex(q, r));
            Assert.False(result.Ok);
            Assert.Equal(expected, result.Reason);
        }
    }

    public class JumpResolverTests
    {
        [Fact]
        public void Validate_OverLowUnwalkablePillar_IsAllowed()
        {
            var map = MoveFixtures.Board(3).With(new Hex(1, 0), "stone", height: 0);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Jump(2), Hex.Zero), new Hex(2, 0));
            Assert.True(result.Ok);
            Assert.Equal(TraversalKind.Leap, result.Plan.Traversal);
            Assert.Equal(new[] { new Hex(2, 0) }, result.Plan.EnteredTiles);
            Assert.Equal(2, result.Plan.Path.Count);
        }

        [Fact]
        public void Validate_TallPillarBetween_BlocksThePath()
        {
            var map = MoveFixtures.Board(3).With(new Hex(1, 0), "stone", height: 2);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Jump(2, jumpHeight: 1), Hex.Zero), new Hex(2, 0));
            Assert.False(result.Ok);
            Assert.Equal(MoveRejectReason.PathBlocked, result.Reason);
        }

        [Fact]
        public void Validate_DestinationTooHigh_IsRejected_LowerIsAllowed()
        {
            var map = MoveFixtures.Board(3)
                .With(new Hex(2, 0), "grass", height: 2)
                .With(new Hex(-2, 0), "grass", height: -4);
            var ctx = MoveFixtures.Context(map, MoveFixtures.Jump(2, jumpHeight: 1), Hex.Zero);
            Assert.Equal(MoveRejectReason.TooHigh, MoveFixtures.Registry.Validate(ctx, new Hex(2, 0)).Reason);
            Assert.True(MoveFixtures.Registry.Validate(ctx, new Hex(-2, 0)).Ok);
        }

        [Fact]
        public void Validate_HeightIsRelativeToWhereTheUnitStands()
        {
            // Standing on a height-2 tile, a height-3 landing is only one up.
            var map = MoveFixtures.Board(3)
                .With(Hex.Zero, "grass", height: 2)
                .With(new Hex(2, 0), "grass", height: 3);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Jump(2, jumpHeight: 1), Hex.Zero), new Hex(2, 0));
            Assert.True(result.Ok);
        }

        [Fact]
        public void Validate_EdgeGrazingArc_IsBlockedByEitherFlankingTile_AndSymmetric()
        {
            // (0,0) -> (2,-1) grazes (1,-1) and (1,0). Only (1,0) is tall.
            var map = MoveFixtures.Board(3).With(new Hex(1, 0), "grass", height: 3);
            var def = MoveFixtures.Jump(2, jumpHeight: 1);
            Assert.Equal(MoveRejectReason.PathBlocked, MoveFixtures.Registry.Validate(MoveFixtures.Context(map, def, Hex.Zero), new Hex(2, -1)).Reason);
            Assert.Equal(MoveRejectReason.PathBlocked, MoveFixtures.Registry.Validate(MoveFixtures.Context(map, def, new Hex(2, -1)), Hex.Zero).Reason);
        }

        [Fact]
        public void Validate_HoleInMap_IsFlownOver()
        {
            var map = new TileMap();
            map.Add(new Tile(Hex.Zero, "grass"));
            map.Add(new Tile(new Hex(2, 0), "grass"));   // (1,0) does not exist
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Jump(2), Hex.Zero), new Hex(2, 0));
            Assert.True(result.Ok);
        }

        [Fact]
        public void Validate_CannotLandOnStoneOrOnAUnit()
        {
            var map = MoveFixtures.Board(3).With(new Hex(2, 0), "stone");
            var ctx = MoveFixtures.Context(map, MoveFixtures.Jump(2), Hex.Zero, new Hex(0, 2));
            Assert.Equal(MoveRejectReason.NotEnterable, MoveFixtures.Registry.Validate(ctx, new Hex(2, 0)).Reason);
            Assert.Equal(MoveRejectReason.Occupied, MoveFixtures.Registry.Validate(ctx, new Hex(0, 2)).Reason);
        }

        [Fact]
        public void Validate_IgnoreHeight_ClearsAnyWall()
        {
            var map = MoveFixtures.Board(3).With(new Hex(1, 0), "stone", height: 9).With(new Hex(2, 0), "grass", height: 9);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Jump(2, ignoreHeight: true), Hex.Zero), new Hex(2, 0));
            Assert.True(result.Ok);
        }

        [Fact]
        public void Enumerate_RangeTwoOnOpenBoard_IsEighteenTiles()
        {
            var options = MoveFixtures.Registry.Enumerate(MoveFixtures.Context(MoveFixtures.Board(3), MoveFixtures.Jump(2), Hex.Zero));
            Assert.Equal(18, options.Count);
        }
    }

    public class TeleportResolverTests
    {
        [Fact]
        public void Validate_IgnoresWallsAndHeight()
        {
            var map = MoveFixtures.Board(3)
                .With(new Hex(1, 0), "stone", height: 9)
                .With(new Hex(2, 0), "grass", height: 9);
            var result = MoveFixtures.Registry.Validate(MoveFixtures.Context(map, MoveFixtures.Teleport(3), Hex.Zero), new Hex(2, 0));
            Assert.True(result.Ok);
            Assert.Equal(TraversalKind.Blink, result.Plan.Traversal);
            Assert.Equal(new[] { new Hex(2, 0) }, result.Plan.EnteredTiles);
        }

        [Fact]
        public void Validate_OccupiedOrStoneDestination_IsRejected()
        {
            var map = MoveFixtures.Board(3).With(new Hex(3, 0), "stone");
            var ctx = MoveFixtures.Context(map, MoveFixtures.Teleport(3), Hex.Zero, new Hex(0, 3));
            Assert.Equal(MoveRejectReason.Occupied, MoveFixtures.Registry.Validate(ctx, new Hex(0, 3)).Reason);
            Assert.Equal(MoveRejectReason.NotEnterable, MoveFixtures.Registry.Validate(ctx, new Hex(3, 0)).Reason);
            Assert.Equal(MoveRejectReason.OutOfRange, MoveFixtures.Registry.Validate(ctx, new Hex(-3, -1)).Reason);
        }

        [Fact]
        public void Validate_WithLineOfSight_BlockedByStoneOrTallTile_NotByHole()
        {
            var map = MoveFixtures.Board(3)
                .With(new Hex(1, 0), "stone")
                .With(new Hex(0, 1), "grass", height: 2);
            var ctx = MoveFixtures.Context(map, MoveFixtures.Teleport(3, los: true), Hex.Zero);
            Assert.Equal(MoveRejectReason.NoLineOfSight, MoveFixtures.Registry.Validate(ctx, new Hex(2, 0)).Reason);
            Assert.Equal(MoveRejectReason.NoLineOfSight, MoveFixtures.Registry.Validate(ctx, new Hex(0, 2)).Reason);
            Assert.True(MoveFixtures.Registry.Validate(ctx, new Hex(-2, 0)).Ok);

            var holey = new TileMap();
            holey.Add(new Tile(Hex.Zero, "grass"));
            holey.Add(new Tile(new Hex(2, 0), "grass"));
            Assert.True(MoveFixtures.Registry.Validate(MoveFixtures.Context(holey, MoveFixtures.Teleport(3, los: true), Hex.Zero), new Hex(2, 0)).Ok);
        }

        [Fact]
        public void LineOfSight_StandingHigh_SeesOverLowerTiles()
        {
            var map = MoveFixtures.Board(3)
                .With(Hex.Zero, "grass", height: 3)
                .With(new Hex(1, 0), "grass", height: 2);
            Assert.True(LineOfSight.IsClear(map, Hex.Zero, new Hex(2, 0)));
            Assert.True(LineOfSight.IsClear(map, new Hex(2, 0), Hex.Zero));
        }
    }

    public class MovementRegistryTests
    {
        [Fact]
        public void CreateDefault_HasWalkJumpTeleport()
        {
            var registry = MovementResolverRegistry.CreateDefault();
            Assert.Equal(new[] { "walk", "jump", "teleport" }, registry.All.Select(r => r.Mode).ToArray());
        }

        [Fact]
        public void Register_DuplicateMode_Throws()
        {
            var registry = MovementResolverRegistry.CreateDefault();
            Assert.Throws<System.InvalidOperationException>(() => registry.Register(new WalkResolver()));
        }

        [Fact]
        public void UnknownMode_FailsClosed()
        {
            var def = new MovementDef("burrow", "Burrow", "burrow", 3);
            var ctx = MoveFixtures.Context(MoveFixtures.Board(3), def, Hex.Zero);
            Assert.Equal(0, MoveFixtures.Registry.Enumerate(ctx).Count);
            Assert.Equal(MoveRejectReason.UnknownMovement, MoveFixtures.Registry.Validate(ctx, new Hex(1, 0)).Reason);
        }

        private static (TileMap map, MapData data, MovementDefSet defs) Arena()
        {
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            var data = MapData.FromJson(RepoData.Arena4Json);
            var defs = MovementDefSet.FromJson(new[]
            {
                RepoData.Read("abilities/move.json"),
                RepoData.Read("abilities/jump.json"),
                RepoData.Read("abilities/teleport.json"),
            });
            return (data.BuildTileMap(terrains), data, defs);
        }

        [Fact]
        public void Arena4_ValidateAgreesWithEnumerate_ForEveryAbilityAndTile()
        {
            var (map, data, defs) = Arena();
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            var units = new UnitSet();
            units.Add(new Unit(0, 0, data.SpawnP1));
            units.Add(new Unit(1, 1, data.SpawnP2));

            // Test from several origins, not just the spawn, so the plateau and pillars are exercised.
            foreach (var origin in new[] { data.SpawnP1, new Hex(-2, 0), new Hex(0, 0), new Hex(-1, 2) })
            {
                units.Get(0).MoveTo(origin);
                foreach (var def in defs.All)
                {
                    var ctx = MovementContext.For(map, terrains, units, units.Get(0), def);
                    var options = MoveFixtures.Registry.Enumerate(ctx);
                    Assert.True(options.Count > 0, $"{def.Id} from {origin} has no options");
                    foreach (var tile in map.Tiles)
                    {
                        var result = MoveFixtures.Registry.Validate(ctx, tile.Position);
                        MovePlan enumerated;
                        bool listed = options.TryGet(tile.Position, out enumerated);
                        Assert.True(result.Ok == listed, $"{def.Id} from {origin} to {tile.Position}: validate={result.Reason} listed={listed}");
                        if (listed)
                        {
                            Assert.Equal(enumerated.Path, result.Plan.Path);
                            Assert.Equal(enumerated.Cost, result.Plan.Cost);
                        }
                    }
                }
            }
        }

        [Fact]
        public void Arena4_HeightsAreRotationallySymmetric()
        {
            var (map, _, _) = Arena();
            foreach (var tile in map.Tiles)
                Assert.Equal(tile.Height, map[tile.Position.Rotate180()].Height);
        }

        [Fact]
        public void Arena4_JumpFromSpawn_ClearsLowPillar_WalkCannot()
        {
            var (map, data, defs) = Arena();
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            var units = new UnitSet();
            units.Add(new Unit(0, 0, data.SpawnP1));

            var jump = MoveFixtures.Registry.Enumerate(MovementContext.For(map, terrains, units, units.Get(0), defs.Get("jump")));
            var walk = MoveFixtures.Registry.Enumerate(MovementContext.For(map, terrains, units, units.Get(0), defs.Get("move")));
            Assert.True(jump.Contains(new Hex(-2, 0)));    // over the height-1 pillar at (-3,0) onto the ramp
            Assert.False(walk.Contains(new Hex(-3, 0)));   // stone
        }

        [Fact]
        public void Arena4_Enumerate_IsDeterministic()
        {
            var (map, data, defs) = Arena();
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            var units = new UnitSet();
            units.Add(new Unit(0, 0, new Hex(-2, 0)));
            foreach (var def in defs.All)
            {
                var ctx = MovementContext.For(map, terrains, units, units.Get(0), def);
                var a = MoveFixtures.Registry.Enumerate(ctx).Plans.Select(p => p.ToString()).ToArray();
                var b = MoveFixtures.Registry.Enumerate(ctx).Plans.Select(p => p.ToString()).ToArray();
                Assert.Equal(a, b);
            }
        }
    }
}
