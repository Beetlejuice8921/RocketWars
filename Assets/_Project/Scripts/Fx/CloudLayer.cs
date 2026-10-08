using UnityEngine;

namespace RocketWars.Fx
{
    /// <summary>
    /// Облака на небе: плоские спрайты из атласа Textures/Clouds.png (материал Resources/Cloud — URP Unlit, прозрачный),
    /// раскиданы высоко над островами и вокруг них, всегда повёрнуты к камере и медленно плывут по ветру.
    /// Чисто визуальные и локальные: у каждого игрока свои (одинаковые по seed) облака.
    /// </summary>
    public class CloudLayer : MonoBehaviour
    {
        const int Count = 28;
        /// <summary>Облака живут в квадрате ±Extent вокруг центра карты; уплывшее за край возвращается с другой стороны.</summary>
        const float Extent = 450f;
        const float MinHeight = 75f, MaxHeight = 140f;
        const float MinWidth = 45f, MaxWidth = 110f;
        static readonly Vector3 Wind = new Vector3(1.6f, 0f, 0.5f);

        // прямоугольники облаков в атласе 752×664 (px, начало — левый верхний угол картинки)
        static readonly RectInt[] Sprites =
        {
            new RectInt(31, 32, 304, 168),
            new RectInt(376, 32, 345, 99),
            new RectInt(390, 181, 331, 120),
            new RectInt(44, 273, 305, 168),
            new RectInt(390, 369, 79, 51),
            new RectInt(492, 341, 216, 100),
            new RectInt(38, 461, 659, 169),
        };

        static CloudLayer _instance;
        Transform[] _clouds;

        /// <summary>Создать облака один раз (вызывается при настройке камеры — и в меню, и в игре).</summary>
        public static void Ensure()
        {
            if (_instance != null)
                return;
            var material = Resources.Load<Material>("Cloud");
            if (material == null || material.mainTexture == null)
            {
                Debug.LogWarning("[RocketWars] Cloud material or texture missing — no clouds");
                return;
            }
            _instance = new GameObject("Clouds").AddComponent<CloudLayer>();
            DontDestroyOnLoad(_instance.gameObject);
            _instance.Build(material);
        }

        void Build(Material material)
        {
            var tex = material.mainTexture;
            var meshes = new Mesh[Sprites.Length];
            for (int i = 0; i < Sprites.Length; i++)
                meshes[i] = BuildQuad(Sprites[i], tex.width, tex.height);

            var rng = new System.Random(2024);
            _clouds = new Transform[Count];
            for (int i = 0; i < Count; i++)
            {
                int sprite = rng.Next(Sprites.Length);
                var go = new GameObject($"Cloud_{i}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[sprite];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = material;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;

                // маленький обрывок — и в мире поменьше
                float width = Mathf.Lerp(MinWidth, MaxWidth, (float)rng.NextDouble()) * (Sprites[sprite].width < 120 ? 0.4f : 1f);
                go.transform.localScale = Vector3.one * width;
                go.transform.position = new Vector3(
                    Mathf.Lerp(-Extent, Extent, (float)rng.NextDouble()),
                    Mathf.Lerp(MinHeight, MaxHeight, (float)rng.NextDouble()),
                    Mathf.Lerp(-Extent, Extent, (float)rng.NextDouble()));
                _clouds[i] = go.transform;
            }
        }

        /// <summary>Квад шириной 1 (высота по пропорциям облака), pivot в центре, UV — прямоугольник облака в атласе.</summary>
        static Mesh BuildQuad(RectInt px, int texW, int texH)
        {
            float aspect = px.height / (float)px.width;
            float u0 = px.x / (float)texW, u1 = (px.x + px.width) / (float)texW;
            // в PNG y идёт сверху вниз, в UV — снизу вверх
            float v1 = 1f - px.y / (float)texH, v0 = 1f - (px.y + px.height) / (float)texH;
            var mesh = new Mesh { name = "CloudQuad" };
            mesh.SetVertices(new[]
            {
                new Vector3(-0.5f, -0.5f * aspect, 0f), new Vector3(-0.5f, 0.5f * aspect, 0f),
                new Vector3(0.5f, 0.5f * aspect, 0f), new Vector3(0.5f, -0.5f * aspect, 0f)
            });
            mesh.SetUVs(0, new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) });
            mesh.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || _clouds == null)
                return;
            Vector3 camPos = cam.transform.position;
            Vector3 drift = Wind * Time.deltaTime;
            foreach (var cloud in _clouds)
            {
                var p = cloud.position + drift;
                if (p.x > Extent) p.x -= 2f * Extent;
                if (p.z > Extent) p.z -= 2f * Extent;
                cloud.position = p;
                // лицом к камере: квад смотрит +Z «от зрителя», поэтому направление — от камеры к облаку
                cloud.rotation = Quaternion.LookRotation(p - camPos);
            }
        }
    }
}
