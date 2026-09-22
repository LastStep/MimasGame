using Mimas.Core;
using Mimas.Core.Content;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Core.Session;
using Newtonsoft.Json.Linq;

namespace Mimas.Server.Tests;

/// <summary>
/// A player who knows only what the server told them. It rebuilds <see cref="MatchState.FromView"/> from
/// the view attached to the last message, reads the session block beside it, asks that mirror what is
/// legal, and sends one of those — a move inside a round, a draft pick between rounds.
/// <para>
/// This is the client's own architecture (ADR-026, ADR-036) standing in as a test double, which is the
/// point: if the mirror could not answer these questions the real client could not either, and a series
/// played this way is a series a browser can play.
/// </para>
/// </summary>
public sealed class MirrorPlayer
{
    private readonly ContentCatalog _catalog;
    private readonly FakeClient _client;
    private readonly Rng _rng;

    public int Seat { get; }
    public int MatchId { get; }

    /// <summary>The mirror as of the last view: what every preview would be asked. Not refreshed between rounds.</summary>
    public MatchState Mirror { get; private set; }

    public PlayerView View { get; private set; }

    /// <summary>The series as this seat sees it: score, phase, own offers, the opponent's boons as far as revealed.</summary>
    public SessionView Session { get; private set; }

    public MirrorPlayer(ContentCatalog catalog, FakeClient client, JObject matchStart, uint seed)
    {
        _catalog = catalog;
        _client = client;
        _rng = new Rng(seed);
        Seat = matchStart.Value<int>("youAre");
        MatchId = matchStart.Value<int>("matchId");
        View = Wire.ReadView((JObject)matchStart["view"]!);
        Mirror = MatchState.FromView(catalog, View);
        Session = Wire.ReadSession((JObject)matchStart["session"]!, View);
    }

    /// <summary>Replaces the mirror and the session from any message that carries them.</summary>
    public void Absorb(JObject payload)
    {
        PlayerView? view = payload["view"] is JObject v ? Wire.ReadView(v) : null;
        if (view != null)
        {
            View = view;
            Mirror = MatchState.FromView(_catalog, view);
        }
        if (payload["session"] is JObject session) Session = Wire.ReadSession(session, view);
    }

    public bool IsDrafting => Session.Phase == SessionPhase.Draft;

    public bool SessionOver => Session.IsOver;

    public bool IsMyTurn => !IsDrafting && !SessionOver && !Mirror.IsOver && Mirror.ActivePlayer == Seat;

    /// <summary>
    /// One legal command, chosen from the mirror: a random offer in a draft, otherwise a random action.
    /// Prefers acting over passing, so a test round actually ends rather than two players passing at each
    /// other until the step cap. Null when there is nothing this seat may do right now.
    /// </summary>
    public Command? Choose()
    {
        if (SessionOver) return null;
        if (IsDrafting)
        {
            if (Session.IHavePicked || Session.MyOffers.Count == 0) return null;
            return new DraftPickCommand(Seat, (int)_rng.Range(0, Session.MyOffers.Count));
        }
        if (!IsMyTurn) return null;

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

    public Task PlayOneAsync()
    {
        Command? command = Choose();
        return command == null ? Task.CompletedTask : SendAsync(command);
    }

    /// <summary>A fresh round arrived: the mirror and the session are rebuilt from its <c>match.start</c>.</summary>
    public void AdoptRound(JObject matchStart)
    {
        View = Wire.ReadView((JObject)matchStart["view"]!);
        Mirror = MatchState.FromView(_catalog, View);
        Session = Wire.ReadSession((JObject)matchStart["session"]!, View);
    }
}
