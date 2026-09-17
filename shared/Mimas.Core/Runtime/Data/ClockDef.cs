using System;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// The one clock M2 ships (<c>rules.json</c> <c>clock</c>, design: #time-controls, #online): a flat turn
    /// deadline, the round-trip allowance the server adds before it enforces that deadline, and how long a
    /// dropped seat is kept before it forfeits. These are rules rather than balance because both sides read
    /// them: the server times the turn, the client draws the same rope from the same number.
    /// <para>
    /// Banks, increments and a choice of time control are a later design (<c>timecontrols.json</c> stays
    /// loaded and unused until then).
    /// </para>
    /// </summary>
    public sealed class ClockDef
    {
        /// <summary>The turn deadline in milliseconds, for every turn of every player.</summary>
        public int TurnMs { get; }

        /// <summary>
        /// Cap on the round-trip allowance the server adds to a turn before ending it: a command that left the
        /// client before the deadline is never refused for arriving after it.
        /// </summary>
        public int LagGraceMs { get; }

        /// <summary>How long a disconnected seat is kept before the server forfeits it on the player's behalf.</summary>
        public int ReconnectGraceMs { get; }

        public ClockDef(int turnMs, int lagGraceMs, int reconnectGraceMs)
        {
            if (turnMs <= 0) throw new ArgumentOutOfRangeException(nameof(turnMs), "clock.turnMs must be positive.");
            if (lagGraceMs <= 0) throw new ArgumentOutOfRangeException(nameof(lagGraceMs), "clock.lagGraceMs must be positive.");
            if (reconnectGraceMs <= 0) throw new ArgumentOutOfRangeException(nameof(reconnectGraceMs), "clock.reconnectGraceMs must be positive.");
            TurnMs = turnMs;
            LagGraceMs = lagGraceMs;
            ReconnectGraceMs = reconnectGraceMs;
        }

        /// <summary>The shipped numbers (30 s turn, 1 s lag grace, 60 s reconnect grace); the fallback for fixtures that build a <see cref="RulesDef"/> by hand.</summary>
        public static ClockDef Default() => new ClockDef(30000, 1000, 60000);

        internal static ClockDef FromJsonAt(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            int turnMs = MapJson.RequireInt(obj, "turnMs", where);
            int lagGraceMs = MapJson.RequireInt(obj, "lagGraceMs", where);
            int reconnectGraceMs = MapJson.RequireInt(obj, "reconnectGraceMs", where);
            if (turnMs <= 0) throw new MapLoadException($"{where}.turnMs must be positive.");
            if (lagGraceMs <= 0) throw new MapLoadException($"{where}.lagGraceMs must be positive.");
            if (reconnectGraceMs <= 0) throw new MapLoadException($"{where}.reconnectGraceMs must be positive.");
            return new ClockDef(turnMs, lagGraceMs, reconnectGraceMs);
        }

        public override string ToString() => $"turn {TurnMs} ms, lag grace {LagGraceMs} ms, reconnect grace {ReconnectGraceMs} ms";
    }
}
