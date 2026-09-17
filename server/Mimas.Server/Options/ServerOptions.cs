namespace Mimas.Server.Options;

/// <summary>
/// Everything about this server that is not a rule, bound from the <c>Mimas</c> configuration section.
/// The three clock values are nullable and fall back to <c>rules.json</c>'s <c>clock</c> block, which is
/// the real answer: overriding them is for tests that cannot wait thirty seconds for a turn.
/// </summary>
public sealed class ServerOptions
{
    public const string Section = "Mimas";

    /// <summary>Turn deadline in ms; null uses <c>rules.clock.turnMs</c>.</summary>
    public int? TurnMs { get; set; }

    /// <summary>Cap on the measured round-trip allowance; null uses <c>rules.clock.lagGraceMs</c>.</summary>
    public int? LagGraceMs { get; set; }

    /// <summary>How long a dropped seat is held before it forfeits; null uses <c>rules.clock.reconnectGraceMs</c>.</summary>
    public int? ReconnectGraceMs { get; set; }

    /// <summary>How long the server's bot appears to think before each of its actions.</summary>
    public int BotThinkMs { get; set; } = 1000;

    public string MapId { get; set; } = "arena-4";

    /// <summary>
    /// The hidden modifiers the bot seat carries. The shipped pair is deliberate: it is what makes an
    /// online bot match exercise the reveal path rather than only the happy one.
    /// </summary>
    public string[] BotModifierIds { get; set; } = { "ward-of-feathers", "stone-skin" };

    /// <summary>The bot's gear, in slot order (weapon, crown, boots, armour). The bot does not choose in a room.</summary>
    public string[] BotLoadout { get; set; } = { "flintlock", "ember-circlet", "blink-boots", "leather-jerkin" };

    public string BotName { get; set; } = "Random Bot";

    /// <summary>How often the server pings each connection; the answer is what the round-trip estimate is made of.</summary>
    public int PingIntervalMs { get; set; } = 5000;

    /// <summary>A connection that has not answered a ping for this long is closed.</summary>
    public int IdleCloseMs { get; set; } = 60000;

    /// <summary>How often each room's clock, bot and reconnect graces are checked.</summary>
    public int TickMs { get; set; } = 100;
}
