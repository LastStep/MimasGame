using System;
using System.Collections.Generic;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Session;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    using Session = Mimas.Core.Session.Session;

    /// <summary>
    /// Practice against a bot, with the whole <b>series</b> in this process (ADR-028, ADR-036, P7). The Arena
    /// scene opened directly in the Editor lands here; everything online goes through
    /// <see cref="OnlineMatchDriver"/>. The session itself lives in <see cref="LocalSessionHost"/>, which
    /// outlives the scene reload between rounds; this is the per-scene adapter over it.
    /// <para>
    /// It owns the clocks from ADR-015 — turn cap, and the idle penalty that shortens the turn after you let
    /// one run out without acting — plus the draft's one deadline. That penalty is deliberately local only
    /// (D5): online, a turn you waste is simply a turn you wasted.
    /// </para>
    /// </summary>
    public sealed class LocalMatchDriver : IMatchDriver
    {
        private const int Local = 0;
        private const int Bot = 1;

        private readonly ContentCatalog _catalog;
        private readonly MatchSettings _settings;
        private readonly Func<bool> _isPlaying;
        private readonly List<MatchEvent> _filtered = new List<MatchEvent>();
        private readonly LocalSessionHost _host;
        private readonly Session _session;
        private readonly RandomBot _bot;

        private MatchState _state;
        private PlayerView _view;
        private SessionView _sessionView;

        /// <summary>The round this scene was built for; a <see cref="RoundStartedEvent"/> for another one reloads it.</summary>
        private int _round;

        private float _turnSeconds;
        private float _idleTurnSeconds;
        private float _turnTotal;
        private float _turnRemaining;
        private bool _clockRunning;
        private bool _penaltyPending;
        private bool _penalised;
        private float _botTimer;

        public int LocalPlayer { get { return Local; } }
        public MatchState Rules { get { return _state; } }
        public PlayerView View { get { return _view; } }
        public SessionView Session { get { return _sessionView; } }
        public bool Ready { get; private set; }
        public string OpponentName { get { return "Random Bot"; } }

        /// <summary>Practice has no seat names: "You" (spec H §7.2).</summary>
        public string MyName { get { return "You"; } }

        /// <summary>Nothing to say: there is no opponent to lose.</summary>
        public string OpponentStatus { get { return null; } }

        public float TurnSecondsRemaining { get { return _turnRemaining > 0f ? _turnRemaining : 0f; } }
        public float TurnSecondsTotal { get { return _turnTotal; } }

        /// <summary>You cannot concede to a bot in practice; restarting the scene is the same thing and less ceremony.</summary>
        public bool CanResign { get { return false; } }

        public event Action<IReadOnlyList<MatchEvent>> EventsArrived;
        public event Action Resynced;
        public event Action StatusChanged;
        public event Action NextRound;

        /// <param name="isPlaying">
        /// Whether the board is mid-animation. The local clock waits for it and the bot waits for it, because
        /// here the truth is in the same process as the pictures and there is nobody to be unfair to.
        /// </param>
        public LocalMatchDriver(ContentCatalog catalog, MatchSettings settings, Func<bool> isPlaying)
        {
            _catalog = catalog;
            _settings = settings;
            _isPlaying = isPlaying;

            _host = LocalSessionHost.For(catalog, settings);
            _session = _host.Session;
            _bot = _host.Bot;
            _round = _session.Round;

            _turnSeconds = settings.ResolveTurnSeconds(catalog);
            _idleTurnSeconds = settings.IdleTurnSeconds;
            _turnTotal = _turnSeconds;
            _turnRemaining = _turnSeconds;

            ReadState();
            Ready = true;
        }

        /// <summary>
        /// Starts round 1 and hands back its events, or — on a scene that came up in the middle of a series —
        /// snaps to whatever the session already is. Call once, after the presenter has subscribed.
        /// </summary>
        public void Begin()
        {
            if (!_host.Started)
            {
                _host.Started = true;
                Raise(_session.Start());
                return;
            }

            ReadState();
            if (_sessionView.Phase == SessionPhase.Draft) ArmDraftClock();
            else ArmTurnClock();
            Action resynced = Resynced;
            if (resynced != null) resynced();
        }

        public bool Submit(Command command)
        {
            if (!Ready) return false;
            CommandResult check = _session.Validate(command);
            if (!check.Ok)
            {
                Debug.Log("[LocalMatchDriver] " + command + " refused: " + check);
                return false;
            }
            Raise(_session.Apply(command));
            return true;
        }

        public bool SubmitDraftPick(int offerIndex)
        {
            return Submit(new DraftPickCommand(Local, offerIndex));
        }

        public void Resign()
        {
            if (!Ready || _session.IsOver) return;
            Submit(new ResignCommand(Local));
        }

        public void Tick(float deltaTime)
        {
            if (!Ready || _session.IsOver) return;
            if (_sessionView != null && _sessionView.Phase == SessionPhase.Draft)
            {
                TickDraft(deltaTime);
                return;
            }
            TickRound(deltaTime);
        }

        private void TickRound(float deltaTime)
        {
            if (_state == null) return;
            bool playing = _isPlaying != null && _isPlaying();

            if (_clockRunning)
            {
                _turnRemaining -= deltaTime;
                if (_turnRemaining <= 0f)
                {
                    if (playing)
                    {
                        _turnRemaining = 0f;          // let the animation land; the turn passes right after
                    }
                    else
                    {
                        int active = _state.ActivePlayer;
                        _clockRunning = false;
                        Debug.Log("[LocalMatchDriver] turn of player " + active + " ended by the clock"
                            + (_state.ActedThisTurn ? "." : " before any action."));
                        Submit(new EndTurnCommand(active, EndTurnReason.Timeout));
                        return;
                    }
                }
            }

            // Acting during a penalty turn buys the full turn back (the Hearthstone rule).
            if (_penalised && _state.ActedThisTurn)
            {
                _penalised = false;
                _turnTotal = _turnSeconds;
                _turnRemaining = _turnSeconds;
                RaiseStatus();
            }

            if (_state.ActivePlayer != Bot || playing) return;

            _botTimer -= deltaTime;
            if (_botTimer > 0f) return;
            _botTimer = _settings.OpponentThinkSeconds;
            Command choice = _bot.Choose(_state, Bot);
            if (choice != null) Submit(choice);
        }

        /// <summary>
        /// Between rounds: one deadline for both seats, and the bot keeping the first offer after the same
        /// pause it takes over a move. The timeout is a command, exactly as the server submits it — Core
        /// never sees a clock (golden rule 4).
        /// </summary>
        private void TickDraft(float deltaTime)
        {
            if (_clockRunning)
            {
                _turnRemaining -= deltaTime;
                if (_turnRemaining <= 0f)
                {
                    _turnRemaining = 0f;
                    _clockRunning = false;
                    if (!_session.HasPicked(Local))
                    {
                        Debug.Log("[LocalMatchDriver] the draft timer ran out; keeping the first card.");
                        Submit(new DraftPickCommand(Local, 0, DraftPickReason.Timeout));
                        return;
                    }
                }
            }

            if (_session.HasPicked(Bot)) return;
            _botTimer -= deltaTime;
            if (_botTimer > 0f) return;
            _botTimer = _settings.OpponentThinkSeconds;
            Command pick = _bot.ChooseDraft(_session, Bot);
            if (pick != null) Submit(pick);
        }

        public void Dispose()
        {
            EventsArrived = null;
            Resynced = null;
            StatusChanged = null;
            NextRound = null;
            Ready = false;
        }

        /// <summary>
        /// Filters the events for the local player before anything sees them, exactly as the server would.
        /// The practice game therefore shows no more than the online one, and a bug where it did would show
        /// up here rather than on 10 October. A batch that starts a later round is not animated at all: the
        /// scene reloads for it and the fresh one adopts the host (ADR-036).
        /// </summary>
        private void Raise(IReadOnlyList<MatchEvent> events)
        {
            _filtered.Clear();
            SessionEventFilter.ForPlayer(events, Local, _session, _filtered);
            ReadClock(_filtered);
            ReadState();

            if (StartsALaterRound(_filtered))
            {
                Action next = NextRound;
                if (next != null) next();
                return;
            }

            Action<IReadOnlyList<MatchEvent>> handler = EventsArrived;
            if (handler != null) handler(_filtered);
        }

        private bool StartsALaterRound(List<MatchEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                var started = events[i] as RoundStartedEvent;
                if (started == null) continue;
                if (_round == 0) { _round = started.Round; continue; }      // round 1 of a fresh session
                if (started.Round != _round) return true;
            }
            return false;
        }

        private void ReadState()
        {
            _state = _session.Match;
            _view = _state != null ? _state.ViewFor(Local) : null;
            _sessionView = SessionView.For(_session, Local);
        }

        /// <summary>The clock is driven by the events, so it can never disagree with whose turn it is.</summary>
        private void ReadClock(List<MatchEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
                if (events[i] is DraftStartedEvent)
                {
                    ArmDraftClock();
                    continue;
                }

                var ended = events[i] as TurnEndedEvent;
                if (ended != null)
                {
                    _clockRunning = false;
                    if (ended.Player == LocalPlayer && !ended.Acted && ended.Reason == EndTurnReason.Timeout)
                        _penaltyPending = _idleTurnSeconds > 0f;
                    continue;
                }

                var started = events[i] as TurnStartedEvent;
                if (started != null)
                {
                    if (started.Player == Local)
                    {
                        _penalised = _penaltyPending;
                        _penaltyPending = false;
                        _turnTotal = _penalised ? _idleTurnSeconds : _turnSeconds;
                        if (_penalised) Debug.Log("[LocalMatchDriver] idle penalty: this turn has " + _idleTurnSeconds + " s until you act.");
                    }
                    else
                    {
                        _penalised = false;
                        _turnTotal = _turnSeconds;
                        _botTimer = _settings.OpponentThinkSeconds;
                    }
                    _turnRemaining = _turnTotal;
                    _clockRunning = true;
                    continue;
                }

                if (events[i] is SessionEndedEvent || events[i] is MatchEndedEvent) _clockRunning = false;
            }
        }

        private void ArmDraftClock()
        {
            _turnTotal = _catalog.Rules.Draft.TimeoutMs / 1000f;
            _turnRemaining = _turnTotal;
            _clockRunning = true;
            _botTimer = _settings.OpponentThinkSeconds;
            _penalised = false;
            _penaltyPending = false;
        }

        private void ArmTurnClock()
        {
            _turnTotal = _turnSeconds;
            _turnRemaining = _turnSeconds;
            _clockRunning = _state != null && !_state.IsOver;
            _botTimer = _settings.OpponentThinkSeconds;
        }

        private void RaiseStatus()
        {
            Action handler = StatusChanged;
            if (handler != null) handler();
        }
    }
}
