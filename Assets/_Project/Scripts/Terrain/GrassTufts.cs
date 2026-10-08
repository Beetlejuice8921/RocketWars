using System.Collections.Generic;
using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>
    /// Пучки травы «как в Minecraft»: два скрещённых квада с пиксельной текстурой 16×16 (alpha cutout, обе стороны).
    /// Материал — ассет Resources/GrassTuft (URP Lit с Alpha Clipping и Cull Off: так нужный вариант шейдера точно
    /// попадает в сборку), текстура генерируется кодом. Нормали смотрят вверх — пучок освещён как верх кубика.
    /// </summary>
    public static class GrassTufts
    {
        const int Tile = 16;
        const int Variants = 4;
        /// <summary>Оттенков травы: 0 — зелёный … 3 — тёпло-зелёный (с желтизной).</summary>
        public const int Palettes = 4;
        /// <summary>Доля верхних травяных кубиков с пучком, %.</summary>
        public const int Density = 40;

        // основание, два средних тона травинок, кончики — для каждого оттенка
        static readonly Color32[][] PaletteColors =
        {
            new[] { new Color32(64, 122, 40, 255), new Color32(98, 166, 58, 255), new Color32(112, 180, 66, 255), new Color32(150, 208, 92, 255) },
            new[] { new Color32(74, 124, 38, 255), new Color32(114, 170, 54, 255), new Color32(126, 182, 60, 255), new Color32(164, 208, 88, 255) },
            new[] { new Color32(82, 124, 36, 255), new Color32(124, 172, 52, 255), new Color32(136, 182, 58, 255), new Color32(172, 208, 86, 255) },
            new[] { new Color32(90, 124, 34, 255), new Color32(136, 172, 50, 255), new Color32(148, 182, 56, 255), new Color32(184, 206, 86, 255) },
        };

        static Material _material;

        public static Material GetMaterial()
        {
            if (_material != null)
                return _material;
            var asset = Resources.Load<Material>("GrassTuft");
            _material = asset != null ? new Material(asset) : FallbackMaterial();
            _material.name = "GrassTuft (runtime)";
            _material.SetTexture("_BaseMap", BuildTexture());
            return _material;
        }

        /// <summary>На случай отсутствия ассета: тот же URP Lit, настроенный кодом (в редакторе работает всегда).</summary>
        static Material FallbackMaterial()
        {
            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetFloat("_AlphaClip", 1f);
            mat.SetFloat("_Cutoff", 0.5f);
            mat.SetFloat("_Cull", 0f);
            mat.SetFloat("_Smoothness", 0f);
            mat.EnableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            return mat;
        }

        /// <summary>
        /// Атлас 256×16: четыре формы пучка (5–7 травинок разной высоты с лёгким наклоном) × четыре оттенка.
        /// Тайл = форма + оттенок × Variants; формы во всех оттенках одинаковые.
        /// </summary>
        static Texture2D BuildTexture()
        {
            var tex = new Texture2D(Tile * Variants * Palettes, Tile, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "GrassTufts"
            };
            var pixels = new Color32[tex.width * tex.height];
            var rng = new System.Random(1717);

            for (int v = 0; v < Variants; v++)
            {
                int blades = 5 + rng.Next(3);
                for (int b = 0; b < blades; b++)
                {
                    int x = 1 + rng.Next(Tile - 2);
                    int height = 6 + rng.Next(Tile - 6);
                    int lean = rng.Next(3) - 1;               // −1 влево, 0 прямо, +1 вправо
                    int leanEvery = 3 + rng.Next(3);
                    int midIndex = 1 + rng.Next(2);
                    for (int y = 0; y < height; y++)
                    {
                        int px = x + (y / leanEvery) * lean;
                        if (px < 0 || px >= Tile)
                            break;
                        float k = y / (float)(height - 1);
                        int shade = k < 0.3f ? 0 : k > 0.8f ? 3 : midIndex;
                        for (int p = 0; p < Palettes; p++)
                            pixels[y * tex.width + (p * Variants + v) * Tile + px] = PaletteColors[p][shade];
                    }
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>
        /// Пучок в точке bottom (низ — на верхней грани кубика). hash задаёт форму, размер и зеркалирование,
        /// palette — оттенок (0 — зелёный … Palettes−1 — тёпло-зелёный).
        /// </summary>
        public static void Add(List<Vector3> verts, List<Vector3> normals, List<Vector2> uvs, List<int> tris, Vector3 bottom, int hash, int palette)
        {
            int variant = Mathf.Clamp(palette, 0, Palettes - 1) * Variants + hash % Variants;
            const float tiles = Variants * Palettes;
            float height = 0.5f + (hash / 7 % 5) * 0.06f;        // 0.5–0.74 м
            float half = 0.4f;
            bool flip = (hash / 41 & 1) == 1;
            float u0 = (variant + (flip ? 1f : 0f)) / tiles, u1 = (variant + (flip ? 0f : 1f)) / tiles;

            // два квада крест-накрест по диагоналям клетки
            for (int q = 0; q < 2; q++)
            {
                Vector3 axis = q == 0 ? new Vector3(0.7071f, 0f, 0.7071f) : new Vector3(0.7071f, 0f, -0.7071f);
                int start = verts.Count;
                verts.Add(bottom - axis * half);
                verts.Add(bottom - axis * half + Vector3.up * height);
                verts.Add(bottom + axis * half + Vector3.up * height);
                verts.Add(bottom + axis * half);
                for (int i = 0; i < 4; i++)
                    normals.Add(Vector3.up);
                uvs.Add(new Vector2(u0, 0f));
                uvs.Add(new Vector2(u0, 1f));
                uvs.Add(new Vector2(u1, 1f));
                uvs.Add(new Vector2(u1, 0f));
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
