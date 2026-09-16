using System;
using System.Collections.Generic;
using System.Text;

namespace Mimas.Core.Combat
{
    /// <summary>What a damage line came from.</summary>
    public enum DamageLineKind
    {
        /// <summary>The attack's flat base damage.</summary>
        Base = 0,

        /// <summary>The attacker's <c>power.&lt;type&gt;</c> stat.</summary>
        Power = 1,

        /// <summary>The target's <c>defense.&lt;type&gt;</c> stat (negative).</summary>
        Defense = 2,

        /// <summary>A <c>modifiers/*.json</c> entry that applied.</summary>
        Modifier = 3,
    }

    /// <summary>Whose side a line belongs to. Decides visibility and the breakdown's display order.</summary>
    public enum DamageLineOwner
    {
        None = 0,
        Attacker = 1,
        AttackerTile = 2,
        Target = 3,
        TargetTile = 4,
        Global = 5,
    }

    /// <summary>
    /// One signed contribution to an attack's damage. <see cref="Hidden"/> marks a modifier the owning unit's
    /// opponent does not know about until it is revealed; the calculator drops such lines from a
    /// knowledge-limited preview and the event filter drops them from what the other player receives.
    /// </summary>
    public sealed class DamageLine
    {
        public DamageLineKind Kind { get; }

        /// <summary>Stat key, modifier id, or "base".</summary>
        public string Id { get; }

        public DamageLineOwner Owner { get; }

        /// <summary>
        /// The body that carries this line's modifier or stat, or -1 for tiles and globals. A prop can own a
        /// defence line, so this is a body id, not necessarily a unit id; the name is kept for the wire.
        /// </summary>
        public int OwnerUnitId { get; }

        public int Amount { get; }

        public bool Hidden { get; }

        public DamageLine(DamageLineKind kind, string id, DamageLineOwner owner, int ownerUnitId, int amount, bool hidden)
        {
            Kind = kind;
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Owner = owner;
            OwnerUnitId = ownerUnitId;
            Amount = amount;
            Hidden = hidden;
        }

        public override string ToString() => $"{Kind} {Id} ({Owner}) {(Amount >= 0 ? "+" : "")}{Amount}{(Hidden ? " hidden" : "")}";
    }

    /// <summary>
    /// The full arithmetic of one attack as a fixed-order list of signed integers plus the floor-0 total,
    /// so a player can verify the number at a glance and a preview can be diffed line by line against the
    /// actual result. <see cref="UnknownCount"/> is how many hidden modifiers on the units involved the
    /// viewer cannot see (0 for a full-knowledge result): the HUD shows a "?" row when it is positive.
    /// </summary>
    public sealed class DamageBreakdown
    {
        private readonly List<DamageLine> _lines;

        public IReadOnlyList<DamageLine> Lines => _lines;

        /// <summary>Sum of the visible lines, never below 0.</summary>
        public int Total { get; }

        public int UnknownCount { get; }

        /// <summary>True when nothing the viewer cannot see could change <see cref="Total"/>.</summary>
        public bool IsExact => UnknownCount == 0;

        public DamageBreakdown(List<DamageLine> lines, int unknownCount)
        {
            _lines = lines ?? throw new ArgumentNullException(nameof(lines));
            if (unknownCount < 0) throw new ArgumentOutOfRangeException(nameof(unknownCount));
            UnknownCount = unknownCount;
            int sum = 0;
            for (int i = 0; i < _lines.Count; i++) sum += _lines[i].Amount;
            Total = sum < 0 ? 0 : sum;
        }

        /// <summary>The line for a modifier id, or null.</summary>
        public DamageLine FindModifier(string modifierId)
        {
            for (int i = 0; i < _lines.Count; i++)
                if (_lines[i].Kind == DamageLineKind.Modifier && _lines[i].Id == modifierId) return _lines[i];
            return null;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(_lines[i]);
            }
            sb.Append(" = ").Append(Total);
            if (UnknownCount > 0) sb.Append(" (+").Append(UnknownCount).Append(" unknown)");
            return sb.ToString();
        }
    }
}
