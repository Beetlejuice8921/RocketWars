using System;
using RocketWars.Net;
using RocketWars.Player;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RocketWars.Core
{
    public enum MatchPhase : byte
    {
        Lobby,
        Countdown,
        Round,
        RoundEnd,
        MatchEnd
    }

    public struct MatchState : INetworkSerializeByMemcpy, IEquatable<MatchState>
    {
        public MatchPhase Phase;
        public byte Round;
        public byte Score0;
        public byte Score1;
        /// <summary>Победитель последнего раунда/матча: 0, 1 или −1 (ничья).</summary>
        public sbyte Winner;
        public double PhaseEnd;

        public int Score(int team) => team == 0 ? Score0 : Score1;

        public bool Equals(MatchState o) =>
            Phase == o.Phase && Round == o.Round && Score0 == o.Score0 && Score1 == o.Score1 && Winner == o.Winner && PhaseEnd.Equals(o.PhaseEnd);

        public override bool Equals(object obj) => obj is MatchState o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Phase, Round, Score0, Score1, Winner);
    }

    /// <summary>
    /// Игровой цикл (решает сервер): Лобби → Отсчёт → Раунд → Итог раунда → … → Конец матча.
    /// Раунд проигрывает команда, у которой все игроки одновременно лежат. Матч — до 2 побед,
    /// каждый раунд — новые острова, полное HP, заряженные шахты.
    /// </summary>
    [RequireComponent(typeof(MatchNet))]
    public class MatchManager : NetworkBehaviour
    {
        public const int WinsNeeded = 2;
        public const float CountdownTime = 3f;
        public const float RoundEndTime = 4f;

        public static MatchManager Instance { get; private set; }

        readonly NetworkVariable<MatchState> _state = new NetworkVariable<MatchState>();

        public MatchState State => _state.Value;
        public MatchPhase Phase => _state.Value.Phase;

        /// <summary>Пульты работают только во время раунда (и без менеджера — например, в тестах).</summary>
        public static bool ConsolesEnabled => Instance == null || !Instance.IsSpawned || Instance.Phase == MatchPhase.Round;

        /// <summary>Игроки стоят на месте во время отсчёта.</summary>
        public static bool FreezePlayers => Instance != null && Instance.IsSpawned && Instance.Phase == MatchPhase.Countdown;

        /// <summary>Сервер: раунд закончился (победитель или −1).</summary>
        public event Action<int> RoundEnded;
        /// <summary>Сервер: начался новый матч.</summary>
        public event Action MatchStarted;
        /// <summary>Сервер: матч закончился (победитель).</summary>
        public event Action<int> MatchEnded;

        MatchNet _net;

        void Awake()
        {
            Instance = this;
            _net = GetComponent<MatchNet>();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
                _state.Value = new MatchState { Phase = MatchPhase.Lobby, Winner = -1 };
        }

        // ---------- запуск ----------

        /// <summary>Почему матч нельзя начать (null — можно).</summary>
        public string StartBlocker()
        {
            CountTeam(0, out int n0, out _);
            CountTeam(1, out int n1, out _);
            if (n0 == 0 || n1 == 0)
                return "Нужен хотя бы один игрок в каждой команде";
            return null;
        }

        public bool CanStartMatch => Phase == MatchPhase.Lobby || Phase == MatchPhase.MatchEnd;

        /// <summary>Хост нажал «Начать матч».</summary>
        public void RequestStartMatch()
        {
            if (!IsServer || !CanStartMatch || StartBlocker() != null)
                return;
            var s = new MatchState { Round = 0, Winner = -1 };
            _state.Value = s;
            MatchStarted?.Invoke();
            NextRound();
        }

        /// <summary>Отладка (R у хоста): переиграть текущий раунд на новых островах.</summary>
        public void DebugRestartRound()
        {
            if (!IsServer)
                return;
            if (Phase == MatchPhase.Lobby || Phase == MatchPhase.MatchEnd)
            {
                _net.StartRound(Random.Range(1, int.MaxValue));
                return;
            }
            var s = _state.Value;
            s.Round = (byte)Mathf.Max(0, s.Round - 1);
            _state.Value = s;
            NextRound();
        }

        void NextRound()
        {
            var s = _state.Value;
            s.Round++;
            s.Phase = MatchPhase.Countdown;
            s.PhaseEnd = NetClock.Now + CountdownTime;
            _state.Value = s;
            _net.StartRound(Random.Range(1, int.MaxValue));
        }

        // ---------- цикл ----------

        void Update()
        {
            if (!IsServer || !IsSpawned)
                return;

            var s = _state.Value;
            double now = NetClock.Now;
            switch (s.Phase)
            {
                case MatchPhase.Countdown when now >= s.PhaseEnd:
                    s.Phase = MatchPhase.Round;
                    _state.Value = s;
                    break;

                case MatchPhase.Round:
                    int result = CheckRoundResult();
                    if (result == NoResult)
                        break;
                    s.Winner = (sbyte)result;
                    if (result == 0) s.Score0++;
                    else if (result == 1) s.Score1++;
                    s.Phase = MatchPhase.RoundEnd;
                    s.PhaseEnd = now + RoundEndTime;
                    _state.Value = s;
                    Debug.Log($"[RocketWars] Round {s.Round} winner {result}, score {s.Score0}:{s.Score1}");
                    RoundEnded?.Invoke(result);
                    break;

                case MatchPhase.RoundEnd when now >= s.PhaseEnd:
                    if (s.Score0 >= WinsNeeded || s.Score1 >= WinsNeeded)
                    {
                        s.Phase = MatchPhase.MatchEnd;
                        s.Winner = (sbyte)(s.Score0 >= WinsNeeded ? 0 : 1);
                        _state.Value = s;
                        Debug.Log($"[RocketWars] Match winner {s.Winner}, score {s.Score0}:{s.Score1}");
                        MatchEnded?.Invoke(s.Winner);
                    }
                    else
                    {
                        NextRound();
                    }
                    break;
            }
        }

        const int NoResult = -2;

        /// <summary>Победитель раунда, −1 — ничья (обе команды легли разом), NoResult — раунд идёт.</summary>
        static int CheckRoundResult()
        {
            CountTeam(0, out int n0, out int down0);
            CountTeam(1, out int n1, out int down1);
            if (n0 == 0 && n1 == 0)
                return NoResult;

            // команда без игроков (все вышли) тоже проигрывает
            bool lost0 = n0 == 0 || down0 == n0;
            bool lost1 = n1 == 0 || down1 == n1;
            if (lost0 && lost1) return -1;
            if (lost0) return 1;
            if (lost1) return 0;
            return NoResult;
        }

        static void CountTeam(int team, out int players, out int downed)
        {
            players = downed = 0;
            foreach (var p in PlayerHealth.All)
            {
                if (!p.IsSpawned || p.Team != team)
                    continue;
                players++;
                if (p.IsDowned)
                    downed++;
            }
        }
    }
}
