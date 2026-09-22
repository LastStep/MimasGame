using Mimas.Core.Bots;
using Mimas.Core.Content;
using Mimas.Core.Match;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    using Session = Mimas.Core.Session.Session;

    /// <summary>
    /// The practice series, kept alive across the Arena reload that every round after the first causes
    /// (P7, ADR-036). Online that job belongs to the server and the pending <c>match.start</c> in
    /// <c>NetClient</c>; offline there is nobody else to hold it, so this object does — one
    /// <see cref="Session"/>, one bot, <see cref="Object.DontDestroyOnLoad"/>, and a
    /// <see cref="LocalMatchDriver"/> per scene that adopts it.
    /// <para>
    /// The clocks are deliberately <b>not</b> here: every round and every draft arms its own, so nothing
    /// about them needs to outlive a scene.
    /// </para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LocalSessionHost : MonoBehaviour
    {
        public static LocalSessionHost Instance { get; private set; }

        /// <summary>The series being played. Never null once the host exists.</summary>
        public Session Session { get; private set; }

        public RandomBot Bot { get; private set; }

        /// <summary>The settings this session was built from; a different asset means a different session.</summary>
        public MatchSettings Settings { get; private set; }

        /// <summary>False until <see cref="Session.Start"/> has been called for round 1.</summary>
        public bool Started { get; set; }

        /// <summary>
        /// The live host, or a fresh one. A finished session is replaced: opening the Arena after a practice
        /// series ended starts another (§13), which is what restarting the scene has always meant here.
        /// </summary>
        public static LocalSessionHost For(ContentCatalog catalog, MatchSettings settings)
        {
            if (Instance != null && Instance.Session != null && !Instance.Session.IsOver
                && ReferenceEquals(Instance.Settings, settings))
                return Instance;

            if (Instance != null) Destroy(Instance.gameObject);

            var go = new GameObject("LocalSessionHost");
            DontDestroyOnLoad(go);
            var host = go.AddComponent<LocalSessionHost>();
            host.Build(catalog, settings);
            Instance = host;
            return host;
        }

        private void Build(ContentCatalog catalog, MatchSettings settings)
        {
            Settings = settings;
            var setup = new Mimas.Core.Session.SessionSetup(
                new PlayerBuild(settings.PlayerLoadout.ToLoadout(), settings.PlayerLineage),
                new PlayerBuild(settings.OpponentLoadout.ToLoadout(), settings.OpponentLineage));
            Session = new Session(catalog, setup, settings.Seed);
            Bot = new RandomBot(settings.Seed ^ 0x9E3779B9u);
            Started = false;
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
        }
    }
}
