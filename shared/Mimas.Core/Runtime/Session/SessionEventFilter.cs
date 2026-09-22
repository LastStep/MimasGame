using System;
using System.Collections.Generic;
using Mimas.Core.Match;

namespace Mimas.Core.Session
{
    /// <summary>
    /// Trims a session's event list to what one player may receive (ADR-010 at the session level): a
    /// <see cref="DraftStartedEvent"/> keeps only the viewer's own offers, the opponent's
    /// <see cref="DraftPickedEvent"/> loses its boon id ("a pick was made", design: #draft rule 5), and a
    /// round's events go through <see cref="EventFilter"/> with the state that produced them. One
    /// <see cref="Session.Apply"/> can end a round and start the next; a <see cref="RoundStartedEvent"/> is
    /// where the state switches from the round that ended to the one that began.
    /// </summary>
    public static class SessionEventFilter
    {
        public static void ForPlayer(IReadOnlyList<MatchEvent> events, int viewer, Session session, List<MatchEvent> into)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (session == null) throw new ArgumentNullException(nameof(session));
            if (into == null) throw new ArgumentNullException(nameof(into));
            if (viewer < 0 || viewer >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));

            // Events before a RoundStartedEvent belong to the round that ended (still the last match); after
            // it, to the round that began. When the list is one round's events, both are the same state.
            MatchState state = session.LastMatch;
            for (int i = 0; i < events.Count; i++)
            {
                MatchEvent e = events[i];
                switch (e)
                {
                    case RoundStartedEvent started:
                        state = session.LastMatch;
                        into.Add(started);
                        break;
                    case DraftStartedEvent draft:
                        into.Add(new DraftStartedEvent(draft.Round, viewer == 0 ? draft.Offers0 : null, viewer == 1 ? draft.Offers1 : null));
                        break;
                    case DraftPickedEvent picked:
                        into.Add(picked.Player == viewer ? picked : new DraftPickedEvent(picked.Player, null, picked.Reason));
                        break;
                    case SessionEvent other:
                        into.Add(other);
                        break;
                    default:
                        if (state == null) throw new InvalidOperationException("A round event arrived before any round started.");
                        MatchEvent filtered = EventFilter.ForPlayer(e, viewer, state);
                        if (filtered != null) into.Add(filtered);
                        break;
                }
            }
        }
    }
}
