using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Sdk;

namespace Mimas.Server.Tests;

/// <summary>
/// How two friends end up in the same match (OPT-0001, spec amendment A1): a four-letter code, and gear
/// chosen after you can see who you are playing. On 10 October four friends in two arranged pairs each
/// need to reach the right person, which is a link, not a queue.
/// </summary>
public class RoomTests
{
    private static string SeatName(JObject state, int index) =>
        ((JArray)state["seats"]!)[index].Value<string>("name") ?? "";

    private static bool SeatReady(JObject state, int index) =>
        ((JArray)state["seats"]!)[index].Value<bool>("ready");

    private static bool SeatPresent(JObject state, int index) =>
        ((JArray)state["seats"]!)[index].Value<bool>("present");

    private static JArray Events(JObject payload) => (JArray)payload["events"]!;

    /// <summary>Gives up the match this seat is in, the way the HUD's two clicks do.</summary>
    private static Task ResignAsync(FakeClient client, JObject start) =>
        client.SendAsync(Messages.MatchCommand, new JObject
        {
            ["matchId"] = start.Value<int>("matchId"),
            ["command"] = Wire.Command(new ResignCommand(start.Value<int>("youAre"))),
        });

    /// <summary>
    /// Consumes <c>match.events</c> until the batch carrying the result, so a fast test clock ending a
    /// few turns on the way cannot be mistaken for the end of the match.
    /// </summary>
    private static async Task<JObject> ExpectMatchEndedAsync(FakeClient client, TimeSpan? budget = null)
    {
        DateTime deadline = DateTime.UtcNow + (budget ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            JObject payload = await client.ExpectAsync(Messages.MatchEvents, TimeSpan.FromSeconds(10));
            if (Events(payload).Any(e => e.Value<string>("type") == "matchEnded")) return payload;
        }
        throw new XunitException($"{client.Name}: the match never ended");
    }

    [Fact]
    public async Task Room_Create_ReturnsCodeAndSeat()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");

        await host.SendAsync(Messages.RoomCreate);
        JObject state = await host.ExpectAsync(Messages.RoomState);

