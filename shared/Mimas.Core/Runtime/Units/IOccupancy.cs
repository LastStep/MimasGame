using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>Who stands where. Movement rules only ever ask this; they never enumerate units themselves.</summary>
    public interface IOccupancy
    {
        bool IsOccupied(Hex hex);

        bool TryGetUnitAt(Hex hex, out Unit unit);
    }
}
