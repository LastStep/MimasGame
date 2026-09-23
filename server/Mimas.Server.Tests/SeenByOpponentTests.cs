using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Sdk;

namespace Mimas.Server.Tests;

/// <summary>
/// What the opponent has seen of your own hero, over a real socket (ADR-039, spec 2026-09-23-hud-restyle §5,
/// §9): the <c>seen</c> / <c>lineageSeen</c> keys ride only on the unit that is the receiver's own, and an
/// ability of yours flips to seen in your next view once the other seat has watched you use it. No server
/// code builds them — the room's views come from <c>PlayerView.Build</c> — so these are the proof the flag
/// arrives by itself.
/// </summary>
public class SeenByOpponentTests
{
    private static Dictionary<string, string> UnhurriedClock()
    {
        var settings = new Dictionary<string, string>(MimasServerFactory.FastClock);
        settings["Mimas:TurnMs"] = "20000";
        settings["Mimas:ReconnectGraceMs"] = "20000";
        return settings;
    }

    private static JObject UnitJson(JObject view, bool mine) =>
        view["units"]!.Cast<JObject>().Single(u => u.Value<bool>("mine") == mine);

    private static IEnumerable<JObject> Entries(JObject unit) =>
        new[] { "abilities", "modifiers", "boons" }.SelectMany(list => unit[list]!.Cast<JObject>());

    private static void AssertSeenKeysOnlyOnOwnUnit(JObject view)
    {
        JObject own = UnitJson(view, mine: true);
        Assert.Equal(JTokenType.Boolean, own["lineageSeen"]!.Type);
        Assert.All(Entries(own), e => Assert.Equal(JTokenType.Boolean, e["seen"]!.Type));

        JObject enemy = UnitJson(view, mine: false);
        Assert.Null(enemy["lineageSeen"]);
        Assert.All(Entries(enemy), e => Assert.Null(e["seen"]));
    }

    [Fact]
    public async Task SeenFlags_OverSockets_OnlyOnOwnUnit()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            AssertSeenKeysOnlyOnOwnUnit((JObject)hostStart["view"]!);
            AssertSeenKeysOnlyOnOwnUnit((JObject)guestStart["view"]!);

            // Read back through the client's codec, the enemy's entries are never flagged.
            foreach (JObject start in new[] { hostStart, guestStart })
            {
                PlayerView view = Wire.ReadView((JObject)start["view"]!);
                UnitView enemy = view.Units.Single(u => !u.IsMine);
                Assert.DoesNotContain(enemy.Abilities.Concat(enemy.Modifiers).Concat(enemy.Boons), e => e.SeenByOpponent);
                Assert.False(enemy.LineageSeenByOpponent);
            }
        }
    }

    [Fact]
    public async Task SeenFlags_OverSockets_FlipAfterOpponentSeesAbility()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            (MirrorPlayer mover, FakeClient moverClient, MirrorPlayer watcher, FakeClient watcherClient) =
                a.IsMyTurn ? (a, host, b, guest) : (b, guest, a, host);
            Assert.True(mover.IsMyTurn);

            // The mover walks: the first legal move the mirror offers.
            var legal = new List<Command>();
            mover.Mirror.EnumerateLegal(mover.Seat, legal);
            MoveCommand move = legal.OfType<MoveCommand>().FirstOrDefault()
                ?? throw new XunitException("the first mover had no legal move");
            UnitView before = mover.View.Units.Single(u => u.IsMine);
            Assert.False(before.Abilities.Single(e => e.Id == move.AbilityId).SeenByOpponent);

            await mover.SendAsync(move);

            // The mover's next view says the watcher has now seen that ability of theirs.
            JObject moved = await moverClient.ExpectAsync(Messages.MatchEvents, TimeSpan.FromSeconds(20));
            mover.Absorb(moved);
            UnitView after = mover.View.Units.Single(u => u.IsMine);
            Assert.True(after.Abilities.Single(e => e.Id == move.AbilityId).SeenByOpponent);

            // The watcher learnt the ability, and their view of the mover carries no seen key at all.
            JObject watched = await watcherClient.ExpectAsync(Messages.MatchEvents, TimeSpan.FromSeconds(20));
            watcher.Absorb(watched);
            JObject theirs = UnitJson((JObject)watched["view"]!, mine: false);
            Assert.Contains(theirs["abilities"]!.Cast<JObject>(), e => e.Value<string>("id") == move.AbilityId);
            Assert.All(Entries(theirs), e => Assert.Null(e["seen"]));
            Assert.Null(theirs["lineageSeen"]);
        }
    }
}
