using UnityEngine;

namespace RocketWars.Terrain
{
    /// <summary>
    /// Процедурный атлас 160×8: 20 тайлов по 8 px. Даёт «пиксельный» воксельный вид без внешних текстур.
    /// Тайлы 0–7 — базовые (верх в первом оттенке, земля, камень), 8–19 — верх в трёх других оттенках
    /// (на каждый оттенок: 3 варианта верха + бок). Раскладка общая для всех биомов (Biome), у каждого — свои
    /// цвета и узоры: трава/земля/камень, песок/плотный песок/песчаник, снег/мёрзлая земля/холодный камень.
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

        /// <summary>Цвета одного биома. «Верх» — трава/песок/снег, «под ним» — земля/плотный песок/мёрзлая земля.</summary>
        sealed class BiomeColors
        {
            public Color[] TopLight, TopDark; // по оттенку (GrassPalettes штук)
            public Color SubA, SubB;
            public Color StoneLight, StoneMid, StoneDark;
        }

        static readonly BiomeColors[] Colors =
        {
            // трава: 0 — зелёный, дальше теплее
            new BiomeColors
            {
                TopLight = new Color[] { new Color32(118, 186, 72, 255), new Color32(130, 188, 68, 255), new Color32(142, 188, 64, 255), new Color32(154, 188, 60, 255) },
                TopDark = new Color[] { new Color32(98, 166, 58, 255), new Color32(110, 168, 54, 255), new Color32(122, 168, 50, 255), new Color32(134, 168, 46, 255) },
                SubA = new Color32(121, 85, 58, 255), SubB = new Color32(99, 68, 46, 255),
                StoneLight = new Color32(146, 146, 142, 255), StoneMid = new Color32(122, 122, 119, 255), StoneDark = new Color32(92, 92, 90, 255),
            },
            // пустыня: песок от бледного к рыжему, под ним плотный песок, внизу песчаник
            new BiomeColors
            {
                TopLight = new Color[] { new Color32(236, 214, 150, 255), new Color32(238, 208, 140, 255), new Color32(238, 202, 130, 255), new Color32(236, 194, 120, 255) },
                TopDark = new Color[] { new Color32(218, 192, 128, 255), new Color32(220, 186, 118, 255), new Color32(220, 180, 108, 255), new Color32(216, 170, 98, 255) },
                SubA = new Color32(206, 170, 108, 255), SubB = new Color32(184, 148, 90, 255),
                StoneLight = new Color32(214, 160, 104, 255), StoneMid = new Color32(192, 138, 86, 255), StoneDark = new Color32(164, 112, 70, 255),
            },
            // снег: от белого к голубоватому, под ним мёрзлая земля, внизу холодный серо-голубой камень
            new BiomeColors
            {
                TopLight = new Color[] { new Color32(246, 248, 250, 255), new Color32(242, 246, 252, 255), new Color32(238, 244, 252, 255), new Color32(234, 242, 252, 255) },
                TopDark = new Color[] { new Color32(226, 232, 240, 255), new Color32(220, 228, 240, 255), new Color32(214, 224, 240, 255), new Color32(208, 220, 240, 255) },
                SubA = new Color32(104, 82, 68, 255), SubB = new Color32(84, 66, 56, 255),
                StoneLight = new Color32(150, 156, 166, 255), StoneMid = new Color32(124, 130, 140, 255), StoneDark = new Color32(94, 100, 110, 255),
            },
        };

        static readonly Material[] Materials = new Material[Biomes.Count];

