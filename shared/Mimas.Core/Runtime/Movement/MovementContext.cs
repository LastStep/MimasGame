using System;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;
using Mimas.Core.Units;

namespace Mimas.Core.Movement
{
    /// <summary>
    /// Everything a resolver needs to answer "where can this unit go with this movement": the board, the
    /// terrain catalogue, who is standing where, the mover's origin and the movement definition. Built fresh
    /// per query; it holds no mutable state of its own.
    /// </summary>
    public sealed class MovementContext
    {
        public TileMap Map { get; }
        public TerrainSet Terrains { get; }
        public IOccupancy Occupancy { get; }
        public Hex Origin { get; }
        public MovementDef Def { get; }

        public MovementContext(TileMap map, TerrainSet terrains, IOccupancy occupancy, Hex origin, MovementDef def)
        {
            Map = map ?? throw new ArgumentNullException(nameof(map));
            Terrains = terrains ?? throw new ArgumentNullException(nameof(terrains));
            Occupancy = occupancy ?? throw new ArgumentNullException(nameof(occupancy));
            Origin = origin;
            Def = def ?? throw new ArgumentNullException(nameof(def));
        }

        /// <summary>Convenience for the common case of moving a <see cref="Unit"/> from where it stands.</summary>
        public static MovementContext For(TileMap map, TerrainSet terrains, IOccupancy occupancy, Unit unit, MovementDef def)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            return new MovementContext(map, terrains, occupancy, unit.Position, def);
        }

        /// <summary>Height of the origin tile, or 0 when the origin is off the map.</summary>
        public int OriginHeight
        {
            get
            {
                Tile tile;
                return Map.TryGet(Origin, out tile) ? tile.Height : 0;
            }
        }

        public bool CanEnter(Tile tile) => Def.CanEnter(tile);

        public int EntryCost(Tile tile) => Def.EntryCost(tile, Terrains);

        /// <summary>A tile some other unit stands on. The origin itself never counts as occupied.</summary>
        public bool IsBlockedByUnit(Hex hex) => hex != Origin && Occupancy.IsOccupied(hex);
    }
}
