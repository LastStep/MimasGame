using System;
using System.Collections.Generic;
using UnityEngine;
using Mimas.Client.Content;
using Mimas.Core.Content;
using Mimas.Core.Data;
using Mimas.Core.Geometry;
using Mimas.Core.Grid;

namespace Mimas.Client.Presentation
{
    /// <summary>
    /// The presentation owner of one board. It loads the terrain catalogue and the map from JSON via
    /// Core, builds the playable <see cref="TileMap"/>, then generates one child GameObject per hex
    /// (shared prism mesh per distinct terrain height, <see cref="MeshCollider"/> for picking).
    /// Everything above this class talks in <see cref="Hex"/>; everything below talks in world space —
    /// this is the only place the two meet.
    /// The board never mutates rules state: it reads the <see cref="TileMap"/> and paints.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardView : MonoBehaviour
    {
        [Header("Content")]
        [Tooltip("Where the catalogue comes from. The board never touches JSON itself.")]
        [SerializeField] private ContentBootstrap _content;

        [Tooltip("Id of the map to build (maps/*.json). Match setup will drive this later.")]
        [SerializeField] private string _mapId = "arena-4";

        [Header("Layout")]
        [Tooltip("Hex circumradius in world units (centre to corner).")]
        [SerializeField] private float _tileSize = HexLayout.DefaultTileSize;

        [Tooltip("Material applied to every tile. Use a URP Lit material; colour comes from a MaterialPropertyBlock.")]
        [SerializeField] private Material _tileMaterial;

        [Tooltip("Layer index for generated tiles. -1 inherits this GameObject's layer.")]
        [SerializeField] private int _tileLayer = -1;

        [Tooltip("Build the board during Awake. Turn off to drive Build() yourself.")]
        [SerializeField] private bool _buildOnAwake = true;

        [Header("Terrain visuals")]
        [SerializeField] private List<TerrainVisual> _terrainVisuals = new List<TerrainVisual>
        {
            new TerrainVisual("grass", new Color(0.3294f, 0.5765f, 0.3059f, 1f), 0.15f),
            new TerrainVisual("stone", new Color(0.6039f, 0.6275f, 0.6588f, 1f), 0.90f),
        };

        [Tooltip("Used when a terrain id has no entry above — deliberately garish so it is noticed.")]
        [SerializeField] private Color _fallbackColor = new Color(0.9f, 0.1f, 0.9f, 1f);
        [SerializeField] private float _fallbackPrismHeight = 0.15f;

        [Header("Tile height")]
        [Tooltip("World units added to a tile's prism per unit of its rules `height` (maps/*.json). Rules read the integer; this only draws it.")]
        [SerializeField] private float _heightUnit = 0.45f;

        [Tooltip("Prisms never get shorter than this, even for negative heights.")]
        [SerializeField] private float _minPrismHeight = 0.05f;

        [Tooltip("How much each unit of height brightens the tile colour, so plateaus read at a glance. 0 disables.")]
        [Range(0f, 0.5f)] [SerializeField] private float _heightTintPerUnit = 0.12f;

        private readonly Dictionary<Hex, TileView> _tileViews = new Dictionary<Hex, TileView>();
        private readonly List<Mesh> _generatedMeshes = new List<Mesh>();
        private readonly HashSet<Hex> _reachable = new HashSet<Hex>();
        private readonly HashSet<Hex> _path = new HashSet<Hex>();

        private bool _hasHovered;
        private Hex _hovered;

        /// <summary>Raised once, after every tile view exists. Late subscribers should check <see cref="IsBuilt"/>.</summary>
        public event Action BoardBuilt;

        /// <summary>The playable tile graph. Null until the board is built.</summary>
        public TileMap Map { get; private set; }

        /// <summary>The parsed map definition (spawns, ladder position, symmetry). Null until the board is built.</summary>
        public MapData MapData { get; private set; }

        /// <summary>The terrain catalogue this board was built against. Null until the board is built.</summary>
        public TerrainSet Terrains { get; private set; }

        /// <summary>Hex circumradius in world units.</summary>
        public float TileSize => _tileSize;

        /// <summary>True once <see cref="Build"/> has succeeded.</summary>
        public bool IsBuilt { get; private set; }

        /// <summary>Number of generated tile views.</summary>
        public int TileCount => _tileViews.Count;

        private void Awake()
        {
            if (_buildOnAwake) Build();
        }

        private void OnDestroy()
        {
            BoardBuilt = null;
            for (int i = 0; i < _generatedMeshes.Count; i++)
            {
                if (_generatedMeshes[i] != null) Destroy(_generatedMeshes[i]);
            }
            _generatedMeshes.Clear();
        }

        /// <summary>
        /// Parses the JSON, builds the tile graph and instantiates the tile views. Idempotent: a second
        /// call is ignored. Data problems are reported once as an error and leave the board unbuilt.
        /// </summary>
        public void Build()
        {
            if (IsBuilt) return;

            if (_content == null)
            {
                Debug.LogError("[BoardView] _content (ContentBootstrap) must be assigned.", this);
                return;
            }
            if (_tileMaterial == null)
            {
                Debug.LogWarning("[BoardView] No tile material assigned; tiles will render with the error shader.", this);
            }

            ContentCatalog catalog = _content.EnsureLoaded();
            if (catalog == null)
            {
                Debug.LogError("[BoardView] Content failed to load; board not built.", this);
                return;
            }

            MapData mapData;
            if (!catalog.Maps.TryGet(_mapId, out mapData))
            {
                Debug.LogError("[BoardView] Unknown map id '" + _mapId + "'. Loaded maps: " + string.Join(", ", MapIds(catalog)), this);
                return;
            }

            TerrainSet terrains = catalog.Terrains;
            TileMap map;
            try
            {
                // The catalogue already validated this map; building again just gives us our own mutable copy.
                map = mapData.BuildTileMap(terrains);
            }
            catch (MapLoadException e)
            {
                Debug.LogError("[BoardView] Failed to build map '" + _mapId + "': " + e.Message, this);
                return;
            }

            Terrains = terrains;
            MapData = mapData;
            Map = map;

            CreateTileViews();

            IsBuilt = true;
            BoardBuilt?.Invoke();
        }

        private static List<string> MapIds(ContentCatalog catalog)
        {
            var ids = new List<string>(catalog.Maps.Count);
            for (int i = 0; i < catalog.Maps.Count; i++) ids.Add(catalog.Maps.ByIndex(i).Id);
            return ids;
        }

        /// <summary>Looks up the view for a coordinate. False when the hex is not on this board.</summary>
        public bool TryGetTileView(Hex hex, out TileView view) => _tileViews.TryGetValue(hex, out view);

        /// <summary>World position of a tile centre at the board's base plane (y of this transform).</summary>
        public Vector3 HexToWorld(Hex hex) => transform.TransformPoint(HexLayout.HexToWorld(hex, _tileSize));

        /// <summary>World position of the top face of a tile's prism — where units stand.</summary>
        public Vector3 HexToSurface(Hex hex)
        {
            Vector3 local = HexLayout.HexToWorld(hex, _tileSize, SurfaceHeight(hex));
            return transform.TransformPoint(local);
        }

        /// <summary>Local-space prism height of a tile: its terrain's base prism plus its rules height, or the fallback.</summary>
        public float SurfaceHeight(Hex hex)
        {
            Tile tile;
            if (Map != null && Map.TryGet(hex, out tile)) return PrismHeightOf(tile);
            return _fallbackPrismHeight;
        }

        private float PrismHeightOf(Tile tile)
        {
            float height = ResolveVisual(tile.Terrain).PrismHeight + tile.Height * _heightUnit;
            return height < _minPrismHeight ? _minPrismHeight : height;
        }

        private Color TileColorOf(Tile tile, Color terrainColor)
        {
            if (_heightTintPerUnit <= 0f || tile.Height == 0) return terrainColor;
            float t = Mathf.Clamp01(Mathf.Abs(tile.Height) * _heightTintPerUnit);
            Color target = tile.Height > 0 ? Color.white : Color.black;
            return Color.Lerp(terrainColor, target, t);
        }

        /// <summary>
        /// Nearest hex to a world point. Returns false when that hex is not part of this board,
        /// but still writes the rounded coordinate so callers can log it.
        /// </summary>
        public bool TryWorldToHex(Vector3 world, out Hex hex)
        {
            hex = HexLayout.WorldToHex(transform.InverseTransformPoint(world), _tileSize);
            return Map != null && Map.Contains(hex);
        }

        /// <summary>Marks a set of hexes as in-range. Clears any active path preview.</summary>
        public void HighlightReachable(IReadOnlyCollection<Hex> hexes)
        {
            _reachable.Clear();
            _path.Clear();
            if (hexes != null)
            {
                foreach (Hex h in hexes) _reachable.Add(h);
            }
            Repaint();
        }

        /// <summary>Paints a path on top of the reachable set. Null or empty clears the preview.</summary>
        public void ShowPathPreview(IReadOnlyList<Hex> path)
        {
            _path.Clear();
            if (path != null)
            {
                for (int i = 0; i < path.Count; i++) _path.Add(path[i]);
            }
            Repaint();
        }

        /// <summary>Marks the tile under the cursor. Pass null to clear.</summary>
        public void SetHovered(TileView tile)
        {
            if (tile == null)
            {
                if (!_hasHovered) return;
                _hasHovered = false;
                Repaint();
                return;
            }

            if (_hasHovered && _hovered == tile.Coord) return;
            _hovered = tile.Coord;
            _hasHovered = true;
            Repaint();
        }

        /// <summary>Drops the reachable set, the path preview and the hover mark.</summary>
        public void ClearHighlights()
        {
            _reachable.Clear();
            _path.Clear();
            _hasHovered = false;
            Repaint();
        }

        private void CreateTileViews()
        {
            int layer = _tileLayer >= 0 ? _tileLayer : gameObject.layer;
            var meshesByHeight = new Dictionary<float, Mesh>();

            // Authored file order — deterministic hierarchy, unlike iterating the tile dictionary.
            foreach (MapHex authored in MapData.Hexes)
            {
                TerrainVisual visual = ResolveVisual(authored.Terrain);
                Tile tile = Map[authored.Position];
                float prismHeight = PrismHeightOf(tile);

                Mesh mesh;
                if (!meshesByHeight.TryGetValue(prismHeight, out mesh))
                {
                    mesh = HexMeshFactory.CreatePrism(_tileSize, prismHeight, "HexPrism_" + prismHeight.ToString("0.###"));
                    meshesByHeight[prismHeight] = mesh;
                    _generatedMeshes.Add(mesh);
                }

                var go = new GameObject("Tile_" + authored.Position.Q + "_" + authored.Position.R);
                go.layer = layer;
                go.transform.SetParent(transform, false);
                go.transform.localPosition = HexLayout.HexToWorld(authored.Position, _tileSize);
                go.transform.localRotation = Quaternion.identity;

                MeshFilter filter = go.AddComponent<MeshFilter>();
                filter.sharedMesh = mesh;

                MeshRenderer renderer = go.AddComponent<MeshRenderer>();
                if (_tileMaterial != null) renderer.sharedMaterial = _tileMaterial;

                MeshCollider collider = go.AddComponent<MeshCollider>();
                collider.sharedMesh = mesh;

                TileView view = go.AddComponent<TileView>();
                view.Init(authored.Position, TileColorOf(tile, visual.Color));

                _tileViews[authored.Position] = view;
            }
        }

        private TerrainVisual ResolveVisual(string terrainId)
        {
            for (int i = 0; i < _terrainVisuals.Count; i++)
            {
                if (string.Equals(_terrainVisuals[i].TerrainId, terrainId, StringComparison.Ordinal))
                {
                    TerrainVisual configured = _terrainVisuals[i];
                    if (configured.PrismHeight <= 0f) configured.PrismHeight = _fallbackPrismHeight;
                    return configured;
                }
            }
            return new TerrainVisual(terrainId, _fallbackColor, _fallbackPrismHeight);
        }

        private void Repaint()
        {
            foreach (KeyValuePair<Hex, TileView> pair in _tileViews)
            {
                Hex hex = pair.Key;
                TileHighlight state;
                if (_hasHovered && hex == _hovered) state = TileHighlight.Hovered;
                else if (_path.Contains(hex)) state = TileHighlight.PathPreview;
                else if (_reachable.Contains(hex)) state = TileHighlight.Reachable;
                else state = TileHighlight.None;

                pair.Value.SetHighlight(state);
            }
        }
    }
}
