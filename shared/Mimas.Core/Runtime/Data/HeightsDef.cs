using System;
using Mimas.Core.Grid;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// How a map's integer height <em>levels</em> become the <em>body units</em> that sight and trajectories are
    /// measured in (<c>rules.json</c> <c>heights</c>, design: #board, #line-of-sight). One level is
    /// <see cref="UnitsPerLevel"/> units tall; a hero's body is <see cref="Body"/> units above the tile top and
    /// every attack leaves from and lands at <see cref="Aim"/>. Keeping the conversion in data is what lets a
    /// level-1 step be shootable over while a level-2 plateau is not, without a single float in the rules.
    /// </summary>
    public sealed class HeightsDef
    {
        /// <summary>Height units one map level is worth. At least 1.</summary>
        public int UnitsPerLevel { get; }

        /// <summary>A hero's body height in units above the tile top. At least 1.</summary>
        public int Body { get; }

        /// <summary>Where attacks leave from and land, in units above the tile top. 1..<see cref="Body"/>.</summary>
        public int Aim { get; }

        public HeightsDef(int unitsPerLevel, int body, int aim)
        {
            if (unitsPerLevel < 1) throw new ArgumentOutOfRangeException(nameof(unitsPerLevel), "heights.unitsPerLevel must be at least 1.");
            if (body < 1) throw new ArgumentOutOfRangeException(nameof(body), "heights.body must be at least 1.");
            if (aim < 1 || aim > body) throw new ArgumentOutOfRangeException(nameof(aim), "heights.aim must be between 1 and heights.body.");
            UnitsPerLevel = unitsPerLevel;
            Body = body;
            Aim = aim;
        }

        /// <summary>Top of a tile's terrain column in height units.</summary>
        public int TileTop(Tile tile)
        {
            if (tile == null) throw new ArgumentNullException(nameof(tile));
            return tile.Height * UnitsPerLevel;
        }

        /// <summary>Top of a height level in units (a tile-less caller already has the level).</summary>
        public int TopOfLevel(int level) => level * UnitsPerLevel;

        internal static HeightsDef FromJsonAt(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            int unitsPerLevel = MapJson.RequireInt(obj, "unitsPerLevel", where);
            int body = MapJson.RequireInt(obj, "body", where);
            int aim = MapJson.RequireInt(obj, "aim", where);
            if (unitsPerLevel < 1) throw new MapLoadException($"{where}.unitsPerLevel must be at least 1.");
            if (body < 1) throw new MapLoadException($"{where}.body must be at least 1.");
            if (aim < 1 || aim > body) throw new MapLoadException($"{where}.aim must be between 1 and body ({body}).");
            return new HeightsDef(unitsPerLevel, body, aim);
        }
    }
}
