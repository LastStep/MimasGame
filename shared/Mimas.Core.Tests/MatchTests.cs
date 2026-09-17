#nullable disable

using System.Collections.Generic;
using System.Linq;
using Mimas.Core.Bots;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Units;
using Xunit;

namespace Mimas.Core.Tests
{
    public class MatchStateTests
    {
        private static readonly Hex High = new Hex(-1, 0);
        private static readonly Hex Low = new Hex(2, 0);

        [Fact]
        public void Start_SpawnsBothUnitsFullHp_AndGivesFirstPlayerAp()
        {
            var catalog = CombatFixtures.Catalog();
            var state = new MatchState(catalog, CombatFixtures.Setup(), 7);
            Assert.Equal(0, state.TurnNumber);
            Assert.Equal(0, state.Units.Get(0).Ap);

            var events = state.Start();
            var started = Assert.IsType<TurnStartedEvent>(Assert.Single(events));
            Assert.Equal(0, started.Player);
            Assert.Equal(1, state.TurnNumber);
            Assert.Equal(3, state.Units.Get(0).Ap);
            Assert.Equal(0, state.Units.Get(1).Ap);
            Assert.Equal(12, state.Units.Get(0).Hp);
            Assert.Equal(state.MapData.SpawnP1, state.Units.Get(0).Position);
            Assert.Equal(state.MapData.SpawnP2, state.Units.Get(1).Position);
        }

        [Fact]
        public void Validate_RejectsOutOfTurn_WrongOwner_UnknownAbility()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            Assert.Equal(CommandRejectReason.NotYourTurn, state.Validate(new EndTurnCommand(1)).Reason);
            Assert.Equal(CommandRejectReason.NotYourUnit, state.Validate(new MoveCommand(0, 1, "move", new Hex(2, 0))).Reason);
            Assert.Equal(CommandRejectReason.UnknownUnit, state.Validate(new MoveCommand(0, 9, "move", new Hex(2, 0))).Reason);
            Assert.Equal(CommandRejectReason.UnknownAbility, state.Validate(new MoveCommand(0, 0, "fly", new Hex(-2, 0))).Reason);
            Assert.Equal(CommandRejectReason.UnitLacksAbility, state.Validate(new AttackCommand(0, 0, "strike", new Hex(3, 0))).Reason);
            Assert.Equal(CommandRejectReason.WrongAbilityType, state.Validate(new AttackCommand(0, 0, "move", new Hex(3, 0))).Reason);
            Assert.Equal(CommandRejectReason.WrongAbilityType, state.Validate(new MoveCommand(0, 0, "bow", new Hex(-2, 0))).Reason);
            Assert.Equal(MoveRejectReason.OutOfRange, state.Validate(new MoveCommand(0, 0, "move", new Hex(0, 0))).MoveReason);
            Assert.Equal(TargetRejectReason.OutOfRange, state.Validate(new AttackCommand(0, 0, "bow", new Hex(3, 0))).TargetReason);
            Assert.True(state.Validate(new MoveCommand(0, 0, "move", new Hex(-2, 0))).Ok);
        }

        [Fact]
        public void Move_SpendsAp_MovesUnit_RevealsAbilityToOpponent()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            var events = state.Apply(new MoveCommand(0, 0, "move", new Hex(-2, 0)));

