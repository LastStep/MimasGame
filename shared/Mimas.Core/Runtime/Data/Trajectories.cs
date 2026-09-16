namespace Mimas.Core.Data
{
    /// <summary>
    /// How an attack travels from the attacker's aim point to the target's (design: #trajectories). The string
    /// is the resolver key, exactly as <see cref="Mimas.Core.Movement.MovementModes"/> is for movement, so a new
    /// mode is one resolver class plus one register line. Unknown modes fail closed at parse and at resolve.
    /// </summary>
    public static class Trajectories
    {
        /// <summary>The straight ray: the same test as sight.</summary>
        public const string Direct = "direct";

        /// <summary>An integer parabola over the same endpoints, peaking <c>apex</c> above the higher end.</summary>
        public const string Arc = "arc";

        /// <summary>Nothing in between matters.</summary>
        public const string Sky = "sky";

        public static bool IsKnown(string trajectory)
            => trajectory == Direct || trajectory == Arc || trajectory == Sky;
    }
}
