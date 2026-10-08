using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Flags;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Player;
using RocketWars.Terrain;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

namespace RocketWars.Net
{
    public struct RoundInfo : INetworkSerializeByMemcpy, IEquatable<RoundInfo>
    {
        public int Seed;
        public int Id;

        public bool Equals(RoundInfo other) => Seed == other.Seed && Id == other.Id;
        public override bool Equals(object obj) => obj is RoundInfo o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(Seed, Id);
    }

    /// <summary>
    /// Сетевое состояние мира (in-scene NetworkObject):
    /// • seed раунда — острова и шахты генерируются у всех одинаково;
    /// • взрывы рассылаются всем и применяются к кубикам детерминированно, подключившимся позже — история раунда;
    /// • состояние шахт и пультов — NetworkList&lt;SiloSync&gt;, ящиков с припасами — NetworkList&lt;CrateSync&gt;;
    /// • спавн игроков с автобалансом команд, тестовые боты.
    /// </summary>
    public class MatchNet : NetworkBehaviour
    {
        public static MatchNet Instance { get; private set; }

        public float islandGap = 45f;

        readonly NetworkVariable<RoundInfo> _round = new NetworkVariable<RoundInfo>();
        NetworkList<SiloSync> _siloSync;
        NetworkList<CrateSync> _crateSync;
        NetworkList<FlagSync> _flagSync;

        readonly List<VoxelIsland> _islands = new List<VoxelIsland>();
        readonly List<RocketSilo> _silos = new List<RocketSilo>();
        readonly List<LootCrate> _crates = new List<LootCrate>();
        readonly List<TeamFlag> _flags = new List<TeamFlag>();
        readonly List<Vector3> _history = new List<Vector3>();
        Transform _worldRoot;
        int _builtRoundId;

        public IReadOnlyList<VoxelIsland> Islands => _islands;
        public int RoundId => _round.Value.Id;

        /// <summary>Сервер: заспавнить тестовых ботов (союзник лежит рядом, два врага).</summary>
        public static bool SpawnTestBots { get; set; }

        /// <summary>Сервер: новый раунд построен (для MatchManager на этапе 4).</summary>
        public event Action<int> RoundStarted;

        void Awake()
        {
            Instance = this;
            _siloSync = new NetworkList<SiloSync>();
            _crateSync = new NetworkList<CrateSync>();
            _flagSync = new NetworkList<FlagSync>();

            _worldRoot = new GameObject("World").transform;
            for (int team = 0; team < 2; team++)
            {
                var go = new GameObject($"Island_T{team}");
                go.transform.SetParent(_worldRoot, false);
                // острова друг напротив друга по оси Z, как на map.jpg
                float z = team == 0 ? -VoxelIsland.Depth - islandGap * 0.5f : islandGap * 0.5f;
                go.transform.position = new Vector3(-VoxelIsland.Width * 0.5f, 0f, z);
                _islands.Add(go.AddComponent<VoxelIsland>());
            }
        }

