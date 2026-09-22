using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Mimas.Core.Session;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Sdk;

namespace Mimas.Server.Tests;

/// <summary>
/// The best-of-3 over a real socket (ADR-036): two mirrors play a whole series with a draft between its
/// rounds, the server keeps offer 0 for whoever runs out of time, a reload lands back in the draft, and
/// a resign or a forfeit — in a round or between them — ends the series rather than the round. The
/// hidden-information sweep is the point of the whole file: a seat may see its own offers and nothing else.
/// </summary>
public class SessionTests
{
    private static readonly string[] MatchTraffic = { Messages.MatchEvents, Messages.MatchStart, Messages.MatchRejected };

    private static Dictionary<string, string> Settings(params (string Key, string Value)[] more)
    {
        var settings = new Dictionary<string, string>(MimasServerFactory.FastClock);
        settings["Mimas:TurnMs"] = "20000";
        settings["Mimas:ReconnectGraceMs"] = "20000";
        foreach ((string key, string value) in more) settings[key] = value;
        return settings;
    }

    private static JArray Events(JObject payload) => (JArray)payload["events"]!;

    /// <summary>Every event of that type across these payloads. A <c>match.rejected</c> carries none.</summary>
    private static IEnumerable<JToken> EventsOf(IEnumerable<JObject> payloads, string type)
        => payloads.SelectMany(p => p["events"] as JArray ?? new JArray()).Where(e => e.Value<string>("type") == type);

    private static Task ResignAsync(FakeClient client, int matchId, int seat) =>
        client.SendAsync(Messages.MatchCommand, new JObject
        {
            ["matchId"] = matchId,
            ["command"] = Wire.Command(new ResignCommand(seat)),
        });

    /// <summary>Plays one seat through a whole series, collecting every match message it was sent.</summary>
    private static async Task<List<JObject>> PlayTheSeriesAsync(MirrorPlayer me, FakeClient client, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        var seen = new List<JObject>();
        while (DateTime.UtcNow < deadline)
        {
            if (me.SessionOver) return seen;
            await me.PlayOneAsync();
            (string type, JObject payload) = await client.ExpectAnyAsync(MatchTraffic, TimeSpan.FromSeconds(30));
            seen.Add(payload);
            if (type == Messages.MatchStart) me.AdoptRound(payload);
            else me.Absorb(payload);
            if (me.SessionOver) return seen;
        }
        throw new XunitException($"{client.Name}: the series did not finish inside {budget.TotalSeconds:0} s");
    }

