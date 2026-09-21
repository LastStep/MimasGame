namespace Mimas.Core.Protocol
{
    /// <summary>
    /// Every message type and error code on the wire, as constants, so the server and the client cannot
    /// misspell one at each other (ADR-027). The envelope is always <c>{ "t": &lt;one of these&gt;, "p": { … } }</c>,
    /// one JSON text frame per message.
    /// <para>
    /// Room codes rather than a queue: OPT-0001, decided 17 Sep 2026 (spec amendment A1). The matchmaking
    /// queue returns at M5 with ratings and will add its own types beside these.
    /// </para>
    /// </summary>
    public static class Messages
    {
        // ---- client -> server --------------------------------------------------------------------

        /// <summary><c>{ name }</c> — become a guest and receive a resumable token.</summary>
        public const string AuthGuest = "auth.guest";

        /// <summary><c>{ token }</c> — resume a previous identity, and any live match it is seated in.</summary>
        public const string AuthResume = "auth.resume";

        /// <summary><c>{}</c> — open a fresh room and take seat 0. Replies with <see cref="RoomState"/>.</summary>
        public const string RoomCreate = "room.create";

        /// <summary><c>{ code }</c> — take the free seat in someone else's room.</summary>
        public const string RoomJoin = "room.join";

        /// <summary><c>{ loadout, ready }</c> — choose gear in the room, and say whether you are ready.</summary>
        public const string RoomLoadout = "room.loadout";

        /// <summary><c>{}</c> — leave a room you have not yet started playing in.</summary>
        public const string RoomLeave = "room.leave";

        /// <summary><c>{}</c> — open a room whose other seat is the server's random bot, already ready.</summary>
        public const string BotPlay = "bot.play";

        /// <summary><c>{ matchId, command }</c> — submit a command to the room that holds the truth.</summary>
        public const string MatchCommand = "match.command";

        /// <summary><c>{ matchId }</c> — ask for a fresh full view.</summary>
        public const string MatchResync = "match.resync";

        /// <summary><c>{ t }</c> — the answer to <see cref="Ping"/>; feeds the round-trip estimate.</summary>
        public const string Pong = "pong";

        // ---- server -> client --------------------------------------------------------------------

        /// <summary><c>{ playerId, token, name, room? }</c> — <c>room</c> is the four-letter code when the
        /// resumed player is still seated in a room that is waiting (ADR-032).</summary>
        public const string AuthOk = "auth.ok";

        /// <summary><c>{ code, youAre, seats: [ { name, ready, bot, present } … ] }</c> — sent to every occupant on every change.</summary>
        public const string RoomState = "room.state";

        /// <summary><c>{}</c> — you are out of the room; the lobby is yours again.</summary>
        public const string RoomLeft = "room.left";

        /// <summary><c>{ matchId, round, seq, mapId, youAre, opponentName, view, clock, events }</c> —
        /// <c>matchId</c> is the room's id and is reused across rounds; <c>round</c> counts the matches
        /// played in it, 1 for the first.</summary>
        public const string MatchStart = "match.start";

        /// <summary><c>{ matchId, seq, events, view, clock }</c> — events already filtered for this seat.</summary>
        public const string MatchEvents = "match.events";

        /// <summary><c>{ matchId, reason, view, clock }</c></summary>
        public const string MatchRejected = "match.rejected";

        /// <summary><c>{ matchId, seq, view, clock }</c></summary>
        public const string MatchView = "match.view";

        /// <summary><c>{ matchId, connected, graceMs }</c></summary>
        public const string OpponentStatus = "opponent.status";

        /// <summary><c>{ t }</c> — server-initiated, every <c>PingIntervalMs</c>; the client answers at once.</summary>
        public const string Ping = "ping";

        /// <summary><c>{ code, message }</c></summary>
        public const string Error = "error";

        // ---- error codes -------------------------------------------------------------------------

        public static class Errors
        {
            /// <summary>A message arrived before <c>auth.*</c>.</summary>
            public const string Unauthenticated = "unauthenticated";

            /// <summary>The envelope's <c>t</c> is not one of the types above.</summary>
            public const string UnknownType = "unknown_type";

            /// <summary>The frame was not a JSON object, or a field had the wrong shape.</summary>
            public const string BadJson = "bad_json";

            /// <summary><c>auth.resume</c> with a token the server does not know.</summary>
            public const string BadToken = "bad_token";

            /// <summary>You are already seated in a live match.</summary>
            public const string InMatch = "in_match";

            /// <summary>You are already in a room; leave it first.</summary>
            public const string InRoom = "in_room";

            /// <summary>That action needs you to be in a room and you are not.</summary>
            public const string NotInRoom = "not_in_room";

            /// <summary>No open room has that code.</summary>
            public const string NoSuchRoom = "no_such_room";

            /// <summary>Both seats of that room are taken.</summary>
            public const string RoomFull = "room_full";

            /// <summary>An item id is unknown or sits in the wrong slot; nothing was stored.</summary>
            public const string BadLoadout = "bad_loadout";

            /// <summary>A command named a match this connection is not seated in.</summary>
            public const string UnknownMatch = "unknown_match";
        }
    }
}
