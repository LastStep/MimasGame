using System.Security.Cryptography;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Core.Session;
using Mimas.Server.Net;
using Mimas.Server.Options;
using Mimas.Server.Players;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Rooms;

// The class is Mimas.Core.Session.Session; from another namespace the bare name finds the namespace
// first, so this alias inside our own namespace puts the class first (spec D part 2 §6.2).
using Session = Mimas.Core.Session.Session;

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
/// truth of one <b>session</b>: a best-of-3 with a draft between its rounds (ADR-036). The room is the only
/// thing that touches its <see cref="Session"/>, always under one lock, and the only bytes that leave it
/// are one seat's <see cref="PlayerView"/> and <see cref="SessionView"/> and the events
/// <see cref="SessionEventFilter"/> passed for that seat. That is the hidden-information guarantee as a
/// place in the code rather than as a promise.
/// <para>
/// The room never reads the session's rules: it forwards commands, arms the clocks, submits the timeouts
/// and the forfeits as commands, and splits the outgoing batches — a batch carrying a
/// <see cref="RoundStartedEvent"/> goes out as <c>match.start</c>, everything else as <c>match.events</c>.
/// </para>
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

    /// <summary>When the open draft is up, in <see cref="Environment.TickCount64"/> ms; 0 when no draft is open.</summary>
    private long _draftDeadline;

    private int _seq;

    public int MatchId { get; }
    public string Code { get; }
    public RoomPhase Phase { get; private set; } = RoomPhase.Waiting;
    public RoomClock Clock { get; }

    /// <summary>
    /// Which <b>series</b> this is in the room's life: 1 for the first, 2 for the rematch, and so on. The
    /// room's <see cref="MatchId"/> is the room's own id and is reused across series, so this is what tells
    /// two of them apart in a log line (ADR-032, ADR-036). The round inside a series is
    /// <see cref="Mimas.Core.Session.Session.Round"/>.
    /// </summary>
    public int Series { get; private set; }

    /// <summary>
    /// The truth, while the series is being played. Never serialised, never handed out (golden rule 6).
    /// Non-null exactly while <see cref="Phase"/> is <see cref="RoomPhase.Playing"/>: the result is sent
    /// before the room goes back to waiting, and nothing may answer from a finished series afterwards.
    /// </summary>
    public Session? Session { get; private set; }

    /// <summary>The running round, or null between rounds. Shorthand for the places that only need the round.</summary>
    public MatchState? State => Session?.Match;

    /// <summary>Drawn once, when a bot room opens: what the bot's lineage is picked with (P6).</summary>
    public uint RoomSeed { get; private set; }

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

    private int DraftTimeoutMs => _options.DraftTimeoutMs ?? _catalog.Rules.Draft.TimeoutMs;

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
                _seats[1].LineageId = _options.BotLineageId ?? PickBotLineage();
                _seats[1].Ready = true;
            }
            _log.LogInformation("room {Code} ({MatchId}) opened by {Player}{Bot}", Code, MatchId, host,
                vsBot ? $" vs bot ({_seats[1].LineageId})" : "");
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

    internal bool SetLoadout(Player player, Loadout loadout, string lineageId, bool ready)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null || Phase != RoomPhase.Waiting) return false;
            seat.Loadout = loadout;
            seat.LineageId = lineageId;
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
        seat.LineageId = null;
        player.RoomId = MatchId;
    }

    private Loadout BotLoadout()
    {
        string[] ids = _options.BotLoadout;
        return new Loadout(ids[0], ids[1], ids[2], ids[3]);
    }

    /// <summary>One draw, at the room's birth, from the catalogue's lineages in id order (P6).</summary>
    private string PickBotLineage()
    {
        RoomSeed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        List<string> ids = _catalog.Lineages.All.Select(l => l.Id).OrderBy(id => id, StringComparer.Ordinal).ToList();
        return ids[(int)new Mimas.Core.Rng(RoomSeed).Range(0, ids.Count)];
    }

    // ---- starting --------------------------------------------------------------------------------

    private void StartIfBothReady()
    {
        if (Phase != RoomPhase.Waiting) return;
        foreach (Seat seat in _seats)
            if (!seat.Occupied || !seat.Ready || seat.Loadout == null || seat.LineageId == null) return;

        // The seed comes from the server, not from either client, so neither can pick its rolls. Everything
        // downstream of it is deterministic (golden rule 4) — the coin flip, every round's seed, every
        // draft's shuffle are the session's own, drawn from this one number.
        var seed = (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);
        var setup = new SessionSetup(
            new PlayerBuild(_seats[0].Loadout!, _seats[0].LineageId!),
            new PlayerBuild(_seats[1].Loadout!, _seats[1].LineageId!));

        Session = new Session(_catalog, setup, seed);
        foreach (Seat seat in _seats)
            if (seat.IsBot) _bot = new RandomBot(seed ^ 0x9E3779B9);

        Phase = RoomPhase.Playing;
        _seq = 0;
        _draftDeadline = 0;
        Series++;

        IReadOnlyList<MatchEvent> started = Session.Start();
        long now = Environment.TickCount64;
        Clock.Arm(now);
        _botDueAt = now + _options.BotThinkMs;

        RoundStartedEvent round1 = started.OfType<RoundStartedEvent>().First();
        _log.LogInformation("room {Code}: match {MatchId} series {Series} round {Round} started on {Map}, seed {Seed}, first player {First}",
            Code, MatchId, Series, round1.Round, round1.MapId, seed, round1.FirstPlayer);

        foreach (Seat seat in _seats) SendMatchStart(seat, started);
        StartTicking();
    }

    private void SendMatchStart(Seat seat, IReadOnlyList<MatchEvent>? events)
    {
        if (seat.Connection == null || !seat.IsHuman || Session == null) return;

        _filtered.Clear();
        if (events != null) SessionEventFilter.ForPlayer(events, seat.Index, Session, _filtered);

        SessionView session = SessionView.For(Session, seat.Index);
        var p = new JObject
        {
            ["matchId"] = MatchId,
            ["series"] = Series,
            ["round"] = Session.Round,
            ["seq"] = _seq,
            ["mapId"] = session.MapId,
            ["youAre"] = seat.Index,
            ["opponentName"] = _seats[1 - seat.Index].Name,
            ["view"] = ViewPayload(session),
            ["session"] = Wire.Session(session),
            ["clock"] = ClockPayload(),
            ["events"] = Wire.Events(_filtered),
        };
        seat.Connection.Send(Messages.MatchStart, p);
    }

    /// <summary>The round's view, or an explicit null between rounds — never an omitted field.</summary>
    private static JToken ViewPayload(SessionView session)
        => session.Match != null ? Wire.View(session.Match) : JValue.CreateNull();

    // ---- playing ---------------------------------------------------------------------------------

    internal void HandleCommand(Player player, WsConnection connection, JObject commandJson)
    {
        lock (_gate)
        {
            Seat? seat = SeatOf(player);
            if (seat == null || Session == null || Phase != RoomPhase.Playing)
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
            if (!Session.TryApply(command, _scratch, out CommandResult result))
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
            if (seat == null || Session == null)
            {
                connection.SendError(Messages.Errors.UnknownMatch, "You are not seated in a running match.");
                return;
            }
            SessionView session = SessionView.For(Session, seat.Index);
            connection.Send(Messages.MatchView, new JObject
            {
                ["matchId"] = MatchId,
                ["seq"] = _seq,
                ["view"] = ViewPayload(session),
                ["session"] = Wire.Session(session),
                ["clock"] = ClockPayload(),
            });
        }
    }

    private void Reject(Seat seat, string reason)
    {
        if (seat.Connection == null || Session == null) return;
        SessionView session = SessionView.For(Session, seat.Index);
        seat.Connection.Send(Messages.MatchRejected, new JObject
        {
            ["matchId"] = MatchId,
            ["reason"] = reason,
            ["view"] = ViewPayload(session),
            ["session"] = Wire.Session(session),
            ["clock"] = ClockPayload(),
        });
    }

    /// <summary>
    /// The one way anything reaches a client during a match: per seat, the events that seat is allowed
    /// and the view that seat is allowed. A <see cref="MatchState"/> never leaves this method.
    /// </summary>
    private void Broadcast(IReadOnlyList<MatchEvent> events)
    {
        if (Session == null) return;
        _seq++;

        // The clocks. A round's turn clock re-arms on the last turnStarted; a draft has one deadline for
        // both seats, armed when the draft opens, and no lag grace — nobody is racing a move.
        long now = Environment.TickCount64;
        if (Session.Phase == SessionPhase.Round && events.Any(e => e is TurnStartedEvent))
        {
            Clock.Arm(now);
            _botDueAt = now + _options.BotThinkMs;
        }
        if (events.Any(e => e is DraftStartedEvent) && Session.Phase == SessionPhase.Draft)
        {
            _draftDeadline = now + DraftTimeoutMs;
            _botDueAt = now + _options.BotThinkMs;
        }

        // The split rule (ADR-036): a batch that started a round is that round's match.start.
        bool startsARound = events.Any(e => e is RoundStartedEvent);
        foreach (Seat seat in _seats)
        {
            if (!seat.IsHuman || seat.Connection == null) continue;
            if (startsARound)
            {
                SendMatchStart(seat, events);
                continue;
            }
            _filtered.Clear();
            SessionEventFilter.ForPlayer(events, seat.Index, Session, _filtered);
            SessionView session = SessionView.For(Session, seat.Index);
            seat.Connection.Send(Messages.MatchEvents, new JObject
            {
                ["matchId"] = MatchId,
                ["seq"] = _seq,
                ["events"] = Wire.Events(_filtered),
                ["view"] = ViewPayload(session),
                ["session"] = Wire.Session(session),
                ["clock"] = ClockPayload(),
            });
        }

        var roundEnded = events.OfType<RoundEndedEvent>().LastOrDefault();
        if (roundEnded != null)
            _log.LogInformation("room {Code}: match {MatchId} series {Series} round {Round} over, winner {Winner} ({Reason}), score {Score0}-{Score1}",
                Code, MatchId, Series, roundEnded.Round, roundEnded.Winner, roundEnded.Reason, roundEnded.Score0, roundEnded.Score1);

        if (!Session.IsOver) return;

        var over = events.OfType<SessionEndedEvent>().LastOrDefault();
        _log.LogInformation("room {Code}: match {MatchId} series {Series} over, winner {Winner} {Score0}-{Score1} ({Reason})",
            Code, MatchId, Series, Session.Winner, over?.Score0, over?.Score1, over?.Reason);
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
        Session = null;
        _draftDeadline = 0;

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

            // Loadout and lineage are deliberately kept: Ready is one click, and the preset dropdown and
            // the lineage row still show what they played. Neither is ever broadcast (q-online-room-loadout).
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

    /// <summary>
    /// One shape, two modes: a turn's deadline for the active seat, or — in a draft — one deadline for both,
    /// which <c>activePlayer: -1</c> says. The client re-anchors on it exactly the same way either way.
    /// </summary>
    private JObject ClockPayload()
    {
        long now = Environment.TickCount64;
        if (Session != null && Session.Phase == SessionPhase.Draft)
        {
            long left = _draftDeadline - now;
            return new JObject
            {
                ["activePlayer"] = -1,
                ["turnMs"] = DraftTimeoutMs,
                ["remainingMs"] = left <= 0 ? 0 : (int)left,
            };
        }
        return new JObject
        {
            ["activePlayer"] = State?.ActivePlayer ?? 0,
            ["turnMs"] = Clock.TurnMs,
            ["remainingMs"] = Clock.Remaining(now),
        };
    }

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

    /// <summary>
    /// The clocks, the bot and the reconnect graces. Two modes, because a session has two: inside a round
    /// it is the turn clock and the bot's move, between rounds it is the one draft deadline and the bot's
    /// pick. The graces run in both — a seat that never comes back forfeits the series either way.
    /// Internal so a test can step it by hand.
    /// </summary>
    internal void Tick()
    {
        lock (_gate)
        {
            if (Session == null || Phase != RoomPhase.Playing) return;
            switch (Session.Phase)
            {
                case SessionPhase.Round:
                    TickRound();
                    return;
                case SessionPhase.Draft:
                    TickDraft();
                    return;
                default:
                    return;                                     // Over: ReturnToWaiting already ran
            }
        }
    }

    private void TickRound()
    {
        long now = Environment.TickCount64;
        Seat active = _seats[Session!.Match!.ActivePlayer];

        // 1. The clock. A bot never times out; it has nothing to wait for.
        if (!active.IsBot && Clock.Expired(now, active.RttMs))
        {
            _log.LogInformation("room {Code}: seat {Seat} timed out", Code, active.Index);
            Apply(new EndTurnCommand(active.Index, EndTurnReason.Timeout));
            if (!StillIn(SessionPhase.Round)) return;
            now = Environment.TickCount64;
            active = _seats[Session!.Match!.ActivePlayer];
        }

        // 2. The bot, once it has appeared to think for long enough.
        if (active.IsBot && _bot != null && now >= _botDueAt)
        {
            Command command = _bot.Choose(Session!.Match!, active.Index) ?? new EndTurnCommand(active.Index);
            Apply(command);
            _botDueAt = Environment.TickCount64 + _options.BotThinkMs;
            if (!StillIn(SessionPhase.Round)) return;
        }

        TickGraces(Environment.TickCount64);
    }

    private void TickDraft()
    {
        // 1. The deadline, with no lag grace: the server keeps offer 0 for whoever has not chosen (#draft rule 4).
        if (_draftDeadline > 0 && Environment.TickCount64 >= _draftDeadline)
        {
            foreach (Seat seat in _seats)
            {
                if (!seat.IsHuman || Session!.HasPicked(seat.Index)) continue;
                _log.LogInformation("room {Code}: seat {Seat} did not pick in time", Code, seat.Index);
                Apply(new DraftPickCommand(seat.Index, 0, DraftPickReason.Timeout));
                if (!StillIn(SessionPhase.Draft)) return;
            }
        }

        // 2. The bot picks after the same think delay it uses for a move.
        Seat? bot = _seats.FirstOrDefault(s => s.IsBot);
        if (bot != null && _bot != null && !Session!.HasPicked(bot.Index) && Environment.TickCount64 >= _botDueAt)
        {
            Command? pick = _bot.ChooseDraft(Session, bot.Index);
            if (pick != null) Apply(pick);
            if (!StillIn(SessionPhase.Draft)) return;
        }

        TickGraces(Environment.TickCount64);
    }

    /// <summary>Anyone who has been gone too long concedes the series — through the same command a player would send.</summary>
    private void TickGraces(long now)
    {
        foreach (Seat seat in _seats)
        {
            if (!seat.IsHuman || seat.DisconnectedAt == null) continue;
            if (now - seat.DisconnectedAt.Value < ReconnectGraceMs) continue;
            _log.LogInformation("room {Code}: seat {Seat} did not return, forfeiting the series", Code, seat.Index);
            Apply(new ResignCommand(seat.Index, ResignReason.Disconnect));
            return;
        }
    }

    private bool StillIn(SessionPhase phase) => Session != null && Phase == RoomPhase.Playing && Session.Phase == phase;

    /// <summary>Applies a command the server itself decided on (a timeout, a forfeit, the bot's move or pick).</summary>
    private void Apply(Command command)
    {
        if (Session == null) return;
        _scratch.Clear();
        if (!Session.TryApply(command, _scratch, out CommandResult result))
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
