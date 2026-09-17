using System.Net.WebSockets;
using Mimas.Core.Content;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Tests;

/// <summary>
/// The server, in process, with the shipped data and a clock fast enough to test. Nothing about the
/// rules is stubbed: these tests play real matches through the real <c>MatchState</c>, because the only
/// interesting question about this server is whether the game survives the wire.
/// </summary>
public sealed class MimasServerFactory : WebApplicationFactory<Program>
{
    /// <summary>Test defaults: 400 ms turns, no lag grace, 400 ms to come back, a bot that does not pause.</summary>
    public static readonly IReadOnlyDictionary<string, string> FastClock = new Dictionary<string, string>
    {
        ["Mimas:TurnMs"] = "400",
        ["Mimas:LagGraceMs"] = "0",
        ["Mimas:ReconnectGraceMs"] = "400",
        ["Mimas:BotThinkMs"] = "0",
        ["Mimas:PingIntervalMs"] = "200",
        ["Mimas:IdleCloseMs"] = "30000",
        ["Mimas:TickMs"] = "20",
    };

    private readonly Dictionary<string, string> _settings;

    public MimasServerFactory(IReadOnlyDictionary<string, string>? settings = null)
    {
        _settings = new Dictionary<string, string>(settings ?? FastClock);
    }

    /// <summary>The repo's data folder, found by walking up to the directory holding <c>Mimas.slnx</c>.</summary>
    public static string DataPath
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (dir.GetFiles("Mimas.slnx").Length > 0)
                    return Path.Combine(dir.FullName, "MimasClient", "Assets", "_Game", "Data");
                dir = dir.Parent;
            }
            throw new InvalidOperationException("Could not find Mimas.slnx above " + AppContext.BaseDirectory);
        }
    }

    public ContentCatalog Catalog => Services.GetRequiredService<ContentCatalog>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("MIMAS_DATA_PATH", DataPath);
        foreach ((string key, string value) in _settings) builder.UseSetting(key, value);
        builder.ConfigureLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
    }

    /// <summary>A connected, unauthenticated client.</summary>
    public async Task<FakeClient> ConnectAsync(string name)
    {
        WebSocketClient ws = Server.CreateWebSocketClient();
        WebSocket socket = await ws.ConnectAsync(new Uri(Server.BaseAddress, "/ws"), CancellationToken.None);
        return new FakeClient(socket, name);
    }

    /// <summary>A connected client that has already become a guest.</summary>
    public async Task<FakeClient> JoinAsync(string name)
    {
        FakeClient client = await ConnectAsync(name);
        await client.AuthenticateAsync(name);
        return client;
    }

    public async Task<JObject> HealthAsync()
    {
        using HttpClient http = CreateClient();
        return JObject.Parse(await http.GetStringAsync("/health"));
    }
}
