using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using Mimas.Core.Protocol;
using Mimas.Server.Players;
using Mimas.Server.Rooms;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Net;

/// <summary>
/// One WebSocket, from accept to close. It owns three things and nothing else: reading frames, writing
/// frames, and deciding which registry a message belongs to.
/// <para>
/// Writes go through a channel drained by a single task, so two rooms broadcasting at once can never
/// interleave halfway through a frame. Reads are capped at 64 KB and text-only: a client has no reason
/// to send anything larger and every reason not to be trusted about it.
/// </para>
/// </summary>
public sealed class WsConnection
{
    private const int MaxFrameBytes = 64 * 1024;

    private readonly WebSocket _socket;
    private readonly ILogger _log;
    private readonly PlayerRegistry _players;
    private readonly RoomRegistry _rooms;
    private readonly int _pingIntervalMs;
    private readonly int _idleCloseMs;
    private readonly Channel<string> _out = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });

    private long _lastPongAt = Environment.TickCount64;
    private long _pingSentAt;
    private int _pingToken;
    private int _closedOnce;

    /// <summary>Set once <c>auth.*</c> has succeeded; null before that, and every other message is refused.</summary>
    public Player? Player { get; private set; }

    /// <summary>Round-trip estimate in ms, an exponentially weighted mean of the pongs. 0 until the first one.</summary>
    public int RttMs { get; private set; }

    /// <summary>Raised once, when the socket is finished, whatever ended it.</summary>
    public event Action<WsConnection>? Closed;

    public WsConnection(WebSocket socket, ILogger log, PlayerRegistry players, RoomRegistry rooms, int pingIntervalMs, int idleCloseMs)
    {
        _socket = socket;
        _log = log;
        _players = players;
        _rooms = rooms;
        _pingIntervalMs = pingIntervalMs;
        _idleCloseMs = idleCloseMs;
    }

    // ---- sending ---------------------------------------------------------------------------------

    public void Send(string t, JObject p)
    {
        var envelope = new JObject { ["t"] = t, ["p"] = p };
        _out.Writer.TryWrite(envelope.ToString(Formatting.None));
    }

    public void SendError(string code, string message)
    {
        _log.LogDebug("error to {Player}: {Code} {Message}", Player, code, message);
        Send(Messages.Error, new JObject { ["code"] = code, ["message"] = message });
    }

    // ---- the loop --------------------------------------------------------------------------------

    public async Task RunAsync(CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task writer = WriteLoopAsync(cts.Token);
        Task pinger = PingLoopAsync(cts.Token);

        try
        {
            await ReadLoopAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException)
        {
            // A client that closes its tab, or a proxy that drops the connection, arrives here. It is the
            // normal end of a socket, not an error; the seat it was attached to is told by the finally.
            _log.LogDebug("socket ended: {Message}", e.Message);
        }
        finally
        {
            cts.Cancel();
            _out.Writer.TryComplete();
            Task both = Task.WhenAll(writer, pinger);
            await Task.WhenAny(both, Task.Delay(1000, CancellationToken.None));
            // Whether or not they finished in time, somebody has to look at them.
            _ = both.ContinueWith(t => _log.LogDebug("connection tasks ended: {Message}", t.Exception?.Message),
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            RaiseClosed();
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        while (_socket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            var sb = new StringBuilder();
            WebSocketReceiveResult result;
            do
            {
                result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, "bye");
                    return;
                }
                if (result.MessageType != WebSocketMessageType.Text)
                {
                    await CloseAsync(WebSocketCloseStatus.InvalidMessageType, "text frames only");
                    return;
                }
                if (sb.Length + result.Count > MaxFrameBytes)
                {
                    await CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame too large");
                    return;
                }
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            } while (!result.EndOfMessage);

            Dispatch(sb.ToString());
        }
    }

    private async Task WriteLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (string message in _out.Reader.ReadAllAsync(ct))
            {
                if (_socket.State != WebSocketState.Open) return;
                byte[] bytes = Encoding.UTF8.GetBytes(message);
                await _socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException)
        {
        }
    }

    private async Task PingLoopAsync(CancellationToken ct)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_pingIntervalMs));
            while (await timer.WaitForNextTickAsync(ct))
            {
                if (Environment.TickCount64 - _lastPongAt > _idleCloseMs)
                {
                    _log.LogInformation("closing idle connection {Player}", Player);
                    await CloseAsync(WebSocketCloseStatus.NormalClosure, "idle");
                    return;
                }
                _pingSentAt = Environment.TickCount64;
                Send(Messages.Ping, new JObject { ["t"] = ++_pingToken });
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    // ---- routing ---------------------------------------------------------------------------------

    private void Dispatch(string frame)
    {
        JObject envelope;
        string type;
        JObject payload;
        try
        {
            envelope = JObject.Parse(frame);
            type = envelope.Value<string>("t") ?? "";
            payload = envelope["p"] as JObject ?? new JObject();
        }
        catch (JsonException)
        {
            SendError(Messages.Errors.BadJson, "Message must be a JSON object with fields t and p.");
            return;
        }

        try
        {
            Route(type, payload);
        }
        catch (WireException e)
        {
            SendError(Messages.Errors.BadJson, e.Message);
        }
        catch (Exception e)
        {
            // A handler throwing must not take the socket with it; the player sees a refusal and plays on.
            _log.LogError(e, "unhandled error handling '{Type}' for {Player}", type, Player);
            SendError(Messages.Errors.BadJson, "The server could not handle that message.");
        }
    }

    private void Route(string type, JObject p)
    {
        if (type == Messages.Pong)
        {
            OnPong();
            return;
        }

        if (type == Messages.AuthGuest)
        {
            Authenticated(_players.Guest(p.Value<string>("name")));
            return;
        }

        if (type == Messages.AuthResume)
        {
            Player? resumed = _players.TryResume(p.Value<string>("token"));
            if (resumed == null)
            {
                SendError(Messages.Errors.BadToken, "That token is not known to this server.");
                return;
            }
            Authenticated(resumed);
            _rooms.Reattach(resumed, this);
            return;
        }

        if (Player == null)
        {
            SendError(Messages.Errors.Unauthenticated, "Send auth.guest or auth.resume first.");
            return;
        }

        switch (type)
        {
            case Messages.RoomCreate:
                _rooms.HandleCreate(Player, this, vsBot: false);
                return;
            case Messages.BotPlay:
                _rooms.HandleCreate(Player, this, vsBot: true);
                return;
            case Messages.RoomJoin:
                _rooms.HandleJoin(Player, this, p.Value<string>("code"));
                return;
            case Messages.RoomLoadout:
                _rooms.HandleLoadout(Player, this, p);
                return;
            case Messages.RoomLeave:
                _rooms.HandleLeave(Player, this);
                return;
            case Messages.MatchCommand:
                _rooms.HandleCommand(Player, this, p);
                return;
            case Messages.MatchResync:
                _rooms.HandleResync(Player, this, p);
                return;
            default:
                SendError(Messages.Errors.UnknownType, $"Unknown message type '{type}'.");
                return;
        }
    }

    private void Authenticated(Player player)
    {
        Player = player;
        player.Connection = this;
        _log.LogInformation("authenticated {Player}", player);
        Send(Messages.AuthOk, new JObject { ["playerId"] = player.Id, ["token"] = player.Token, ["name"] = player.Name });
    }

    private void OnPong()
    {
        _lastPongAt = Environment.TickCount64;
        if (_pingSentAt == 0) return;
        var sample = (int)(_lastPongAt - _pingSentAt);
        RttMs = RttMs == 0 ? sample : (RttMs * 3 + sample) / 4;
    }

    // ---- closing ---------------------------------------------------------------------------------

    private async Task CloseAsync(WebSocketCloseStatus status, string reason)
    {
        try
        {
            // CloseReceived matters as much as Open: the client has sent its half of the handshake and is
            // waiting for ours. Skip it and the client's CloseAsync never returns.
            if (_socket.State == WebSocketState.Open || _socket.State == WebSocketState.CloseReceived)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _socket.CloseAsync(status, reason, timeout.Token);
            }
        }
        catch (Exception e) when (e is WebSocketException || e is IOException || e is ObjectDisposedException || e is OperationCanceledException)
        {
        }
    }

    private void RaiseClosed()
    {
        if (Interlocked.Exchange(ref _closedOnce, 1) != 0) return;
        if (Player != null && ReferenceEquals(Player.Connection, this)) Player.Connection = null;
        Closed?.Invoke(this);
    }
}
