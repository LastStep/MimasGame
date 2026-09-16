using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Units;

namespace Mimas.Core.Match
{
    /// <summary>
    /// One ability or modifier as one player sees it on a unit. <see cref="Id"/> is null while hidden.
    /// <see cref="SourceItemId"/> is set even for a hidden ability: which item grants it is public, what it
    /// does is not (design: #hidden-info).
    /// </summary>
    public sealed class KnownEntry
    {
        public string Id { get; }

        /// <summary>The item that granted this ability, or null when innate or when this is a modifier.</summary>
        public string SourceItemId { get; }

        public bool Revealed => Id != null;

        public KnownEntry(string id)
        {
            Id = id;
        }

        public KnownEntry(string id, string sourceItemId)
        {
            Id = id;
            SourceItemId = sourceItemId;
        }
    }

    /// <summary>A unit as one player sees it. Position, gear and numbers are public; abilities and hidden modifiers are not.</summary>
    public sealed class UnitView
    {
        private readonly List<KnownEntry> _abilities;
        private readonly List<KnownEntry> _modifiers;

        public int Id { get; }
        public int Owner { get; }

        /// <summary>Equipped item ids in slot order (weapon, crown, boots, armour). Always public.</summary>
        public IReadOnlyList<string> ItemIds { get; }
        public Hex Position { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public int Ap { get; }
        public int ApPerTurn { get; }
        public bool IsAlive => Hp > 0;

        /// <summary>True when the viewer owns this unit (everything is visible).</summary>
        public bool IsMine { get; }

        /// <summary>Abilities in grant order; hidden entries have a null id but still count.</summary>
        public IReadOnlyList<KnownEntry> Abilities => _abilities;

        /// <summary>Modifiers in grant order; hidden, unrevealed entries have a null id.</summary>
        public IReadOnlyList<KnownEntry> Modifiers => _modifiers;

        public UnitView(int id, int owner, IReadOnlyList<string> itemIds, Hex position, int hp, int maxHp, int ap, int apPerTurn, bool isMine,
            List<KnownEntry> abilities, List<KnownEntry> modifiers)
        {
            Id = id;
            Owner = owner;
            ItemIds = itemIds ?? new List<string>();
            Position = position;
            Hp = hp;
            MaxHp = maxHp;
            Ap = ap;
            ApPerTurn = apPerTurn;
            IsMine = isMine;
            _abilities = abilities ?? new List<KnownEntry>();
            _modifiers = modifiers ?? new List<KnownEntry>();
        }

        public int UnrevealedModifierCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _modifiers.Count; i++) if (!_modifiers[i].Revealed) n++;
                return n;
            }
        }
    }

    /// <summary>
    /// Everything one player may know about the match (ADR-010): the projection the server sends and the
    /// only thing a client renders from. Built fresh by <see cref="MatchState.ViewFor"/>; it holds no
    /// reference back to the state.
    /// </summary>
    public sealed class PlayerView
    {
        private readonly List<UnitView> _units;

        public int Viewer { get; }
        public int ActivePlayer { get; }
        public int TurnNumber { get; }
        public bool ActedThisTurn { get; }
        public bool IsOver { get; }
        public int Winner { get; }
        public string MapId { get; }

        public bool IsMyTurn => !IsOver && ActivePlayer == Viewer;

        public IReadOnlyList<UnitView> Units => _units;

        private PlayerView(int viewer, int activePlayer, int turnNumber, bool acted, bool isOver, int winner, string mapId, List<UnitView> units)
        {
            Viewer = viewer;
            ActivePlayer = activePlayer;
            TurnNumber = turnNumber;
            ActedThisTurn = acted;
            IsOver = isOver;
            Winner = winner;
            MapId = mapId;
            _units = units;
        }

        public UnitView FindUnit(int id)
        {
            for (int i = 0; i < _units.Count; i++) if (_units[i].Id == id) return _units[i];
            return null;
        }

        public static PlayerView Build(MatchState state, int viewer)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (viewer < 0 || viewer >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));

            var units = new List<UnitView>(state.Units.Count);
            for (int i = 0; i < state.Units.All.Count; i++)
            {
                Unit unit = state.Units.All[i];
                bool mine = unit.Owner == viewer;

                var abilities = new List<KnownEntry>(unit.AbilityIds.Count);
                for (int a = 0; a < unit.AbilityIds.Count; a++)
                {
                    string id = unit.AbilityIds[a];
                    bool known = mine || state.Knows(viewer, unit.Id, id);
                    abilities.Add(new KnownEntry(known ? id : null, unit.AbilitySourceOf(id)));
                }

                var modifiers = new List<KnownEntry>(unit.ModifierIds.Count);
                for (int m = 0; m < unit.ModifierIds.Count; m++)
                {
                    string id = unit.ModifierIds[m];
                    ModifierDef def;
                    bool hidden = state.Catalog.Modifiers.TryGet(id, out def) && def.IsHidden;
                    bool known = mine || !hidden || state.Knows(viewer, unit.Id, id);
                    modifiers.Add(new KnownEntry(known ? id : null));
                }

                units.Add(new UnitView(unit.Id, unit.Owner, unit.ItemIds, unit.Position, unit.Hp, unit.MaxHp, unit.Ap, unit.ApPerTurn, mine, abilities, modifiers));
            }

            return new PlayerView(viewer, state.ActivePlayer, state.TurnNumber, state.ActedThisTurn, state.IsOver, state.Winner, state.MapData.Id, units);
        }
    }
}
