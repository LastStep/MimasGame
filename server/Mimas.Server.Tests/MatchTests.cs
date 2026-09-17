using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Protocol;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Sdk;

namespace Mimas.Server.Tests;

/// <summary>
/// Whole matches, over a real socket, played by the client's own mirror (ADR-026). This is the gate the
/// online milestone hangs on: if these pass, two browsers can play, and every claim about clocks,
/// hidden information, resignation and reconnection is a fact rather than an intention.
/// </summary>
public class MatchTests
{
    /// <summary>Turns long enough that a test can think, for the tests that play a whole game.</summary>
    private static Dictionary<string, string> UnhurriedClock()
    {
        var settings = new Dictionary<string, string>(MimasServerFactory.FastClock);
        settings["Mimas:TurnMs"] = "20000";
        settings["Mimas:ReconnectGraceMs"] = "20000";
        return settings;
    }

    private static JArray Events(JObject payload) => (JArray)payload["events"]!;

    private static bool Has(JObject payload, string eventType) =>
        Events(payload).Any(e => e.Value<string>("type") == eventType);

    // ---- starting --------------------------------------------------------------------------------

    [Fact]
    public async Task BotPlay_StartsMatch_ViewClockAndOpponentName()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            Assert.Equal(0, start.Value<int>("youAre"));
            Assert.Equal("Random Bot", start.Value<string>("opponentName"));
            Assert.Equal("arena-4", start.Value<string>("mapId"));

            var clock = (JObject)start["clock"]!;
            Assert.Equal(400, clock.Value<int>("turnMs"));
            Assert.InRange(clock.Value<int>("remainingMs"), 0, 400);

