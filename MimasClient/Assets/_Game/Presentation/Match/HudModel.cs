using System;
using System.Collections.Generic;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Match;
using Mimas.Core.Units;
using CoreUnitView = Mimas.Core.Match.UnitView;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The HUD's composition rules as pure functions (spec H §3, §7.2): the turn track's burning fraction, when
    /// End Turn lights, the score by seat, the round moments, the draft headline, the preview's one "?" row, the
    /// enemy's boon marks, and the action bar built from the examine builder's tiles. <see cref="MatchSession"/>
    /// composes the HUD from these and <c>MatchHudView</c> only formats what comes out; static and scene-free
    /// so EditMode tests can reach them.
    /// </summary>
    public static class HudModel
    {
        /// <summary>The preview's row for everything of theirs you have not seen: one row, whatever the count (docs/ui/hud.md §5).</summary>
        public const string HiddenLineLabel = "A boon of theirs you have not seen";

        /// <summary>
        /// How much of the active half of the track is still lit (hud.md §3.1): all of it outside the rope, then
        /// shrinking towards the centre as the last <paramref name="ropeSeconds"/> burn, to nothing at zero.
        /// </summary>
        public static float LitFraction(float remaining, float ropeSeconds)
        {
            if (ropeSeconds <= 0f || remaining >= ropeSeconds) return 1f;
            if (remaining <= 0f) return 0f;
            return remaining / ropeSeconds;
        }

        /// <summary>True while the rope burns: the ember replaces the marker.</summary>
        public static bool IsBurning(float remaining, float ropeSeconds, float turnTotal)
            => turnTotal > 0f && ropeSeconds > 0f && remaining > 0f && remaining <= ropeSeconds;

        /// <summary>End Turn lights (language §5 <c>state.done</c>): it is your turn and nothing on the bar is affordable.</summary>
        public static bool IsDone(bool isMyTurn, IReadOnlyList<HudAction> actions)
        {
            if (!isMyTurn) return false;
            if (actions == null) return true;
            for (int i = 0; i < actions.Count; i++) if (actions[i].Affordable) return false;
            return true;
        }

        /// <summary>The series score with the local seat's first.</summary>
        public static (int Mine, int Theirs) ScoresBySeat(int score0, int score1, int localSeat)
            => localSeat == 1 ? (score1, score0) : (score0, score1);

        // ---- the round moments (docs/ui/between-rounds.md §3) --------------------------------------------

        public static HudMoment RoundStart(int round, string mapName, int firstPlayer, int localSeat)
        {
            return new HudMoment
            {
                Kind = HudMomentKind.RoundStart,
                Round = round,
                MapName = mapName,
                IMoveFirst = firstPlayer == localSeat,
            };
        }

        /// <param name="winnerName">The name of whoever took the round, already chosen by seat.</param>
        public static HudMoment RoundResult(int round, int winner, int localSeat, string winnerName, int score0, int score1, string reason)
        {
            (int mine, int theirs) = ScoresBySeat(score0, score1, localSeat);
            return new HudMoment
            {
                Kind = HudMomentKind.RoundResult,
                Round = round,
                IWon = winner == localSeat,
                WinnerName = winnerName,
                ScoreMine = mine,
                ScoreTheirs = theirs,
                Reason = reason,
            };
        }

        /// <summary>The series result: the only moment with a way out, which shows a beat later (<see cref="HudMoment.ShowBack"/>).</summary>
        public static HudMoment SeriesResult(int winner, int localSeat, int score0, int score1, string reason, string backLabel)
        {
            (int mine, int theirs) = ScoresBySeat(score0, score1, localSeat);
            return new HudMoment
            {
                Kind = HudMomentKind.SeriesResult,
                IWon = winner == localSeat,
                ScoreMine = mine,
                ScoreTheirs = theirs,
                Reason = reason,
                BackLabel = backLabel,
            };
        }

        /// <summary>The server no longer has the match: no result, no winner, only a way back, at once.</summary>
        public static HudMoment MatchLost(string reason, string backLabel)
        {
            return new HudMoment
            {
                Kind = HudMomentKind.MatchLost,
                Reason = reason,
                ShowBack = true,
                BackLabel = backLabel,
            };
        }

        /// <summary>"ROUND 2 TO GUEST-2869 · 1 – 1": the round that just ended, who took it, the score yours first.</summary>
        public static string DraftHeadline(int round, string winnerName, int mine, int theirs)
            => "ROUND " + round + " TO " + (winnerName ?? string.Empty).ToUpperInvariant() + " · " + mine + " – " + theirs;

        // ---- the attack preview (hud.md §5) ------------------------------------------------------------

        /// <summary>Adds the one "?" row when anything of theirs you have not seen may move the number.</summary>
        public static void AddHiddenLine(List<HudPreviewLine> lines, int unknownCount)
        {
            if (lines == null || unknownCount <= 0) return;
            lines.Add(new HudPreviewLine { Label = HiddenLineLabel, Unknown = true });
        }

        /// <summary>The preview's type line word for how the shot travels (design #trajectories).</summary>
        public static string TrajectoryWord(string trajectory)
        {
            switch (trajectory)
            {
                case Trajectories.Arc: return "lobbed";
                case Trajectories.Sky: return "from the sky";
                default: return "straight";
            }
        }

        // ---- tags (hud.md §3.5) ------------------------------------------------------------------------

        /// <summary>One mark per boon an enemy unit holds, in grant order: its kind once revealed, null while unseen.</summary>
        public static void BoonMarks(ContentCatalog catalog, CoreUnitView unit, List<HudBoonMark> into)
        {
            into.Clear();
            if (unit == null || unit.IsMine) return;
            for (int i = 0; i < unit.Boons.Count; i++)
            {
                KnownEntry entry = unit.Boons[i];
                BoonDef def;
                string kind = entry.Revealed && catalog != null && catalog.Boons.TryGet(entry.Id, out def) ? def.Kind : null;
                into.Add(new HudBoonMark { Kind = kind });
            }
        }

        // ---- the action bar (hud.md §3.2) ----------------------------------------------------------------

        /// <summary>
        /// The bar's abilities, resolved through the driver's state rather than read from the catalogue (ADR-034):
        /// a Sigil's grant and an Enchant's changed cost, range or damage live on the unit's overlay.
        /// </summary>
        public static void CollectAbilities(MatchState rules, int unitId, List<AbilityDef> into)
        {
            into.Clear();
            Unit unit;
            if (rules == null || unitId < 0 || !rules.Units.TryGet(unitId, out unit)) return;
            for (int i = 0; i < unit.AbilityIds.Count; i++)
            {
                AbilityDef def;
                if (rules.ResolveAbility(unit, unit.AbilityIds[i], out def)) into.Add(def);
            }
        }

        /// <summary>
        /// One <see cref="HudAction"/> per ability, each carrying the examine builder's tile for it (ADR-040) and
        /// the tile's flags: changed, added by a Sigil, not yet seen by the opponent.
        /// </summary>
        public static void BuildActions(ContentCatalog catalog, PlayerView view, MatchState rules, int unitId,
            IReadOnlyList<AbilityDef> abilities, bool canAct, List<HudAction> into, Func<AttackDef, string> detail = null)
        {
            into.Clear();
            CoreUnitView me = view != null ? view.FindUnit(unitId) : null;
            var tiles = new Dictionary<string, HudTile>(StringComparer.Ordinal);
            if (me != null) ExamineModelBuilder.ActionTiles(catalog, view, rules, unitId, tiles);

            for (int i = 0; i < abilities.Count; i++)
            {
                AbilityDef def = abilities[i];
                bool affordable = me != null && me.Ap >= def.Cost;
                HudTile tile;
                tiles.TryGetValue(def.Id, out tile);
                into.Add(new HudAction(def.Id, def.Name, def.Description, detail != null ? detail(def as AttackDef) : null,
                    def.Icon, def.Category, def.Cost, affordable, canAct && affordable,
                    modified: tile != null && tile.Changed,
                    added: tile != null && tile.Added,
                    unseenByThem: tile != null && tile.UnseenByThem,
                    hover: tile));
            }
        }
    }
}
