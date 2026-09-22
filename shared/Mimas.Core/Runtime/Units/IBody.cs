using System.Collections.Generic;
using Mimas.Core.Data;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// Anything that stands on a tile with a height: it occupies the tile, blocks sight and trajectories with
    /// its body, and — when it has hit points — can be hit (design: #props, ADR-024). Heroes and props are the
    /// two implementations; targeting, damage and the body set only ever see this interface, so "anything with
    /// health can be hit, nothing else" is one rule rather than a type switch.
    /// </summary>
    public interface IBody
    {
        /// <summary>Unique among all bodies of a match (units first, then props).</summary>
        int Id { get; }

        /// <summary>Player index, or -1 for a neutral prop.</summary>
        int Owner { get; }

        Hex Position { get; }

        /// <summary>Height in units above the tile top: what an intervening column adds.</summary>
        int BodyHeight { get; }

        /// <summary>Where attacks leave from and land, in units above the tile top.</summary>
        int AimHeight { get; }

        /// <summary>Unit: hp &gt; 0. Prop: not destroyed (a wall is always alive).</summary>
        bool IsAlive { get; }

        /// <summary>Unit: always. Prop: only when its definition gives it hit points.</summary>
        bool IsDamageable { get; }

        int Hp { get; }

        int MaxHp { get; }

        /// <summary>The numbers the rules read: everything, boons included.</summary>
        StatBlock Stats { get; }

        /// <summary>The numbers gear explains, and therefore public (design: #hidden-info). A prop's are its <see cref="Stats"/>.</summary>
        StatBlock PublicStats { get; }

        /// <summary>Every boon contribution to one stat key, in boon order: each one is its own hidden damage line. Always empty for props.</summary>
        IReadOnlyList<StatContribution> BoonStatContributions(string key);

        /// <summary>Modifier ids this body carries, in grant order. Always empty for props.</summary>
        IReadOnlyList<string> ModifierIds { get; }

        /// <summary>Applies damage and returns the hit points actually lost. Throws when <see cref="IsDamageable"/> is false.</summary>
        int TakeDamage(int amount);
    }

    /// <summary>Where the bodies are. Sight, trajectories and targeting only ever ask this.</summary>
    public interface IBodyLookup
    {
        /// <summary>Units in unit-set order, then props in map authored order.</summary>
        IReadOnlyList<IBody> All { get; }

        /// <summary>The living body standing on <paramref name="hex"/>; dead units and destroyed props occupy nothing.</summary>
        bool TryGetBodyAt(Hex hex, out IBody body);

        bool TryGetBody(int id, out IBody body);
    }
}
