using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>Кусок острова 16×16 столбцов: меш только видимых граней + MeshCollider, отдельным мешом — пучки травы.</summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public class VoxelChunk : MonoBehaviour
    {
        // Грань: нормаль + 4 вершины в порядке «низ-лево, верх-лево, верх-право, низ-право» (по часовой снаружи).
        static readonly Vector3Int[] FaceDirs =
        {
            Vector3Int.up, Vector3Int.down, Vector3Int.right, Vector3Int.left, new Vector3Int(0, 0, 1), new Vector3Int(0, 0, -1)
        };

        static readonly Vector3[][] FaceVerts =
        {
            new[] { new Vector3(0, 1, 0), new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 1, 0) }, // +Y
            new[] { new Vector3(0, 0, 0), new Vector3(1, 0, 0), new Vector3(1, 0, 1), new Vector3(0, 0, 1) }, // -Y
            new[] { new Vector3(1, 0, 0), new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1) }, // +X
            new[] { new Vector3(0, 0, 1), new Vector3(0, 1, 1), new Vector3(0, 1, 0), new Vector3(0, 0, 0) }, // -X
            new[] { new Vector3(1, 0, 1), new Vector3(1, 1, 1), new Vector3(0, 1, 1), new Vector3(0, 0, 1) }, // +Z
            new[] { new Vector3(0, 0, 0), new Vector3(0, 1, 0), new Vector3(1, 1, 0), new Vector3(1, 0, 0) }, // -Z
        };

        VoxelIsland _island;
        int _originX, _originZ;
        Mesh _mesh, _grassMesh;
        MeshCollider _collider;

        readonly List<Vector3> _grassVerts = new List<Vector3>();
        readonly List<Vector3> _grassNormals = new List<Vector3>();
        readonly List<Vector2> _grassUvs = new List<Vector2>();
        readonly List<int> _grassTris = new List<int>();

        readonly List<Vector3> _verts = new List<Vector3>();
        readonly List<Vector3> _normals = new List<Vector3>();
        readonly List<Vector2> _uvs = new List<Vector2>();
        readonly List<int> _tris = new List<int>();

        public void Init(VoxelIsland island, int cx, int cz, Material material)
        {
            _island = island;
            _originX = cx * VoxelIsland.ChunkSize;
            _originZ = cz * VoxelIsland.ChunkSize;
            _mesh = new Mesh { name = name };
            _mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = _mesh;
            GetComponent<MeshRenderer>().sharedMaterial = material;
            _collider = GetComponent<MeshCollider>();

            // трава — без коллайдера и без тени (иначе мерцающие тени от сотен квадов)
            var grass = new GameObject("Grass");
            grass.transform.SetParent(transform, false);
            _grassMesh = new Mesh { name = name + "_grass" };
            _grassMesh.MarkDynamic();
            grass.AddComponent<MeshFilter>().sharedMesh = _grassMesh;
            var grassRenderer = grass.AddComponent<MeshRenderer>();
            grassRenderer.sharedMaterial = GrassTufts.GetMaterial();
            grassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        public void Rebuild()
        {
            _verts.Clear();
            _normals.Clear();
            _uvs.Clear();
            _tris.Clear();
            _grassVerts.Clear();
            _grassNormals.Clear();
            _grassUvs.Clear();
            _grassTris.Clear();

            int maxX = Mathf.Min(_originX + VoxelIsland.ChunkSize, VoxelIsland.Width);
            int maxZ = Mathf.Min(_originZ + VoxelIsland.ChunkSize, VoxelIsland.Depth);
            for (int x = _originX; x < maxX; x++)
            for (int z = _originZ; z < maxZ; z++)
            for (int y = 0; y < VoxelIsland.MaxHeight; y++)
            {
                if (!_island.IsSolid(x, y, z))
                    continue;

                bool covered = _island.IsSolid(x, y + 1, z);
                for (int f = 0; f < 6; f++)
                {
                    var d = FaceDirs[f];
                    if (_island.IsSolid(x + d.x, y + d.y, z + d.z))
                        continue;
                    AddFace(f, new Vector3(x - _originX, y, z - _originZ), PickTile(f, covered, x, y, z));
                }

                if (!covered && _island.CanGrowGrass(x, y, z))
                    TryAddGrass(x, y, z);
            }

            _mesh.Clear();
            _mesh.SetVertices(_verts);
            _mesh.SetNormals(_normals);
            _mesh.SetUVs(0, _uvs);
            _mesh.SetTriangles(_tris, 0);
            _mesh.RecalculateBounds();

            _collider.sharedMesh = null;
            if (_verts.Count > 0)
                _collider.sharedMesh = _mesh;

            _grassMesh.Clear();
            _grassMesh.SetVertices(_grassVerts);
            _grassMesh.SetNormals(_grassNormals);
            _grassMesh.SetUVs(0, _grassUvs);
            _grassMesh.SetTriangles(_grassTris, 0);
            _grassMesh.RecalculateBounds();
        }

        /// <summary>Пучок травы на части верхних кубиков (детерминированно по координатам — у всех одинаково).</summary>
        void TryAddGrass(int x, int y, int z)
        {
            int hash = ((x * 92837111) ^ (z * 689287499) ^ (_island.Seed * 283923481)) & int.MaxValue;
            if (hash % 100 >= GrassTufts.Density)
                return;
            hash /= 100;
            // небольшой сдвиг внутри клетки, чтобы пучки не стояли ровной сеткой
            float ox = (hash % 7 - 3) * 0.06f, oz = (hash / 7 % 7 - 3) * 0.06f;
            var bottom = new Vector3(x - _originX + 0.5f + ox, y + 1f, z - _originZ + 0.5f + oz);

            // пучок в тон своему кубику
            GrassTufts.Add(_grassVerts, _grassNormals, _grassUvs, _grassTris, bottom, hash / 49, _island.GrassPalette(x, z));
        }

        int PickTile(int face, bool covered, int x, int y, int z)
        {
            int hash = (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
            hash &= int.MaxValue;
            if (_island.IsStoneBlock(x, y, z))
                return VoxelAtlas.Stone0 + hash % VoxelAtlas.StoneVariants;
            if (face == 0)
                return covered ? VoxelAtlas.Dirt0 : VoxelAtlas.GrassTop(_island.GrassPalette(x, z), hash % VoxelAtlas.GrassTopVariants);
            if (face == 1)
                return VoxelAtlas.Dirt0 + 1;
            return covered ? VoxelAtlas.Dirt0 + hash % VoxelAtlas.DirtVariants : VoxelAtlas.GrassSideTile(_island.GrassPalette(x, z));
        }

        void AddFace(int face, Vector3 basePos, int tile)
        {
            int start = _verts.Count;
            var verts = FaceVerts[face];
            Vector3 normal = FaceDirs[face];
            var r = VoxelAtlas.TileRect(tile);

            for (int i = 0; i < 4; i++)
            {
                _verts.Add(basePos + verts[i]);
                _normals.Add(normal);
            }

            _uvs.Add(new Vector2(r.xMin, r.yMin));
            _uvs.Add(new Vector2(r.xMin, r.yMax));
            _uvs.Add(new Vector2(r.xMax, r.yMax));
            _uvs.Add(new Vector2(r.xMax, r.yMin));

            _tris.Add(start);
            _tris.Add(start + 1);
            _tris.Add(start + 2);
            _tris.Add(start);
            _tris.Add(start + 2);
            _tris.Add(start + 3);
        }
    }
}