            Assert.Equal(new Hex(-2, 0), state.Units.Get(0).Position);
            Assert.Equal(2, state.Units.Get(0).Ap);
            Assert.True(state.ActedThisTurn);
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(UnitMovedEvent), typeof(ApSpentEvent) }, events.Select(e => e.GetType()));
            Assert.Equal(1, ((AbilityRevealedEvent)events[0]).ToPlayer);
            Assert.True(state.Knows(1, 0, "move"));
            Assert.False(state.Knows(0, 0, "move"));   // the owner has nothing to learn

            // Using it again reveals nothing new.
            var again = state.Apply(new MoveCommand(0, 0, "move", new Hex(-1, 0)));
            Assert.DoesNotContain(again, e => e is AbilityRevealedEvent);
        }

        [Fact]
        public void Ap_IsTheOnlyLimit_ThreeStepsThenNothing()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            state.Apply(new MoveCommand(0, 0, "move", new Hex(-2, 0)));
            state.Apply(new MoveCommand(0, 0, "move", new Hex(-1, 0)));
            state.Apply(new MoveCommand(0, 0, "move", new Hex(0, 0)));
            Assert.Equal(0, state.Units.Get(0).Ap);
            Assert.Equal(CommandRejectReason.InsufficientAp, state.Validate(new MoveCommand(0, 0, "move", new Hex(1, 0))).Reason);

            var legal = new List<Command>();
            state.EnumerateLegal(0, legal);
            Assert.IsType<EndTurnCommand>(Assert.Single(legal));
        }

        [Fact]
        public void EndTurn_RefreshesOpponentAp_AndDropsUnspent()
        {
            var state = CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            var events = state.Apply(new EndTurnCommand(0));
            Assert.Equal(new[] { typeof(TurnEndedEvent), typeof(TurnStartedEvent) }, events.Select(e => e.GetType()));
            Assert.False(((TurnEndedEvent)events[0]).Acted);
            Assert.Equal(1, state.ActivePlayer);
            Assert.Equal(2, state.TurnNumber);
            Assert.Equal(0, state.Units.Get(0).Ap);
            Assert.Equal(3, state.Units.Get(1).Ap);

            var timeout = state.Apply(new EndTurnCommand(1, EndTurnReason.Timeout));
            Assert.Equal(EndTurnReason.Timeout, ((TurnEndedEvent)timeout[0]).Reason);
            Assert.Equal(0, state.ActivePlayer);
        }

        [Fact]
        public void Attack_DealsDamage_SpendsAp_RevealsHiddenWard()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, High, Low);

            var preview = state.PreviewAttack(0, 0, "bow", Low);
            Assert.Equal(6, preview.Total);

            var events = state.Apply(new AttackCommand(0, 0, "bow", Low));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(ModifierRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, events.Select(e => e.GetType()));

            var revealed = (ModifierRevealedEvent)events[1];
            Assert.Equal("ward", revealed.ModifierId);
            Assert.Equal(0, revealed.ToPlayer);
            Assert.True(state.Knows(0, 1, "ward"));

            var resolved = (AttackResolvedEvent)events[2];
            Assert.Equal(2, resolved.Damage);
            Assert.Equal(18, resolved.TargetHpAfter);
            Assert.Equal(18, state.Units.Get(1).Hp);
            Assert.Equal(1, state.Units.Get(0).Ap);

            // Only 1 ap left: the bow cannot be armed, so there is nothing to preview.
            Assert.Null(state.PreviewAttack(0, 0, "bow", Low));

            // Next turn the ward is known: the preview matches the truth.
            state.Apply(new EndTurnCommand(0));
            state.Apply(new EndTurnCommand(1));
            var after = state.PreviewAttack(0, 0, "bow", Low);
            Assert.NotNull(after.FindModifier("ward"));
            Assert.True(after.IsExact);
            Assert.Equal(2, after.Total);
        }

        [Fact]
        public void Attack_ThatKillsEndsTheMatch()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), High, Low);
            state.Units.Get(1).TakeDamage(19);     // 1 hp left
            var events = state.Apply(new AttackCommand(0, 0, "bow", Low));

            Assert.Contains(events, e => e is UnitDiedEvent);
            var ended = Assert.IsType<MatchEndedEvent>(events[events.Count - 1]);
            Assert.Equal(0, ended.Winner);
            Assert.True(state.IsOver);
            Assert.Equal(0, state.Winner);
            Assert.False(state.Units.Get(1).IsAlive);
            Assert.False(state.Units.IsOccupied(Low));
            Assert.Equal(CommandRejectReason.MatchOver, state.Validate(new EndTurnCommand(0)).Reason);

            var legal = new List<Command>();
            state.EnumerateLegal(0, legal);
            Assert.Empty(legal);
        }

        [Fact]
        public void EnumerateLegal_ListsMovesTargetsAndEndTurn_AndEveryEntryValidates()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), High, Low);
            var legal = new List<Command>();
            state.EnumerateLegal(0, legal);

            Assert.Equal(5, legal.OfType<MoveCommand>().Count());   // six neighbours, but the forest at (0,-1) costs 2
            var attack = Assert.Single(legal.OfType<AttackCommand>());
            Assert.Equal(Low, attack.Target);
            Assert.IsType<EndTurnCommand>(legal[legal.Count - 1]);
            foreach (var command in legal) Assert.True(state.Validate(command).Ok, command.ToString());

            state.EnumerateLegal(1, legal);
            Assert.Empty(legal);
        }

        [Fact]
        public void MoveOptions_AndAttackTargets_AreEmptyWhenUnaffordable()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), High, Low);
            var targets = new List<IBody>();

            Assert.Equal(5, state.MoveOptions(0, "move").Count);
            state.AttackTargets(0, "bow", targets);
            Assert.Single(targets);

            state.Apply(new MoveCommand(0, 0, "move", new Hex(-1, 1)));
            state.Apply(new MoveCommand(0, 0, "move", new Hex(-1, 0)));
            Assert.Equal(1, state.Units.Get(0).Ap);
            state.AttackTargets(0, "bow", targets);
            Assert.Empty(targets);
            Assert.Equal(0, state.MoveOptions(1, "move").Count);   // not their turn
        }
    }

    public class PlayerViewAndFilterTests
    {
        private static readonly Hex High = new Hex(-1, 0);
        private static readonly Hex Low = new Hex(2, 0);

        [Fact]
        public void View_HidesEnemyAbilitiesAndHiddenModifiers_UntilRevealed()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, High, Low);

            var view = state.ViewFor(0);
            Assert.True(view.IsMyTurn);
            Assert.Equal(1, view.TurnNumber);
            var mine = view.FindUnit(0);
            var theirs = view.FindUnit(1);

            Assert.True(mine.IsMine);
            Assert.All(mine.Abilities, a => Assert.True(a.Revealed));
            Assert.Equal(20, theirs.Hp);                                   // hp is public
            Assert.Equal(3, theirs.Abilities.Count);
            Assert.All(theirs.Abilities, a => Assert.False(a.Revealed));
            Assert.Equal(1, theirs.UnrevealedModifierCount);

            state.Apply(new AttackCommand(0, 0, "bow", Low));
            var after = state.ViewFor(0).FindUnit(1);
            Assert.Equal("ward", Assert.Single(after.Modifiers).Id);
            Assert.Equal(0, after.UnrevealedModifierCount);

            // The brute's owner always sees the ward, and has learned the archer's bow.
            var theirView = state.ViewFor(1);
            Assert.Equal("ward", theirView.FindUnit(1).Modifiers[0].Id);
            Assert.Equal(new[] { null, "bow" }, theirView.FindUnit(0).Abilities.Select(a => a.Id));
        }

        [Fact]
        public void Filter_RoutesRevealsToOnePlayer_AndKeepsPublicEvents()
        {
            var catalog = CombatFixtures.Catalog();
            var setup = CombatFixtures.Setup().WithModifier(1, "ward");
            var state = CombatFixtures.Started(catalog, setup, High, Low);
            var events = state.Apply(new AttackCommand(0, 0, "bow", Low));

            var forAttacker = new List<MatchEvent>();
            var forDefender = new List<MatchEvent>();
            EventFilter.ForPlayer(events, 0, state, forAttacker);
            EventFilter.ForPlayer(events, 1, state, forDefender);

            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(ModifierRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, events.Select(e => e.GetType()));
            Assert.Equal(new[] { typeof(ModifierRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, forAttacker.Select(e => e.GetType()));
            Assert.Equal(new[] { typeof(AbilityRevealedEvent), typeof(AttackResolvedEvent), typeof(ApSpentEvent) }, forDefender.Select(e => e.GetType()));

            // Both sides see the ward line: the attacker because it was just revealed, the defender because it is theirs.
            Assert.NotNull(forAttacker.OfType<AttackResolvedEvent>().Single().Breakdown.FindModifier("ward"));
            Assert.NotNull(forDefender.OfType<AttackResolvedEvent>().Single().Breakdown.FindModifier("ward"));
        }

        [Fact]
        public void Filter_StripsUnrevealedHiddenLines_FromABreakdown()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup().WithModifier(1, "ward"), High, Low);
            var breakdown = state.ResolveAttackFully(0, "bow", Low);
            var fake = new AttackResolvedEvent(0, 1, "bow", breakdown, breakdown.Total, 18);

            var trimmed = (AttackResolvedEvent)EventFilter.ForPlayer(fake, 0, state);
            Assert.Null(trimmed.Breakdown.FindModifier("ward"));
            Assert.Equal(1, trimmed.Breakdown.UnknownCount);
            Assert.Equal(6, trimmed.Breakdown.Total);
        }
    }


    /// <summary>
    /// Props inside a live match (spec A, D11/D12): where they come from, what they block, what happens when
    /// one falls, and what each player is told about them.
    /// </summary>
    public class MatchPropTests
    {
        private static readonly Hex PillarAt = new Hex(0, -1);       // props-3 fixture, prop id 4
        private static readonly Hex WallAt = new Hex(1, 0);          // props-3 fixture, prop id 3

        /// <summary>The props-3 fixture with the archer placed next to the south pillar.</summary>
        private static MatchState Props(Hex archerAt)
        {
            var state = new MatchState(CombatFixtures.Catalog(), new MatchSetup("props-3", CombatFixtures.Archer, CombatFixtures.Brute), 3);
            state.Units.Get(0).MoveTo(archerAt);
            state.Start();
            return state;
        }

        [Fact]
        public void Match_PropsCreatedFromMap_IdsAfterUnits()
        {
            var state = Props(new Hex(0, -2));
            Assert.Equal(4, state.Props.Count);
            Assert.Equal(new[] { 2, 3, 4, 5 }, state.Props.Select(p => p.Id).ToArray());
            Assert.Equal(new[] { "pillar", "wall", "pillar", "wall" }, state.Props.Select(p => p.Def.Id).ToArray());

            // Units first, then props in the map's authored hex order.
            Assert.Equal(new[] { 0, 1, 2, 3, 4, 5 }, state.Bodies.All.Select(b => b.Id).ToArray());
            Assert.Equal(-1, state.Props[0].Owner);
        }

        [Fact]
        public void Match_PropOccupiesTile_MoveRejected()
        {
            var state = Props(new Hex(0, -2));
            var result = state.Validate(new MoveCommand(0, 0, "move", PillarAt));
            Assert.Equal(MoveRejectReason.Occupied, result.MoveReason);
            Assert.True(state.Bodies.IsOccupied(PillarAt));
        }

        [Fact]
        public void Match_AttackOnPillar_ResolvedEventFlagsProp()
        {
            var state = Props(new Hex(0, -2));
            var events = state.Apply(new AttackCommand(0, 0, "bow", PillarAt));
            var resolved = events.OfType<AttackResolvedEvent>().Single();

            Assert.True(resolved.TargetIsProp);
            Assert.Equal(4, resolved.TargetId);
            Assert.Equal(7, resolved.Damage);                        // 5 base + 2 power, no armour
            Assert.Equal(3, resolved.TargetHpAfter);
            Assert.DoesNotContain(events, e => e is PropDestroyedEvent);
        }

        [Fact]
        public void Match_DestroyedPillar_TileEnterable_EventEmitted()
        {
            var state = Props(new Hex(0, -2));
            state.Apply(new AttackCommand(0, 0, "bow", PillarAt));    // 7 of 10
            state.Apply(new EndTurnCommand(0));
            state.Apply(new EndTurnCommand(1));
            var events = state.Apply(new AttackCommand(0, 0, "bow", PillarAt));

            Assert.Equal(4, Assert.IsType<PropDestroyedEvent>(events.Single(e => e is PropDestroyedEvent)).PropId);
            Assert.True(state.Props.Single(p => p.Id == 4).IsDestroyed);

            // Gone from the board: nothing reports it and the tile can be walked into.
            Assert.False(state.Bodies.IsOccupied(PillarAt));
            Assert.False(state.Bodies.TryGetBodyAt(PillarAt, out _));
            Assert.True(state.Validate(new MoveCommand(0, 0, "move", PillarAt)).Ok);
        }

        [Fact]
        public void Match_DestroyingPillar_DoesNotEndMatch()
        {
            var state = Props(new Hex(0, -2));
            state.Apply(new AttackCommand(0, 0, "bow", PillarAt));
            state.Apply(new EndTurnCommand(0));
            state.Apply(new EndTurnCommand(1));
            state.Apply(new AttackCommand(0, 0, "bow", PillarAt));

            Assert.False(state.IsOver);
            Assert.Equal(-1, state.Winner);
            Assert.True(state.HasLivingUnit(0));
            Assert.True(state.HasLivingUnit(1));
        }

        [Fact]
        public void EnumerateLegal_IncludesPillarTargets()
        {
            var state = Props(new Hex(0, -2));
            var legal = new List<Command>();
            state.EnumerateLegal(0, legal);

            var targets = legal.OfType<AttackCommand>().Select(a => a.Target).ToList();
            Assert.Contains(PillarAt, targets);
            Assert.DoesNotContain(WallAt, targets);                  // a wall blocks but cannot be hit
            foreach (var command in legal) Assert.True(state.Validate(command).Ok, command.ToString());
        }

        [Fact]
        public void PlayerView_ListsPropsForBothViewers()
        {
            var state = Props(new Hex(0, -2));
            foreach (int viewer in new[] { 0, 1 })
            {
                var view = state.ViewFor(viewer);
                Assert.Equal(4, view.Props.Count);
                var pillar = view.FindProp(4);
                Assert.Equal("pillar", pillar.DefId);
                Assert.Equal(PillarAt, pillar.Position);
                Assert.Equal(10, pillar.Hp);
                Assert.True(pillar.IsDamageable);
                Assert.False(view.FindProp(3).IsDamageable);          // the wall
                Assert.Equal(6, view.FindProp(3).BodyHeight);
            }

            state.Apply(new AttackCommand(0, 0, "bow", PillarAt));
            state.Apply(new EndTurnCommand(0));
            state.Apply(new EndTurnCommand(1));
            state.Apply(new AttackCommand(0, 0, "bow", PillarAt));
            Assert.Equal(3, state.ViewFor(1).Props.Count);            // the destroyed pillar is simply gone
            Assert.Null(state.ViewFor(1).FindProp(4));
        }

        [Fact]
        public void PlayerView_UnitViewCarriesHeights()
        {
            var state = Props(new Hex(0, -2));
            var view = state.ViewFor(0);
            Assert.Equal(6, view.FindUnit(0).BodyHeight);
            Assert.Equal(4, view.FindUnit(0).AimHeight);
            Assert.Equal(6, view.FindUnit(1).BodyHeight);
        }

        [Fact]
        public void RangeBand_ReturnsTilesInsideCircle_OrderStable()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = new MatchState(catalog, new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.BowKit), 1);
            state.Units.Get(0).MoveTo(Hex.Zero);
            state.Start();

            var band = new List<Hex>();
            state.RangeBand(0, "arcane-spark", band);                 // range 2, so 1..4 squared
            Assert.Equal(18, band.Count);
            Assert.All(band, h => Assert.InRange(Hex.EuclideanSquared(Hex.Zero, h), 1, 4));

            var again = new List<Hex>();
            state.RangeBand(0, "arcane-spark", again);
            Assert.Equal(band, again);

            // Authored map order, not a dictionary walk.
            var authored = state.MapData.Hexes.Select(h => h.Position).Where(band.Contains).ToList();
            Assert.Equal(authored, band);
        }

        [Fact]
        public void Match_TeleportSight_UsesRay_IgnoresMover()
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = new MatchState(catalog, new MatchSetup("arena-4", ContentFixtures.GunKit, ContentFixtures.GunKit), 1);
            var mover = state.Units.Get(0);
            var other = state.Units.Get(1);

            // Across the level-2 plateau: the ray is stopped even though the tiles are only two apart.
            mover.MoveTo(new Hex(0, -1));
            other.MoveTo(new Hex(3, 0));
            state.Start();
            Assert.Equal(MoveRejectReason.NoLineOfSight, state.Validate(new MoveCommand(0, 0, "teleport", new Hex(0, 1))).MoveReason);

            // Along a clear corridor: legal, and the mover's own body at the origin never blocks it.
            mover.MoveTo(new Hex(-4, 3));
            Assert.True(state.Validate(new MoveCommand(0, 0, "teleport", new Hex(-1, 3))).Ok);

            // The other hero standing in that corridor does block it.
            other.MoveTo(new Hex(-3, 3));
            Assert.Equal(MoveRejectReason.NoLineOfSight, state.Validate(new MoveCommand(0, 0, "teleport", new Hex(-1, 3))).MoveReason);
        }

        [Fact]
        public void Bot_GamesOnArena4WithProps_InvariantsHold_SameSeedSameLog()
        {
            var first = PlayOnArena(11, 400);
            var second = PlayOnArena(11, 400);
            Assert.Equal(first, second);
            Assert.NotEmpty(first);
        }

        /// <summary>Plays random bots on arena-4 and returns the command log, checking the invariants each step.</summary>
        private static List<string> PlayOnArena(uint seed, int maxCommands)
        {
            var catalog = ContentFixtures.RepoCatalog();
            var state = new MatchState(catalog, new MatchSetup("arena-4", ContentFixtures.BowKit, ContentFixtures.GunKit), seed);
            state.Start();
            var bots = new IBot[] { new RandomBot(seed * 2 + 1), new RandomBot(seed * 2 + 2) };
            var log = new List<string>();
            var legal = new List<Command>();

            while (!state.IsOver && log.Count < maxCommands)
            {
                Command command = bots[state.ActivePlayer].Choose(state, state.ActivePlayer);
                Assert.NotNull(command);
                Assert.True(state.Validate(command).Ok, command.ToString());
                state.Apply(command);
                log.Add(command.ToString());

                foreach (var unit in state.Units.All)
                {
                    Assert.InRange(unit.Ap, 0, unit.ApPerTurn);
                    Assert.InRange(unit.Hp, 0, unit.MaxHp);
                    Assert.True(state.Map.Contains(unit.Position));
                }
                foreach (var prop in state.Props)
                {
                    Assert.InRange(prop.Hp, 0, prop.MaxHp);
                    // A destroyed prop's tile is free again and no body reports it.
                    if (!prop.IsDestroyed) continue;
                    IBody there;
                    Assert.False(state.Bodies.TryGetBodyAt(prop.Position, out there) && ReferenceEquals(there, prop));
                }
                var standing = state.Bodies.All.Where(b => b.IsAlive).Select(b => b.Position).ToList();
                Assert.Equal(standing.Count, standing.Distinct().Count());

                state.EnumerateLegal(state.ActivePlayer, legal);
                foreach (var c in legal) Assert.True(state.Validate(c).Ok, c.ToString());
            }
            return log;
        }
    }

    public class BotAndDeterminismTests
    {
        private static (MatchState state, int commands) PlayOut(uint seed, int maxCommands)
        {
            var catalog = CombatFixtures.Catalog();
            var state = new MatchState(catalog, CombatFixtures.Setup(), seed);
            state.Start();
            var bots = new IBot[] { new RandomBot(seed * 2 + 1), new RandomBot(seed * 2 + 2) };
            var legal = new List<Command>();
            int count = 0;
            while (!state.IsOver && count < maxCommands)
            {
                Command command = bots[state.ActivePlayer].Choose(state, state.ActivePlayer);
                Assert.NotNull(command);
                Assert.True(state.Validate(command).Ok, command.ToString());
                state.Apply(command);
                count++;

                // Invariants after every command.
                foreach (var unit in state.Units.All)
                {
                    Assert.InRange(unit.Ap, 0, unit.ApPerTurn);
                    Assert.InRange(unit.Hp, 0, unit.MaxHp);
                    Assert.True(state.Map.Contains(unit.Position));
                }
                var living = state.Units.All.Where(u => u.IsAlive).Select(u => u.Position).ToList();
                Assert.Equal(living.Count, living.Distinct().Count());
                state.EnumerateLegal(state.ActivePlayer, legal);
                foreach (var c in legal) Assert.True(state.Validate(c).Ok, c.ToString());
            }
            return (state, count);
        }

        [Fact]
        public void RandomBots_PlayLegalGames_ThatEndByElimination()
        {
            int finished = 0;
            for (uint seed = 1; seed <= 8; seed++)
            {
                var (state, _) = PlayOut(seed, 4000);
                if (state.IsOver)
                {
                    finished++;
                    Assert.False(state.HasLivingUnit(1 - state.Winner));
                }
            }
            Assert.True(finished >= 1, "no random game finished within the command budget");
        }

        [Fact]
        public void SameSeedAndCommands_ProduceIdenticalStates()
        {
            var a = PlayOut(42, 500);
            var b = PlayOut(42, 500);
            Assert.Equal(a.commands, b.commands);
            Assert.Equal(a.state.TurnNumber, b.state.TurnNumber);
            Assert.Equal(a.state.IsOver, b.state.IsOver);
            foreach (var unit in a.state.Units.All)
            {
                var twin = b.state.Units.Get(unit.Id);
                Assert.Equal(unit.Position, twin.Position);
                Assert.Equal(unit.Hp, twin.Hp);
                Assert.Equal(unit.Ap, twin.Ap);
            }
        }

        [Fact]
        public void RandomBot_OnlyEndsTurnWhenNothingElseIsLegal()
        {
            var catalog = CombatFixtures.Catalog();
            var state = CombatFixtures.Started(catalog, CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));
            var bot = new RandomBot(5);
            for (int i = 0; i < 3; i++)
            {
                var command = bot.Choose(state, 0);
                Assert.IsNotType<EndTurnCommand>(command);
                state.Apply(command);
            }
            Assert.IsType<EndTurnCommand>(bot.Choose(state, 0));
            Assert.Null(bot.Choose(state, 1));
        }
    }
    /// <summary>
    /// Resigning (design: #win-conditions, #online). A concession is a command like any other, so the
    /// server can submit one for a player who never came back and a replay of the command list still
    /// reproduces the match. It is the one command that is legal off turn, and the one a bot never sees.
    /// </summary>
    public class ResignTests
    {
        private static MatchState Started() =>
            CombatFixtures.Started(CombatFixtures.Catalog(), CombatFixtures.Setup(), new Hex(-3, 0), new Hex(3, 0));

        [Fact]
        public void Resign_OnOwnTurn_EndsMatchOpponentWins()
        {
            var state = Started();
            Assert.Equal(0, state.ActivePlayer);

            var events = state.Apply(new ResignCommand(0));

            var ended = Assert.IsType<MatchEndedEvent>(Assert.Single(events));
            Assert.Equal(1, ended.Winner);
            Assert.Equal(MatchEndReason.Resign, ended.Reason);
            Assert.True(state.IsOver);
            Assert.Equal(1, state.Winner);
            Assert.Equal(3, state.Units.Get(0).Ap);      // no turn change, no points spent
            Assert.Equal(1, state.TurnNumber);
        }

        [Fact]
        public void Resign_OffTurn_IsLegal()
        {
            var state = Started();
            Assert.Equal(0, state.ActivePlayer);

            Assert.True(state.Validate(new ResignCommand(1)).Ok);
            var ended = Assert.IsType<MatchEndedEvent>(Assert.Single(state.Apply(new ResignCommand(1))));
            Assert.Equal(0, ended.Winner);
            Assert.Equal(MatchEndReason.Resign, ended.Reason);
        }

        [Fact]
        public void Resign_AfterMatchOver_Rejected()
        {
            var state = Started();
            state.Apply(new ResignCommand(0));

            Assert.Equal(CommandRejectReason.MatchOver, state.Validate(new ResignCommand(1)).Reason);
            Assert.Equal(CommandRejectReason.MatchOver, state.Validate(new ResignCommand(0)).Reason);
        }

        [Fact]
        public void Resign_Disconnect_EndsWithForfeit()
        {
            var state = Started();
            var ended = Assert.IsType<MatchEndedEvent>(Assert.Single(state.Apply(new ResignCommand(1, ResignReason.Disconnect))));
            Assert.Equal(MatchEndReason.Forfeit, ended.Reason);
            Assert.Equal(0, ended.Winner);
            Assert.Equal("P1 resigns (Disconnect)", new ResignCommand(1, ResignReason.Disconnect).ToString());
        }

        [Fact]
        public void EnumerateLegal_NeverContainsResign()
        {
            var state = Started();
            var commands = new List<Command>();
            for (int turn = 0; turn < 8 && !state.IsOver; turn++)
            {
                state.EnumerateLegal(state.ActivePlayer, commands);
                Assert.NotEmpty(commands);
                Assert.DoesNotContain(commands, c => c is ResignCommand);
                state.Apply(new EndTurnCommand(state.ActivePlayer));
            }
        }
    }
}
