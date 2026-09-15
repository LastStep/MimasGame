using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// The units in a match, in insertion order (never a dictionary walk: order must be stable for
    /// determinism). Lookups are linear: a 1v1 has a handful of units, and this keeps the type trivially
    /// correct while the unit model is still forming.
    /// </summary>
    public sealed class UnitSet : IOccupancy
    {
        private readonly List<Unit> _units = new List<Unit>();

        public IReadOnlyList<Unit> All => _units;

        public int Count => _units.Count;

        public void Add(Unit unit)
        {
            if (unit == null) throw new ArgumentNullException(nameof(unit));
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Id == unit.Id) throw new InvalidOperationException($"Duplicate unit id {unit.Id}.");
                if (_units[i].Position == unit.Position) throw new InvalidOperationException($"Tile {unit.Position} is already occupied.");
            }
            _units.Add(unit);
        }

        public bool TryGet(int id, out Unit unit)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Id == id)
                {
                    unit = _units[i];
                    return true;
                }
            }
            unit = null;
            return false;
        }

        public Unit Get(int id)
        {
            Unit unit;
            if (TryGet(id, out unit)) return unit;
            throw new KeyNotFoundException($"No unit with id {id}.");
        }

        public bool IsOccupied(Hex hex)
        {
            Unit unused;
            return TryGetUnitAt(hex, out unused);
        }

        public bool TryGetUnitAt(Hex hex, out Unit unit)
        {
            for (int i = 0; i < _units.Count; i++)
            {
                if (_units[i].Position == hex)
                {
                    unit = _units[i];
                    return true;
                }
            }
            unit = null;
            return false;
        }
    }
}
