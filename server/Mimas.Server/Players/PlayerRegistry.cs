using System.Security.Cryptography;

namespace Mimas.Server.Players;

/// <summary>
/// Every guest this process has met, by id and by token. In memory only (D4). Thread-safe by one lock:
/// there are two players per match and a handful of matches, so a lock is honest and a concurrent
/// dictionary would only be a claim about a scale this does not have.
/// </summary>
public sealed class PlayerRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<int, Player> _byId = new();
    private readonly Dictionary<string, Player> _byToken = new(StringComparer.Ordinal);
    private int _nextId = 1;

    public int Count
    {
        get { lock (_gate) return _byId.Count; }
    }

    /// <summary>A new guest. An empty or whitespace name becomes <c>Guest-1234</c>; anything longer than 24 characters is trimmed.</summary>
    public Player Guest(string? name)
    {
        lock (_gate)
        {
            string chosen = CleanName(name) ?? "Guest-" + RandomNumberGenerator.GetInt32(1000, 10000);
            var player = new Player(_nextId++, NewToken(), chosen);
            _byId[player.Id] = player;
            _byToken[player.Token] = player;
            return player;
        }
    }

    /// <summary>The player that token belongs to, or null. Unknown tokens are an error, never a silent new guest.</summary>
    public Player? TryResume(string? token)
    {
        if (string.IsNullOrEmpty(token)) return null;
        lock (_gate) return _byToken.TryGetValue(token, out Player? player) ? player : null;
    }

    public Player? TryGet(int id)
    {
        lock (_gate) return _byId.TryGetValue(id, out Player? player) ? player : null;
    }

    public void Rename(Player player, string? name)
    {
        string? cleaned = CleanName(name);
        if (cleaned == null) return;
        lock (_gate) player.Name = cleaned;
    }

    /// <summary>Trimmed, capped at 24 characters, control characters removed; null when nothing is left.</summary>
    private static string? CleanName(string? name)
    {
        if (name == null) return null;
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (char c in name)
            if (!char.IsControl(c)) sb.Append(c);
        string cleaned = sb.ToString().Trim();
        if (cleaned.Length == 0) return null;
        return cleaned.Length > 24 ? cleaned[..24] : cleaned;
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
