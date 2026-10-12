using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Flags;
using RocketWars.Launcher;
using RocketWars.Player;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Stats
{
    /// <summary>Строка таблицы результатов (одна на игрока, остаётся и после его выхода).</summary>
    public struct PlayerStatsEntry : INetworkSerializeByMemcpy, IEquatable<PlayerStatsEntry>
    {
        public ulong PlayerId;          // NetworkObjectId игрока
        public FixedString32Bytes Name;
        public int Team;
        public ushort EnemiesDowned;    // положил врагов
        public ushort TimesDowned;      // лёг сам
        public ushort Revives;          // поднял союзников
        public ushort SilosDestroyed;   // взорвал вражеских шахт
        public ushort Falls;            // падения с острова
        public ushort Teamkills;        // положил своих
        public uint Achievements;       // маска по AchievementLibrary
        public bool Left;               // вышел из комнаты

        public bool Equals(PlayerStatsEntry o) =>
            PlayerId == o.PlayerId && Name.Equals(o.Name) && Team == o.Team && EnemiesDowned == o.EnemiesDowned &&
            TimesDowned == o.TimesDowned && Revives == o.Revives && SilosDestroyed == o.SilosDestroyed &&
            Falls == o.Falls && Teamkills == o.Teamkills && Achievements == o.Achievements && Left == o.Left;

        public override bool Equals(object obj) => obj is PlayerStatsEntry o && Equals(o);
        public override int GetHashCode() => PlayerId.GetHashCode();
    }

    /// <summary>
    /// Статистика матча. Считает сервер по событиям (кто кого положил, падения, подъёмы, шахты),
    /// клиенты получают таблицу через NetworkList. Достижения вычисляются в конце матча.
    /// </summary>
    [RequireComponent(typeof(MatchManager))]
    public class StatsNet : NetworkBehaviour
    {
        public static StatsNet Instance { get; private set; }

        public AchievementLibrary library;

        NetworkList<PlayerStatsEntry> _entries;
        MatchManager _match;

        public NetworkList<PlayerStatsEntry> Entries => _entries;

        void Awake()
        {
            Instance = this;
            _entries = new NetworkList<PlayerStatsEntry>();
            _match = GetComponent<MatchManager>();
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            base.OnDestroy();
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
                return;
            PlayerHealth.AnyDowned += OnDowned;
            NetworkPlayer.AnyFell += OnFell;
            NetworkPlayer.AnyRevivedAlly += OnRevived;
            RocketSilo.AnyDestroyed += OnSiloDestroyed;
            TeamFlag.FlagEvent += OnFlagEvent;
            _match.MatchStarted += OnMatchStarted;
            _match.MatchEnded += OnMatchEnded;
        }

        public override void OnNetworkDespawn()
        {
            PlayerHealth.AnyDowned -= OnDowned;
            NetworkPlayer.AnyFell -= OnFell;
            NetworkPlayer.AnyRevivedAlly -= OnRevived;
            RocketSilo.AnyDestroyed -= OnSiloDestroyed;
            TeamFlag.FlagEvent -= OnFlagEvent;
            if (_match != null)
            {
                _match.MatchStarted -= OnMatchStarted;
                _match.MatchEnded -= OnMatchEnded;
            }
        }

        /// <summary>Считаем только во время матча (разминка не в счёт).</summary>
        bool Counting => _match.Phase != MatchPhase.Lobby && _match.Phase != MatchPhase.MatchEnd;

        void Update()
        {
            if (!IsServer || !IsSpawned)
                return;

            // новые игроки появляются в таблице сразу, ушедшие помечаются
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && IndexOf(p.NetworkObjectId) < 0)
                    _entries.Add(NewEntry(p));

            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Left || FindPlayer(e.PlayerId) != null)
                    continue;
                e.Left = true;
                _entries[i] = e;
            }
        }

        // ---------- события ----------

        void OnMatchStarted()
        {
            _entries.Clear();
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned)
                    _entries.Add(NewEntry(p));
        }

        void OnDowned(PlayerHealth victim, DamageInfo info)
        {
            var killer = info.Source != null ? info.Source.GetComponent<PlayerHealth>() : null;
            var kind = info.Kind == DamageKind.Bullet ? FeedKind.Bullet : info.Kind == DamageKind.Fall ? FeedKind.Fall : FeedKind.Rocket;
            // своя ракета / падение — без «убийцы», в ленте только тот, кто лёг
            Feed(kind, killer != null && killer != victim && kind != FeedKind.Fall ? killer : null, victim.DisplayName, victim.Team, victim.NetworkObjectId);

            if (!Counting)
                return;
            Modify(victim.NetworkObjectId, (ref PlayerStatsEntry e) => e.TimesDowned++);

            var attacker = info.Source != null ? info.Source.GetComponent<PlayerHealth>() : null;
            if (attacker == null || attacker == victim || info.Kind == DamageKind.Fall)
                return;
            if (attacker.Team != victim.Team)
                Modify(attacker.NetworkObjectId, (ref PlayerStatsEntry e) => e.EnemiesDowned++);
            else
                Modify(attacker.NetworkObjectId, (ref PlayerStatsEntry e) => e.Teamkills++);
        }

        void OnFell(NetworkPlayer player)
        {
            if (Counting)
                Modify(player.NetworkObjectId, (ref PlayerStatsEntry e) => e.Falls++);
        }

        void OnRevived(NetworkPlayer reviver, PlayerHealth target)
        {
            Feed(FeedKind.Revive, reviver.Health, target.DisplayName, target.Team, target.NetworkObjectId);
            if (Counting)
                Modify(reviver.NetworkObjectId, (ref PlayerStatsEntry e) => e.Revives++);
        }

        void OnSiloDestroyed(RocketSilo silo, DamageInfo info)
        {
            var destroyer = info.Source != null ? info.Source.GetComponent<PlayerHealth>() : null;
            Feed(FeedKind.Silo, destroyer, "", silo.Team, ulong.MaxValue); // подпись «шахта КРАСНЫХ» собирает клиент на своём языке

            if (!Counting || info.Source == null || !info.Source.TryGetComponent(out PlayerHealth attacker))
                return;
            if (attacker.Team != silo.Team)
                Modify(attacker.NetworkObjectId, (ref PlayerStatsEntry e) => e.SilosDestroyed++);
        }

        /// <summary>Флаг забрали / уронили / доставили / вернулся на базу — строка в ленте.</summary>
        void OnFlagEvent(TeamFlag flag, string what, PlayerHealth actor)
        {
            // по сети идёт только глагол-ключ, подпись собирает клиент на своём языке (KillFeed.TargetText)
            Feed(FeedKind.Flag, actor, actor != null ? what : "", flag.Team, ulong.MaxValue);
        }

        // ---------- лента событий ----------

        void Feed(FeedKind kind, PlayerHealth actor, string target, int targetTeam, ulong targetId)
        {
            FeedRpc((byte)kind,
                new FixedString64Bytes(actor != null ? Trim(actor.DisplayName) : ""), actor != null ? actor.Team : -1,
                actor != null ? actor.NetworkObjectId : ulong.MaxValue,
                new FixedString64Bytes(Trim(target)), targetTeam, targetId);
        }

        static string Trim(string s) => s.Length > 24 ? s.Substring(0, 24) : s;

        [Rpc(SendTo.ClientsAndHost)]
        void FeedRpc(byte kind, FixedString64Bytes actor, int actorTeam, ulong actorId, FixedString64Bytes target, int targetTeam, ulong targetId)
        {
            KillFeed.Raise(new FeedEntry
            {
                Kind = (FeedKind)kind,
                Actor = actor.ToString(),
                ActorTeam = actorTeam,
                ActorId = actorId,
                Target = target.ToString(),
                TargetTeam = targetTeam,
                TargetId = targetId
            });
        }

        void OnMatchEnded(int winner)
        {
            if (library == null)
                return;
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                e.Achievements = 0;
                for (int a = 0; a < library.achievements.Count && a < 32; a++)
                    if (Earned(library.achievements[a].rule, e))
                        e.Achievements |= 1u << a;
                _entries[i] = e;
            }
        }

        bool Earned(AchievementRule rule, in PlayerStatsEntry e)
        {
            switch (rule)
            {
                case AchievementRule.MostEnemiesDowned: return IsBest(e, x => x.EnemiesDowned);
                case AchievementRule.MostRevives: return IsBest(e, x => x.Revives);
                case AchievementRule.MostSilosDestroyed: return IsBest(e, x => x.SilosDestroyed);
                case AchievementRule.MostFalls: return IsBest(e, x => x.Falls);
                case AchievementRule.AnyTeamkill: return e.Teamkills > 0;
                case AchievementRule.NeverDowned: return e.TimesDowned == 0 && !e.Left;
                default: return false;
            }
        }

        /// <summary>Лучший результат в комнате (≥1; при равенстве достижение получают все лидеры).</summary>
        bool IsBest(in PlayerStatsEntry e, Func<PlayerStatsEntry, int> value)
        {
            int mine = value(e);
            if (mine <= 0)
                return false;
            foreach (var other in _entries)
                if (value(other) > mine)
                    return false;
            return true;
        }

        // ---------- служебное ----------

        delegate void EntryModifier(ref PlayerStatsEntry e);

        void Modify(ulong playerId, EntryModifier modify)
        {
            int i = IndexOf(playerId);
            if (i < 0)
                return;
            var e = _entries[i];
            modify(ref e);
            _entries[i] = e;
        }

        int IndexOf(ulong playerId)
        {
            for (int i = 0; i < _entries.Count; i++)
                if (_entries[i].PlayerId == playerId)
                    return i;
            return -1;
        }

        static PlayerHealth FindPlayer(ulong playerId)
        {
            foreach (var p in PlayerHealth.All)
                if (p.IsSpawned && p.NetworkObjectId == playerId)
                    return p;
            return null;
        }

        static PlayerStatsEntry NewEntry(PlayerHealth p) => new PlayerStatsEntry
        {
            PlayerId = p.NetworkObjectId,
            Name = new FixedString32Bytes(p.DisplayName),
            Team = p.Team
        };
    }
}
