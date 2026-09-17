using System;
using System.Collections.Generic;
using Mimas.Core.Match;

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

        /// <summary>The truth (local) or the mirror (online). Never null once <see cref="Ready"/>.</summary>
        MatchState Rules { get; }

        /// <summary>The latest projection for <see cref="LocalPlayer"/>.</summary>
        PlayerView View { get; }

        /// <summary>False until there is a match to show.</summary>
        bool Ready { get; }

        /// <summary>
        /// Offers a command. Locally that validates and applies it; online it validates against the mirror —
        /// so a click the rules would refuse costs nothing and says so at once — and then sends it.
        /// Returns false when it was refused here rather than sent.
        /// </summary>
        bool Submit(Command command);

        /// <summary>Concede. Legal at any time while the match runs, including off turn.</summary>
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

        /// <summary>The opponent's status line or the clock's length changed; nothing about the board did.</summary>
        event Action StatusChanged;

        /// <summary>Called from the presenter's <c>Update</c>.</summary>
        void Tick(float deltaTime);

        void Dispose();
    }
}