        public override void OnNetworkSpawn()
        {
            _round.OnValueChanged += OnRoundChanged;

            if (IsServer)
            {
                Explosion.Broadcast = BroadcastExplosion;
                NetworkManager.OnConnectionEvent += OnConnectionEvent;
                StartRound(Random.Range(1, int.MaxValue));
                foreach (var clientId in NetworkManager.ConnectedClientsIds)
                    SpawnPlayerFor(clientId);
                if (SpawnTestBots)
                    SpawnBots();
            }
            else
            {
                BuildWorld(_round.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            _round.OnValueChanged -= OnRoundChanged;
            if (IsServer && NetworkManager != null)
                NetworkManager.OnConnectionEvent -= OnConnectionEvent;
            if (Explosion.Broadcast == (Action<Vector3>)BroadcastExplosion)
                Explosion.Broadcast = null;
        }

        public override void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            if (_worldRoot != null)
                Destroy(_worldRoot.gameObject);
            base.OnDestroy();
        }

        public RocketSilo GetSilo(int index) => index >= 0 && index < _silos.Count ? _silos[index] : null;
        public LootCrate GetCrate(int index) => index >= 0 && index < _crates.Count ? _crates[index] : null;
        /// <summary>Флаг команды team (0/1) или null.</summary>
        public TeamFlag GetFlag(int team) => _flags.Find(f => f != null && f.Team == team);
        public IReadOnlyList<TeamFlag> Flags => _flags;

        // ---------- раунд ----------

        /// <summary>Сервер: новые острова и шахты, игроки — полное HP и сверху своих островов.</summary>
        public void StartRound(int seed)
        {
            if (!IsServer)
                return;

            _history.Clear();
            var round = new RoundInfo { Seed = seed, Id = _round.Value.Id + 1 };
            _round.Value = round;
            BuildWorld(round);

            _siloSync.Clear();
            foreach (var silo in _silos)
                _siloSync.Add(silo.ToSync());
            _crateSync.Clear();
            foreach (var crate in _crates)
                _crateSync.Add(crate.ToSync());
            _flagSync.Clear();
            foreach (var flag in _flags)
                _flagSync.Add(flag.ToSync());

            foreach (var p in PlayerHealth.All)
            {
                p.ResetFull();
                if (p.TryGetComponent(out NetworkPlayer loadout) && loadout.IsSpawned)
                    loadout.ServerResetLoadout();
                if (p.TryGetComponent(out NetworkPlayer np) && np.IsSpawned)
                    np.TeleportRpc(_islands[p.Team].GetRandomSpawnPoint(1f), FacingEnemy(p.Team));
            }

            Debug.Log($"[RocketWars] Round {round.Id}, seed {seed}");
            RoundStarted?.Invoke(round.Id);
        }

        void OnRoundChanged(RoundInfo previous, RoundInfo current) => BuildWorld(current);

        void BuildWorld(RoundInfo round)
        {
            if (round.Id == 0 || round.Id == _builtRoundId)
                return;
            _builtRoundId = round.Id;

            foreach (var silo in _silos)
                if (silo != null)
                    Destroy(silo.gameObject);
            _silos.Clear();
            foreach (var crate in _crates)
                if (crate != null)
                    Destroy(crate.gameObject);
            _crates.Clear();
            foreach (var flag in _flags)
                if (flag != null)
                    Destroy(flag.gameObject);
            _flags.Clear();

            // ракеты прошлого раунда не должны долететь до новых островов
            foreach (var rocket in FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                Destroy(rocket.gameObject);

            for (int team = 0; team < _islands.Count; team++)
            {
                _islands[team].Build(team, round.Seed + team * 7919);
                var islandSilos = SiloPlacer.Place(_islands[team], round.Seed + team * 104729);
                _silos.AddRange(islandSilos);
                _crates.AddRange(CratePlacer.Place(_islands[team], round.Seed + team * 15485863, islandSilos, _worldRoot));
                var flag = TeamFlag.Create(_islands[team], _worldRoot);
                flag.IsAuthority = IsServer;
                _flags.Add(flag);
            }

            for (int i = 0; i < _crates.Count; i++)
            {
                _crates[i].NetIndex = i;
                _crates[i].IsAuthority = IsServer;
            }

            for (int i = 0; i < _silos.Count; i++)
            {
                _silos[i].NetIndex = i;
                _silos[i].IsAuthority = IsServer;
                if (IsServer)
                    _silos[i].Launched += OnSiloLaunched;
            }

            TacticalMap.Ensure().Setup(_islands);
            LogTerrainChecksum("build");
        }

        Quaternion FacingEnemy(int team)
        {
            Vector3 dir = _islands[1 - team].Center - _islands[team].Center;
            dir.y = 0f;
            return Quaternion.LookRotation(dir);
        }

        // ---------- шахты ----------

        void Update()
        {
            if (!IsSpawned)
                return;

            if (IsServer)
            {
                for (int i = 0; i < _silos.Count && i < _siloSync.Count; i++)
                {
                    var sync = _silos[i].ToSync();
                    if (!_siloSync[i].Equals(sync))
                        _siloSync[i] = sync;
                }
                for (int i = 0; i < _crates.Count && i < _crateSync.Count; i++)
                {
                    var sync = _crates[i].ToSync();
                    if (!_crateSync[i].Equals(sync))
                        _crateSync[i] = sync;
                }
                for (int i = 0; i < _flags.Count && i < _flagSync.Count; i++)
                {
                    _flags[i].ServerUpdate();
                    var sync = _flags[i].ToSync();
                    if (!_flagSync[i].Equals(sync))
                        _flagSync[i] = sync;
                }
            }
            else
            {
                for (int i = 0; i < _silos.Count && i < _siloSync.Count; i++)
                    _silos[i].ApplySync(_siloSync[i]);
                for (int i = 0; i < _crates.Count && i < _crateSync.Count; i++)
                    _crates[i].ApplySync(_crateSync[i]);
                for (int i = 0; i < _flags.Count && i < _flagSync.Count; i++)
                    _flags[i].ApplySync(_flagSync[i]);
            }
        }

        void OnSiloLaunched(RocketSilo silo, LaunchInfo info)
        {
            LaunchRpc(info.Start, info.Target, info.HasGround, info.SourceTeam, info.LaunchTime, RoundId, info.SiloIndex);
        }

        [Rpc(SendTo.NotServer)]
        void LaunchRpc(Vector3 start, Vector3 target, bool hasGround, int team, double launchTime, int roundId, int siloIndex)
        {
            if (roundId != _builtRoundId)
                return;
            Rocket.Spawn(new LaunchInfo
            {
                Start = start,
                Target = target,
                HasGround = hasGround,
                SourceTeam = team,
                LaunchTime = launchTime,
                SiloIndex = siloIndex
            }, false);
        }

        // ---------- взрывы ----------

        void BroadcastExplosion(Vector3 position)
        {
            _history.Add(position);
            ExplodeRpc(position, RoundId);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void ExplodeRpc(Vector3 position, int roundId)
        {
            if (roundId != _builtRoundId)
                return;
            Explosion.ApplyLocal(position, true);
            LogTerrainChecksum("explosion");
        }

        void LogTerrainChecksum(string reason)
        {
            if (!Debug.isDebugBuild)
                return;
            int hash = 0;
            foreach (var island in _islands)
                hash = hash * 31 + island.ComputeChecksum();
            Debug.Log($"[RocketWars] terrain {reason}: round {_builtRoundId} hash {hash:X8}");
        }

        [Rpc(SendTo.SpecifiedInParams)]
        void HistoryRpc(Vector3[] positions, int roundId, RpcParams rpcParams = default)
        {
            if (roundId != _builtRoundId)
                return;
            foreach (var p in positions)
                Explosion.ApplyLocal(p, false);
            LogTerrainChecksum($"history x{positions.Length}");
        }

        // ---------- игроки ----------

        void OnConnectionEvent(NetworkManager nm, ConnectionEventData data)
        {
            if (data.EventType != ConnectionEvent.ClientConnected)
                return;
            SpawnPlayerFor(data.ClientId);
            if (data.ClientId != NetworkManager.ServerClientId && _history.Count > 0)
                HistoryRpc(_history.ToArray(), RoundId, RpcTarget.Single(data.ClientId, RpcTargetUse.Temp));
        }

        void SpawnPlayerFor(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || client.PlayerObject != null)
                return;
            int team = PickTeam();
            var player = CreatePlayer(team, NetworkBootstrap.GetPlayerName(clientId), false);
            player.NetworkObject.SpawnAsPlayerObject(clientId, true);
        }

        /// <summary>Автобаланс: в команду, где меньше игроков.</summary>
        static int PickTeam()
        {
            int a = 0, b = 0;
            foreach (var p in PlayerHealth.All)
                if (p.Team == 0) a++;
                else b++;
            return b < a ? 1 : 0;
        }

        PlayerHealth CreatePlayer(int team, string displayName, bool bot)
        {
            var prefab = NetworkBootstrap.Instance.playerPrefab;
            var go = Instantiate(prefab, _islands[team].GetRandomSpawnPoint(1f), FacingEnemy(team));
            var health = go.GetComponent<PlayerHealth>();
            health.ServerInit(team, displayName);
            go.GetComponent<NetworkPlayer>().ServerInit(bot);
            return health;
        }

        void SpawnBots()
        {
            var host = PlayerHealth.All.Find(p => p.OwnerClientId == NetworkManager.ServerClientId && !p.GetComponent<NetworkPlayer>().IsBot);
            int myTeam = host != null ? host.Team : 0;

            var ally = CreatePlayer(myTeam, "Ally (bot)", true);
            if (host != null)
                ally.transform.position = host.transform.position + host.transform.forward * 2f + Vector3.up * 0.5f;
            ally.NetworkObject.Spawn(true);
            ally.TakeDamage(new DamageInfo(1000f, ally.transform.position, null, -1, DamageKind.Explosion));

            for (int i = 0; i < 2; i++)
                CreatePlayer(1 - myTeam, $"Enemy{i + 1} (bot)", true).NetworkObject.Spawn(true);
        }
    }
}
