using System;
using Newtonsoft.Json.Linq;

namespace Mimas.Core.Data
{
    /// <summary>
    /// The shape of a session (<c>rules.json</c> <c>series</c>, design: #session): how many rounds are played
    /// at most, from which the number needed to win follows. Best of 3 at launch; the count is data so a
    /// longer series is a number change (design: <c>q-session-length</c>, closed 21 Sep 2026).
    /// </summary>
    public sealed class SeriesDef
    {
        /// <summary>Maximum rounds in a session. Odd, at least 1.</summary>
        public int BestOf { get; }

        /// <summary>Rounds a player must win to take the session: <c>bestOf / 2 + 1</c>.</summary>
        public int RoundsToWin => BestOf / 2 + 1;

        public SeriesDef(int bestOf)
        {
            if (bestOf < 1 || bestOf % 2 == 0) throw new ArgumentOutOfRangeException(nameof(bestOf), "series.bestOf must be odd and at least 1.");
            BestOf = bestOf;
        }

        /// <summary>Best of 3: the fallback for fixtures that build a <see cref="RulesDef"/> by hand.</summary>
        public static SeriesDef Default() => new SeriesDef(3);

        internal static SeriesDef FromJsonAt(JObject obj, string where)
        {
            if (obj == null) throw new MapLoadException($"{where} must be an object.");
            int bestOf = MapJson.RequireInt(obj, "bestOf", where);
            if (bestOf < 1 || bestOf % 2 == 0) throw new MapLoadException($"{where}.bestOf must be odd and at least 1 (got {bestOf}).");
            return new SeriesDef(bestOf);
        }

        public override string ToString() => $"best of {BestOf}";
    }
}
