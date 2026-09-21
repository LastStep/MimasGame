using System.Security.Cryptography;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Server.Net;
using Mimas.Server.Options;
using Mimas.Server.Players;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Rooms;

public enum RoomPhase
{
    /// <summary>Someone is in it, choosing gear, possibly alone. Reachable by code.</summary>
    Waiting = 0,

    /// <summary>Both seats were ready; there is a match.</summary>
    Playing = 1,

    /// <summary>Nobody human is left. The room is closing and its code is about to be forgotten.</summary>
    Over = 2,
}

/// <summary>
/// One room: a four-letter code two friends can agree on, two seats, and — once both are ready — the
/// truth of one match. The room is the only thing that touches its <see cref="MatchState"/>, always under
/// one lock, and the only bytes that leave it are a <see cref="PlayerView"/> for one seat or the events
/// <see cref="EventFilter"/> passed for that seat. That is the hidden-information guarantee as a place in
/// the code rather than as a promise.
/// <para>
/// Room codes rather than a queue: OPT-0001 (spec amendment A1). Four friends in two arranged pairs are
/// not a matchmaking problem, and a queue pairs them in the order they click.
/// </para>
/// </summary>
public sealed class Room
{
    private readonly object _gate = new();
    private readonly ContentCatalog _catalog;
    private readonly ServerOptions _options;
    private readonly ILogger _log;
    private readonly Action<Room> _onClosed;
    private readonly Seat[] _seats = { new Seat(0), new Seat(1) };
    private readonly List<MatchEvent> _scratch = new();
    private readonly List<MatchEvent> _filtered = new();

    private CancellationTokenSource? _ticker;
    private RandomBot? _bot;
    private long _botDueAt;
    private int _seq;

    public int MatchId { get; }
    public string Code { get; }
    public RoomPhase Phase { get; private set; } = RoomPhase.Waiting;
    public RoomClock Clock { get; }

    /// <summary>
    /// Which match this is in the room's life: 1 for the first, 2 for the rematch, and so on. The room's
    /// <see cref="MatchId"/> is the room's own id and is reused across rounds, so this is what tells two
    /// matches apart in a log line (ADR-032).
    /// </summary>
    public int Round { get; private set; }

    /// <summary>
    /// The truth, while the match is being played. Never serialised, never handed out (golden rule 6).
    /// Non-null exactly while <see cref="Phase"/> is <see cref="RoomPhase.Playing"/>: the result is sent
    /// before the room goes back to waiting, and nothing may answer from a finished match afterwards.
    /// </summary>
    public MatchState? State { get; private set; }

    public IReadOnlyList<Seat> Seats => _seats;

    public Room(int matchId, string code, ContentCatalog catalog, ServerOptions options, ILogger log, Action<Room> onClosed)
    {
        MatchId = matchId;
        Code = code;
        _catalog = catalog;
        _options = options;
        _log = log;
        _onClosed = onClosed;
        Clock = new RoomClock(options.TurnMs ?? catalog.Rules.Clock.TurnMs, options.LagGraceMs ?? catalog.Rules.Clock.LagGraceMs);
    }

    private int ReconnectGraceMs => _options.ReconnectGraceMs ?? _catalog.Rules.Clock.ReconnectGraceMs;

    // ---- joining and leaving ---------------------------------------------------------------------

    /// <summary>Puts the host in seat 0, and the bot in seat 1 when this is a bot room.</summary>
    internal void Open(Player host, WsConnection connection, bool vsBot)
    {
        lock (_gate)
        {
            Occupy(_seats[0], host, connection);
            if (vsBot)
            {
                _seats[1].IsBot = true;
                _seats[1].BotName = _options.BotName;
                _seats[1].Loadout = BotLoadout();
                _seats[1].Ready = true;
            }
            _log.LogInformation("room {Code} ({MatchId}) opened by {Player}{Bot}", Code, MatchId, host, vsBot ? " vs bot" : "");
            BroadcastRoomState();
        }
    }

