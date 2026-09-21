using Mimas.Core.Content;
using Mimas.Core.Geometry;
using Mimas.Server.Net;
using Mimas.Server.Options;
using Mimas.Server.Players;
using Mimas.Server.Rooms;
using Microsoft.Extensions.Options;

// Mimas game server.
//
// Holds the truth for every online match: guest identities, rooms reached by a four-letter code, a
// server-authoritative turn clock, a bot seat, and hidden information filtered per seat. A client only
// ever receives its own PlayerView and the events filtered for it (golden rule 6, ADR-010); the wire
// itself is docs/networking.md and Mimas.Core.Protocol.

var builder = WebApplication.CreateBuilder(args);
// Where Kestrel listens. MIMAS_BIND=loopback keeps the process off every interface but 127.0.0.1,
// which is how it runs on the VPS: nginx terminates TLS and proxies to it, and the box has no
// firewall (ADR-031). Anything else, including unset, is "any" — so development and the tests are
// unchanged.
var port = int.Parse(Environment.GetEnvironmentVariable("MIMAS_PORT") ?? "7777");
var loopbackOnly = string.Equals(Environment.GetEnvironmentVariable("MIMAS_BIND"), "loopback", StringComparison.OrdinalIgnoreCase);
builder.WebHost.ConfigureKestrel(o =>
{
    if (loopbackOnly) o.ListenLocalhost(port);
    else o.ListenAnyIP(port);
});

// Content is loaded once, before anything can serve, from the same JSON folder the client ships.
// MIMAS_DATA_PATH overrides the default (the Data/ folder copied next to the binary by the csproj).
var dataPath = Environment.GetEnvironmentVariable("MIMAS_DATA_PATH") ?? Path.Combine(AppContext.BaseDirectory, "Data");
ContentCatalog catalog;
try
{
    catalog = ContentCatalog.Load(ContentFiles.FromDirectory(dataPath));
}
catch (Exception e) when (e is ContentLoadException || e is DirectoryNotFoundException)
{
    Console.Error.WriteLine("Refusing to start: " + e.Message);
    return 1;
}

builder.Services.AddSingleton(catalog);
builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.Section));
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ServerOptions>>().Value);
builder.Services.AddSingleton<PlayerRegistry>();
builder.Services.AddSingleton<RoomRegistry>();

var app = builder.Build();
var options = app.Services.GetRequiredService<ServerOptions>();
var players = app.Services.GetRequiredService<PlayerRegistry>();
var rooms = app.Services.GetRequiredService<RoomRegistry>();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.Logger.LogInformation("content loaded from {Path}: {Files} files, hash {Hash}", dataPath, catalog.Files.Count, catalog.Hash);
app.Logger.LogInformation("listening on {Bind}:{Port}", loopbackOnly ? "loopback" : "any", port);
app.Logger.LogInformation("clock: turn {Turn} ms, lag grace {Lag} ms, reconnect grace {Reconnect} ms",
    options.TurnMs ?? catalog.Rules.Clock.TurnMs,
    options.LagGraceMs ?? catalog.Rules.Clock.LagGraceMs,
    options.ReconnectGraceMs ?? catalog.Rules.Clock.ReconnectGraceMs);

// Optionally serve the Web build beside the socket, so one process gives a playable page (§7.11).
// Inert unless MIMAS_WEB_PATH is set.
var webPath = Environment.GetEnvironmentVariable("MIMAS_WEB_PATH");
if (!string.IsNullOrWhiteSpace(webPath) && Directory.Exists(webPath))
{
    var fileOptions = new StaticFileOptions
    {
        FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(Path.GetFullPath(webPath)),
        ServeUnknownFileTypes = true,
        OnPrepareResponse = ctx =>
        {
            // Unity's Brotli output is "thing.wasm.br": the browser needs the encoding and the inner type.
            string name = ctx.File.Name;
            if (name.EndsWith(".br", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Context.Response.Headers.ContentEncoding = "br";
                string inner = Path.GetExtension(name[..^3]).ToLowerInvariant();
                ctx.Context.Response.ContentType = inner switch
                {
                    ".wasm" => "application/wasm",
                    ".js" => "application/javascript",
                    ".data" => "application/octet-stream",
                    ".symbols" => "application/octet-stream",
                    _ => "application/octet-stream",
                };
            }
            ctx.Context.Response.Headers.CacheControl = "no-store";
        },
    };
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = fileOptions.FileProvider });
    app.UseStaticFiles(fileOptions);
    app.Logger.LogInformation("serving the web build from {Path}", Path.GetFullPath(webPath));
}

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "mimas-server",
    coreCheck = Hex.Distance(new Hex(-3, 0), new Hex(3, 0)),   // proves Core is linked (== 6)
    rooms = rooms.Count,
    waitingRooms = rooms.WaitingCount,
    players = players.Count,
    content = new
    {
        hash = catalog.Hash,
        files = catalog.Files.Count,
        abilities = catalog.Abilities.Count,
        items = catalog.Items.Count,
        maps = catalog.Maps.Count,
        props = catalog.Props.Count,
        terrains = catalog.Terrains.Count,
    },
    utc = DateTime.UtcNow,
}));

// Who may open a game socket. Empty — the default, and what every test and a plain `dotnet run` rely
// on — means anyone. Production names its one origin in appsettings.Production.json, so moving the
// site is a redeploy and not a VPS setup (ADR-033).
var allowedOrigins = options.AllowedOrigins ?? Array.Empty<string>();
if (allowedOrigins.Length > 0) app.Logger.LogInformation("ws origins allowed: {Origins}", string.Join(", ", allowedOrigins));
else app.Logger.LogInformation("ws origins allowed: any");

app.Map("/ws", async context =>
{
    var log = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ws");

    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    // A browser always sends Origin on a WebSocket handshake, so a missing one is refused with the
    // rest. The header is compared as a string: an origin has no trailing slash and no path, and
    // parsing it would only invent ways for two spellings of the same site to disagree.
    if (allowedOrigins.Length > 0)
    {
        string origin = context.Request.Headers.Origin.ToString();
        if (!allowedOrigins.Any(o => string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)))
        {
            log.LogWarning("ws: origin {Origin} refused", string.IsNullOrEmpty(origin) ? "(none)" : origin);
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsync("This server does not accept game sockets from that origin.");
            return;
        }
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    log.LogInformation("client connected {Remote}", context.Connection.RemoteIpAddress);

    var connection = new WsConnection(socket, log, players, rooms, options.PingIntervalMs, options.IdleCloseMs);
    connection.Closed += rooms.OnConnectionClosed;
    await connection.RunAsync(context.RequestAborted);
});

app.Run();
return 0;

/// <summary>Named so <c>WebApplicationFactory&lt;Program&gt;</c> can host this server in a test.</summary>
public partial class Program
{
}
