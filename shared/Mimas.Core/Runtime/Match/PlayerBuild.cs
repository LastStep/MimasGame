using System;
using System.Collections.Generic;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Everything a player brings to a round (design: #character): four items, a divine lineage and the
    /// boons owned so far, in the order they were granted. Ids only; the catalogue resolves them. Immutable
    /// and value-equal; a draft pick produces a new build through <see cref="WithBoon"/>. A build with no
    /// lineage and no boons is what every pre-boons caller (the room, the bot options, the mirror) makes.
    /// </summary>
    public sealed class PlayerBuild : IEquatable<PlayerBuild>
    {
        private static readonly string[] NoBoons = new string[0];

        private readonly List<string> _boonIds;

        public Loadout Loadout { get; }

        /// <summary>The lineage prayed to, or null before one is chosen.</summary>
        public string LineageId { get; }

        /// <summary>Boon ids in grant order. An id repeats only for a stackable boon.</summary>
        public IReadOnlyList<string> BoonIds => _boonIds;

        public PlayerBuild(Loadout loadout) : this(loadout, null, null)
        {
        }

        public PlayerBuild(Loadout loadout, string lineageId, IEnumerable<string> boonIds = null)
        {
            Loadout = loadout ?? throw new ArgumentNullException(nameof(loadout));
            LineageId = string.IsNullOrEmpty(lineageId) ? null : lineageId;
            _boonIds = boonIds != null ? new List<string>(boonIds) : new List<string>(NoBoons);
            for (int i = 0; i < _boonIds.Count; i++)
                if (string.IsNullOrEmpty(_boonIds[i])) throw new ArgumentException("A boon id must not be empty.", nameof(boonIds));
        }

        public bool HasBoon(string boonId) => boonId != null && _boonIds.Contains(boonId);

        /// <summary>The same build with one more boon at the end.</summary>
        public PlayerBuild WithBoon(string boonId)
        {
            if (string.IsNullOrEmpty(boonId)) throw new ArgumentException("A boon id must not be empty.", nameof(boonId));
            var ids = new List<string>(_boonIds) { boonId };
            return new PlayerBuild(Loadout, LineageId, ids);
        }

        public bool Equals(PlayerBuild other)
        {
            if (ReferenceEquals(other, null)) return false;
            if (ReferenceEquals(other, this)) return true;
            if (!Loadout.Equals(other.Loadout)) return false;
            if (!string.Equals(LineageId, other.LineageId, StringComparison.Ordinal)) return false;
            if (_boonIds.Count != other._boonIds.Count) return false;
            for (int i = 0; i < _boonIds.Count; i++)
                if (!string.Equals(_boonIds[i], other._boonIds[i], StringComparison.Ordinal)) return false;
            return true;
        }

        public override bool Equals(object obj) => Equals(obj as PlayerBuild);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Loadout.GetHashCode();
                hash = hash * 31 + (LineageId != null ? StringComparer.Ordinal.GetHashCode(LineageId) : 0);
                for (int i = 0; i < _boonIds.Count; i++) hash = hash * 31 + StringComparer.Ordinal.GetHashCode(_boonIds[i]);
                return hash;
            }
        }

        public override string ToString()
            => Loadout + (LineageId != null ? " | " + LineageId : "") + (_boonIds.Count > 0 ? " | " + string.Join(", ", _boonIds) : "");
    }
}
