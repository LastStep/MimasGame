using System.Security.Cryptography;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Server.Net;
using Mimas.Server.Options;
using Mimas.Server.Players;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Rooms;

/// <summary>
/// Every open room, by match id and by code, and the front door for every room message. It hands work to
/// a <see cref="Room"/> and otherwise only decides which one, and whether you are allowed near it.
/// </summary>
public sealed class RoomRegistry
{
    /// <summary>No I, O, 0 or 1: a code is read aloud over a call as often as it is pasted.</summary>
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private const int CodeLength = 4;

    private readonly object _gate = new();
    private readonly Dictionary<int, Room> _byId = new();
    private readonly Dictionary<string, Room> _byCode = new(StringComparer.Ordinal);
    private readonly ContentCatalog _catalog;
    private readonly ServerOptions _options;
    private readonly ILogger<RoomRegistry> _log;
    private int _nextMatchId = 1;

    public RoomRegistry(ContentCatalog catalog, ServerOptions options, ILogger<RoomRegistry> log)
    {
        _catalog = catalog;
        _options = options;
        _log = log;
    }

    public int Count
    {
        get { lock (_gate) return _byId.Count; }
    }

    public int WaitingCount
    {
        get { lock (_gate) return _byId.Values.Count(r => r.Phase == RoomPhase.Waiting); }
    }

    public Room? TryGet(int matchId)
    {
        lock (_gate) return _byId.TryGetValue(matchId, out Room? room) ? room : null;
    }

