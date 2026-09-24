namespace Mimas.Client.UI
{
    /// <summary>One seat of a room as the server's <c>room.state</c> describes it. Null means the server sent nothing for it.</summary>
    public sealed class RoomSeat
    {
        public string Name;
        public bool Present;
        public bool Ready;
        public bool Bot;
    }

    /// <summary>What the room's right-hand side says about the other seat (docs/ui/lobby.md §3, <c>room.them.*</c>).</summary>
    public struct OtherSeatText
    {
        /// <summary>Their name in capitals, or "WAITING FOR A PLAYER".</summary>
        public string Name;

        /// <summary>"READY" or "CHOOSING"; empty while the seat is empty.</summary>
        public string State;

        /// <summary>The dot is filled.</summary>
        public bool Ready;

        /// <summary>Nobody is in the seat: the name is the waiting line, and neither the state nor the note shows.</summary>
        public bool Waiting;
    }

    /// <summary>
    /// The room's pure rules (docs/ui/lobby.md §3; spec H §8, §9), kept out of <see cref="LobbyView"/> so they
    /// are testable without a scene: which seat is drawn on which side, and what the other seat may say. The
    /// other seat only ever says its name and whether it is ready — never its gear or its god
    /// (<c>#q-online-room-loadout</c> stays open).
    /// </summary>
    public static class RoomLayout
    {
        public const string WaitingForAPlayer = "WAITING FOR A PLAYER";
        public const string ReadyText = "READY";
        public const string ChoosingText = "CHOOSING";

        /// <summary>
        /// The seat drawn on the left: always yours, whichever you hold — the same left as the camera and the turn
        /// track. A seat index the server should never send reads as seat 0.
        /// </summary>
        public static int LeftSeat(int mySeat) => mySeat == 1 ? 1 : 0;

        /// <summary>The seat drawn on the right: the other one.</summary>
        public static int RightSeat(int mySeat) => 1 - LeftSeat(mySeat);

        /// <summary>
        /// The other seat's lines. A missing or empty seat waits for a player; a bot is always ready (it has
        /// nothing to choose); anyone else is ready or choosing.
        /// </summary>
        public static OtherSeatText Other(RoomSeat seat)
        {
            if (seat == null || !seat.Present)
                return new OtherSeatText { Name = WaitingForAPlayer, State = string.Empty, Ready = false, Waiting = true };

            bool ready = seat.Bot || seat.Ready;
            string name = string.IsNullOrEmpty(seat.Name) ? (seat.Bot ? "RANDOM BOT" : "GUEST") : seat.Name.ToUpperInvariant();
            return new OtherSeatText { Name = name, State = ready ? ReadyText : ChoosingText, Ready = ready, Waiting = false };
        }

        /// <summary>
        /// The lobby's last result (docs/ui/lobby.md §2 <c>lb.last</c>): "VICTORY VS GUEST-2869 · SERIES 2 – 1", your
        /// score first. When the score does not explain the result — a resignation or a forfeit before the rounds
        /// decided it, "VICTORY … · SERIES 0 – 0" — the reason follows it, as on the series band; with no score at all
        /// the reason stands in.
        /// </summary>
        public static string LastResultLine(bool won, string opponentName, bool hasScore, int mine, int theirs, string reason)
        {
            string line = (won ? "Victory" : "Defeat") + " vs " + (string.IsNullOrEmpty(opponentName) ? "opponent" : opponentName);
            bool scoreExplains = hasScore && (won ? mine > theirs : theirs > mine);
            if (hasScore) line += " · series " + mine + " – " + theirs;
            if (!scoreExplains && !string.IsNullOrEmpty(reason)) line += " · " + reason;
            return line.ToUpperInvariant();
        }
    }

    /// <summary>
    /// What you have chosen in the room and whether you are ready: the preset tile (spec H §8, which replaced the
    /// dropdown's index) and the Ready toggle. A choice is locked while you are ready — the tiles are disabled —
    /// and un-readying keeps it.
    /// </summary>
    public sealed class RoomChoice
    {
        private readonly int _presetCount;

        public RoomChoice(int presetCount)
        {
            _presetCount = presetCount < 0 ? 0 : presetCount;
        }

        /// <summary>The selected preset; 0, the first, on every scene load (remembering it is a follow-up).</summary>
        public int PresetIndex { get; private set; }

        public bool Ready { get; private set; }

        /// <summary>A click on a tile. False when nothing changes: the same tile, one out of range, or while ready.</summary>
        public bool SelectPreset(int index)
        {
            if (Ready || index < 0 || index >= _presetCount || index == PresetIndex) return false;
            PresetIndex = index;
            return true;
        }

        /// <summary>The Ready button: ready if not, not ready if so. Returns the new state.</summary>
        public bool ToggleReady()
        {
            Ready = !Ready;
            return Ready;
        }

        /// <summary>Back to choosing (a change of god, a result, leaving). True when you were ready, so the server must be told.</summary>
        public bool UnReady()
        {
            if (!Ready) return false;
            Ready = false;
            return true;
        }
    }
}
