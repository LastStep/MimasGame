using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

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
        return (host, guest, hostStart, guestStart);
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
