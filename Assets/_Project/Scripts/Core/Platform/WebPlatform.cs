using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Браузерная сборка (Яндекс Игры, VK, Telegram Mini App). SDK площадки вызывается через Plugins/WebGL/RWPlatform.jslib,
    /// ответы приходят на объект RWPlatformBridge. В редакторе и без SDK — заглушки базового класса.
    /// </summary>
    public class WebPlatform : GamePlatform
    {
        /// <summary>
        /// Связь с поддержкой (сообщество VK) — обязательна для каталога VK. Пусто — кнопки «Поддержка» нет,
        /// а сборка VK предупреждает (Editor/BuildAll).
        /// </summary>
        public const string VkSupportUrl = "https://vk.ru/club242407320";

        /// <summary>Связь с поддержкой в Telegram (канал или чат игры, https://t.me/...). Пусто — кнопки «Поддержка» нет.</summary>
        public const string TelegramSupportUrl = "https://t.me/RocketWarsChat";

        /// <summary>Ссылка на Mini App (@BotFather → /newapp); приглашение добавляет к ней ?startapp=room_&lt;код&gt;.</summary>
        public const string TelegramAppUrl = "https://t.me/RocketWarsGameBot/play";

        /// <summary>Чат с ботом (старый Telegram без requestWriteAccess: подписка — кнопкой «Старт» в чате).</summary>
        public const string TelegramBotUrl = "https://t.me/RocketWarsGameBot?start=sub";

        /// <summary>Приставка параметра startapp у приглашения в комнату (остальные значения — на будущее, например рефералы).</summary>
        public const string InviteStartPrefix = "room_";

        /// <summary>
        /// Блоки рекламы Adsgram (partner.adsgram.ai → блок → blockId). Пусто — этот вид рекламы в Telegram не показывается,
        /// а сборка Telegram предупреждает (Editor/BuildAll).
        /// </summary>
        public const string AdsgramInterstitialBlock = "int-54871";
        public const string AdsgramRewardedBlock = "54872";

        /// <summary>
        /// Ключи PlayerPrefs, которые площадка хранит у себя (VKWebAppStorage, Telegram CloudStorage): хостинг VK меняет адрес
        /// игры при каждой выкладке, а WebView Telegram может очистить данные сайта, — сохранения браузера пропадают.
        /// Ключи — из Loc, UiRoot, GameInput, NetworkBootstrap, AdPolicy.
        /// </summary>
        static readonly (string key, char type)[] CloudPrefs = BuildCloudPrefs();

        const float CloudSaveInterval = 2f;

        readonly string _id;
        string _cloudSaved;
        string _accountName, _inviteCode, _authData;
        bool _initialized, _readyPending;
        bool _canToggleWindowed, _windowed, _allowsCtrl;
        Action<bool> _adClosed;
        Action<bool?> _writeAccess;

        public WebPlatform(string id)
        {
            _id = id;
            Bridge.Create(this);
            var config = new InitConfig { adInterstitial = AdsgramInterstitialBlock, adRewarded = AdsgramRewardedBlock };
            RW_Init(id, JsonUtility.ToJson(config));
        }

        public override string Id => _id;

        public override bool IsWeb => true;

        public override string AccountName => _accountName;

        public override bool IsInitialized => _initialized;

        public override string SupportUrl => _id switch
        {
            "vk" when !string.IsNullOrEmpty(VkSupportUrl) => VkSupportUrl,
            "telegram" when !string.IsNullOrEmpty(TelegramSupportUrl) => TelegramSupportUrl,
            _ => null,
        };

        // ссылки t.me внутри Telegram открываются его же средствами (openTelegramLink), а не новой вкладкой
        public override void OpenSupport()
        {
            if (!string.IsNullOrEmpty(SupportUrl))
                RW_OpenLink(SupportUrl);
        }

        public override bool CanInvite => _id == "telegram" && !string.IsNullOrEmpty(TelegramAppUrl);

        public override void Invite(string roomCode)
        {
            if (!CanInvite || string.IsNullOrEmpty(roomCode))
                return;
            RW_Share($"{InviteUrl(roomCode)}&mode=fullscreen", Loc.F("Заходи ко мне в Rocket Wars! Код комнаты: {0}", roomCode));
        }

        public override bool HasBot => _id == "telegram" && !string.IsNullOrEmpty(_authData);

        public override void RequestBotMessages(Action<bool?> onDone)
        {
            if (_writeAccess != null)
                return; // окно Telegram уже открыто
            _writeAccess = onDone ?? (_ => { });
            RW_RequestWriteAccess();
        }

        void OnWriteAccess(string answer)
        {
            var callback = _writeAccess;
            _writeAccess = null;
            if (answer == "-1")
                RW_OpenLink(TelegramBotUrl);
            callback?.Invoke(answer == "-1" ? (bool?)null : answer == "1");
        }

        public override bool AllowsCtrl => _allowsCtrl;

        public override bool CanToggleWindowed => _canToggleWindowed;

        // сразу отражаем выбор игрока; ответ Telegram (fullscreenChanged / fullscreenFailed) поправит через OnWindowed
        public override bool IsWindowed => _windowed;

        public override void SetWindowed(bool windowed)
        {
            if (!_canToggleWindowed || windowed == _windowed)
                return;
            _windowed = windowed;
            RW_SetFullscreen(windowed ? 0 : 1);
        }

        public override bool TryGetAccountProof(out string provider, out string data)
        {
            provider = _id == "telegram" && !string.IsNullOrEmpty(_authData) ? "telegram" : null;
            data = provider != null ? _authData : null;
            return provider != null;
        }

        public override string TakeInviteCode()
        {
            string code = _inviteCode;
            _inviteCode = null;
            return code;
        }

        /// <summary>startapp=room_ABC123 → ABC123 (startapp допускает только A–Z, a–z, 0–9, _ и -).</summary>
        static string ParseInviteCode(string start)
        {
            if (string.IsNullOrEmpty(start) || !start.StartsWith(InviteStartPrefix, StringComparison.Ordinal))
                return null;
            string code = start.Substring(InviteStartPrefix.Length).ToUpperInvariant();
            foreach (char c in code)
                if (!(c >= 'A' && c <= 'Z' || c >= '0' && c <= '9'))
                    return null;
            return code.Length > 0 ? code : null;
        }

        bool UsesCloudPrefs => _id == "vk" || _id == "telegram";

        public override void GameReady()
        {
            // SDK ещё грузится — сообщим, как только он ответит
            if (_initialized) RW_GameReady();
            else _readyPending = true;
        }

        public override void GameplayStarted() => RW_Gameplay(1);

        public override void GameplayStopped() => RW_Gameplay(0);

        public override void ShowInterstitial(Action onClosed) => ShowAd(false, _ => onClosed?.Invoke());

        public override void ShowRewarded(Action<bool> onClosed) => ShowAd(true, onClosed);

        void ShowAd(bool rewarded, Action<bool> onClosed)
        {
            if (_adClosed != null)
            {
                onClosed?.Invoke(false); // реклама уже на экране
                return;
            }
            _adClosed = onClosed ?? (_ => { });
            RW_ShowAd(rewarded ? 1 : 0);
        }

        void OnInit(string json)
        {
            var data = JsonUtility.FromJson<InitData>(json);
            _accountName = string.IsNullOrWhiteSpace(data.name) ? null : data.name.Trim();
            _inviteCode = ParseInviteCode(data.start);
            _authData = data.auth;
            _canToggleWindowed = data.winToggle;
            _windowed = data.windowed;
            _allowsCtrl = data.ctrlKeys;
            _initialized = true;
            // клавиши по умолчанию зависят от AllowsCtrl (присесть — Ctrl или C), а могли уже прочитаться в кэш
            GameInput.ReloadSaved();
            Debug.Log($"[RocketWars] platform {_id}: lang '{data.lang}', name '{_accountName}', start '{data.start}', cloud prefs {data.prefs?.Length ?? 0} chars");
            if (UsesCloudPrefs)
            {
                if (!string.IsNullOrEmpty(data.prefs))
                    ApplyCloudPrefs(data.prefs);
                _cloudSaved = CloudPrefsJson();
            }
            if (_readyPending)
            {
                _readyPending = false;
                RW_GameReady();
            }
            // язык площадки — пока игрок не выбрал свой (требование Яндекса)
            if (!Loc.HasSavedChoice && ParseLanguage(data.lang) is Lang lang)
                Loc.SetDefault(lang);
            RaiseInitialized();
        }

        /// <summary>Облако — самая свежая копия (с любого устройства): его значения перекрывают локальные.</summary>
        static void ApplyCloudPrefs(string json)
        {
            CloudData cloud;
            try { cloud = JsonUtility.FromJson<CloudData>(json); }
            catch (Exception e)
            {
                Debug.LogWarning("[RocketWars] cloud prefs: " + e.Message);
                return;
            }
            if (cloud.items == null)
                return;
            foreach (var item in cloud.items)
            {
                char type = Array.Find(CloudPrefs, p => p.key == item.k).type;
                switch (type)
                {
                    case 'i': if (int.TryParse(item.v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) PlayerPrefs.SetInt(item.k, i); break;
                    case 'f': if (float.TryParse(item.v, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)) PlayerPrefs.SetFloat(item.k, f); break;
                    case 's': PlayerPrefs.SetString(item.k, item.v ?? ""); break;
                }
            }
            PlayerPrefs.Save();
            // значения могли уже прочитаться в кэш до ответа SDK
            Loc.ReloadSaved();
            GameInput.ReloadSaved();
        }

        static string CloudPrefsJson()
        {
            var items = new System.Collections.Generic.List<CloudItem>();
            foreach (var (key, type) in CloudPrefs)
            {
                if (!PlayerPrefs.HasKey(key))
                    continue;
                string value = type switch
                {
                    'i' => PlayerPrefs.GetInt(key).ToString(CultureInfo.InvariantCulture),
                    'f' => PlayerPrefs.GetFloat(key).ToString("R", CultureInfo.InvariantCulture),
                    _ => PlayerPrefs.GetString(key),
                };
                items.Add(new CloudItem { k = key, v = value });
            }
            return JsonUtility.ToJson(new CloudData { items = items.ToArray() });
        }

        /// <summary>Раз в пару секунд: настройки изменились — отправить в облако (дёшево: десяток ключей).</summary>
        void SyncCloudPrefs()
        {
            if (!UsesCloudPrefs || !_initialized)
                return;
            string json = CloudPrefsJson();
            if (json == _cloudSaved)
                return;
            _cloudSaved = json;
            RW_SavePrefs(json);
        }

        static (string, char)[] BuildCloudPrefs()
        {
            var list = new System.Collections.Generic.List<(string, char)>
            {
                ("language", 'i'), ("volume", 'f'), (GameInput.MouseSensitivityKey, 'f'), (GameInput.TouchSensitivityKey, 'f'), (TouchInput.AutoFireKey, 'i'), ("playerName", 's'), (AdPolicy.MatchesKey, 'i'),
            };
            foreach (var action in Enum.GetNames(typeof(GameAction)))
                list.Add(("bind." + action, 'i'));
            return list.ToArray();
        }

        void OnAdClosed(bool rewarded)
        {
            var callback = _adClosed;
            _adClosed = null;
            callback?.Invoke(rewarded);
        }

        /// <summary>Код языка площадки (ru, en, pt-BR, zh, …) → язык игры; null — нет такого.</summary>
        static Lang? ParseLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
                return null;
            string primary = code.Split('-', '_')[0].ToLowerInvariant();
            switch (primary)
            {
                case "ru": return Lang.Ru;
                case "be": return Lang.Be;
                case "kk": return Lang.Kk;
                case "uz": return Lang.Uz;
                case "en": return Lang.En;
                case "zh": return Lang.ZhHans;
                case "es": return Lang.Es;
                case "pt": return Lang.PtBr;
                case "de": return Lang.De;
                case "fr": return Lang.Fr;
                case "ja": return Lang.Ja;
                case "ko": return Lang.Ko;
                case "tr": return Lang.Tr;
                case "pl": return Lang.Pl;
                case "uk": return Lang.Uk;
                case "it": return Lang.It;
                case "id": return Lang.Id;
                default: return Lang.En;
            }
        }

        /// <summary>Настройки площадки для jslib (RW_Init).</summary>
        [Serializable]
        struct InitConfig
        {
            public string adInterstitial;
            public string adRewarded;
        }

        [Serializable]
        struct InitData
        {
            public string lang;
            public string name;
            public string prefs;
            public string start;
            public string auth; // Telegram: initData целиком (подпись проверяет сервер рейтинга)
            public bool winToggle; // Telegram на компьютере: можно переключать «во весь экран / в окне»
            public bool windowed;
            public bool ctrlKeys; // приложение Telegram на компьютере: Ctrl можно назначать на действия
        }

        [Serializable]
        struct CloudData
        {
            public CloudItem[] items;
        }

        [Serializable]
        struct CloudItem
        {
            public string k, v;
        }

        /// <summary>Приёмник SendMessage из jslib; имя объекта зашито в RWPlatform.jslib.</summary>
        class Bridge : MonoBehaviour
        {
            WebPlatform _platform;
            bool _tabHidden, _sdkPaused, _adShown;
            float _nextCloudSync;

            public static void Create(WebPlatform platform)
            {
                var go = new GameObject("RWPlatformBridge");
                DontDestroyOnLoad(go);
                go.AddComponent<Bridge>()._platform = platform;
            }

            void OnInit(string json) => _platform.OnInit(json);

            void Update()
            {
                if (Time.unscaledTime < _nextCloudSync)
                    return;
                _nextCloudSync = Time.unscaledTime + CloudSaveInterval;
                _platform.SyncCloudPrefs();
            }

            void OnAdClosed(string rewarded) => _platform.OnAdClosed(rewarded == "1");

            void OnWindowed(string windowed) => _platform._windowed = windowed == "1";

            void OnWriteAccess(string answer) => _platform.OnWriteAccess(answer);

            // звук молчит, пока вкладка скрыта или идёт реклама. Игру не останавливаем — она сетевая
            void OnPause(string reason) => SetPaused(reason, true);

            void OnResume(string reason)
            {
                SetPaused(reason, false);
                if (reason != "ad")
                    RaiseResumed();
            }

            void SetPaused(string reason, bool paused)
            {
                switch (reason)
                {
                    case "tab": _tabHidden = paused; break;
                    case "ad": _adShown = paused; break;
                    default: _sdkPaused = paused; break;
                }
                AudioListener.pause = _tabHidden || _sdkPaused || _adShown;
            }
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void RW_Init(string platform, string config);
        [DllImport("__Internal")] static extern void RW_GameReady();
        [DllImport("__Internal")] static extern void RW_Gameplay(int started);
        [DllImport("__Internal")] static extern void RW_ShowAd(int rewarded);
        [DllImport("__Internal")] static extern void RW_SavePrefs(string json);
        [DllImport("__Internal")] static extern void RW_OpenLink(string url);
        [DllImport("__Internal")] static extern void RW_Share(string url, string text);
        [DllImport("__Internal")] static extern void RW_SetFullscreen(int on);
        [DllImport("__Internal")] static extern void RW_RequestWriteAccess();
#else
        // редактор: SDK нет — готовы сразу (без события: конструктор ещё не вернул Current), реклама сразу закрывается
        void RW_Init(string platform, string config) => _initialized = true;
        static void RW_GameReady() { }
        static void RW_Gameplay(int started) { }
        void RW_ShowAd(int rewarded) => OnAdClosed(false);
        static void RW_SavePrefs(string json) { }
        static void RW_OpenLink(string url) => Application.OpenURL(url);
        static void RW_Share(string url, string text) => Application.OpenURL("https://t.me/share/url?url=" + Uri.EscapeDataString(url) + "&text=" + Uri.EscapeDataString(text));
        static void RW_SetFullscreen(int on) { }
        void RW_RequestWriteAccess() => OnWriteAccess("1");
#endif
    }
}
