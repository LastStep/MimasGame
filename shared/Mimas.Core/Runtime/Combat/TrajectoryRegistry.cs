using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Combat
{
    /// <summary>
    /// Maps an <see cref="Mimas.Core.Data.AttackDef.Trajectory"/> string to the resolver that implements it. The
    /// one place that knows which trajectories exist, so the HUD preview, the server and the bots cannot drift.
    /// Unlike movement, an unknown trajectory is not "no options" but a bug in the content pipeline: it throws.
    /// </summary>
    public sealed class TrajectoryRegistry
    {
        private readonly Dictionary<string, ITrajectoryResolver> _byMode = new Dictionary<string, ITrajectoryResolver>(StringComparer.Ordinal);
        private readonly List<ITrajectoryResolver> _ordered = new List<ITrajectoryResolver>();

        public IReadOnlyList<ITrajectoryResolver> All => _ordered;

        public int Count => _ordered.Count;

        /// <summary>Direct, arc and sky. Callers that add modes register them on top.</summary>
        public static TrajectoryRegistry Default()
        {
            var registry = new TrajectoryRegistry();
            registry.Register(new DirectTrajectory());
            registry.Register(new ArcTrajectory());
            registry.Register(new SkyTrajectory());
            return registry;
        }

        public void Register(ITrajectoryResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (string.IsNullOrEmpty(resolver.Mode)) throw new ArgumentException("Resolver mode must be non-empty.", nameof(resolver));
            if (_byMode.ContainsKey(resolver.Mode)) throw new InvalidOperationException($"A resolver for trajectory '{resolver.Mode}' is already registered.");
            _byMode[resolver.Mode] = resolver;
            _ordered.Add(resolver);
        }

        public bool TryGet(string mode, out ITrajectoryResolver resolver)
        {
            if (mode == null)
            {
                resolver = null;
                return false;
            }
            return _byMode.TryGetValue(mode, out resolver);
        }

        public bool Supports(string mode) => mode != null && _byMode.ContainsKey(mode);

        /// <summary>Runs one flight. Throws on an unknown mode: content that reaches here has already been parsed.</summary>
        public bool IsClear(string mode, in TrajectoryContext ctx, out Hex blockedAt)
        {
            ITrajectoryResolver resolver;
            if (!TryGet(mode, out resolver)) throw new InvalidOperationException($"Unknown trajectory '{mode}'.");
            return resolver.IsClear(in ctx, out blockedAt);
        }
    }
}
