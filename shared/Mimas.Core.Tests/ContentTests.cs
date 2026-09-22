#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
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
            new ContentFile("rules.json", @"{ ""version"": 1, ""damageTypes"": [ ""melee"", ""ranged"", ""magic"" ],
                ""heights"": { ""unitsPerLevel"": 3, ""body"": 6, ""aim"": 4 }, ""baseStats"": { ""hp"": 10, ""ap"": 3 }, ""innateAbilities"": [ ""move"" ], ""clock"": { ""turnMs"": 30000, ""lagGraceMs"": 1000, ""reconnectGraceMs"": 60000 },
                ""elements"": [ ""fire"", ""frost"", ""lightning"" ], ""series"": { ""bestOf"": 3 }, ""draft"": { ""offers"": { ""winner"": 3, ""loser"": 3 }, ""timeoutMs"": 20000 }, ""boons"": { ""floors"": { ""hp"": 1, ""ap"": 1 }, ""minCost"": 1 } }"),
            new ContentFile("abilities/move.json", @"{ ""version"": 1, ""id"": ""move"", ""type"": ""movement"", ""movement"": { ""mode"": ""walk"", ""range"": 1 } }"),
        };

        /// <summary>The bow kit from the shipped catalogue.</summary>
        internal static readonly Loadout BowKit = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");

        /// <summary>The gun kit from the shipped catalogue: the other weapon and the other boots.</summary>
        internal static readonly Loadout GunKit = new Loadout("flintlock", "ember-circlet", "blink-boots", "leather-jerkin");

        /// <summary>A hero built from a loadout, resolving every slot through the catalogue.</summary>
        internal static Unit HeroFrom(ContentCatalog catalog, Loadout loadout, Hex at)
        {
            var items = new List<ItemDef>(ItemSlots.All.Length);
            for (int i = 0; i < ItemSlots.All.Length; i++)
                items.Add(catalog.GetItemForSlot(ItemSlots.All[i], loadout.IdForSlot(ItemSlots.All[i])));
            return new Unit(0, 0, at, catalog.Rules, items);
        }

        /// <summary>A build: gear, a lineage and boons in grant order (spec D part 1 §9).</summary>
        internal static PlayerBuild BuildFor(Loadout loadout, string lineage, params string[] boons) => new PlayerBuild(loadout, lineage, boons);

        /// <summary>A hero built from a build, resolving gear, lineage and boons through the catalogue, with the given owner and id.</summary>
        internal static Unit HeroFrom(ContentCatalog catalog, PlayerBuild build, Hex at, int id = 0, int owner = 0)
        {
            var items = new List<ItemDef>(ItemSlots.All.Length);
            for (int i = 0; i < ItemSlots.All.Length; i++)
                items.Add(catalog.GetItemForSlot(ItemSlots.All[i], build.Loadout.IdForSlot(ItemSlots.All[i])));
            var boons = new List<BoonDef>();
            foreach (string boonId in build.BoonIds) boons.Add(catalog.GetBoon(boonId));
            return new Unit(id, owner, at, catalog.Rules, items, build.LineageId, boons);
        }

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
            // The eleven of the loadout slice, the six part 1 shipped (spec D part 1 §5.8) and part 2's storm-bolt.
            Assert.Equal(new[] { "aimed-shot", "arcane-spark", "arrow-shot", "dash", "ember-shot", "fire-bolt", "hammerfall", "heavy-shot", "jab", "jump", "long-leap", "move", "quick-shot", "storm-bolt", "strike", "teleport", "wind-step", "zeus-bolt" },
                catalog.Abilities.All.Select(a => a.Id).ToArray());
            // The three of the loadout slice plus the nine the boons ship.
            Assert.Equal(new[] { "agni-fire", "agni-warmth", "apollo-eye", "athena-plating", "high-ground", "indra-mail", "indra-wrath", "skadi-hide", "stone-skin", "thor-charge", "ward-of-feathers", "ymir-hide" },
                catalog.Modifiers.All.Select(m => m.Id).ToArray());
            Assert.Equal(new[] { "weapon", "spell" }, catalog.Rules.DamageTypes);
            Assert.Equal(new[] { "blink-boots", "ember-circlet", "flintlock", "leaping-boots", "leather-jerkin", "longbow" },
                catalog.Items.All.Select(i => i.Id).ToArray());
            Assert.Contains("arena-4", catalog.Maps.All.Select(m => m.Id));
            Assert.Equal(new[] { "pillar", "wall" }, catalog.Props.All.Select(p => p.Id).ToArray());
            Assert.Equal(3, catalog.TimeControls.Count);
            Assert.Equal(6, catalog.Movements.Count);
            Assert.Equal(30, catalog.Boons.Count);                                  // 21 from part 1 plus part 2's nine
            Assert.Equal(new[] { "greek", "hindu", "norse" }, catalog.Lineages.All.Select(l => l.Id).ToArray());
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
            Assert.Equal(0, catalog.Items.Count);
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
        public void Catalogue_ItemWithUnknownAbility_ReportsItemFile()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("items/x.json", @"{ ""version"": 1, ""id"": ""x"", ""slot"": ""crown"", ""kind"": ""odd"", ""stats"": {}, ""abilities"": [ ""nope"" ] }"));
            Assert.Contains("items/x.json: item 'x' references unknown ability 'nope'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_ItemGrantingInnateAbility_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("items/walker.json", @"{ ""version"": 1, ""id"": ""walker"", ""slot"": ""boots"", ""kind"": ""plain"", ""stats"": {}, ""abilities"": [ ""move"" ] }"));
            Assert.Contains("item 'walker' grants 'move', which is already innate (rules.innateAbilities)", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_WeaponWithoutAttack_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("items/stick.json", @"{ ""version"": 1, ""id"": ""stick"", ""slot"": ""weapon"", ""kind"": ""stick"", ""stats"": {}, ""abilities"": [] }"));
            Assert.Contains("item 'stick' is a weapon but grants no attack ability", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Schemas_AreIgnoredByTheLoader()
        {
            var files = ContentFixtures.Minimal()
                .Select(f => new ContentFile(f.Path, @"{ ""$schema"": ""../../tools/schemas/x.schema.json"", " + f.Text.Substring(f.Text.IndexOf('{') + 1)))
                .ToList();
            var catalog = ContentCatalog.Load(files);
            Assert.NotNull(catalog.GetMovement("move"));
        }

        [Fact]
        public void Catalogue_WeaponGrantingSpellAttack_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/zap.json", @"{ ""version"": 1, ""id"": ""zap"", ""type"": ""attack"", ""category"": ""spell"",
                ""attack"": { ""damage"": 1, ""damageType"": ""magic"", ""range"": 2, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            files.Add(new ContentFile("items/odd-bow.json", @"{ ""version"": 1, ""id"": ""odd-bow"", ""slot"": ""weapon"", ""kind"": ""bow"",
                ""stats"": {}, ""abilities"": [ ""zap"" ] }"));
            Assert.Contains("item 'odd-bow' is a weapon but grants attack 'zap' with category 'spell'; a weapon may only grant 'weapon' attacks",
                ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_CrownGrantingWeaponAttack_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/whack.json", @"{ ""version"": 1, ""id"": ""whack"", ""type"": ""attack"", ""category"": ""weapon"",
                ""attack"": { ""damage"": 1, ""damageType"": ""melee"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            files.Add(new ContentFile("items/odd-crown.json", @"{ ""version"": 1, ""id"": ""odd-crown"", ""slot"": ""crown"", ""kind"": ""circlet"",
                ""stats"": {}, ""abilities"": [ ""whack"" ] }"));
            Assert.Contains("item 'odd-crown' is a crown but grants attack 'whack' with category 'weapon'; a crown may only grant 'spell' attacks",
                ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_ArmourGrantingAnAttack_IsAllowed()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("abilities/spikes.json", @"{ ""version"": 1, ""id"": ""spikes"", ""type"": ""attack"", ""category"": ""weapon"",
                ""attack"": { ""damage"": 1, ""damageType"": ""melee"", ""range"": 1, ""trajectory"": ""direct"", ""lineOfSight"": true } }"));
            files.Add(new ContentFile("items/spiked-vest.json", @"{ ""version"": 1, ""id"": ""spiked-vest"", ""slot"": ""armour"", ""kind"": ""vest"",
                ""stats"": {}, ""abilities"": [ ""spikes"" ] }"));
            var catalog = ContentCatalog.Load(files);
            Assert.Equal(new[] { "spikes" }, catalog.Items.Get("spiked-vest").AbilityIds);
        }

        [Fact]
        public void Catalogue_UnknownInnateAbility_IsAnError()
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"[ ""move"" ]", @"[ ""move"", ""hover"" ]"))
                : f).ToList();
            Assert.Contains("rules.json: rules reference unknown innate ability 'hover'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_InnateWithoutMovement_IsAnError()
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"[ ""move"" ]", @"[ ""jab"" ]"))
                : f).ToList();
            Assert.Contains("rules.json: rules.innateAbilities must include at least one movement ability (usually 'move')", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_BaseStatsWithUndeclaredLane_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files = files.Select(f => f.Path == "rules.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"""baseStats"": { ""hp"": 10, ""ap"": 3 }", @"""baseStats"": { ""hp"": 10, ""ap"": 3, ""power.psychic"": 1 }"))
                : f).ToList();
            Assert.Contains("rules.json: rules.baseStats 'power.psychic' uses undeclared damage type 'psychic'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_DuplicateItemId_ReportsBothFiles()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("items/hat.json", @"{ ""version"": 1, ""id"": ""hat"", ""slot"": ""crown"", ""kind"": ""hat"", ""stats"": {}, ""abilities"": [] }"));
            files.Add(new ContentFile("items/hat2.json", @"{ ""version"": 1, ""id"": ""hat"", ""slot"": ""crown"", ""kind"": ""cap"", ""stats"": {}, ""abilities"": [] }"));
            string errors = ContentFixtures.ErrorsOf(files);
            Assert.Contains("duplicate item id 'hat'", errors);
            Assert.Contains("items/hat.json", errors);
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
            files.Add(new ContentFile("items/a.json", @"{ ""version"": 1, ""id"": ""a"", ""slot"": ""crown"", ""kind"": ""a"", ""stats"": {}, ""abilities"": [ ""x"" ] }"));
            files.Add(new ContentFile("items/b.json", @"{ ""version"": 1, ""id"": ""b"", ""slot"": ""crown"", ""kind"": ""b"", ""stats"": {}, ""abilities"": [ ""y"" ] }"));
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
            Assert.Throws<KeyNotFoundException>(() => catalog.Items.Get("missing"));
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
                ? new ContentFile(f.Path, "{\r\n \"movement\": {\"maxClimb\":1,\"range\":1,\"mode\":\"walk\"},\r\n\"type\":\"movement\",\"icon\":\"move\",\"description\":\"Walk to an adjacent tile. The default movement every unit has.\",\"name\":\"Move\",\"id\":\"move\",\"version\":1,\"$schema\":\"../../../../../tools/schemas/ability.schema.json\"}")
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

    /// <summary>Props in the catalogue: their own folder, their own link checks (spec A, D11/D12).</summary>
    public class PropCatalogTests
    {
        [Fact]
        public void Catalog_LoadsProps()
        {
            var catalog = ContentFixtures.RepoCatalog();
            Assert.Equal(2, catalog.Props.Count);
            Assert.True(catalog.Props.Get("pillar").IsDestructible);
            Assert.False(catalog.Props.Get("wall").IsDestructible);
            Assert.Contains("props/pillar.json", catalog.Files);
        }

        [Fact]
        public void Catalog_HashChangesWhenPropChanges()
        {
            var files = ContentFixtures.Repo();
            string before = ContentHash.Compute(files);
            var edited = files.Select(f => f.Path == "props/pillar.json"
                ? new ContentFile(f.Path, f.Text.Replace("\"hp\": 10", "\"hp\": 12"))
                : f).ToList();
            Assert.NotEqual(before, ContentHash.Compute(edited));
        }

        [Fact]
        public void Map_UnknownProp_LinkError()
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "maps/props-3.json"
                ? new ContentFile(f.Path, f.Text.Replace(@"""prop"": ""pillar""", @"""prop"": ""obelisk"""))
                : f).ToList();
            Assert.Contains("maps/props-3.json: map 'props-3' hex Hex(0,1) uses unknown prop id 'obelisk'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Map_PropOnUnwalkableTerrain_LinkError()
        {
            var files = CombatFixtures.Files();
            files = files.Select(f => f.Path == "maps/props-3.json"
                ? new ContentFile(f.Path, f.Text
                    .Replace(@"{ ""q"": 1, ""r"": 0, ""terrain"": ""grass"", ""prop"": ""wall"" }", @"{ ""q"": 1, ""r"": 0, ""terrain"": ""stone"", ""prop"": ""wall"" }")
                    .Replace(@"{ ""q"": -1, ""r"": 0, ""terrain"": ""grass"", ""prop"": ""wall"" }", @"{ ""q"": -1, ""r"": 0, ""terrain"": ""stone"", ""prop"": ""wall"" }"))
                : f).ToList();
            Assert.Contains("carries prop 'wall' on unwalkable terrain 'stone'", ContentFixtures.ErrorsOf(files));
        }

        [Fact]
        public void Catalogue_PropStatWithUndeclaredLane_IsAnError()
        {
            var files = ContentFixtures.Minimal();
            files.Add(new ContentFile("props/odd.json", @"{ ""version"": 1, ""id"": ""odd"", ""bodyHeight"": 3, ""aimHeight"": 1,
                ""stats"": { ""hp"": 4, ""defense.psychic"": 1 } }"));
            Assert.Contains("props/odd.json: prop 'odd' stat 'defense.psychic' uses undeclared damage type 'psychic'", ContentFixtures.ErrorsOf(files));
        }
    }

    /// <summary>The shipped catalogue's aiming fields (spec A, D7): what each weapon and crown actually does.</summary>
    public class ShippedAimingTests
    {
        [Fact]
        public void ShippedAttacks_AllDeclareTrajectoryAndSight()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var attacks = catalog.Abilities.All.OfType<AttackDef>().ToList();
            Assert.Equal(12, attacks.Count);                      // 8 from the loadout slice, 4 Sigil attacks from the boons
            Assert.All(attacks, a => Assert.True(Trajectories.IsKnown(a.Trajectory), a.Id));
            Assert.All(attacks, a => Assert.True(a.Apex == 0 || a.Trajectory == Trajectories.Arc, a.Id));
        }

        [Fact]
        public void ShippedBow_IsArcWithoutSight()
        {
            var catalog = ContentFixtures.RepoCatalog();
            Assert.Equal(Trajectories.Arc, catalog.GetAttack("arrow-shot").Trajectory);
            Assert.Equal(3, catalog.GetAttack("arrow-shot").Apex);
            Assert.False(catalog.GetAttack("arrow-shot").LineOfSight);

            Assert.Equal(Trajectories.Arc, catalog.GetAttack("aimed-shot").Trajectory);
            Assert.Equal(4, catalog.GetAttack("aimed-shot").Apex);
            Assert.False(catalog.GetAttack("aimed-shot").LineOfSight);
        }

        [Fact]
        public void ShippedGunAndSpells_AreDirectWithSight()
        {
            var catalog = ContentFixtures.RepoCatalog();
            foreach (string id in new[] { "quick-shot", "heavy-shot", "fire-bolt", "arcane-spark" })
            {
                var attack = catalog.GetAttack(id);
                Assert.Equal(Trajectories.Direct, attack.Trajectory);
                Assert.True(attack.LineOfSight, id);
                Assert.Equal(0, attack.Apex);
            }
        }

        [Fact]
        public void ShippedRules_CarryHeights()
        {
            var heights = ContentFixtures.RepoCatalog().Rules.Heights;
            Assert.Equal(3, heights.UnitsPerLevel);
            Assert.Equal(6, heights.Body);
            Assert.Equal(4, heights.Aim);
        }
    }

    public class ItemAndUnitTests
    {
        [Fact]
        public void ItemDef_FromJson_DuplicateAbility_Throws()
        {
            Assert.Throws<MapLoadException>(() => ItemDef.FromJson(@"{ ""version"": 1, ""id"": ""x"", ""slot"": ""crown"", ""kind"": ""odd"", ""stats"": {}, ""abilities"": [ ""move"", ""move"" ] }"));
        }

        [Fact]
        public void Unit_FromLoadout_StartsWithInnateThenItemAbilities_AndCanGainMore()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var unit = ContentFixtures.HeroFrom(catalog, ContentFixtures.BowKit, Hex.Zero);
            Assert.Equal(new[] { "longbow", "ember-circlet", "leaping-boots", "leather-jerkin" }, unit.ItemIds);
            Assert.Equal(new[] { "move", "arrow-shot", "aimed-shot", "fire-bolt", "arcane-spark", "jump" }, unit.AbilityIds);
            Assert.Null(unit.AbilitySourceOf("move"));
            Assert.Equal("leaping-boots", unit.AbilitySourceOf("jump"));
            Assert.True(unit.HasAbility("jump"));
            Assert.False(unit.HasAbility("teleport"));

            unit.AddAbility("teleport");
            unit.AddAbility("teleport");
            Assert.Equal(new[] { "move", "arrow-shot", "aimed-shot", "fire-bolt", "arcane-spark", "jump", "teleport" }, unit.AbilityIds);
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
            var hero = ContentFixtures.HeroFrom(catalog, ContentFixtures.GunKit, map.SpawnP1);
            units.Add(hero);
            var bodies = new BodySet(units);

            var registry = MovementResolverRegistry.CreateDefault();
            var movements = hero.AbilityIds.Select(catalog.GetMovement).Where(m => m != null).ToList();
            Assert.Equal(new[] { "move", "teleport" }, movements.Select(m => m.Id));
            foreach (var movement in movements)
            {
                var options = registry.Enumerate(MovementContext.For(tiles, catalog.Terrains, bodies, hero, movement, catalog.Rules.Heights));
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
