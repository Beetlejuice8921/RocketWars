using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RocketWars.Core
{
    /// <summary>
    /// Сборщик «воксельных» моделей из параллелепипедов: один меш, по сабмешу на цвет.
    /// Готовые меши кэшируются по ключу — одинаковые шахты/игроки/ракеты используют один меш.
    /// </summary>
    public class BoxModel
    {
        static readonly Dictionary<string, (Mesh mesh, Material[] materials)> Cache = new Dictionary<string, (Mesh, Material[])>();

        readonly List<Color> _colors = new List<Color>();
        readonly List<List<(Vector3 center, Vector3 size, Quaternion rot)>> _boxes = new List<List<(Vector3, Vector3, Quaternion)>>();
        readonly List<List<(Vector3 center, Vector3 du, Vector3 dv, Vector3 normal)>> _quads = new List<List<(Vector3, Vector3, Vector3, Vector3)>>();

        public BoxModel Box(Vector3 center, Vector3 size, Color color) => Box(center, size, color, Quaternion.identity);

        public BoxModel Box(Vector3 center, Vector3 size, Color color, Quaternion rotation)
        {
            _boxes[ColorIndex(color)].Add((center, size, rotation));
            return this;
        }

        /// <summary>
        /// Параллелепипед, поверхность которого разбита на плитки размером ~tile; цвет каждой плитки
        /// задаёт colorAt(центр плитки) — так получается «пиксельный» камуфляж.
        /// </summary>
        public BoxModel PatternBox(Vector3 center, Vector3 size, float tile, System.Func<Vector3, Color> colorAt)
        {
            Vector3 h = size * 0.5f;
            foreach (var n in FaceNormals)
            {
                Vector3 u = n.y != 0 ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(u, n);
                float lenU = Mathf.Abs(Vector3.Dot(size, u)), lenV = Mathf.Abs(Vector3.Dot(size, v));
                int nu = Mathf.Max(1, Mathf.RoundToInt(lenU / tile)), nv = Mathf.Max(1, Mathf.RoundToInt(lenV / tile));
                float su = lenU / nu, sv = lenV / nv;
                Vector3 faceCenter = center + Vector3.Scale(n, h);
                for (int i = 0; i < nu; i++)
                for (int j = 0; j < nv; j++)
                {
                    Vector3 p = faceCenter + u * (-lenU * 0.5f + (i + 0.5f) * su) + v * (-lenV * 0.5f + (j + 0.5f) * sv);
                    _quads[ColorIndex(colorAt(p))].Add((p, u * (su * 0.5f), v * (sv * 0.5f), n));
                }
            }
            return this;
        }

        int ColorIndex(Color color)
        {
            int i = _colors.IndexOf(color);
            if (i >= 0)
                return i;
            _colors.Add(color);
            _boxes.Add(new List<(Vector3, Vector3, Quaternion)>());
            _quads.Add(new List<(Vector3, Vector3, Vector3, Vector3)>());
            return _colors.Count - 1;
        }

        public static bool TryGetCached(string key, out (Mesh mesh, Material[] materials) model) => Cache.TryGetValue(key, out model);

        public (Mesh mesh, Material[] materials) Build(string cacheKey)
        {
            if (cacheKey != null && Cache.TryGetValue(cacheKey, out var cached))
                return cached;

            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var mesh = new Mesh { name = cacheKey ?? "BoxModel" };
            var materials = new Material[_colors.Count];
            var submeshTris = new List<int>[_colors.Count];

            for (int c = 0; c < _colors.Count; c++)
            {
                submeshTris[c] = new List<int>();
                materials[c] = RuntimeMaterials.Lit(_colors[c]);
                foreach (var (center, size, rot) in _boxes[c])
                    AddBox(verts, normals, submeshTris[c], center, size, rot);
                foreach (var (center, du, dv, n) in _quads[c])
                    AddQuad(verts, normals, submeshTris[c], center, du, dv, n);
            }

            mesh.indexFormat = verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.subMeshCount = _colors.Count;
            for (int c = 0; c < _colors.Count; c++)
                mesh.SetTriangles(submeshTris[c], c);
            mesh.RecalculateBounds();

            var model = (mesh, materials);
            if (cacheKey != null)
                Cache[cacheKey] = model;
            return model;
        }

        /// <summary>Создать дочерний объект с этой моделью.</summary>
        public GameObject Spawn(string cacheKey, Transform parent, Vector3 localPos = default, string name = "Model")
        {
            var (mesh, materials) = Build(cacheKey);
            return Spawn(mesh, materials, parent, localPos, name);
        }

        public static GameObject Spawn(Mesh mesh, Material[] materials, Transform parent, Vector3 localPos, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            return go;
        }

        static readonly Vector3[] FaceNormals =
            { Vector3.up, Vector3.down, Vector3.right, Vector3.left, Vector3.forward, Vector3.back };

        static void AddQuad(List<Vector3> verts, List<Vector3> normals, List<int> tris, Vector3 center, Vector3 du, Vector3 dv, Vector3 n)
        {
            int start = verts.Count;
            verts.Add(center - du - dv);
            verts.Add(center - du + dv);
            verts.Add(center + du + dv);
            verts.Add(center + du - dv);
            for (int i = 0; i < 4; i++)
                normals.Add(n);
            tris.Add(start);
            tris.Add(start + 1);
            tris.Add(start + 2);
            tris.Add(start);
            tris.Add(start + 2);
            tris.Add(start + 3);
        }

        static void AddBox(List<Vector3> verts, List<Vector3> normals, List<int> tris, Vector3 center, Vector3 size, Quaternion rot)
        {
            Vector3 h = size * 0.5f;
            foreach (var n in FaceNormals)
            {
                // два касательных вектора грани; v = u × n даёт обход по часовой стрелке снаружи (лицевая сторона в Unity)
                Vector3 u = n.y != 0 ? Vector3.right : Vector3.up;
                Vector3 v = Vector3.Cross(u, n);
                Vector3 c = Vector3.Scale(n, h);
                Vector3 du = Vector3.Scale(u, h), dv = Vector3.Scale(v, h);

                int start = verts.Count;
                verts.Add(center + rot * (c - du - dv));
                verts.Add(center + rot * (c - du + dv));
                verts.Add(center + rot * (c + du + dv));
                verts.Add(center + rot * (c + du - dv));
                for (int i = 0; i < 4; i++)
                    normals.Add(rot * n);

                tris.Add(start);
                tris.Add(start + 1);
                tris.Add(start + 2);
                tris.Add(start);
                tris.Add(start + 2);
                tris.Add(start + 3);
            }
        }
    }
}
