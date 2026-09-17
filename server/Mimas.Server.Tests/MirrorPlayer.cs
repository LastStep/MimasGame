using Mimas.Core;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Tests;

/// <summary>
/// A player who knows only what the server told them. It rebuilds <see cref="MatchState.FromView"/> from
/// the view attached to the last message, asks that mirror what is legal, and sends one of those.
/// <para>
/// This is the client's own architecture (ADR-026) standing in as a test double, which is the point: if
/// the mirror could not answer these questions the real client could not either, and a match played this
/// way is a match a browser can play.
/// </para>
/// </summary>
public sealed class MirrorPlayer
{
    private readonly ContentCatalog _catalog;
    private readonly FakeClient _client;
    private readonly Rng _rng;

    public int Seat { get; }
    public int MatchId { get; }

    /// <summary>The mirror as of the last message: what every preview would be asked.</summary>
    public MatchState Mirror { get; private set; }

    public PlayerView View { get; private set; }

    public MirrorPlayer(ContentCatalog catalog, FakeClient client, JObject matchStart, uint seed)
    {
        _catalog = catalog;
        _client = client;
        _rng = new Rng(seed);
        Seat = matchStart.Value<int>("youAre");
        MatchId = matchStart.Value<int>("matchId");
        View = Wire.ReadView((JObject)matchStart["view"]!);
        Mirror = MatchState.FromView(catalog, View);
    }

    /// <summary>Replaces the mirror from any message that carries a view.</summary>
    public void Absorb(JObject payload)
    {
        if (payload["view"] is not JObject view) return;
        View = Wire.ReadView(view);
        Mirror = MatchState.FromView(_catalog, View);
    }

    public bool IsMyTurn => !Mirror.IsOver && Mirror.ActivePlayer == Seat;

    /// <summary>
    /// One legal command, chosen from the mirror. Prefers acting over passing, so a test match actually
    /// ends rather than two players passing at each other until the step cap.
    /// </summary>
    public Command Choose()
    {
        var legal = new List<Command>();
        Mirror.EnumerateLegal(Seat, legal);
        if (legal.Count == 0) return new EndTurnCommand(Seat);

        var actions = legal.Where(c => c is not EndTurnCommand).ToList();
        if (actions.Count == 0) return legal[^1];
        return actions[(int)_rng.Range(0, actions.Count)];
    }

    public Task SendAsync(Command command) =>
        _client.SendAsync(Messages.MatchCommand, new JObject
        {
            ["matchId"] = MatchId,
            ["command"] = Wire.Command(command),
        });

    public Task PlayOneAsync() => SendAsync(Choose());
}
