using System;
using System.Collections.Generic;
using Mimas.Client.Net;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// A match whose truth is on the server (ADR-026, ADR-027). Every message carries this seat's
    /// <c>PlayerView</c>, and this rebuilds <see cref="MatchState.FromView"/> from it — so the board can
    /// answer every preview question instantly and locally, and can only ever know what the server chose to
    /// tell it. Events are for animation; they never advance anything.
    /// <para>
    /// Rebuilt rather than advanced on purpose: a view is two heroes and a handful of props, and a state
    /// that is only ever replaced cannot drift from the server. That is the whole of ADR-026.
    /// </para>
    /// </summary>
    public sealed class OnlineMatchDriver : IMatchDriver
    {
        private readonly ContentCatalog _catalog;
        private readonly NetClient _net;
        private readonly int _matchId;

        private MatchState _rules;
        private PlayerView _view;
        private int _seq = -1;

        // The clock is anchored on every message and counted down locally between them, so the rope is
        // smooth but never drifts: the server's number always wins.
        private float _anchoredRemaining;
        private float _sinceAnchor;
        private float _turnTotal;

        private string _opponentStatus;
        private float _graceRemaining;
        private bool _matchOver;

        public int LocalPlayer { get; private set; }
        public MatchState Rules { get { return _rules; } }
        public PlayerView View { get { return _view; } }
        public bool Ready { get { return _rules != null; } }
        public string OpponentName { get; private set; }
        public string OpponentStatus { get { return _opponentStatus; } }
        public float TurnSecondsTotal { get { return _turnTotal; } }

        public float TurnSecondsRemaining
        {
            get
            {
                float left = _anchoredRemaining - _sinceAnchor;
                return left > 0f ? left : 0f;
            }
        }

        /// <summary>You may concede any time the match is running, including while the other player is thinking.</summary>
        public bool CanResign { get { return Ready && !_matchOver; } }

        public event Action<IReadOnlyList<MatchEvent>> EventsArrived;
        public event Action Resynced;
        public event Action StatusChanged;

        public OnlineMatchDriver(ContentCatalog catalog, NetClient net, JObject matchStart)
        {
            _catalog = catalog;
            _net = net;
            _matchId = matchStart.Value<int>("matchId");
            LocalPlayer = matchStart.Value<int>("youAre");
            OpponentName = matchStart.Value<string>("opponentName") ?? "Opponent";

            Adopt(matchStart);
            _net.MessageReceived += HandleMessage;
            _net.Disconnected += HandleDisconnected;
            _net.Connected += HandleConnected;
        }

        /// <summary>Hands the presenter the events that came with <c>match.start</c> (normally one turn starting).</summary>
        public void Begin(JObject matchStart)
        {
            var events = matchStart["events"] as JArray;
            if (events == null || events.Count == 0) return;
            RaiseEvents(Wire.ReadEvents(events));
        }

        // ---- commands --------------------------------------------------------------------------------

        public bool Submit(Command command)
        {
            if (!Ready) return false;

            // Asked of the mirror, which is the same rules code the server runs: a click the rules would
            // refuse is refused here, at once, and never becomes a round trip.
            CommandResult check = _rules.Validate(command);
            if (!check.Ok)
            {
                Debug.Log("[OnlineMatchDriver] " + command + " refused locally: " + check);
                return false;
            }

            return _net.Send(Messages.MatchCommand, new JObject
            {
                ["matchId"] = _matchId,
                ["command"] = Wire.Command(command),
            });
        }

        public void Resign()
        {
            if (!CanResign) return;
            _net.Send(Messages.MatchCommand, new JObject
            {
                ["matchId"] = _matchId,
                ["command"] = Wire.Command(new ResignCommand(LocalPlayer)),
            });
        }

        public void Tick(float deltaTime)
        {
            _sinceAnchor += deltaTime;

            if (_graceRemaining <= 0f) return;
            _graceRemaining -= deltaTime;
            if (_graceRemaining < 0f) _graceRemaining = 0f;

            string line = "Opponent disconnected · " + Mathf.CeilToInt(_graceRemaining) + " s";
            if (line == _opponentStatus) return;
            _opponentStatus = line;
            RaiseStatus();
        }

        public void Dispose()
        {
            if (_net != null)
            {
                _net.MessageReceived -= HandleMessage;
                _net.Disconnected -= HandleDisconnected;
                _net.Connected -= HandleConnected;
            }
            EventsArrived = null;
            Resynced = null;
            StatusChanged = null;
        }

        // ---- the server speaks --------------------------------------------------------------------------

        private void HandleMessage(string type, JObject p)
        {
            switch (type)
            {
                case Messages.MatchEvents:
                {
                    if (p.Value<int>("matchId") != _matchId) return;

                    // A batch from before a reconnect is stale: the view that came with the reconnect is
                    // newer than anything that was in flight when the socket died.
                    int seq = p.Value<int>("seq");
                    if (seq <= _seq) return;
                    _seq = seq;

                    Adopt(p);
                    var events = p["events"] as JArray;
                    if (events != null && events.Count > 0) RaiseEvents(Wire.ReadEvents(events));
                    break;
                }

                case Messages.MatchRejected:
                {
                    if (p.Value<int>("matchId") != _matchId) return;
                    // The mirror already refuses anything illegal, so this means the two disagreed: take the
                    // server's view, say so in the log, and let the HUD redraw from it.
                    Debug.LogWarning("[OnlineMatchDriver] the server refused a command the mirror allowed: " + p.Value<string>("reason"));
                    Adopt(p);
                    RaiseStatus();
                    break;
                }

                case Messages.MatchView:
                {
                    if (p.Value<int>("matchId") != _matchId) return;
                    _seq = p.Value<int>("seq");
                    Adopt(p);
                    RaiseResynced();
                    break;
                }

                case Messages.MatchStart:
                {
                    // A second match.start for the same match is a reconnect: the whole board, as it now is.
                    if (p.Value<int>("matchId") != _matchId) return;
                    _seq = p.Value<int>("seq");
                    Adopt(p);
                    SetStatus(null);
                    _graceRemaining = 0f;
                    RaiseResynced();
                    break;
                }

                case Messages.OpponentStatus:
                {
                    if (p.Value<int>("matchId") != _matchId) return;
                    if (p.Value<bool>("connected"))
                    {
                        _graceRemaining = 0f;
                        SetStatus(null);
                    }
                    else
                    {
                        _graceRemaining = p.Value<int>("graceMs") / 1000f;
                        SetStatus("Opponent disconnected · " + Mathf.CeilToInt(_graceRemaining) + " s");
                    }
                    break;
                }
            }
        }

        private void HandleDisconnected(string reason)
        {
            if (_matchOver) return;
            SetStatus("Reconnecting…");
        }

        private void HandleConnected()
        {
            if (_matchOver) return;
            SetStatus("Reconnecting…");
        }

        /// <summary>Replaces the mirror and the clock from whatever the server just sent.</summary>
        private void Adopt(JObject p)
        {
            var view = p["view"] as JObject;
            if (view != null)
            {
                _view = Wire.ReadView(view);
                _rules = MatchState.FromView(_catalog, _view);
                if (_view.IsOver) _matchOver = true;
            }

            var clock = p["clock"] as JObject;
            if (clock == null) return;

            float total = clock.Value<int>("turnMs") / 1000f;
            if (!Mathf.Approximately(total, _turnTotal))
            {
                _turnTotal = total;
                RaiseStatus();
            }
            _anchoredRemaining = clock.Value<int>("remainingMs") / 1000f;
            _sinceAnchor = 0f;
        }

        private void SetStatus(string status)
        {
            if (status == _opponentStatus) return;
            _opponentStatus = status;
            RaiseStatus();
        }

        private void RaiseEvents(List<MatchEvent> events)
        {
            Action<IReadOnlyList<MatchEvent>> handler = EventsArrived;
            if (handler != null) handler(events);
        }

        private void RaiseResynced()
        {
            Action handler = Resynced;
            if (handler != null) handler();
        }

        private void RaiseStatus()
        {
            Action handler = StatusChanged;
            if (handler != null) handler();
        }
    }
}
