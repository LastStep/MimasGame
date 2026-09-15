using Mimas.Core.Geometry;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// One movement mode's geometry: how it finds destinations and what it checks along the way. Resolvers
    /// are stateless and deterministic. Terrain costs and enterability are not their concern; they read those
    /// from the <see cref="MovementContext"/>, which is why a new mode is one class and one registry line.
    /// </summary>
    public interface IMovementResolver
    {
        /// <summary>The <see cref="MovementDef.Mode"/> this resolver handles.</summary>
        string Mode { get; }

        /// <summary>Every legal destination with its plan, in a deterministic order.</summary>
        MovementOptions Enumerate(MovementContext context);

        /// <summary>
        /// Checks one destination. Must agree with <see cref="Enumerate"/>: accepted here if and only if the
        /// destination appears there, with an identical plan.
        /// </summary>
        MoveResult Validate(MovementContext context, Hex destination);
    }
}
