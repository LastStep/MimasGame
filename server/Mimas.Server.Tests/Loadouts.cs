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

    public static JObject Ready(JObject loadout) => new() { ["loadout"] = loadout, ["ready"] = true };

    public static JObject NotReady(JObject loadout) => new() { ["loadout"] = loadout, ["ready"] = false };
}
