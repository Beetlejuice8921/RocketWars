using System;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Слой площадки (Steam / Яндекс Игры / VK): всё, чем сборки отличаются друг от друга, живёт здесь.
    /// Остальной код спрашивает только GamePlatform.Current и не знает, где запущен.
    /// Площадку выбирает символ компиляции из профиля сборки (Editor/BuildAll): PLATFORM_YANDEX, PLATFORM_VK, иначе — Steam.
    /// </summary>
    public abstract class GamePlatform
    {
        static GamePlatform _current;

        public static GamePlatform Current => _current ??= Create();

        /// <summary>SDK площадки ответил (язык и имя игрока известны) — интерфейс пересобирается.</summary>
        public static event Action Initialized;

        /// <summary>SDK начинает грузиться ещё до первой сцены.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Boot() => _ = Current;

        static GamePlatform Create()
        {
#if PLATFORM_YANDEX
            return new WebPlatform("yandex");
#elif PLATFORM_VK
            return new WebPlatform("vk");
#elif UNITY_WEBGL
            return new WebPlatform("web");
#else
            return new SteamPlatform();
#endif
        }

        protected static void RaiseInitialized() => Initialized?.Invoke();

        /// <summary>Код площадки для логов и аналитики: steam, yandex, vk.</summary>
        public abstract string Id { get; }

        /// <summary>Браузерная сборка: только WebSocket, без LAN и без выхода из игры.</summary>
        public virtual bool IsWeb => false;

        /// <summary>Комнаты в локальной сети (UDP-рассылка и вход по IP).</summary>
        public virtual bool SupportsLan => !IsWeb;

        /// <summary>Кнопка «Выйти из игры» (в браузере закрывают вкладку).</summary>
        public virtual bool CanQuit => !IsWeb;

        /// <summary>
        /// Группа совместимых по транспорту клиентов: в одну интернет-комнату попадают только клиенты одной группы
        /// (UDP/DTLS и WebSocket не смешиваем).
        /// </summary>
        public string NetGroup => IsWeb ? "web" : "pc";

        /// <summary>Системные шрифты ОС (деванагари для хинди есть только в Windows-шрифте Nirmala UI).</summary>
        public virtual bool HasSystemFonts => !IsWeb;

        /// <summary>SDK площадки готов (у ПК-версии ждать нечего).</summary>
        public virtual bool IsInitialized => true;

        public virtual void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Язык по умолчанию (пока игрок не выбрал свой).</summary>
        public virtual Lang DetectLanguage() => FromSystemLanguage(Application.systemLanguage);

        /// <summary>Связь с поддержкой (сообщество, почта); null — кнопки «Поддержка» в меню нет.</summary>
        public virtual string SupportUrl => null;

        public virtual void OpenSupport()
        {
            if (!string.IsNullOrEmpty(SupportUrl))
                Application.OpenURL(SupportUrl);
        }

        /// <summary>Имя игрока из аккаунта площадки; null — площадка имени не даёт, берём введённое в меню.</summary>
        public virtual string AccountName => null;

        /// <summary>Игра загрузилась и готова к игре (Яндекс: LoadingAPI.ready).</summary>
        public virtual void GameReady() { }

        /// <summary>Игрок начал / закончил играть (Яндекс: GameplayAPI.start/stop).</summary>
        public virtual void GameplayStarted() { }

        public virtual void GameplayStopped() { }

        /// <summary>Межстраничная реклама; onClosed вызывается всегда (и если рекламы нет).</summary>
        public virtual void ShowInterstitial(Action onClosed) => onClosed?.Invoke();

        /// <summary>Реклама за награду; onClosed(true) — досмотрел, награду выдать.</summary>
        public virtual void ShowRewarded(Action<bool> onClosed) => onClosed?.Invoke(false);

        protected static Lang FromSystemLanguage(SystemLanguage language)
        {
            switch (language)
            {
                case SystemLanguage.Russian: return Lang.Ru;
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional: return Lang.ZhHans;
                case SystemLanguage.Spanish: return Lang.Es;
                case SystemLanguage.Portuguese: return Lang.PtBr;
                case SystemLanguage.German: return Lang.De;
                case SystemLanguage.French: return Lang.Fr;
                case SystemLanguage.Japanese: return Lang.Ja;
                case SystemLanguage.Korean: return Lang.Ko;
                case SystemLanguage.Turkish: return Lang.Tr;
                case SystemLanguage.Polish: return Lang.Pl;
                case SystemLanguage.Ukrainian: return Lang.Uk;
                case SystemLanguage.Italian: return Lang.It;
                case SystemLanguage.Indonesian: return Lang.Id;
                case SystemLanguage.Belarusian: return Lang.Be;
                default: return Lang.En;
            }
        }
    }
}