        /// <summary>Материал атласа биома (строится один раз и кэшируется).</summary>
        public static Material GetMaterial(Biome biome)
        {
            int index = (int)biome;
            if (Materials[index] != null)
                return Materials[index];

            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = $"VoxelAtlas {biome} (runtime)" };
            material.SetTexture("_BaseMap", BuildTexture(biome));
            material.SetColor("_BaseColor", Color.white);
            // снег слегка блестит, песок и трава матовые
            material.SetFloat("_Smoothness", biome == Biome.Snow ? 0.2f : 0.05f);
            Materials[index] = material;
            return material;
        }

        /// <summary>UV-прямоугольник тайла с небольшим отступом от краёв, чтобы не цеплять соседей.</summary>
        public static Rect TileRect(int tile)
        {
            const float inset = 0.02f;
            float w = TileSize * TileCount;
            return new Rect((tile * TileSize + inset) / w, inset / TileSize, (TileSize - 2 * inset) / w, 1f - 2 * inset / TileSize);
        }

        static Texture2D BuildTexture(Biome biome)
        {
            int w = TileSize * TileCount;
            var tex = new Texture2D(w, TileSize, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
                name = $"VoxelAtlas {biome}"
            };

            var colors = Colors[(int)biome];
            var rng = new System.Random(1234);
            var pixels = new Color[w * TileSize];
            for (int tile = 0; tile < TileCount; tile++)
            for (int px = 0; px < TileSize; px++)
            for (int py = 0; py < TileSize; py++)
            {
                float n = (float)rng.NextDouble();
                Color c;
                // остальные оттенки повторяют раскладку базовых: 3 верха + бок
                int palette = tile >= WarmGrass0 ? 1 + (tile - WarmGrass0) / (GrassTopVariants + 1) : 0;
                int grassSlot = tile >= WarmGrass0 ? (tile - WarmGrass0) % (GrassTopVariants + 1) : tile;
                Color topA = colors.TopLight[palette], topB = colors.TopDark[palette];
                if (grassSlot < GrassTopVariants && (tile >= WarmGrass0 || tile < GrassTopVariants))
                {
                    c = TopPixel(biome, topA, topB, px, py, grassSlot, n);
                }
                else if (tile == GrassSide || tile >= WarmGrass0)
                {
                    // верхние 2–3 ряда — трава/песок/снег с рваной кромкой, остальное — то, что под ними
                    int topRows = 2 + (rng.Next(3) == 0 ? 1 : 0);
                    if (biome == Biome.Snow && (px * 5 + tile) % 3 == 0)
                        topRows++; // снег свисает неровной шапкой
                    c = py >= TileSize - topRows ? Color.Lerp(topA, topB, n * 0.7f) : SubPixel(biome, colors, py, n * 0.6f);
                }
                else if (tile >= Stone0)
                {
                    c = StonePixel(biome, colors, tile, px, py, n);
                }
                else
                {
                    c = SubPixel(biome, colors, py, n * 0.6f + (tile - Dirt0) * 0.25f);
                }

                pixels[py * w + tile * TileSize + px] = c;
            }

            tex.SetPixels(pixels);
            tex.Apply(false, true);
            return tex;
        }

        /// <summary>Пиксель верхней грани: трава — шум, песок — косая рябь, снег — шум с белыми искорками.</summary>
        static Color TopPixel(Biome biome, Color a, Color b, int px, int py, int slot, float n)
        {
            switch (biome)
            {
                case Biome.Desert:
                {
                    var c = Color.Lerp(a, b, n * 0.45f + slot * 0.1f);
                    // гребни ряби: полоса сдвигается на пиксель каждые 3 столбца, у вариантов — со смещением
                    bool ripple = (py + (px + slot * 2) / 3) % 4 == 0;
                    return ripple ? Color.Lerp(c, b * 0.92f, 0.75f) : c;
                }
                case Biome.Snow:
                {
                    var c = Color.Lerp(a, b, n * 0.5f + slot * 0.12f);
                    return n > 0.96f ? Color.white : c;
                }
                default:
                    return Color.Lerp(a, b, n * 0.6f + slot * 0.15f);
            }
        }

        /// <summary>То, что под верхом. В пустыне плотный песок темнеет к низу тайла — будто слежался.</summary>
        static Color SubPixel(Biome biome, BiomeColors colors, int py, float t)
        {
            if (biome == Biome.Desert)
                t += (TileSize - 1 - py) * 0.04f;
            return Color.Lerp(colors.SubA, colors.SubB, t);
        }

        static Color StonePixel(Biome biome, BiomeColors colors, int tile, int px, int py, float n)
        {
            if (biome == Biome.Desert)
            {
                // песчаник: горизонтальные пласты с лёгким изгибом
                int wave = Mathf.FloorToInt(Mathf.PerlinNoise(px * 0.3f + tile * 3.1f, tile * 1.7f) * 2.5f);
                int band = ((py + wave) / 2 + tile) % 3;
                var c = band == 0 ? colors.StoneLight : band == 1 ? colors.StoneMid : colors.StoneDark;
                c = Color.Lerp(c, colors.StoneMid, n * 0.3f);
                return n > 0.94f ? colors.StoneDark : c;
            }

            // необработанная порода: неровные пятна (шум низкой частоты) + тёмные вкрапления и светлые сколы
            float blob = Mathf.PerlinNoise(px * 0.45f + tile * 5.3f, py * 0.45f + tile * 2.1f);
            var rock = Color.Lerp(colors.StoneMid, blob > 0.5f ? colors.StoneLight : colors.StoneDark, Mathf.Abs(blob - 0.5f) * 1.6f);
            rock = Color.Lerp(rock, colors.StoneMid, n * 0.25f);
            if (n > 0.9f) rock = colors.StoneDark;
            else if (n < 0.06f) rock = colors.StoneLight;
            return rock;
        }
    }
}