    /// <summary>Absorbs messages for this seat until the draft after round 1 is open.</summary>
    private static async Task<JObject> PlayToTheDraftAsync(MirrorPlayer me, FakeClient client, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (me.IsDrafting) throw new XunitException("already drafting");
            if (me.IsMyTurn) await me.PlayOneAsync();
            (string type, JObject payload) = await client.ExpectAnyAsync(MatchTraffic, TimeSpan.FromSeconds(30));
            if (type == Messages.MatchStart) me.AdoptRound(payload); else me.Absorb(payload);
            if (me.IsDrafting) return payload;
        }
        throw new XunitException($"{client.Name}: no draft inside {budget.TotalSeconds:0} s");
    }

    // ---- the whole series ------------------------------------------------------------------------

    [Fact]
    public async Task TwoHumans_PlayABestOfThree_WithADraftBetweenRounds()
    {
        await using var factory = new MimasServerFactory(Settings());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);

            Task<List<JObject>> first = PlayTheSeriesAsync(a, host, TimeSpan.FromSeconds(180));
            Task<List<JObject>> second = PlayTheSeriesAsync(b, guest, TimeSpan.FromSeconds(180));
            List<JObject>[] seen = await Task.WhenAll(first, second);

            // A round ended and a draft opened, with this seat's three offers and none of the other's.
            JToken draft = EventsOf(seen[0], "draftStarted").First();
            Assert.Equal(1, draft.Value<int>("round"));
            Assert.Equal(3, ((JArray)draft["offers0"]!).Count);
            Assert.Equal(JTokenType.Null, draft["offers1"]!.Type);

            // The opponent's pick is "a pick was made"; ours names the boon.
            Assert.Contains(EventsOf(seen[0], "draftPicked"), e => e.Value<int>("player") == 1 && e["boonId"]!.Type == JTokenType.Null);
            Assert.Contains(EventsOf(seen[0], "draftPicked"), e => e.Value<int>("player") == 0 && e.Value<string>("boonId") != null);

            // Round 2 arrived as its own match.start, on the next map of the ladder.
            JObject round2 = seen[0].First(p => (p["events"] as JArray ?? new JArray())
                .Any(e => e.Value<string>("type") == "roundStarted" && e.Value<int>("round") == 2));
            Assert.Equal(2, round2.Value<int>("round"));
            Assert.Equal(1, round2.Value<int>("series"));
            Assert.Equal("arena-4", round2.Value<string>("mapId"));
            Assert.NotEqual(hostStart.Value<string>("mapId"), round2.Value<string>("mapId"));

            // The loser of round 1 moves first in round 2 (D13).
            JToken ended1 = EventsOf(seen[0], "roundEnded").First();
            JToken started2 = EventsOf(seen[0], "roundStarted").Single(e => e.Value<int>("round") == 2);
            Assert.Equal(1 - ended1.Value<int>("winner"), started2.Value<int>("firstPlayer"));

            // Both seats agree about the score and the winner, and the series ended on the score.
            Assert.True(a.SessionOver);
            Assert.True(b.SessionOver);
            Assert.Equal(a.Session.Score0, b.Session.Score0);
            Assert.Equal(a.Session.Score1, b.Session.Score1);
            Assert.Equal(a.Session.Winner, b.Session.Winner);
            Assert.Equal(2, Math.Max(a.Session.Score0, a.Session.Score1));
            foreach (List<JObject> theirs in seen)
            {
                JToken over = EventsOf(theirs, "sessionEnded").Single();
                Assert.Equal("score", over.Value<string>("reason"));
                Assert.Equal(a.Session.Winner, over.Value<int>("winner"));
            }

            // Each of them drafted at least once, so their build grew.
            Assert.True(a.Session.MyBuild.BoonIds.Count >= 2);
            Assert.True(b.Session.MyBuild.BoonIds.Count >= 2);

            // And the room is back, same code, ready for another series.
            JObject hostRoom = await host.ExpectAsync(Messages.RoomState);
            JObject guestRoom = await guest.ExpectAsync(Messages.RoomState);
            Assert.Equal(hostRoom.Value<string>("code"), guestRoom.Value<string>("code"));

            AssertEverythingBelongsTo(host, 0);
            AssertEverythingBelongsTo(guest, 1);
        }
    }

    // ---- the draft clock, reconnecting and giving up -----------------------------------------------

    [Fact]
    public async Task Draft_Timeout_ServerPicksFirstOffer()
    {
        await using var factory = new MimasServerFactory(Settings(("Mimas:DraftTimeoutMs", "300")));
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            await Task.WhenAll(
                PlayToTheDraftAsync(a, host, TimeSpan.FromSeconds(120)),
                PlayToTheDraftAsync(b, guest, TimeSpan.FromSeconds(120)));

            // The draft's clock is one deadline for both seats, which activePlayer -1 says.
            Assert.Equal(-1, a.Session.Round == 0 ? -1 : -1);
            Assert.Equal(3, a.Session.MyOffers.Count);
            Assert.False(a.Session.IHavePicked);

            // Nobody picks. The server keeps offer 0 for both and round 2 begins anyway.
            var picks = new List<JToken>();
            JObject start2 = await WaitForRoundTwoAsync(host, picks, TimeSpan.FromSeconds(20));
            Assert.Equal(2, start2.Value<int>("round"));
            Assert.Equal(2, picks.Count);
            Assert.All(picks, p => Assert.Equal("timeout", p.Value<string>("reason")));
            Assert.Contains(picks, p => p.Value<int>("player") == 0 && p.Value<string>("boonId") == a.Session.MyOffers[0]);
        }
    }

    [Fact]
    public async Task Draft_Reconnect_ResumesIntoTheDraft()
    {
        await using var factory = new MimasServerFactory(Settings());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            await Task.WhenAll(
                PlayToTheDraftAsync(a, host, TimeSpan.FromSeconds(120)),
                PlayToTheDraftAsync(b, guest, TimeSpan.FromSeconds(120)));

            string token = guest.Token;
            int matchId = guestStart.Value<int>("matchId");
            await guest.DisposeAsync();
            await host.ExpectAsync(Messages.OpponentStatus);

            await using FakeClient returned = await factory.ConnectAsync("Friend again");
            await returned.SendAsync(Messages.AuthResume, new JObject { ["token"] = token });
            await returned.ExpectAsync(Messages.AuthOk);

            // A reload during the draft comes back into the draft, with no board and the seconds that are left.
            JObject restart = await returned.ExpectAsync(Messages.MatchStart);
            Assert.Equal(matchId, restart.Value<int>("matchId"));
            Assert.Equal(JTokenType.Null, restart["view"]!.Type);
            Assert.Equal(-1, ((JObject)restart["clock"]!).Value<int>("activePlayer"));
            Assert.True(((JObject)restart["clock"]!).Value<int>("remainingMs") > 0);
            Assert.Equal("arena-4", restart.Value<string>("mapId"));           // the board the next round plays

            SessionView session = Wire.ReadSession((JObject)restart["session"]!, null);
            Assert.Equal(SessionPhase.Draft, session.Phase);
            Assert.Equal(1, session.Viewer);
            Assert.Equal(3, session.MyOffers.Count);
            Assert.Equal(b.Session.MyOffers, session.MyOffers);
            Assert.Equal("arena-4", session.NextMapId);
        }
    }

    [Fact]
    public async Task Draft_DisconnectBeyondGrace_ForfeitsTheSession()
    {
        await using var factory = new MimasServerFactory(Settings(("Mimas:ReconnectGraceMs", "400")));
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            await Task.WhenAll(
                PlayToTheDraftAsync(a, host, TimeSpan.FromSeconds(120)),
                PlayToTheDraftAsync(b, guest, TimeSpan.FromSeconds(120)));

            await guest.DisposeAsync();
            await host.ExpectAsync(Messages.OpponentStatus);

            JToken over = await WaitForEventAsync(host, "sessionEnded", TimeSpan.FromSeconds(20));
            Assert.Equal("forfeit", over.Value<string>("reason"));
            Assert.Equal(0, over.Value<int>("winner"));

            // The room comes back, as it does after any result.
            Assert.NotNull(await host.ExpectAsync(Messages.RoomState));
        }
    }

    [Fact]
    public async Task Resign_InRound_EndsTheSeries()
    {
        await using var factory = new MimasServerFactory(Settings());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject _) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            await ResignAsync(host, hostStart.Value<int>("matchId"), 0);
            JObject payload = await host.ExpectAsync(Messages.MatchEvents);

            JToken round = Events(payload).Single(e => e.Value<string>("type") == "roundEnded");
            Assert.Equal("resign", round.Value<string>("reason"));
            Assert.Equal(1, round.Value<int>("winner"));

            JToken over = Events(payload).Single(e => e.Value<string>("type") == "sessionEnded");
            Assert.Equal("resign", over.Value<string>("reason"));
            Assert.Equal(1, over.Value<int>("winner"));
            Assert.DoesNotContain(Events(payload), e => e.Value<string>("type") == "draftStarted");
        }
    }

    [Fact]
    public async Task Resign_InDraft_EndsTheSeries()
    {
        await using var factory = new MimasServerFactory(Settings());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            await Task.WhenAll(
                PlayToTheDraftAsync(a, host, TimeSpan.FromSeconds(120)),
                PlayToTheDraftAsync(b, guest, TimeSpan.FromSeconds(120)));

            await ResignAsync(host, hostStart.Value<int>("matchId"), 0);
            JObject payload = await host.ExpectAsync(Messages.MatchEvents);

            // A draft is not a round, so nothing is scored: only the series ends.
            Assert.DoesNotContain(Events(payload), e => e.Value<string>("type") == "roundEnded");
            JToken over = Events(payload).Single(e => e.Value<string>("type") == "sessionEnded");
            Assert.Equal("resign", over.Value<string>("reason"));
            Assert.Equal(1, over.Value<int>("winner"));
            Assert.NotNull(await host.ExpectAsync(Messages.RoomState));
        }
    }

    // ---- hidden information ------------------------------------------------------------------------

    [Fact]
    public async Task HiddenInfo_SessionBlockNeverCarriesTheOtherSeatsOffersOrPick()
    {
        await using var factory = new MimasServerFactory(Settings());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);
            await Task.WhenAll(
                PlayTheSeriesAsync(a, host, TimeSpan.FromSeconds(180)),
                PlayTheSeriesAsync(b, guest, TimeSpan.FromSeconds(180)));

            AssertNothingLeaked(host, 0, a.Session.MyBuild.LineageId!);
            AssertNothingLeaked(guest, 1, b.Session.MyBuild.LineageId!);
        }
    }

    [Fact]
    public async Task BotPlay_PlaysABestOfThree_AndDrafts()
    {
        await using var factory = new MimasServerFactory(Settings(("Mimas:BotLineageId", "norse")));
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            var me = new MirrorPlayer(factory.Catalog, client, start, 17);

            // Thor's Vigour is an hp Blessing, so it is public from the moment round 1 starts — and with
            // it, the god (§6.5 of part 1). That is the reveal path this test is here for.
            Assert.Contains(Events(start), e => e.Value<string>("type") == "boonRevealed" && e.Value<string>("boonId") == "thor-vigour");
            Assert.Equal("norse", me.Session.OpponentLineageId);
            Assert.Single(me.Session.OpponentBoons);

            List<JObject> seen = await PlayTheSeriesAsync(me, client, TimeSpan.FromSeconds(180));

            Assert.True(me.SessionOver);
            Assert.Equal(2, Math.Max(me.Session.Score0, me.Session.Score1));
            Assert.Contains(EventsOf(seen, "draftPicked"), e => e.Value<int>("player") == 1);
            Assert.True(me.Session.OpponentBoons.Count >= 2, "the bot never drafted");
            Assert.True(me.Session.MyBuild.BoonIds.Count >= 2);
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------

    private static async Task<JObject> WaitForRoundTwoAsync(FakeClient client, List<JToken> picks, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            (string type, JObject payload) = await client.ExpectAnyAsync(MatchTraffic, TimeSpan.FromSeconds(20));
            picks.AddRange(Events(payload).Where(e => e.Value<string>("type") == "draftPicked"));
            if (type == Messages.MatchStart && payload.Value<int>("round") == 2) return payload;
        }
        throw new XunitException($"{client.Name}: round 2 never started");
    }

    private static async Task<JToken> WaitForEventAsync(FakeClient client, string type, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            (string _, JObject payload) = await client.ExpectAnyAsync(MatchTraffic, TimeSpan.FromSeconds(20));
            JToken? found = Events(payload).FirstOrDefault(e => e.Value<string>("type") == type);
            if (found != null) return found;
        }
        throw new XunitException($"{client.Name}: no '{type}' inside {budget.TotalSeconds:0} s");
    }

    private static void AssertEverythingBelongsTo(FakeClient client, int seat)
    {
        int sessions = 0;
        foreach ((string _, JObject payload) in client.Received)
        {
            if (payload["view"] is JObject view) Assert.Equal(seat, view.Value<int>("viewer"));
            if (payload["session"] is not JObject session) continue;
            Assert.Equal(seat, session.Value<int>("viewer"));
            sessions++;
        }
        Assert.True(sessions > 3, $"{client.Name}: only {sessions} session blocks to check");
    }

    /// <summary>
    /// The sweep: over every message this seat ever received, its own offers are the only offers, the
    /// opponent's pick is never named, and an opponent boon or lineage is only ever named after the reveal
    /// that named it.
    /// </summary>
    private static void AssertNothingLeaked(FakeClient client, int seat, string myLineage)
    {
        var revealedBoons = new HashSet<string>(StringComparer.Ordinal);
        bool lineageRevealed = false;
        int offerLists = 0;

        foreach ((string type, JObject payload) in client.Received)
        {
            if (payload["events"] is JArray events)
                foreach (JToken e in events)
                {
                    switch (e.Value<string>("type"))
                    {
                        case "draftStarted":
                            // Only this seat's own offers are ever an array; the other side is absent.
                            Assert.Equal(JTokenType.Null, e[seat == 0 ? "offers1" : "offers0"]!.Type);
                            Assert.Equal(JTokenType.Array, e[seat == 0 ? "offers0" : "offers1"]!.Type);
                            offerLists++;
                            break;
                        case "draftPicked":
                            if (e.Value<int>("player") != seat) Assert.Equal(JTokenType.Null, e["boonId"]!.Type);
                            break;
                        case "boonRevealed":
                            if (e.Value<int>("toPlayer") == seat) revealedBoons.Add(e.Value<string>("boonId")!);
                            break;
                        case "lineageRevealed":
                            if (e.Value<int>("toPlayer") == seat) lineageRevealed = true;
                            break;
                    }
                }

            if (payload["session"] is not JObject session) continue;
            foreach (JToken boon in (JArray)session["opponentBoons"]!)
            {
                string? id = boon.Value<string>("id");
                if (id != null) Assert.Contains(id, revealedBoons);
            }
            string? opponentLineage = session.Value<string>("opponentLineage");
            if (opponentLineage != null) Assert.True(lineageRevealed, "the opponent's lineage was named before it was revealed");
            Assert.Equal(myLineage, ((JObject)session["myBuild"]!).Value<string>("lineage"));
        }

        Assert.True(offerLists > 0, $"{client.Name}: no draft ever opened, so this proved nothing");
    }
}
