using System;
using System.Collections.Generic;
using Mimas.Core.Match;
using Mimas.Core.Session;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Where the match comes from (ADR-028). The presenter — board, HUD, aiming, playback, facing — does not
    /// change between a local practice game and an online one; only this does.
    /// <para>
    /// <see cref="Rules"/> is a <c>MatchState</c> either way: the truth locally, a mirror rebuilt from the
    /// server's <c>PlayerView</c> online (ADR-026). So every preview question — reachable tiles, range bands,
    /// <c>CheckTarget</c>, the damage preview and its "?" row — is answered by the same code in both, and an
    /// illegal click is refused instantly without a round trip in both.
    /// </para>
    /// </summary>
    public interface IMatchDriver
    {
        /// <summary>The seat the person at this screen is playing.</summary>
        int LocalPlayer { get; }

        /// <summary>
        /// The truth (local) or the mirror (online) for the <b>round</b>. Null between rounds and after the
        /// series, where there is no board to answer questions about (ADR-036); every presenter path that
        /// touches it null-checks.
        /// </summary>
        MatchState Rules { get; }

        /// <summary>The latest projection for <see cref="LocalPlayer"/>. Null whenever <see cref="Rules"/> is.</summary>
        PlayerView View { get; }

        /// <summary>The series as this seat sees it: score, phase, own offers, the opponent's boons. Never null once <see cref="Ready"/>.</summary>
        SessionView Session { get; }

        /// <summary>False until there is a session to show. A session with no running round is still Ready.</summary>
        bool Ready { get; }

        /// <summary>
        /// Offers a command. Locally that validates and applies it; online it validates against the mirror —
        /// so a click the rules would refuse costs nothing and says so at once — and then sends it.
        /// Returns false when it was refused here rather than sent.
        /// </summary>
        bool Submit(Command command);

        /// <summary>
        /// Keeps one of the boons on offer. False when it was refused here: no draft is open, this seat has
        /// already picked, or the index is not one of its offers.
        /// </summary>
        bool SubmitDraftPick(int offerIndex);

        /// <summary>Concede the series. Legal at any time while it runs, including off turn and in a draft (P8).</summary>
        void Resign();

        /// <summary>Who is on the other side, for the HUD.</summary>
        string OpponentName { get; }

        /// <summary>
        /// A line about the opponent's connection, or null when there is nothing to say:
        /// "Opponent disconnected · 47 s", "Reconnecting…", "Match lost".
        /// </summary>
        string OpponentStatus { get; }

        /// <summary>Seconds left on the current turn, whoever's it is. Never negative.</summary>
        float TurnSecondsRemaining { get; }

        /// <summary>Length of the current turn in seconds.</summary>
        float TurnSecondsTotal { get; }

        /// <summary>False when resigning would do nothing: before the match, after it, or in local practice.</summary>
        bool CanResign { get; }

        /// <summary>Events for <see cref="LocalPlayer"/>, already filtered, in order. The presenter animates them.</summary>
        event Action<IReadOnlyList<MatchEvent>> EventsArrived;

        /// <summary>A whole fresh state replaced the old one (a reconnect, a resync): snap everything, animate nothing.</summary>
        event Action Resynced;

        /// <summary>
        /// A later round has begun. The presenter reloads the Arena scene and the fresh one adopts the
        /// start that is waiting for it — the same path a match start already takes (ADR-036).
        /// </summary>
        event Action NextRound;

        /// <summary>The opponent's status line or the clock's length changed; nothing about the board did.</summary>
        event Action StatusChanged;

        /// <summary>Called from the presenter's <c>Update</c>.</summary>
        void Tick(float deltaTime);

        void Dispose();
    }
}
