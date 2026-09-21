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

        [Tooltip("Id of the map to build (maps/*.json). Overridden by the MatchSettings asset when one is set.")]
        [SerializeField] private string _mapId = "arena-4";

        [Tooltip("Optional. When set and its MapId is not empty, that map is built instead of the id above.")]
        [SerializeField] private MatchSettings _settings;

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
        private readonly HashSet<Hex> _targets = new HashSet<Hex>();
        private readonly HashSet<Hex> _path = new HashSet<Hex>();

        private bool _hasHovered;
        private Hex _hovered;
        private Hex _blocker;
        private bool _hasBlocker;
        private int _unitsPerLevel = 1;
        private Material _tileMaterialInstance;

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

        /// <summary>Centre-to-centre distance between two adjacent tiles: the world length of one hex of range.</summary>
        public float Spacing => HexLayout.TileWidth(_tileSize);

        /// <summary>
        /// The material every tile renders with: a runtime copy of the assigned one, so the range-circle
        /// uniforms (<see cref="RangeCircles"/>) never dirty the material asset on disk. Per-tile colour keeps
        /// going through each tile's own property block. Null until the board is built.
        /// </summary>
        public Material TileMaterial => _tileMaterialInstance != null ? _tileMaterialInstance : _tileMaterial;

        /// <summary>Height units one map level is worth (<c>rules.heights.unitsPerLevel</c>). 1 until the board is built.</summary>
        public int UnitsPerLevel => _unitsPerLevel;

        /// <summary>
        /// World units per height unit — the one conversion between Core's integer body units and the board.
        /// A tile level is <see cref="UnitsPerLevel"/> of these, so prisms and bodies cannot drift apart.
        /// </summary>
        public float WorldPerHeightUnit => _heightUnit / Mathf.Max(1, _unitsPerLevel);

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
            if (_tileMaterialInstance != null) Destroy(_tileMaterialInstance);
        }

        /// <summary>
        /// Parses the JSON, builds the tile graph and instantiates the tile views. Idempotent: a second
        /// call is ignored. Data problems are reported once as an error and leave the board unbuilt.
        /// </summary>
        public void Build()
        {
            Build(null);
        }

        /// <summary>
        /// The same, for a map chosen at run time. Online the map is not known until <c>match.start</c>
        /// arrives, so the board cannot build itself in <c>Awake</c> from a serialized id; the session builds
        /// it once it knows what it is playing on. A null or empty id keeps the old behaviour.
        /// </summary>
        public void Build(string mapIdOverride)
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

            string mapId = !string.IsNullOrEmpty(mapIdOverride)
                ? mapIdOverride
                : _settings != null && !string.IsNullOrEmpty(_settings.MapId) ? _settings.MapId : _mapId;
            MapData mapData;
            if (!catalog.Maps.TryGet(mapId, out mapData))
            {
                Debug.LogError("[BoardView] Unknown map id '" + mapId + "'. Loaded maps: " + string.Join(", ", MapIds(catalog)), this);
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
                Debug.LogError("[BoardView] Failed to build map '" + mapId + "': " + e.Message, this);
                return;
            }

            Terrains = terrains;
            MapData = mapData;
            Map = map;

            if (catalog.Rules != null && catalog.Rules.Heights != null) _unitsPerLevel = catalog.Rules.Heights.UnitsPerLevel;
            else Debug.LogWarning("[BoardView] rules.json has no heights block; treating one map level as one height unit.", this);

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

        /// <summary>
        /// Where a shot leaves from or lands on this hex: the tile top lifted by <paramref name="aimHeight"/>
        /// height units. Bodies have their own <c>AimPoint</c>; this is the answer for an empty tile.
        /// </summary>
        public Vector3 HexToAimPoint(Hex hex, int aimHeight) => HexToSurface(hex) + Vector3.up * (aimHeight * WorldPerHeightUnit);

        /// <summary>Top of a tile's terrain column in Core's height units — the <c>h0</c>/<c>h1</c> a trajectory is drawn between.</summary>
        public int TileTopUnits(Hex hex)
        {
            Tile tile;
            if (Map != null && Map.TryGet(hex, out tile)) return tile.Height * _unitsPerLevel;
            return 0;
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

        /// <summary>Marks a set of hexes as in-range. Clears targets and any active path preview.</summary>
        public void HighlightReachable(IReadOnlyCollection<Hex> hexes)
        {
            _reachable.Clear();
            _targets.Clear();
            _path.Clear();
            _hasBlocker = false;      // what is armed changed; the aim recomputes on the next hover
            if (hexes != null)
            {
                foreach (Hex h in hexes) _reachable.Add(h);
            }
            Repaint();
        }

        /// <summary>Marks the tiles holding legal targets for an armed attack. Clears the reachable set and any path preview.</summary>
        public void HighlightTargets(IReadOnlyCollection<Hex> hexes)
        {
            _reachable.Clear();
            _targets.Clear();
            _path.Clear();
            _hasBlocker = false;
            if (hexes != null)
            {
                foreach (Hex h in hexes) _targets.Add(h);
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

        /// <summary>
        /// The tile that stops the shot currently being aimed — whatever the rules named in
        /// <c>TargetCheck.BlockedAt</c>. The X sits on the curve, this sits on the hex the rules blame, and
        /// between them a refusal stops being a mystery: the pillar that blocks a grazing line is often
        /// beside the line the board draws, not on it (T-0006, design <c>#line-of-sight</c> rule 5).
        /// </summary>
        public void SetBlocker(Hex hex)
        {
            if (_hasBlocker && _blocker == hex) return;
            _blocker = hex;
            _hasBlocker = true;
            Repaint();
        }

        /// <summary>No shot is being refused any more: the hover moved, or the preview was cleared.</summary>
        public void ClearBlocker()
        {
            if (!_hasBlocker) return;
            _hasBlocker = false;
            Repaint();
        }

        /// <summary>Drops the reachable set, the targets, the path preview, the blocker and the hover mark.</summary>
        public void ClearHighlights()
        {
            _reachable.Clear();
            _targets.Clear();
            _path.Clear();
            _hasBlocker = false;
            _hasHovered = false;
            Repaint();
        }

        private void CreateTileViews()
        {
            int layer = _tileLayer >= 0 ? _tileLayer : gameObject.layer;
            var meshesByHeight = new Dictionary<float, Mesh>();

            // One runtime copy for the whole board: the range circles write uniforms on it every time the
            // player arms an attack, and the material asset on disk must not follow them around.
            if (_tileMaterial != null && _tileMaterialInstance == null)
            {
                _tileMaterialInstance = new Material(_tileMaterial) { name = _tileMaterial.name + " (board)" };
            }
            Material tileMaterial = TileMaterial;

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
                if (tileMaterial != null) renderer.sharedMaterial = tileMaterial;

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
                else if (_hasBlocker && hex == _blocker) state = TileHighlight.Blocker;
                else if (_path.Contains(hex)) state = TileHighlight.PathPreview;
                else if (_targets.Contains(hex)) state = TileHighlight.Targetable;
                else if (_reachable.Contains(hex)) state = TileHighlight.Reachable;
                else state = TileHighlight.None;

                pair.Value.SetHighlight(state);
            }
        }
    }
}
