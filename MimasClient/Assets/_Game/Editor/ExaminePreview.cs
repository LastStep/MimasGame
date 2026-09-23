using System.Collections.Generic;
using System.Reflection;
using Mimas.Client.Presentation;
using Mimas.Core.Content;
using Mimas.Core.Geometry;
using Mimas.Core.Match;
using UnityEditor;
using UnityEngine;

namespace Mimas.Client.Editor
{
    /// <summary>
    /// Puts the pinned render's round-3 state on the live examine plate, for the acceptance screenshots of
    /// spec E §14 that a practice game cannot reach on demand (§13: "a menu item that feeds ExamineView a
    /// fixture"). Play Mode only, with a practice match running.
    /// <para>
    /// The fixture is a real <see cref="MatchState"/> on the shipped content, not a hand-made model: your
    /// hero is the render's Greek (Longbow, Ember Circlet, Leaping Boots, Leather Jerkin; Athena's Guard,
    /// Hera's Resolve, Apollo's Bowstring, Nike's Jab), the enemy a Norse Flintlock with Thor's Vigour (public
    /// from round start), Thor's Might and Skadi's Hide. The enemy then fires one Quick Shot, which reveals
    /// the shot and Thor's Might; Skadi's Hide stays hidden. The plate is built by
    /// <see cref="ExamineModelBuilder"/> from the view you would hold, exactly as the presenter builds it.
    /// </para>
    /// <para>
    /// Time is stopped (<c>Time.timeScale = 0</c>) so the practice match underneath does not replace the
    /// preview with its own next event; <c>Mimas/Examine/End preview</c> starts it again.
    /// </para>
    /// </summary>
    public static class ExaminePreview
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [MenuItem("Mimas/Examine/Preview round 3 (your hero)")]
        public static void PreviewOwn() => Preview(0);

        [MenuItem("Mimas/Examine/Preview round 3 (the enemy)")]
        public static void PreviewEnemy() => Preview(1);

        [MenuItem("Mimas/Examine/End preview")]
        public static void EndPreview()
        {
            Time.timeScale = 1f;
            MatchSession session = Object.FindAnyObjectByType<MatchSession>();
            if (session != null) session.CloseExamine();
        }

        private static void Preview(int unitId)
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogError("[ExaminePreview] Play Mode only: start a practice match in the Arena first.");
                return;
            }
            MatchSession session = Object.FindAnyObjectByType<MatchSession>();
            if (session == null)
            {
                Debug.LogError("[ExaminePreview] No MatchSession in the scene.");
                return;
            }

            var catalog = (ContentCatalog)typeof(MatchSession).GetField("_catalog", Private).GetValue(session);
            MatchState truth = Round3(catalog);
            PlayerView view = truth.ViewFor(0);
            HudExamine model = ExamineModelBuilder.BuildUnit(catalog, view, truth, unitId,
                unitId == 0 ? "Rohan" : "Random Bot", unitId == 0 ? null : new List<string> { "thor-vigour", "thor-might" });

            Time.timeScale = 0f;
            typeof(MatchSession).GetField("_examinedUnitId", Private).SetValue(session, -1);
            typeof(MatchSession).GetField("_examinedPropId", Private).SetValue(session, -1);
            typeof(MatchSession).GetField("_examine", Private).SetValue(session, model);
            typeof(MatchSession).GetMethod("RaiseStateChanged", Private).Invoke(session, null);
            Debug.Log("[ExaminePreview] round-3 fixture on the plate for unit " + unitId + " (time stopped).");
        }

        private static MatchState Round3(ContentCatalog catalog)
        {
            var mine = new PlayerBuild(new Loadout("longbow", "ember-circlet", "leaping-boots", "leather-jerkin"), "greek",
                new[] { "athena-guard", "hera-resolve", "apollo-bowstring", "nike-jab" });
            var theirs = new PlayerBuild(new Loadout("flintlock", "ember-circlet", "blink-boots", "leather-jerkin"), "norse",
                new[] { "thor-vigour", "thor-might", "skadi-hide" });
            var state = new MatchState(catalog, new MatchSetup("arena-4", mine, theirs), 3);
            state.Start();

            // The enemy steps within Quick Shot's reach of your hero and fires once: the shot is seen, and
            // Thor's Might shows itself by changing the number (design #hidden-info).
            state.Apply(new EndTurnCommand(0));
            Hex at = state.Units.Get(0).Position;
            var targets = new List<Mimas.Core.Units.IBody>();
            foreach (Hex h in Hex.Spiral(at, 5))
            {
                if (Hex.Distance(at, h) < 2 || !state.Map.Contains(h) || state.Bodies.IsOccupied(h)) continue;
                state.Units.Get(1).MoveTo(h);
                state.AttackTargets(1, "quick-shot", targets);
                bool reaches = false;
                for (int i = 0; i < targets.Count; i++) if (targets[i].Id == 0) reaches = true;
                if (reaches) break;
            }
            state.Apply(new AttackCommand(1, 1, "quick-shot", at));
            state.Apply(new EndTurnCommand(1));
            return state;
        }
    }
}
