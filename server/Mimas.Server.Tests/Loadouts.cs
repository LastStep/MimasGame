using Newtonsoft.Json.Linq;

namespace Mimas.Server.Tests;

/// <summary>The two shipped kits, as the <c>room.loadout</c> message carries them.</summary>
public static class Loadouts
{
    public static JObject Bow => new()
    {
        ["weapon"] = "longbow",
        ["crown"] = "ember-circlet",
        ["boots"] = "leaping-boots",
        ["armour"] = "leather-jerkin",
    };

    public static JObject Gun => new()
    {
        ["weapon"] = "flintlock",
        ["crown"] = "ember-circlet",
        ["boots"] = "blink-boots",
        ["armour"] = "leather-jerkin",
    };

    /// <summary>A loadout naming an item that does not exist.</summary>
    public static JObject Unknown => new()
    {
        ["weapon"] = "railgun",
        ["crown"] = "ember-circlet",
        ["boots"] = "blink-boots",
        ["armour"] = "leather-jerkin",
    };

    /// <summary>A loadout with a real item in the wrong slot.</summary>
    public static JObject WrongSlot => new()
    {
        ["weapon"] = "leather-jerkin",
        ["crown"] = "ember-circlet",
        ["boots"] = "blink-boots",
        ["armour"] = "leather-jerkin",
    };

    /// <summary>The lineage a kit prays to unless a test says otherwise: Bow is Greek, Gun is Norse.</summary>
    public static string LineageFor(JObject loadout) => loadout.Value<string>("weapon") == "flintlock" ? "norse" : "greek";

    public static JObject Ready(JObject loadout, string? lineage = null) => new()
    {
        ["loadout"] = loadout,
        ["lineage"] = lineage ?? LineageFor(loadout),
        ["ready"] = true,
    };

    public static JObject NotReady(JObject loadout, string? lineage = null) => new()
    {
        ["loadout"] = loadout,
        ["lineage"] = lineage ?? LineageFor(loadout),
        ["ready"] = false,
    };

    /// <summary>Ready, praying to a named lineage: what a test that wants a known reveal sends.</summary>
    public static JObject ReadyWith(JObject loadout, string lineage) => Ready(loadout, lineage);

    /// <summary>A loadout with no lineage at all: what an old client sends.</summary>
    public static JObject NoLineage(JObject loadout) => new() { ["loadout"] = loadout, ["ready"] = true };
}
