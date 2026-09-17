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

        /// <summary>Body height in units above the tile top: what this hero blocks with. Public.</summary>
        public int BodyHeight { get; }

        /// <summary>Where shots leave from and land on this hero, in units above the tile top. Public.</summary>
        public int AimHeight { get; }

        /// <summary>True when the viewer owns this unit (everything is visible).</summary>
        public bool IsMine { get; }

        /// <summary>Abilities in grant order; hidden entries have a null id but still count.</summary>
        public IReadOnlyList<KnownEntry> Abilities => _abilities;

        /// <summary>Modifiers in grant order; hidden, unrevealed entries have a null id.</summary>
        public IReadOnlyList<KnownEntry> Modifiers => _modifiers;

        public UnitView(int id, int owner, IReadOnlyList<string> itemIds, Hex position, int hp, int maxHp, int ap, int apPerTurn, bool isMine,
            List<KnownEntry> abilities, List<KnownEntry> modifiers, int bodyHeight = 0, int aimHeight = 0)
        {
            BodyHeight = bodyHeight;
            AimHeight = aimHeight;
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
    /// A prop as a player sees it. Props are neutral and entirely public (design: #props): there is nothing
    /// about one to hide, so both viewers get the same rows.
    /// </summary>
    public sealed class PropView
    {
        public int Id { get; }

        /// <summary>The <c>props/*.json</c> id: what to render.</summary>
        public string DefId { get; }

        public Hex Position { get; }
        public int Hp { get; }
        public int MaxHp { get; }
        public bool IsAlive { get; }
        public int BodyHeight { get; }
        public int AimHeight { get; }

        /// <summary>False for a wall: it blocks shots but can never be targeted.</summary>
        public bool IsDamageable { get; }

        public PropView(int id, string defId, Hex position, int hp, int maxHp, bool isAlive, int bodyHeight, int aimHeight, bool isDamageable)
        {
            Id = id;
            DefId = defId;
            Position = position;
            Hp = hp;
            MaxHp = maxHp;
            IsAlive = isAlive;
            BodyHeight = bodyHeight;
            AimHeight = aimHeight;
            IsDamageable = isDamageable;
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
        private readonly List<PropView> _props;

        public int Viewer { get; }
        public int ActivePlayer { get; }
        public int TurnNumber { get; }
        public bool ActedThisTurn { get; }
        public bool IsOver { get; }
        public int Winner { get; }
        public string MapId { get; }

        public bool IsMyTurn => !IsOver && ActivePlayer == Viewer;

        public IReadOnlyList<UnitView> Units => _units;

        /// <summary>Every prop the map placed, in its authored order. Destroyed ones are gone from this list.</summary>
        public IReadOnlyList<PropView> Props => _props;

        private PlayerView(int viewer, int activePlayer, int turnNumber, bool acted, bool isOver, int winner, string mapId,
            List<UnitView> units, List<PropView> props)
        {
            Viewer = viewer;
            ActivePlayer = activePlayer;
            TurnNumber = turnNumber;
            ActedThisTurn = acted;
            IsOver = isOver;
            Winner = winner;
            MapId = mapId;
            _units = units;
            _props = props;
        }

        /// <summary>
        /// Builds a view from parts rather than from a state: what <c>Mimas.Core.Protocol.Wire.ReadView</c>
        /// calls after decoding a frame, and the only way a client ever obtains one. <see cref="Build"/>
        /// remains the only path from a <see cref="MatchState"/>.
        /// </summary>
        public static PlayerView Create(int viewer, int activePlayer, int turnNumber, bool acted, bool isOver, int winner, string mapId,
            List<UnitView> units, List<PropView> props)
        {
            if (viewer < 0 || viewer >= MatchSetup.PlayerCount) throw new ArgumentOutOfRangeException(nameof(viewer));
            if (mapId == null) throw new ArgumentNullException(nameof(mapId));
            return new PlayerView(viewer, activePlayer, turnNumber, acted, isOver, winner, mapId,
                units ?? new List<UnitView>(), props ?? new List<PropView>());
        }

        public UnitView FindUnit(int id)
        {
            for (int i = 0; i < _units.Count; i++) if (_units[i].Id == id) return _units[i];
            return null;
        }

        public PropView FindProp(int id)
        {
            for (int i = 0; i < _props.Count; i++) if (_props[i].Id == id) return _props[i];
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

                // The hidden slots are empty on the truth, so this is the plain loop there; on a mirror they
                // are the entries it was never told, put back where they were (ADR-026).
                int abilityTotal = unit.AbilityIds.Count + unit.HiddenAbilityCount;
                var abilities = new List<KnownEntry>(abilityTotal);
                for (int a = 0, next = 0, gap = 0; a < abilityTotal; a++)
                {
                    if (gap < unit.HiddenAbilitySlots.Count && unit.HiddenAbilitySlots[gap].Index == a)
                    {
                        abilities.Add(new KnownEntry(null, unit.HiddenAbilitySlots[gap].SourceItemId));
                        gap++;
                        continue;
                    }
                    string id = unit.AbilityIds[next++];
                    bool known = mine || state.Knows(viewer, unit.Id, id);
                    abilities.Add(new KnownEntry(known ? id : null, unit.AbilitySourceOf(id)));
                }

                int modifierTotal = unit.ModifierIds.Count + unit.HiddenModifierCount;
                var modifiers = new List<KnownEntry>(modifierTotal);
                for (int m = 0, next = 0, gap = 0; m < modifierTotal; m++)
                {
                    if (gap < unit.HiddenModifierSlots.Count && unit.HiddenModifierSlots[gap].Index == m)
                    {
                        modifiers.Add(new KnownEntry(null));
                        gap++;
                        continue;
                    }
                    string id = unit.ModifierIds[next++];
                    ModifierDef def;
                    bool hidden = state.Catalog.Modifiers.TryGet(id, out def) && def.IsHidden;
                    bool known = mine || !hidden || state.Knows(viewer, unit.Id, id);
                    modifiers.Add(new KnownEntry(known ? id : null));
                }

                units.Add(new UnitView(unit.Id, unit.Owner, unit.ItemIds, unit.Position, unit.Hp, unit.MaxHp, unit.Ap, unit.ApPerTurn, mine,
                    abilities, modifiers, unit.BodyHeight, unit.AimHeight));
            }

            var props = new List<PropView>(state.Props.Count);
            for (int i = 0; i < state.Props.Count; i++)
            {
                Prop prop = state.Props[i];
                if (prop.IsDestroyed) continue;
                props.Add(new PropView(prop.Id, prop.Def.Id, prop.Position, prop.Hp, prop.MaxHp, prop.IsAlive,
                    prop.BodyHeight, prop.AimHeight, prop.IsDamageable));
            }

            return new PlayerView(viewer, state.ActivePlayer, state.TurnNumber, state.ActedThisTurn, state.IsOver, state.Winner, state.MapData.Id, units, props);
        }
    }
}
