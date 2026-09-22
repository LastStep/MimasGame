using Mimas.Core.Match;
using Mimas.Server.Net;
using Mimas.Server.Players;

namespace Mimas.Server.Rooms;

/// <summary>
/// One of a room's two places: who is in it, what they chose to wear, whether they are ready, and the
/// socket they are currently on. A seat outlives a connection — that is the whole point of the reconnect
/// grace — so <see cref="Connection"/> going null is a fact about the network, not about the player.
/// </summary>
public sealed class Seat
{
    public int Index { get; }

    /// <summary>The human in this seat, or null when it is empty or the bot's.</summary>
    public Player? Player { get; internal set; }

    public bool IsBot { get; internal set; }

    /// <summary>Display name: the player's, or the bot's. Null while the seat is empty.</summary>
    public string? Name => IsBot ? BotName : Player?.Name;

    internal string? BotName { get; set; }

    /// <summary>Gear chosen in the room. Null until chosen — and a seat with no loadout can never be ready.</summary>
    public Loadout? Loadout { get; internal set; }

    /// <summary>
    /// The lineage prayed to, chosen in the room under the preset. Null until chosen, and a seat without
    /// one can never be ready. Never broadcast: which god you chose is hidden until a reveal says so
    /// (design: #lineage rule 3).
    /// </summary>
    public string? LineageId { get; internal set; }

    public bool Ready { get; internal set; }

    /// <summary>The socket this seat is attached to, or null while the player is away. Always null for the bot.</summary>
    public WsConnection? Connection { get; internal set; }

    /// <summary>When the seat's socket dropped (<see cref="Environment.TickCount64"/>), or null while it is present.</summary>
    public long? DisconnectedAt { get; internal set; }

    /// <summary>Last known round trip for this seat, in ms; the lag grace is drawn from it.</summary>
    public int RttMs => Connection?.RttMs ?? 0;

    public bool Occupied => IsBot || Player != null;

    public bool IsHuman => Player != null;

    public Seat(int index)
    {
        Index = index;
    }

    internal void Clear()
    {
        Player = null;
        IsBot = false;
        BotName = null;
        Loadout = null;
        LineageId = null;
        Ready = false;
        Connection = null;
        DisconnectedAt = null;
    }

    public override string ToString() => $"seat {Index} {(Occupied ? Name : "(empty)")}";
}
