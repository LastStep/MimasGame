using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Mimas.Client.Content;

namespace Mimas.Client.Editor
{
    /// <summary>
    /// Keeps <c>Assets/_Game/Content/GameDataManifest.asset</c> equal to the set of JSON files under
    /// <c>Assets/_Game/Data</c>. Runs after any import, move or delete inside the data folder, before
    /// every build, and on demand from the menu. Nobody maintains the list by hand.
    /// </summary>
    public sealed class GameDataManifestBuilder : AssetPostprocessor, IPreprocessBuildWithReport
    {
        public const string DataRoot = "Assets/_Game/Data";
        public const string ContentFolder = "Assets/_Game/Content";
        public const string ManifestPath = ContentFolder + "/GameDataManifest.asset";

        public int callbackOrder => 0;

        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            if (Touches(imported) || Touches(deleted) || Touches(moved) || Touches(movedFrom)) Rebuild(false);
        }

        [MenuItem("Mimas/Rebuild Game Data Manifest")]
        public static void RebuildFromMenu() => Rebuild(true);

        public void OnPreprocessBuild(BuildReport report)
        {
            if (Rebuild(false)) Debug.LogWarning("[GameDataManifest] Manifest was stale and has been rebuilt before the build.");
        }

        private static bool Touches(string[] paths)
        {
            if (paths == null) return false;
            for (int i = 0; i < paths.Length; i++)
            {
                string p = paths[i].Replace('\\', '/');
                if (p.StartsWith(DataRoot + "/", StringComparison.Ordinal) && p.EndsWith(".json", StringComparison.Ordinal)) return true;
            }
            return false;
        }

        /// <summary>Rebuilds if needed. Returns true when the asset changed.</summary>
        public static bool Rebuild(bool verbose)
        {
            var entries = new List<KeyValuePair<string, TextAsset>>();
            foreach (string guid in AssetDatabase.FindAssets("t:TextAsset", new[] { DataRoot }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
                if (!assetPath.EndsWith(".json", StringComparison.Ordinal)) continue;
                if (!assetPath.StartsWith(DataRoot + "/", StringComparison.Ordinal)) continue;
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
                if (asset == null) continue;
                entries.Add(new KeyValuePair<string, TextAsset>(assetPath.Substring(DataRoot.Length + 1), asset));
            }
            entries.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var paths = new string[entries.Count];
            var files = new TextAsset[entries.Count];
            for (int i = 0; i < entries.Count; i++)
            {
                paths[i] = entries[i].Key;
                files[i] = entries[i].Value;
            }

            GameDataManifest manifest = AssetDatabase.LoadAssetAtPath<GameDataManifest>(ManifestPath);
            bool created = false;
            if (manifest == null)
            {
                if (!AssetDatabase.IsValidFolder(ContentFolder)) AssetDatabase.CreateFolder("Assets/_Game", "Content");
                manifest = ScriptableObject.CreateInstance<GameDataManifest>();
                AssetDatabase.CreateAsset(manifest, ManifestPath);
                created = true;
            }

            if (!created && manifest.Matches(paths, files))
            {
                if (verbose) Debug.Log("[GameDataManifest] Up to date: " + entries.Count + " files.");
                return false;
            }

            manifest.SetEntries(paths, files);
            EditorUtility.SetDirty(manifest);
            AssetDatabase.SaveAssetIfDirty(manifest);
            Debug.Log("[GameDataManifest] " + (created ? "Created" : "Rebuilt") + " with " + entries.Count + " files.");
            return true;
        }
    }
}
