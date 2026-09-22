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

    /// <summary>How long a draft is open before the server picks offer 0 for whoever has not; null uses <c>rules.draft.timeoutMs</c>.</summary>
    public int? DraftTimeoutMs { get; set; }

    /// <summary>How long the server's bot appears to think before each of its actions, a draft pick included.</summary>
    public int BotThinkMs { get; set; } = 1000;

    /// <summary>
    /// The lineage the bot seat prays to; null draws one at random from the catalogue with the room's own
    /// seed, so every lineage gets seen (decided 22 Sep 2026, P6). A test that wants a known reveal fixes it.
    /// The bot's starting Blessing is its lineage's; the two dev passives it used to carry retired with part 2.
    /// </summary>
    public string? BotLineageId { get; set; }

    /// <summary>The bot's gear, in slot order (weapon, crown, boots, armour). The bot does not choose in a room.</summary>
    public string[] BotLoadout { get; set; } = { "flintlock", "ember-circlet", "blink-boots", "leather-jerkin" };

    public string BotName { get; set; } = "Random Bot";

    /// <summary>How often the server pings each connection; the answer is what the round-trip estimate is made of.</summary>
    public int PingIntervalMs { get; set; } = 5000;

    /// <summary>A connection that has not answered a ping for this long is closed.</summary>
    public int IdleCloseMs { get; set; } = 60000;

    /// <summary>How often each room's clock, bot and reconnect graces are checked.</summary>
    public int TickMs { get; set; } = 100;

    /// <summary>
    /// The origins allowed to open a game socket, exactly as a browser writes them (scheme, host, and a
    /// port if it is not the default; no trailing slash). <b>Null or empty means any origin</b>, which is
    /// what development, the tests and a plain <c>dotnet run</c> rely on; production names its one origin
    /// in <c>appsettings.Production.json</c> so that changing it is a redeploy and not a VPS setup
    /// (ADR-033).
    /// </summary>
    public string[]? AllowedOrigins { get; set; }
}
