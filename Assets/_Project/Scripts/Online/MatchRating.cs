using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Player;
using RocketWars.Stats;
using UnityEngine;

namespace RocketWars.Online
{
    public enum RatingState
    {
        None,       // матч не закончен
        Unrated,    // матч без рейтинга (сервер рейтинга недоступен хосту)
        Sending,    // отправляем итог
        Pending,    // ждём, пока итог подтвердят другие игроки
        Credited,   // очки начислены
        Rejected,   // итог не подтвердили (таблицы не сошлись или подтверждений не хватило)
        Failed      // нет связи с сервером рейтинга
    }

    /// <summary>
    /// Отчёт об итогах матча. Каждый живой игрок (и хост) после конца матча отправляет таблицу итогов на сервер рейтинга
    /// и отмечает в ней свою строку; сервер начисляет очки, когда таблицы нескольких игроков совпали.
    /// Состояние для экрана итогов — State / Result.
    /// </summary>
    public static class MatchRating
    {
        /// <summary>Таблица на клиентах доходит не мгновенно — отправляем, когда она точно доехала.</summary>
        const float ReportDelay = 2f;
        const float PollInterval = 3f;
        const int PollCount = 10;

        public static RatingState State { get; private set; }
        public static MatchResult Result { get; private set; }

        static float _matchEndSeenAt = -1f;
        static string _reportedMatch;
        static int _serial;

        /// <summary>Каждый кадр с каждого пира (StatsNet.Update).</summary>
        public static void Tick(StatsNet stats, MatchManager match)
        {
            if (match == null || !match.IsSpawned || match.Phase != MatchPhase.MatchEnd)
            {
                if (_matchEndSeenAt >= 0f)
                    Reset();
                return;
            }
            if (_matchEndSeenAt < 0f)
            {
                _matchEndSeenAt = Time.unscaledTime;
                State = string.IsNullOrEmpty(stats.RatedMatchId) ? RatingState.Unrated : RatingState.Sending;
            }
            string id = stats.RatedMatchId;
            if (string.IsNullOrEmpty(id) || id == _reportedMatch || Time.unscaledTime - _matchEndSeenAt < ReportDelay)
                return;
            var report = BuildReport(stats, match, id);
            if (report == null)
                return;
            _reportedMatch = id;
            Send(report);
        }

        static void Reset()
        {
            _matchEndSeenAt = -1f;
            _serial++; // ответ об этом матче, пришедший позже, уже не показываем
            State = RatingState.None;
            Result = null;
        }

        static MatchReport BuildReport(StatsNet stats, MatchManager match, string matchId)
        {
            var me = NetworkPlayer.LocalPlayer;
            if (me == null || me.IsBot)
                return null;
            var rows = new List<ReportRow>();
            bool found = false;
            foreach (var e in stats.Entries)
            {
                found |= e.PlayerId == me.NetworkObjectId;
                rows.Add(new ReportRow
                {
                    id = (long)e.PlayerId,
                    team = e.Team,
                    name = e.Name.ToString(),
                    downed = e.EnemiesDowned,
                    deaths = e.TimesDowned,
                    revives = e.Revives,
                    silos = e.SilosDestroyed,
                    falls = e.Falls,
                    tk = e.Teamkills,
                    ach = e.Achievements,
                    left = e.Left,
                    bot = e.Bot,
                });
            }
            if (!found)
            {
                State = RatingState.None; // зашёл уже на итоги — в матче не играл
                return null;
            }
            var s = match.State;
            return new MatchReport { matchId = matchId, me = (long)me.NetworkObjectId, wins = match.WinsTarget, score0 = s.Score0, score1 = s.Score1, winner = s.Winner, rows = rows.ToArray() };
        }

        static async void Send(MatchReport report)
        {
            int serial = _serial;
            State = RatingState.Sending;
            var result = await RatingApi.Report(report);
            for (int i = 0; i < PollCount && result != null && result.status == "pending"; i++)
            {
                if (serial != _serial)
                    return;
                State = RatingState.Pending;
                await Awaitable.WaitForSecondsAsync(PollInterval);
                result = await RatingApi.Result(report.matchId) ?? result;
            }
            if (serial != _serial)
                return;
            Result = result;
            State = result == null ? RatingState.Failed
                : result.status == "credited" ? RatingState.Credited
                : RatingState.Rejected;
        }
    }
}
