using System;
using System.Collections.Generic;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Movement;
using Mimas.Core.Units;

namespace Mimas.Core.Match
{
    /// <summary>Why a command was refused. <see cref="None"/> means it was accepted.</summary>
    public enum CommandRejectReason
    {
        None = 0,
        MatchOver,
        NotYourTurn,
        UnknownUnit,
        NotYourUnit,
        UnitDead,
        UnknownAbility,
        UnitLacksAbility,
        WrongAbilityType,
        InsufficientAp,
        IllegalMove,
        IllegalTarget,
    }

    /// <summary>Outcome of <see cref="MatchState.Validate"/>. Carries the finer reason for moves and targets so UI and bots can explain.</summary>
    public readonly struct CommandResult
    {
        public readonly CommandRejectReason Reason;
        public readonly MoveRejectReason MoveReason;
        public readonly TargetRejectReason TargetReason;

        public bool Ok => Reason == CommandRejectReason.None;

        private CommandResult(CommandRejectReason reason, MoveRejectReason moveReason, TargetRejectReason targetReason)
        {
            Reason = reason;
            MoveReason = moveReason;
            TargetReason = targetReason;
        }

        public static readonly CommandResult Accepted = new CommandResult(CommandRejectReason.None, MoveRejectReason.None, TargetRejectReason.None);

        public static CommandResult Reject(CommandRejectReason reason) => new CommandResult(reason, MoveRejectReason.None, TargetRejectReason.None);

        public static CommandResult RejectMove(MoveRejectReason reason) => new CommandResult(CommandRejectReason.IllegalMove, reason, TargetRejectReason.None);

        public static CommandResult RejectTarget(TargetRejectReason reason) => new CommandResult(CommandRejectReason.IllegalTarget, MoveRejectReason.None, reason);

        public override string ToString()
        {
            if (Ok) return "Ok";
            if (Reason == CommandRejectReason.IllegalMove) return "IllegalMove:" + MoveReason;
            if (Reason == CommandRejectReason.IllegalTarget) return "IllegalTarget:" + TargetReason;
            return Reason.ToString();
        }
    }

    /// <summary>
    /// The full truth of one map round: board, units, whose turn it is, what each player has learned about
    /// the other's hidden things, and the match RNG. <see cref="Validate"/> is pure; <see cref="Apply"/> is
    /// the only mutator and returns the events that describe exactly what changed; <see cref="EnumerateLegal"/>
    /// generates candidates structurally and filters them through the same validation, so the bot, the HUD
    /// highlight and the server can never disagree. Never send this object to a client: project it with
    /// <see cref="ViewFor"/> and filter events with <see cref="EventFilter"/>.
    /// <para>
    /// A <b>mirror</b> (<see cref="FromView"/>, <see cref="IsMirror"/>, ADR-026) is the client's copy: the same
    /// class built from one player's <see cref="PlayerView"/> and nothing else, so every preview question —
    /// move options, range bands, <see cref="CheckTarget"/>, the damage preview and its "?" row — is answered
    /// by the same code the server runs, instantly and without a round trip, and cannot see a thing the server
    /// did not choose to send. A mirror only answers questions: it never starts or applies anything.
    /// </para>
    /// </summary>
    public sealed class MatchState : IRevealedKnowledge
    {
        private readonly MovementResolverRegistry _resolvers;
        private readonly DamageCalculator _damage;
        private readonly RevealedSet _revealed = new RevealedSet();
        private readonly List<IBody> _targetScratch = new List<IBody>();

        public ContentCatalog Catalog { get; }
        public MatchSetup Setup { get; }
        public MapData MapData { get; }
        public TileMap Map { get; }
        public UnitSet Units { get; }

        /// <summary>Every body on the board: the units plus the props the map placed (design: #props).</summary>
        public BodySet Bodies { get; }

        /// <summary>The props this map placed, in authored order. Public information, for both players.</summary>
        public IReadOnlyList<Prop> Props => Bodies.Props;

        /// <summary>Which trajectories exist. One registry per match so a mod could add one.</summary>
        public TrajectoryRegistry Trajectories { get; }

        public Rng Rng { get; }

        /// <summary>
        /// True when this is a client-side mirror built by <see cref="FromView"/> rather than the truth: it
        /// holds only what one player was told, and it refuses to be advanced (ADR-026).
        /// </summary>
        public bool IsMirror { get; }

