using System.Collections.Generic;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Launcher
{
    /// <summary>
    /// Карта для экранов пультов: вид сверху на оба острова, 1 px = 1 м. Для каждой команды своя текстура:
    /// свой остров слева и зелёный, вражеский справа и красный (поворот вида, не зеркало). Две версии: с точками своих
    /// шахт (мини-карта в HUD) и без них (экран пульта).
    /// Экранные координаты uv: u — вправо (по оси Z к врагу), v — вверх.
    /// </summary>
    public class TacticalMap : MonoBehaviour
    {
        const float Margin = 6f;
        const float RebuildInterval = 0.2f;

        static readonly Color32 Background = new Color32(118, 118, 122, 255);
        static readonly Color32 OwnHigh = new Color32(70, 220, 70, 255);
        static readonly Color32 OwnLow = new Color32(30, 140, 40, 255);
        static readonly Color32 EnemyHigh = new Color32(240, 70, 60, 255);
        static readonly Color32 EnemyLow = new Color32(160, 35, 30, 255);
        static readonly Color32 StoneGray = new Color32(150, 150, 146, 255);
        static readonly Color32 SiloAlive =new Color32(30, 30, 30, 255);
        static readonly Color32 SiloDead = new Color32(60, 60, 60, 255);

        public static TacticalMap Instance { get; private set; }

        public float MinX { get; private set; }
        public float MaxX { get; private set; }
        public float MinZ { get; private set; }
        public float MaxZ { get; private set; }
        public float SpanX => MaxX - MinX;
        public float SpanZ => MaxZ - MinZ;

        readonly Texture2D[] _textures = new Texture2D[2];
        /// <summary>Та же карта без точек своих шахт — для экрана пульта.</summary>
        readonly Texture2D[] _terrainTextures = new Texture2D[2];
        readonly List<VoxelIsland> _islands = new List<VoxelIsland>();
        Color32[] _pixels;
        bool _dirty;
        float _nextRebuild;

        public static TacticalMap Ensure()
        {
            if (Instance == null)
                Instance = new GameObject("TacticalMap").AddComponent<TacticalMap>();
            return Instance;
        }

        public void Setup(IEnumerable<VoxelIsland> islands)
        {
            foreach (var island in _islands)
                if (island != null)
                    island.BlocksChanged -= MarkDirty;
            _islands.Clear();
            _islands.AddRange(islands);

            MinX = MinZ = float.MaxValue;
            MaxX = MaxZ = float.MinValue;
            foreach (var island in _islands)
            {
                island.BlocksChanged += MarkDirty;
                var b = island.WorldBounds;
                MinX = Mathf.Min(MinX, b.min.x - Margin);
                MaxX = Mathf.Max(MaxX, b.max.x + Margin);
                MinZ = Mathf.Min(MinZ, b.min.z - Margin);
                MaxZ = Mathf.Max(MaxZ, b.max.z + Margin);
            }

            int w = Mathf.CeilToInt(SpanZ), h = Mathf.CeilToInt(SpanX);
            for (int team = 0; team < 2; team++)
            {
                EnsureTexture(ref _textures[team], w, h, $"TacticalMap_T{team}");
                EnsureTexture(ref _terrainTextures[team], w, h, $"TacticalMapTerrain_T{team}");
            }
            _pixels = new Color32[w * h];
            Rebuild();
        }

        static void EnsureTexture(ref Texture2D tex, int w, int h, string name)
        {
            if (tex != null && tex.width == w && tex.height == h)
                return;
            if (tex != null)
                Destroy(tex);
            tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = name
            };
        }

        /// <summary>Карта команды: с точками своих шахт (мини-карта HUD) или без них (экран пульта).</summary>
        public Texture2D GetTexture(int team, bool withSilos = true) =>
            team < 0 || team >= 2 ? null : withSilos ? _textures[team] : _terrainTextures[team];

        public void MarkDirty() => _dirty = true;

        void LateUpdate()
        {
            if (_dirty && Time.time >= _nextRebuild)
                Rebuild();
        }

        /// <summary>uv экрана команды → мировая точка (y = 0).</summary>
        public Vector3 ScreenToWorld(int team, Vector2 uv)
        {
            // команда 0: вправо = +Z, вверх = −X; команда 1 — поворот на 180°
            return team == 0
                ? new Vector3(MaxX - uv.y * SpanX, 0f, MinZ + uv.x * SpanZ)
                : new Vector3(MinX + uv.y * SpanX, 0f, MaxZ - uv.x * SpanZ);
        }

        public Vector2 WorldToScreen(int team, Vector3 world)
        {
            return team == 0
                ? new Vector2((world.z - MinZ) / SpanZ, (MaxX - world.x) / SpanX)
                : new Vector2((MaxZ - world.z) / SpanZ, (world.x - MinX) / SpanX);
        }

        /// <summary>Остров и высота столбца под мировой точкой (null — пустота).</summary>
        public VoxelIsland IslandAt(Vector3 world, out int topHeight)
        {
            foreach (var island in _islands)
            {
                Vector3 local = world - island.transform.position;
                int x = Mathf.FloorToInt(local.x), z = Mathf.FloorToInt(local.z);
                if (x < 0 || z < 0 || x >= VoxelIsland.Width || z >= VoxelIsland.Depth)
                    continue;
                topHeight = island.GetTopHeight(x, z);
                return topHeight > 0 ? island : null;
            }

            topHeight = 0;
            return null;
        }

        void Rebuild()
        {
            _dirty = false;
            _nextRebuild = Time.time + RebuildInterval;

            for (int team = 0; team < 2; team++)
            {
                var tex = _textures[team];
                if (tex == null)
                    continue;
                int w = tex.width, h = tex.height;

                for (int py = 0; py < h; py++)
                for (int px = 0; px < w; px++)
                {
                    var world = ScreenToWorld(team, new Vector2((px + 0.5f) / w, (py + 0.5f) / h));
                    var island = IslandAt(world, out int top);
                    Color32 c = Background;
                    if (island != null)
                    {
                        int surface = top - VoxelIsland.StoneLayers; // 0 — земля выбита до камня
                        float k = (surface - 1f) / (VoxelIsland.SurfaceMaxHeight - 1f);
                        c = island.Team == team ? Color32.Lerp(OwnLow, OwnHigh, k) : Color32.Lerp(EnemyLow, EnemyHigh, k);
                        if (surface <= 0)
                            c = Color32.Lerp(island.Team == team ? OwnLow : EnemyLow, StoneGray, 0.6f);
                    }
                    _pixels[py * w + px] = c;
                }

                var terrain = _terrainTextures[team];
                if (terrain != null)
                {
                    terrain.SetPixels32(_pixels);
                    terrain.Apply(false);
                }

                // только свои шахты: расположение вражеских игрок должен вычислять сам
                foreach (var silo in RocketSilo.All)
                {
                    if (silo.Team != team)
                        continue;
                    var uv = WorldToScreen(team, silo.transform.position);
                    int cx = Mathf.FloorToInt(uv.x * w), cy = Mathf.FloorToInt(uv.y * h);
                    var c = silo.State == SiloState.Destroyed ? SiloDead : SiloAlive;
                    for (int dx = -1; dx <= 1; dx++)
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        int x = cx + dx, y = cy + dy;
                        if (x >= 0 && y >= 0 && x < w && y < h && (silo.State != SiloState.Destroyed || dx == dy || dx == -dy))
                            _pixels[y * w + x] = c;
                    }
                }

                tex.SetPixels32(_pixels);
                tex.Apply(false);
            }
        }

        void OnDestroy()
        {
            foreach (var tex in _textures)
                if (tex != null)
                    Destroy(tex);
            foreach (var tex in _terrainTextures)
                if (tex != null)
                    Destroy(tex);
            if (Instance == this)
                Instance = null;
        }
    }
}
