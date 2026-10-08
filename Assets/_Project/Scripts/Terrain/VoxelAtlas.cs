using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>
    /// Процедурный атлас 160×8: 20 тайлов по 8 px. Даёт «пиксельный» воксельный вид без внешних текстур.
    /// Тайлы 0–7 — базовые (трава в зелёном оттенке, земля, камень), 8–19 — трава в трёх более тёплых оттенках
    /// (на каждый оттенок: 3 варианта верха + бок).
    /// </summary>
    public static class VoxelAtlas
    {
        public const int TileSize = 8;
        public const int TileCount = 20;

        public const int GrassTop0 = 0; // 0..2 — оттенки травы сверху
        public const int GrassSide = 3;
        public const int Dirt0 = 4;     // 4..5 — земля
        public const int Stone0 = 6;    // 6..7 — камень нижнего слоя (необработанная порода)
        public const int GrassTopVariants = 3;
        public const int DirtVariants = 2;
        public const int StoneVariants = 2;
        /// <summary>Оттенков травы на кубиках — столько же, сколько у пучков (GrassTufts.Palettes).</summary>
        public const int GrassPalettes = 4;
        const int WarmGrass0 = 8;

        /// <summary>Тайл верха травы: palette 0 — зелёный … GrassPalettes−1 — тёпло-зелёный.</summary>
        public static int GrassTop(int palette, int variant) =>
            palette <= 0 ? GrassTop0 + variant : WarmGrass0 + (palette - 1) * (GrassTopVariants + 1) + variant;

        /// <summary>Тайл боковой грани травяного кубика в нужном оттенке.</summary>
        public static int GrassSideTile(int palette) =>
            palette <= 0 ? GrassSide : WarmGrass0 + (palette - 1) * (GrassTopVariants + 1) + GrassTopVariants;

        // светлый/тёмный тон травы для каждого оттенка (0 — как было, дальше теплее)
        static readonly Color[] GrassLight =
        {
            new Color32(118, 186, 72, 255), new Color32(130, 188, 68, 255), new Color32(142, 188, 64, 255), new Color32(154, 188, 60, 255)
        };
        static readonly Color[] GrassDark =
        {
            new Color32(98, 166, 58, 255), new Color32(110, 168, 54, 255), new Color32(122, 168, 50, 255), new Color32(134, 168, 46, 255)
        };

        static readonly Color DirtA = new Color32(121, 85, 58, 255);
        static readonly Color DirtB = new Color32(99, 68, 46, 255);
        static readonly Color StoneLight = new Color32(146, 146, 142, 255);
        static readonly Color StoneMid = new Color32(122, 122, 119, 255);
        static readonly Color StoneDark = new Color32(92, 92, 90, 255);

        static Material _material;

        public static Material GetMaterial()
        {
            if (_material != null)
                return _material;

            var tex = BuildTexture();
            _material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "VoxelAtlas (runtime)" };
            _material.SetTexture("_BaseMap", tex);
            _material.SetColor("_BaseColor", Color.white);
            _material.SetFloat("_Smoothness", 0.05f);
            return _material;
        }

        /// <summary>UV-прямоугольник тайла с небольшим отступом от краёв, чтобы не цеплять соседей.</summary>
        public static Rect TileRect(int tile)
        {
            const float inset = 0.02f;
            float w = TileSize * TileCount;
            return new Rect((tile * TileSize + inset) / w, inset / TileSize, (TileSize - 2 * inset) / w, 1f - 2 * inset / TileSize);
        }

        static Texture2D BuildTexture()
        {
            int w = TileSize * TileCount;
            var tex = new Texture2D(w, TileSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = "VoxelAtlas"
            };

            var rng = new System.Random(1234);
            var pixels = new Color[w * TileSize];
            for (int tile = 0; tile < TileCount; tile++)
            for (int px = 0; px < TileSize; px++)
            for (int py = 0; py < TileSize; py++)
            {
                float n = (float)rng.NextDouble();
                Color c;
                // тёплые оттенки травы повторяют раскладку базовых: 3 верха + бок
                int palette = tile >= WarmGrass0 ? 1 + (tile - WarmGrass0) / (GrassTopVariants + 1) : 0;
                int grassSlot = tile >= WarmGrass0 ? (tile - WarmGrass0) % (GrassTopVariants + 1) : tile;
                Color grassA = GrassLight[palette], grassB = GrassDark[palette];
                if (grassSlot < GrassTopVariants && (tile >= WarmGrass0 || tile < GrassTopVariants))
                {
                    c = Color.Lerp(grassA, grassB, n * 0.6f + grassSlot * 0.15f);
                }
                else if (tile == GrassSide || tile >= WarmGrass0)
                {
                    // верхние 2–3 ряда — трава с рваной кромкой, остальное — земля
                    int grassRows = 2 + (rng.Next(3) == 0 ? 1 : 0);
                    c = py >= TileSize - grassRows ? Color.Lerp(grassA, grassB, n * 0.7f) : Color.Lerp(DirtA, DirtB, n * 0.6f);
                }
                else if (tile >= Stone0)
                {
                    // необработанная порода: неровные пятна (шум низкой частоты) + тёмные вкрапления и светлые сколы
                    float blob = Mathf.PerlinNoise(px * 0.45f + tile * 5.3f, py * 0.45f + tile * 2.1f);
                    c = Color.Lerp(StoneMid, blob > 0.5f ? StoneLight : StoneDark, Mathf.Abs(blob - 0.5f) * 1.6f);
                    c = Color.Lerp(c, StoneMid, n * 0.25f);
                    if (n > 0.9f) c = StoneDark;
                    else if (n < 0.06f) c = StoneLight;
                }
                else
                {
                    c = Color.Lerp(DirtA, DirtB, n * 0.6f + (tile - Dirt0) * 0.25f);
                }

                pixels[py * w + tile * TileSize + px] = c;
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }
    }
}
