using System;
using System.Collections.Generic;
using System.Reflection;
using Mimas.Client.Presentation;
using Mimas.Core.Combat;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using Mimas.Core.Movement;
using Mimas.Core.Session;
using UnityEditor;
using UnityEngine;
using CoreUnitView = Mimas.Core.Match.UnitView;
using HeroView = Mimas.Client.Presentation.UnitView;

namespace Mimas.Client.Editor
{
    /// <summary>
    /// Puts the round-3 fixture on the live match HUD, for the acceptance screenshots of spec H §14 that a
    /// practice game cannot reach on demand (§11 step 5): your turn, armed, their turn, last seconds, a refused shot, the
    /// draft, the round card, a round's result, the series result, and the ink plate on either hero. Play Mode
    /// only, with a practice match running in the Arena.
    /// <para>
    /// The fixture is a real <see cref="MatchState"/> on the shipped content and on the board the practice match
    /// built: your hero is the examine preview's Greek (Longbow, Ember Circlet, Leaping Boots, Leather Jerkin;
    /// Athena's Guard, Hera's Resolve, Apollo's Bowstring, Nike's Jab), who has walked until one action point is
    /// left; the enemy is a Norse Flintlock with Thor's Vigour (public from round start), Thor's Might and Skadi's
    /// Hide unseen. A stand-in <see cref="IMatchDriver"/> hands it to the presenter, so every word on the HUD is
    /// composed by the same code a real match runs (<c>MatchSession</c>, <c>HudModel</c>). Time is stopped
    /// (<c>Time.timeScale = 0</c>); <c>Mimas/HUD/End preview</c> reloads the Arena and practice carries on.
    /// </para>
    /// </summary>
    public static class HudPreview
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        private enum Shot { YourTurn, Armed, TheirTurn, LastSeconds, Blocked, Draft, DraftKept, RoundCard, RoundResult, SeriesResult, ExamineThem, ExamineYou }

        [MenuItem("Mimas/HUD/Preview round 3 (your turn)")] public static void YourTurn() => Preview(Shot.YourTurn);
        [MenuItem("Mimas/HUD/Preview round 3 (armed)")] public static void Armed() => Preview(Shot.Armed);
        [MenuItem("Mimas/HUD/Preview round 3 (their turn)")] public static void TheirTurn() => Preview(Shot.TheirTurn);
        [MenuItem("Mimas/HUD/Preview round 3 (last seconds)")] public static void LastSeconds() => Preview(Shot.LastSeconds);
        [MenuItem("Mimas/HUD/Preview round 3 (blocked)")] public static void Blocked() => Preview(Shot.Blocked);
        [MenuItem("Mimas/HUD/Preview round 3 (draft)")] public static void Draft() => Preview(Shot.Draft);
        [MenuItem("Mimas/HUD/Preview round 3 (draft, kept)")] public static void DraftKept() => Preview(Shot.DraftKept);
        [MenuItem("Mimas/HUD/Preview round 3 (round card)")] public static void RoundCard() => Preview(Shot.RoundCard);
        [MenuItem("Mimas/HUD/Preview round 3 (round result)")] public static void RoundResult() => Preview(Shot.RoundResult);
        [MenuItem("Mimas/HUD/Preview round 3 (series result)")] public static void SeriesResult() => Preview(Shot.SeriesResult);
        [MenuItem("Mimas/HUD/Preview round 3 (examine them)")] public static void ExamineThem() => Preview(Shot.ExamineThem);
        [MenuItem("Mimas/HUD/Preview round 3 (examine you)")] public static void ExamineYou() => Preview(Shot.ExamineYou);

