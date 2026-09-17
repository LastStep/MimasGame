using System;
using System.Collections.Generic;
using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Match;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Practice against a bot, with the whole match in this process (ADR-028). The Arena scene opened
    /// directly in the Editor lands here; everything online goes through <see cref="OnlineMatchDriver"/>.
    /// <para>
    /// It owns the truth, the bot, and the local clock from ADR-015 — turn cap, and the idle penalty that
    /// shortens the turn after you let one run out without acting. That penalty is deliberately local only
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

        private MatchState _state;
        private PlayerView _view;
        private RandomBot _bot;

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
        public bool Ready { get; private set; }
        public string OpponentName { get { return "Random Bot"; } }

        /// <summary>Nothing to say: there is no opponent to lose.</summary>
        public string OpponentStatus { get { return null; } }

        public float TurnSecondsRemaining { get { return _turnRemaining > 0f ? _turnRemaining : 0f; } }
        public float TurnSecondsTotal { get { return _turnTotal; } }

        /// <summary>You cannot concede to a bot in practice; restarting the scene is the same thing and less ceremony.</summary>
        public bool CanResign { get { return false; } }

        public event Action<IReadOnlyList<MatchEvent>> EventsArrived;
        public event Action Resynced;
        public event Action StatusChanged;

        /// <param name="isPlaying">
        /// Whether the board is mid-animation. The local clock waits for it and the bot waits for it, because
        /// here the truth is in the same process as the pictures and there is nobody to be unfair to.
        /// </param>
        public LocalMatchDriver(ContentCatalog catalog, MatchSettings settings, string mapId, Func<bool> isPlaying)
        {
            _catalog = catalog;
            _settings = settings;
            _isPlaying = isPlaying;

            var setup = new MatchSetup(mapId, settings.PlayerLoadout.ToLoadout(), settings.OpponentLoadout.ToLoadout(), settings.FirstPlayer);
            foreach (string id in settings.PlayerModifierIds) if (!string.IsNullOrEmpty(id)) setup.WithModifier(Local, id);
            foreach (string id in settings.OpponentModifierIds) if (!string.IsNullOrEmpty(id)) setup.WithModifier(Bot, id);

            _state = new MatchState(catalog, setup, settings.Seed);
            _bot = new RandomBot(settings.Seed ^ 0x9E3779B9u);

            _turnSeconds = settings.ResolveTurnSeconds(catalog);
            _idleTurnSeconds = settings.IdleTurnSeconds;
            _turnTotal = _turnSeconds;
            _turnRemaining = _turnSeconds;

            _view = _state.ViewFor(Local);
            Ready = true;
        }

        /// <summary>Starts the first turn and hands back its events. Call once, after the presenter has subscribed.</summary>
        public void Begin()
        {
            Raise(_state.Start());
        }

        public bool Submit(Command command)
        {
            if (!Ready) return false;
            CommandResult check = _state.Validate(command);
            if (!check.Ok)
            {
                Debug.Log("[LocalMatchDriver] " + command + " refused: " + check);
                return false;
            }
            Raise(_state.Apply(command));
            return true;
        }

        public void Resign()
        {
            if (!Ready || _state.IsOver) return;
            Raise(_state.Apply(new ResignCommand(Local)));
        }

        public void Tick(float deltaTime)
        {
            if (!Ready || _state.IsOver) return;

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

        public void Dispose()
        {
            EventsArrived = null;
            Resynced = null;
            StatusChanged = null;
            Ready = false;
        }

        /// <summary>
        /// Filters the events for the local player before anything sees them, exactly as the server would.
        /// The practice game therefore shows no more than the online one, and a bug where it did would show
        /// up here rather than on 10 October.
        /// </summary>
        private void Raise(IReadOnlyList<MatchEvent> events)
        {
            _filtered.Clear();
            EventFilter.ForPlayer(events, Local, _state, _filtered);
            ReadClock(_filtered);
            _view = _state.ViewFor(Local);

            Action<IReadOnlyList<MatchEvent>> handler = EventsArrived;
            if (handler != null) handler(_filtered);
        }

        /// <summary>The clock is driven by the events, so it can never disagree with whose turn it is.</summary>
        private void ReadClock(List<MatchEvent> events)
        {
            for (int i = 0; i < events.Count; i++)
            {
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

                if (events[i] is MatchEndedEvent) _clockRunning = false;
            }
        }

        private void RaiseStatus()
        {
            Action handler = StatusChanged;
            if (handler != null) handler();
        }
    }
}
