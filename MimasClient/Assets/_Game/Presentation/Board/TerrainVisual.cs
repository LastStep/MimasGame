using System;
using UnityEngine;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// Presentation-only look-up for one terrain id from <c>terrains.json</c>: what colour it paints and
    /// how tall its prism stands. Balance data stays in Core's <c>TerrainDef</c>; this struct carries
    /// nothing a rule could read. Public fields because Unity cannot serialize properties.
    /// </summary>
    [Serializable]
    public struct TerrainVisual
    {
        [Tooltip("Terrain id as authored in terrains.json (e.g. \"grass\", \"stone\").")]
        public string TerrainId;

        [Tooltip("Unhighlighted colour of the tile prism.")]
        public Color Color;

        [Tooltip("Height of the prism in world units; the unit stands on the top face.")]
        [Min(0.001f)] public float PrismHeight;

        public TerrainVisual(string terrainId, Color color, float prismHeight)
        {
            TerrainId = terrainId;
            Color = color;
            PrismHeight = prismHeight;
        }
    }
}
