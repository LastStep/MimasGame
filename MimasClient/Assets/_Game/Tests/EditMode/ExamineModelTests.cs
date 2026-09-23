using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mimas.Client.Presentation;
using Mimas.Core.Content;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Match;
using NUnit.Framework;
using UnityEngine;

namespace Mimas.Client.Tests
{
    /// <summary>
    /// The examine plate's model (spec E §7, §9; docs/ui/examine.md §3) built from the shipped content, the
    /// way practice mode builds it: the driver's state is the truth, and the plate must still show only what
    /// the viewer has been shown. Your hero is the pinned render's round-3 Greek: Longbow, Ember Circlet,
    /// Leaping Boots, Leather Jerkin; Athena's Guard, Hera's Resolve, Apollo's Bowstring, Nike's Jab.
    /// </summary>
    public class ExamineModelTests
    {
        private static ContentCatalog _catalog;

        private static readonly Loadout Greek = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");
        private static readonly Loadout Norse = new Loadout("flintlock", "ember-circlet", "blink-boots", "leather-jerkin");

        private static ContentCatalog Catalog()
        {
            if (_catalog != null) return _catalog;
            string root = Path.Combine(Application.dataPath, "_Game", "Data");
            var files = new List<ContentFile>();
            foreach (string path in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories).OrderBy(p => p, System.StringComparer.Ordinal))
            {
                string relative = path.Substring(root.Length + 1).Replace('\\', '/');
                files.Add(new ContentFile(relative, File.ReadAllText(path)));
            }
            _catalog = ContentCatalog.Load(files);
            return _catalog;
        }

        private static MatchState Round3(params string[] enemyBoons)
        {
            var mine = new PlayerBuild(Greek, "greek", new[] { "athena-guard", "hera-resolve", "apollo-bowstring", "nike-jab" });
            var theirs = new PlayerBuild(Norse, "norse", enemyBoons.Length == 0 ? new[] { "thor-vigour", "thor-might" } : enemyBoons);
            var state = new MatchState(Catalog(), new MatchSetup("arena-4", mine, theirs), 7);
            state.Start();
            return state;
        }

        private static HudExamine Build(MatchState truth, int unitId, IReadOnlyList<string> revealOrder = null)
            => ExamineModelBuilder.BuildUnit(Catalog(), truth.ViewFor(0), truth, unitId, unitId == 0 ? "You" : "Random Bot", revealOrder);

        [Test]
        public void Build_OwnUnit_SixStatsInInventoryOrder()
        {
            HudExamine model = Build(Round3(), 0);
            CollectionAssert.AreEqual(
                new[] { "hp", "ap", "power.weapon", "power.spell", "defense.weapon", "defense.spell" },
                model.Stats.Select(s => s.Id).ToArray());
            CollectionAssert.AreEqual(
                new[] { "hp", "ap", "strength", "magic", "armour-weapon", "armour-spell" },
                model.Stats.Select(s => s.Element).ToArray());
        }

        [Test]
        public void Build_OwnUnit_NetIsStatsMinusPublic()
        {
            HudExamine model = Build(Round3(), 0);
            HudStat hp = model.FindStat("hp");
            Assert.AreEqual(28, hp.Base);                                  // 20 base + 8 Leather Jerkin
            Assert.AreEqual(-4, hp.Net);                                   // Hera's Resolve: red
            Assert.AreEqual(3, model.FindStat("ap").Base);
            Assert.AreEqual(1, model.FindStat("ap").Net);                  // Hera's Resolve: green
            Assert.AreEqual(2, model.FindStat("defense.weapon").Base);
            Assert.AreEqual(1, model.FindStat("defense.weapon").Net);      // Athena's Guard
            Assert.AreEqual(0, model.FindStat("power.spell").Net);
            CollectionAssert.AreEqual(new[] { "base", "Leather Jerkin", "Hera's Resolve" }, hp.Lines.Select(l => l.Label).ToArray());
        }

        [Test]
        public void Build_EnemyWithUnrevealedBoon_LaneStatsHiddenHealthNot()
        {
            MatchState truth = Round3();                                    // Thor's Might (+1 Strength) is hidden
            HudExamine model = Build(truth, 1);
            Assert.IsFalse(model.FindStat("hp").Hidden);
            Assert.IsFalse(model.FindStat("ap").Hidden);
            foreach (string lane in new[] { "power.weapon", "power.spell", "defense.weapon", "defense.spell" })
                Assert.IsTrue(model.FindStat(lane).Hidden, lane);

            // The truth has the +1; the plate must not (the driver's state is the truth in practice).
            Assert.AreEqual(1, truth.Units.Get(1).Stats.Get("power.weapon") - truth.Units.Get(1).PublicStats.Get("power.weapon"));
            Assert.AreEqual(0, model.FindStat("power.weapon").Net);
            Assert.IsFalse(model.FindStat("power.weapon").Lines.Any(l => l.Label == "Thor's Might"));
            Assert.IsTrue(model.FindStat("power.weapon").Lines.Last().Unknown);
            Assert.AreEqual(4, model.FindStat("hp").Net);                   // Thor's Vigour is public from round start
        }

