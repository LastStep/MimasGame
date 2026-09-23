#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    /// <summary>
    /// How a stat is made (spec E §4.1; design #stats rule 3, docs/ui/examine.md §5 gap 1): the rules base,
    /// each item in slot order, each boon in grant order; raw amounts; and on the mirror only what the viewer
    /// has been shown.
    /// </summary>
    public class StatLinesTests
    {
        private static readonly string[] Keys = { "hp", "ap", "power.ranged", "power.melee", "defense.ranged", "defense.melee" };

        private static Unit Build(ContentCatalog catalog, Loadout loadout, params string[] boons)
        {
            var items = new List<ItemDef>();
            for (int i = 0; i < ItemSlots.All.Length; i++)
                items.Add(catalog.GetItemForSlot(ItemSlots.All[i], loadout.IdForSlot(ItemSlots.All[i])));
            return new Unit(0, 0, Hex.Zero, catalog.Rules, items, "trial", boons.Select(catalog.GetBoon).ToList());
        }

        private static List<StatLine> Lines(Unit unit, string key)
        {
            var lines = new List<StatLine>();
            unit.StatLines(key, lines);
            return lines;
        }

        [Fact]
        public void StatLines_BaseAndItems_SumToPublicStats()
        {
            var catalog = CombatFixtures.Catalog();
            Unit unit = Build(catalog, CombatFixtures.Brute);
            foreach (string key in Keys)
            {
                var lines = Lines(unit, key);
                Assert.All(lines, l => Assert.NotEqual(StatLine.BoonKind, l.SourceKind));
                Assert.Equal(unit.PublicStats.Get(key), lines.Sum(l => l.Amount));
            }
        }

        [Fact]
        public void StatLines_WithStatBlessing_SumsToStats()
        {
            var catalog = CombatFixtures.Catalog();
            Unit unit = Build(catalog, CombatFixtures.Archer, "trial-vigour");
            var lines = Lines(unit, "hp");

            Assert.Equal(new[] { "base", "item", "boon" }, lines.Select(l => l.SourceKind));
            Assert.Equal("archer-vest", lines[1].SourceId);
            Assert.Equal("trial-vigour", lines[2].SourceId);
            Assert.Equal(4, lines[2].Amount);
            Assert.Equal(unit.Stats.Hp, lines.Sum(l => l.Amount));
        }

        [Fact]
        public void StatLines_ItemWithoutKey_AddsNoLine()
        {
            var catalog = CombatFixtures.Catalog();
            Unit unit = Build(catalog, CombatFixtures.Archer);
            var lines = Lines(unit, "power.ranged");

            Assert.Equal("archer-bow", Assert.Single(lines).SourceId);
            Assert.DoesNotContain(lines, l => l.SourceId == "bare-boots" || l.SourceId == "archer-vest");
        }

        [Fact]
        public void StatLines_Order_BaseThenSlotOrderThenGrantOrder()
        {
            var catalog = CombatFixtures.Catalog();
            // Granted trade before vigour, the reverse of id order: grant order decides.
            Unit unit = Build(catalog, CombatFixtures.Archer, "trial-trade", "trial-vigour");
            var lines = Lines(unit, "hp");

            Assert.Equal(new[] { "base", "item", "boon", "boon" }, lines.Select(l => l.SourceKind));
            Assert.Equal(new[] { null, "archer-vest", "trial-trade", "trial-vigour" }, lines.Select(l => l.SourceId));
        }

        [Fact]
        public void StatLines_FlooredStat_ListsRawLines()
        {
            var catalog = CombatFixtures.Catalog();
            Unit unit = Build(catalog, CombatFixtures.Archer, "trial-trade");
            var lines = Lines(unit, "hp");

            Assert.Equal(-30, lines.Single(l => l.SourceKind == StatLine.BoonKind).Amount);
            Assert.Equal(2 + 10 - 30, lines.Sum(l => l.Amount));
            Assert.Equal(1, unit.Stats.Hp);                          // rules.boons.floors.hp
        }

        [Fact]
        public void StatLines_OnMirror_KnowsOnlyRevealedBoons()
        {
            var catalog = CombatFixtures.Catalog();
            var truth = CombatFixtures.StartedWith(catalog, CombatFixtures.ArcherWith("trial-might"), CombatFixtures.BruteWith(), new Hex(-1, 1), Hex.Zero);
            Assert.Contains(Lines(truth.Units.Get(0), "power.ranged"), l => l.SourceId == "trial-might");

            MatchState mirror = MatchState.FromView(catalog, truth.ViewFor(1));
            Unit theirs = mirror.Units.Get(0);
            var lines = Lines(theirs, "power.ranged");

            Assert.Equal("archer-bow", Assert.Single(lines).SourceId);
            Assert.Equal(theirs.PublicStats.Get("power.ranged"), lines.Sum(l => l.Amount));
        }
    }
}
