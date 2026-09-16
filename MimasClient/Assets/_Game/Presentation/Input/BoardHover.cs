using Mimas.Core.Geometry;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// What the cursor resolved to this frame: always a tile, plus the body standing on it when the ray
    /// hit the body itself. Bodies win over the tile they stand on (design: #presentation, snap priority),
    /// so hovering any part of a hero or a prop targets that body rather than the hex behind its feet.
    /// A value type with no allocation — <see cref="BoardInputController"/> compares two of these per frame.
    /// </summary>
    public readonly struct BoardHover
    {
        /// <summary>The tile under the resolved point. Never null while <see cref="Any"/> is true.</summary>
        public readonly TileView Tile;

        /// <summary>The hero whose body was hit, or null.</summary>
        public readonly UnitView Unit;

        /// <summary>The prop whose body was hit, or null.</summary>
        public readonly PropView Prop;

        public BoardHover(TileView tile, UnitView unit, PropView prop)
        {
            Tile = tile;
            Unit = unit;
            Prop = prop;
        }

        /// <summary>True when the cursor is over the board at all.</summary>
        public bool Any => Tile != null;

        /// <summary>Coordinate of <see cref="Tile"/>. Only read it when <see cref="Any"/>.</summary>
        public Hex Hex => Tile.Coord;

        /// <summary>True when the two hovers resolved to the same tile and the same body.</summary>
        public bool Same(in BoardHover other) => Tile == other.Tile && Unit == other.Unit && Prop == other.Prop;
    }
}
