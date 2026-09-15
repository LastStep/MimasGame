using System;
using System.Collections.Generic;

namespace Mimas.Core.Match
{
    /// <summary>
    /// Everything needed to start one map round: the map, each player's class, any modifiers the players
    /// bring in (boons carried over between rounds; for now also the dev knob that grants a hidden ward to
    /// test the reveal path), and who moves first. Ids only; the catalogue resolves them.
    /// </summary>
    public sealed class MatchSetup
    {
        public const int PlayerCount = 2;

        private readonly string[] _classIds = new string[PlayerCount];
        private readonly List<string>[] _modifierIds = { new List<string>(), new List<string>() };

        public string MapId { get; }

        /// <summary>Player index that takes the first turn (0 or 1).</summary>
        public int FirstPlayer { get; }

        public MatchSetup(string mapId, string player0ClassId, string player1ClassId, int firstPlayer = 0)
        {
            MapId = mapId ?? throw new ArgumentNullException(nameof(mapId));
            _classIds[0] = player0ClassId ?? throw new ArgumentNullException(nameof(player0ClassId));
            _classIds[1] = player1ClassId ?? throw new ArgumentNullException(nameof(player1ClassId));
            if (firstPlayer < 0 || firstPlayer >= PlayerCount) throw new ArgumentOutOfRangeException(nameof(firstPlayer));
            FirstPlayer = firstPlayer;
        }

        public string ClassIdOf(int player) => _classIds[CheckPlayer(player)];

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
