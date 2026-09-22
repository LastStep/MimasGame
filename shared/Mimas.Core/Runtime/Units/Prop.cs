using System;
using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// One placed <see cref="PropDef"/> on the board: a neutral, public body that occupies its tile and blocks
    /// what its height reaches. A destructible prop is a legal target and is <em>removed</em> when it dies (the
    /// tile becomes enterable again); killing one never ends the round (design: #props, D11).
    /// </summary>
    public sealed class Prop : IBody
    {
        private static readonly IReadOnlyList<string> NoModifiers = new string[0];
        private static readonly IReadOnlyList<StatContribution> NoContributions = new StatContribution[0];

        public int Id { get; }

        public PropDef Def { get; }

        public Hex Position { get; }

        /// <summary>Props belong to nobody.</summary>
        public int Owner => -1;

        public int BodyHeight => Def.BodyHeight;

        public int AimHeight => Def.AimHeight;

        public StatBlock Stats => Def.Stats;

        /// <summary>A prop has nothing to hide: its public stats are its stats.</summary>
        public StatBlock PublicStats => Def.Stats;

        public IReadOnlyList<StatContribution> BoonStatContributions(string key) => NoContributions;

        public int MaxHp => Def.Stats.Hp;

        /// <summary>Current hit points; 0 for a wall, which is never damageable and so never drops.</summary>
        public int Hp { get; private set; }

        public bool IsDestroyed { get; private set; }

        public bool IsAlive => !IsDestroyed;

        public bool IsDamageable => Def.IsDestructible;

        public IReadOnlyList<string> ModifierIds => NoModifiers;

        public Prop(int id, PropDef def, Hex position)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
            Def = def ?? throw new ArgumentNullException(nameof(def));
            Position = position;
            Hp = def.Stats.Hp;
        }

        /// <summary>Applies damage (never below 0 hp) and destroys the prop at 0. Returns the hit points lost.</summary>
        public int TakeDamage(int amount)
        {
            if (!IsDamageable) throw new InvalidOperationException($"Prop {Id} ('{Def.Id}') cannot be damaged.");
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            int before = Hp;
            Hp = amount >= Hp ? 0 : Hp - amount;
            if (Hp == 0) IsDestroyed = true;
            return before - Hp;
        }

        /// <summary>
        /// Puts hit points back without replaying the damage: for a mirror rebuilt from a player view, and for
        /// replays. A prop that was destroyed is simply absent from a view, so this never restores one to life.
        /// </summary>
        public void Restore(int hp)
        {
            if (hp < 0 || hp > MaxHp) throw new ArgumentOutOfRangeException(nameof(hp), $"Prop {Id} hp must be 0..{MaxHp}, was {hp}.");
            Hp = hp;
            IsDestroyed = false;
        }

        public override string ToString() => $"Prop {Id} '{Def.Id}' at {Position}{(IsDestroyed ? " (destroyed)" : "")}";
    }
}