        Assert.Matches("^[A-HJ-NP-Z2-9]{4}$", state.Value<string>("code")!);
        Assert.Equal(0, state.Value<int>("youAre"));
        Assert.Equal("Rohan", SeatName(state, 0));
        Assert.Equal(2, ((JArray)state["seats"]!).Count);
        Assert.False(((JArray)state["seats"]!)[1].Value<bool>("present"));
    }

    [Fact]
    public async Task Room_JoinByCode_BothSeatsSeeEachOther()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;

        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });

        JObject guestState = await guest.ExpectAsync(Messages.RoomState);
        JObject hostState = await host.ExpectAsync(Messages.RoomState);

        Assert.Equal(1, guestState.Value<int>("youAre"));
        Assert.Equal(0, hostState.Value<int>("youAre"));
        Assert.Equal(code, guestState.Value<string>("code"));
        Assert.Equal("Rohan", SeatName(guestState, 0));
        Assert.Equal("Friend", SeatName(guestState, 1));
        Assert.Equal("Friend", SeatName(hostState, 1));
    }

    [Fact]
    public async Task Room_CodeIsCaseInsensitive()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;

        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = "  " + code.ToLowerInvariant() + " " });
        JObject state = await guest.ExpectAsync(Messages.RoomState);

        Assert.Equal(code, state.Value<string>("code"));
        Assert.Equal(1, state.Value<int>("youAre"));
    }

    [Fact]
    public async Task Room_JoinUnknownCode_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = "ZZZZ" });
        JObject error = await guest.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.NoSuchRoom, error.Value<string>("code"));
    }

    [Fact]
    public async Task Room_JoinFullRoom_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");
        await using FakeClient third = await factory.JoinAsync("Latecomer");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        await guest.ExpectAsync(Messages.RoomState);

        await third.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        JObject error = await third.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.RoomFull, error.Value<string>("code"));
    }

    [Fact]
    public async Task Room_JoinWhileInRoom_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");

        await host.SendAsync(Messages.RoomCreate);
        await host.ExpectAsync(Messages.RoomState);

        await host.SendAsync(Messages.RoomCreate);
        JObject error = await host.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.InRoom, error.Value<string>("code"));
    }

    [Fact]
    public async Task Room_MatchStartsOnlyWhenBothReady()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        await guest.ExpectAsync(Messages.RoomState);

        // One side ready is not enough, however long we wait.
        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject afterHost = await guest.ExpectAsync(Messages.RoomState);
        Assert.True(SeatReady(afterHost, 0));
        Assert.False(SeatReady(afterHost, 1));

        await Task.Delay(300);
        Assert.False(host.EverReceived(Messages.MatchStart));

        // Choosing gear without pressing Ready is not enough either.
        await guest.SendAsync(Messages.RoomLoadout, Loadouts.NotReady(Loadouts.Gun));
        await host.ExpectAsync(Messages.RoomState);
        await Task.Delay(300);
        Assert.False(host.EverReceived(Messages.MatchStart));

        await guest.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Gun));

        JObject hostStart = await host.ExpectAsync(Messages.MatchStart);
        JObject guestStart = await guest.ExpectAsync(Messages.MatchStart);
        Assert.Equal(hostStart.Value<int>("matchId"), guestStart.Value<int>("matchId"));
    }

    [Fact]
    public async Task Room_TwoPlayers_Paired_DifferentSeats()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            Assert.Equal(0, hostStart.Value<int>("youAre"));
            Assert.Equal(1, guestStart.Value<int>("youAre"));
            Assert.Equal(hostStart.Value<int>("matchId"), guestStart.Value<int>("matchId"));
            Assert.Equal("Friend", hostStart.Value<string>("opponentName"));
            Assert.Equal("Rohan", guestStart.Value<string>("opponentName"));
            Assert.Equal("arena-4", hostStart.Value<string>("mapId"));
            Assert.Equal(400, ((JObject)hostStart["clock"]!).Value<int>("turnMs"));
        }
    }

    [Fact]
    public async Task Room_BadLoadout_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await host.SendAsync(Messages.RoomCreate);
        await host.ExpectAsync(Messages.RoomState);

        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Unknown));
        Assert.Equal(Messages.Errors.BadLoadout, (await host.ExpectErrorAsync()).Value<string>("code"));

        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.WrongSlot));
        Assert.Equal(Messages.Errors.BadLoadout, (await host.ExpectErrorAsync()).Value<string>("code"));

        // Nothing was stored, so the seat is still not ready and a good loadout still works.
        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        Assert.True(SeatReady(await host.ExpectAsync(Messages.RoomState), 0));
    }

    [Fact]
    public async Task Room_Leave_TellsTheOtherSeat()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        await guest.ExpectAsync(Messages.RoomState);
        await host.ExpectAsync(Messages.RoomState);

        await guest.SendAsync(Messages.RoomLeave);

        await guest.ExpectAsync(Messages.RoomLeft);
        JObject hostState = await host.ExpectAsync(Messages.RoomState);
        Assert.Equal("", SeatName(hostState, 1));
        Assert.False(((JArray)hostState["seats"]!)[1].Value<bool>("present"));

        // The room and its code survive, so a friend who is still typing it in can arrive.
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        Assert.Equal(1, (await guest.ExpectAsync(Messages.RoomState)).Value<int>("youAre"));
    }

    [Fact]
    public async Task Room_LoadoutIsNotLeakedBeforeStart()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient host = await factory.JoinAsync("Rohan");
        await using FakeClient guest = await factory.JoinAsync("Friend");

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        await guest.ExpectAsync(Messages.RoomState);

        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject seen = await guest.ExpectAsync(Messages.RoomState);

        // Ready, yes. What they are wearing, no: that would invent a counter-pick rule the design page
        // does not have (open question q-online-room-loadout).
        Assert.True(SeatReady(seen, 0));
        Assert.DoesNotContain("longbow", seen.ToString());
        Assert.DoesNotContain("loadout", seen.ToString());
    }

    // ---- the room outlives the match (ADR-032) ------------------------------------------------------

    [Fact]
    public async Task Room_MatchOver_ReturnsToWaiting_SameCodeBothSeats()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient host, FakeClient guest, string code, JObject hostStart, JObject _) = await Rooms.PlayWithCodeAsync(factory);
        await using (host)
        await using (guest)
        {
            await ResignAsync(host, hostStart);
            await ExpectMatchEndedAsync(host);
            await ExpectMatchEndedAsync(guest);

            // The result is sent first and the room state after it: the match ends, the room does not.
            foreach (JObject state in new[] { await host.ExpectAsync(Messages.RoomState), await guest.ExpectAsync(Messages.RoomState) })
            {
                Assert.Equal(code, state.Value<string>("code"));
                Assert.True(SeatPresent(state, 0));
                Assert.True(SeatPresent(state, 1));
                Assert.False(SeatReady(state, 0));
                Assert.False(SeatReady(state, 1));
            }
        }
    }

    [Fact]
    public async Task Room_MatchOver_ReadyAgain_StartsRoundTwo()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient host, FakeClient guest, string code, JObject hostStart, JObject _) = await Rooms.PlayWithCodeAsync(factory);
        await using (host)
        await using (guest)
        {
            Assert.Equal(1, hostStart.Value<int>("round"));

            await ResignAsync(host, hostStart);
            await ExpectMatchEndedAsync(host);
            await ExpectMatchEndedAsync(guest);
            await host.ExpectAsync(Messages.RoomState);
            await guest.ExpectAsync(Messages.RoomState);

            // Pressing Ready twice is the whole of the rematch: no offer, no new code, no new message.
            await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
            await guest.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Gun));

            JObject hostSecond = await host.ExpectAsync(Messages.MatchStart);
            JObject guestSecond = await guest.ExpectAsync(Messages.MatchStart);

            Assert.Equal(2, hostSecond.Value<int>("round"));
            Assert.Equal(2, guestSecond.Value<int>("round"));

            // The room's id is the room's, so it is the round that tells the two matches apart, and the
            // rematch is played in the same room rather than in a second one behind the same code.
            Assert.Equal(hostStart.Value<int>("matchId"), hostSecond.Value<int>("matchId"));
            Assert.Equal(1, (await factory.HealthAsync()).Value<int>("rooms"));

            // A board, not the one they just finished: nobody is dead and nothing is over. Who moves
            // first is a fresh coin flip on the server and is deliberately not asserted.
            PlayerView view = Wire.ReadView((JObject)hostSecond["view"]!);
            Assert.False(view.IsOver);
            Assert.Equal(2, view.Units.Count);
            Assert.All(view.Units, u => Assert.Equal(u.MaxHp, u.Hp));
        }
    }

    [Fact]
    public async Task Room_MatchOver_ForfeitedSeatIsFreed()
    {
        await using var factory = new MimasServerFactory();   // 400 ms reconnect grace
        (FakeClient host, FakeClient guest, string code, JObject _, JObject __) = await Rooms.PlayWithCodeAsync(factory);
        await using (host)
        {
            await guest.DisposeAsync();
            await ExpectMatchEndedAsync(host);

            // The grace is something a match owes a player. There is no match, so the seat is free and
            // the code still opens the room.
            JObject state = await host.ExpectAsync(Messages.RoomState);
            Assert.Equal(code, state.Value<string>("code"));
            Assert.True(SeatPresent(state, 0));
            Assert.False(SeatPresent(state, 1));

            await using FakeClient third = await factory.JoinAsync("Latecomer");
            await third.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });

            JObject joined = await third.ExpectAsync(Messages.RoomState);
            Assert.Equal(1, joined.Value<int>("youAre"));
            Assert.Equal(code, joined.Value<string>("code"));
        }
    }

    [Fact]
    public async Task Room_MatchOver_BotRoom_ReadyStartsAgain()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync(Messages.BotPlay);
        string code = (await client.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject start = await client.ExpectAsync(Messages.MatchStart);

        await ResignAsync(client, start);
        await ExpectMatchEndedAsync(client);

        JObject state = await client.ExpectAsync(Messages.RoomState);
        Assert.Equal(code, state.Value<string>("code"));
        Assert.False(SeatReady(state, 0));
        Assert.True(SeatPresent(state, 1));
        Assert.True(SeatReady(state, 1));   // the bot has nothing to choose and never un-readies

        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        Assert.Equal(2, (await client.ExpectAsync(Messages.MatchStart)).Value<int>("round"));
    }

    [Fact]
    public async Task Room_MatchOver_LeaveThenCreate_Works()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync(Messages.BotPlay);
        string code = (await client.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject start = await client.ExpectAsync(Messages.MatchStart);

        await ResignAsync(client, start);
        await ExpectMatchEndedAsync(client);
        await client.ExpectAsync(Messages.RoomState);

        // Leaving after the result must really release the seat, or every later room.create is in_room.
        await client.SendAsync(Messages.RoomLeave);
        await client.ExpectAsync(Messages.RoomLeft);

        await client.SendAsync(Messages.RoomCreate);
        JObject fresh = await client.ExpectAsync(Messages.RoomState);
        Assert.NotEqual(code, fresh.Value<string>("code"));
        Assert.Equal(0, fresh.Value<int>("youAre"));
    }

    [Fact]
    public async Task Room_MatchOver_LoadoutIsStillNotLeaked()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient host, FakeClient guest, string _, JObject hostStart, JObject __) = await Rooms.PlayWithCodeAsync(factory);
        await using (host)
        await using (guest)
        {
            await ResignAsync(host, hostStart);
            await ExpectMatchEndedAsync(guest);

            // The seats keep their gear so Ready is one click — but the room screen still never carries
            // it, before the first match or between two (q-online-room-loadout).
            JObject state = await guest.ExpectAsync(Messages.RoomState);
            Assert.DoesNotContain("longbow", state.ToString());
            Assert.DoesNotContain("flintlock", state.ToString());
            Assert.DoesNotContain("loadout", state.ToString());
        }
    }

    [Fact]
    public async Task Room_NotInRoom_Errors()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        Assert.Equal(Messages.Errors.NotInRoom, (await client.ExpectErrorAsync()).Value<string>("code"));

        await client.SendAsync(Messages.RoomLeave);
        Assert.Equal(Messages.Errors.NotInRoom, (await client.ExpectErrorAsync()).Value<string>("code"));
    }
}

