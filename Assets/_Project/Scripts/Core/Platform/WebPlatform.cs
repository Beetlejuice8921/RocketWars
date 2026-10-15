using System;
using System.Globalization;
using System.Runtime.InteropServices;
using UnityEngine;

namespace RocketWars.Core
{
    /// <summary>
    /// Браузерная сборка (Яндекс Игры, VK). SDK площадки вызывается через Plugins/WebGL/RWPlatform.jslib,
    /// ответы приходят на объект RWPlatformBridge. В редакторе и без SDK — заглушки базового класса.
    /// </summary>
    public class WebPlatform : GamePlatform
    {
        /// <summary>
        /// Связь с поддержкой (сообщество VK) — обязательна для каталога VK. Пусто — кнопки «Поддержка» нет,
        /// а сборка VK предупреждает (Editor/BuildAll).
        /// </summary>
        public const string VkSupportUrl = "";

        /// <summary>
        /// Ключи PlayerPrefs, которые VK хранит у себя (VKWebAppStorage): хостинг VK меняет адрес игры при каждой выкладке,
        /// и сохранения браузера пропадают. Ключи — из Loc, UiRoot, GameInput, NetworkBootstrap.
        /// </summary>
        static readonly (string key, char type)[] CloudPrefs = BuildCloudPrefs();

        const float CloudSaveInterval = 2f;

        readonly string _id;
        string _cloudSaved;
        string _accountName;
        bool _initialized, _readyPending;
        Action<bool> _adClosed;

        public WebPlatform(string id)
        {
            _id = id;
            Bridge.Create(this);
            RW_Init(id);
        }

        public override string Id => _id;

        public override bool IsWeb => true;

        public override string AccountName => _accountName;

        public override bool IsInitialized => _initialized;

        public override string SupportUrl => _id == "vk" && !string.IsNullOrEmpty(VkSupportUrl) ? VkSupportUrl : null;

        bool UsesCloudPrefs => _id == "vk";

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
            _initialized = true;
            Debug.Log($"[RocketWars] platform {_id}: lang '{data.lang}', name '{_accountName}', cloud prefs {data.prefs?.Length ?? 0} chars");
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
                ("language", 'i'), ("volume", 'f'), ("mouseSensitivity", 'f'), ("playerName", 's'),
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

        [Serializable]
        struct InitData
        {
            public string lang;
            public string name;
            public string prefs;
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

            // звук молчит, пока вкладка скрыта или идёт реклама. Игру не останавливаем — она сетевая
            void OnPause(string reason) => SetPaused(reason, true);

            void OnResume(string reason) => SetPaused(reason, false);

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
        [DllImport("__Internal")] static extern void RW_Init(string platform);
        [DllImport("__Internal")] static extern void RW_GameReady();
        [DllImport("__Internal")] static extern void RW_Gameplay(int started);
        [DllImport("__Internal")] static extern void RW_ShowAd(int rewarded);
        [DllImport("__Internal")] static extern void RW_SavePrefs(string json);
#else
        // редактор: SDK нет — готовы сразу (без события: конструктор ещё не вернул Current), реклама сразу закрывается
        void RW_Init(string platform) => _initialized = true;
        static void RW_GameReady() { }
        static void RW_Gameplay(int started) { }
        void RW_ShowAd(int rewarded) => OnAdClosed(false);
        static void RW_SavePrefs(string json) { }
#endif
    }
}
