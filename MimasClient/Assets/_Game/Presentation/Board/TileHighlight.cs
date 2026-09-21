namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The mutually exclusive visual states a board tile can be painted in. Ordered by painting
    /// precedence as applied by <see cref="BoardView"/>: a hovered tile wins over a path tile,
    /// which wins over a targetable tile, which wins over a merely reachable tile.
    /// </summary>
    public enum TileHighlight
    {
        /// <summary>Terrain colour only.</summary>
        None = 0,

        /// <summary>Inside the selected unit's movement range.</summary>
        Reachable = 1,

        /// <summary>Holds a legal target for the armed attack.</summary>
        Targetable = 2,

        /// <summary>A step on the previewed path to the hovered tile.</summary>
        PathPreview = 3,

        /// <summary>Directly under the cursor.</summary>
        Hovered = 4,

        /// <summary>
        /// The tile that stops the shot being aimed right now — the hex the rules named in
        /// <c>TargetCheck.BlockedAt</c>. A pillar beside the drawn line can block a shot the picture makes
        /// look open (design <c>#line-of-sight</c> rule 5), so the hex the rules blame is tinted rather
        /// than left for the player to guess at (T-0006).
        /// </summary>
        Blocker = 5,
    }
}
