using System;
using System.Collections.Generic;

namespace Mimas.Core.Match
{
    /// <summary>One thing a viewer has been shown about a unit: an ability, a modifier, a boon or a lineage id. What a session carries from round to round.</summary>
    public readonly struct RevealedEntry
    {
        public readonly int Viewer;
        public readonly int UnitId;
        public readonly string Id;

        public RevealedEntry(int viewer, int unitId, string id)
        {
            Viewer = viewer;
            UnitId = unitId;
            Id = id ?? throw new ArgumentNullException(nameof(id));
        }

        public override string ToString() => $"P{Viewer} knows {Id} on unit {UnitId}";
    }

    /// <summary>
    /// Everything needed to start one map round: the map, each player's build (gear, lineage, boons), any
    /// modifiers the players bring in (the dev knob that grants a hidden ward to test the reveal path),
    /// what each player already knows from earlier rounds, and who moves first. Ids only; the catalogue
    /// resolves them.
    /// </summary>
    public sealed class MatchSetup
    {
        public const int PlayerCount = 2;

        private readonly PlayerBuild[] _builds = new PlayerBuild[PlayerCount];
        private readonly List<string>[] _modifierIds = { new List<string>(), new List<string>() };
        private readonly List<RevealedEntry> _revealed = new List<RevealedEntry>();

        public string MapId { get; }

        /// <summary>Player index that takes the first turn (0 or 1).</summary>
        public int FirstPlayer { get; }

        /// <summary>Gear only: a bare build for each player. What every pre-boons caller constructs.</summary>
        public MatchSetup(string mapId, Loadout player0, Loadout player1, int firstPlayer = 0)
            : this(mapId, new PlayerBuild(player0 ?? throw new ArgumentNullException(nameof(player0))),
                new PlayerBuild(player1 ?? throw new ArgumentNullException(nameof(player1))), firstPlayer)
        {
        }

        public MatchSetup(string mapId, PlayerBuild player0, PlayerBuild player1, int firstPlayer = 0)
        {
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            _builds[0] = player0 ?? throw new ArgumentNullException(nameof(player0));
            _builds[1] = player1 ?? throw new ArgumentNullException(nameof(player1));
            if (firstPlayer < 0 || firstPlayer >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            FirstPlayer = firstPlayer;
        }

        /// <summary>The gear the player brings into the round.</summary>
        public Loadout LoadoutOf(int player) => _builds[CheckPlayer(player)].Loadout;

        /// <summary>The whole build: gear, lineage and boons.</summary>
        public PlayerBuild BuildOf(int player) => _builds[CheckPlayer(player)];

        /// <summary>Modifier ids the player's unit starts with, in the order they were added.</summary>
        public IReadOnlyList<string> ModifierIdsOf(int player) => _modifierIds[CheckPlayer(player)];

        /// <summary>What each viewer already knows, in insertion order: imported into the round's revealed set (design: #hidden-info rule 1).</summary>
        public IReadOnlyList<RevealedEntry> RevealedEntries => _revealed;

        public MatchSetup WithModifier(int player, string modifierId)
        {
            if (string.IsNullOrEmpty(modifierId)) throw new ArgumentException("Modifier id must not be empty.", nameof(modifierId));
            var list = _modifierIds[CheckPlayer(player)];
            if (!list.Contains(modifierId)) list.Add(modifierId);
            return this;
        }

        /// <summary>Seeds the round with something <paramref name="viewer"/> learned earlier in the session.</summary>
        public MatchSetup WithRevealed(int viewer, int unitId, string id)
        {
            CheckPlayer(viewer);
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A revealed id must not be empty.", nameof(id));
            for (int i = 0; i < _revealed.Count; i++)
                if (_revealed[i].Viewer == viewer && _revealed[i].UnitId == unitId && _revealed[i].Id == id) return this;
            _revealed.Add(new RevealedEntry(viewer, unitId, id));
            return this;
        }

        private static int CheckPlayer(int player)
        {
            if (player < 0 || player >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
            return player;
        }
    }
}
