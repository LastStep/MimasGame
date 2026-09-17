using System;
using System.Collections.Generic;
using Mimas.Client.Net;
using Mimas.Client.Presentation;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Mimas.Client.UI
{
    /// <summary>
    /// The first screen: a name, and three ways into a match — the bot, a room you open, or a room someone
    /// sent you the code for (OPT-0001, spec amendment A1). Gear is chosen in the room, once you can see who
    /// you are playing, and the match starts when both seats are ready.
    /// <para>
    /// A queue would have been less code. With four friends in two arranged pairs it would also have paired
    /// them in the order they clicked, which is a coordination problem wearing a matchmaking hat.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyView : MonoBehaviour
    {
        [Tooltip("The kits the room's dropdown offers. Assets/_Game/Settings/LoadoutPresets.asset.")]
        [SerializeField] private LoadoutPresets _presets;

        [Tooltip("Scene loaded when the match starts.")]
        [SerializeField] private string _arenaScene = "Arena";

        private UIDocument _document;
        private NetClient _net;

        private VisualElement _lobbyPanel;
        private VisualElement _roomPanel;
        private TextField _name;
        private TextField _joinCode;
        private Button _playBot;
        private Button _createRoom;
        private Button _joinRoom;
        private Label _status;
        private Label _server;
        private Label _lastResult;

        private Label _roomCode;
        private Button _copyLink;
        private Label _seat0;
        private Label _seat1;
        private DropdownField _preset;
        private Button _ready;
        private Button _leaveRoom;
        private Label _roomStatus;

        private string _code;
        private int _mySeat;
        private bool _inRoom;
        private bool _isReady;
        private bool _busy;
        private bool _loading;
        private bool _bound;

        // ---- lifecycle -------------------------------------------------------------------------------

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            _net = NetClient.Instance;
            if (_net == null)
            {
                Debug.LogError("[LobbyView] No NetClient in the scene; nothing can be played.", this);
                return;
            }

            _net.MessageReceived += HandleMessage;
            _net.Disconnected += HandleDisconnected;
            _net.MatchLost += HandleMatchLost;
        }

        private void OnDisable()
        {
            if (_net != null)
            {
                _net.MessageReceived -= HandleMessage;
                _net.Disconnected -= HandleDisconnected;
                _net.MatchLost -= HandleMatchLost;
            }
            Unbind();
        }

        private void Start()
        {
            if (!TryBind()) return;

            // A page reload mid-match lands here first: say so, and go straight back into the match when
            // the server confirms the seat is still ours.
            if (_net != null && _net.CurrentMatchId != 0)
            {
                SetStatus("Reconnecting…");
                _busy = true;
                _net.Connect();
                _net.Authenticate(_name.value);
            }
        }

        private bool TryBind()
        {
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _lobbyPanel = root.Q<VisualElement>("lobby-panel");
            _roomPanel = root.Q<VisualElement>("room-panel");
            _name = root.Q<TextField>("name");
            _joinCode = root.Q<TextField>("join-code");
            _playBot = root.Q<Button>("play-bot");
            _createRoom = root.Q<Button>("create-room");
            _joinRoom = root.Q<Button>("join-room");
            _status = root.Q<Label>("status");
            _server = root.Q<Label>("server");
            _lastResult = root.Q<Label>("last-result");

            _roomCode = root.Q<Label>("room-code");
            _copyLink = root.Q<Button>("copy-link");
            _seat0 = root.Q<Label>("seat-0");
            _seat1 = root.Q<Label>("seat-1");
            _preset = root.Q<DropdownField>("preset");
            _ready = root.Q<Button>("ready");
            _leaveRoom = root.Q<Button>("leave-room");
            _roomStatus = root.Q<Label>("room-status");

            if (_lobbyPanel == null || _roomPanel == null || _name == null || _joinCode == null || _playBot == null
                || _createRoom == null || _joinRoom == null || _status == null || _server == null || _lastResult == null
                || _roomCode == null || _copyLink == null || _seat0 == null || _seat1 == null || _preset == null
                || _ready == null || _leaveRoom == null || _roomStatus == null)
            {
                Debug.LogError("[LobbyView] Lobby.uxml is missing one of the named elements.", this);
                enabled = false;
                return false;
            }

            _name.value = string.IsNullOrEmpty(_net != null ? _net.PlayerName : null)
                ? "Guest-" + UnityEngine.Random.Range(1000, 10000)
                : _net.PlayerName;

            _preset.choices = _presets != null ? _presets.Names() : new List<string>();
            if (_preset.choices.Count > 0) _preset.index = 0;

            _server.text = _net != null ? _net.ResolvedUrl : "";

            _playBot.clicked += HandlePlayBot;
            _createRoom.clicked += HandleCreateRoom;
            _joinRoom.clicked += HandleJoinRoom;
            _copyLink.clicked += HandleCopyLink;
            _ready.clicked += HandleReady;
            _leaveRoom.clicked += HandleLeaveRoom;
            _preset.RegisterValueChangedCallback(HandlePresetChanged);

            // Room codes are upper case everywhere; typing them in lower case should still work.
            _joinCode.RegisterValueChangedCallback(e =>
            {
                string upper = (e.newValue ?? "").ToUpperInvariant();
                if (upper != e.newValue) _joinCode.SetValueWithoutNotify(upper);
            });

            _bound = true;
            ShowLastResult();
            ShowLobby();

            // A ?room=CODE link: the whole point of a code is that it can be pasted.
            string invited = _net != null ? _net.ConsumePendingRoomCode() : null;
            if (!string.IsNullOrEmpty(invited))
            {
                _joinCode.value = invited;
                Debug.Log("[LobbyView] invited to room " + invited);
                if (_net.CurrentMatchId == 0) HandleJoinRoom();
            }

            return true;
        }

        private void Unbind()
        {
            if (!_bound) return;
            _playBot.clicked -= HandlePlayBot;
            _createRoom.clicked -= HandleCreateRoom;
            _joinRoom.clicked -= HandleJoinRoom;
            _copyLink.clicked -= HandleCopyLink;
            _ready.clicked -= HandleReady;
            _leaveRoom.clicked -= HandleLeaveRoom;
            _preset.UnregisterValueChangedCallback(HandlePresetChanged);
            _bound = false;
        }

        // ---- the three ways in -------------------------------------------------------------------------

        private void HandlePlayBot()
        {
            BeginRequest(Messages.BotPlay, null);
        }

        private void HandleCreateRoom()
        {
            BeginRequest(Messages.RoomCreate, null);
        }

        private void HandleJoinRoom()
        {
            string code = (_joinCode.value ?? "").Trim().ToUpperInvariant();
            if (code.Length == 0)
            {
                SetStatus("Type the code your friend sent you.");
                return;
            }
            BeginRequest(Messages.RoomJoin, new JObject { ["code"] = code });
        }

        /// <summary>
        /// Saves the name, makes sure there is a connection and an identity, and sends the request once
        /// there is. Everything here needs the same three steps, so they live in one place.
        /// </summary>
        private void BeginRequest(string type, JObject payload)
        {
            if (_busy || _net == null) return;

            string chosen = (_name.value ?? "").Trim();
            if (chosen.Length > 0)
            {
                _net.PlayerName = chosen;
                PlayerPrefs.SetString(NetClient.NamePref, chosen);
            }

            _busy = true;
            SetButtonsEnabled(false);
            _pendingType = type;
            _pendingPayload = payload;

            if (_net.State == NetState.Authenticated)
            {
                SendPending();
                return;
            }

            SetStatus("Connecting…");
            _net.Connect();
            if (_net.State == NetState.Connected) _net.Authenticate(chosen);
            else _authenticateWhenConnected = true;
        }

        private string _pendingType;
        private JObject _pendingPayload;
        private bool _authenticateWhenConnected;

        private void SendPending()
        {
            if (string.IsNullOrEmpty(_pendingType)) return;
            string type = _pendingType;
            JObject payload = _pendingPayload;
            _pendingType = null;
            _pendingPayload = null;
            SetStatus("…");
            _net.Send(type, payload);
        }

        // ---- the room ---------------------------------------------------------------------------------

        private void HandleReady()
        {
            if (_net == null || !_inRoom) return;

            LoadoutPresets.Entry entry = _presets != null ? _presets.Get(Mathf.Max(0, _preset.index)) : null;
            if (entry == null)
            {
                SetRoomStatus("No loadout presets are configured.");
                return;
            }

            _isReady = !_isReady;
            _net.Send(Messages.RoomLoadout, new JObject
            {
                ["loadout"] = new JObject
                {
                    ["weapon"] = entry.Loadout.Weapon,
                    ["crown"] = entry.Loadout.Crown,
                    ["boots"] = entry.Loadout.Boots,
                    ["armour"] = entry.Loadout.Armour,
                },
                ["ready"] = _isReady,
            });
            _ready.text = _isReady ? "Not ready" : "Ready";
            _preset.SetEnabled(!_isReady);
        }

        /// <summary>Changing your mind about gear un-readies you, so the match cannot start on a stale choice.</summary>
        private void HandlePresetChanged(ChangeEvent<string> e)
        {
            if (!_isReady) return;
            _isReady = false;
            _ready.text = "Ready";
            _net.Send(Messages.RoomLoadout, BuildLoadout(false));
        }

        private JObject BuildLoadout(bool ready)
        {
            LoadoutPresets.Entry entry = _presets != null ? _presets.Get(Mathf.Max(0, _preset.index)) : null;
            var loadout = new JObject();
            if (entry != null)
            {
                loadout["weapon"] = entry.Loadout.Weapon;
                loadout["crown"] = entry.Loadout.Crown;
                loadout["boots"] = entry.Loadout.Boots;
                loadout["armour"] = entry.Loadout.Armour;
            }
            return new JObject { ["loadout"] = loadout, ["ready"] = ready };
        }

        private void HandleLeaveRoom()
        {
            if (_net == null || !_inRoom) return;
            _net.Send(Messages.RoomLeave, new JObject());
        }

        /// <summary>The link is the point of a code: this is what gets pasted into a Discord call.</summary>
        private void HandleCopyLink()
        {
            if (string.IsNullOrEmpty(_code)) return;
            string page = Application.absoluteURL ?? "";
            int q = page.IndexOf('?');
            if (q >= 0) page = page.Substring(0, q);
            string link = string.IsNullOrEmpty(page) ? _code : page + "?room=" + _code;
            GUIUtility.systemCopyBuffer = link;
            SetRoomStatus("Link copied.");
            Debug.Log("[LobbyView] copied " + link);
        }

        // ---- the server speaks --------------------------------------------------------------------------

        private void HandleMessage(string type, JObject p)
        {
            if (!_bound) return;

            switch (type)
            {
                case Messages.AuthOk:
                    if (_authenticateWhenConnected) _authenticateWhenConnected = false;
                    SendPending();
                    break;

                case Messages.RoomState:
                    ShowRoom(p);
                    break;

                case Messages.RoomLeft:
                    _inRoom = false;
                    _isReady = false;
                    _code = null;
                    ShowLobby();
                    break;

                case Messages.MatchStart:
                    if (_loading) return;
                    _loading = true;
                    SetStatus("Match found · loading…");
                    SetRoomStatus("Match found · loading…");
                    SetButtonsEnabled(false);
                    Debug.Log("[LobbyView] loading the Arena for match " + p.Value<int>("matchId"));
                    SceneManager.LoadSceneAsync(_arenaScene);
                    break;

                case Messages.Error:
                    _busy = false;
                    _pendingType = null;
                    _pendingPayload = null;
                    SetButtonsEnabled(true);
                    string message = Explain(p.Value<string>("code"), p.Value<string>("message"));
                    if (_inRoom) SetRoomStatus(message);
                    else SetStatus(message);
                    break;
            }
        }

        private void HandleDisconnected(string reason)
        {
            if (!_bound || _loading) return;
            _busy = false;
            _pendingType = null;
            SetButtonsEnabled(true);
            SetStatus("Server unreachable · retrying");
        }

        private void HandleMatchLost()
        {
            if (!_bound) return;
            _busy = false;
            _inRoom = false;
            _isReady = false;
            SetButtonsEnabled(true);
            ShowLobby();
            SetStatus("Match lost");
        }

        /// <summary>The server's error codes in the words a player would use.</summary>
        private static string Explain(string code, string fallback)
        {
            switch (code)
            {
                case null: return fallback ?? "Something went wrong.";
                case "no_such_room": return "No room with that code.";
                case "room_full": return "That room is full.";
                case "in_room": return "You are already in a room.";
                case "in_match": return "You are already in a match.";
                case "bad_loadout": return "That loadout is not valid.";
                case "bad_token": return "Starting a new session…";
                default: return fallback ?? code;
            }
        }

        // ---- panels ------------------------------------------------------------------------------------

        private void ShowLobby()
        {
            _lobbyPanel.EnableInClassList("panel--visible", true);
            _roomPanel.EnableInClassList("panel--visible", false);
            SetButtonsEnabled(true);
            _busy = false;
        }

        private void ShowRoom(JObject p)
        {
            _busy = false;
            _inRoom = true;
            _code = p.Value<string>("code");
            _mySeat = p.Value<int>("youAre");

            _roomCode.text = _code ?? "";
            _lobbyPanel.EnableInClassList("panel--visible", false);
            _roomPanel.EnableInClassList("panel--visible", true);
            SetStatus("");

            var seats = p["seats"] as JArray;
            if (seats != null && seats.Count == 2)
            {
                DrawSeat(_seat0, seats[0] as JObject, 0);
                DrawSeat(_seat1, seats[1] as JObject, 1);
            }

            bool waitingForSomeone = seats != null && seats.Count == 2 && !(seats[1 - _mySeat] as JObject).Value<bool>("present");
            SetRoomStatus(waitingForSomeone ? "Send the code to a friend." : _isReady ? "Waiting for your opponent…" : "Pick your gear.");
        }

        private void DrawSeat(Label label, JObject seat, int index)
        {
            if (seat == null) return;
            string name = seat.Value<string>("name");
            bool present = seat.Value<bool>("present");
            bool ready = seat.Value<bool>("ready");
            bool bot = seat.Value<bool>("bot");

            string who = string.IsNullOrEmpty(name) ? "Waiting for a player…" : name;
            if (index == _mySeat) who += "  (you)";

            string state = !present ? "" : ready ? "  ·  Ready" : bot ? "" : "  ·  Choosing…";
            label.text = who + state;
            label.EnableInClassList("seat--ready", present && ready);
            label.EnableInClassList("seat--empty", !present);
        }

        private void ShowLastResult()
        {
            MatchResult result = _net != null ? _net.LastResult : null;
            bool has = result != null;
            if (has)
            {
                string line = (result.Won ? "Victory" : "Defeat") + " vs " + (result.OpponentName ?? "opponent");
                if (!string.IsNullOrEmpty(result.Reason)) line += "  ·  " + result.Reason;
                _lastResult.text = line;
                _net.LastResult = null;
            }
            _lastResult.EnableInClassList("last-result--visible", has);
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _playBot.SetEnabled(enabled);
            _createRoom.SetEnabled(enabled);
            _joinRoom.SetEnabled(enabled);
        }

        private void SetStatus(string text)
        {
            if (_status != null) _status.text = text ?? "";
        }

        private void SetRoomStatus(string text)
        {
            if (_roomStatus != null) _roomStatus.text = text ?? "";
        }
    }
}
