using System;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Слой площадки (Steam / Яндекс Игры / VK / Telegram): всё, чем сборки отличаются друг от друга, живёт здесь.
    /// Остальной код спрашивает только GamePlatform.Current и не знает, где запущен.
    /// Площадку выбирает символ компиляции из профиля сборки (Editor/BuildAll): PLATFORM_YANDEX, PLATFORM_VK, PLATFORM_TELEGRAM, иначе — Steam.
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
#elif PLATFORM_TELEGRAM
            return new WebPlatform("telegram");
#elif UNITY_WEBGL
            return new WebPlatform("web");
#else
            return new SteamPlatform();
#endif
        }

        protected static void RaiseInitialized() => Initialized?.Invoke();

        /// <summary>Код площадки для логов и аналитики: steam, yandex, vk, telegram.</summary>
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

        /// <summary>
        /// Ctrl можно назначать на действия: ПК-сборка и приложение Telegram на компьютере. В браузере сочетания
        /// с Ctrl (Ctrl+W, Ctrl+R…) перехватывает сам браузер.
        /// </summary>
        public virtual bool AllowsCtrl => !IsWeb;

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

        /// <summary>Кнопка «Позвать друга»: ссылка, открывающая игру сразу в комнате с этим кодом.</summary>
        public virtual bool CanInvite => false;

        public virtual void Invite(string roomCode) { }

        /// <summary>
        /// Ссылка, открывающая игру (Telegram Mini App) сразу в комнате с этим кодом, — для QR-кода стрим-комнаты.
        /// Одна на все площадки: зрители стрима заходят из Telegram.
        /// </summary>
        public static string InviteUrl(string roomCode) =>
            string.IsNullOrEmpty(roomCode) ? null : $"{WebPlatform.TelegramAppUrl}?startapp={WebPlatform.InviteStartPrefix}{roomCode}";

        /// <summary>Переключатель «Оконный режим» в опциях: ПК-сборка и Telegram на компьютере (Telegram Desktop, web.telegram.org).</summary>
        public virtual bool CanToggleWindowed => false;

        /// <summary>Сейчас в окне (не во весь экран). Опрашивается, пока открыты опции: режим могут сменить и мимо игры.</summary>
        public virtual bool IsWindowed => false;

        public virtual void SetWindowed(bool windowed) { }

        /// <summary>
        /// Код комнаты из ссылки-приглашения, с которой открыли игру; отдаётся один раз (потом null),
        /// чтобы после выхода из комнаты меню не входило в неё снова.
        /// </summary>
        public virtual string TakeInviteCode() => null;

        /// <summary>Имя игрока из аккаунта площадки; null — площадка имени не даёт, берём введённое в меню.</summary>
        public virtual string AccountName => null;

        /// <summary>
        /// Подписанное площадкой подтверждение аккаунта для сервера рейтинга (Telegram — initData).
        /// false — площадка его не даёт, игрок входит гостем.
        /// </summary>
        public virtual bool TryGetAccountProof(out string provider, out string data)
        {
            provider = data = null;
            return false;
        }

        /// <summary>Кнопка «Подписаться на бота» (Telegram): бот присылает анонсы стримов и итоги.</summary>
        public virtual bool HasBot => false;

        /// <summary>
        /// Спросить у площадки разрешение боту писать игроку. onDone(true) — разрешил (или уже было), false — отказал,
        /// null — площадка так не умеет (старый Telegram): игра открыла чат с ботом, подписка — его кнопкой «Старт».
        /// </summary>
        public virtual void RequestBotMessages(Action<bool?> onDone) => onDone?.Invoke(null);

        /// <summary>Игрок вернулся в игру (Telegram был свёрнут, вкладка скрыта): состояние снаружи могло измениться.</summary>
        public static event Action Resumed;

        protected static void RaiseResumed() => Resumed?.Invoke();

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
