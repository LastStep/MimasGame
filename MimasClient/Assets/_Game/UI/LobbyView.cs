using System;
using System.Collections.Generic;
using Mimas.Client.Content;
using Mimas.Client.Net;
using Mimas.Client.Presentation;
using Mimas.Core.Content;
using Mimas.Core.Data;
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
    /// <para>
    /// The look is the ink language (docs/ui/lobby.md, T-0014): one column for the lobby; the room as two seats
    /// facing — you always on the left whichever seat you hold, the other seat only a name and whether it is ready —
    /// with your gear as one tile per preset and the three lineages as rows. The state machine is unchanged.
    /// </para>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class LobbyView : MonoBehaviour
    {
        [Tooltip("The kits the room's gear tiles offer, one tile each. Assets/_Game/Settings/LoadoutPresets.asset.")]
        [SerializeField] private LoadoutPresets _presets;

        [Tooltip("Where the lineage rows' names, descriptions, hues and starting Blessings come from. The Content object in this scene.")]
        [SerializeField] private ContentBootstrap _content;

        [Tooltip("Scene loaded when the match starts.")]
        [SerializeField] private string _arenaScene = "Arena";

        private UIDocument _document;
        private NetClient _net;

        private VisualElement _wash;
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
        private Button _copyCode;
        private Label _youName;
        private VisualElement _them;
        private Label _themName;
        private VisualElement _themDot;
        private Label _themState;
        private VisualElement _divider;
        private VisualElement _gearTiles;
        private readonly List<Button> _presetTiles = new List<Button>();
        private readonly List<Action> _presetHandlers = new List<Action>();
        private VisualElement _godRows;
        private readonly List<Button> _lineageButtons = new List<Button>();
        private readonly List<Action> _lineageHandlers = new List<Action>();
        private readonly List<LineageDef> _lineages = new List<LineageDef>();
        private Button _ready;
        private Button _leaveRoom;
        private Label _roomStatus;

        private const int LineageSlots = 3;

        /// <summary><c>--mimas-ink</c>, which the lobby's wash is mixed onto (USS variables cannot be read from C#).</summary>
        private static readonly Color Ink = new Color32(11, 11, 14, 255);

        private string _code;
        private string _lineage;
        private int _mySeat;
        private bool _inRoom;
        private RoomChoice _choice = new RoomChoice(0);
        private bool _busy;
        private bool _loading;
        private bool _bound;
        private bool _subscribed;

        // ---- lifecycle -------------------------------------------------------------------------------

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            TrySubscribe();
        }

        private void OnDisable()
        {
            if (_net != null && _subscribed)
            {
                _net.MessageReceived -= HandleMessage;
                _net.Connected -= HandleConnected;
                _net.Disconnected -= HandleDisconnected;
                _net.MatchLost -= HandleMatchLost;
            }
            _subscribed = false;
            Unbind();
        }

        /// <summary>
        /// Finds the connection and listens to it. Called from both OnEnable and Start because Unity runs
        /// Awake and OnEnable per object in scene order: this view's OnEnable can run before the Net object's
        /// Awake has set the singleton, and did.
        /// </summary>
        private void TrySubscribe()
        {
            if (_subscribed) return;
            _net = NetClient.Instance;
            if (_net == null) return;

            _net.MessageReceived += HandleMessage;
            _net.Connected += HandleConnected;
            _net.Disconnected += HandleDisconnected;
            _net.MatchLost += HandleMatchLost;
            _subscribed = true;
        }

        private void Start()
        {
            TrySubscribe();
            if (_net == null)
            {
                Debug.LogError("[LobbyView] No NetClient in the scene; nothing can be played.", this);
                enabled = false;
                return;
            }

            // TryBind's ShowLastResult consumes it, and the room's status line wants the same words.
            MatchResult result = _net.LastResult;

            if (!TryBind()) return;

            // Coming back from a result: the room outlives the match (ADR-032), so open on the room panel
            // with the result on its status line. The front screen would be wrong — every one of its
            // buttons is answered with in_room while we are still seated.
            if (_net.CurrentMatchId == 0)
            {
                JObject room = _net.ConsumePendingRoom();
                if (room != null)
                {
                    _choice.UnReady();
                    ApplyReadyState();
                    ShowRoom(room);
                    SetRoomStatus(ResultLine(result, OpponentPresent(room)));
                    return;
                }
            }

            // A page reload mid-match lands here first: say so, and go straight back into the match when
            // the server confirms the seat is still ours.
            if (_net.CurrentMatchId != 0)
            {
                SetStatus("Reconnecting…");
                _busy = true;
                _authenticateWhenConnected = true;
                _net.Connect();
                if (_net.State == NetState.Connected) HandleConnected();
            }
        }

        private bool TryBind()
        {
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _wash = root.Q<VisualElement>("lb.wash");
            _lobbyPanel = root.Q<VisualElement>("lobby-panel");
            _roomPanel = root.Q<VisualElement>("room-panel");
            _name = root.Q<TextField>("lb.name");
            _joinCode = root.Q<TextField>("lb.join.code");
            _playBot = root.Q<Button>("lb.play-bot");
            _createRoom = root.Q<Button>("lb.create");
            _joinRoom = root.Q<Button>("lb.join");
            _status = root.Q<Label>("lb.status");
            _server = root.Q<Label>("lb.server");
            _lastResult = root.Q<Label>("lb.last");

            _roomCode = root.Q<Label>("room.code");
            _copyLink = root.Q<Button>("room.copy-link");
            _copyCode = root.Q<Button>("room.copy-code");
            _youName = root.Q<Label>("room.you.name");
            _them = root.Q<VisualElement>("room.them");
            _themName = root.Q<Label>("room.them.name");
            _themDot = root.Q<VisualElement>("room.them.state.dot");
            _themState = root.Q<Label>("room.them.state.text");
            _divider = root.Q<VisualElement>("room.divider");
            _gearTiles = root.Q<VisualElement>("room.gear.tiles");
            _godRows = root.Q<VisualElement>("room.god.rows");
            _ready = root.Q<Button>("room.ready");
            _leaveRoom = root.Q<Button>("room.leave");
            _roomStatus = root.Q<Label>("room.status");

            if (_wash == null || _lobbyPanel == null || _roomPanel == null || _name == null || _joinCode == null || _playBot == null
                || _createRoom == null || _joinRoom == null || _status == null || _server == null || _lastResult == null
                || _roomCode == null || _copyLink == null || _copyCode == null || _youName == null || _them == null
                || _themName == null || _themDot == null || _themState == null || _divider == null || _gearTiles == null
                || _godRows == null || _ready == null || _leaveRoom == null || _roomStatus == null)
            {
                Debug.LogError("[LobbyView] Lobby.uxml is missing one of the named elements.", this);
                enabled = false;
                return false;
            }

            _name.value = string.IsNullOrEmpty(_net != null ? _net.PlayerName : null)
                ? "Guest-" + UnityEngine.Random.Range(1000, 10000)
                : _net.PlayerName;

            // The lobby is the first screen, so its fonts are repaired here, before any of its text is drawn.
            FontSpacing.RepairLoaded();

            BuildPresetTiles();
            BindLineages();
            _divider.style.backgroundImage = new StyleBackground(Ramps.BothEndsVertical(0.2f));

            _server.text = _net != null ? _net.ResolvedUrl : "";

            _playBot.clicked += HandlePlayBot;
            _createRoom.clicked += HandleCreateRoom;
            _joinRoom.clicked += HandleJoinRoom;
            _copyLink.clicked += HandleCopyLink;
            _copyCode.clicked += HandleCopyCode;
            _ready.clicked += HandleReady;
            _leaveRoom.clicked += HandleLeaveRoom;

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
            _copyCode.clicked -= HandleCopyCode;
            _ready.clicked -= HandleReady;
            _leaveRoom.clicked -= HandleLeaveRoom;
            for (int i = 0; i < _presetTiles.Count; i++) _presetTiles[i].clicked -= _presetHandlers[i];
            for (int i = 0; i < _lineageButtons.Count; i++) _lineageButtons[i].clicked -= _lineageHandlers[i];
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
            _authenticateWhenConnected = true;
            _net.Connect();
            if (_net.State == NetState.Connected) HandleConnected();
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

            LoadoutPresets.Entry entry = _presets != null ? _presets.Get(_choice.PresetIndex) : null;
            if (entry == null)
            {
                SetRoomStatus("No loadout presets are configured.");
                return;
            }

            if (string.IsNullOrEmpty(_lineage))
            {
                // Cannot happen with the default, and kept for honesty: the server refuses either way.
                SetRoomStatus("Pick a lineage.");
                return;
            }

            bool ready = _choice.ToggleReady();
            _net.Send(Messages.RoomLoadout, BuildLoadout(ready));
            ApplyReadyState();
        }

        /// <summary>
        /// A gear tile: selects it, and — as the dropdown's change did — un-readies you, so the match cannot start on a
        /// stale choice. The tiles are disabled while you are ready, so in practice the un-ready never has work to do.
        /// </summary>
        private void HandlePresetClicked(int index)
        {
            if (!_choice.SelectPreset(index)) return;
            RefreshPresetTiles();
            UnReady();
        }

        /// <summary>Ready's label and what it locks: the gear tiles and the lineage rows are disabled while you are ready.</summary>
        private void ApplyReadyState()
        {
            _ready.text = _choice.Ready ? "NOT READY" : "READY";
            for (int i = 0; i < _presetTiles.Count; i++) _presetTiles[i].SetEnabled(!_choice.Ready);
            SetLineageRowEnabled(!_choice.Ready);
        }

        // ---- gear: one tile per preset (docs/ui/lobby.md §3 room.preset[i]; spec H §8) -------------------

        /// <summary>
        /// One tile per <see cref="LoadoutPresets"/> entry, however many there are, sharing the row: the weapon's and
        /// the boots' slot glyphs over the preset's name. The first is selected on every scene load, as the dropdown's
        /// was.
        /// </summary>
        private void BuildPresetTiles()
        {
            _gearTiles.Clear();
            _presetTiles.Clear();
            _presetHandlers.Clear();
            int count = _presets != null ? _presets.Count : 0;
            _choice = new RoomChoice(count);

            for (int i = 0; i < count; i++)
            {
                LoadoutPresets.Entry entry = _presets.Get(i);
                var tile = new Button { name = "room.preset[" + i + "]", text = string.Empty };
                tile.AddToClassList("room-preset");
                if (i == 0) tile.AddToClassList("room-preset--first");

                var glyphs = new VisualElement { pickingMode = PickingMode.Ignore };
                glyphs.AddToClassList("room-preset-glyphs");
                var weapon = new Glyph(HoverContents.SlotShape("weapon"));
                weapon.AddToClassList("room-preset-glyph");
                var boots = new Glyph(HoverContents.SlotShape("boots"));
                boots.AddToClassList("room-preset-glyph");
                glyphs.Add(weapon);
                glyphs.Add(boots);
                tile.Add(glyphs);

                var label = new Label((entry != null ? entry.Name : string.Empty).ToUpperInvariant()) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("room-preset-name");
                tile.Add(label);

                int index = i;
                Action handler = () => HandlePresetClicked(index);
                tile.clicked += handler;
                _presetTiles.Add(tile);
                _presetHandlers.Add(handler);
                _gearTiles.Add(tile);
            }
            RefreshPresetTiles();
        }

        private void RefreshPresetTiles()
        {
            for (int i = 0; i < _presetTiles.Count; i++)
                _presetTiles[i].EnableInClassList("room-preset--selected", i == _choice.PresetIndex);
        }

        /// <summary>Changing your god does exactly what changing your gear does, and is remembered the same way.</summary>
        private void HandleLineageClicked(int slot)
        {
            if (_lineages.Count <= slot) return;
            string chosen = _lineages[slot].Id;
            if (chosen == _lineage) return;
            _lineage = chosen;
            PlayerPrefs.SetString(NetClient.LineagePref, _lineage);
            PlayerPrefs.Save();
            RefreshLineageRow();
            UnReady();
        }

        private void UnReady()
        {
            if (!_choice.UnReady()) return;
            ApplyReadyState();
            if (_net != null) _net.Send(Messages.RoomLoadout, BuildLoadout(false));
        }

        private JObject BuildLoadout(bool ready)
        {
            LoadoutPresets.Entry entry = _presets != null ? _presets.Get(_choice.PresetIndex) : null;
            var loadout = new JObject();
            if (entry != null)
            {
                loadout["weapon"] = entry.Loadout.Weapon;
                loadout["crown"] = entry.Loadout.Crown;
                loadout["boots"] = entry.Loadout.Boots;
                loadout["armour"] = entry.Loadout.Armour;
            }
            return new JObject { ["loadout"] = loadout, ["lineage"] = _lineage ?? "", ["ready"] = ready };
        }

        // ---- pray to: the lineage rows (design: #lineage; docs/ui/lobby.md §3 room.lineage[i]) ------------

        /// <summary>
        /// One row per lineage — the catalogue's first three, in id order — each a swatch in the lineage's hues with
        /// its emblem, the name, its line and the Blessing it starts you with. Nothing about a lineage is written in
        /// C#: the name, the line, the hues and the Blessing are all data (golden rule 5). The same hues wash the
        /// lobby's ground (<c>lb.wash</c>).
        /// </summary>
        private void BindLineages()
        {
            _lineages.Clear();
            ContentCatalog catalog = _content != null ? _content.EnsureLoaded() : null;
            if (catalog != null)
            {
                var all = new List<LineageDef>(catalog.Lineages.All);
                all.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
                for (int i = 0; i < all.Count && i < LineageSlots; i++) _lineages.Add(all[i]);
            }
            else
            {
                Debug.LogWarning("[LobbyView] No ContentBootstrap; the lineage rows cannot be filled.", this);
            }

            _lineage = PlayerPrefs.GetString(NetClient.LineagePref, "");
            if (_lineages.Count > 0 && !_lineages.Exists(l => l.Id == _lineage)) _lineage = _lineages[0].Id;

            _godRows.Clear();
            _lineageButtons.Clear();
            _lineageHandlers.Clear();
            var washes = new List<Color>();
            for (int i = 0; i < _lineages.Count; i++)
            {
                LineageDef lineage = _lineages[i];
                Color dark, light;
                bool hued = ColorUtility.TryParseHtmlString(lineage.HueDark ?? string.Empty, out dark)
                    & ColorUtility.TryParseHtmlString(lineage.HueLight ?? string.Empty, out light);
                if (hued) washes.Add(dark);

                string id = "room.lineage[" + i + "]";
                var row = new Button { name = id, text = string.Empty };
                row.AddToClassList("room-lineage");
                if (i == 0) row.AddToClassList("room-lineage--first");

                var swatch = new VisualElement { name = id + ".swatch", pickingMode = PickingMode.Ignore };
                swatch.AddToClassList("room-lineage-swatch");
                if (hued) swatch.style.backgroundImage = new StyleBackground(Ramps.Swatch(dark, light));
                var emblem = new Glyph(ExamineModelBuilder.EmblemOf(lineage)) { name = id + ".emblem" };
                emblem.AddToClassList("room-lineage-emblem");
                swatch.Add(emblem);
                row.Add(swatch);

                var text = new VisualElement { pickingMode = PickingMode.Ignore };
                text.AddToClassList("room-lineage-text");
                text.Add(Text(id + ".name", (lineage.Name ?? lineage.Id).ToUpperInvariant(), "room-lineage-name"));
                if (!string.IsNullOrEmpty(lineage.Description))
                    text.Add(Text(id + ".line", lineage.Description, "room-lineage-line"));
                string blessing = BlessingName(catalog, lineage);
                if (blessing != null)
                {
                    var starts = new VisualElement { name = id + ".blessing", pickingMode = PickingMode.Ignore };
                    starts.AddToClassList("room-lineage-blessing");
                    var circle = new Glyph("blessing") { Filled = true };
                    circle.AddToClassList("room-lineage-blessing-glyph");
                    starts.Add(circle);
                    starts.Add(Text(null, ("Starts with " + blessing).ToUpperInvariant(), "room-lineage-blessing-text"));
                    text.Add(starts);
                }
                row.Add(text);

                int slot = i;
                Action handler = () => HandleLineageClicked(slot);
                row.clicked += handler;
                _lineageButtons.Add(row);
                _lineageHandlers.Add(handler);
                _godRows.Add(row);
            }

            _wash.style.backgroundImage = washes.Count > 0 ? new StyleBackground(Ramps.LobbyWash(washes, Ink)) : new StyleBackground(StyleKeyword.None);
            RefreshLineageRow();
        }

        private static Label Text(string name, string text, string className)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            if (name != null) label.name = name;
            label.AddToClassList(className);
            return label;
        }

        private static string BlessingName(ContentCatalog catalog, LineageDef lineage)
        {
            if (catalog == null || lineage.StartingBlessingId == null) return null;
            BoonDef boon;
            return catalog.Boons.TryGet(lineage.StartingBlessingId, out boon) ? boon.Name : lineage.StartingBlessingId;
        }

        private void RefreshLineageRow()
        {
            for (int i = 0; i < _lineageButtons.Count; i++)
                _lineageButtons[i].EnableInClassList("room-lineage--selected", i < _lineages.Count && _lineages[i].Id == _lineage);
        }

        private void SetLineageRowEnabled(bool enabled)
        {
            for (int i = 0; i < _lineageButtons.Count; i++) _lineageButtons[i].SetEnabled(enabled);
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

            // Not systemCopyBuffer: on Web that writes to a buffer of Unity's own and the browser
            // clipboard never sees it, so this said "Link copied." over an empty clipboard in every
            // build so far. WebClipboard goes through the browser. Say so honestly when it refuses —
            // the room code is on screen in 52px type, which is the fallback.
            bool copied = WebClipboard.Copy(link);
            SetRoomStatus(copied ? "Link copied." : "Could not copy — read them the code instead.");
            Debug.Log("[LobbyView] copy link " + link + " -> " + (copied ? "ok" : "refused"));
        }

        /// <summary>
        /// The code by itself, for the half of the time the link is no use: a phone call, a person in the
        /// room, a chat that eats links. Four characters, nothing else on the clipboard (T-0005).
        /// </summary>
        private void HandleCopyCode()
        {
            if (string.IsNullOrEmpty(_code)) return;
            bool copied = WebClipboard.Copy(_code);
            SetRoomStatus(copied ? "Code copied." : "Could not copy — read them the code instead.");
            Debug.Log("[LobbyView] copy code " + _code + " -> " + (copied ? "ok" : "refused"));
        }

        // ---- the server speaks --------------------------------------------------------------------------

        private void HandleMessage(string type, JObject p)
        {
            if (!_bound) return;

            switch (type)
            {
                case Messages.AuthOk:
                    string seated = p.Value<string>("room");
                    if (!string.IsNullOrEmpty(seated))
                    {
                        // We are still in a room that is between matches (ADR-032). The room.state that
                        // follows opens the room panel; anything we had queued would only be told in_room.
                        _pendingType = null;
                        _pendingPayload = null;
                        _busy = false;
                        SetButtonsEnabled(true);
                        SetStatus("Back in room " + seated + "…");
                        break;
                    }

                    // Nothing queued means we authenticated for our own reasons (a reconnection that came
                    // to nothing). Let go of the wait, or every button stays dead.
                    if (string.IsNullOrEmpty(_pendingType)) { _busy = false; SetButtonsEnabled(true); }
                    else SendPending();
                    break;

                case Messages.RoomState:
                    ShowRoom(p);
                    break;

                case Messages.RoomLeft:
                    _inRoom = false;
                    _choice.UnReady();
                    ApplyReadyState();
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

        /// <summary>
        /// The socket is up. Connecting is asynchronous, so everything a button press wants to do waits
        /// here: say who we are, and only then send what was asked for (on auth.ok).
        /// </summary>
        private void HandleConnected()
        {
            if (!_authenticateWhenConnected) return;
            _authenticateWhenConnected = false;
            _net.Authenticate(_name != null ? _name.value : null);
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
            _choice.UnReady();
            ApplyReadyState();
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
                case "bad_loadout": return "That loadout or lineage is not valid.";
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
            bool waitingForSomeone = DrawSeats(seats);
            SetRoomStatus(waitingForSomeone ? "Send the code to a friend." : _choice.Ready ? "Waiting for your opponent…" : "Pick your gear and your god.");
        }

        /// <summary>
        /// You on the left, whichever seat you hold; them on the right with only a name and whether they are ready
        /// (docs/ui/lobby.md §3). A seat the server sent nothing for reads as empty rather than throwing. Returns
        /// whether the other seat is waiting for a player.
        /// </summary>
        private bool DrawSeats(JArray seats)
        {
            RoomSeat you = ReadSeat(seats, RoomLayout.LeftSeat(_mySeat));
            string myName = you != null && !string.IsNullOrEmpty(you.Name) ? you.Name : _net != null ? _net.PlayerName : null;
            _youName.text = (string.IsNullOrEmpty(myName) ? "You" : myName).ToUpperInvariant();

            OtherSeatText other = RoomLayout.Other(ReadSeat(seats, RoomLayout.RightSeat(_mySeat)));
            _themName.text = other.Name;
            _themState.text = other.State;
            _themDot.EnableInClassList("room-them-dot--ready", other.Ready);
            _them.EnableInClassList("room-seat--waiting", other.Waiting);
            return other.Waiting;
        }

        private static RoomSeat ReadSeat(JArray seats, int index)
        {
            if (seats == null || index < 0 || index >= seats.Count) return null;
            var seat = seats[index] as JObject;
            if (seat == null) return null;
            return new RoomSeat
            {
                Name = seat.Value<string>("name"),
                Present = seat.Value<bool>("present"),
                Ready = seat.Value<bool>("ready"),
                Bot = seat.Value<bool>("bot"),
            };
        }

        /// <summary>Is the other seat still filled? A room whose opponent left is still a room.</summary>
        private bool OpponentPresent(JObject room)
        {
            RoomSeat other = ReadSeat(room["seats"] as JArray, RoomLayout.RightSeat(room.Value<int>("youAre")));
            return other != null && other.Present;
        }

        /// <summary>
        /// The result, on the room's status line, in the same words the banner used — and then the only
        /// question that matters, which is whether there is anybody there to play again.
        /// </summary>
        private static string ResultLine(MatchResult result, bool opponentPresent)
        {
            string tail = opponentPresent ? " — Ready for another?" : " — the seat is free; share the code";
            if (result == null) return opponentPresent ? "Ready for another?" : "The seat is free; share the code.";

            string line = result.Won ? "Victory" : "Defeat";
            if (!string.IsNullOrEmpty(result.Reason)) line += " · " + result.Reason;
            return line + tail;
        }

        /// <summary>"VICTORY VS GUEST-2869 · SERIES 2 – 1" in `you` for a win, `them` for a loss; hidden when there is none.</summary>
        private void ShowLastResult()
        {
            MatchResult result = _net != null ? _net.LastResult : null;
            bool has = result != null;
            if (has)
            {
                _lastResult.text = RoomLayout.LastResultLine(result.Won, result.OpponentName, result.HasScore,
                    result.ScoreMine, result.ScoreTheirs, result.Reason);
                _lastResult.EnableInClassList("lb-last--lost", !result.Won);
                _net.LastResult = null;
            }
            _lastResult.EnableInClassList("lb-last--visible", has);
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