        /// <summary>The only player a mirror may answer for; -1 on the truth.</summary>
        public int MirrorViewer { get; }

        /// <summary>Player whose turn it is.</summary>
        public int ActivePlayer { get; private set; }

        /// <summary>1-based, counts every turn of both players. 0 before the first turn starts.</summary>
        public int TurnNumber { get; private set; }

        /// <summary>True once a move or attack happened this turn (a timeout with nothing done is a skip).</summary>
        public bool ActedThisTurn { get; private set; }

        public bool IsOver { get; private set; }

        /// <summary>Winning player, or -1 while the match runs.</summary>
        public int Winner { get; private set; } = -1;

        /// <summary>The board, the registries and the RNG: everything the truth and a mirror set up the same way.</summary>
        private MatchState(ContentCatalog catalog, MatchSetup setup, uint seed,
            MovementResolverRegistry resolvers, TrajectoryRegistry trajectories, int mirrorViewer)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _resolvers = resolvers ?? MovementResolverRegistry.CreateDefault();
            Trajectories = trajectories ?? TrajectoryRegistry.Default();
            _damage = new DamageCalculator(catalog);
            Rng = new Rng(seed);

            MapData = catalog.Maps.Get(setup.MapId);
            Map = MapData.BuildTileMap(catalog.Terrains);
            Units = new UnitSet();
            Bodies = new BodySet(Units);

            IsMirror = mirrorViewer >= 0;
            MirrorViewer = mirrorViewer;
        }

        /// <summary>Builds the board, spawns one unit per player on its spawn, and starts the first turn (events are returned by <see cref="Start"/>).</summary>
        public MatchState(ContentCatalog catalog, MatchSetup setup, uint seed,
            MovementResolverRegistry resolvers = null, TrajectoryRegistry trajectories = null)
            : this(catalog, setup, seed, resolvers, trajectories, -1)
        {
            for (int player = 0; player < MatchSetup.PlayerCount; player++)
            {
                Loadout loadout = setup.LoadoutOf(player);
                var items = new List<ItemDef>(ItemSlots.All.Length);
                for (int s = 0; s < ItemSlots.All.Length; s++)
                    items.Add(catalog.GetItemForSlot(ItemSlots.All[s], loadout.IdForSlot(ItemSlots.All[s])));
                var unit = new Unit(player, player, player == 0 ? MapData.SpawnP1 : MapData.SpawnP2, catalog.Rules, items);
                IReadOnlyList<string> modifiers = setup.ModifierIdsOf(player);
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (!catalog.Modifiers.Contains(modifiers[i]))
                        throw new ArgumentException($"Unknown modifier '{modifiers[i]}' for player {player}.", nameof(setup));
                    unit.AddModifier(modifiers[i]);
                }
                Units.Add(unit);
            }

            // Props come after the units, in the map's authored hex order, so their ids are stable.
            int nextId = 0;
            for (int i = 0; i < Units.All.Count; i++)
                if (Units.All[i].Id >= nextId) nextId = Units.All[i].Id + 1;
            foreach (MapHex hex in MapData.Hexes)
            {
                if (hex.PropId == null) continue;
                Bodies.AddProp(new Prop(nextId++, catalog.Props.Get(hex.PropId), hex.Position));
            }

