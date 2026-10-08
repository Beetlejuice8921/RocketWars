using System.Collections.Generic;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Launcher
{
    /// <summary>Случайная расстановка шахт на ровных площадках 3×3, не ближе minDistance друг к другу.</summary>
    public static class SiloPlacer
    {
        public const int SilosPerTeam = 6;
        /// <summary>Шахта не ближе к флагу команды (в кубиках): у флага должно быть свободно.</summary>
        const float FlagClearance = 6f;

        public static List<RocketSilo> Place(VoxelIsland island, int seed, float minDistance = 14f)
        {
            var rng = new System.Random(seed);
            var silos = new List<RocketSilo>();
            var used = new List<Vector2>();
            float dist = minDistance;

            // при неудаче постепенно ослабляем требование к расстоянию
            for (int pass = 0; pass < 4 && silos.Count < SilosPerTeam; pass++, dist *= 0.75f)
            {
                for (int attempt = 0; attempt < 3000 && silos.Count < SilosPerTeam; attempt++)
                {
                    int x = rng.Next(2, VoxelIsland.Width - 2);
                    int z = rng.Next(2, VoxelIsland.Depth - 2);
                    if (!IsFlat(island, x, z, out int top) || island.DistanceToFlag(x + 0.5f, z + 0.5f) < FlagClearance)
                        continue;

                    var p = new Vector2(x, z);
                    bool tooClose = false;
                    foreach (var u in used)
                        if (Vector2.Distance(u, p) < dist)
                        {
                            tooClose = true;
                            break;
                        }
                    if (tooClose)
                        continue;

                    used.Add(p);
                    silos.Add(RocketSilo.Create(island,island.BlockToWorld(x + 0.5f, top, z + 0.5f), island.transform.parent));
                    island.ClearGrass(x - 1, z - 1, x + 1, z + 1); // трава не прорастает сквозь плиту шахты
                }
            }

            return silos;
        }

        /// <summary>Площадка 3×3 одной высоты плюс кольцо вокруг (чтобы шахта не висела на самом краю).</summary>
        static bool IsFlat(VoxelIsland island, int cx, int cz, out int top)
        {
            top = island.GetTopHeight(cx, cz);
            if (top == 0)
                return false;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (island.GetTopHeight(cx + dx, cz + dz) != top)
                    return false;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                if (island.GetTopHeight(cx + dx, cz + dz) == 0)
                    return false;
            return true;
        }
    }
}
