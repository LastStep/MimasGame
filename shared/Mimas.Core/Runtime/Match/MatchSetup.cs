using System;
using System.Collections.Generic;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Everything needed to start one map round: the map, each player's loadout, any modifiers the players
    /// bring in (boons carried over between rounds; for now also the dev knob that grants a hidden ward to
    /// test the reveal path), and who moves first. Ids only; the catalogue resolves them.
    /// </summary>
    public sealed class MatchSetup
    {
        public const int PlayerCount = 2;

        private readonly Loadout[] _loadouts = new Loadout[PlayerCount];
        private readonly List<string>[] _modifierIds = { new List<string>(), new List<string>() };

        public string MapId { get; }

        /// <summary>Player index that takes the first turn (0 or 1).</summary>
        public int FirstPlayer { get; }

        public MatchSetup(string mapId, Loadout player0, Loadout player1, int firstPlayer = 0)
        {
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            _loadouts[0] = player0 ?? throw new ArgumentNullException(nameof(player0));
            _loadouts[1] = player1 ?? throw new ArgumentNullException(nameof(player1));
            if (firstPlayer < 0 || firstPlayer >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            FirstPlayer = firstPlayer;
        }

        /// <summary>The gear the player brings into the round.</summary>
        public Loadout LoadoutOf(int player) => _loadouts[CheckPlayer(player)];

        /// <summary>Modifier ids the player's unit starts with, in the order they were added.</summary>
        public IReadOnlyList<string> ModifierIdsOf(int player) => _modifierIds[CheckPlayer(player)];

        public MatchSetup WithModifier(int player, string modifierId)
        {
            if (string.IsNullOrEmpty(modifierId)) throw new ArgumentException("Modifier id must not be empty.", nameof(modifierId));
            var list = _modifierIds[CheckPlayer(player)];
            if (!list.Contains(modifierId)) list.Add(modifierId);
            return this;
        }

        private static int CheckPlayer(int player)
        {
            if (player < 0 || player >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(player));
            return player;
        }
    }
}