    public Room? TryGetByCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        string normalised = code.Trim().ToUpperInvariant();
        lock (_gate) return _byCode.TryGetValue(normalised, out Room? room) ? room : null;
    }

    // ---- message handlers --------------------------------------------------------------------------

    public void HandleCreate(Player player, WsConnection connection, bool vsBot)
    {
        if (Busy(player, connection)) return;

        Room room;
        lock (_gate)
        {
            room = new Room(_nextMatchId++, NewCode(), _catalog, _options, _log, Forget);
            _byId[room.MatchId] = room;
            _byCode[room.Code] = room;
        }
        room.Open(player, connection, vsBot);
    }

    public void HandleJoin(Player player, WsConnection connection, string? code)
    {
        if (Busy(player, connection)) return;

        Room? room = TryGetByCode(code);
        if (room == null)
        {
            connection.SendError(Messages.Errors.NoSuchRoom, $"No open room has the code '{code}'.");
            return;
        }
        if (room.TryJoin(player, connection) < 0)
            connection.SendError(Messages.Errors.RoomFull, "That room is full.");
    }

    public void HandleLoadout(Player player, WsConnection connection, JObject p)
    {
        Room? room = RoomOf(player);
        if (room == null)
        {
            connection.SendError(Messages.Errors.NotInRoom, "You are not in a room.");
            return;
        }

        if (p["loadout"] is not JObject json)
        {
            connection.SendError(Messages.Errors.BadLoadout, "A loadout is four item ids: weapon, crown, boots, armour.");
            return;
        }

        string? lineageId = p.Value<string>("lineage");
        if (string.IsNullOrWhiteSpace(lineageId))
        {
            connection.SendError(Messages.Errors.BadLoadout, $"A lineage is required: one of {KnownLineages()}.");
            return;
        }

        Loadout loadout;
        try
        {
            loadout = new Loadout(
                json.Value<string>("weapon") ?? "",
                json.Value<string>("crown") ?? "",
                json.Value<string>("boots") ?? "",
                json.Value<string>("armour") ?? "");

            // Every id must exist and must fill the slot it was put in; nothing is stored until all four do,
            // and until the lineage is one the catalogue knows.
            foreach (string slot in ItemSlots.All) _catalog.GetItemForSlot(slot, loadout.IdForSlot(slot));
            _catalog.GetLineage(lineageId);
        }
        catch (Exception e) when (e is ArgumentException || e is ContentLoadException || e is KeyNotFoundException)
        {
            connection.SendError(Messages.Errors.BadLoadout, e.Message);
            return;
        }

        bool ready = p.Value<bool?>("ready") ?? true;
        if (!room.SetLoadout(player, loadout, lineageId, ready))
            connection.SendError(Messages.Errors.NotInRoom, "That room is no longer choosing.");
    }

    public void HandleLeave(Player player, WsConnection connection)
    {
        Room? room = RoomOf(player);
        if (room == null || !room.Leave(player))
            connection.SendError(Messages.Errors.NotInRoom, "You are not in a room.");
    }

    public void HandleCommand(Player player, WsConnection connection, JObject p)
    {
        Room? room = RoomOf(player);
        if (room == null || room.MatchId != (p.Value<int?>("matchId") ?? room.MatchId))
        {
            connection.SendError(Messages.Errors.UnknownMatch, "You are not seated in that match.");
            return;
        }
        if (p["command"] is not JObject command)
        {
            connection.SendError(Messages.Errors.BadJson, "match.command needs a command object.");
            return;
        }
        room.HandleCommand(player, connection, command);
    }

    public void HandleResync(Player player, WsConnection connection, JObject p)
    {
        Room? room = RoomOf(player);
        if (room == null)
        {
            connection.SendError(Messages.Errors.UnknownMatch, "You are not seated in a match.");
            return;
        }
        room.HandleResync(player, connection);
    }

    /// <summary>After <c>auth.resume</c>: if this player's seat is still held somewhere, put them back in it.</summary>
    public void Reattach(Player player, WsConnection connection)
    {
        RoomOf(player)?.Reattach(player, connection);
    }

    /// <summary>
    /// The code of the room this player is seated in while it is choosing gear, or null. It is what
    /// <c>auth.ok</c> carries after a reload, so a client that comes back between matches opens on its
    /// room instead of a lobby whose buttons would all answer <c>in_room</c> (ADR-032).
    /// </summary>
    public string? WaitingRoomCodeOf(Player player)
    {
        Room? room = RoomOf(player);
        return room != null && room.Phase == RoomPhase.Waiting ? room.Code : null;
    }

    /// <summary>A socket ended. Whichever room was holding it needs to know; nothing else does.</summary>
    public void OnConnectionClosed(WsConnection connection)
    {
        List<Room> rooms;
        lock (_gate) rooms = _byId.Values.ToList();
        foreach (Room room in rooms) room.OnConnectionClosed(connection);
    }

    // ---- plumbing ----------------------------------------------------------------------------------

    private Room? RoomOf(Player player) => player.RoomId == 0 ? null : TryGet(player.RoomId);

    /// <summary>The catalogue's lineage ids, sorted, for the message a missing lineage gets.</summary>
    private string KnownLineages()
        => string.Join(", ", _catalog.Lineages.All.Select(l => l.Id).OrderBy(id => id, StringComparer.Ordinal));

    /// <summary>True (and the client has been told) when this player already has somewhere to be.</summary>
    private bool Busy(Player player, WsConnection connection)
    {
        Room? room = RoomOf(player);
        if (room == null) return false;
        if (room.Phase == RoomPhase.Playing)
        {
            connection.SendError(Messages.Errors.InMatch, "You are already in a match.");
            return true;
        }
        connection.SendError(Messages.Errors.InRoom, $"You are already in room {room.Code}.");
        return true;
    }

    private void Forget(Room room)
    {
        lock (_gate)
        {
            _byId.Remove(room.MatchId);
            if (_byCode.TryGetValue(room.Code, out Room? held) && ReferenceEquals(held, room)) _byCode.Remove(room.Code);
        }
        _log.LogInformation("room {Code} ({MatchId}) closed", room.Code, room.MatchId);
    }

    /// <summary>Caller holds the lock.</summary>
    private string NewCode()
    {
        for (int attempt = 0; attempt < 1000; attempt++)
        {
            var chars = new char[CodeLength];
            for (int i = 0; i < CodeLength; i++) chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
            var code = new string(chars);
            if (!_byCode.ContainsKey(code)) return code;
        }
        throw new InvalidOperationException("Could not find a free room code; far too many rooms are open.");
    }
}
