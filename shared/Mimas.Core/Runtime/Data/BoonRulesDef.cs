using System;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// The floors boons are held to (<c>rules.json</c> <c>boons</c>, design: #stats rule 3, #enchant, #blessing):
    /// a self trade-off may lower a stat but never below these, and an Enchant may lower an ability's cost
    /// but never below <see cref="MinCost"/> ("nothing is ever free", 21 Sep 2026). Every other stat key is
    /// floored at 0.
    /// </summary>
    public sealed class BoonRulesDef
    {
        /// <summary>The least a unit's maximum hit points can be after every boon. At least 1.</summary>
        public int HpFloor { get; }

        /// <summary>The least a unit's action points per turn can be after every boon. 0 or more.</summary>
        public int ApFloor { get; }

        /// <summary>The least an ability's action-point cost can be after every override. 0 or more.</summary>
        public int MinCost { get; }

        public BoonRulesDef(int hpFloor, int apFloor, int minCost)
        {
            if (hpFloor < 1) throw new ArgumentOutOfRangeException(nameof(hpFloor), "boons.floors.hp must be at least 1.");
            if (apFloor < 0) throw new ArgumentOutOfRangeException(nameof(apFloor), "boons.floors.ap must not be negative.");
            if (minCost < 0) throw new ArgumentOutOfRangeException(nameof(minCost), "boons.minCost must not be negative.");
            HpFloor = hpFloor;
            ApFloor = apFloor;
            MinCost = minCost;
        }

        /// <summary>hp 1, ap 1, cost 1: the fallback for fixtures that build a <see cref="RulesDef"/> by hand.</summary>
        public static BoonRulesDef Default() => new BoonRulesDef(1, 1, 1);

        internal static BoonRulesDef FromJsonAt(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            if (!(MapJson.Require(obj, "floors", where) is JObject floors))
                throw new MapLoadException($"{where}.floors must be an object with 'hp' and 'ap'.");
            int hp = MapJson.RequireInt(floors, "hp", where + ".floors");
            int ap = MapJson.RequireInt(floors, "ap", where + ".floors");
            int minCost = MapJson.RequireInt(obj, "minCost", where);
            if (hp < 1) throw new MapLoadException($"{where}.floors.hp must be at least 1.");
            if (ap < 0) throw new MapLoadException($"{where}.floors.ap must not be negative.");
            if (minCost < 0) throw new MapLoadException($"{where}.minCost must not be negative.");
            return new BoonRulesDef(hp, ap, minCost);
        }

        public override string ToString() => $"floors hp {HpFloor} / ap {ApFloor}, min cost {MinCost}";
    }
}
