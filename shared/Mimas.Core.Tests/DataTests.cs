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

        internal static string Read(string relativePath) =>
            File.ReadAllText(Path.Combine(DataRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));

        internal static string TerrainsJson => Read("terrains.json");
        internal static string Arena4Json => Read("maps/arena-4.json");
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
        public void BuildTileMap_Arena4_Yields61TilesWith12Unwalkable()
        {
            var tiles = MapData.FromJson(RepoData.Arena4Json).BuildTileMap(Terrains());
            Assert.Equal(61, tiles.Count);
            Assert.Equal(12, tiles.Tiles.Count(t => !t.Walkable));
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
            HexAt(tree, 1, 1)["terrain"] = "grass";      // twin (-1,-1) stays stone
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
            tree["spawns"]["p1"]["q"] = -3;             // (-3,0) is a stone pillar
            tree["spawns"]["p2"]["q"] = 3;
            var map = MapData.FromJson(tree.ToString());
            Assert.Throws<MapLoadException>(() => map.BuildTileMap(Terrains()));
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
            var map = Arena4();
            var start = new Hex(-4, 0);
            var goal = new Hex(4, 0);
            var path = map.FindPath(start, goal);
            Assert.Equal(11, path.Count);                                   // 10 steps vs a raw distance of 8
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
            var map = Arena4();
            Assert.Null(map.FindPath(new Hex(-4, 0), new Hex(3, 0)));       // (3,0) is a stone pillar
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
}
