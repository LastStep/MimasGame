namespace Mimas.Server.Rooms;

/// <summary>
/// One turn's deadline, and the allowance the server adds before enforcing it. No sockets, no rooms: it
/// is arithmetic over <see cref="Environment.TickCount64"/> so it can be tested without either.
/// <para>
/// The allowance is the measured round trip, capped: a command that left the client before the deadline
/// must never be refused for arriving after it, because the player did everything right and the network
/// did not. Lichess compensates a move by up to a second of measured ping for the same reason.
/// </para>
/// </summary>
public sealed class RoomClock
{
    public int TurnMs { get; }
    public int LagGraceMs { get; }

    /// <summary>When the current turn is up, in <see cref="Environment.TickCount64"/> ms. 0 before the first turn.</summary>
    public long Deadline { get; private set; }

    public RoomClock(int turnMs, int lagGraceMs)
    {
        if (turnMs <= 0) throw new ArgumentOutOfRangeException(nameof(turnMs));
        if (lagGraceMs < 0) throw new ArgumentOutOfRangeException(nameof(lagGraceMs));
        TurnMs = turnMs;
        LagGraceMs = lagGraceMs;
    }

    public void Arm(long now) => Deadline = now + TurnMs;

    /// <summary>What the player's rope should show, never negative.</summary>
    public int Remaining(long now)
    {
        long left = Deadline - now;
        return left <= 0 ? 0 : (int)left;
    }

    /// <summary>The allowance for a seat with this round trip: <c>min(rtt, cap)</c>, and nothing for the bot.</summary>
    public int Grace(int rttMs)
    {
        if (rttMs <= 0) return 0;
        return rttMs < LagGraceMs ? rttMs : LagGraceMs;
    }

    /// <summary>True once the deadline plus that seat's allowance has passed.</summary>
    public bool Expired(long now, int rttMs) => Deadline > 0 && now >= Deadline + Grace(rttMs);
}
