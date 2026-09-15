#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Movement;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    internal static class ContentFixtures
    {
        /// <summary>The shipped data folder, exactly as the client and server will load it.</summary>
        internal static List<ContentFile> Repo() => ContentFiles.FromDirectory(RepoData.DataRoot);

        internal static ContentCatalog RepoCatalog() => ContentCatalog.Load(Repo());

        internal static List<ContentFile> Minimal() => new List<ContentFile>
        {
            new ContentFile("terrains.json", @"{ ""version"": 1, ""terrains"": [ { ""id"": ""grass"", ""walkable"": true } ] }"),
            new ContentFile("rules.json", @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""ranged"", ""magic"" ] }"),
            new ContentFile("abilities/move.json", @"{ ""version"": 1, ""id"": ""move"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 1 } }"),
            new ContentFile("classes/scout.json", @"{ ""version"": 1, ""id"": ""scout"", ""abilities"": [ ""move"" ], ""stats"": { ""hp"": 10, ""ap"": 3 } }"),
        };

        internal static string ErrorsOf(List<ContentFile> files)
        {
            var e = Assert.Throws<ContentLoadException>(() => ContentCatalog.Load(files));
            return string.Join("\n", e.Errors.Select(x => x.ToString()));
        }
    }

    public class ContentCatalogTests
    {
        [Fact]
        public void Load_ShippedDataFolder_HasNoErrors()
        {
            var catalog = ContentFixtures.RepoCatalog();
            Assert.True(catalog.Terrains.Count >= 2);
            Assert.Equal(new[] { "fire-bolt", "jab", "jump", "move", "strike", "teleport" }, catalog.Abilities.All.Select(a => a.Id).ToArray());
            Assert.Equal(new[] { "high-ground", "ward-of-feathers" }, catalog.Modifiers.All.Select(m => m.Id).ToArray());
            Assert.Equal(new[] { "melee", "ranged", "magic" }, catalog.Rules.DamageTypes);
            Assert.Equal(new[] { "mage", "warrior" }, catalog.Classes.All.Select(c => c.Id).ToArray());
            Assert.Contains("arena-4", catalog.Maps.All.Select(m => m.Id));
            Assert.Equal(3, catalog.TimeControls.Count);
            Assert.Equal(3, catalog.Movements.Count);
            Assert.Equal(64, catalog.Hash.Length);
        }

        [Fact]
        public void Load_ShippedDataFolder_EveryFileIsAccountedFor()
        {
            var files = ContentFixtures.Repo();
            var catalog = ContentCatalog.Load(files);
            Assert.Equal(files.Select(f => f.Path).OrderBy(p => p, System.StringComparer.Ordinal), catalog.Files);
        }

        [Fact]
        public void Load_Minimal_Succeeds()
        {
            var catalog = ContentCatalog.Load(ContentFixtures.Minimal());
            Assert.Equal(1, catalog.Classes.Count);
            Assert.NotNull(catalog.GetMovement("move"));
            Assert.Null(catalog.GetMovement("nope"));
        }

        [Fact]
        public void Load_MissingTerrains_IsAnError()
        {
            var files = ContentFixtures.Minimal().Where(f => f.Path != "terrains.json").ToList();
            Assert.Contains("terrains.json: missing", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Load_UnrecognisedFile_FailsClosed()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("spells/fireball.json", "{}"));
            Assert.Contains("spells/fireball.json: unrecognised", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Load_ClassReferencingUnknownAbility_ErrorNamesTheClassFile()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("classes/broken.json", @"{ ""version"": 1, ""id"": ""broken"", ""abilities"": [ ""move"", ""fly"" ], ""stats"": { ""hp"": 10, ""ap"": 3 } }"));
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("classes/broken.json: class 'broken' references unknown ability 'fly'", errors);
        }

        [Fact]
        public void Load_ClassWithoutMovement_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("classes/statue.json", @"{ ""version"": 1, ""id"": ""statue"", ""abilities"": [], ""stats"": { ""hp"": 10, ""ap"": 3 } }"));
            Assert.Contains("class 'statue' has no movement ability", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Load_MovementOverridingUnknownTerrain_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/swim.json",
                @"{ ""version"": 1, ""id"": ""swim"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 2, ""terrainCosts"": { ""water"": 1 } } }"));
            Assert.Contains("abilities/swim.json: movement 'swim' overrides unknown terrain 'water'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Load_DuplicateIdAcrossFiles_ReportsBothFiles()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/move2.json", @"{ ""version"": 1, ""id"": ""move"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 3 } }"));
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("duplicate ability id 'move'", errors);
            Assert.Contains("abilities/move.json", errors);
        }

        [Fact]
        public void Load_InvalidMap_ErrorNamesTheMapFile()
        {
            var files = ContentFixtures.Minimal();
            // Radius-1 ring with no centre is not connected and its spawns are not at max distance among walkable tiles.
            files.Add(new ContentFile("maps/bad.json",
                @"{ ""version"": 1, ""id"": ""bad"", ""name"": ""Bad"", ""symmetry"": ""rotational-180"", ""ladderPosition"": 1,
                    ""spawns"": { ""p1"": { ""q"": 0, ""r"": 0 }, ""p2"": { ""q"": 0, ""r"": 0 } },
                    ""hexes"": [ { ""q"": 1, ""r"": 0, ""terrain"": ""grass"" }, { ""q"": -1, ""r"": 0, ""terrain"": ""grass"" } ] }"));
            Assert.StartsWith("maps/bad.json:", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Load_CollectsEveryError_NotJustTheFirst()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("classes/a.json", @"{ ""version"": 1, ""id"": ""a"", ""abilities"": [ ""x"" ], ""stats"": { ""hp"": 10, ""ap"": 3 } }"));
            files.Add(new ContentFile("classes/b.json", @"{ ""version"": 1, ""id"": ""b"", ""abilities"": [ ""y"" ], ""stats"": { ""hp"": 10, ""ap"": 3 } }"));
            files.Add(new ContentFile("readme.txt", "hello"));
            var e = Assert.Throws<ContentLoadException>(() => ContentCatalog.Load(files));
            Assert.True(e.Errors.Count >= 3, e.Message);
        }

        [Fact]
        public void Load_UnsupportedAbilityType_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/sing.json", @"{ ""version"": 1, ""id"": ""sing"", ""type"": ""song"" }"));
            Assert.Contains("unsupported type 'song'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Tables_AreSortedById_WithSequentialIndices()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var ids = catalog.Abilities.All.Select(a => a.Id).ToList();
            Assert.Equal(ids.OrderBy(i => i, System.StringComparer.Ordinal), ids);
            for (int i = 0; i < ids.Count; i++)
            {
                Assert.Equal(i, catalog.Abilities.IndexOf(ids[i]));
                Assert.Same(catalog.Abilities.ByIndex(i), catalog.Abilities.Get(ids[i]));
            }
            Assert.Equal(-1, catalog.Abilities.IndexOf("missing"));
            Assert.Throws<KeyNotFoundException>(() => catalog.Classes.Get("missing"));
        }

        [Fact]
        public void Hash_IsStableAcrossLoads_AndIndependentOfFileOrderAndFormatting()
        {
            var files = ContentFixtures.Repo();
            string a = ContentCatalog.Load(files).Hash;

            files.Reverse();
            string b = ContentCatalog.Load(files).Hash;

            // Reformat one file: different whitespace, CRLF, and key order must not change the hash.
            var reformatted = files.Select(f => f.Path == "abilities/move.json"
                ? new ContentFile(f.Path, "{\r\n \"movement\": {\"maxClimb\":1,\"range\":1,\"mode\":\"walk\"},\r\n\"type\":\"movement\",\"icon\":\"move\",\"description\":\"Walk to an adjacent tile. The default movement every unit has.\",\"name\":\"Move\",\"id\":\"move\",\"version\":1}")
                : f).ToList();
            string c = ContentCatalog.Load(reformatted).Hash;

            Assert.Equal(a, b);
            Assert.Equal(a, c);
        }

        [Fact]
        public void Hash_ChangesWhenABalanceNumberChanges()
        {
            var files = ContentFixtures.Repo();
            string before = ContentHash.Compute(files);
            var edited = files.Select(f => f.Path == "abilities/jump.json" ? new ContentFile(f.Path, f.Text.Replace("\"range\": 2", "\"range\": 3")) : f).ToList();
            Assert.NotEqual(before, ContentHash.Compute(edited));
        }

        [Fact]
        public void Canonical_SortsKeysAndStripsWhitespace()
        {
            Assert.Equal("{\"a\":[1,true,null],\"b\":\"x\"}", ContentHash.Canonical("{ \"b\" : \"x\", \"a\" : [ 1 , true , null ] }"));
        }

        [Fact]
        public void ContentFile_NormalisesPaths()
        {
            Assert.Equal("abilities/jump.json", new ContentFile(".\\abilities\\jump.json", "{}").Path);
            Assert.Equal("maps/x.json", new ContentFile("/maps/x.json", "{}").Path);
        }
    }

    public class ClassAndUnitTests
    {
        [Fact]
        public void ClassDef_FromJson_DuplicateAbility_Throws()
        {
            Assert.Throws<MapLoadException>(() => ClassDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""abilities"": [ ""move"", ""move"" ], ""stats"": { ""hp"": 10, ""ap"": 3 } }"));
        }

        [Fact]
        public void Unit_FromClass_StartsWithClassAbilities_AndCanGainMore()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var unit = new Unit(0, 0, Hex.Zero, catalog.Classes.Get("warrior"));
            Assert.Equal("warrior", unit.ClassId);
            Assert.Equal(new[] { "move", "jump", "jab", "strike" }, unit.AbilityIds);
            Assert.True(unit.HasAbility("jump"));
            Assert.False(unit.HasAbility("teleport"));

            unit.AddAbility("teleport");
            unit.AddAbility("teleport");
            Assert.Equal(new[] { "move", "jump", "jab", "strike", "teleport" }, unit.AbilityIds);
            Assert.True(unit.RemoveAbility("jump"));
            Assert.False(unit.RemoveAbility("jump"));
        }

        [Fact]
        public void Unit_MovementAbilities_ResolveThroughCatalogue()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var map = catalog.Maps.Get("arena-4");
            var tiles = map.BuildTileMap(catalog.Terrains);
            var units = new UnitSet();
            var mage = new Unit(0, 0, map.SpawnP1, catalog.Classes.Get("mage"));
            units.Add(mage);

            var registry = MovementResolverRegistry.CreateDefault();
            var movements = mage.AbilityIds.Select(catalog.GetMovement).Where(m => m != null).ToList();
            Assert.Equal(new[] { "move", "teleport" }, movements.Select(m => m.Id));
            foreach (var movement in movements)
            {
                var options = registry.Enumerate(MovementContext.For(tiles, catalog.Terrains, units, mage, movement));
                Assert.True(options.Count > 0, movement.Id);
            }
        }

        [Fact]
        public void TimeControls_LoadFromRepoFile()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var blitz = catalog.TimeControls.Get("3+2");
            Assert.Equal(180000, blitz.BaseMs);
            Assert.Equal(2000, blitz.IncrementMs);
            Assert.Equal(45000, blitz.TurnCapMs);
        }
    }
}