            ActivePlayer = setup.FirstPlayer;
            TurnNumber = 0;
        }

        /// <summary>
        /// The client's mirror (ADR-026): a <see cref="MatchState"/> rebuilt from one player's
        /// <see cref="PlayerView"/> and nothing else. Every number on it is one the server chose to send, so
        /// asking it a question can never leak; and because it is the same class, every preview the local game
        /// already answers is answered identically online, with no second implementation to drift.
        /// <para>
        /// Rebuilt per message rather than advanced by events: a view is two heroes and a handful of props, and
        /// a state that is only ever replaced cannot fall out of step with the server.
        /// </para>
        /// <para>
        /// <see cref="Setup"/>'s <c>FirstPlayer</c> is meaningless on a mirror (a view does not say who started)
        /// and <see cref="Rng"/> is seeded 0 and never drawn from — a mirror decides nothing.
        /// </para>
        /// </summary>
        public static MatchState FromView(ContentCatalog catalog, PlayerView view,
            MovementResolverRegistry resolvers = null, TrajectoryRegistry trajectories = null)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (view == null) throw new ArgumentNullException(nameof(view));
            if (view.Units.Count != MatchSetup.PlayerCount)
                throw new ArgumentException($"A view of a 1v1 needs {MatchSetup.PlayerCount} units, had {view.Units.Count}.", nameof(view));

            var loadouts = new Loadout[MatchSetup.PlayerCount];
            for (int i = 0; i < view.Units.Count; i++)
            {
                UnitView u = view.Units[i];
                if (u.Owner < 0 || u.Owner >= MatchSetup.PlayerCount)
                    throw new ArgumentException($"Unit {u.Id} has owner {u.Owner}.", nameof(view));
                if (u.ItemIds.Count != ItemSlots.All.Length)
                    throw new ArgumentException($"Unit {u.Id} lists {u.ItemIds.Count} items.", nameof(view));
                loadouts[u.Owner] = new Loadout(u.ItemIds[0], u.ItemIds[1], u.ItemIds[2], u.ItemIds[3]);
            }
            for (int player = 0; player < MatchSetup.PlayerCount; player++)
                if (loadouts[player] == null) throw new ArgumentException($"The view has no unit for player {player}.", nameof(view));

            var setup = new MatchSetup(view.MapId, loadouts[0], loadouts[1]);
            var state = new MatchState(catalog, setup, 0, resolvers, trajectories, view.Viewer);

            for (int i = 0; i < view.Units.Count; i++)
                state.Units.Add(Unit.FromView(view.Units[i], catalog));

            // Destroyed props are simply absent from a view, so the mirror's board has the holes the player
            // can see and walk into.
            for (int i = 0; i < view.Props.Count; i++)
            {
                PropView p = view.Props[i];
                var prop = new Prop(p.Id, catalog.Props.Get(p.DefId), p.Position);
                prop.Restore(p.Hp);
                state.Bodies.AddProp(prop);
            }

            // Everything the viewer has been shown about the opponent is knowledge they keep: without this the
            // mirror would re-hide a revealed ability the moment it rebuilt.
            for (int i = 0; i < view.Units.Count; i++)
            {
                UnitView u = view.Units[i];
                if (u.Owner == view.Viewer) continue;
                for (int a = 0; a < u.Abilities.Count; a++)
                    if (u.Abilities[a].Revealed) state._revealed.Add(view.Viewer, u.Id, u.Abilities[a].Id);
                for (int m = 0; m < u.Modifiers.Count; m++)
                    if (u.Modifiers[m].Revealed) state._revealed.Add(view.Viewer, u.Id, u.Modifiers[m].Id);
            }

            state.ActivePlayer = view.ActivePlayer;
            state.TurnNumber = view.TurnNumber;
            state.ActedThisTurn = view.ActedThisTurn;
            state.IsOver = view.IsOver;
            state.Winner = view.Winner;
            return state;
        }

        private void RefuseOnMirror(string what)
        {
            if (IsMirror) throw new InvalidOperationException($"A mirror only answers questions ({what}).");
        }

        /// <summary>Starts the first turn. Call exactly once after construction.</summary>
        public IReadOnlyList<MatchEvent> Start()
        {
            RefuseOnMirror("Start");
            if (TurnNumber != 0) throw new InvalidOperationException("The match has already started.");
            var events = new List<MatchEvent>();
            BeginTurn(ActivePlayer, events);
            return events;
        }

        // ---- IRevealedKnowledge ---------------------------------------------------------------------

        public bool Knows(int viewer, int unitId, string id) => _revealed.Contains(viewer, unitId, id);

        // ---- validation ----------------------------------------------------------------------------

        public CommandResult Validate(Command command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            if (IsOver) return CommandResult.Reject(CommandRejectReason.MatchOver);
            if (TurnNumber == 0) return CommandResult.Reject(CommandRejectReason.NotYourTurn);

            // Resigning is not a turn action: you may concede while the other player is thinking.
            if (command is ResignCommand) return CommandResult.Accepted;

            if (command.Player != ActivePlayer) return CommandResult.Reject(CommandRejectReason.NotYourTurn);

            switch (command)
            {
                case MoveCommand move: return ValidateMove(move, out _);
                case AttackCommand attack: return ValidateAttack(attack, out _, out _, out _);
                case EndTurnCommand _: return CommandResult.Accepted;
                default: throw new ArgumentException("Unknown command type " + command.GetType().Name, nameof(command));
            }
        }

        private CommandResult ValidateActor(int player, int unitId, string abilityId, out Unit unit, out AbilityDef ability)
        {
            ability = null;
            if (!Units.TryGet(unitId, out unit)) return CommandResult.Reject(CommandRejectReason.UnknownUnit);
            if (unit.Owner != player) return CommandResult.Reject(CommandRejectReason.NotYourUnit);
            if (!unit.IsAlive) return CommandResult.Reject(CommandRejectReason.UnitDead);
            if (!Catalog.Abilities.TryGet(abilityId, out ability)) return CommandResult.Reject(CommandRejectReason.UnknownAbility);
            if (!unit.HasAbility(abilityId)) return CommandResult.Reject(CommandRejectReason.UnitLacksAbility);
            if (!unit.CanAfford(ability.Cost)) return CommandResult.Reject(CommandRejectReason.InsufficientAp);
            return CommandResult.Accepted;
        }

        private CommandResult ValidateMove(MoveCommand move, out MoveResult result)
        {
            result = default;
            Unit unit; AbilityDef ability;
            CommandResult actor = ValidateActor(move.Player, move.UnitId, move.AbilityId, out unit, out ability);
            if (!actor.Ok) return actor;
            var movement = ability as MovementDef;
            if (movement == null) return CommandResult.Reject(CommandRejectReason.WrongAbilityType);
            result = _resolvers.Validate(MoveContext(unit, movement), move.Destination);
            return result.Ok ? CommandResult.Accepted : CommandResult.RejectMove(result.Reason);
        }

        private CommandResult ValidateAttack(AttackCommand attack, out Unit attacker, out AttackDef def, out IBody victim)
        {
            attacker = null; def = null; victim = null;
            AbilityDef ability;
            CommandResult actor = ValidateActor(attack.Player, attack.UnitId, attack.AbilityId, out attacker, out ability);
            if (!actor.Ok) return actor;
            def = ability as AttackDef;
            if (def == null) return CommandResult.Reject(CommandRejectReason.WrongAbilityType);
            TargetCheck target = AttackTargeting.Check(Map, Bodies, Catalog.Rules.Heights, Trajectories, attacker, def, attack.Target);
            victim = target.Victim;
            return target.Ok ? CommandResult.Accepted : CommandResult.RejectTarget(target.Reason);
        }

        /// <summary>The movement context every resolver call goes through: bodies, heights and the mover's id.</summary>
        private MovementContext MoveContext(Unit unit, MovementDef movement)
            => MovementContext.For(Map, Catalog.Terrains, Bodies, unit, movement, Catalog.Rules.Heights);

        // ---- application ---------------------------------------------------------------------------

        /// <summary>Applies a command. Throws <see cref="InvalidOperationException"/> when it does not validate; call <see cref="Validate"/> first.</summary>
        public IReadOnlyList<MatchEvent> Apply(Command command)
        {
            RefuseOnMirror("Apply");
            CommandResult check = Validate(command);
            if (!check.Ok) throw new InvalidOperationException($"Command {command} rejected: {check}");

            var events = new List<MatchEvent>();
            switch (command)
            {
                case MoveCommand move: ApplyMove(move, events); break;
                case AttackCommand attack: ApplyAttack(attack, events); break;
                case EndTurnCommand end: ApplyEndTurn(end, events); break;
                case ResignCommand resign: ApplyResign(resign, events); break;
            }
            return events;
        }

        /// <summary>Validates then applies; returns false (no events, no change) when the command is illegal.</summary>
        public bool TryApply(Command command, List<MatchEvent> into, out CommandResult result)
        {
            RefuseOnMirror("TryApply");
            if (into == null) throw new ArgumentNullException(nameof(into));
            result = Validate(command);
            if (!result.Ok) return false;
            into.AddRange(Apply(command));
            return true;
        }

        private void ApplyMove(MoveCommand move, List<MatchEvent> events)
        {
            MoveResult result;
            ValidateMove(move, out result);
            Unit unit = Units.Get(move.UnitId);
            MovementDef movement = Catalog.GetMovement(move.AbilityId);

            unit.SpendAp(movement.Cost);
            unit.MoveTo(result.Plan.Destination);
            ActedThisTurn = true;

            RevealAbility(unit, movement.Id, events);
            events.Add(new UnitMovedEvent(unit.Id, movement.Id, result.Plan));
            events.Add(new ApSpentEvent(unit.Id, movement.Id, movement.Cost, unit.Ap));
        }

        private void ApplyAttack(AttackCommand attack, List<MatchEvent> events)
        {
            Unit attacker; IBody victim; AttackDef def;
            ValidateAttack(attack, out attacker, out def, out victim);

            attacker.SpendAp(def.Cost);
            ActedThisTurn = true;
            RevealAbility(attacker, def.Id, events);

            DamageBreakdown breakdown = _damage.Compute(Map, attacker, victim, def, Knowledge.Full);

            // Any hidden line that changed the number is now known to the other side.
            for (int i = 0; i < breakdown.Lines.Count; i++)
            {
                DamageLine line = breakdown.Lines[i];
                if (!line.Hidden || line.Amount == 0) continue;
                IBody body;
                // Props carry no hidden modifiers; the guard keeps the reveal honest if one ever does.
                if (!Bodies.TryGetBody(line.OwnerUnitId, out body) || !(body is Unit owner)) continue;
                int other = 1 - owner.Owner;
                if (_revealed.Add(other, owner.Id, line.Id))
                    events.Add(new ModifierRevealedEvent(owner.Id, line.Id, other));
            }

            int lost = victim.TakeDamage(breakdown.Total);
            var prop = victim as Prop;
            events.Add(new AttackResolvedEvent(attacker.Id, victim.Id, def.Id, breakdown, lost, victim.Hp, prop != null));
            events.Add(new ApSpentEvent(attacker.Id, def.Id, def.Cost, attacker.Ap));

            if (victim.IsAlive) return;
            if (prop != null)
            {
                // A destroyed prop is simply gone: its tile is enterable again and nobody was eliminated (D11).
                events.Add(new PropDestroyedEvent(prop.Id));
                return;
            }
            events.Add(new UnitDiedEvent(victim.Id));
            CheckElimination(events);
        }

        /// <summary>A concession ends the match where it stands: no turn change, no action points spent.</summary>
        private void ApplyResign(ResignCommand resign, List<MatchEvent> events)
        {
            IsOver = true;
            Winner = 1 - resign.Player;
            events.Add(new MatchEndedEvent(Winner, resign.Reason == ResignReason.Player ? MatchEndReason.Resign : MatchEndReason.Forfeit));
        }

        private void ApplyEndTurn(EndTurnCommand end, List<MatchEvent> events)
        {
            EndTurn(end.Reason, events);
            if (!IsOver) BeginTurn(1 - ActivePlayer, events);
        }

        private void BeginTurn(int player, List<MatchEvent> events)
        {
            ActivePlayer = player;
            TurnNumber++;
            ActedThisTurn = false;
            for (int i = 0; i < Units.All.Count; i++)
            {
                Unit unit = Units.All[i];
                if (unit.Owner == player && unit.IsAlive) unit.RefreshAp();
            }
            events.Add(new TurnStartedEvent(player, TurnNumber));
        }

        private void EndTurn(EndTurnReason reason, List<MatchEvent> events)
        {
            for (int i = 0; i < Units.All.Count; i++)
            {
                Unit unit = Units.All[i];
                if (unit.Owner == ActivePlayer) unit.ClearAp();
            }
            events.Add(new TurnEndedEvent(ActivePlayer, TurnNumber, reason, ActedThisTurn));
        }

        private void RevealAbility(Unit unit, string abilityId, List<MatchEvent> events)
        {
            int other = 1 - unit.Owner;
            if (_revealed.Add(other, unit.Id, abilityId))
                events.Add(new AbilityRevealedEvent(unit.Id, abilityId, other));
        }

        private void CheckElimination(List<MatchEvent> events)
        {
            for (int player = 0; player < MatchSetup.PlayerCount; player++)
            {
                if (HasLivingUnit(player)) continue;
                IsOver = true;
                Winner = 1 - player;
                events.Add(new MatchEndedEvent(Winner, MatchEndReason.Elimination));
                return;
            }
        }

        public bool HasLivingUnit(int player)
        {
            for (int i = 0; i < Units.All.Count; i++)
                if (Units.All[i].Owner == player && Units.All[i].IsAlive) return true;
            return false;
        }

        // ---- queries for UI, bots and previews -------------------------------------------------------

        /// <summary>
        /// Every legal command for <paramref name="player"/> right now, in a deterministic order: for each
        /// living unit, for each ability in grant order, each destination or target; then End Turn. Empty
        /// when it is not that player's turn or the match is over.
        /// </summary>
        public void EnumerateLegal(int player, List<Command> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            if (IsOver || TurnNumber == 0 || player != ActivePlayer) return;

            for (int u = 0; u < Units.All.Count; u++)
            {
                Unit unit = Units.All[u];
                if (unit.Owner != player || !unit.IsAlive) continue;
                for (int a = 0; a < unit.AbilityIds.Count; a++)
                {
                    AbilityDef ability;
                    if (!Catalog.Abilities.TryGet(unit.AbilityIds[a], out ability)) continue;
                    if (!unit.CanAfford(ability.Cost)) continue;

                    var movement = ability as MovementDef;
                    if (movement != null)
                    {
                        MovementOptions options = _resolvers.Enumerate(MoveContext(unit, movement));
                        for (int i = 0; i < options.Plans.Count; i++)
                            into.Add(new MoveCommand(player, unit.Id, movement.Id, options.Plans[i].Destination));
                        continue;
                    }

                    var attack = ability as AttackDef;
                    if (attack != null)
                    {
                        AttackTargeting.Enumerate(Map, Bodies, Catalog.Rules.Heights, Trajectories, unit, attack, _targetScratch);
                        for (int i = 0; i < _targetScratch.Count; i++)
                            into.Add(new AttackCommand(player, unit.Id, attack.Id, _targetScratch[i].Position));
                    }
                }
            }

            // End Turn closes the list; a resign is never a candidate, because a bot does not concede.
            into.Add(new EndTurnCommand(player));
        }

        /// <summary>Legal destinations for a unit's movement ability, or empty options when the unit cannot use it now.</summary>
        public MovementOptions MoveOptions(int unitId, string abilityId)
        {
            Unit unit; AbilityDef ability;
            if (IsOver || !Units.TryGet(unitId, out unit) || unit.Owner != ActivePlayer || !unit.IsAlive) return MovementOptions.Empty;
            if (!Catalog.Abilities.TryGet(abilityId, out ability) || !unit.HasAbility(abilityId) || !unit.CanAfford(ability.Cost)) return MovementOptions.Empty;
            var movement = ability as MovementDef;
            if (movement == null) return MovementOptions.Empty;
            return _resolvers.Enumerate(MoveContext(unit, movement));
        }

        /// <summary>Legal targets for a unit's attack ability (empty when the unit cannot use it now).</summary>
        public void AttackTargets(int unitId, string abilityId, List<IBody> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            Unit unit; AbilityDef ability;
            if (IsOver || !Units.TryGet(unitId, out unit) || unit.Owner != ActivePlayer || !unit.IsAlive) return;
            if (!Catalog.Abilities.TryGet(abilityId, out ability) || !unit.HasAbility(abilityId) || !unit.CanAfford(ability.Cost)) return;
            var attack = ability as AttackDef;
            if (attack == null) return;
            AttackTargeting.Enumerate(Map, Bodies, Catalog.Rules.Heights, Trajectories, unit, attack, into);
        }

        /// <summary>
        /// Why a target would be refused, without throwing: what a HUD asks while the cursor moves. Unknown
        /// units and abilities read as <see cref="TargetRejectReason.NoBody"/> rather than an exception.
        /// </summary>
        public TargetCheck CheckTarget(int unitId, string abilityId, Hex target)
        {
            Unit unit; AbilityDef ability;
            if (!Units.TryGet(unitId, out unit) || !Catalog.Abilities.TryGet(abilityId, out ability))
                return TargetCheck.Reject(TargetRejectReason.NoBody);
            var attack = ability as AttackDef;
            if (attack == null) return TargetCheck.Reject(TargetRejectReason.NoBody);
            return AttackTargeting.Check(Map, Bodies, Catalog.Rules.Heights, Trajectories, unit, attack, target);
        }

        /// <summary>
        /// Every map tile inside an attack's circular range band from where the unit stands, in the map's
        /// authored hex order. Geometry only: it says nothing about sight, trajectory or what is standing there.
        /// </summary>
        public void RangeBand(int unitId, string abilityId, List<Hex> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            Unit unit; AbilityDef ability;
            if (!Units.TryGet(unitId, out unit) || !Catalog.Abilities.TryGet(abilityId, out ability)) return;
            var attack = ability as AttackDef;
            if (attack == null) return;
            foreach (MapHex hex in MapData.Hexes)
                if (attack.InRangeSquared(Hex.EuclideanSquared(unit.Position, hex.Position))) into.Add(hex.Position);
        }

        /// <summary>
        /// The damage <paramref name="viewer"/> can predict for an attack, from what they know. Null when the
        /// attack is not legal right now. A server never sends this to the other player.
        /// </summary>
        public DamageBreakdown PreviewAttack(int viewer, int unitId, string abilityId, Hex target)
        {
            if (IsMirror && viewer != MirrorViewer)
                throw new InvalidOperationException($"This mirror belongs to player {MirrorViewer} and cannot preview for player {viewer}.");

            Unit attacker; IBody victim; AttackDef def;
            var command = new AttackCommand(ActivePlayer, unitId, abilityId, target);
            if (IsOver || TurnNumber == 0) return null;
            if (!ValidateAttack(command, out attacker, out def, out victim).Ok) return null;
            return PreviewAgainst(viewer, unitId, def, victim);
        }

        /// <summary>
        /// What an attack would do to a body from what one player knows, <em>without</em> asking whether it is
        /// legal: the number a HUD shows behind a "No line of sight" line, so a refused shot can still explain
        /// itself. Null when the unit or the target is not there.
        /// <para>
        /// On a mirror this adds back the hidden modifiers the viewer was never sent. The calculator counts an
        /// unknown for every hidden modifier it can see but may not show; a mirror does not have those
        /// modifiers at all, so without this the "?" row would quietly disappear online and the player would
        /// be told a guess was a certainty (ADR-026).
        /// </para>
        /// </summary>
        public DamageBreakdown PreviewAgainst(int viewer, int unitId, AttackDef attack, IBody target)
        {
            if (attack == null) throw new ArgumentNullException(nameof(attack));
            if (IsMirror && viewer != MirrorViewer)
                throw new InvalidOperationException($"This mirror belongs to player {MirrorViewer} and cannot preview for player {viewer}.");
            if (target == null) return null;

            Unit attacker;
            if (!Units.TryGet(unitId, out attacker)) return null;

            DamageBreakdown breakdown = _damage.Compute(Map, attacker, target, attack, Knowledge.For(viewer, this));
            if (!IsMirror) return breakdown;

            var victimUnit = target as Unit;
            return breakdown.WithUnknown(attacker.HiddenModifierCount + (victimUnit != null ? victimUnit.HiddenModifierCount : 0));
        }

        /// <summary>The same arithmetic with full knowledge: for tests and the server only. A mirror has no full knowledge to give.</summary>
        public DamageBreakdown ResolveAttackFully(int unitId, string abilityId, Hex target)
        {
            RefuseOnMirror("ResolveAttackFully");
            Unit attacker; IBody victim; AttackDef def;
            var command = new AttackCommand(ActivePlayer, unitId, abilityId, target);
            if (IsOver || TurnNumber == 0) return null;
            if (!ValidateAttack(command, out attacker, out def, out victim).Ok) return null;
            return _damage.Compute(Map, attacker, victim, def, Knowledge.Full);
        }

        /// <summary>
        /// What <paramref name="viewer"/> is allowed to see (ADR-010). On a mirror this reproduces the view it
        /// was built from, "?" rows and all, and refuses any other viewer: a mirror holds one player's half of
        /// the match and has nothing honest to say about the other's.
        /// </summary>
        public PlayerView ViewFor(int viewer)
        {
            if (IsMirror && viewer != MirrorViewer)
                throw new InvalidOperationException($"This mirror belongs to player {MirrorViewer} and cannot produce a view for player {viewer}.");
            return PlayerView.Build(this, viewer);
        }

        /// <summary>Records what each player has learned. Small lists, linear scans, stable order.</summary>
        private sealed class RevealedSet
        {
            private readonly List<Entry> _entries = new List<Entry>();

            public bool Contains(int viewer, int unitId, string id)
            {
                for (int i = 0; i < _entries.Count; i++)
                    if (_entries[i].Viewer == viewer && _entries[i].UnitId == unitId && _entries[i].Id == id) return true;
                return false;
            }

            /// <summary>Returns true when this was new.</summary>
            public bool Add(int viewer, int unitId, string id)
            {
                if (Contains(viewer, unitId, id)) return false;
                _entries.Add(new Entry(viewer, unitId, id));
                return true;
            }

            private readonly struct Entry
            {
                public readonly int Viewer;
                public readonly int UnitId;
                public readonly string Id;

                public Entry(int viewer, int unitId, string id)
                {
                    Viewer = viewer;
                    UnitId = unitId;
                    Id = id;
                }
            }
        }
    }
}
