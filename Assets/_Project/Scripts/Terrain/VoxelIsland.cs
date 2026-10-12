using System;
using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>Вид кубика (для обломков): верх (трава/песок/снег), земля, каменный нижний слой; Wood — щепки ящика.</summary>
    public enum BlockKind : byte
    {
        Grass,
        Dirt,
        Stone,
        Wood
    }

    /// <summary>
    /// Парящий воксельный остров команды. Хранит HP каждого кубика (земля 50, каменный нижний слой 200),
    /// режет себя на чанки 16×16 и перестраивает только изменённые чанки.
    /// Позиция transform — угол блока (0,0,0); поворот не поддерживается.
    /// </summary>
    public class VoxelIsland : MonoBehaviour
    {
        public const int Width = 105;
        public const int Depth = 68;
        /// <summary>Нижний слой каждого столбца — камень (y = 0), над ним 1–4 кубика земли с травой.</summary>
        public const int StoneLayers = 1;
        public const int SurfaceMaxHeight = 4;
        public const int MaxHeight = StoneLayers + SurfaceMaxHeight;
        public const int ChunkSize = 16;
        public const byte BlockMaxHp = 50;
        public const byte StoneMaxHp = 200;

        public static bool IsStoneLayer(int y) => y < StoneLayers;

        public static readonly List<VoxelIsland> All = new List<VoxelIsland>();

        public int Team { get; private set; }
        public int Seed { get; private set; }
        public Biome Biome { get; private set; }

        /// <summary>Вызывается после разрушения кубиков (мировые позиции центров уничтоженных блоков не передаём — хватит факта).</summary>
        public event Action BlocksChanged;

        byte[] _hp;
        /// <summary>Исходная высота столбца после генерации: трава растёт только на ней (не в свежих воронках).</summary>
        byte[] _surface;
        /// <summary>Столбцы, где трава не нужна: под шахтами и ящиками (иначе она прорастает сквозь них).</summary>
        bool[] _noGrass;
        /// <summary>Площадка флага: верхние кубики — камень, а сами столбцы неразрушимы (флаг стоит всегда).</summary>
        readonly HashSet<int> _flagStone = new HashSet<int>();
        readonly HashSet<int> _unbreakable = new HashSet<int>();

        /// <summary>Флаг команды: на дальней от противника стороне, по центру ширины. Угол площадки 2×2 (локально).</summary>
        public Vector2Int FlagCell { get; private set; }
        /// <summary>Мировая точка, где стоит флаг: центр верха каменной площадки 2×2.</summary>
        public Vector3 FlagBase { get; private set; }
        /// <summary>Отступ площадки флага от дальнего края острова (в кубиках).</summary>
        const int FlagEdgeMargin = 8;
        VoxelChunk[,] _chunks;
        const int MaxRebuildsPerFrame = 6;
        readonly HashSet<VoxelChunk> _dirty = new HashSet<VoxelChunk>();
        readonly List<VoxelChunk> _rebuildBuffer = new List<VoxelChunk>();

        public static VoxelIsland ForTeam(int team)
        {
            foreach (var island in All)
                if (island.Team == team)
                    return island;
            return null;
        }

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public void Build(int team, int seed, Biome biome)
        {
            Team = team;
            Seed = seed;
            Biome = biome;

            var heights = IslandGenerator.Generate(seed, Width, Depth, SurfaceMaxHeight);
            _hp = new byte[Width * Depth * MaxHeight];
            _surface = new byte[Width * Depth];
            _noGrass = new bool[Width * Depth];
            for (int x = 0; x < Width; x++)
            for (int z = 0; z < Depth; z++)
            {
                if (heights[x, z] == 0)
                    continue;
                _surface[x + Width * z] = (byte)(StoneLayers + heights[x, z]);
                for (int y = 0; y < StoneLayers + heights[x, z]; y++)
                    _hp[Index(x, y, z)] = IsStoneLayer(y) ? StoneMaxHp : BlockMaxHp;
            }
            PlaceFlagPad();

            _dirty.Clear();
            foreach (Transform child in transform)
                Destroy(child.gameObject);

            int cxCount = Mathf.CeilToInt(Width / (float)ChunkSize);
            int czCount = Mathf.CeilToInt(Depth / (float)ChunkSize);
            _chunks = new VoxelChunk[cxCount, czCount];
            var material = VoxelAtlas.GetMaterial(biome);
            for (int cx = 0; cx < cxCount; cx++)
            for (int cz = 0; cz < czCount; cz++)
            {
                var go = new GameObject($"Chunk_{cx}_{cz}");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(cx * ChunkSize, 0, cz * ChunkSize);
                var chunk = go.AddComponent<VoxelChunk>();
                chunk.Init(this, cx, cz, material);
                _chunks[cx, cz] = chunk;
                chunk.Rebuild();
            }
        }

        static int Index(int x, int y, int z) => x + Width * (z + Depth * y);

        /// <summary>Кубик каменный: нижний слой острова или верх площадки флага.</summary>
        public bool IsStoneBlock(int x, int y, int z) =>
            IsStoneLayer(y) || (_flagStone.Count > 0 && _flagStone.Contains(Index(x, y, z)));

        /// <summary>
        /// Площадка флага 2×2: по центру ширины, у дальнего от противника края (команда 0 — у z = 0, команда 1 —
        /// у z = Depth). Если там пусто или край — сдвигаемся вглубь острова. Четыре столбца выравниваются,
        /// верхние кубики становятся каменными, а столбцы — неразрушимыми. Детерминированно — у всех одинаково.
        /// </summary>
        void PlaceFlagPad()
        {
            _flagStone.Clear();
            _unbreakable.Clear();
            int x0 = Width / 2 - 1;
            int inward = Team == 0 ? 1 : -1;
            int zStart = Team == 0 ? FlagEdgeMargin : Depth - 2 - FlagEdgeMargin;
            for (int step = 0; step < Depth / 2; step++)
            {
                int z0 = zStart + step * inward;
                if (!PadFits(x0, z0))
                    continue;

                int h = 0;
                for (int dx = 0; dx < 2; dx++)
                for (int dz = 0; dz < 2; dz++)
                    h = Mathf.Max(h, GetTopHeight(x0 + dx, z0 + dz));
                for (int dx = 0; dx < 2; dx++)
                for (int dz = 0; dz < 2; dz++)
                {
                    int x = x0 + dx, z = z0 + dz;
                    for (int y = 0; y < MaxHeight; y++)
                    {
                        int i = Index(x, y, z);
                        _hp[i] = y >= h ? (byte)0 : IsStoneLayer(y) || y == h - 1 ? StoneMaxHp : BlockMaxHp;
                        if (y < h)
                            _unbreakable.Add(i);
                    }
                    _flagStone.Add(Index(x, h - 1, z));
                    _surface[x + Width * z] = (byte)h;
                    _noGrass[x + Width * z] = true;
                }
                FlagCell = new Vector2Int(x0, z0);
                FlagBase = BlockToWorld(x0 + 1f, h, z0 + 1f);
                return;
            }
            FlagCell = new Vector2Int(x0, Depth / 2);
            FlagBase = Center + Vector3.up * MaxHeight;
        }

        /// <summary>Под площадку 2×2: все её столбцы и кольцо вокруг есть (не на краю и не над ямой).</summary>
        bool PadFits(int x0, int z0)
        {
            for (int dx = -1; dx <= 2; dx++)
            for (int dz = -1; dz <= 2; dz++)
                if (GetTopHeight(x0 + dx, z0 + dz) == 0)
                    return false;
            return true;
        }

        /// <summary>Расстояние по горизонтали (в кубиках) от столбца до центра площадки флага.</summary>
        public float DistanceToFlag(float x, float z) => Vector2.Distance(new Vector2(x, z), new Vector2(FlagCell.x + 1f, FlagCell.y + 1f));

        /// <summary>
        /// Оттенок травы столбца (0 — зелёный … VoxelAtlas.GrassPalettes−1 — тёпло-зелёный): плавные пятна шума
        /// плюс небольшой разброс у соседей. Общий для кубика и пучков травы на нём; у всех игроков одинаковый.
        /// </summary>
        public int GrassPalette(int x, int z)
        {
            float warmth = Mathf.PerlinNoise(x * 0.09f + Seed % 1000, z * 0.09f + Team * 37.7f);
            int jitter = ((x * 73428767) ^ (z * 912931) ^ (Seed * 31)) & int.MaxValue;
            warmth += (jitter % 5 - 2) * 0.08f;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(0.2f, 0.8f, warmth) * VoxelAtlas.GrassPalettes), 0, VoxelAtlas.GrassPalettes - 1);
        }

        /// <summary>Можно ли посадить пучок травы на кубик (x, y, z): это нетронутый верх исходной поверхности.</summary>
        public bool CanGrowGrass(int x, int y, int z)
        {
            if (x < 0 || z < 0 || x >= Width || z >= Depth || IsStoneBlock(x, y, z))
                return false;
            int column = x + Width * z;
            return !_noGrass[column] && _surface[column] == y + 1 && !IsSolid(x, y + 1, z);
        }

        /// <summary>Убрать траву в прямоугольнике столбцов (включительно) — под постройками.</summary>
        public void ClearGrass(int x0, int z0, int x1, int z1)
        {
            for (int x = Mathf.Max(0, x0); x <= Mathf.Min(Width - 1, x1); x++)
            for (int z = Mathf.Max(0, z0); z <= Mathf.Min(Depth - 1, z1); z++)
            {
                _noGrass[x + Width * z] = true;
                MarkDirty(x, z);
            }
        }

        /// <summary>Контрольная сумма HP всех кубиков — для проверки, что у клиентов тот же террейн.</summary>
        public int ComputeChecksum()
        {
            unchecked
            {
                int hash = 17;
                foreach (var hp in _hp)
                    hash = hash * 31 + hp;
                return hash;
            }
        }

        public bool IsSolid(int x, int y, int z)
        {
            if (x < 0 || y < 0 || z < 0 || x >= Width || y >= MaxHeight || z >= Depth)
                return false;
            return _hp[Index(x, y, z)] > 0;
        }

        /// <summary>Высота верхней грани столбца (0 — столбца нет).</summary>
        public int GetTopHeight(int x, int z)
        {
            for (int y = MaxHeight - 1; y >= 0; y--)
                if (IsSolid(x, y, z))
                    return y + 1;
            return 0;
        }

        public Vector3 BlockToWorld(float x, float y, float z) => transform.position + new Vector3(x, y, z);

        public Vector3 Center => transform.position + new Vector3(Width * 0.5f, 0, Depth * 0.5f);

        public Bounds WorldBounds => new Bounds(Center + Vector3.up * MaxHeight * 0.5f, new Vector3(Width, MaxHeight, Depth));

        /// <summary>
        /// Случайная точка над островом: столбец, у которого есть все 8 соседей (не на самом краю).
        /// </summary>
        public Vector3 GetRandomSpawnPoint(float heightAbove)
        {
            for (int attempt = 0; attempt < 500; attempt++)
            {
                int x = UnityEngine.Random.Range(1, Width - 1);
                int z = UnityEngine.Random.Range(1, Depth - 1);
                int top = GetTopHeight(x, z);
                if (top == 0 || !HasAllNeighbours(x, z))
                    continue;
                return BlockToWorld(x + 0.5f, top + heightAbove, z + 0.5f);
            }

            return Center + Vector3.up * (MaxHeight + heightAbove);
        }

        bool HasAllNeighbours(int x, int z)
        {
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (GetTopHeight(x + dx, z + dz) == 0)
                    return false;
            return true;
        }

        /// <summary>
        /// Наносит урон кубикам в сфере. damageByDistance возвращает урон по расстоянию до центра кубика.
        /// Возвращает число разрушенных кубиков.
        /// </summary>
        public int DamageSphere(Vector3 worldCenter, float radius, Func<float, float> damageByDistance,
            List<Vector3> destroyedCenters = null, List<BlockKind> destroyedKinds = null)
        {
            Vector3 local = worldCenter - transform.position;
            int minX = Mathf.Max(0, Mathf.FloorToInt(local.x - radius));
            int maxX = Mathf.Min(Width - 1, Mathf.CeilToInt(local.x + radius));
            int minY = Mathf.Max(0, Mathf.FloorToInt(local.y - radius));
            int maxY = Mathf.Min(MaxHeight - 1, Mathf.CeilToInt(local.y + radius));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(local.z - radius));
            int maxZ = Mathf.Min(Depth - 1, Mathf.CeilToInt(local.z + radius));
            if (minX > maxX || minY > maxY || minZ > maxZ)
                return 0;

            int destroyed = 0;
            for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            for (int z = minZ; z <= maxZ; z++)
            {
                int i = Index(x, y, z);
                if (_hp[i] == 0 || (_unbreakable.Count > 0 && _unbreakable.Contains(i)))
                    continue;

                float dist = Vector3.Distance(local, new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                float dmg = damageByDistance(dist);
                if (dmg <= 0)
                    continue;

                int hp = _hp[i] - Mathf.CeilToInt(dmg);
                if (hp <= 0)
                {
                    // вид кубика для обломков; «верх» (трава) смотрим до разрушения верхнего соседа — y идёт снизу вверх
                    destroyedKinds?.Add(IsStoneBlock(x, y, z) ? BlockKind.Stone : !IsSolid(x, y + 1, z) ? BlockKind.Grass : BlockKind.Dirt);
                    destroyedCenters?.Add(BlockToWorld(x + 0.5f, y + 0.5f, z + 0.5f));
                    _hp[i] = 0;
                    destroyed++;
                    MarkDirty(x, z);
                }
                else
                {
                    _hp[i] = (byte)hp;
                }
            }

            if (destroyed > 0)
                BlocksChanged?.Invoke();

            return destroyed;
        }

        void MarkDirty(int x, int z)
        {
            int cx = x / ChunkSize, cz = z / ChunkSize;
            _dirty.Add(_chunks[cx, cz]);
            // грань соседнего чанка тоже могла открыться
            if (x % ChunkSize == 0 && cx > 0) _dirty.Add(_chunks[cx - 1, cz]);
            if (x % ChunkSize == ChunkSize - 1 && cx < _chunks.GetLength(0) - 1) _dirty.Add(_chunks[cx + 1, cz]);
            if (z % ChunkSize == 0 && cz > 0) _dirty.Add(_chunks[cx, cz - 1]);
            if (z % ChunkSize == ChunkSize - 1 && cz < _chunks.GetLength(1) - 1) _dirty.Add(_chunks[cx, cz + 1]);
        }

        /// <summary>
        /// Меши и коллайдеры перестраиваются не сразу, а с бюджетом на кадр: серия взрывов (или история взрывов
        /// у подключившегося игрока) не даёт одного тяжёлого кадра. Данные кубиков меняются мгновенно.
        /// </summary>
        void LateUpdate()
        {
            if (_dirty.Count == 0)
                return;
            int budget = MaxRebuildsPerFrame;
            _rebuildBuffer.Clear();
            foreach (var chunk in _dirty)
            {
                if (budget-- <= 0)
                    break;
                _rebuildBuffer.Add(chunk);
            }
            foreach (var chunk in _rebuildBuffer)
            {
                _dirty.Remove(chunk);
                chunk.Rebuild();
            }
        }

        public bool HasPendingRebuilds => _dirty.Count > 0;
    }
}
