#nullable disable   // JObject navigation in these tests is intentionally loose; failures surface as test failures.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>Locates the real data files so tests and shipped JSON can never drift apart.</summary>
    internal static class RepoData
    {
        private static readonly string Root = FindRepoRoot();

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("Mimas.sln*").Length > 0) return dir.FullName;
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not locate the repo root (no Mimas.sln* found above " + AppContext.BaseDirectory + ").");
        }

        /// <summary>Absolute path of the shipped data folder (the single source of truth for content).</summary>
        internal static string DataRoot => Path.Combine(Root, "MimasClient", "Assets", "_Game", "Data");

        /// <summary>The text of one Core source file, for the tests that scan code rather than run it.</summary>
        internal static string CoreSource(string relativePath) =>
            File.ReadAllText(Path.Combine(Root, "shared", "Mimas.Core", "Runtime", relativePath.Replace('/', Path.DirectorySeparatorChar)));

        internal static string Read(string relativePath) =>
            File.ReadAllText(Path.Combine(DataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        internal static string TerrainsJson => Read("terrains.json");
        internal static string Arena4Json => Read("maps/arena-4.json");
        internal static string Board3Json => Read("maps/board-3.json");
    }

    public class TerrainSetTests
    {
        [Fact]
        public void FromJson_RepoTerrainsFile_ParsesEveryTerrain()
        {
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            Assert.Equal(new[] { "grass", "stone" }, terrains.All.Select(t => t.Id).ToArray());
        }

        [Fact]
        public void Get_KnownId_ReturnsWalkableGrassWithCostOne()
        {
            var grass = TerrainSet.FromJson(RepoData.TerrainsJson).Get("grass");
            Assert.True(grass.Walkable);
            Assert.Equal(1, grass.MoveCost);
        }

        [Fact]
        public void Get_UnknownId_ThrowsMapLoadException()
        {
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            Assert.Throws<MapLoadException>(() => terrains.Get("lava"));
        }

        [Fact]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            Assert.False(terrains.TryGet("lava", out var def));
            Assert.Null(def);
        }

        [Fact]
        public void IsWalkable_StoneAndUnknownId_AreBothFalse()
        {
            var terrains = TerrainSet.FromJson(RepoData.TerrainsJson);
            Assert.False(terrains.IsWalkable("stone"));
            Assert.False(terrains.IsWalkable("lava"));
            Assert.True(terrains.IsWalkable("grass"));
        }

        [Fact]
        public void FromJson_WrongVersion_ThrowsMapLoadException()
        {
            const string json = "{\"version\":2,\"terrains\":[{\"id\":\"grass\",\"walkable\":true,\"moveCost\":1}]}";
            Assert.Throws<MapLoadException>(() => TerrainSet.FromJson(json));
        }

        [Fact]
        public void FromJson_DuplicateId_ThrowsMapLoadException()
        {
            const string json = "{\"version\":1,\"terrains\":[" +
                                "{\"id\":\"grass\",\"walkable\":true,\"moveCost\":1}," +
                                "{\"id\":\"grass\",\"walkable\":false,\"moveCost\":0}]}";
            Assert.Throws<MapLoadException>(() => TerrainSet.FromJson(json));
        }

        [Fact]
        public void FromJson_EmptyTerrainList_ThrowsMapLoadException()
        {
            Assert.Throws<MapLoadException>(() => TerrainSet.FromJson("{\"version\":1,\"terrains\":[]}"));
        }

        [Fact]
        public void FromJson_MalformedJson_ThrowsMapLoadException()
        {
            Assert.Throws<MapLoadException>(() => TerrainSet.FromJson("{\"version\":1,"));
        }
    }

    /// <summary>
    /// Heights in rules.json and the two aiming fields on an attack: both required, both fail closed
    /// (spec A, D1/D5/D6).
    /// </summary>
    public class HeightsAndTrajectoryParsingTests
    {
        private const string RulesHead = @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""baseStats"": { ""hp"": 20, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ], ""clock"": { ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 },
            ""elements"": [], ""series"": { ""bestOf"": 3 }, ""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }, ""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 }";

        private static RulesDef Rules(string heights) => RulesDef.FromJson(RulesHead + @", ""heights"": " + heights + " }");

        private static AttackDef Attack(string attack) => (AttackDef)AbilityDef.FromJson(
            @"{ ""version"": 1, ""id"": ""shot"", ""type"": ""attack"", ""category"": ""weapon"", ""attack"": " + attack + " }");

        [Fact]
        public void Rules_MissingHeights_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => RulesDef.FromJson(RulesHead + " }"));
            Assert.Contains("'heights'", e.Message);
        }

        [Fact]
        public void Rules_AimAboveBody_Throws()
        {
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 7 }"));
            Assert.Throws<MapLoadException>(() => Rules(@"{ ""unitsPerLevel"": 0, ""body"": 6, ""aim"": 4 }"));

            var ok = Rules(@"{ ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }");
            Assert.Equal(3, ok.Heights.UnitsPerLevel);
            Assert.Equal(6, ok.Heights.Body);
            Assert.Equal(4, ok.Heights.Aim);
            Assert.Equal(6, ok.Heights.TileTop(new Tile(Hex.Zero, "grass", 2)));
        }

        [Fact]
        public void Attack_MissingTrajectory_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""lineOfSight"": true }"));
            Assert.Contains("'trajectory'", e.Message);
        }

        [Fact]
        public void Attack_MissingLineOfSight_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"" }"));
            Assert.Contains("'lineOfSight'", e.Message);
        }

        [Fact]
        public void Attack_ArcWithoutApex_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""arc"", ""lineOfSight"": false }"));
            Assert.Contains("apex is required for trajectory 'arc'", e.Message);
        }

        [Fact]
        public void Attack_ApexOnDirect_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""apex"": 2, ""lineOfSight"": true }"));
            Assert.Contains("only allowed for trajectory 'arc'", e.Message);
        }

        [Fact]
        public void Attack_UnknownTrajectory_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""lob"", ""lineOfSight"": true }"));
            Assert.Contains("trajectory is 'lob'", e.Message);
        }

        [Fact]
        public void Attack_ArcParsesApex()
        {
            var arc = Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 5, ""trajectory"": ""arc"", ""apex"": 3, ""lineOfSight"": false }");
            Assert.Equal(Trajectories.Arc, arc.Trajectory);
            Assert.Equal(3, arc.Apex);
            Assert.False(arc.LineOfSight);
        }

        [Fact]
        public void InRangeSquared_IsACircle_NotAHexDistanceBand()
        {
            var gun = Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 7, ""trajectory"": ""direct"", ""lineOfSight"": true }");

            // (4,4) is 8 hexes away but only sqrt(48) < 7 spacings from the centre.
            Assert.Equal(8, Hex.Distance(Hex.Zero, new Hex(4, 4)));
            Assert.True(gun.InRangeSquared(Hex.EuclideanSquared(Hex.Zero, new Hex(4, 4))));
            Assert.False(gun.InRangeSquared(Hex.EuclideanSquared(Hex.Zero, new Hex(8, 0))));
        }

        [Fact]
        public void WithTrajectory_KeepsEveryOtherField()
        {
            var gun = Attack(@"{ ""damage"": 2, ""damageType"": ""weapon"", ""range"": 7, ""minRange"": 2, ""trajectory"": ""direct"", ""lineOfSight"": true }");
            var lobbed = gun.WithTrajectory(Trajectories.Arc, 4, false);

            Assert.Equal(Trajectories.Arc, lobbed.Trajectory);
            Assert.Equal(4, lobbed.Apex);
            Assert.False(lobbed.LineOfSight);
            Assert.Equal(gun.Id, lobbed.Id);
            Assert.Equal(gun.Damage, lobbed.Damage);
            Assert.Equal(gun.Range, lobbed.Range);
            Assert.Equal(gun.MinRange, lobbed.MinRange);
            Assert.Equal(gun.Cost, lobbed.Cost);
            Assert.Equal(Trajectories.Direct, gun.Trajectory);      // the original is untouched
        }
    }

    /// <summary>Props as authored in <c>props/*.json</c> (spec A, D11): heights, hit points, and what a prop may not carry.</summary>
    public class PropDefTests
    {
        private static PropDef Prop(string body) => PropDef.FromJson(
            @"{ ""version"": 1, ""id"": ""thing"", " + body + " }");

        [Fact]
        public void Prop_ParsesWallWithoutStats_NotDestructible()
        {
            var wall = PropDef.FromJson(RepoData.Read("props/wall.json"));
            Assert.Equal("wall", wall.Id);
            Assert.Equal(6, wall.BodyHeight);
            Assert.Equal(3, wall.AimHeight);
            Assert.False(wall.IsDestructible);
            Assert.Equal(0, wall.Stats.Hp);
        }

        [Fact]
        public void Prop_PillarWithHp_Destructible()
        {
            var pillar = PropDef.FromJson(RepoData.Read("props/pillar.json"));
            Assert.True(pillar.IsDestructible);
            Assert.Equal(10, pillar.Stats.Hp);
            Assert.Equal(0, pillar.Stats.Get("defense.weapon"));
            Assert.Equal("Stone Pillar", pillar.Name);
        }

        [Fact]
        public void Prop_AimAboveBody_Throws()
        {
            Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6, ""aimHeight"": 7"));
            Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 0, ""aimHeight"": 1"));
            Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6"));
            Assert.Equal(6, Prop(@"""bodyHeight"": 6, ""aimHeight"": 6").AimHeight);
        }

        [Fact]
        public void Prop_PowerStat_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6, ""aimHeight"": 3, ""stats"": { ""hp"": 5, ""power.weapon"": 1 }"));
            Assert.Contains("unknown stat 'power.weapon'", e.Message);
        }

        [Fact]
        public void Prop_ApStat_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6, ""aimHeight"": 3, ""stats"": { ""hp"": 5, ""ap"": 3 }"));
            Assert.Contains("unknown stat 'ap'", e.Message);
        }

        [Fact]
        public void Prop_ZeroHp_Throws()
        {
            Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6, ""aimHeight"": 3, ""stats"": { ""hp"": 0 }"));
            Assert.Throws<MapLoadException>(() => Prop(@"""bodyHeight"": 6, ""aimHeight"": 3, ""stats"": { ""defense.weapon"": -1 }"));
        }
    }

    public class MapDataTests
    {
        private static TerrainSet Terrains() => TerrainSet.FromJson(RepoData.TerrainsJson);

        /// <summary>arena-4 as a mutable tree, so a test can break exactly one invariant.</summary>
        private static JObject Arena4Tree() => JObject.Parse(RepoData.Arena4Json);

        private static JObject HexAt(JObject map, int q, int r) =>
            map["hexes"].Children<JObject>().Single(h => (int)h["q"] == q && (int)h["r"] == r);

        [Fact]
        public void FromJson_RepoArena4File_ParsesHeaderAndSpawns()
        {
            var map = MapData.FromJson(RepoData.Arena4Json);
            Assert.Equal("arena-4", map.Id);
            Assert.Equal("Arena", map.Name);
            Assert.Equal("rotational-180", map.Symmetry);
            Assert.Equal(3, map.LadderPosition);
            Assert.Equal(new Hex(-4, 0), map.SpawnP1);
            Assert.Equal(new Hex(4, 0), map.SpawnP2);
        }

        [Fact]
        public void FromJson_RepoArena4File_ContainsTheFullRadiusFourHexagon()
        {
            var map = MapData.FromJson(RepoData.Arena4Json);
            var positions = new HashSet<Hex>(map.Hexes.Select(h => h.Position));
            Assert.Equal(61, positions.Count);
            Assert.Equal(new HashSet<Hex>(Hex.Spiral(Hex.Zero, 4)), positions);
        }

        [Fact]
        public void FromJson_WrongVersion_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            tree["version"] = 2;
            Assert.Throws<MapLoadException>(() => MapData.FromJson(tree.ToString()));
        }

        [Fact]
        public void FromJson_MissingSpawns_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            tree.Remove("spawns");
            Assert.Throws<MapLoadException>(() => MapData.FromJson(tree.ToString()));
        }

        [Fact]
        public void FromJson_DuplicateHex_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            ((JArray)tree["hexes"]).Add(new JObject { ["q"] = 0, ["r"] = 0, ["terrain"] = "grass" });
            Assert.Throws<MapLoadException>(() => MapData.FromJson(tree.ToString()));
        }

        [Fact]
        public void FromJson_MalformedJson_ThrowsMapLoadException()
        {
            Assert.Throws<MapLoadException>(() => MapData.FromJson("{\"version\":1,\"id\":"));
        }

        [Fact]
        public void BuildTileMap_Arena4_Yields61WalkableTiles_CoverIsProps()
        {
            var map = MapData.FromJson(RepoData.Arena4Json);
            var tiles = map.BuildTileMap(Terrains());
            Assert.Equal(61, tiles.Count);
            Assert.All(tiles.Tiles, t => Assert.True(t.Walkable));
            Assert.Equal(8, map.Hexes.Count(h => h.PropId != null));
        }

        [Fact]
        public void BuildTileMap_Board3_StoneCentreIsUnwalkable()
        {
            var tiles = MapData.FromJson(RepoData.Board3Json).BuildTileMap(Terrains());
            Assert.Equal(37, tiles.Count);
            Assert.False(tiles[Hex.Zero].Walkable);
            Assert.All(tiles.Tiles, t => Assert.Equal(t.Terrain == "grass", t.Walkable));
        }

        [Fact]
        public void BuildTileMap_UnknownTerrainId_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            HexAt(tree, 0, 0)["terrain"] = "lava";
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_UnsupportedSymmetry_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            tree["symmetry"] = "mirror-q";
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_TerrainNotMirroredByItsTwin_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            HexAt(tree, 1, 1)["terrain"] = "stone";      // twin (-1,-1) stays grass
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_ShapeNotRotationallySymmetric_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            var hexes = (JArray)tree["hexes"];
            hexes.Remove(HexAt(tree, 0, 4));            // twin (0,-4) survives
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_SpawnOnUnwalkableTile_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            HexAt(tree, -4, 0)["terrain"] = "stone";    // both spawn corners, so the map stays symmetric
            HexAt(tree, 4, 0)["terrain"] = "stone";
            var map = MapData.FromJson(tree.ToString());
            var e = Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
            Assert.Contains("spawn p1 Hex(-4,0) is not walkable", e.Message);
        }

        [Fact]
        public void Map_PropOnSpawn_Throws()
        {
            var tree = Arena4Tree();
            HexAt(tree, -4, 0)["prop"] = "pillar";
            HexAt(tree, 4, 0)["prop"] = "pillar";
            var map = MapData.FromJson(tree.ToString());
            var e = Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
            Assert.Contains("spawn p1 Hex(-4,0) carries a prop", e.Message);
        }

        [Fact]
        public void Map_PropTwinMismatch_Throws()
        {
            var tree = Arena4Tree();
            HexAt(tree, 3, 0)["prop"] = "pillar";       // the twin (-3,0) carries none
            var map = MapData.FromJson(tree.ToString());
            var e = Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
            Assert.Contains("breaks rotational symmetry", e.Message);
            Assert.Contains("has prop 'none' but its twin Hex(3,0) has 'pillar'", e.Message);
        }

        [Fact]
        public void Arena4_PropsAreRotationallySymmetric()
        {
            var map = MapData.FromJson(RepoData.Arena4Json);
            foreach (var hex in map.Hexes)
            {
                var twin = map.HexAt(hex.Position.Rotate180());
                Assert.NotNull(twin);
                Assert.Equal(hex.PropId, twin.PropId);
                Assert.Equal(hex.Height, twin.Height);
            }
        }

        [Fact]
        public void Arena4_HasNoStone()
        {
            var map = MapData.FromJson(RepoData.Arena4Json);
            Assert.All(map.Hexes, h => Assert.Equal("grass", h.Terrain));
            Assert.Equal(4, map.Hexes.Count(h => h.PropId == "wall"));
            Assert.Equal(4, map.Hexes.Count(h => h.PropId == "pillar"));
        }

        [Fact]
        public void BuildTileMap_SpawnsNotAtMaximalDistance_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            tree["spawns"]["p1"]["q"] = -3;             // (-3,1)..(3,-1) are 6 apart, the corners are 8
            tree["spawns"]["p1"]["r"] = 1;
            tree["spawns"]["p2"]["q"] = 3;
            tree["spawns"]["p2"]["r"] = -1;
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_SpawnOffTheMap_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            tree["spawns"]["p1"]["q"] = -9;
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }

        [Fact]
        public void BuildTileMap_DisconnectedWalkableRegion_ThrowsMapLoadException()
        {
            var tree = Arena4Tree();
            // Wall off ring 3 entirely: the inner blob can no longer reach the outer rim.
            foreach (var hex in tree["hexes"].Children<JObject>())
            {
                var h = new Hex((int)hex["q"], (int)hex["r"]);
                if (h.Length == 3) hex["terrain"] = "stone";
            }
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
        }
    }

    public class FindPathTests
    {
        private static TileMap Board(int radius)
        {
            var map = new TileMap();
            foreach (var h in Hex.Spiral(Hex.Zero, radius)) map.Add(new Tile(h, "grass"));
            return map;
        }

        private static TileMap Arena4() =>
            MapData.FromJson(RepoData.Arena4Json).BuildTileMap(TerrainSet.FromJson(RepoData.TerrainsJson));

        private static TileMap Board3() =>
            MapData.FromJson(RepoData.Board3Json).BuildTileMap(TerrainSet.FromJson(RepoData.TerrainsJson));

        [Fact]
        public void FindPath_OnOpenBoard_HasLengthOfHexDistancePlusOne()
        {
            var map = Board(3);
            var start = new Hex(-3, 0);
            var goal = new Hex(3, -3);
            var path = map.FindPath(start, goal);
            Assert.Equal(Hex.Distance(start, goal) + 1, path.Count);
            Assert.Equal(start, path[0]);
            Assert.Equal(goal, path[path.Count - 1]);
        }

        [Fact]
        public void FindPath_OnOpenBoard_StepsAreAdjacentAndOnTheMap()
        {
            var map = Board(3);
            var path = map.FindPath(new Hex(-3, 0), new Hex(3, 0));
            for (int i = 1; i < path.Count; i++)
            {
                Assert.Equal(1, Hex.Distance(path[i - 1], path[i]));
                Assert.True(map.Contains(path[i]));
            }
        }

        [Fact]
        public void FindPath_StartEqualsGoal_ReturnsSingleHex()
        {
            var path = Board(2).FindPath(Hex.Zero, Hex.Zero);
            Assert.Equal(new[] { Hex.Zero }, path);
        }

        [Fact]
        public void FindPath_BlockedDirectRoute_GoesAroundTheStone()
        {
            var map = Board3();                                             // board-3 keeps its stone centre
            var start = new Hex(-3, 0);
            var goal = new Hex(3, 0);
            var path = map.FindPath(start, goal);
            Assert.Equal(8, path.Count);                                    // 7 steps vs a raw distance of 6
            Assert.True(path.Count > Hex.Distance(start, goal) + 1);
            Assert.All(path, h => Assert.True(map[h].Walkable));
        }

        [Fact]
        public void FindPath_StartTileUnwalkable_StillFindsAPath()
        {
            var map = Board(2);
            map[Hex.Zero].Walkable = false;                                 // a unit may stand on blocking terrain
            var path = map.FindPath(Hex.Zero, new Hex(2, 0));
            Assert.NotNull(path);
            Assert.Equal(Hex.Zero, path[0]);
        }

        [Fact]
        public void FindPath_UnwalkableGoal_ReturnsNull()
        {
            var map = Board3();
            Assert.Null(map.FindPath(new Hex(-3, 0), Hex.Zero));            // board-3's centre is stone
        }

        [Fact]
        public void FindPath_GoalOffTheMap_ReturnsNull()
        {
            Assert.Null(Board(2).FindPath(Hex.Zero, new Hex(9, 0)));
        }

        [Fact]
        public void FindPath_StartOffTheMap_ReturnsNull()
        {
            Assert.Null(Board(2).FindPath(new Hex(9, 0), Hex.Zero));
        }

        [Fact]
        public void FindPath_WalledOffGoal_ReturnsNull()
        {
            var map = Board(2);
            foreach (var h in Hex.Ring(Hex.Zero, 1)) map[h].Walkable = false;
            Assert.Null(map.FindPath(Hex.Zero, new Hex(2, 0)));
        }

        [Fact]
        public void FindPath_CalledTwice_ReturnsTheIdenticalPath()
        {
            var map = Arena4();
            var start = new Hex(-4, 0);
            var goal = new Hex(4, 0);
            Assert.Equal(map.FindPath(start, goal), map.FindPath(start, goal));
        }
    }
    /// <summary>
    /// The one clock M2 ships (rules.json <c>clock</c>): required, all three numbers positive. Both the
    /// server's deadline and the client's rope read it, so a missing or zero value must fail at load
    /// rather than silently produce a turn that never ends.
    /// </summary>
    public class ClockParsingTests
    {
        private const string Head = @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 },
            ""baseStats"": { ""hp"": 20, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ],
            ""elements"": [], ""series"": { ""bestOf"": 3 }, ""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }, ""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 }";

        private static RulesDef WithClock(string clock) => RulesDef.FromJson(Head + @", ""clock"": " + clock + " }");

        [Fact]
        public void Rules_ClockBlock_Parsed()
        {
            var rules = WithClock(@"{ ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 }");
            Assert.Equal(30000, rules.Clock.TurnMs);
            Assert.Equal(1000, rules.Clock.LagGraceMs);
            Assert.Equal(60000, rules.Clock.ReconnectGraceMs);
        }

        [Fact]
        public void Rules_MissingClock_Throws()
        {
            var e = Assert.Throws<MapLoadException>(() => RulesDef.FromJson(Head + " }"));
            Assert.Contains("'clock'", e.Message);
        }

        [Fact]
        public void Rules_ClockNonPositive_Throws()
        {
            Assert.Throws<MapLoadException>(() => WithClock(@"{ ""turnMs"": 0, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 }"));
            Assert.Throws<MapLoadException>(() => WithClock(@"{ ""turnMs"": 30000, ""lagGraceMs"": 0, ""reconnectGraceMs"": 60000 }"));
            Assert.Throws<MapLoadException>(() => WithClock(@"{ ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": -1 }"));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ClockDef(30000, 1000, 0));
        }

        [Fact]
        public void Rules_ShippedFile_HasTheClock()
        {
            var rules = RulesDef.FromJson(RepoData.Read("rules.json"));
            Assert.Equal(30000, rules.Clock.TurnMs);
            Assert.Equal(1000, rules.Clock.LagGraceMs);
            Assert.Equal(60000, rules.Clock.ReconnectGraceMs);
        }
    }

    /// <summary>
    /// The four rules blocks the boons system reads (spec D part 1 §5.1): <c>elements</c>, <c>series</c>,
    /// <c>draft</c>, <c>boons</c>. All required, each failing closed on a bad number; the constructor
    /// defaults them for hand-built fixtures exactly as <c>clock</c> is defaulted.
    /// </summary>
    public class BoonRulesParsingTests
    {
        private const string Head = @"{ ""version"": 1, ""damageTypes"": [ ""weapon"" ], ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 },
            ""baseStats"": { ""hp"": 20, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ], ""clock"": { ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 }";

        private const string Elements = @"""elements"": [ ""fire"", ""frost"", ""lightning"" ]";
        private const string Series = @"""series"": { ""bestOf"": 3 }";
        private const string Draft = @"""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }";
        private const string Boons = @"""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 }";

        private static RulesDef With(params string[] blocks) => RulesDef.FromJson(Head + ", " + string.Join(", ", blocks) + " }");

        [Fact]
        public void Rules_MissingBoonsBlock_IsAnError()
        {
            var e = Assert.Throws<MapLoadException>(() => With(Elements, Series, Draft));
            Assert.Contains("'boons'", e.Message);
        }

        [Fact]
        public void Rules_MissingElementsSeriesOrDraft_IsAnError()
        {
            Assert.Contains("'elements'", Assert.Throws<MapLoadException>(() => With(Series, Draft, Boons)).Message);
            Assert.Contains("'series'", Assert.Throws<MapLoadException>(() => With(Elements, Draft, Boons)).Message);
            Assert.Contains("'draft'", Assert.Throws<MapLoadException>(() => With(Elements, Series, Boons)).Message);
        }

        [Fact]
        public void Rules_FourBlocks_Parse()
        {
            var rules = With(Elements, Series, Draft, Boons);
            Assert.Equal(new[] { "fire", "frost", "lightning" }, rules.Elements);
            Assert.True(rules.IsElement("frost"));
            Assert.False(rules.IsElement("acid"));
            Assert.Equal(3, rules.Series.BestOf);
            Assert.Equal(2, rules.Series.RoundsToWin);
            Assert.Equal(3, rules.Draft.OffersWinner);
            Assert.Equal(3, rules.Draft.OffersLoser);
            Assert.Equal(20000, rules.Draft.TimeoutMs);
            Assert.Equal(1, rules.Boons.HpFloor);
            Assert.Equal(1, rules.Boons.ApFloor);
            Assert.Equal(1, rules.Boons.MinCost);
        }

        [Fact]
        public void Rules_SeriesBestOfMustBeOddAndPositive()
        {
            Assert.Throws<MapLoadException>(() => With(Elements, @"""series"": { ""bestOf"": 2 }", Draft, Boons));
            Assert.Throws<MapLoadException>(() => With(Elements, @"""series"": { ""bestOf"": 0 }", Draft, Boons));
            Assert.Equal(3, With(Elements, @"""series"": { ""bestOf"": 5 }", Draft, Boons).Series.RoundsToWin);
            Assert.Throws<ArgumentOutOfRangeException>(() => new SeriesDef(4));
        }

        [Fact]
        public void Rules_DraftAndBoonFloors_FailClosedOnBadNumbers()
        {
            Assert.Throws<MapLoadException>(() => With(Elements, Series, @"""draft"": { ""offers"": { ""winner"": -1, ""loser"": 3 }, ""timeoutMs"": 20000 }", Boons));
            Assert.Throws<MapLoadException>(() => With(Elements, Series, @"""draft"": { ""offers"": { ""winner"": 3 }, ""timeoutMs"": 20000 }", Boons));
            Assert.Throws<MapLoadException>(() => With(Elements, Series, Draft, @"""boons"": { ""floors"": { ""hp"": 0, ""ap"": 1 }, ""minCost"": 1 }"));
            Assert.Throws<MapLoadException>(() => With(Elements, Series, Draft, @"""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": -1 }"));
            Assert.Throws<MapLoadException>(() => With(@"""elements"": [ ""fire"", ""fire"" ]", Series, Draft, Boons));
            Assert.Throws<ArgumentOutOfRangeException>(() => new BoonRulesDef(0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DraftDef(3, -1, 0));
        }

        [Fact]
        public void Rules_HandBuilt_DefaultsTheFourBlocks()
        {
            var rules = new RulesDef(new List<string> { "weapon" }, null, new StatBlock(new[] { new KeyValuePair<string, int>("hp", 5), new KeyValuePair<string, int>("ap", 3) }),
                new HeightsDef(3, 6, 4), new List<string> { "move" });
            Assert.Empty(rules.Elements);
            Assert.Equal(3, rules.Series.BestOf);
            Assert.Equal(3, rules.Draft.OffersLoser);
            Assert.Equal(1, rules.Boons.MinCost);
        }

        [Fact]
        public void Rules_ShippedFile_HasTheFourBlocks()
        {
            var rules = RulesDef.FromJson(RepoData.Read("rules.json"));
            Assert.Equal(new[] { "fire", "frost", "lightning" }, rules.Elements);
            Assert.Equal(3, rules.Series.BestOf);
            Assert.Equal(3, rules.Draft.OffersWinner);
            Assert.Equal(3, rules.Draft.OffersLoser);
            Assert.Equal(20000, rules.Draft.TimeoutMs);
            Assert.Equal(1, rules.Boons.HpFloor);
            Assert.Equal(1, rules.Boons.ApFloor);
            Assert.Equal(1, rules.Boons.MinCost);
        }
    }

    /// <summary>Elements and the multi-hit skeleton on an attack; elements, item kinds and immunity on a modifier (spec D part 1 §5.2, §5.3).</summary>
    public class ElementAndImmunityParsingTests
    {
        private static AttackDef Attack(string attack) => (AttackDef)AbilityDef.FromJson(
            @"{ ""version"": 1, ""id"": ""shot"", ""type"": ""attack"", ""category"": ""weapon"", ""attack"": " + attack + " }");

        [Fact]
        public void Attack_ElementParses_AndHitsDefaultsToOne()
        {
            var plain = Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true }");
            Assert.Empty(plain.Elements);
            Assert.Equal(1, plain.Hits);

            var fiery = Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true, ""element"": ""fire"" }");
            Assert.Equal(new[] { "fire" }, fiery.Elements);
            Assert.True(fiery.HasElement("fire"));
            Assert.False(fiery.HasElement("frost"));
        }

        [Fact]
        public void Attack_HitsParses_AndFailsBelowOne()
        {
            Assert.Equal(3, Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true, ""hits"": 3 }").Hits);
            var e = Assert.Throws<MapLoadException>(() => Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true, ""hits"": 0 }"));
            Assert.Contains("hits must be at least 1", e.Message);
        }

        [Fact]
        public void WithTrajectory_KeepsElementsAndHits()
        {
            var fiery = Attack(@"{ ""damage"": 1, ""damageType"": ""weapon"", ""range"": 3, ""trajectory"": ""direct"", ""lineOfSight"": true, ""element"": ""fire"", ""hits"": 2 }");
            var lobbed = fiery.WithTrajectory(Trajectories.Arc, 2, false);
            Assert.Equal(new[] { "fire" }, lobbed.Elements);
            Assert.Equal(2, lobbed.Hits);
        }

        [Fact]
        public void Attack_ShippedFireBolt_IsFire()
        {
            var bolt = (AttackDef)AbilityDef.FromJson(RepoData.Read("abilities/fire-bolt.json"));
            Assert.Equal(new[] { "fire" }, bolt.Elements);
            Assert.Equal(1, bolt.Hits);
        }

        [Fact]
        public void Modifier_ElementsAndItemKindsParse()
        {
            var def = ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""when"": { ""elements"": [ ""fire"", ""frost"" ], ""itemKinds"": [ ""bow"" ] }, ""effect"": { ""damage"": 2 } }");
            Assert.Equal(new[] { "fire", "frost" }, def.Elements);
            Assert.Equal(new[] { "bow" }, def.ItemKinds);
            Assert.False(def.Nullify);
            Assert.Equal(2, def.Damage);

            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""when"": { ""elements"": [] }, ""effect"": { ""damage"": 2 } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""dealDamage"", ""when"": { ""itemKinds"": [] }, ""effect"": { ""damage"": 2 } }"));
        }

        [Fact]
        public void Modifier_NullifyParses_WithZeroDamage()
        {
            var immune = ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""takeDamage"", ""visibility"": ""hidden"", ""when"": { ""elements"": [ ""fire"" ] }, ""effect"": { ""nullify"": true } }");
            Assert.True(immune.Nullify);
            Assert.Equal(0, immune.Damage);
            Assert.True(immune.IsHidden);
        }

        [Fact]
        public void Modifier_NullifyAndDamage_AreExclusive()
        {
            var both = Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""takeDamage"", ""effect"": { ""nullify"": true, ""damage"": -1 } }"));
            Assert.Contains("mutually exclusive", both.Message);
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""takeDamage"", ""effect"": { ""nullify"": false } }"));
            Assert.Throws<MapLoadException>(() => ModifierDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""trigger"": ""takeDamage"", ""effect"": { } }"));
            Assert.Throws<ArgumentException>(() => new ModifierDef("x", "x", ModifierTriggers.TakeDamage, -1, nullify: true));
            Assert.Throws<ArgumentException>(() => new ModifierDef("x", "x", ModifierTriggers.TakeDamage, 0));
        }
    }
}
