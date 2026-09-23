using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mimas.Client.Presentation;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using NUnit.Framework;
using UnityEngine;

namespace Mimas.Client.Tests
{
    /// <summary>
    /// The HUD's composition rules (spec H §7.2, §9; docs/ui/hud.md, between-rounds.md): the turn track's
    /// burning fraction, when End Turn lights, the score by seat, the round moments, the action bar's flags
    /// built from the examine builder's tiles, and the preview's one "?" row. The action tests run on the
    /// shipped content with the pinned render's round-3 Greek (Longbow, Ember Circlet, Leaping Boots, Leather
    /// Jerkin; Athena's Guard, Hera's Resolve, Apollo's Bowstring, Nike's Jab), as ExamineModelTests does.
    /// </summary>
    public class HudModelTests
    {
        private static ContentCatalog _catalog;

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

        private static MatchState Round3()
        {
            var mine = new PlayerBuild(new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin"), "greek",
                new[] { "athena-guard", "hera-resolve", "apollo-bowstring", "nike-jab" });
            var theirs = new PlayerBuild(new Loadout("flintlock", "ember-circlet", "blink-boots", "leather-jerkin"), "norse",
                new[] { "thor-vigour", "thor-might" });
            var state = new MatchState(Catalog(), new MatchSetup("arena-4", mine, theirs), 7);
            state.Start();
            return state;
        }

        private static List<HudAction> Actions(MatchState truth)
        {
            var abilities = new List<AbilityDef>();
            HudModel.CollectAbilities(truth, 0, abilities);
            var actions = new List<HudAction>();
            HudModel.BuildActions(Catalog(), truth.ViewFor(0), truth, 0, abilities, true, actions);
            return actions;
        }

        private static HudAction Action(List<HudAction> actions, string id) => actions.First(a => a.Id == id);

        private static HudAction Affordable(bool affordable) =>
            new HudAction("a", "A", null, null, null, "weapon", 2, affordable, affordable);

        [Test]
        public void LitFraction_OutsideRope_IsOne()
        {
            Assert.AreEqual(1f, HudModel.LitFraction(25f, 10f));
            Assert.AreEqual(1f, HudModel.LitFraction(10f, 10f));
        }

        [Test]
        public void LitFraction_InsideRope_ShrinksToZero()
        {
            Assert.AreEqual(0.5f, HudModel.LitFraction(5f, 10f), 1e-5f);
            Assert.AreEqual(0.1f, HudModel.LitFraction(1f, 10f), 1e-5f);
            Assert.AreEqual(0f, HudModel.LitFraction(0f, 10f));
            Assert.AreEqual(0f, HudModel.LitFraction(-2f, 10f));
        }

        [Test]
        public void IsDone_NoAffordableAction_True()
        {
            Assert.IsTrue(HudModel.IsDone(true, new List<HudAction> { Affordable(false), Affordable(false) }));
            Assert.IsFalse(HudModel.IsDone(true, new List<HudAction> { Affordable(false), Affordable(true) }));
        }

        [Test]
        public void IsDone_TheirTurn_False()
        {
            Assert.IsFalse(HudModel.IsDone(false, new List<HudAction> { Affordable(false) }));
        }

        [Test]
        public void ScoresBySeat_Seat1_YoursFirst()
        {
            (int mine, int theirs) = HudModel.ScoresBySeat(2, 1, 1);
            Assert.AreEqual(1, mine);
            Assert.AreEqual(2, theirs);
            (mine, theirs) = HudModel.ScoresBySeat(2, 1, 0);
            Assert.AreEqual(2, mine);
            Assert.AreEqual(1, theirs);
        }

        [Test]
        public void Moment_RoundResult_WinnerIsMe_IWonTrue()
        {
            HudMoment moment = HudModel.RoundResult(2, 1, 1, "Rohan", 0, 1, "by elimination");
            Assert.AreEqual(HudMomentKind.RoundResult, moment.Kind);
            Assert.IsTrue(moment.IWon);
            Assert.AreEqual(1, moment.ScoreMine);
            Assert.AreEqual(0, moment.ScoreTheirs);
            Assert.AreEqual("Rohan", moment.WinnerName);
            Assert.IsFalse(moment.ShowBack, "a round's result has no way out: the series goes on");
            Assert.IsFalse(HudModel.RoundResult(2, 0, 1, "Guest-2869", 1, 0, "by elimination").IWon);
        }

        [Test]
        public void Moment_RoundStart_OpponentFirst_IMoveFirstFalse()
        {
            HudMoment moment = HudModel.RoundStart(3, "Open Field", 1, 0);
            Assert.AreEqual(HudMomentKind.RoundStart, moment.Kind);
            Assert.AreEqual(3, moment.Round);
            Assert.AreEqual("Open Field", moment.MapName);
            Assert.IsFalse(moment.IMoveFirst);
            Assert.IsTrue(HudModel.RoundStart(3, "Open Field", 0, 0).IMoveFirst);
        }

        [Test]
        public void Action_OwnUnusedAbility_UnseenByThem()
        {
            MatchState truth = Round3();
            HudAction arrow = Action(Actions(truth), "arrow-shot");
            Assert.IsTrue(arrow.UnseenByThem);
            Assert.IsNotNull(arrow.Hover, "the bar's hover is the plate's tile");
            Assert.IsTrue(arrow.Hover.UnseenByThem);
            Assert.IsTrue(Action(Actions(truth), "move").UnseenByThem);

            // Walking shows the walk to the opponent; the bow is still a surprise.
            MovementOptions options = truth.MoveOptions(0, "move");
            Assert.IsTrue(options.Plans.Count > 0);
            truth.Apply(new MoveCommand(0, 0, "move", options.Plans[0].Destination));
            List<HudAction> after = Actions(truth);
            Assert.IsFalse(Action(after, "move").UnseenByThem);
            Assert.IsTrue(Action(after, "arrow-shot").UnseenByThem);
        }

        [Test]
        public void Action_SigilGrant_Added()
        {
            List<HudAction> actions = Actions(Round3());
            HudAction jab = Action(actions, "jab");
            Assert.IsTrue(jab.Added);
            Assert.IsTrue(jab.Hover.Added);
            HudAction arrow = Action(actions, "arrow-shot");
            Assert.IsFalse(arrow.Added);
            Assert.IsTrue(arrow.Modified, "Apollo's Bowstring changed its reach: the diamond, not the triangle");
        }

        [Test]
        public void Preview_HiddenLine_OneRowWithTheLanguageWording()
        {
            var lines = new List<HudPreviewLine> { new HudPreviewLine { Label = "Base", Amount = 3 } };
            HudModel.AddHiddenLine(lines, 3);
            Assert.AreEqual(2, lines.Count, "one row, whatever the count");
            Assert.IsTrue(lines[1].Unknown);
            Assert.AreEqual("A boon of theirs you have not seen", lines[1].Label);

            var none = new List<HudPreviewLine>();
            HudModel.AddHiddenLine(none, 0);
            Assert.AreEqual(0, none.Count);
        }
    }
}
