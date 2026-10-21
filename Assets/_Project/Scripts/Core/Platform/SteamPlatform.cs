using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// ПК-сборка (будущая Steam-версия). Пока повторяет прежнее поведение игры; Steamworks не подключён.
    /// </summary>
    public class SteamPlatform : GamePlatform
    {
        public override string Id => "steam";

#if UNITY_STANDALONE
        // размер и режим окна Unity сам запоминает между запусками
        public override bool CanToggleWindowed => true;

        public override bool IsWindowed => Screen.fullScreenMode == FullScreenMode.Windowed; // могли переключить Alt+Enter

        /// <summary>Окно — 3/4 экрана (в окне на всю ширину заголовок ушёл бы за край), полный экран — родное разрешение.</summary>
        public override void SetWindowed(bool windowed)
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (windowed)
                Screen.SetResolution(w * 3 / 4, h * 3 / 4, FullScreenMode.Windowed);
            else
                Screen.SetResolution(w, h, FullScreenMode.FullScreenWindow);
        }
#endif

        /// <summary>Язык интерфейса Windows → язык игры; неподдерживаемый — английский.</summary>
        public override Lang DetectLanguage()
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            try
            {
                // Application.systemLanguage не знает хинди и не различает варианты португальского — берём LANGID
                int primary = GetUserDefaultUILanguage() & 0x3FF;
                switch (primary)
                {
                    case 0x09: return Lang.En;
                    case 0x19: return Lang.Ru;
                    case 0x04: return Lang.ZhHans; // традиционный китайский тоже сюда — ближе, чем английский
                    case 0x0A: return Lang.Es;
                    case 0x16: return Lang.PtBr;
                    case 0x07: return Lang.De;
                    case 0x0C: return Lang.Fr;
                    case 0x11: return Lang.Ja;
                    case 0x12: return Lang.Ko;
                    case 0x1F: return Lang.Tr;
                    case 0x15: return Lang.Pl;
                    case 0x39: return Lang.Hi;
                    case 0x22: return Lang.Uk;
                    case 0x10: return Lang.It;
                    case 0x21: return Lang.Id;
                    case 0x23: return Lang.Be;
                    case 0x3F: return Lang.Kk;
                    case 0x43: return Lang.Uz;
                    default: return Lang.En;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[RocketWars] system language detection failed: {e.Message}");
            }
#endif
            return base.DetectLanguage();
        }

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [DllImport("kernel32.dll")]
        static extern ushort GetUserDefaultUILanguage();
#endif
    }
}
