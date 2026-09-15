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
    /// </summary>
    public sealed class MatchState : IRevealedKnowledge
    {
        private readonly MovementResolverRegistry _resolvers;
        private readonly DamageCalculator _damage;
        private readonly RevealedSet _revealed = new RevealedSet();
        private readonly List<Unit> _targetScratch = new List<Unit>();

        public ContentCatalog Catalog { get; }
        public MatchSetup Setup { get; }
        public MapData MapData { get; }
        public TileMap Map { get; }
        public UnitSet Units { get; }
        public Rng Rng { get; }

        /// <summary>Player whose turn it is.</summary>
        public int ActivePlayer { get; private set; }

        /// <summary>1-based, counts every turn of both players. 0 before the first turn starts.</summary>
        public int TurnNumber { get; private set; }

        /// <summary>True once a move or attack happened this turn (a timeout with nothing done is a skip).</summary>
        public bool ActedThisTurn { get; private set; }

        public bool IsOver { get; private set; }

        /// <summary>Winning player, or -1 while the match runs.</summary>
        public int Winner { get; private set; } = -1;

        /// <summary>Builds the board, spawns one unit per player on its spawn, and starts the first turn (events are returned by <see cref="Start"/>).</summary>
        public MatchState(ContentCatalog catalog, MatchSetup setup, uint seed, MovementResolverRegistry resolvers = null)
        {
            Catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            Setup = setup ?? throw new ArgumentNullException(nameof(setup));
            _resolvers = resolvers ?? MovementResolverRegistry.CreateDefault();
            _damage = new DamageCalculator(catalog);
            Rng = new Rng(seed);

            MapData = catalog.Maps.Get(setup.MapId);
            Map = MapData.BuildTileMap(catalog.Terrains);
            Units = new UnitSet();

            for (int player = 0; player < MatchSetup.PlayerCount; player++)
            {
                ClassDef cls = catalog.Classes.Get(setup.ClassIdOf(player));
                var unit = new Unit(player, player, player == 0 ? MapData.SpawnP1 : MapData.SpawnP2, cls);
                IReadOnlyList<string> modifiers = setup.ModifierIdsOf(player);
                for (int i = 0; i < modifiers.Count; i++)
                {
                    if (!catalog.Modifiers.Contains(modifiers[i]))
                        throw new ArgumentException($"Unknown modifier '{modifiers[i]}' for player {player}.", nameof(setup));
                    unit.AddModifier(modifiers[i]);
                }
                Units.Add(unit);
            }

            ActivePlayer = setup.FirstPlayer;
            TurnNumber = 0;
        }

        /// <summary>Starts the first turn. Call exactly once after construction.</summary>
        public IReadOnlyList<MatchEvent> Start()
        {
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
            result = _resolvers.Validate(MovementContext.For(Map, Catalog.Terrains, Units, unit, movement), move.Destination);
            return result.Ok ? CommandResult.Accepted : CommandResult.RejectMove(result.Reason);
        }

        private CommandResult ValidateAttack(AttackCommand attack, out Unit attacker, out AttackDef def, out Unit victim)
        {
            attacker = null; def = null; victim = null;
            AbilityDef ability;
            CommandResult actor = ValidateActor(attack.Player, attack.UnitId, attack.AbilityId, out attacker, out ability);
            if (!actor.Ok) return actor;
            def = ability as AttackDef;
            if (def == null) return CommandResult.Reject(CommandRejectReason.WrongAbilityType);
            TargetRejectReason target = AttackTargeting.Check(Map, Units, attacker, def, attack.Target, out victim);
            return target == TargetRejectReason.None ? CommandResult.Accepted : CommandResult.RejectTarget(target);
        }

        // ---- application ---------------------------------------------------------------------------

        /// <summary>Applies a command. Throws <see cref="InvalidOperationException"/> when it does not validate; call <see cref="Validate"/> first.</summary>
        public IReadOnlyList<MatchEvent> Apply(Command command)
        {
            CommandResult check = Validate(command);
            if (!check.Ok) throw new InvalidOperationException($"Command {command} rejected: {check}");

            var events = new List<MatchEvent>();
            switch (command)
            {
                case MoveCommand move: ApplyMove(move, events); break;
                case AttackCommand attack: ApplyAttack(attack, events); break;
                case EndTurnCommand end: ApplyEndTurn(end, events); break;
            }
            return events;
        }

        /// <summary>Validates then applies; returns false (no events, no change) when the command is illegal.</summary>
        public bool TryApply(Command command, List<MatchEvent> into, out CommandResult result)
        {
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
            Unit attacker, victim; AttackDef def;
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
                Unit owner = Units.Get(line.OwnerUnitId);
                int other = 1 - owner.Owner;
                if (_revealed.Add(other, owner.Id, line.Id))
                    events.Add(new ModifierRevealedEvent(owner.Id, line.Id, other));
            }

            int lost = victim.TakeDamage(breakdown.Total);
            events.Add(new AttackResolvedEvent(attacker.Id, victim.Id, def.Id, breakdown, lost, victim.Hp));
            events.Add(new ApSpentEvent(attacker.Id, def.Id, def.Cost, attacker.Ap));

            if (!victim.IsAlive)
            {
                events.Add(new UnitDiedEvent(victim.Id));
                CheckElimination(events);
            }
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
                        MovementOptions options = _resolvers.Enumerate(MovementContext.For(Map, Catalog.Terrains, Units, unit, movement));
                        for (int i = 0; i < options.Plans.Count; i++)
                            into.Add(new MoveCommand(player, unit.Id, movement.Id, options.Plans[i].Destination));
                        continue;
                    }

                    var attack = ability as AttackDef;
                    if (attack != null)
                    {
                        AttackTargeting.Enumerate(Map, Units, unit, attack, _targetScratch);
                        for (int i = 0; i < _targetScratch.Count; i++)
                            into.Add(new AttackCommand(player, unit.Id, attack.Id, _targetScratch[i].Position));
                    }
                }
            }

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
            return _resolvers.Enumerate(MovementContext.For(Map, Catalog.Terrains, Units, unit, movement));
        }

        /// <summary>Legal targets for a unit's attack ability (empty when the unit cannot use it now).</summary>
        public void AttackTargets(int unitId, string abilityId, List<Unit> into)
        {
            if (into == null) throw new ArgumentNullException(nameof(into));
            into.Clear();
            Unit unit; AbilityDef ability;
            if (IsOver || !Units.TryGet(unitId, out unit) || unit.Owner != ActivePlayer || !unit.IsAlive) return;
            if (!Catalog.Abilities.TryGet(abilityId, out ability) || !unit.HasAbility(abilityId) || !unit.CanAfford(ability.Cost)) return;
            var attack = ability as AttackDef;
            if (attack == null) return;
            AttackTargeting.Enumerate(Map, Units, unit, attack, into);
        }

        /// <summary>
        /// The damage <paramref name="viewer"/> can predict for an attack, from what they know. Null when the
        /// attack is not legal right now. A server never sends this to the other player.
        /// </summary>
        public DamageBreakdown PreviewAttack(int viewer, int unitId, string abilityId, Hex target)
        {
            Unit attacker, victim; AttackDef def;
            var command = new AttackCommand(ActivePlayer, unitId, abilityId, target);
            if (IsOver || TurnNumber == 0) return null;
            if (!ValidateAttack(command, out attacker, out def, out victim).Ok) return null;
            return _damage.Compute(Map, attacker, victim, def, Knowledge.For(viewer, this));
        }

        /// <summary>The same arithmetic with full knowledge: for tests and the server only.</summary>
        public DamageBreakdown ResolveAttackFully(int unitId, string abilityId, Hex target)
        {
            Unit attacker, victim; AttackDef def;
            var command = new AttackCommand(ActivePlayer, unitId, abilityId, target);
            if (IsOver || TurnNumber == 0) return null;
            if (!ValidateAttack(command, out attacker, out def, out victim).Ok) return null;
            return _damage.Compute(Map, attacker, victim, def, Knowledge.Full);
        }

        /// <summary>What <paramref name="viewer"/> is allowed to see (ADR-010).</summary>
        public PlayerView ViewFor(int viewer) => PlayerView.Build(this, viewer);

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