/// <summary>Getting two clients into a started match, which nearly every test below needs.</summary>
public static class Rooms
{
    public static async Task<(FakeClient Host, FakeClient Guest, JObject HostStart, JObject GuestStart)> PlayAsync(
        MimasServerFactory factory, string hostName = "Rohan", string guestName = "Friend")
    {
        (FakeClient host, FakeClient guest, string _, JObject hostStart, JObject guestStart) =
            await PlayWithCodeAsync(factory, hostName, guestName);
        return (host, guest, hostStart, guestStart);
    }

    /// <summary>The same, and the room's code, which anything about the room outliving the match needs.</summary>
    public static async Task<(FakeClient Host, FakeClient Guest, string Code, JObject HostStart, JObject GuestStart)> PlayWithCodeAsync(
        MimasServerFactory factory, string hostName = "Rohan", string guestName = "Friend")
    {
        FakeClient host = await factory.JoinAsync(hostName);
        FakeClient guest = await factory.JoinAsync(guestName);

        await host.SendAsync(Messages.RoomCreate);
        string code = (await host.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await guest.SendAsync(Messages.RoomJoin, new JObject { ["code"] = code });
        await guest.ExpectAsync(Messages.RoomState);

        await host.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        await guest.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Gun));

        JObject hostStart = await host.ExpectAsync(Messages.MatchStart);
        JObject guestStart = await guest.ExpectAsync(Messages.MatchStart);
        return (host, guest, code, hostStart, guestStart);
    }

    /// <summary>One client in a started match against the server's bot.</summary>
    public static async Task<(FakeClient Client, JObject Start)> VsBotAsync(MimasServerFactory factory, string name = "Rohan")
    {
        FakeClient client = await factory.JoinAsync(name);
        await client.SendAsync(Messages.BotPlay);
        await client.ExpectAsync(Messages.RoomState);
        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject start = await client.ExpectAsync(Messages.MatchStart);
        return (client, start);
    }
}
