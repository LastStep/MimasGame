using System;
using System.Collections.Generic;
using Mimas.Core.Protocol;
using NativeWebSocket;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Mimas.Client.Net
{
    public enum NetState
    {
        Disconnected = 0,
        Connecting = 1,
        Connected = 2,

        /// <summary>The socket is up and the server knows who we are; only now may anything else be sent.</summary>
        Authenticated = 3,
    }

    /// <summary>What a finished match left behind, for the lobby to show when it comes back.</summary>
    public sealed class MatchResult
    {
        public bool Won;
        public string Reason;
        public string OpponentName;
    }

    /// <summary>
    /// The one socket to the server, alive across scene loads (ADR-027, ADR-028). It owns the connection,
    /// the guest identity and the one message every scene needs to agree about — <c>match.start</c> — and
    /// nothing else: it does not know what a match is, and it never touches the rules.
    /// <para>
    /// Identity lives in <c>PlayerPrefs</c> (D4): no database, but a token that survives a page refresh, so
    /// reloading a browser tab mid-match puts you back in your seat rather than making you a stranger.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetClient : MonoBehaviour
    {
        public const string NamePref = "mimas.name";
        public const string TokenPref = "mimas.token";
        public const string MatchPref = "mimas.matchId";

        private const float ReconnectDelaySeconds = 2f;
        private const float RejoinWindowSeconds = 2f;

        [Header("Server")]
        [Tooltip("Where to connect in the Editor. A Web build derives its own from the page, or from a ?ws= query parameter.")]
        [SerializeField] private string _url = "ws://localhost:7777/ws";

        [Tooltip("Log every message type as it arrives. Payloads are never logged; the token never is either way.")]
        [SerializeField] private bool _verbose;

        private WebSocket _socket;
        private NetState _state = NetState.Disconnected;
        private string _resolvedUrl;
        private float _retryIn;
        private float _rejoinWindow;
        private bool _retriedAsGuest;
        private bool _wantsConnection;

        public static NetClient Instance { get; private set; }

        public NetState State => _state;

        /// <summary>The URL actually in use, for the lobby to show.</summary>
        public string ResolvedUrl => _resolvedUrl ?? _url;

        public string PlayerName { get; set; }

        public int PlayerId { get; private set; }

        /// <summary>The last <c>match.start</c> payload, waiting for the Arena scene to pick it up.</summary>
        public JObject PendingMatch { get; private set; }

        /// <summary>The match this client believes it is seated in, or 0. Survives a reload through PlayerPrefs.</summary>
        public int CurrentMatchId { get; private set; }

        /// <summary>The result of the match just finished, for the lobby's result line. Null until there is one.</summary>
        public MatchResult LastResult { get; set; }

        /// <summary>A room code taken from a <c>?room=CODE</c> link, consumed once by the lobby.</summary>
        public string PendingRoomCode { get; private set; }

        public event Action Connected;
        public event Action<string> Disconnected;
        public event Action<string, JObject> MessageReceived;

        /// <summary>The room we thought we were in is gone: the lobby shows "Match lost" and lets go.</summary>
        public event Action MatchLost;

        private string Token
        {
            get { return PlayerPrefs.GetString(TokenPref, ""); }
            set { PlayerPrefs.SetString(TokenPref, value ?? ""); SavePrefs(); }
        }

        // ---- lifecycle -------------------------------------------------------------------------------

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                // The Lobby scene carries one; coming back to it must not make a second.
                Destroy(gameObject);
                return;
            }

            Instance = this;
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);

            PlayerName = PlayerPrefs.GetString(NamePref, "");
            CurrentMatchId = PlayerPrefs.GetInt(MatchPref, 0);
            _resolvedUrl = ResolveUrl();
            PendingRoomCode = RoomCodeFromUrl();

            Debug.Log("[NetClient] server " + _resolvedUrl + (CurrentMatchId != 0 ? ", resuming match " + CurrentMatchId : ""));
        }

        private void Update()
        {
#if !UNITY_WEBGL || UNITY_EDITOR
            // In the Editor the socket runs on its own thread and hands messages over here; on WebGL the
            // browser calls straight back into the callbacks and this does not exist.
            if (_socket != null) _socket.DispatchMessageQueue();
#endif

            if (_retryIn > 0f)
            {
                _retryIn -= Time.unscaledDeltaTime;
                if (_retryIn <= 0f && _wantsConnection && _state == NetState.Disconnected) Connect();
            }

            if (_rejoinWindow > 0f)
            {
                _rejoinWindow -= Time.unscaledDeltaTime;
                if (_rejoinWindow <= 0f && PendingMatch == null && CurrentMatchId != 0)
                {
                    // We authenticated, said nothing arrived, and the room never claimed us back.
                    Debug.Log("[NetClient] the match we were in is gone");
                    ForgetMatch();
                    Action handler = MatchLost;
                    if (handler != null) handler();
                }
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            Connected = null;
            Disconnected = null;
            MessageReceived = null;
            MatchLost = null;
            CloseSocket();
        }

        private void OnApplicationQuit()
        {
            _wantsConnection = false;
            CloseSocket();
        }

        // ---- connecting ------------------------------------------------------------------------------

        public void Connect()
        {
            _wantsConnection = true;
            if (_state == NetState.Connecting || _state == NetState.Connected || _state == NetState.Authenticated) return;

            _state = NetState.Connecting;
            _socket = new WebSocket(_resolvedUrl);

            _socket.OnOpen += HandleOpen;
            _socket.OnMessage += HandleMessage;
            _socket.OnError += HandleError;
            _socket.OnClose += HandleClose;

            Debug.Log("[NetClient] connecting to " + _resolvedUrl);
            _socket.Connect();
        }

        public void Disconnect()
        {
            _wantsConnection = false;
            _retryIn = 0f;
            CloseSocket();
            _state = NetState.Disconnected;
        }

        private void CloseSocket()
        {
            if (_socket == null) return;
            WebSocket socket = _socket;
            _socket = null;
            socket.OnOpen -= HandleOpen;
            socket.OnMessage -= HandleMessage;
            socket.OnError -= HandleError;
            socket.OnClose -= HandleClose;
            if (socket.State == WebSocketState.Open) socket.Close();
        }

        private void HandleOpen()
        {
            _state = NetState.Connected;
            Debug.Log("[NetClient] connected");
            Action handler = Connected;
            if (handler != null) handler();
        }

        private void HandleError(string error)
        {
            Debug.LogWarning("[NetClient] socket error: " + error);
        }

        private void HandleClose(WebSocketCloseCode code)
        {
            bool wasAuthenticated = _state == NetState.Authenticated;
            _state = NetState.Disconnected;
            _socket = null;
            Debug.Log("[NetClient] disconnected (" + code + ")");

            Action<string> handler = Disconnected;
            if (handler != null) handler(code.ToString());

            // Only chase the connection when there is something to come back to. Otherwise the lobby
            // decides when to try again, which is when the player presses something.
            if (_wantsConnection && CurrentMatchId != 0)
            {
                _retryIn = ReconnectDelaySeconds;
                if (wasAuthenticated) Debug.Log("[NetClient] will retry in " + ReconnectDelaySeconds + " s");
            }
        }

        // ---- identity --------------------------------------------------------------------------------

        /// <summary>Resumes the stored token if there is one, otherwise becomes a new guest.</summary>
        public void Authenticate(string name)
        {
            if (!string.IsNullOrEmpty(name)) PlayerName = name;
            string token = Token;
            if (string.IsNullOrEmpty(token))
            {
                Send(Messages.AuthGuest, new JObject { ["name"] = PlayerName ?? "" });
                return;
            }
            Send(Messages.AuthResume, new JObject { ["token"] = token });
        }

        // ---- messages --------------------------------------------------------------------------------

        public bool Send(string t, JObject p)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                Debug.LogWarning("[NetClient] cannot send '" + t + "': not connected");
                return false;
            }
            var envelope = new JObject { ["t"] = t, ["p"] = p ?? new JObject() };
            _socket.SendText(envelope.ToString(Formatting.None));
            return true;
        }

        private void HandleMessage(byte[] bytes)
        {
            string frame = System.Text.Encoding.UTF8.GetString(bytes);
            string type;
            JObject payload;
            try
            {
                var envelope = JObject.Parse(frame);
                type = envelope.Value<string>("t") ?? "";
                payload = envelope["p"] as JObject ?? new JObject();
            }
            catch (JsonException e)
            {
                Debug.LogWarning("[NetClient] unreadable frame: " + e.Message);
                return;
            }

            // Answered here and nowhere else: the round trip the server measures is the one it uses to
            // decide whether a command that left before the deadline still counts.
            if (type == Messages.Ping)
            {
                Send(Messages.Pong, new JObject { ["t"] = payload["t"] });
                return;
            }

            if (_verbose) Debug.Log("[NetClient] <- " + type);

            switch (type)
            {
                case Messages.AuthOk:
                    OnAuthOk(payload);
                    break;
                case Messages.MatchStart:
                    OnMatchStart(payload);
                    break;
                case Messages.Error:
                    OnError(payload);
                    break;
            }

            Action<string, JObject> handler = MessageReceived;
            if (handler != null) handler(type, payload);
        }

        private void OnAuthOk(JObject p)
        {
            _state = NetState.Authenticated;
            PlayerId = p.Value<int>("playerId");
            PlayerName = p.Value<string>("name");
            Token = p.Value<string>("token");
            PlayerPrefs.SetString(NamePref, PlayerName ?? "");
            SavePrefs();
            _retriedAsGuest = false;
            Debug.Log("[NetClient] authenticated as " + PlayerName + " (#" + PlayerId + ")");

            // If we believed we were in a match, the server has this moment to say so.
            if (CurrentMatchId != 0 && PendingMatch == null) _rejoinWindow = RejoinWindowSeconds;
        }

        private void OnMatchStart(JObject p)
        {
            PendingMatch = p;
            _rejoinWindow = 0f;
            CurrentMatchId = p.Value<int>("matchId");
            PlayerPrefs.SetInt(MatchPref, CurrentMatchId);
            SavePrefs();
            Debug.Log("[NetClient] match " + CurrentMatchId + " starting, you are seat " + p.Value<int>("youAre"));
        }

        private void OnError(JObject p)
        {
            string code = p.Value<string>("code");
            Debug.LogWarning("[NetClient] server error '" + code + "': " + p.Value<string>("message"));

            if (code != Messages.Errors.BadToken || _retriedAsGuest) return;

            // The server has been restarted and has never heard of us. Start again, once.
            _retriedAsGuest = true;
            Token = "";
            ForgetMatch();
            Send(Messages.AuthGuest, new JObject { ["name"] = PlayerName ?? "" });
        }

        /// <summary>The Arena scene takes the pending start and clears it, so coming back to the lobby does not reload it.</summary>
        public JObject ConsumePendingMatch()
        {
            JObject pending = PendingMatch;
            PendingMatch = null;
            return pending;
        }

        public string ConsumePendingRoomCode()
        {
            string code = PendingRoomCode;
            PendingRoomCode = null;
            return code;
        }

        /// <summary>Called when a match ends, or when the room turns out to be gone.</summary>
        public void ForgetMatch()
        {
            CurrentMatchId = 0;
            PendingMatch = null;
            _rejoinWindow = 0f;
            PlayerPrefs.SetInt(MatchPref, 0);
            SavePrefs();
        }

        // ---- where to connect --------------------------------------------------------------------------

        /// <summary>
        /// The Editor uses the serialized field. A Web build works out its own: <c>?ws=</c> wins so one build
        /// can be pointed anywhere, then the page's own scheme, so a page served over https talks wss and
        /// nobody has to rebuild to move servers.
        /// </summary>
        private string ResolveUrl()
        {
#if UNITY_EDITOR
            return _url;
#else
            string fromQuery = QueryValue("ws");
            if (!string.IsNullOrEmpty(fromQuery)) return fromQuery;

            string page = Application.absoluteURL ?? "";
            if (page.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return "wss://" + HostOf(page) + "/ws";
            if (page.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                return "ws://" + HostOnly(page) + ":7777/ws";
            return _url;
#endif
        }

        private string RoomCodeFromUrl()
        {
            string code = QueryValue("room");
            return string.IsNullOrEmpty(code) ? null : code.Trim().ToUpperInvariant();
        }

        /// <summary>One query parameter of the page's own URL, or null. Empty everywhere but a Web build.</summary>
        private static string QueryValue(string key)
        {
            string url = Application.absoluteURL;
            if (string.IsNullOrEmpty(url)) return null;
            int q = url.IndexOf('?');
            if (q < 0 || q == url.Length - 1) return null;

            string[] pairs = url.Substring(q + 1).Split('&');
            for (int i = 0; i < pairs.Length; i++)
            {
                int eq = pairs[i].IndexOf('=');
                if (eq <= 0) continue;
                if (!string.Equals(pairs[i].Substring(0, eq), key, StringComparison.OrdinalIgnoreCase)) continue;
                return Uri.UnescapeDataString(pairs[i].Substring(eq + 1));
            }
            return null;
        }

        /// <summary>Host and port of a page URL.</summary>
        private static string HostOf(string url)
        {
            int start = url.IndexOf("//", StringComparison.Ordinal);
            if (start < 0) return url;
            start += 2;
            int end = url.IndexOfAny(new[] { '/', '?', '#' }, start);
            return end < 0 ? url.Substring(start) : url.Substring(start, end - start);
        }

        /// <summary>Host of a page URL without its port: an http page is served beside a server on 7777, not on the page's port.</summary>
        private static string HostOnly(string url)
        {
            string host = HostOf(url);
            int colon = host.IndexOf(':');
            return colon < 0 ? host : host.Substring(0, colon);
        }

        /// <summary>
        /// On WebGL the browser only writes prefs to IndexedDB when this is called, and a tab that is closed
        /// never gets to do it later — so it is called at every point identity changes, not at quit.
        /// </summary>
        private static void SavePrefs()
        {
            try
            {
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[NetClient] could not save prefs: " + e.Message);
            }
        }
    }
}
