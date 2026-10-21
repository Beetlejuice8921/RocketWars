using System.Collections.Generic;
using RocketWars.Launcher;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Loot
{
    /// <summary>
    /// Случайная расстановка ящиков с припасами: по 4 на остров, не у самого края, не вплотную к шахтам и пультам
    /// и не слишком близко друг к другу. Содержимое каждого ящика — случайное (детерминированно по seed).
    /// </summary>
    public static class CratePlacer
    {
        public const int CratesPerIsland = 4;
        /// <summary>От центра шахты: площадка 3×3 и пульт в 2,4 м сбоку.</summary>
        const float SiloClearance = 5.5f;

        /// <summary>Точка для ящика: клетка острова, содержимое и поворот (шаг 90°).</summary>
        public struct Spot
        {
            public int X, Z;
            public LootKind Kind;
            public int YawStep;
        }

        public static List<LootCrate> Place(VoxelIsland island, int seed, IReadOnlyList<RocketSilo> silos, Transform parent,
            float minDistance = 14f)
        {
            var crates = new List<LootCrate>();
            foreach (var spot in Pick(island, new System.Random(seed), silos, CratesPerIsland, minDistance, null))
            {
                crates.Add(LootCrate.Create(island, spot.X, spot.Z, spot.Kind, spot.YawStep * 90f, parent));
                island.ClearGrass(spot.X, spot.Z, spot.X, spot.Z); // и сквозь дно ящика
                island.BlockSpawn(spot.X - 1, spot.Z - 1, spot.X + 1, spot.Z + 1);
            }
            return crates;
        }

        /// <summary>
        /// Подобрать count мест: на целой земле, не у шахт и флага, не ближе minDistance друг к другу
        /// и к уже стоящим ящикам (avoid — клетки острова). Не нашлось места — расстояние постепенно уменьшается.
        /// </summary>
        public static List<Spot> Pick(VoxelIsland island, System.Random rng, IReadOnlyList<RocketSilo> silos, int count,
            float minDistance, IReadOnlyList<Vector2> avoid)
        {
            var spots = new List<Spot>();
            var used = new List<Vector2>();
            if (avoid != null)
                used.AddRange(avoid);
            float dist = minDistance;

            for (int pass = 0; pass < 4 && spots.Count < count; pass++, dist *= 0.7f)
            {
                for (int attempt = 0; attempt < 2000 && spots.Count < count; attempt++)
                {
                    int x = rng.Next(3, VoxelIsland.Width - 3);
                    int z = rng.Next(3, VoxelIsland.Depth - 3);
                    if (!IsSolidGround(island, x, z))
                        continue;

                    var p = new Vector2(x, z);
                    if (TooClose(p, used, dist) || NearSilo(island, x, z, silos) || island.DistanceToFlag(x + 0.5f, z + 0.5f) < 4f)
                        continue;

                    used.Add(p);
                    var kind = (LootKind)rng.Next(6);
                    int yawStep = rng.Next(4);
                    spots.Add(new Spot { X = x, Z = z, Kind = kind, YawStep = yawStep });
                }
            }

            return spots;
        }

        /// <summary>Столбец и все соседи на месте (ящик не на краю и не над ямой).</summary>
        static bool IsSolidGround(VoxelIsland island, int x, int z)
        {
            int top = island.GetTopHeight(x, z);
            if (top == 0)
                return false;
            for (int dx = -2; dx <= 2; dx++)
            for (int dz = -2; dz <= 2; dz++)
                if (island.GetTopHeight(x + dx, z + dz) == 0)
                    return false;
            return true;
        }

        static bool TooClose(Vector2 p, List<Vector2> used, float dist)
        {
            foreach (var u in used)
                if (Vector2.Distance(u, p) < dist)
                    return true;
            return false;
        }

        static bool NearSilo(VoxelIsland island, int x, int z, IReadOnlyList<RocketSilo> silos)
        {
            Vector3 world = island.BlockToWorld(x + 0.5f, 0f, z + 0.5f);
            foreach (var silo in silos)
            {
                Vector3 d = silo.transform.position - world;
                d.y = 0f;
                if (d.magnitude < SiloClearance)
                    return true;
            }
            return false;
        }
    }
}
