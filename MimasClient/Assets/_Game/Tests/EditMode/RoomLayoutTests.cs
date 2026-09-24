using Mimas.Client.UI;
using NUnit.Framework;

namespace Mimas.Client.Tests
{
    /// <summary>
    /// The room's rules (docs/ui/lobby.md §3; spec H §8, §9): you are drawn on the left whichever seat you hold,
    /// the preset you picked survives un-readying, and a seat the server said nothing about waits for a player.
    /// </summary>
    public class RoomLayoutTests
    {
        [Test]
        public void Sides_Seat0_YouLeft()
        {
            Assert.AreEqual(0, RoomLayout.LeftSeat(0), "seat 0 is drawn on the left");
            Assert.AreEqual(1, RoomLayout.RightSeat(0), "and the other seat on the right");
        }

        [Test]
        public void Sides_Seat1_YouLeft()
        {
            Assert.AreEqual(1, RoomLayout.LeftSeat(1), "seat 1 is drawn on the left too");
            Assert.AreEqual(0, RoomLayout.RightSeat(1), "and seat 0 on the right");
        }

        [Test]
        public void PresetTiles_IndexSurvivesUnready()
        {
            var choice = new RoomChoice(4);
            Assert.IsTrue(choice.SelectPreset(2));
            Assert.IsTrue(choice.ToggleReady());
            Assert.IsFalse(choice.SelectPreset(3), "the tiles are locked while ready");

            Assert.IsTrue(choice.UnReady(), "un-readying reports that the server must be told");

            Assert.IsFalse(choice.Ready);
            Assert.AreEqual(2, choice.PresetIndex, "the tile you picked is still the one selected");
        }

        [Test]
        public void OtherSeat_Missing_ShowsWaiting()
        {
            OtherSeatText missing = RoomLayout.Other(null);
            OtherSeatText empty = RoomLayout.Other(new RoomSeat { Name = null, Present = false });

            Assert.AreEqual("WAITING FOR A PLAYER", missing.Name);
            Assert.IsTrue(missing.Waiting);
            Assert.IsFalse(missing.Ready);
            Assert.AreEqual(string.Empty, missing.State);
            Assert.AreEqual("WAITING FOR A PLAYER", empty.Name);
            Assert.IsTrue(empty.Waiting);
        }

        [Test]
        public void LastResultLine_SeriesScore_YoursFirstInCapitals()
        {
            string line = RoomLayout.LastResultLine(true, "Guest-2869", true, 2, 1, "by elimination");

            Assert.AreEqual("VICTORY VS GUEST-2869 · SERIES 2 – 1", line);
        }

        [Test]
        public void LastResultLine_ResignedBeforeAnyRound_ReasonFollowsScore()
        {
            string line = RoomLayout.LastResultLine(true, "Guest-4417", true, 0, 0, "opponent resigned");

            Assert.AreEqual("VICTORY VS GUEST-4417 · SERIES 0 – 0 · OPPONENT RESIGNED", line);
        }
    }
}
