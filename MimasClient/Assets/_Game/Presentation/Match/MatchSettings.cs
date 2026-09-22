using UnityEngine;
using Mimas.Core.Content;
using Mimas.Core.Data;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// One place to tweak a local match while the real setup flow (matchmaking, lobby, server) does not
    /// exist yet: which map, which gear each side wears, how long a turn is, how the bot behaves, which
    /// passives each side brings in. Edit the asset in the Inspector (Assets/_Game/Settings/DefaultMatchSettings.asset)
    /// or create another via Create > Mimas > Match Settings and point the scene at it. Balance data stays
    /// in JSON; this asset only picks ids and dev-loop knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "MatchSettings", menuName = "Mimas/Match Settings")]
    public sealed class MatchSettings : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("Unused since part 2 of boons: a practice session plays the map ladder, round by round (design #round rule 3).")]
        public string MapId = "arena-4";

        [Header("Sides (items/*.json ids)")]
        public LoadoutSettings PlayerLoadout = new LoadoutSettings();
        public LoadoutSettings OpponentLoadout = new LoadoutSettings { Weapon = "flintlock", Boots = "blink-boots" };

        [Tooltip("lineages/*.json id you pray to in practice. Its starting Blessing comes with it.")]
        public string PlayerLineage = "hindu";

        [Tooltip("lineages/*.json id the bot prays to in practice.")]
        public string OpponentLineage = "norse";

        [Header("Match")]
        [Tooltip("Seed for the match RNG and the bot. Same seed + same commands = same game.")]
        public uint Seed = 1;

        [Tooltip("Who takes turn 1: 0 = you, 1 = the opponent.")]
        [Range(0, 1)] public int FirstPlayer = 0;

        [Header("Clock")]
        [Tooltip("Unused since M2; the turn length is rules.json clock.turnMs unless overridden below. Time controls with banks and increments are a later design (#time-controls).")]
        public string TimeControlId = "3+2";

        [Tooltip("Seconds per turn in local practice. 0 = use rules.json clock.turnMs, which is what the server uses online.")]
        [Min(0f)] public float TurnSecondsOverride = 30f;

        [Tooltip("The rope appears when this many seconds remain in a turn (Hearthstone shows it for the last 20 of 75).")]
        [Min(1f)] public float RopeSeconds = 10f;

        [Tooltip("After a turn times out with no action, the next turn starts with only this many seconds; taking an action restores the full turn. 0 disables the penalty.")]
        [Min(0f)] public float IdleTurnSeconds = 7f;

        [Header("Bot opponent")]
        [Tooltip("How long the bot waits before each of its actions.")]
        [Min(0f)] public float OpponentThinkSeconds = 1.2f;

        [Header("Feedback")]
        [Tooltip("Pause after a hit lands so the number and the hp change can be read before the next event plays.")]
        [Min(0f)] public float HitPauseSeconds = 0.7f;

        private const float FallbackTurnSeconds = 30f;

        /// <summary>
        /// Seconds per turn: the override when set, else <c>rules.json</c>'s <c>clock.turnMs</c> — the same
        /// number the server times an online turn with, so practice and a real match feel the same length.
        /// </summary>
        public float ResolveTurnSeconds(ContentCatalog catalog)
        {
            if (TurnSecondsOverride > 0f) return TurnSecondsOverride;
            if (catalog != null && catalog.Rules != null && catalog.Rules.Clock != null) return catalog.Rules.Clock.TurnMs / 1000f;
            return FallbackTurnSeconds;
        }
    }
}
