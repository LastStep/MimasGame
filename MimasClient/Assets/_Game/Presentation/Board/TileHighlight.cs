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
    }
}
