using System;
using System.Collections.Generic;
using Mimas.Core.Geometry;

namespace Mimas.Core.Units
{
    /// <summary>
    /// Every body of a match: the <see cref="UnitSet"/> plus the props the map placed. Movement, sight and
    /// targeting take this, so a prop blocks a walk and stops an arrow through exactly the same code path a
    /// hero does. <see cref="All"/> is units in <see cref="UnitSet.All"/> order followed by props in map
    /// authored order — a fixed order, because determinism forbids a dictionary walk.
    /// </summary>
    public sealed class BodySet : IOccupancy, IBodyLookup
    {
        private readonly UnitSet _units;
        private readonly List<Prop> _props = new List<Prop>();
        private readonly List<IBody> _all = new List<IBody>();

        public BodySet(UnitSet units)
        {
            _units = units ?? throw new ArgumentNullException(nameof(units));
        }

        public UnitSet Units => _units;

        /// <summary>Props in map authored order.</summary>
        public IReadOnlyList<Prop> Props => _props;

        public IReadOnlyList<IBody> All
        {
            get
            {
                // Units are never removed, but a unit added after construction must still appear.
                if (_all.Count != _units.Count + _props.Count) Rebuild();
                return _all;
            }
        }

        public void AddProp(Prop prop)
        {
            if (prop == null) throw new ArgumentNullException(nameof(prop));
            for (int i = 0; i < _props.Count; i++)
                if (_props[i].Id == prop.Id) throw new InvalidOperationException($"Duplicate prop id {prop.Id}.");
            if (IsOccupied(prop.Position)) throw new InvalidOperationException($"Tile {prop.Position} is already occupied.");
            _props.Add(prop);
            Rebuild();
        }

        /// <summary>A living body stands here: a unit or a prop that has not been destroyed.</summary>
        public bool IsOccupied(Hex hex)
        {
            IBody unused;
            return TryGetBodyAt(hex, out unused);
        }

        /// <summary>The living <em>unit</em> on this hex. A prop is not a unit; ask <see cref="TryGetBodyAt"/> for that.</summary>
        public bool TryGetUnitAt(Hex hex, out Unit unit) => _units.TryGetUnitAt(hex, out unit);

        public bool TryGetBodyAt(Hex hex, out IBody body)
        {
            Unit unit;
            if (_units.TryGetUnitAt(hex, out unit))
            {
                body = unit;
                return true;
            }
            for (int i = 0; i < _props.Count; i++)
            {
                if (_props[i].IsAlive && _props[i].Position == hex)
                {
                    body = _props[i];
                    return true;
                }
            }
            body = null;
            return false;
        }

        public bool TryGetBody(int id, out IBody body)
        {
            Unit unit;
            if (_units.TryGet(id, out unit))
            {
                body = unit;
                return true;
            }
            for (int i = 0; i < _props.Count; i++)
            {
                if (_props[i].Id == id)
                {
                    body = _props[i];
                    return true;
                }
            }
            body = null;
            return false;
        }

        private void Rebuild()
        {
            _all.Clear();
            for (int i = 0; i < _units.All.Count; i++) _all.Add(_units.All[i]);
            for (int i = 0; i < _props.Count; i++) _all.Add(_props[i]);
        }
    }
}
