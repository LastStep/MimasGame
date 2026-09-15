using System;
using UnityEngine;
using Mimas.Core.Content;

namespace Mimas.Client.Content
{
    /// <summary>
    /// Loads the <see cref="ContentCatalog"/> once for the whole client and hands it out. Anything that
    /// needs content calls <see cref="EnsureLoaded"/> from its own <c>Awake</c>, so script execution order
    /// never matters. A failed load logs every error once and leaves <see cref="Catalog"/> null; callers
    /// treat null as "do not start". The catalogue's hash is what the server will compare against.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-1000)]
    public sealed class ContentBootstrap : MonoBehaviour
    {
        [SerializeField] private GameDataManifest _manifest;

        [Tooltip("Log the catalogue summary (hash, counts) when it loads.")]
        [SerializeField] private bool _logSummary = true;

        private bool _attempted;

        /// <summary>The loaded catalogue, or null before loading or after a failed load.</summary>
        public ContentCatalog Catalog { get; private set; }

        public bool IsLoaded => Catalog != null;

        /// <summary>True when a load was attempted and failed. Errors are already in the console.</summary>
        public bool HasFailed => _attempted && Catalog == null;

        /// <summary>Raised once with the catalogue on a successful load.</summary>
        public event Action<ContentCatalog> Loaded;

        private void Awake()
        {
            EnsureLoaded();
        }

        private void OnDestroy()
        {
            Loaded = null;
        }

        /// <summary>Loads on first call, then returns the same catalogue. Returns null when loading failed.</summary>
        public ContentCatalog EnsureLoaded()
        {
            if (_attempted) return Catalog;
            _attempted = true;

            if (_manifest == null)
            {
                Debug.LogError("[ContentBootstrap] No GameDataManifest assigned.", this);
                return null;
            }

            try
            {
                Catalog = _manifest.LoadCatalog();
            }
            catch (ContentLoadException e)
            {
                Debug.LogError("[ContentBootstrap] " + e.Message, this);
                return null;
            }

            if (_logSummary)
            {
                Debug.Log("[ContentBootstrap] Content loaded: " + Catalog.Files.Count + " files, "
                    + Catalog.Abilities.Count + " abilities, " + Catalog.Classes.Count + " classes, "
                    + Catalog.Maps.Count + " maps, " + Catalog.Terrains.Count + " terrains. Hash " + Catalog.Hash.Substring(0, 12) + "…");
            }

            Action<ContentCatalog> handler = Loaded;
            if (handler != null) handler(Catalog);
            return Catalog;
        }
    }
}
