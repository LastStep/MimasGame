using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Mimas.Server.Tests;

/// <summary>
/// Who may open a game socket (ADR-033). The list is configuration, not code: empty means anyone, which
/// is development and every other test in this project, and production names the one site it serves. A
/// page on someone else's domain can still fetch this server — that is the web — but it cannot make a
/// browser hand it a socket that plays as one of our guests.
/// </summary>
public class OriginTests
{
    private static MimasServerFactory WithAllowList(params string[] origins)
    {
        var settings = new Dictionary<string, string>(MimasServerFactory.FastClock);
        for (int i = 0; i < origins.Length; i++) settings[$"Mimas:AllowedOrigins:{i}"] = origins[i];
        return new MimasServerFactory(settings);
    }

    /// <summary>A raw socket with whatever <c>Origin</c> a browser would have put on the handshake.</summary>
    private static Task<WebSocket> ConnectAsync(MimasServerFactory factory, string? origin)
    {
        WebSocketClient ws = factory.Server.CreateWebSocketClient();
        if (origin != null) ws.ConfigureRequest = r => r.Headers["Origin"] = origin;
        return ws.ConnectAsync(new Uri(factory.Server.BaseAddress, "/ws"), CancellationToken.None);
    }

    private static void AssertRefused(Exception e) =>
        Assert.True(e.Message.Contains("403") || e.Message.Contains("Forbidden", StringComparison.OrdinalIgnoreCase),
            "the handshake did not fail with 403: " + e.Message);

    [Fact]
    public async Task Ws_OriginNotInAllowList_Refused403()
    {
        await using MimasServerFactory factory = WithAllowList("https://good.example");

        Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(factory, "https://evil.example"));

        AssertRefused(refused);
    }

    [Fact]
    public async Task Ws_NoOriginHeader_Refused403()
    {
        await using MimasServerFactory factory = WithAllowList("https://good.example");

        // Every browser sends Origin on a WebSocket handshake. Something that does not is not a browser,
        // and in production it is refused with the rest of them.
        Exception refused = await Assert.ThrowsAnyAsync<Exception>(() => ConnectAsync(factory, null));

        AssertRefused(refused);
    }

    [Fact]
    public async Task Ws_OriginInAllowList_Connects()
    {
        await using MimasServerFactory factory = WithAllowList("https://good.example", "http://localhost:7777");

        using WebSocket socket = await ConnectAsync(factory, "https://good.example");
        await using var client = new FakeClient(socket, "Rohan");
        JObject ok = await client.AuthenticateAsync("Rohan");

        Assert.True(ok.Value<int>("playerId") > 0);
    }

    [Fact]
    public async Task Ws_NoAllowListConfigured_AnyOriginConnects()
    {
        await using var factory = new MimasServerFactory();   // the default: no list at all

        using WebSocket socket = await ConnectAsync(factory, "https://anything.example");
        await using var client = new FakeClient(socket, "Rohan");
        JObject ok = await client.AuthenticateAsync("Rohan");

        Assert.Equal("Rohan", ok.Value<string>("name"));
    }
}
