using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;

namespace Mimas.Client.Editor
{
    /// <summary>
    /// Makes the UI Toolkit font assets the interface language names (docs/ui/language.md §2): one dynamic
    /// TextCore font asset per <c>.ttf</c> under <c>Assets/_Game/UI/Fonts/</c>, saved beside it as
    /// <c>&lt;name&gt; SDF.asset</c> with its atlas and material as sub-assets. The files are Latin subsets
    /// (spec E §7.9), so a dynamic atlas never has anything but Latin to rasterise. An asset that already
    /// exists is left alone, so its GUID — which <c>Theme.uss</c> points at — never changes.
    /// </summary>
    public static class Fonts
    {
        private const string Folder = "Assets/_Game/UI/Fonts";
        private const int SamplingPointSize = 90;
        private const int Padding = 9;
        private const int AtlasSize = 1024;

        [MenuItem("Mimas/Fonts/Generate")]
        public static void Generate()
        {
            string[] guids = AssetDatabase.FindAssets("t:Font", new[] { Folder });
            int made = 0, kept = 0;
            for (int i = 0; i < guids.Length; i++)
            {
                string fontPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (!fontPath.EndsWith(".ttf", System.StringComparison.OrdinalIgnoreCase)) continue;

                string name = Path.GetFileNameWithoutExtension(fontPath) + " SDF";
                string assetPath = Folder + "/" + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) != null) { kept++; continue; }

                var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
                if (font == null)
                {
                    Debug.LogError("[Fonts] Could not load " + fontPath);
                    continue;
                }

                FontAsset asset = FontAsset.CreateFontAsset(font, SamplingPointSize, Padding, GlyphRenderMode.SDFAA,
                    AtlasSize, AtlasSize, AtlasPopulationMode.Dynamic, true);
                if (asset == null)
                {
                    Debug.LogError("[Fonts] TextCore refused " + fontPath);
                    continue;
                }

                asset.name = name;
                AssetDatabase.CreateAsset(asset, assetPath);

                // The atlas and material live inside the font asset, as the Editor's own creation menu does it.
                Texture2D atlas = asset.atlasTextures != null && asset.atlasTextures.Length > 0 ? asset.atlasTextures[0] : null;
                if (atlas != null)
                {
                    atlas.name = name + " Atlas";
                    AssetDatabase.AddObjectToAsset(atlas, asset);
                }
                if (asset.material != null)
                {
                    asset.material.name = name + " Material";
                    AssetDatabase.AddObjectToAsset(asset.material, asset);
                }

                EditorUtility.SetDirty(asset);
                made++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Fonts] Generated " + made + " font asset(s), kept " + kept + ".");
        }
    }
}