    /// <summary>The free seat, or -1 when there is none. Joining never starts the match: gear comes first.</summary>
    internal int TryJoin(Player player, WsConnection connection)
    {
        lock (_gate)
        {
            if (Phase != RoomPhase.Waiting) return -1;
            for (int i = 0; i < _seats.Length; i++)
            {
                if (_seats[i].Occupied) continue;
                Occupy(_seats[i], player, connection);
                _log.LogInformation("room {Code}: {Player} took seat {Seat}", Code, player, i);
                BroadcastRoomState();
                return i;
            }
            return -1;
        }
    }

    internal bool SetLoadout(Player player, Loadout loadout, bool ready)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null || Phase != RoomPhase.Waiting) return false;
            seat.Loadout = loadout;
            seat.Ready = ready;
            BroadcastRoomState();
            StartIfBothReady();
            return true;
        }
    }

    /// <summary>Leaves a room that has not started. The other seat keeps the room and its code, so a friend can still arrive.</summary>
    internal bool Leave(Player player)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null) return false;

            player.RoomId = 0;
            seat.Connection?.Send(Messages.RoomLeft, new JObject());
            seat.Clear();
            _log.LogInformation("room {Code}: a player left seat {Seat}", Code, seat.Index);

            if (!_seats.Any(s => s.IsHuman))
            {
                Close();
                return true;
            }
            BroadcastRoomState();
            return true;
        }
    }

    private void Occupy(Seat seat, Player player, WsConnection connection)
    {
        seat.Player = player;
        seat.Connection = connection;
        seat.DisconnectedAt = null;
        seat.Ready = false;
        seat.Loadout = null;
        player.RoomId = MatchId;
    }

    private Loadout BotLoadout()
    {
        string[] ids = _options.BotLoadout;
        return new Loadout(ids[0], ids[1], ids[2], ids[3]);
    }

    // ---- starting --------------------------------------------------------------------------------

    private void StartIfBothReady()
    {
        if (Phase != RoomPhase.Waiting) return;
        foreach (Seat seat in _seats)
            if (!seat.Occupied || !seat.Ready || seat.Loadout == null) return;

        // Seed and coin flip come from the server, not from either client, so neither can pick its map
        // side or its rolls. Everything downstream of the seed is deterministic (golden rule 4).
        var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        int firstPlayer = (int)new Mimas.Core.Rng(seed).Range(0, MatchSetup.PlayerCount);

        var setup = new MatchSetup(_options.MapId, _seats[0].Loadout!, _seats[1].Loadout!, firstPlayer);
        foreach (Seat seat in _seats)
        {
            if (!seat.IsBot) continue;
            foreach (string modifierId in _options.BotModifierIds) setup.WithModifier(seat.Index, modifierId);
            _bot = new RandomBot(seed ^ 0x9E3779B9);
        }

        State = new MatchState(_catalog, setup, seed);
        Phase = RoomPhase.Playing;
        _seq = 0;
        Round++;

        IReadOnlyList<MatchEvent> started = State.Start();
        long now = Environment.TickCount64;
        Clock.Arm(now);
        _botDueAt = now + _options.BotThinkMs;

        _log.LogInformation("room {Code}: match {MatchId} round {Round} started on {Map}, seed {Seed}, first player {First}",
            Code, MatchId, Round, _options.MapId, seed, firstPlayer);

        foreach (Seat seat in _seats) SendMatchStart(seat, started);
        StartTicking();
    }

    private void SendMatchStart(Seat seat, IReadOnlyList<MatchEvent>? events)
    {
        if (seat.Connection == null || !seat.IsHuman || State == null) return;

        _filtered.Clear();
        if (events != null) EventFilter.ForPlayer(events, seat.Index, State, _filtered);

        var p = new JObject
        {
            ["matchId"] = MatchId,
            ["round"] = Round,
            ["seq"] = _seq,
            ["mapId"] = State.MapData.Id,
            ["youAre"] = seat.Index,
            ["opponentName"] = _seats[1 - seat.Index].Name,
            ["view"] = Wire.View(State.ViewFor(seat.Index)),
            ["clock"] = ClockPayload(),
            ["events"] = Wire.Events(_filtered),
        };
        seat.Connection.Send(Messages.MatchStart, p);
    }

    // ---- playing ---------------------------------------------------------------------------------

    internal void HandleCommand(Player player, WsConnection connection, JObject commandJson)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null || State == null || Phase != RoomPhase.Playing)
            {
                connection.SendError(Messages.Errors.UnknownMatch, "You are not seated in a running match.");
                return;
            }

            Command command = Wire.ReadCommand(commandJson);
            if (command.Player != seat.Index)
            {
                Reject(seat, "not your seat");
                return;
            }

            _scratch.Clear();
            if (!State.TryApply(command, _scratch, out CommandResult result))
            {
                _log.LogDebug("room {Code}: refused {Command} ({Reason})", Code, command, result);
                Reject(seat, result.ToString());
                return;
            }

            _log.LogInformation("room {Code}: {Command}", Code, command);
            Broadcast(_scratch);
        }
    }

    internal void HandleResync(Player player, WsConnection connection)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null || State == null)
            {
                connection.SendError(Messages.Errors.UnknownMatch, "You are not seated in a running match.");
                return;
            }
            connection.Send(Messages.MatchView, new JObject
            {
                ["matchId"] = MatchId,
                ["seq"] = _seq,
                ["view"] = Wire.View(State.ViewFor(seat.Index)),
                ["clock"] = ClockPayload(),
            });
        }
    }

    private void Reject(Seat seat, string reason)
    {
        if (seat.Connection == null || State == null) return;
        seat.Connection.Send(Messages.MatchRejected, new JObject
        {
            ["matchId"] = MatchId,
            ["reason"] = reason,
            ["view"] = Wire.View(State.ViewFor(seat.Index)),
            ["clock"] = ClockPayload(),
        });
    }

    /// <summary>
    /// The one way anything reaches a client during a match: per seat, the events that seat is allowed
    /// and the view that seat is allowed. A <see cref="MatchState"/> never leaves this method.
    /// </summary>
    private void Broadcast(IReadOnlyList<MatchEvent> events)
    {
        if (State == null) return;
        _seq++;

        long now = Environment.TickCount64;
        MatchEvent? lastTurnStarted = events.LastOrDefault(e => e is TurnStartedEvent);
        if (lastTurnStarted != null && !State.IsOver)
        {
            Clock.Arm(now);
            _botDueAt = now + _options.BotThinkMs;
        }

        foreach (Seat seat in _seats)
        {
            if (!seat.IsHuman || seat.Connection == null) continue;
            _filtered.Clear();
            EventFilter.ForPlayer(events, seat.Index, State, _filtered);
            seat.Connection.Send(Messages.MatchEvents, new JObject
            {
                ["matchId"] = MatchId,
                ["seq"] = _seq,
                ["events"] = Wire.Events(_filtered),
                ["view"] = Wire.View(State.ViewFor(seat.Index)),
                ["clock"] = ClockPayload(),
            });
        }

        if (!State.IsOver) return;

        var ended = events.OfType<MatchEndedEvent>().LastOrDefault();
        _log.LogInformation("room {Code}: match {MatchId} round {Round} over, winner {Winner} ({Reason})",
            Code, MatchId, Round, State.Winner, ended?.Reason);
        ReturnToWaiting();
    }

    /// <summary>
    /// The match is over; the room is not (ADR-032). The code two friends already agreed on stays open,
    /// both seats keep their place and their gear, Ready goes back off, and pressing it again is the
    /// whole of the rematch. Only a room with nobody human left in it closes.
    /// <para>
    /// A seat whose socket is gone at the result is freed rather than held: the reconnect grace is a
    /// thing a *match* owes a player, and there is no match. That is what lets a friend who dropped
    /// come back by code, and a different friend take the seat.
    /// </para>
    /// </summary>
    private void ReturnToWaiting()
    {
        _ticker?.Cancel();
        _ticker = null;
        _bot = null;
        State = null;

        foreach (Seat seat in _seats)
        {
            if (seat.IsBot)
            {
                seat.Ready = true;            // the bot has nothing to choose and never un-readies
                continue;
            }
            if (!seat.IsHuman) continue;

            if (seat.Connection == null)
            {
                if (seat.Player != null) seat.Player.RoomId = 0;
                seat.Clear();
                _log.LogInformation("room {Code}: seat {Seat} was gone at the result; the seat is free", Code, seat.Index);
                continue;
            }

            // Loadout is deliberately kept: Ready is one click, and the preset dropdown still shows what
            // they played. It is still never broadcast (q-online-room-loadout).
            seat.Ready = false;
            seat.DisconnectedAt = null;
        }

        if (!_seats.Any(s => s.IsHuman))
        {
            Close();
            return;
        }

        Phase = RoomPhase.Waiting;
        BroadcastRoomState();
    }

    private JObject ClockPayload() => new()
    {
        ["activePlayer"] = State?.ActivePlayer ?? 0,
        ["turnMs"] = Clock.TurnMs,
        ["remainingMs"] = Clock.Remaining(Environment.TickCount64),
    };

    // ---- the tick --------------------------------------------------------------------------------

    private void StartTicking()
    {
        _ticker = new CancellationTokenSource();
        CancellationToken ct = _ticker.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_options.TickMs));
                while (await timer.WaitForNextTickAsync(ct)) Tick();
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                _log.LogError(e, "room {Code}: tick loop died", Code);
            }
        }, ct);
    }

    /// <summary>The clock, the bot and the reconnect graces, in that order. Internal so a test can step it by hand.</summary>
    internal void Tick()
    {
        lock (_gate)
        {
            if (State == null || Phase != RoomPhase.Playing) return;
            long now = Environment.TickCount64;

            Seat active = _seats[State.ActivePlayer];

            // 1. The clock. A bot never times out; it has nothing to wait for.
            if (!active.IsBot && Clock.Expired(now, active.RttMs))
            {
                _log.LogInformation("room {Code}: seat {Seat} timed out", Code, active.Index);
                Apply(new EndTurnCommand(active.Index, EndTurnReason.Timeout));
                if (State == null || Phase != RoomPhase.Playing) return;
                now = Environment.TickCount64;
                active = _seats[State.ActivePlayer];
            }

            // 2. The bot, once it has appeared to think for long enough.
            if (active.IsBot && _bot != null && now >= _botDueAt)
            {
                Command command = _bot.Choose(State, active.Index) ?? new EndTurnCommand(active.Index);
                Apply(command);
                _botDueAt = Environment.TickCount64 + _options.BotThinkMs;
                if (State == null || Phase != RoomPhase.Playing) return;
            }

            // 3. Anyone who has been gone too long concedes — through the same command a player would send.
            foreach (Seat seat in _seats)
            {
                if (!seat.IsHuman || seat.DisconnectedAt == null) continue;
                if (now - seat.DisconnectedAt.Value < ReconnectGraceMs) continue;
                _log.LogInformation("room {Code}: seat {Seat} did not return, forfeiting", Code, seat.Index);
                Apply(new ResignCommand(seat.Index, ResignReason.Disconnect));
                return;
            }
        }
    }

    /// <summary>Applies a command the server itself decided on (a timeout, a forfeit, the bot's move).</summary>
    private void Apply(Command command)
    {
        if (State == null) return;
        _scratch.Clear();
        if (!State.TryApply(command, _scratch, out CommandResult result))
        {
            _log.LogWarning("room {Code}: the server's own {Command} was refused ({Reason})", Code, command, result);
            return;
        }
        Broadcast(_scratch);
    }

    // ---- connections ------------------------------------------------------------------------------

    internal void OnConnectionClosed(WsConnection connection)
    {
        lock (_gate)
        {
            Seat? seat = _seats.FirstOrDefault(s => ReferenceEquals(s.Connection, connection));
            if (seat == null) return;

            seat.Connection = null;

            if (Phase == RoomPhase.Waiting)
            {
                // Nothing has been played, so nothing is owed: free the seat and keep the room for the
                // other player, who may still be waiting for a friend with the code.
                Player? player = seat.Player;
                if (player != null) player.RoomId = 0;
                seat.Clear();
                _log.LogInformation("room {Code}: seat {Seat} dropped before the match started", Code, seat.Index);
                if (!_seats.Any(s => s.IsHuman)) Close();
                else BroadcastRoomState();
                return;
            }

            if (Phase != RoomPhase.Playing) return;

            // The clock is deliberately not paused: a player who pulls the cable is not gaining time.
            seat.DisconnectedAt = Environment.TickCount64;
            _log.LogInformation("room {Code}: seat {Seat} disconnected, {Grace} ms of grace", Code, seat.Index, ReconnectGraceMs);
            TellOpponentAbout(seat);
        }
    }

    /// <summary>A returning player is put back in their seat and given the whole match as it stands.</summary>
    internal bool Reattach(Player player, WsConnection connection)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null) return false;

            seat.Connection = connection;
            seat.DisconnectedAt = null;
            _log.LogInformation("room {Code}: seat {Seat} came back", Code, seat.Index);

            if (Phase == RoomPhase.Playing)
            {
                SendMatchStart(seat, null);
                TellOpponentAbout(seat);
            }
            else
            {
                BroadcastRoomState();
            }
            return true;
        }
    }

    private void TellOpponentAbout(Seat seat)
    {
        Seat other = _seats[1 - seat.Index];
        if (!other.IsHuman || other.Connection == null) return;
        bool connected = seat.DisconnectedAt == null;
        int graceLeft = connected ? 0 : Math.Max(0, ReconnectGraceMs - (int)(Environment.TickCount64 - seat.DisconnectedAt!.Value));
        other.Connection.Send(Messages.OpponentStatus, new JObject
        {
            ["matchId"] = MatchId,
            ["connected"] = connected,
            ["graceMs"] = graceLeft,
        });
    }

    // ---- room state -------------------------------------------------------------------------------

    /// <summary>
    /// What the room screen draws. A seat's chosen gear is deliberately not in it: items are public once
    /// the match starts, but showing a preset during selection would invent a counter-pick rule the design
    /// page does not have (open question <c>q-online-room-loadout</c>).
    /// </summary>
    internal void BroadcastRoomState()
    {
        foreach (Seat seat in _seats)
        {
            if (!seat.IsHuman || seat.Connection == null) continue;
            seat.Connection.Send(Messages.RoomState, new JObject
            {
                ["code"] = Code,
                ["youAre"] = seat.Index,
                ["seats"] = new JArray(_seats.Select(s => new JObject
                {
                    ["name"] = s.Name,
                    ["ready"] = s.Ready,
                    ["bot"] = s.IsBot,
                    ["present"] = s.Occupied && (s.IsBot || s.Connection != null),
                }).Cast<object>().ToArray()),
            });
        }
    }

    internal Seat? SeatOf(Player player) => _seats.FirstOrDefault(s => ReferenceEquals(s.Player, player));

    private void Close()
    {
        _ticker?.Cancel();
        _ticker = null;
        Phase = RoomPhase.Over;
        foreach (Seat seat in _seats)
            if (seat.Player != null) seat.Player.RoomId = 0;
        _onClosed(this);
    }
}
