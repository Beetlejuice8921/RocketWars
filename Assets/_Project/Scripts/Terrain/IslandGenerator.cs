using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>
    /// Генерирует карту высот острова: 0 — пустота, 1..maxHeight — высота столбца в кубиках.
    /// Рваный край — эллиптическая маска + шум Перлина, остаётся только самый большой связный кусок.
    /// </summary>
    public static class IslandGenerator
    {
        const float EdgeThreshold = 0.12f;
        const float MinFill = 0.45f;

        public static int[,] Generate(int seed, int width, int depth, int maxHeight)
        {
            var rng = new System.Random(seed);
            int[,] heights = null;

            for (int attempt = 0; attempt < 10; attempt++)
            {
                heights = TryGenerate(rng, width, depth, maxHeight, out int filled);
                if (filled >= width * depth * MinFill)
                    break;
            }

            return heights;
        }

        static int[,] TryGenerate(System.Random rng, int width, int depth, int maxHeight, out int filled)
        {
            float edgeOffX = rng.Next(-10000, 10000), edgeOffZ = rng.Next(-10000, 10000);
            float detailOffX = rng.Next(-10000, 10000), detailOffZ = rng.Next(-10000, 10000);
            float heightOffX = rng.Next(-10000, 10000), heightOffZ = rng.Next(-10000, 10000);

            var mask = new bool[width, depth];
            var edgeValue = new float[width, depth];
            float halfW = width * 0.5f, halfD = depth * 0.5f;

            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                float nx = (x + 0.5f - halfW) / halfW;
                float nz = (z + 0.5f - halfD) / halfD;
                float r = Mathf.Sqrt(nx * nx + nz * nz);

                float coarse = Mathf.PerlinNoise((x + edgeOffX) * 0.08f, (z + edgeOffZ) * 0.08f) - 0.5f;
                float detail = Mathf.PerlinNoise((x + detailOffX) * 0.22f, (z + detailOffZ) * 0.22f) - 0.5f;
                float v = (1f - r) + coarse * 0.9f + detail * 0.35f;

                edgeValue[x, z] = v;
                mask[x, z] = v > EdgeThreshold;
            }

            KeepLargestComponent(mask, width, depth);
            FillHoles(mask, width, depth);

            var heights = new int[width, depth];
            filled = 0;
            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                if (!mask[x, z])
                    continue;

                float hn = Mathf.PerlinNoise((x + heightOffX) * 0.06f, (z + heightOffZ) * 0.06f) * 1.25f - 0.15f;
                // ближе к краю остров тоньше
                hn *= Mathf.Clamp01((edgeValue[x, z] - EdgeThreshold) * 3f + 0.35f);
                int h = 1 + Mathf.FloorToInt(Mathf.Clamp(hn, 0f, 0.999f) * maxHeight);
                heights[x, z] = Mathf.Clamp(h, 1, maxHeight);
                filled++;
            }

            return heights;
        }

        /// <summary>
        /// Заливает «дыры» внутри острова: пустые клетки, до которых нельзя дойти от края карты
        /// (шум иногда проваливается ниже порога посреди суши). Заливы, открытые к краю, остаются.
        /// </summary>
        static void FillHoles(bool[,] mask, int width, int depth)
        {
            var outside = new bool[width, depth];
            var queue = new Queue<Vector2Int>();

            void Seed(int x, int z)
            {
                if (mask[x, z] || outside[x, z])
                    return;
                outside[x, z] = true;
                queue.Enqueue(new Vector2Int(x, z));
            }

            for (int x = 0; x < width; x++)
            {
                Seed(x, 0);
                Seed(x, depth - 1);
            }
            for (int z = 0; z < depth; z++)
            {
                Seed(0, z);
                Seed(width - 1, z);
            }

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                if (p.x > 0) Seed(p.x - 1, p.y);
                if (p.x < width - 1) Seed(p.x + 1, p.y);
                if (p.y > 0) Seed(p.x, p.y - 1);
                if (p.y < depth - 1) Seed(p.x, p.y + 1);
            }

            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                if (!mask[x, z] && !outside[x, z])
                    mask[x, z] = true;
        }

        static void KeepLargestComponent(bool[,] mask, int width, int depth)
        {
            var label = new int[width, depth];
            var queue = new Queue<Vector2Int>();
            int bestLabel = 0, bestSize = 0, current = 0;

            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
            {
                if (!mask[x, z] || label[x, z] != 0)
                    continue;

                current++;
                int size = 0;
                label[x, z] = current;
                queue.Enqueue(new Vector2Int(x, z));
                while (queue.Count > 0)
                {
                    var p = queue.Dequeue();
                    size++;
                    TryVisit(p.x + 1, p.y);
                    TryVisit(p.x - 1, p.y);
                    TryVisit(p.x, p.y + 1);
                    TryVisit(p.x, p.y - 1);
                }

                if (size > bestSize)
                {
                    bestSize = size;
                    bestLabel = current;
                }
            }

            for (int x = 0; x < width; x++)
            for (int z = 0; z < depth; z++)
                mask[x, z] = label[x, z] == bestLabel && bestLabel != 0;

            void TryVisit(int vx, int vz)
            {
                if (vx < 0 || vz < 0 || vx >= width || vz >= depth)
                    return;
                if (!mask[vx, vz] || label[vx, vz] != 0)
                    return;
                label[vx, vz] = current;
                queue.Enqueue(new Vector2Int(vx, vz));
            }
        }
    }
}
