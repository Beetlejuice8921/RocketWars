using System.Collections.Generic;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.UI
{
    /// <summary>
    /// Земля сцены главного меню: воксельное травяное поле (тот же атлас и те же пучки травы, что на островах)
    /// вокруг камеры и поля боя, у горизонта — ступенчатые холмы (пятый план), дальше — ровная зелёная плоскость.
    /// Неразрушимая: взрывы в меню только раскидывают комья, кубики не трогают. Верх поля — y = Top.
    /// </summary>
    public static class MenuGround
    {
        public const float Top = 1f;
        const int MinX = -112, MaxX = 112, MinZ = -8, MaxZ = 168;
        const int Chunk = 32;
        const int Seed = 4242;
        /// <summary>Пучки травы: густо у камеры, реже вдали, дальше TuftsFar не сажаем (там они — пиксельный шум).</summary>
        const float TuftsNear = 45f, TuftsFar = 110f;
        /// <summary>У самой камеры пучков нет — иначе они загораживают полэкрана.</summary>
        const float TuftsClear = 4f;
        static readonly Color FarColor = new Color32(116, 174, 64, 255);

        /// <summary>
        /// Высота столбца (1 или 2 кубика): редкие бугорки по бокам; поле боя, передний план и даль ровные
        /// (стенки бугорков у горизонта рябят тёмной полосой).
        /// </summary>
        static int Height(int x, int z)
        {
            bool open = Mathf.Abs(x - 4) < 34 || z < 20 || z > 70;
            if (open)
                return 1;
            return Mathf.PerlinNoise(x * 0.11f + 31.7f, z * 0.11f + 12.3f) > 0.7f ? 2 : 1;
        }

        static int Palette(int x, int z)
        {
            float warmth = Mathf.PerlinNoise(x * 0.09f + 77f, z * 0.09f + 13f);
            int jitter = ((x * 73428767) ^ (z * 912931)) & int.MaxValue;
            warmth += (jitter % 5 - 2) * 0.08f;
            return Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(0.2f, 0.8f, warmth) * VoxelAtlas.GrassPalettes), 0, VoxelAtlas.GrassPalettes - 1);
        }

        public static void Build(Transform parent)
        {
            var root = new GameObject("Ground").transform;
            root.SetParent(parent, false);

            // дальняя плоскость чуть ниже верха кубиков — под воксельным полем её не видно; о неё бьются комья взрывов
            var far = GameObject.CreatePrimitive(PrimitiveType.Cube);
            far.name = "FarField";
            far.transform.SetParent(root, false);
            far.transform.localPosition = new Vector3(0f, Top - 0.02f - 0.05f, 0f);
            far.transform.localScale = new Vector3(3000f, 0.1f, 3000f);
            var farRenderer = far.GetComponent<MeshRenderer>();
            farRenderer.sharedMaterial = Core.RuntimeMaterials.Lit(FarColor);
            farRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var material = VoxelAtlas.GetMaterial(Biome.Grass);
            var tuftMaterial = GrassTufts.GetMaterial();
            for (int cx = MinX; cx < MaxX; cx += Chunk)
            for (int cz = MinZ; cz < MaxZ; cz += Chunk)
                BuildChunk(root, cx, cz, material, tuftMaterial);

            for (int cx = HillsMinX; cx < HillsMaxX; cx += Chunk)
            for (int cz = HillsMinZ; cz < HillsMaxZ; cz += Chunk)
                BuildHillChunk(root, cx, cz, material);
        }

        // ---------- холмы (пятый план) ----------

        /// <summary>
        /// Холмы у горизонта — ступенчатые, из кубиков покрупнее (HillBlock м: вдали разницы не видно, а мешей вчетверо
        /// меньше). Сетка холмов — в кубиках HillBlock; полоса по глубине — от HillsNear до HillsFar метров.
        /// </summary>
        const float HillBlock = 2f;
        const int HillsMinX = -140, HillsMaxX = 140, HillsMinZ = 56, HillsMaxZ = 148;
        const float HillsNear = 110f, HillsFar = 290f;
        const float HillMaxHeight = 48f;

        /// <summary>Высота холма в кубиках HillBlock над полем (0 — ровно): два слоя шума, к краям полосы сходит на нет.</summary>
        static int HillHeight(int gx, int gz)
        {
            float x = gx * HillBlock, z = gz * HillBlock;
            float mask = Mathf.Clamp01((z - HillsNear) / 40f) * Mathf.Clamp01((HillsFar - z) / 30f);
            if (mask <= 0f)
                return 0;
            float n = Mathf.PerlinNoise(x * 0.01f + 5.3f, z * 0.01f + 9.1f) * 0.7f + Mathf.PerlinNoise(x * 0.03f + 1.7f, z * 0.03f + 2.9f) * 0.3f;
            return Mathf.FloorToInt(Mathf.Max(0f, n - 0.4f) * HillMaxHeight * mask / HillBlock);
        }

        static void BuildHillChunk(Transform root, int gx0, int gz0, Material material)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 right = Vector3.right * HillBlock, forward = Vector3.forward * HillBlock, up = Vector3.up * HillBlock;

            for (int gx = gx0; gx < Mathf.Min(gx0 + Chunk, HillsMaxX); gx++)
            for (int gz = gz0; gz < Mathf.Min(gz0 + Chunk, HillsMaxZ); gz++)
            {
                int h = HillHeight(gx, gz);
                if (h == 0)
                    continue;
                int palette = Palette(gx, gz);
                int hash = ((gx * 73856093) ^ (gz * 83492791)) & int.MaxValue;
                Vector3 corner = new Vector3(gx, 0f, gz) * HillBlock;
                Quad(verts, normals, uvs, tris, corner + up * h, right, forward, Vector3.up,
                    VoxelAtlas.GrassTop(palette, hash % VoxelAtlas.GrassTopVariants));

                // стенки к соседям пониже: верхний кубик с травяной кромкой, под ним земля
                for (int y = 0; y < h; y++)
                {
                    int tile = y == h - 1 ? VoxelAtlas.GrassSideTile(palette) : VoxelAtlas.Dirt0 + (hash + y) % VoxelAtlas.DirtVariants;
                    Vector3 b = corner + up * y;
                    if (HillHeight(gx + 1, gz) <= y) Quad(verts, normals, uvs, tris, b + right, forward, up, Vector3.right, tile);
                    if (HillHeight(gx - 1, gz) <= y) Quad(verts, normals, uvs, tris, b + forward, -forward, up, Vector3.left, tile);
                    if (HillHeight(gx, gz + 1) <= y) Quad(verts, normals, uvs, tris, b + right + forward, -right, up, Vector3.forward, tile);
                    if (HillHeight(gx, gz - 1) <= y) Quad(verts, normals, uvs, tris, b, right, up, Vector3.back, tile);
                }
            }
            if (verts.Count == 0)
                return;

            var go = new GameObject($"Hills_{gx0}_{gz0}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, Top, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = MakeMesh(go.name, verts, normals, uvs, tris);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        static void BuildChunk(Transform root, int x0, int z0, Material material, Material tuftMaterial)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var tVerts = new List<Vector3>();
            var tNormals = new List<Vector3>();
            var tUvs = new List<Vector2>();
            var tTris = new List<int>();

            int x1 = Mathf.Min(x0 + Chunk, MaxX), z1 = Mathf.Min(z0 + Chunk, MaxZ);
            for (int x = x0; x < x1; x++)
            for (int z = z0; z < z1; z++)
            {
                int h = Height(x, z);
                int palette = Palette(x, z);
                int hash = ((x * 73856093) ^ (z * 83492791)) & int.MaxValue;
                // верх; стенки — только там, где сосед ниже (бугорок), низ поля не виден никогда
                Quad(verts, normals, uvs, tris, new Vector3(x, h, z), Vector3.right, Vector3.forward, Vector3.up,
                    VoxelAtlas.GrassTop(palette, hash % VoxelAtlas.GrassTopVariants));
                if (h > 1)
                {
                    int side = VoxelAtlas.GrassSideTile(palette);
                    if (Height(x + 1, z) < h) Quad(verts, normals, uvs, tris, new Vector3(x + 1, h - 1, z), Vector3.forward, Vector3.up, Vector3.right, side);
                    if (Height(x - 1, z) < h) Quad(verts, normals, uvs, tris, new Vector3(x, h - 1, z + 1), Vector3.back, Vector3.up, Vector3.left, side);
                    if (Height(x, z + 1) < h) Quad(verts, normals, uvs, tris, new Vector3(x + 1, h - 1, z + 1), Vector3.left, Vector3.up, Vector3.forward, side);
                    if (Height(x, z - 1) < h) Quad(verts, normals, uvs, tris, new Vector3(x, h - 1, z), Vector3.right, Vector3.up, Vector3.back, side);
                }

                // пучки травы — как на островах, реже с расстоянием
                float dist = Mathf.Sqrt(x * x + z * z);
                if (dist > TuftsFar || dist < TuftsClear)
                    continue;
                int density = dist < TuftsNear ? GrassTufts.Density(Biome.Grass) : GrassTufts.Density(Biome.Grass) / 3;
                int tuftHash = ((x * 92837111) ^ (z * 689287499) ^ unchecked(Seed * 283923481)) & int.MaxValue;
                if (tuftHash % 100 >= density)
                    continue;
                tuftHash /= 100;
                float ox = (tuftHash % 7 - 3) * 0.06f, oz = (tuftHash / 7 % 7 - 3) * 0.06f;
                GrassTufts.Add(tVerts, tNormals, tUvs, tTris, new Vector3(x + 0.5f + ox, h, z + 0.5f + oz), tuftHash / 49, palette, Biome.Grass);
            }

            var go = new GameObject($"Chunk_{x0}_{z0}");
            go.transform.SetParent(root, false);
            go.transform.localPosition = new Vector3(0f, Top - 1f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = MakeMesh(go.name, verts, normals, uvs, tris);
            go.AddComponent<MeshRenderer>().sharedMaterial = material;

            if (tVerts.Count == 0)
                return;
            var grass = new GameObject("Grass");
            grass.transform.SetParent(go.transform, false);
            grass.AddComponent<MeshFilter>().sharedMesh = MakeMesh(go.name + "_grass", tVerts, tNormals, tUvs, tTris);
            var r = grass.AddComponent<MeshRenderer>();
            r.sharedMaterial = tuftMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>Квадрат 1×1 с углом в origin, сторонами du и dv (по часовой снаружи, как грани VoxelChunk).</summary>
        static void Quad(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris,
            Vector3 origin, Vector3 du, Vector3 dv, Vector3 normal, int tile)
        {
            int start = verts.Count;
            verts.Add(origin);
            verts.Add(origin + dv);
            verts.Add(origin + du + dv);
            verts.Add(origin + du);
            for (int i = 0; i < 4; i++)
                normals.Add(normal);
            var r = VoxelAtlas.TileRect(tile);
            uvs.Add(new Vector2(r.xMin, r.yMin));
            uvs.Add(new Vector2(r.xMin, r.yMax));
            uvs.Add(new Vector2(r.xMax, r.yMax));
            uvs.Add(new Vector2(r.xMax, r.yMin));
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }

        static Mesh MakeMesh(string name, List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
