using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mimas.Server.Tests;

/// <summary>
/// Guest identity (D4). No accounts, no database — but a token that survives a page refresh, because
/// without it a reload is a new stranger and the match you were in is gone.
/// </summary>
public class AuthTests
{
    [Fact]
    public async Task Auth_Guest_ReturnsIdAndToken()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.ConnectAsync("Rohan");

        JObject ok = await client.AuthenticateAsync("Rohan");

        Assert.True(ok.Value<int>("playerId") > 0);
        Assert.Equal("Rohan", ok.Value<string>("name"));
        Assert.Equal(32, ok.Value<string>("token")!.Length);
        Assert.Matches("^[0-9a-f]{32}$", ok.Value<string>("token")!);
    }

    [Fact]
    public async Task Auth_EmptyName_GetsGuestName()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.ConnectAsync("anon");

        await client.SendAsync(Messages.AuthGuest, new JObject { ["name"] = "   " });
        JObject ok = await client.ExpectAsync(Messages.AuthOk);

        Assert.Matches("^Guest-[0-9]{4}$", ok.Value<string>("name")!);
    }

    [Fact]
    public async Task Auth_Resume_SamePlayerId()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient first = await factory.JoinAsync("Rohan");

        await using FakeClient second = await factory.ConnectAsync("Rohan again");
        await second.SendAsync(Messages.AuthResume, new JObject { ["token"] = first.Token });
        JObject ok = await second.ExpectAsync(Messages.AuthOk);

        Assert.Equal(first.PlayerId, ok.Value<int>("playerId"));
        Assert.Equal(first.Token, ok.Value<string>("token"));
        Assert.Equal("Rohan", ok.Value<string>("name"));
    }

    [Fact]
    public async Task Auth_ResumeInWaitingRoom_AuthOkNamesRoom()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync(Messages.BotPlay);
        string code = (await client.ExpectAsync(Messages.RoomState)).Value<string>("code")!;
        await client.SendAsync(Messages.RoomLoadout, Loadouts.Ready(Loadouts.Bow));
        JObject start = await client.ExpectAsync(Messages.MatchStart);

        await client.SendAsync(Messages.MatchCommand, new JObject
        {
            ["matchId"] = start.Value<int>("matchId"),
            ["command"] = Wire.Command(new ResignCommand(start.Value<int>("youAre"))),
        });
        await client.ExpectAsync(Messages.RoomState);   // the room went back to waiting

        // A second socket with the same token — a reopened tab, or a reload the server has not yet
        // noticed. auth.ok names the room so the lobby can open on it instead of on buttons that would
        // all answer in_room, and the room state follows (ADR-032).
        await using FakeClient returned = await factory.ConnectAsync("Rohan again");
        await returned.SendAsync(Messages.AuthResume, new JObject { ["token"] = client.Token });

        JObject ok = await returned.ExpectAsync(Messages.AuthOk);
        Assert.Equal(code, ok.Value<string>("room"));
        Assert.Equal(code, (await returned.ExpectAsync(Messages.RoomState)).Value<string>("code"));
    }

    [Fact]
    public async Task Auth_NotInARoom_AuthOkHasNoRoom()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.ConnectAsync("Rohan");

        JObject ok = await client.AuthenticateAsync("Rohan");

        Assert.Null(ok["room"]);
    }

    [Fact]
    public async Task Auth_ResumeUnknownToken_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.ConnectAsync("ghost");

        await client.SendAsync(Messages.AuthResume, new JObject { ["token"] = new string('a', 32) });
        JObject error = await client.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.BadToken, error.Value<string>("code"));
    }

    [Fact]
    public async Task Unauthenticated_Command_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.ConnectAsync("nobody");

        await client.SendAsync(Messages.RoomCreate);
        JObject error = await client.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.Unauthenticated, error.Value<string>("code"));
    }

    [Fact]
    public async Task Unknown_MessageType_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync("match.teleport");
        JObject error = await client.ExpectErrorAsync();

        Assert.Equal(Messages.Errors.UnknownType, error.Value<string>("code"));
    }

    [Fact]
    public async Task Health_ReportsRoomsAndPlayers()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");
        await client.SendAsync(Messages.RoomCreate);
        await client.ExpectAsync(Messages.RoomState);

        JObject health = await factory.HealthAsync();

        Assert.True(health.Value<bool>("ok"));
        Assert.Equal(6, health.Value<int>("coreCheck"));
        Assert.Equal(1, health.Value<int>("rooms"));
        Assert.Equal(1, health.Value<int>("waitingRooms"));
        Assert.Equal(1, health.Value<int>("players"));
    }
}
