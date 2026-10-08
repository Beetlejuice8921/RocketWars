using UnityEngine;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace RocketWars.Core
{
    /// <summary>
    /// Пиксельный шрифт игры — Press Start 2P (SIL OFL 1.1, Resources/Fonts, полная кириллица).
    /// Начертание у шрифта одно; если файла нет — встроенный LegacyRuntime.
    /// </summary>
    public static class GameFonts
    {
        static Font _font;
        static FontAsset _uiAsset;

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

        /// <summary>
        /// Шрифт для UI Toolkit. Жирное начертание (-unity-font-style: bold) — тот же шрифт:
        /// он и так «толстый», а синтетическое утолщение размывает пиксели.
        /// </summary>
        public static FontDefinition Ui
        {
            get
            {
                if (_uiAsset == null)
                {
                    _uiAsset = FontAsset.CreateFontAsset(Pixel);
                    if (_uiAsset != null)
                        _uiAsset.fontWeightTable[7].regularTypeface = _uiAsset;
                }
                return _uiAsset != null ? FontDefinition.FromSDFFont(_uiAsset) : FontDefinition.FromFont(Pixel);
            }
        }
    }
}
