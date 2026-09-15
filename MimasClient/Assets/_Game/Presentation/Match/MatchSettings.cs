using UnityEngine;
using Mimas.Core.Content;
using Mimas.Core.Data;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// One place to tweak a local match while the real setup flow (matchmaking, lobby, server) does not
    /// exist yet: which map, which classes, how long a turn is, how the placeholder opponent behaves.
    /// Edit the asset in the Inspector (Assets/_Game/Settings/DefaultMatchSettings.asset) or create another
    /// via Create > Mimas > Match Settings and point the scene at it. Balance data stays in JSON; this asset
    /// only picks ids and dev-loop knobs.
    /// </summary>
    [CreateAssetMenu(fileName = "MatchSettings", menuName = "Mimas/Match Settings")]
    public sealed class MatchSettings : ScriptableObject
    {
        [Header("Board")]
        [Tooltip("maps/*.json id. Leave empty to keep whatever the BoardView has set.")]
        public string MapId = "arena-4";

        [Header("Sides (classes/*.json ids)")]
        public string PlayerClassId = "warrior";
        public string OpponentClassId = "mage";

        [Header("Clock")]
        [Tooltip("timecontrols.json preset; its turnCapMs is the turn length unless overridden below.")]
        public string TimeControlId = "3+2";

        [Tooltip("Seconds per turn. 0 = use the time control's turnCapMs.")]
        [Min(0f)] public float TurnSecondsOverride = 30f;

        [Tooltip("The rope appears when this many seconds remain in a turn (Hearthstone shows it for the last 20 of 75).")]
        [Min(1f)] public float RopeSeconds = 10f;

        [Tooltip("After a turn times out with no action, the next turn starts with only this many seconds; taking an action restores the full turn. 0 disables the penalty.")]
        [Min(0f)] public float IdleTurnSeconds = 7f;

        [Header("Placeholder opponent")]
        [Tooltip("How long the fake opponent waits before moving, and again before ending its turn.")]
        [Min(0f)] public float OpponentThinkSeconds = 1.5f;

        private const float FallbackTurnSeconds = 45f;

        /// <summary>Seconds per turn: the override when set, else the time control's cap, else 45.</summary>
        public float ResolveTurnSeconds(ContentCatalog catalog)
        {
            if (TurnSecondsOverride > 0f) return TurnSecondsOverride;
            TimeControlDef timeControl;
            if (catalog != null && catalog.TimeControls.TryGet(TimeControlId, out timeControl))
                return timeControl.TurnCapMs / 1000f;
            return FallbackTurnSeconds;
        }
    }
}
