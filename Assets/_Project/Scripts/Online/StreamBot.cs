using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Player;
using RocketWars.Stats;
using UnityEngine;

namespace RocketWars.Online
{
    /// <summary>
    /// Стрим-комната и бот Telegram (Backend/src/bot.js). Стример (хост): при открытии комнаты и потом раз в несколько
    /// минут сообщает серверу «стрим идёт, комната такая-то» — сервер отвечает, партнёр ли он (только партнёры делают
    /// анонсы подписчикам бота); при выходе из комнаты стрим закрывается, и бот рассылает игрокам итоги.
    /// Зритель: после каждого матча — своё место на пьедестале и победу команды (для итогов и скина-награды).
    /// Всё работает только у входа через Telegram; без связи стрим-комната просто живёт без бота.
    /// </summary>
    public static class StreamBot
    {
        const float HeartbeatInterval = 300f; // сервер считает стрим законченным через 20 мин тишины
        const float PlayedDelay = 2f;         // таблица итогов доезжает до клиентов не мгновенно

        /// <summary>Сервер ответил на открытие стрима.</summary>
        public static bool Known { get; private set; }
        public static bool Partner { get; private set; }
        public static int AnnouncesLeft { get; private set; }
        public static bool Busy { get; private set; }
        /// <summary>Результат последнего анонса (уже переведённый) или null.</summary>
        public static string Status { get; private set; }

        static string _openedCode;
        static float _nextHeartbeat, _announceReadyAt;
        static float _matchEndSeenAt = -1f;
        static bool _playedSent;

        /// <summary>Секунд до следующего возможного анонса (сервер разрешает раз в 10 минут).</summary>
        public static int AnnounceCooldown => Mathf.Max(0, Mathf.CeilToInt(_announceReadyAt - Time.unscaledTime));

        /// <summary>Каждый кадр, пока открыт экран стрим-комнаты (StreamView).</summary>
        public static void Tick(MatchNet net, bool host, string roomCode)
        {
            if (host)
                HostTick(roomCode);
            else
                ViewerTick(net, roomCode);
        }

        // ---------- стример ----------

        static void HostTick(string roomCode)
        {
            if (string.IsNullOrEmpty(roomCode) || !RatingApi.Verified && RatingApi.SignedIn)
                return; // гость: бот его не знает
            if (Known && !Partner && roomCode == _openedCode)
                return; // не партнёр — спрашивать снова незачем
            if (roomCode == _openedCode && Time.unscaledTime < _nextHeartbeat)
                return;
            _openedCode = roomCode;
            _nextHeartbeat = Time.unscaledTime + HeartbeatInterval;
            Open(roomCode);
        }

        static async void Open(string roomCode)
        {
            var info = await RatingApi.StreamOpen(roomCode);
            if (info == null || roomCode != _openedCode)
            {
                if (info == null)
                    _nextHeartbeat = Time.unscaledTime + 30f; // нет связи — попробуем скоро ещё раз
                return;
            }
            Apply(info);
        }

        static void Apply(StreamInfo info)
        {
            Known = true;
            Partner = info.partner;
            AnnouncesLeft = info.announcesLeft;
            _announceReadyAt = Time.unscaledTime + info.nextAnnounceIn;
        }

        /// <summary>Анонс подписчикам бота: soon — «через 15 минут», иначе «играю прямо сейчас».</summary>
        public static async void Announce(bool soon)
        {
            if (Busy || !Partner)
                return;
            Busy = true;
            Status = null;
            var (info, error) = await RatingApi.StreamAnnounce(soon);
            Busy = false;
            if (info != null)
            {
                Apply(info);
                Status = Loc.F("Анонс отправлен: {0} подписчикам", info.queued);
                return;
            }
            Status = error switch
            {
                "announce_limit" => Loc.T("Анонсы на этот стрим закончились"),
                "announce_too_soon" => Loc.T("Следующий анонс — чуть позже"),
                _ => Loc.T("Не удалось отправить анонс"),
            };
            if (error == "announce_limit")
                AnnouncesLeft = 0;
        }

        /// <summary>Хост выходит из комнаты (NetworkBootstrap.ReturnToMenu): стрим закончен, бот разошлёт итоги.</summary>
        public static void OnLeaveRoom()
        {
            if (_openedCode != null && Partner)
                _ = RatingApi.StreamClose();
            _openedCode = null;
            Known = Partner = Busy = false;
            Status = null;
            AnnouncesLeft = 0;
            _matchEndSeenAt = -1f;
            _playedSent = false;
        }

        // ---------- зритель ----------

        static void ViewerTick(MatchNet net, string roomCode)
        {
            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned || match.Phase != MatchPhase.MatchEnd)
            {
                _matchEndSeenAt = -1f;
                _playedSent = false;
                return;
            }
            if (_matchEndSeenAt < 0f)
                _matchEndSeenAt = Time.unscaledTime;
            if (_playedSent || Time.unscaledTime - _matchEndSeenAt < PlayedDelay || string.IsNullOrEmpty(roomCode))
                return;
            _playedSent = true;
            if (!TryGetPlace(match, out int place, out bool won))
                return; // смотрел из очереди — в этом матче не играл
            _ = RatingApi.StreamPlayed(roomCode, place, won);
        }

        /// <summary>Место на пьедестале — тем же порядком, что на экране итогов (ResultsView): враги ↓, свои падения ↑.</summary>
        static bool TryGetPlace(MatchManager match, out int place, out bool won)
        {
            place = 0;
            won = false;
            var me = NetworkPlayer.LocalPlayer;
            var stats = StatsNet.Instance;
            if (me == null || stats == null)
                return false;
            var entries = new System.Collections.Generic.List<PlayerStatsEntry>();
            foreach (var e in stats.Entries)
                if (!e.Left)
                    entries.Add(e);
            entries.Sort((a, b) => b.EnemiesDowned != a.EnemiesDowned ? b.EnemiesDowned.CompareTo(a.EnemiesDowned) : a.TimesDowned.CompareTo(b.TimesDowned));
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].PlayerId != me.NetworkObjectId)
                    continue;
                place = i + 1;
                won = entries[i].Team == match.State.Winner;
                return true;
            }
            return false;
        }
    }
}