            PlayerView view = Wire.ReadView((JObject)start["view"]!);
            Assert.Equal(0, view.Viewer);
            Assert.Equal(2, view.Units.Count);
            Assert.Equal(8, view.Props.Count);
            Assert.Single(view.Units.Where(u => u.IsMine));
        }
    }

    [Fact]
    public async Task BotMatch_MirrorPlayerPlaysToTheEnd()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            var me = new MirrorPlayer(factory.Catalog, client, start, 17);
            JObject last = await PlayToTheEndAsync(me, client, TimeSpan.FromSeconds(60));

            Assert.True(me.Mirror.IsOver);
            Assert.InRange(me.View.Winner, 0, 1);
            Assert.True(Has(last, "matchEnded"));
        }
    }

    [Fact]
    public async Task TwoHumans_MirrorPlayersPlayToTheEnd()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            var a = new MirrorPlayer(factory.Catalog, host, hostStart, 5);
            var b = new MirrorPlayer(factory.Catalog, guest, guestStart, 9);

            Task<JObject> first = PlayToTheEndAsync(a, host, TimeSpan.FromSeconds(90));
            Task<JObject> second = PlayToTheEndAsync(b, guest, TimeSpan.FromSeconds(90));
            JObject[] ends = await Task.WhenAll(first, second);

            Assert.True(a.Mirror.IsOver);
            Assert.True(b.Mirror.IsOver);
            Assert.Equal(a.View.Winner, b.View.Winner);
            Assert.All(ends, e => Assert.True(Has(e, "matchEnded")));

            // Every view either player was ever sent was their own. This is golden rule 6 as an
            // assertion: a MatchState never leaves the server, only a projection of it per seat.
            AssertEveryViewBelongsTo(host, a.Seat);
            AssertEveryViewBelongsTo(guest, b.Seat);
        }
    }

    // ---- commands --------------------------------------------------------------------------------

    [Fact]
    public async Task Command_WrongSeat_Rejected()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject _) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            // Seat 0 claims to be seat 1.
            await host.SendAsync(Messages.MatchCommand, new JObject
            {
                ["matchId"] = hostStart.Value<int>("matchId"),
                ["command"] = Wire.Command(new EndTurnCommand(1)),
            });

            JObject rejected = await host.ExpectAsync(Messages.MatchRejected);
            Assert.Equal("not your seat", rejected.Value<string>("reason"));
            Assert.NotNull(rejected["view"]);
        }
    }

    [Fact]
    public async Task Command_Illegal_RejectedWithView()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            var me = new MirrorPlayer(factory.Catalog, client, start, 3);

            // Who moves first is a seeded coin flip on the server, so wait for our turn rather than
            // assume it: off turn the refusal would be NotYourTurn and this would prove nothing.
            await WaitForMyTurnAsync(me, client);

            // Somewhere that is not on the map at all: the sort of thing only a broken client sends,
            // and exactly what the server must refuse without losing the match.
            await me.SendAsync(new MoveCommand(me.Seat, me.View.Units.First(u => u.IsMine).Id, "move", new Hex(99, -99)));

            JObject rejected = await client.ExpectAsync(Messages.MatchRejected);
            Assert.StartsWith("IllegalMove", rejected.Value<string>("reason"));

            PlayerView view = Wire.ReadView((JObject)rejected["view"]!);
            Assert.Equal(me.Seat, view.Viewer);
            Assert.False(view.IsOver);
        }
    }

    [Fact]
    public async Task Command_UnknownMatch_Error()
    {
        await using var factory = new MimasServerFactory();
        await using FakeClient client = await factory.JoinAsync("Rohan");

        await client.SendAsync(Messages.MatchCommand, new JObject
        {
            ["matchId"] = 999,
            ["command"] = Wire.Command(new EndTurnCommand(0)),
        });

        Assert.Equal(Messages.Errors.UnknownMatch, (await client.ExpectErrorAsync()).Value<string>("code"));
    }

    // ---- the clock -------------------------------------------------------------------------------

    [Fact]
    public async Task Turn_TimesOut_ServerEndsTurn()
    {
        await using var factory = new MimasServerFactory();
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            int me = start.Value<int>("youAre");

            // Do nothing at all. The server is the timekeeper, and a turn nobody spends still ends.
            JObject timedOut = await WaitForEventAsync(client, e =>
                e.Value<string>("type") == "turnEnded" && e.Value<string>("reason") == "timeout" && e.Value<int>("player") == me,
                TimeSpan.FromSeconds(10));

            Assert.True(Has(timedOut, "turnEnded"));

            // And the other seat's turn began, so the match moved on rather than stalling.
            JObject next = await WaitForEventAsync(client, e => e.Value<string>("type") == "turnStarted", TimeSpan.FromSeconds(10));
            Assert.NotNull(next);
        }
    }

    [Fact]
    public async Task Clock_RemainingDecreasesBetweenMessages()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            int firstRemaining = ((JObject)start["clock"]!).Value<int>("remainingMs");
            await Task.Delay(250);

            await client.SendAsync(Messages.MatchResync, new JObject { ["matchId"] = start.Value<int>("matchId") });
            JObject view = await client.ExpectAsync(Messages.MatchView);
            int laterRemaining = ((JObject)view["clock"]!).Value<int>("remainingMs");

            Assert.True(laterRemaining < firstRemaining, $"{laterRemaining} ms is not less than {firstRemaining} ms");
            Assert.True(laterRemaining >= 0);
        }
    }

    [Fact]
    public async Task Resync_ReturnsCurrentView()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            await client.SendAsync(Messages.MatchResync, new JObject { ["matchId"] = start.Value<int>("matchId") });
            JObject resync = await client.ExpectAsync(Messages.MatchView);

            Assert.Equal(start.Value<int>("matchId"), resync.Value<int>("matchId"));
            PlayerView view = Wire.ReadView((JObject)resync["view"]!);
            Assert.Equal(start.Value<int>("youAre"), view.Viewer);
            Assert.Equal(2, view.Units.Count);
        }
    }

    // ---- hidden information ------------------------------------------------------------------------

    [Fact]
    public async Task HiddenInfo_OpponentViewHidesAbilitiesAndModifiers()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            var me = new MirrorPlayer(factory.Catalog, client, start, 21);
            UnitView enemy = me.View.Units.Single(u => !u.IsMine);

            // The bot carries both hidden modifiers (D11) so this path is exercised online, and none of
            // its abilities has been used yet.
            Assert.Equal(2, enemy.UnrevealedModifierCount);
            Assert.All(enemy.Modifiers, m => Assert.Null(m.Id));
            Assert.Contains(enemy.Abilities, a => a.Id == null);

            // Gear is public even while what it grants is not: that is the whole shape of the bluff.
            Assert.Equal(4, enemy.ItemIds.Count);
            Assert.All(enemy.ItemIds, id => Assert.False(string.IsNullOrEmpty(id)));

            // Play until the bot uses something, then it is no longer a secret.
            JObject revealed = await WaitForEventAsync(client, e => e.Value<string>("type") == "abilityRevealed",
                TimeSpan.FromSeconds(30), () => me.IsMyTurn ? me.PlayOneAsync() : Task.CompletedTask, me);

            string shown = Events(revealed).First(e => e.Value<string>("type") == "abilityRevealed").Value<string>("abilityId")!;
            UnitView after = me.View.Units.Single(u => !u.IsMine);
            Assert.Contains(after.Abilities, a => a.Id == shown);
        }
    }

    [Fact]
    public async Task HiddenInfo_NoHiddenLineLeaksBeforeReveal()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient client, JObject start) = await Rooms.VsBotAsync(factory);
        await using (client)
        {
            var me = new MirrorPlayer(factory.Catalog, client, start, 33);
            await PlayToTheEndAsync(me, client, TimeSpan.FromSeconds(60));

            var revealedSoFar = new HashSet<string>(StringComparer.Ordinal);
            int hiddenLinesSeen = 0;

            foreach ((string type, JObject payload) in client.Received)
            {
                if (type != Messages.MatchEvents && type != Messages.MatchStart) continue;
                foreach (JToken e in Events(payload))
                {
                    string kind = e.Value<string>("type") ?? "";

                    if (kind == "modifierRevealed")
                    {
                        revealedSoFar.Add(e.Value<string>("modifierId")!);
                        continue;
                    }
                    if (kind != "attackResolved") continue;

                    foreach (JToken line in (JArray)e["breakdown"]!["lines"]!)
                    {
                        if (!line.Value<bool>("hidden")) continue;
                        string id = line.Value<string>("id")!;
                        hiddenLinesSeen++;

                        // A hidden line may only appear once its reveal has been sent — in an earlier
                        // batch, or earlier in this one.
                        bool revealedHere = Events(payload).Any(x => x.Value<string>("type") == "modifierRevealed" && x.Value<string>("modifierId") == id);
                        Assert.True(revealedSoFar.Contains(id) || revealedHere,
                            $"hidden modifier '{id}' appeared in a damage breakdown before it was revealed");
                    }
                }
            }

            // If no hidden line ever appeared the assertion above proved nothing; the bot's stone-skin
            // and ward-of-feathers are defensive, so any attack on it produces one.
            Assert.True(hiddenLinesSeen > 0, "no hidden damage line appeared in the whole match");
        }
    }

    // ---- resigning and disconnecting ----------------------------------------------------------------

    [Fact]
    public async Task Resign_EndsMatch_OpponentWins()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            await host.SendAsync(Messages.MatchCommand, new JObject
            {
                ["matchId"] = hostStart.Value<int>("matchId"),
                ["command"] = Wire.Command(new ResignCommand(0)),
            });

            JObject toLoser = await host.ExpectAsync(Messages.MatchEvents);
            JObject toWinner = await guest.ExpectAsync(Messages.MatchEvents);

            foreach (JObject payload in new[] { toLoser, toWinner })
            {
                JToken ended = Events(payload).Single(e => e.Value<string>("type") == "matchEnded");
                Assert.Equal(1, ended.Value<int>("winner"));
                Assert.Equal("resign", ended.Value<string>("reason"));
            }

            Assert.True(Wire.ReadView((JObject)toWinner["view"]!).IsOver);
        }
    }

    [Fact]
    public async Task Resign_OffTurn_Allowed()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        await using (guest)
        {
            // Whoever is not to move concedes. Giving up is not a turn action.
            int active = Wire.ReadView((JObject)hostStart["view"]!).ActivePlayer;
            int quitter = 1 - active;
            FakeClient quitterClient = quitter == 0 ? host : guest;
            JObject quitterStart = quitter == 0 ? hostStart : guestStart;

            await quitterClient.SendAsync(Messages.MatchCommand, new JObject
            {
                ["matchId"] = quitterStart.Value<int>("matchId"),
                ["command"] = Wire.Command(new ResignCommand(quitter)),
            });

            JObject payload = await quitterClient.ExpectAsync(Messages.MatchEvents);
            JToken ended = Events(payload).Single(e => e.Value<string>("type") == "matchEnded");
            Assert.Equal(active, ended.Value<int>("winner"));
            Assert.Equal("resign", ended.Value<string>("reason"));
        }
    }

    [Fact]
    public async Task Reconnect_WithinGrace_GetsMatchStartAndOpponentIsTold()
    {
        await using var factory = new MimasServerFactory(UnhurriedClock());
        (FakeClient host, FakeClient guest, JObject hostStart, JObject guestStart) = await Rooms.PlayAsync(factory);
        await using (host)
        {
            string token = guest.Token;
            int matchId = guestStart.Value<int>("matchId");
            await guest.DisposeAsync();

            JObject gone = await host.ExpectAsync(Messages.OpponentStatus);
            Assert.False(gone.Value<bool>("connected"));
            Assert.True(gone.Value<int>("graceMs") > 0);

            // A page refresh is a new socket with the same token, and it must land back in the match.
            await using FakeClient returned = await factory.ConnectAsync("Friend again");
            await returned.SendAsync(Messages.AuthResume, new JObject { ["token"] = token });
            await returned.ExpectAsync(Messages.AuthOk);

            JObject restart = await returned.ExpectAsync(Messages.MatchStart);
            Assert.Equal(matchId, restart.Value<int>("matchId"));
            Assert.Equal(1, restart.Value<int>("youAre"));
            Assert.Empty(Events(restart));
            Assert.Equal(1, Wire.ReadView((JObject)restart["view"]!).Viewer);

            JObject back = await host.ExpectAsync(Messages.OpponentStatus);
            Assert.True(back.Value<bool>("connected"));
        }
    }

    [Fact]
    public async Task Disconnect_BeyondGrace_Forfeit()
    {
        await using var factory = new MimasServerFactory();   // 400 ms grace
        (FakeClient host, FakeClient guest, JObject hostStart, JObject _) = await Rooms.PlayAsync(factory);
        await using (host)
        {
            await guest.DisposeAsync();

            JObject gone = await host.ExpectAsync(Messages.OpponentStatus);
            Assert.False(gone.Value<bool>("connected"));

            JObject forfeited = await WaitForEventAsync(host, e => e.Value<string>("type") == "matchEnded", TimeSpan.FromSeconds(10));
            JToken ended = Events(forfeited).Single(e => e.Value<string>("type") == "matchEnded");

            Assert.Equal(0, ended.Value<int>("winner"));
            Assert.Equal("forfeit", ended.Value<string>("reason"));
        }
    }

    // ---- helpers -----------------------------------------------------------------------------------

    /// <summary>Plays this seat until the match ends, one legal command at a time, chosen from the mirror.</summary>
    private static async Task<JObject> PlayToTheEndAsync(MirrorPlayer me, FakeClient client, TimeSpan budget)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        JObject? last = null;

        while (DateTime.UtcNow < deadline)
        {
            if (me.Mirror.IsOver) return last ?? throw new XunitException("the match was over before anything happened");
            if (me.IsMyTurn) await me.PlayOneAsync();

            (string type, JObject payload) = await client.ExpectEitherAsync(Messages.MatchEvents, Messages.MatchRejected, TimeSpan.FromSeconds(20));
            me.Absorb(payload);
            last = payload;

            // A refusal is not a failure here: two mirrors can both believe it is their turn for as long
            // as a message is in flight. It is a failure if the match stops making progress, which the
            // budget catches.
            if (type == Messages.MatchEvents && me.Mirror.IsOver) return payload;
        }

        throw new XunitException($"{client.Name}: the match did not finish inside {budget.TotalSeconds:0} s");
    }

    /// <summary>Absorbs messages until this seat is the one to move.</summary>
    private static async Task WaitForMyTurnAsync(MirrorPlayer me, FakeClient client, TimeSpan? budget = null)
    {
        DateTime deadline = DateTime.UtcNow + (budget ?? TimeSpan.FromSeconds(30));
        while (!me.IsMyTurn)
        {
            if (DateTime.UtcNow > deadline) throw new XunitException($"{client.Name}: never got a turn");
            (string _, JObject payload) = await client.ExpectEitherAsync(Messages.MatchEvents, Messages.MatchRejected, TimeSpan.FromSeconds(20));
            me.Absorb(payload);
        }
    }

    /// <summary>Waits for a <c>match.events</c> batch containing an event that matches, optionally acting between batches.</summary>
    private static async Task<JObject> WaitForEventAsync(FakeClient client, Func<JToken, bool> predicate, TimeSpan budget,
        Func<Task>? act = null, MirrorPlayer? me = null)
    {
        DateTime deadline = DateTime.UtcNow + budget;
        while (DateTime.UtcNow < deadline)
        {
            if (act != null) await act();
            (string type, JObject payload) = await client.ExpectEitherAsync(Messages.MatchEvents, Messages.MatchRejected, TimeSpan.FromSeconds(20));
            me?.Absorb(payload);
            if (type == Messages.MatchEvents && Events(payload).Any(predicate)) return payload;
        }
        throw new XunitException($"{client.Name}: no matching event inside {budget.TotalSeconds:0} s");
    }

    private static void AssertEveryViewBelongsTo(FakeClient client, int seat)
    {
        int checkedViews = 0;
        foreach ((string type, JObject payload) in client.Received)
        {
            if (payload["view"] is not JObject view) continue;
            Assert.Equal(seat, view.Value<int>("viewer"));
            checkedViews++;
        }
        Assert.True(checkedViews > 3, $"{client.Name}: only {checkedViews} views to check");
    }
}
