using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Maps a <see cref="MovementDef.Mode"/> string to the resolver that implements it. The one place that
    /// knows which movement kinds exist: UI highlighting, server validation and bots all go through here, so
    /// none of them can drift. Unknown modes fail closed (no options, <see cref="MoveRejectReason.UnknownMovement"/>).
    /// </summary>
    public sealed class MovementResolverRegistry
    {
        private readonly Dictionary<string, IMovementResolver> _byMode = new Dictionary<string, IMovementResolver>(StringComparer.Ordinal);
        private readonly List<IMovementResolver> _ordered = new List<IMovementResolver>();

        public IReadOnlyList<IMovementResolver> All => _ordered;

        /// <summary>Walk, jump and teleport. Callers that add modes register them on top.</summary>
        public static MovementResolverRegistry CreateDefault()
        {
            var registry = new MovementResolverRegistry();
            registry.Register(new WalkResolver());
            registry.Register(new JumpResolver());
            registry.Register(new TeleportResolver());
            return registry;
        }

        public void Register(IMovementResolver resolver)
        {
            if (resolver == null) throw new ArgumentNullException(nameof(resolver));
            if (string.IsNullOrEmpty(resolver.Mode)) throw new ArgumentException("Resolver mode must be non-empty.", nameof(resolver));
            if (_byMode.ContainsKey(resolver.Mode)) throw new InvalidOperationException($"A resolver for mode '{resolver.Mode}' is already registered.");
            _byMode[resolver.Mode] = resolver;
            _ordered.Add(resolver);
        }

        public bool TryGet(string mode, out IMovementResolver resolver)
        {
            if (mode == null)
            {
                resolver = null;
                return false;
            }
            return _byMode.TryGetValue(mode, out resolver);
        }

        public bool Supports(string mode) => mode != null && _byMode.ContainsKey(mode);

        /// <summary>All legal moves for the context's movement; empty when the mode is unknown.</summary>
        public MovementOptions Enumerate(MovementContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            IMovementResolver resolver;
            return _byMode.TryGetValue(context.Def.Mode, out resolver) ? resolver.Enumerate(context) : MovementOptions.Empty;
        }

        /// <summary>Validates one destination; <see cref="MoveRejectReason.UnknownMovement"/> when the mode is unknown.</summary>
        public MoveResult Validate(MovementContext context, Hex destination)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            IMovementResolver resolver;
            return _byMode.TryGetValue(context.Def.Mode, out resolver)
                ? resolver.Validate(context, destination)
                : MoveResult.Reject(MoveRejectReason.UnknownMovement);
        }
    }
}