        [Test]
        public void Build_Tiles_OwnAbilitiesBeforeSigilGrants()
        {
            HudItem bow = Build(Round3(), 0).FindItem("weapon");
            CollectionAssert.AreEqual(new[] { "arrow-shot", "aimed-shot", "jab" }, bow.Tiles.Select(t => t.Id).ToArray());
            HudItem boots = Build(Round3(), 0).FindItem("boots");
            Assert.AreEqual("move", boots.Tiles[0].Id, "the innate walk comes first under the boots");
        }

        [Test]
        public void Build_Tiles_EnchantOverride_MarksChangedAndChangeLine()
        {
            HudTile arrow = Build(Round3(), 0).FindItem("weapon").Tiles.First(t => t.Id == "arrow-shot");
            Assert.IsTrue(arrow.Changed);
            Assert.IsFalse(arrow.Added);
            Assert.IsTrue(arrow.Numbers.RangeChanged);
            Assert.AreEqual(6, arrow.Numbers.RangeMax);
            CollectionAssert.Contains(arrow.Changes, "reach 5 → 6 · Apollo's Bowstring");
            StringAssert.StartsWith("3 base + 3 Strength", arrow.DamageLine);
        }

        [Test]
        public void Build_Tiles_SigilGrant_MarksAddedNoBoonNameOnItem()
        {
            HudItem bow = Build(Round3(), 0).FindItem("weapon");
            HudTile jab = bow.Tiles.First(t => t.Id == "jab");
            Assert.IsTrue(jab.Added);
            CollectionAssert.Contains(jab.Changes, "granted by Nike's Jab");

            // E7: nothing the plate draws for the item names the boon; only the hover panel's list does.
            foreach (string drawn in new[] { bow.Name, bow.StatLine, bow.Note })
                Assert.IsFalse(drawn != null && drawn.Contains("Nike"), drawn);
            CollectionAssert.Contains(bow.BoonNames, "Nike's Jab");
        }

        [Test]
        public void Build_EnemyTiles_UnrevealedAreUnseen()
        {
            HudItem gun = Build(Round3(), 1).FindItem("weapon");
            Assert.AreEqual(2, gun.Tiles.Count);
            Assert.IsTrue(gun.Tiles.All(t => !t.Revealed && t.Id == null && t.Numbers == null));
            Assert.AreEqual("You have seen 0 of its 2 abilities.", gun.SeenLine);
        }

        [Test]
        public void Build_Boons_StartingFirstThenGrantOrder_UnknownLast()
        {
            HudExamine mine = Build(Round3(), 0);
            CollectionAssert.AreEqual(new[] { "Athena's Guard", "Hera's Resolve", "Apollo's Bowstring", "Nike's Jab" }, mine.Boons.Select(b => b.Name).ToArray());
            Assert.IsTrue(mine.Boons[0].Starting);
            Assert.AreEqual("starting Blessing", mine.Boons[0].Badge);
            Assert.AreEqual("drafted", mine.Boons[1].Badge);
            Assert.AreEqual("Longbow", mine.Boons[2].OnItemName);
            Assert.IsNull(mine.Boons[0].OnItemName);

            // Theirs granted hidden-first; the revealed one still leads and the unknown goes last.
            HudExamine theirs = Build(Round3("thor-might", "thor-vigour"), 1);
            Assert.AreEqual("Thor's Vigour", theirs.Boons[0].Name);
            Assert.IsFalse(theirs.Boons[1].Revealed);
        }

        [Test]
        public void Build_EnemyBoons_RevealOrderWins()
        {
            // Both hp Blessings reveal at round start; the client saw Odin's first.
            MatchState truth = Round3("thor-vigour", "berserker-blood");
            HudExamine inGrantOrder = Build(truth, 1);
            CollectionAssert.AreEqual(new[] { "thor-vigour", "berserker-blood" }, inGrantOrder.Boons.Select(b => b.Id).ToArray());

            HudExamine inRevealOrder = Build(truth, 1, new[] { "berserker-blood", "thor-vigour" });
            CollectionAssert.AreEqual(new[] { "berserker-blood", "thor-vigour" }, inRevealOrder.Boons.Select(b => b.Id).ToArray());
        }

        [Test]
        public void Build_Height_FromMapTile()
        {
            MatchState truth = Round3();
            Tile high = truth.Map.Tiles.Where(t => t.Height > 0 && t.Walkable).OrderBy(t => t.Position.Q).ThenBy(t => t.Position.R).First();
            truth.Units.Get(0).MoveTo(high.Position);
            Assert.AreEqual(high.Height, Build(truth, 0).Height);

            truth.Units.Get(0).MoveTo(new Hex(-4, 0));
            Tile spawn;
            truth.Map.TryGet(new Hex(-4, 0), out spawn);
            Assert.AreEqual(spawn.Height, Build(truth, 0).Height);
        }
    }
}