        [MenuItem("Mimas/HUD/End preview")]
        public static void EndPreview()
        {
            Time.timeScale = 1f;
            if (EditorApplication.isPlaying)
                UnityEngine.SceneManagement.SceneManager.LoadScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene().buildIndex);
        }

        private static void Preview(Shot shot)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[HudPreview] Play Mode only: start a practice match in the Arena first.");
                return;
            }
            MatchSession session = UnityEngine.Object.FindAnyObjectByType<MatchSession>();
            if (session == null)
            {
                Debug.LogError("[HudPreview] No MatchSession in the scene.");
                return;
            }

            var catalog = Field<ContentCatalog>(session, "_catalog");
            var board = Field<BoardView>(session, "_board");
            string mapId = board.MapData.Id;

            int apLeft = shot == Shot.LastSeconds ? 0 : 1;
            bool blocked = shot == Shot.Blocked;
            Hex enemyAt;
            string armedId;
            MatchState truth = Round3(catalog, mapId, apLeft, blocked, out enemyAt, out armedId);
            if (shot == Shot.TheirTurn) truth.Apply(new EndTurnCommand(0));

            PlayerView view = truth.ViewFor(0);
            var driver = new FixtureDriver { Rules = truth, View = view };
            driver.Session = SessionAt(view, mapId, SessionPhase.Round, 3, 1, 1, false, -1, null, false);
            if (shot == Shot.LastSeconds) driver.Remaining = 3.5f;

            Install(session, driver, board);
            Time.timeScale = 0f;

            switch (shot)
            {
                case Shot.Armed:
                case Shot.Blocked:
                    Arm(session, board, armedId, enemyAt);
                    break;

                case Shot.Draft:
                case Shot.DraftKept:
                {
                    // Between rounds 2 and 3 there is no round: no view, no rules, the draft over the board.
                    var build = new PlayerBuild(Greek, "greek", new[] { "athena-guard", "hera-resolve", "apollo-bowstring" });
                    driver.Rules = null;
                    driver.View = null;
                    driver.Total = 20f;
                    driver.Remaining = 13f;
                    driver.Session = SessionView.Create(0, 2, SessionPhase.Draft, 1, 1, 2, false, -1, mapId, mapId, build, "norse",
                        EnemyBoons(view), new List<string> { "zeus-favour", "hermes-sandals", "nike-jab" }, false, true, null);
                    Set(session, "_lastRoundHeadline", HudModel.DraftHeadline(2, "Guest-2869", 1, 1));
                    Invoke(session, "OpenDraft");
                    var draft = Field<HudDraft>(session, "_draft");
                    draft.Selected = 2;
                    if (shot == Shot.DraftKept)
                    {
                        draft.Picked = true;
                        draft.Status = "Waiting for Guest-2869…";
                    }
                    break;
                }

                case Shot.RoundCard:
                    Set(session, "_moment", HudModel.RoundStart(3, board.MapData.Name, 0, 0));
                    break;

                case Shot.RoundResult:
                    driver.Session = SessionAt(view, mapId, SessionPhase.Round, 2, 1, 1, false, -1, null, false);
                    Set(session, "_moment", HudModel.RoundResult(2, 1, 0, "Guest-2869", 1, 1, "by elimination · the draft opens in a moment"));
                    break;

                case Shot.SeriesResult:
                {
                    driver.Session = SessionAt(view, mapId, SessionPhase.Round, 3, 2, 1, true, 0, null, false);
                    HudMoment moment = HudModel.SeriesResult(0, 0, 2, 1, "by elimination", "Back to room");
                    moment.ShowBack = true;
                    Set(session, "_moment", moment);
                    break;
                }

                case Shot.ExamineThem:
                case Shot.ExamineYou:
                    Set(session, "_examinedUnitId", shot == Shot.ExamineThem ? 1 : 0);
                    Invoke(session, "RefreshView");
                    break;
            }

            Invoke(session, "RaiseStateChanged");
            Debug.Log("[HudPreview] round-3 fixture on the HUD: " + shot + " (time stopped; Mimas/HUD/End preview restarts).");
        }

        // ---- the fixture -------------------------------------------------------------------------------

        private static readonly Loadout Greek = new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin");
        private static readonly Loadout Norse = new Loadout("flintlock", "ember-circlet", "blink-boots", "leather-jerkin");

        /// <summary>
        /// Your Greek walks towards the centre until <paramref name="apLeft"/> is left (the walk is then seen, the rest
        /// not); the Norse stands where Arrow Shot reaches it — or, for <paramref name="blocked"/>, where a shot at it
        /// is refused (Arrow Shot if the board can block a lob, Aimed Shot otherwise).
        /// </summary>
        private static MatchState Round3(ContentCatalog catalog, string mapId, int apLeft, bool blocked, out Hex enemyAt, out string armedId)
        {
            var mine = new PlayerBuild(Greek, "greek", new[] { "athena-guard", "hera-resolve", "apollo-bowstring", "nike-jab" });
            var theirs = new PlayerBuild(Norse, "norse", new[] { "thor-vigour", "thor-might", "skadi-hide" });
            var state = new MatchState(catalog, new MatchSetup(mapId, mine, theirs), 3);
            state.Start();

            Mimas.Core.Units.Unit me = state.Units.Get(0);
            for (int guard = 0; guard < 8 && me.Ap > apLeft; guard++)
            {
                MovementOptions options = state.MoveOptions(0, "move");
                MovePlan best = null;
                int bestDistance = int.MaxValue;
                for (int i = 0; i < options.Plans.Count; i++)
                {
                    int d = Hex.Distance(options.Plans[i].Destination, Hex.Zero);
                    if (d < bestDistance && d >= 2) { bestDistance = d; best = options.Plans[i]; }
                }
                if (best == null) break;
                var command = new MoveCommand(0, 0, "move", best.Destination);
                if (!state.Validate(command).Ok) break;
                state.Apply(command);
            }

            // A little wear on both, so the tags read like the middle of a round.
            me.TakeDamage(me.Hp / 3);
            state.Units.Get(1).TakeDamage(10);

            armedId = "arrow-shot";
            if (!PlaceEnemy(state, armedId, blocked, out enemyAt) && blocked)
            {
                armedId = "aimed-shot";
                PlaceEnemy(state, armedId, true, out enemyAt);
            }
            return state;
        }

        private static bool PlaceEnemy(MatchState state, string abilityId, bool blocked, out Hex best)
        {
            Hex from = state.Units.Get(0).Position;
            Hex start = state.Units.Get(1).Position;
            best = start;
            int bestScore = int.MinValue;
            foreach (MapHex hex in state.MapData.Hexes)
            {
                Hex h = hex.Position;
                if (h == from || (h != start && state.Bodies.IsOccupied(h))) continue;
                int distance = Hex.Distance(from, h);
                if (distance < 3 || distance > 6) continue;
                state.Units.Get(1).MoveTo(h);
                TargetCheck check = state.CheckTarget(0, abilityId, h);
                bool refused = check.Reason == TargetRejectReason.NoLineOfSight || check.Reason == TargetRejectReason.TrajectoryBlocked;
                if (blocked ? !refused : check.Reason != TargetRejectReason.None) continue;
                // Prefer the enemy to your right and level with you, where the camera shows it well.
                int score = (h.Q - from.Q) * 4 - Math.Abs(h.R - from.R) * 2 - Math.Abs(distance - 4);
                if (score > bestScore) { bestScore = score; best = h; }
            }
            state.Units.Get(1).MoveTo(best);
            return bestScore > int.MinValue;
        }

        private static List<KnownEntry> EnemyBoons(PlayerView view)
        {
            var list = new List<KnownEntry>();
            CoreUnitView enemy = view.FindUnit(1);
            if (enemy != null) for (int i = 0; i < enemy.Boons.Count; i++) list.Add(new KnownEntry(enemy.Boons[i].Id));
            return list;
        }

        private static SessionView SessionAt(PlayerView view, string mapId, SessionPhase phase, int round, int score0, int score1,
            bool over, int winner, List<string> offers, bool opponentPicked)
        {
            var build = new PlayerBuild(Greek, "greek", new[] { "athena-guard", "hera-resolve", "apollo-bowstring", "nike-jab" });
            return SessionView.Create(0, round, phase, score0, score1, 2, over, winner, mapId, null, build, "norse",
                EnemyBoons(view), offers ?? new List<string>(), false, opponentPicked, view);
        }

        // ---- putting it on the HUD ---------------------------------------------------------------------

        private static void Install(MatchSession session, FixtureDriver driver, BoardView board)
        {
            Set(session, "_driver", driver);
            Field<Queue<MatchEvent>>(session, "_pending").Clear();
            Set(session, "_localUnitId", 0);
            Set(session, "_draft", null);
            Set(session, "_moment", null);
            Set(session, "_momentClearsAt", -1f);
            Set(session, "_backAt", -1f);
            Set(session, "_examinedUnitId", -1);
            Set(session, "_examinedPropId", -1);
            Set(session, "_examine", null);
            Invoke(session, "Disarm");

            var heroes = Field<Dictionary<int, HeroView>>(session, "_unitViews");
            var tags = Field<Dictionary<int, HudUnit>>(session, "_hudUnitsById");
            foreach (CoreUnitView unit in driver.View.Units)
            {
                HeroView hero;
                if (heroes.TryGetValue(unit.Id, out hero))
                {
                    hero.gameObject.SetActive(unit.IsAlive);
                    hero.SnapTo(unit.Position, board);
                }
                HudUnit tag;
                if (!tags.TryGetValue(unit.Id, out tag)) continue;
                tag.Hp = unit.Hp;
                tag.MaxHp = unit.MaxHp;
                tag.Ap = unit.Ap;
                tag.ApPerTurn = unit.ApPerTurn;
                tag.IsAlive = unit.IsAlive;
                tag.GhostDamage = 0;
            }

            Invoke(session, "CollectAbilities");
            Invoke(session, "RefreshMarkers");
            Invoke(session, "RefreshView");
            Invoke(session, "FaceNearestEnemies");
        }

        /// <summary>Arms the attack and aims it at the enemy exactly as a hover would: the line, the tint, the panel, the ghost.</summary>
        private static void Arm(MatchSession session, BoardView board, string abilityId, Hex target)
        {
            var abilities = Field<List<AbilityDef>>(session, "_abilities");
            int index = abilities.FindIndex(a => a.Id == abilityId);
            if (index < 0)
            {
                Debug.LogError("[HudPreview] the fixture hero has no " + abilityId);
                return;
            }
            Set(session, "_armed", index);
            Invoke(session, "PaintOptions");

            TileView tile;
            HeroView enemy;
            if (!board.TryGetTileView(target, out tile) || !Field<Dictionary<int, HeroView>>(session, "_unitViews").TryGetValue(1, out enemy)) return;
            typeof(MatchSession).GetMethod("ShowAim", Private).Invoke(session, new object[] { abilities[index], new BoardHover(tile, enemy, null) });
        }

        private static T Field<T>(MatchSession session, string name)
        {
            FieldInfo field = typeof(MatchSession).GetField(name, Private);
            if (field == null) throw new MissingFieldException("MatchSession", name);
            return (T)field.GetValue(session);
        }

        private static void Set(MatchSession session, string name, object value)
        {
            FieldInfo field = typeof(MatchSession).GetField(name, Private);
            if (field == null) throw new MissingFieldException("MatchSession", name);
            field.SetValue(session, value);
        }

        private static void Invoke(MatchSession session, string name)
        {
            MethodInfo method = typeof(MatchSession).GetMethod(name, Private, null, Type.EmptyTypes, null);
            if (method == null) throw new MissingMethodException("MatchSession", name);
            method.Invoke(session, null);
        }

        /// <summary>A match that stands still: the fixture's state and view, the room's names, a clock that says what it is told.</summary>
        private sealed class FixtureDriver : IMatchDriver
        {
            public int LocalPlayer => 0;
            public MatchState Rules { get; set; }
            public PlayerView View { get; set; }
            public SessionView Session { get; set; }
            public bool Ready => true;
            public string OpponentName => "Guest-2869";
            public string MyName => "Rohan";
            public string OpponentStatus => null;
            public float Remaining = 21f;
            public float Total = 30f;
            public float TurnSecondsRemaining => Remaining;
            public float TurnSecondsTotal => Total;
            public bool CanResign => true;
            public bool Submit(Command command) => false;
            public bool SubmitDraftPick(int offerIndex) => false;
            public void Resign() { }
            public void Tick(float deltaTime) { }
            public void Dispose() { }
            public event Action<IReadOnlyList<MatchEvent>> EventsArrived { add { } remove { } }
            public event Action Resynced { add { } remove { } }
            public event Action NextRound { add { } remove { } }
            public event Action StatusChanged { add { } remove { } }
        }
    }
}
