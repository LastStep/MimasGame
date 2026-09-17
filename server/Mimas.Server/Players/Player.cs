using Mimas.Server.Net;

namespace Mimas.Server.Players;

/// <summary>
/// One guest identity. It lives in memory for as long as the process does (D4: no database in M2) and is
/// reached again by its <see cref="Token"/>, which is what lets a browser refresh mid-match and come back
/// as the same person rather than as a stranger the room has never met.
/// </summary>
public sealed class Player
{
    /// <summary>1-based, unique for the life of the process.</summary>
    public int Id { get; }

    /// <summary>32 hex characters from a cryptographic source. Never logged.</summary>
    public string Token { get; }

    public string Name { get; internal set; }

    /// <summary>The socket this player is on right now, or null between a drop and a return.</summary>
    public WsConnection? Connection { get; internal set; }

    /// <summary>The room this player is in, waiting or playing; 0 when they are in the lobby.</summary>
    public int RoomId { get; internal set; }

    internal Player(int id, string token, string name)
    {
        Id = id;
        Token = token;
        Name = name;
    }

    public override string ToString() => $"P{Id} '{Name}'";
}
