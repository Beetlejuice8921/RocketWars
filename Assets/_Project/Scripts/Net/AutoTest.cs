using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Online;
using RocketWars.Player;
using RocketWars.Stats;
using RocketWars.Terrain;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Net
{
    /// <summary>
    /// Автотест для dev-сборок (-autotest). Хост сам проводит матч: ждёт клиентов (-expect N), стартует,
    /// в каждом раунде пускает ракеты с обеих сторон, а через 12 с кладёт команду:
    /// раунд 1 — зелёных, раунд 2 — всех (ничья), дальше — зелёных. Итог: 4:0 через переигровку (5 раундов).
    /// Все участники пишут в лог фазы, статистику и (MatchNet) контрольные суммы террейна после взрывов —
    /// их сравнивают между хостом и клиентами.
    /// </summary>
    public class AutoTest : MonoBehaviour
    {
        const string Tag = "[AUTOTEST]";
        const float RocketInterval = 2.5f;
        const float RoundKnockTime = 12f;
        const float LobbyWaitLimit = 40f;
        const float HostQuitDelay = 8f;
        const float ClientQuitDelay = 5f;
        const float RatingWaitLimit = 40f;

        public int expectClients;
        public float timeout = 400f;

        MatchPhase _lastPhase = (MatchPhase)255;
        int _lastRound = -1;
        float _phaseStart, _nextRocket, _quitAt = -1f;
        bool _knocked, _startRequested;
        RatingState _lastRating;

        public static void Log(string message) => Debug.Log($"{Tag} {message}");

        bool _quitting;

        void Start() => Log($"started, expect {expectClients} clients");

        // клиента отключили (хост ушёл) — NetworkManager уничтожается при возврате в меню: завершаем тест
        void OnDestroy()
        {
            if (_quitting)
                return;
            Log("disconnected");
            Quit();
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (now > timeout)
            {
                Log("TIMEOUT");
                Quit();
                return;
            }
            if (MatchRating.State != _lastRating)
            {
                _lastRating = MatchRating.State;
                var r = MatchRating.Result;
                Log($"rating {_lastRating}" + (r != null ? $" status {r.status} points {r.points} week {r.weekPoints} rank {r.rank} practice {r.practice}" : ""));
            }
            // итог рейтинга приходит через несколько секунд после конца матча — дождаться его (не дольше RatingWaitLimit)
            bool ratingBusy = MatchRating.State == RatingState.Sending || MatchRating.State == RatingState.Pending;
            if (_quitAt > 0f && now >= _quitAt && (!ratingBusy || now >= _quitAt + RatingWaitLimit))
            {
                Log("DONE");
                Quit();
                return;
            }

            var match = MatchManager.Instance;
            if (match == null || !match.IsSpawned)
                return;

            var s = match.State;
            if (s.Phase != _lastPhase || s.Round != _lastRound)
                OnPhase(match, s, now);

            if (!match.IsServer)
                return;

            switch (s.Phase)
            {
                case MatchPhase.Lobby:
                    int clients = NetworkManager.Singleton.ConnectedClientsIds.Count - 1;
                    if (!_startRequested && (clients >= expectClients || now - _phaseStart > LobbyWaitLimit) && match.StartBlocker() == null)
                    {
                        _startRequested = true;
                        Log($"starting match with {clients} clients");
                        match.RequestStartMatch();
                    }
                    break;

                case MatchPhase.Round:
                    if (now >= _nextRocket)
                    {
                        _nextRocket = now + RocketInterval;
                        LaunchRandomRocket();
                    }
                    if (!_knocked && now - _phaseStart > RoundKnockTime)
                    {
                        _knocked = true;
                        KnockDown(s.Round == 2 ? -1 : 1);
                    }
                    break;
            }
        }

        void OnPhase(MatchManager match, MatchState s, float now)
        {
            _lastPhase = s.Phase;
            _lastRound = s.Round;
            _phaseStart = now;
            _knocked = false;
            Log($"phase {s.Phase} round {s.Round} score {s.Score0}:{s.Score1} winner {s.Winner} players {PlayerHealth.All.Count}");

            if (s.Phase == MatchPhase.MatchEnd)
            {
                LogStats();
                _quitAt = now + (match.IsServer ? HostQuitDelay : ClientQuitDelay);
            }
        }

        static void LaunchRandomRocket()
        {
            var ready = RocketSilo.All.FindAll(x => x.State == SiloState.Ready);
            if (ready.Count == 0)
                return;
            var silo = ready[Random.Range(0, ready.Count)];
            var enemy = VoxelIsland.ForTeam(1 - silo.Team);
            var target = enemy.GetRandomSpawnPoint(0f);
            if (silo.ServerLaunch(target, true, null, silo.Team))
                Log($"launch from team {silo.Team} silo {silo.NetIndex} to {target}");
        }

        /// <summary>team = −1 — положить всех (ничья).</summary>
        static void KnockDown(int team)
        {
            foreach (var p in PlayerHealth.All.ToArray())
            {
                if (!p.IsSpawned || p.IsDowned || (team >= 0 && p.Team != team))
                    continue;
                p.TakeDamage(new DamageInfo(1000f, p.transform.position, null, -1, DamageKind.Explosion));
            }
            Log(team < 0 ? "knocked down everyone (draw)" : $"knocked down team {team}");
        }

        static void LogStats()
        {
            var stats = StatsNet.Instance;
            if (stats == null)
                return;
            foreach (var e in stats.Entries)
                Log($"stats {e.Name} team {e.Team} downedEnemies {e.EnemiesDowned} downed {e.TimesDowned} revives {e.Revives} silos {e.SilosDestroyed} falls {e.Falls} teamkills {e.Teamkills} ach {e.Achievements} left {e.Left}");
        }

        void Quit()
        {
            _quitting = true;
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
