using System;
using System.Threading.Tasks;
using RocketWars.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace RocketWars.Online
{
    /// <summary>
    /// Клиент сервера рейтинга (Backend/, Cloudflare Worker). Вход — аккаунтом площадки (Telegram: подписанный initData),
    /// без него — гостем по случайному ключу устройства (гость копит очки, но в общий рейтинг не попадает).
    /// Все вызовы безопасны без сети: возвращают null / false, игра работает как раньше.
    /// </summary>
    public static class RatingApi
    {
        const string DefaultUrl = "https://rocketwars-api.tg-placeholder.workers.dev";
        const string GuestKeyPref = "ratingGuestKey";
        const int TimeoutSeconds = 15;
        const float AuthRetryDelay = 30f;

        static string _baseUrl;
        static string _token;
        static Task<bool> _auth;
        static float _authFailedAt = float.NegativeInfinity;

        /// <summary>Адрес сервера: аргумент «-api http://127.0.0.1:8787» или переменная RW_API_URL (проверка с локальным wrangler dev).</summary>
        public static string BaseUrl => _baseUrl ??= ResolveUrl();

        /// <summary>Аккаунт попадает в общую таблицу (Telegram); гость — только свои очки.</summary>
        public static bool Ranked { get; private set; }

        public static bool SignedIn => _token != null;

        /// <summary>Вошёл аккаунтом площадки (Telegram), а не гостем: стримы, анонсы и награды — только так.</summary>
        public static bool Verified { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            // войти заранее, чтобы первый матч уже шёл с рейтингом; у веба — когда SDK площадки ответит
            GamePlatform.Initialized += () => _ = EnsureAuth();
            _ = EnsureAuth();
        }

        static string ResolveUrl()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-api")
                    return args[i + 1].TrimEnd('/');
            string env = null;
            try { env = Environment.GetEnvironmentVariable("RW_API_URL"); }
            catch (Exception) { } // в браузере переменных окружения нет
            return string.IsNullOrEmpty(env) ? DefaultUrl : env.TrimEnd('/');
        }

        // ---------- вход ----------

        /// <summary>Войти, если ещё не вошли; повторная попытка после неудачи — не чаще раза в 30 с.</summary>
        public static Task<bool> EnsureAuth()
        {
            if (_token != null)
                return Task.FromResult(true);
            if (!GamePlatform.Current.IsInitialized || Time.unscaledTime - _authFailedAt < AuthRetryDelay)
                return Task.FromResult(false);
            return _auth ??= Auth();
        }

        static async Task<bool> Auth()
        {
            var platform = GamePlatform.Current;
            var request = platform.TryGetAccountProof(out string provider, out string data)
                ? new AuthRequest { provider = provider, data = data }
                : new AuthRequest { provider = "guest", data = GuestKey() };
            var (ok, text) = await Send("POST", "/v1/auth", JsonUtility.ToJson(request), false);
            _auth = null;
            if (!ok)
            {
                _authFailedAt = Time.unscaledTime;
                return false;
            }
            var response = JsonUtility.FromJson<AuthResponse>(text);
            _token = response.token;
            Ranked = response.ranked;
            Verified = response.verified;
            Skins.SetUnlocked(response.skins);
            Debug.Log($"[RocketWars] rating: signed in as {request.provider} #{response.playerId}, ranked {Ranked}");
            return true;
        }

        /// <summary>Ключ гостя: 32 случайных hex-символа, хранится на устройстве (сервер знает только его хэш).</summary>
        static string GuestKey()
        {
            // dev-сборка: «-guestKey <32 hex>» — у нескольких копий игры на одном ПК (общие PlayerPrefs) разные аккаунты
            var args = Environment.GetCommandLineArgs();
            int arg = Array.IndexOf(args, "-guestKey");
            if (Debug.isDebugBuild && arg >= 0 && arg + 1 < args.Length)
                return args[arg + 1];

            string key = PlayerPrefs.GetString(GuestKeyPref, "");
            if (key.Length == 32)
                return key;
            key = Guid.NewGuid().ToString("N");
            PlayerPrefs.SetString(GuestKeyPref, key);
            PlayerPrefs.Save();
            return key;
        }

        // ---------- матч ----------

        /// <summary>Хост: зарегистрировать матч; null — без рейтинга (нет сети или входа).</summary>
        public static async Task<string> StartMatch()
        {
            if (!await EnsureAuth())
                return null;
            var (ok, text) = await Send("POST", "/v1/match/start", JsonUtility.ToJson(new StartRequest { platform = MatchPlatform }), true);
            return ok ? JsonUtility.FromJson<StartResponse>(text).matchId : null;
        }

        /// <summary>Площадка матча для статистики (/stats бота): редактор и dev-сборки помечены, чтобы тесты не путались с игроками.</summary>
        static string MatchPlatform => Application.isEditor ? "editor"
            : Debug.isDebugBuild ? GamePlatform.Current.Id + "-dev"
            : GamePlatform.Current.Id;

        public static async Task<MatchResult> Report(MatchReport report)
        {
            if (!await EnsureAuth())
                return null;
            var (ok, text) = await Send("POST", "/v1/match/report", JsonUtility.ToJson(report), true);
            return ok ? JsonUtility.FromJson<MatchResult>(text) : null;
        }

        public static async Task<MatchResult> Result(string matchId)
        {
            if (_token == null)
                return null;
            var (ok, text) = await Send("GET", "/v1/match/result?matchId=" + UnityWebRequest.EscapeURL(matchId), null, true);
            return ok ? JsonUtility.FromJson<MatchResult>(text) : null;
        }

        // ---------- таблица ----------

        /// <summary>Таблица недели или всего времени; null — нет связи.</summary>
        public static async Task<Leaderboard> Leaderboard(bool allTime)
        {
            await EnsureAuth(); // без входа таблица тоже есть, только без своей строки
            var (ok, text) = await Send("GET", "/v1/leaderboard?limit=50&season=" + (allTime ? "all" : "week"), null, _token != null);
            if (!ok)
                return null;
            var board = JsonUtility.FromJson<Leaderboard>(text);
            board.hasMe = _token != null && board.me != null;
            return board;
        }

        // ---------- стримы (Backend/src/bot.js) ----------

        /// <summary>Стример открыл стрим-комнату (и потом «я ещё тут»); null — нет связи или вход гостем.</summary>
        public static async Task<StreamInfo> StreamOpen(string roomCode)
        {
            if (!await EnsureAuth() || !Verified)
                return null;
            var (ok, text) = await Send("POST", "/v1/stream/open", JsonUtility.ToJson(new StreamRequest { roomCode = roomCode }), true);
            return ok ? JsonUtility.FromJson<StreamInfo>(text) : null;
        }

        /// <summary>Анонс подписчикам бота: soon — «через 15 минут», иначе «играю прямо сейчас». error — код отказа сервера.</summary>
        public static async Task<(StreamInfo info, string error)> StreamAnnounce(bool soon)
        {
            if (!await EnsureAuth() || !Verified)
                return (null, "no_auth");
            var (ok, text) = await Send("POST", "/v1/stream/announce", JsonUtility.ToJson(new AnnounceRequest { kind = soon ? "soon" : "now" }), true);
            if (ok)
                return (JsonUtility.FromJson<StreamInfo>(text), null);
            ErrorResponse error = default;
            try { error = JsonUtility.FromJson<ErrorResponse>(text ?? ""); }
            catch (Exception) { } // не JSON: нет связи
            return (null, string.IsNullOrEmpty(error.error) ? "network" : error.error);
        }

        /// <summary>Стример вышел из стрим-комнаты: стрим закончен, игрокам — итоги.</summary>
        public static async Task StreamClose()
        {
            if (_token != null && Verified)
                await Send("POST", "/v1/stream/close", "{}", true);
        }

        /// <summary>Игрок доиграл матч в стрим-комнате: место на пьедестале и победа команды.</summary>
        public static async Task StreamPlayed(string roomCode, int place, bool won)
        {
            if (!await EnsureAuth() || !Verified)
                return;
            await Send("POST", "/v1/stream/played", JsonUtility.ToJson(new PlayedRequest { roomCode = roomCode, place = place, won = won }), true);
        }

        // ---------- HTTP ----------

        static async Task<(bool ok, string text)> Send(string method, string path, string json, bool withToken)
        {
            using var request = new UnityWebRequest(BaseUrl + path, method) { downloadHandler = new DownloadHandlerBuffer(), timeout = TimeoutSeconds };
            if (json != null)
            {
                request.uploadHandler = new UploadHandlerRaw(System.Text.Encoding.UTF8.GetBytes(json));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            if (withToken && _token != null)
                request.SetRequestHeader("Authorization", "Bearer " + _token);

            var done = new TaskCompletionSource<bool>();
            request.SendWebRequest().completed += _ => done.SetResult(true);
            await done.Task;

            if (request.responseCode == 401 && withToken)
                _token = null; // токен истёк — при следующем вызове войдём заново
            bool ok = request.result == UnityWebRequest.Result.Success;
            if (!ok)
                Debug.LogWarning($"[RocketWars] rating {method} {path}: {request.responseCode} {request.error} {request.downloadHandler?.text}");
            return (ok, request.downloadHandler?.text);
        }

        // ---------- данные (поля — как в JSON сервера) ----------

        [Serializable] struct AuthRequest { public string provider, data; }
        [Serializable] struct AuthResponse { public string token; public long playerId; public bool verified, ranked; public int[] skins; }
        [Serializable] struct StreamRequest { public string roomCode; }
        [Serializable] struct AnnounceRequest { public string kind; }
        [Serializable] struct PlayedRequest { public string roomCode; public int place; public bool won; }
        [Serializable] struct ErrorResponse { public string error; }
        [Serializable] struct StartRequest { public string platform; }
        [Serializable] struct StartResponse { public string matchId; }
    }

    /// <summary>Стрим стримера-партнёра: partner = false — стрим-комната без анонсов и наград.</summary>
    [Serializable]
    public class StreamInfo
    {
        public bool partner;
        public string streamId, name;
        public int announcesLeft;
        public int nextAnnounceIn; // секунд до следующего возможного анонса
        public int queued;         // ответ на анонс: скольким подписчикам поставлен в очередь
    }

    [Serializable]
    public class MatchReport
    {
        public string matchId;
        public long me;
        public int wins; // до скольких побед шёл матч (стрим-комната — короче)
        public int score0, score1, winner;
        public ReportRow[] rows;
    }

    [Serializable]
    public class ReportRow
    {
        public long id;
        public int team;
        public string name;
        public int downed, deaths, revives, silos, falls, tk;
        public uint ach;
        public bool left, bot;
    }

    /// <summary>Ответ на отчёт: credited — очки начислены, pending — ждём отчёты других игроков, rejected — таблицы не сошлись.</summary>
    [Serializable]
    public class MatchResult
    {
        public string status;
        public int points;
        public bool practice;
        public int weekPoints;
        public int rank;       // место за неделю после матча; 0 — нет (гость или 0 очков)
        public int rankBefore; // 0 — раньше в таблице не было
    }

    [Serializable]
    public class Leaderboard
    {
        public string season;
        public long resetsIn; // секунд до сброса недели; 0 — «всё время»
        public LeaderboardRow[] top;
        public LeaderboardMe me;
        [NonSerialized] public bool hasMe;
    }

    [Serializable]
    public class LeaderboardRow
    {
        public int rank;
        public string name;
        public int points, matches, wins;
        public bool me;
    }

    [Serializable]
    public class LeaderboardMe
    {
        public string name;
        public int points, matches, wins;
        public bool ranked;
        public int rank;
    }
}
