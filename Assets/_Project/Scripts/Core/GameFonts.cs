using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RocketWars.Core
{
    /// <summary>
    /// Пиксельные шрифты игры (Resources/Fonts, SIL OFL 1.1):
    /// Press Start 2P — латиница и кириллица; Fusion Pixel 12px — китайский, японский, корейский (у каждого свой вариант иероглифов).
    /// Деванагари (хинди) пиксельного нет — подставляется системный Nirmala UI.
    /// Системные шрифты Windows стоят запасными, чтобы имена игроков на любом языке не превращались в квадраты.
    /// </summary>
    public static class GameFonts
    {
        static Font _font;
        static readonly Dictionary<string, Font> CjkFonts = new Dictionary<string, Font>();
        static readonly Dictionary<string, FontAsset> UiAssets = new Dictionary<string, FontAsset>();
        static List<FontAsset> _osFallbacks;

        /// <summary>Шрифты Windows для символов, которых нет в пиксельных (хинди — первым).</summary>
        static readonly string[] OsFallbackFamilies = { "Nirmala UI", "Microsoft YaHei", "Yu Gothic UI", "Malgun Gothic" };

        /// <summary>Press Start 2P (если файла нет — встроенный LegacyRuntime).</summary>
        public static Font Pixel
        {
            get
            {
                if (_font == null)
                    _font = Resources.Load<Font>("Fonts/PressStart2P-Regular");
                if (_font == null)
                    _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                return _font;
            }
        }

        /// <summary>Шрифт для uGUI-текста (пульт) на текущем языке.</summary>
        public static Font Console => CjkFile(Loc.Current) is string file ? LoadCjk(file) ?? Pixel : Pixel;

        /// <summary>Шрифт UI Toolkit для текущего языка.</summary>
        public static FontDefinition Ui => UiFor(Loc.Current);

        /// <summary>
        /// Шрифт UI Toolkit для языка (в меню выбора каждое название — своим шрифтом).
        /// Жирное начертание (-unity-font-style: bold) — тот же шрифт: он и так «толстый», а синтетическое утолщение размывает пиксели.
        /// </summary>
        public static FontDefinition UiFor(Lang lang)
        {
            string file = CjkFile(lang);
            string key = lang == Lang.Hi ? HindiFamily : file ?? "PressStart2P";
            if (!UiAssets.TryGetValue(key, out var asset) || asset == null)
            {
                if (lang == Lang.Hi)
                {
                    // хинди целиком системным шрифтом: с Press Start основным пробелы между словами выходят огромными.
                    // Тот же экземпляр, что в запасных: второй ассет с тем же именем TextCore считает циклом
                    asset = OsFallbacks().Find(f => f.faceInfo.familyName == HindiFamily);
                    Scale(asset, HindiScale);
                }
                else
                {
                    asset = FontAsset.CreateFontAsset(file != null ? LoadCjk(file) ?? Pixel : Pixel);
                    if (file != null)
                        Scale(asset, CjkScale);
                }
                if (asset != null)
                {
                    asset.fontWeightTable[7].regularTypeface = asset;
                    asset.fallbackFontAssetTable = OsFallbacks().FindAll(f => f.faceInfo.familyName != asset.faceInfo.familyName);
                }
                UiAssets[key] = asset;
            }
            return asset != null ? FontDefinition.FromSDFFont(asset) : FontDefinition.FromFont(Pixel);
        }

        /// <summary>
        /// Размеры в USS подобраны под Press Start 2P, у которого буква занимает весь кегль;
        /// иероглифы Fusion Pixel и деванагари Nirmala при том же кегле заметно мельче — увеличиваем.
        /// </summary>
        const float CjkScale = 1.5f;
        const float HindiScale = 1.3f;
        const string HindiFamily = "Nirmala UI";

        static void Scale(FontAsset asset, float scale)
        {
            if (asset == null)
                return;
            var face = asset.faceInfo;
            face.scale = scale;
            asset.faceInfo = face;
        }

        /// <summary>
        /// Генератор текста для языка: хинди нужен «продвинутый» (HarfBuzz) — без него буквы деванагари не склеиваются в слоги.
        /// </summary>
        public static TextGeneratorType GeneratorFor(Lang lang) => lang == Lang.Hi ? TextGeneratorType.Advanced : TextGeneratorType.Standard;

        static string CjkFile(Lang lang) => lang switch
        {
            Lang.ZhHans => "fusion-pixel-12px-proportional-zh_hans",
            Lang.Ja => "fusion-pixel-12px-proportional-ja",
            Lang.Ko => "fusion-pixel-12px-proportional-ko",
            _ => null,
        };

        static Font LoadCjk(string file)
        {
            if (!CjkFonts.TryGetValue(file, out var font) || font == null)
                CjkFonts[file] = font = Resources.Load<Font>("Fonts/" + file);
            return font;
        }

        static List<FontAsset> OsFallbacks()
        {
            // созданные в рантайме ассеты уничтожаются при выходе из Play (статика в редакторе может пережить это)
            if (_osFallbacks != null && _osFallbacks.TrueForAll(f => f != null))
                return _osFallbacks;
            _osFallbacks = new List<FontAsset>();
            foreach (var family in OsFallbackFamilies)
                if (CreateOsFont(family) is FontAsset fa)
                    _osFallbacks.Add(fa);
            return _osFallbacks;
        }

        static FontAsset CreateOsFont(string family)
        {
            try { return FontAsset.CreateFontAsset(family, "Regular"); }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[RocketWars] OS font {family}: {e.Message}");
                return null;
            }
        }
    }
}
