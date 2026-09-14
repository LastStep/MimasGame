using System.Net.WebSockets;
using System.Text;
using Mimas.Core.Geometry;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

// Mimas game server — M0 scaffold.
// Exposes /health and a /ws WebSocket that understands {"t":"ping"} → {"t":"pong"}.
// Matchmaking, rooms, clocks and hidden-info filtering come in M2 (see docs/networking.md).

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.ListenAnyIP(int.Parse(Environment.GetEnvironmentVariable("MIMAS_PORT") ?? "7777")));

var app = builder.Build();
app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

app.MapGet("/health", () => Results.Ok(new
{
    ok = true,
    service = "mimas-server",
    coreCheck = Hex.Distance(new Hex(-3, 0), new Hex(3, 0)),   // proves Core is linked (== 6)
    utc = DateTime.UtcNow,
}));

app.Map("/ws", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var log = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ws");
    log.LogInformation("client connected {Remote}", context.Connection.RemoteIpAddress);

    var buffer = new byte[16 * 1024];
    while (socket.State == WebSocketState.Open)
    {
        var sb = new StringBuilder();
        WebSocketReceiveResult result;
        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), context.RequestAborted);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return;
            }
            sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
        } while (!result.EndOfMessage);

        string reply;
        try
        {
            var msg = JObject.Parse(sb.ToString());
            var type = msg.Value<string>("t") ?? "";
            reply = type switch
            {
                "ping" => JsonConvert.SerializeObject(new { t = "pong", p = new { } }),
                _ => JsonConvert.SerializeObject(new { t = "error", p = new { code = "unknown_type", message = $"Unknown message type '{type}'" } }),
            };
        }
        catch (JsonException)
        {
            reply = JsonConvert.SerializeObject(new { t = "error", p = new { code = "bad_json", message = "Message must be a JSON object with fields t and p" } });
        }

        var bytes = Encoding.UTF8.GetBytes(reply);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, context.RequestAborted);
    }
});

app.Run();
