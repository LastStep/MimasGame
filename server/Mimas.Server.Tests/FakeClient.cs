using System.Net.WebSockets;
using System.Text;
using Mimas.Core.Protocol;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Tests;

/// <summary>
/// One player's socket, as a test holds it. It buffers everything that arrives, answers <c>ping</c> by
/// itself the way a real client does, and lets a test say "wait for the next <c>match.events</c>" without
/// caring what else turned up first.
/// </summary>
public sealed class FakeClient : IAsyncDisposable
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(5);

    private readonly WebSocket _socket;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<(string Type, JObject Payload)> _received = new();
    private readonly SemaphoreSlim _arrived = new(0);
    private readonly object _gate = new();
    private readonly Task _reader;
    private int _cursor;

    public string Name { get; }

    /// <summary>Set by <see cref="AuthenticateAsync"/>.</summary>
    public int PlayerId { get; private set; }

    public string Token { get; private set; } = "";

    public FakeClient(WebSocket socket, string name)
    {
        _socket = socket;
        Name = name;
        _reader = Task.Run(ReadLoopAsync);
    }

    /// <summary>Everything that has arrived so far, in order, including the messages already consumed.</summary>
    public IReadOnlyList<(string Type, JObject Payload)> Received
    {
        get { lock (_gate) return _received.ToList(); }
    }

    public async Task SendAsync(string t, JObject? p = null)
    {
        var envelope = new JObject { ["t"] = t, ["p"] = p ?? new JObject() };
        byte[] bytes = Encoding.UTF8.GetBytes(envelope.ToString(Formatting.None));
        await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, _cts.Token);
    }

    /// <summary>The next message of that type, skipping anything else. Fails the test rather than hanging forever.</summary>
    public async Task<JObject> ExpectAsync(string t, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (_gate)
            {
                while (_cursor < _received.Count)
                {
                    (string Type, JObject Payload) message = _received[_cursor++];
                    if (message.Type == t) return message.Payload;
                    if (message.Type == Messages.Error)
                        throw new Xunit.Sdk.XunitException(
                            $"{Name}: waiting for '{t}' but the server sent error '{message.Payload.Value<string>("code")}': {message.Payload.Value<string>("message")}");
                }
            }

            TimeSpan left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                throw new Xunit.Sdk.XunitException($"{Name}: timed out waiting for '{t}'. Got: {string.Join(", ", Received.Select(m => m.Type))}");
            await _arrived.WaitAsync(left < TimeSpan.FromMilliseconds(50) ? left : TimeSpan.FromMilliseconds(50), _cts.Token);
        }
    }

    /// <summary>The next message of either type, whichever comes first: for "events or the match ended" loops.</summary>
    public async Task<(string Type, JObject Payload)> ExpectEitherAsync(string a, string b, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (_gate)
            {
                while (_cursor < _received.Count)
                {
                    (string Type, JObject Payload) message = _received[_cursor++];
                    if (message.Type == a || message.Type == b) return message;
                }
            }

            TimeSpan left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                throw new Xunit.Sdk.XunitException($"{Name}: timed out waiting for '{a}' or '{b}'. Got: {string.Join(", ", Received.Select(m => m.Type))}");
            await _arrived.WaitAsync(left < TimeSpan.FromMilliseconds(50) ? left : TimeSpan.FromMilliseconds(50), _cts.Token);
        }
    }

    /// <summary>The next message of any of these types: for a series loop, which takes events, starts and refusals.</summary>
    public async Task<(string Type, JObject Payload)> ExpectAnyAsync(string[] types, TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (_gate)
            {
                while (_cursor < _received.Count)
                {
                    (string Type, JObject Payload) message = _received[_cursor++];
                    if (types.Contains(message.Type)) return message;
                }
            }

            TimeSpan left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                throw new Xunit.Sdk.XunitException($"{Name}: timed out waiting for one of '{string.Join("', '", types)}'. Got: {string.Join(", ", Received.Select(m => m.Type))}");
            await _arrived.WaitAsync(left < TimeSpan.FromMilliseconds(50) ? left : TimeSpan.FromMilliseconds(50), _cts.Token);
        }
    }

    /// <summary>True when a message of that type has arrived at any point, consumed or not.</summary>
    public bool EverReceived(string t)
    {
        lock (_gate) return _received.Any(m => m.Type == t);
    }

    public async Task<JObject> AuthenticateAsync(string? name = null)
    {
        await SendAsync(Messages.AuthGuest, new JObject { ["name"] = name ?? Name });
        JObject ok = await ExpectAsync(Messages.AuthOk);
        PlayerId = ok.Value<int>("playerId");
        Token = ok.Value<string>("token") ?? "";
        return ok;
    }

    public async Task<JObject> ExpectErrorAsync(TimeSpan? timeout = null)
    {
        DateTime deadline = DateTime.UtcNow + (timeout ?? DefaultTimeout);
        while (true)
        {
            lock (_gate)
            {
                while (_cursor < _received.Count)
                {
                    (string Type, JObject Payload) message = _received[_cursor++];
                    if (message.Type == Messages.Error) return message.Payload;
                }
            }
            TimeSpan left = deadline - DateTime.UtcNow;
            if (left <= TimeSpan.Zero)
                throw new Xunit.Sdk.XunitException($"{Name}: timed out waiting for an error. Got: {string.Join(", ", Received.Select(m => m.Type))}");
            await _arrived.WaitAsync(left < TimeSpan.FromMilliseconds(50) ? left : TimeSpan.FromMilliseconds(50), _cts.Token);
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_socket.State == WebSocketState.Open)
            {
                // Bounded: if the server never answers the handshake the test should fail on its own
                // assertion, not hang the whole run.
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", timeout.Token);
            }
        }
        catch (Exception)
        {
            // Closing a socket the other end has already dropped is not a failure worth reporting.
        }
    }

    private async Task ReadLoopAsync()
    {
        var buffer = new byte[64 * 1024];
        try
        {
            while (_socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                var sb = new StringBuilder();
                WebSocketReceiveResult result;
                do
                {
                    result = await _socket.ReceiveAsync(new ArraySegment<byte>(buffer), _cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close) return;
                    sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                } while (!result.EndOfMessage);

                var envelope = JObject.Parse(sb.ToString());
                string type = envelope.Value<string>("t") ?? "";
                JObject payload = envelope["p"] as JObject ?? new JObject();

                // A real client answers a ping before anything else looks at it, and so does this one:
                // without it the server closes the socket as idle halfway through a long test.
                if (type == Messages.Ping)
                {
                    await SendAsync(Messages.Pong, new JObject { ["t"] = payload["t"] });
                    continue;
                }

                lock (_gate) _received.Add((type, payload));
                _arrived.Release();
            }
        }
        catch (Exception)
        {
            // The socket ended. That is how every one of these tests finishes.
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await CloseAsync();
        }
        finally
        {
            // The reader must be awaited whatever happened above: a faulted task nobody observes is
            // collected by the finalizer, and that is what crashes the test host rather than failing a test.
            _cts.Cancel();
            try
            {
                await _reader;
            }
            catch (Exception)
            {
            }
            _socket.Dispose();
            _cts.Dispose();
        }
    }
}
