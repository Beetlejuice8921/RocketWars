using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
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
    /// • ящики, сброшенные с неба посреди раунда, — NetworkList&lt;CrateDrop&gt; (у всех создаются в том же порядке);
    /// • спавн игроков с автобалансом команд, тестовые боты;
    /// • стрим-комната: очередь зрителей и смена игроков после матча (MatchNet.Stream.cs).
    /// </summary>
    public partial class MatchNet : NetworkBehaviour
    {
        public static MatchNet Instance { get; private set; }

        public float islandGap = 45f;

        readonly NetworkVariable<RoundInfo> _round = new NetworkVariable<RoundInfo>();
        NetworkList<SiloSync> _siloSync;
        NetworkList<CrateSync> _crateSync;
        NetworkList<FlagSync> _flagSync;
        NetworkList<CrateDrop> _crateDrops;
        int _droppedCrates; // сколько ящиков из _crateDrops этого раунда уже создано

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

        /// <summary>Как часто сервер сверяет численность команд и добавляет/убирает ботов-балансиров.</summary>
        public const float BotBalanceInterval = 15f;

        /// <summary>Минимальный размер команды: недостающих добивают боты (по умолчанию матч 2×2).</summary>
        public const int MinTeamSize = 2;

        readonly List<PlayerHealth> _balanceBots = new List<PlayerHealth>();
        readonly List<NetworkPlayer> _switchQueue = new List<NetworkPlayer>(); // запросы смены команды во время матча
        float _botBalanceTimer = 1f; // первая проверка — сразу после старта хоста
        int _botCounter;

        /// <summary>Сервер: новый раунд построен (для MatchManager на этапе 4).</summary>
        public event Action<int> RoundStarted;

        void Awake()
        {
            Instance = this;
            _siloSync = new NetworkList<SiloSync>();
            _crateSync = new NetworkList<CrateSync>();
            _flagSync = new NetworkList<FlagSync>();
            _crateDrops = new NetworkList<CrateDrop>();
            _queue = new NetworkList<QueueEntry>();

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
            _crateDrops.Clear();
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
                    np.ServerTeleport(_islands[p.Team].GetRandomSpawnPoint(1f), FacingEnemy(p.Team));
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
            _droppedCrates = 0;
            foreach (var flag in _flags)
                if (flag != null)
                    Destroy(flag.gameObject);
            _flags.Clear();

            // земля по кругу: каждый следующий раунд — на другой
            var biome = Biomes.ForRound(round.Id);
            Biomes.Current = biome;

            // ракеты прошлого раунда не должны долететь до новых островов
            foreach (var rocket in FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                Destroy(rocket.gameObject);

            for (int team = 0; team < _islands.Count; team++)
            {
                _islands[team].Build(team, round.Seed + team * 7919, biome);
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
                UpdateStream();
                UpdateBotBalance();
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
                CreateDroppedCrates();
                for (int i = 0; i < _silos.Count && i < _siloSync.Count; i++)
                    _silos[i].ApplySync(_siloSync[i]);
                for (int i = 0; i < _crates.Count && i < _crateSync.Count; i++)
                    _crates[i].ApplySync(_crateSync[i]);
                for (int i = 0; i < _flags.Count && i < _flagSync.Count; i++)
                    _flags[i].ApplySync(_flagSync[i]);
            }
        }

        // ---------- сброс ящиков ----------

        /// <summary>Сервер: на каждый остров с неба опускаются ещё count ящиков (в случайные места, подальше от стоящих).</summary>
        public void ServerAirDrop(int count = CratePlacer.CratesPerIsland)
        {
            if (!IsServer || _builtRoundId == 0)
                return;
            var rng = new System.Random(Random.Range(1, int.MaxValue));
            double start = NetClock.Now;
            for (int team = 0; team < _islands.Count; team++)
            {
                var island = _islands[team];
                var avoid = new List<Vector2>();
                foreach (var crate in _crates)
                    if (crate != null && crate.Island == island && crate.State != CrateState.Destroyed)
                        avoid.Add(crate.Cell);
                var silos = _silos.FindAll(s => s != null && s.Island == island);

                foreach (var spot in CratePlacer.Pick(island, rng, silos, count, 10f, avoid))
                {
                    var drop = new CrateDrop
                    {
                        RoundId = _builtRoundId, StartTime = start, Team = (byte)team,
                        X = (short)spot.X, Z = (short)spot.Z, Kind = (byte)spot.Kind, YawStep = (byte)spot.YawStep
                    };
                    var crate = SpawnDroppedCrate(drop);
                    _crateDrops.Add(drop);
                    _crateSync.Add(crate.ToSync());
                    _droppedCrates++;
                }
            }
            Debug.Log($"[RocketWars] Air drop: {_droppedCrates} crates, round {_builtRoundId}");
        }

        /// <summary>Клиент: создать сброшенные ящики, о которых ещё не знаем (в том же порядке, что на сервере, — те же NetIndex).</summary>
        void CreateDroppedCrates()
        {
            int k = 0;
            foreach (var drop in _crateDrops)
            {
                if (drop.RoundId != _builtRoundId)
                    continue; // запись прошлого раунда, список ещё не очистился
                if (k++ >= _droppedCrates)
                {
                    SpawnDroppedCrate(drop);
                    _droppedCrates++;
                }
            }
        }

        LootCrate SpawnDroppedCrate(in CrateDrop drop)
        {
            var crate = LootCrate.CreateAirDrop(_islands[drop.Team], drop.X, drop.Z, (LootKind)drop.Kind, drop.YawStep * 90f,
                _worldRoot, drop.StartTime);
            crate.NetIndex = _crates.Count;
            crate.IsAuthority = IsServer;
            _crates.Add(crate);
            return crate;
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
            if (data.EventType == ConnectionEvent.ClientDisconnected)
                OnStreamClientLeft(data.ClientId);
            if (data.EventType != ConnectionEvent.ClientConnected)
                return;
            // стрим-комната: забаненный или вход закрыт — отключаем с причиной (MatchNet.Stream)
            if (StreamHost.TakeRejection(data.ClientId) is string rejection)
            {
                NetworkManager.DisconnectClient(data.ClientId, rejection);
                return;
            }
            SpawnPlayerFor(data.ClientId);
            if (data.ClientId != NetworkManager.ServerClientId && _history.Count > 0)
                HistoryRpc(_history.ToArray(), RoundId, RpcTarget.Single(data.ClientId, RpcTargetUse.Temp));
        }

        void SpawnPlayerFor(ulong clientId)
        {
            // стрим-комната: зрители встают в очередь, в игру их пускает UpdateStream, когда есть место
            if (StreamHost.Enabled && clientId != NetworkManager.ServerClientId)
            {
                Enqueue(clientId);
                return;
            }
            SpawnPlayerNow(clientId);
        }

        bool SpawnPlayerNow(ulong clientId)
        {
            if (!NetworkManager.ConnectedClients.TryGetValue(clientId, out var client) || HasPlayer(client))
                return false;
            int team = PickTeam();
            var player = CreatePlayer(team, NetworkBootstrap.GetPlayerName(clientId), false);
            player.NetworkObject.SpawnAsPlayerObject(clientId, true);
            return true;
        }

        /// <summary>Автобаланс: в команду, где меньше живых игроков (боты-балансиры потом уйдут сами).</summary>
        static int PickTeam()
        {
            CountHumans(out int a, out int b);
            return b < a ? 1 : 0;
        }

        static void CountHumans(out int team0, out int team1)
        {
            team0 = team1 = 0;
            foreach (var p in PlayerHealth.All)
            {
                if (!p.IsSpawned || p.GetComponent<NetworkPlayer>().IsBot)
                    continue;
                if (p.Team == 0) team0++;
                else team1++;
            }
        }

        // ---------- смена команды ----------

        /// <summary>
        /// Почему игрок не может перейти в другую команду прямо сейчас (null — может). Считается и на клиенте
        /// (кнопка в паузе), и на сервере: только в лобби или после матча, не на ракете, и люди в командах
        /// после перехода различаются не больше чем на одного. Во время матча вместо этого — запрос (QueueTeamSwitch).
        /// </summary>
        public static string TeamSwitchBlocker(PlayerHealth player)
        {
            var match = MatchManager.Instance;
            if (player == null || !player.IsSpawned || match == null || !match.CanStartMatch)
                return Loc.T("Команду можно сменить только в лобби или после матча");
            if (player.GetComponent<NetworkPlayer>().IsRiding)
                return Loc.T("Сейчас нельзя сменить команду");
            return Unbalanced(player) ? Loc.T("Команды станут неравными") : null;
        }

        static bool Unbalanced(PlayerHealth player)
        {
            CountHumans(out int h0, out int h1);
            int from = player.Team == 0 ? h0 : h1, to = player.Team == 0 ? h1 : h0;
            return Mathf.Abs(to + 1 - (from - 1)) > 1;
        }

        /// <summary>
        /// Сервер, кнопка «Перейти в команду»: в лобби и после матча — сразу; во время матча — запрос (повторное
        /// нажатие отменяет), который выполнится, когда матч закончится.
        /// </summary>
        public void ServerRequestTeamSwitch(NetworkPlayer player)
        {
            if (!IsServer || player == null || MatchManager.Instance == null)
                return;
            if (!MatchManager.Instance.CanStartMatch)
            {
                bool queued = !player.TeamSwitchQueued;
                player.ServerSetTeamSwitchQueued(queued);
                _switchQueue.Remove(player);
                if (queued)
                    _switchQueue.Add(player);
                return;
            }
            var health = player.GetComponent<PlayerHealth>();
            if (TeamSwitchBlocker(health) == null)
                ApplyTeamSwitch(health);
        }

        /// <summary>
        /// Сервер, каждый кадр после матча: выполнить запросы, сделанные во время матча, в порядке очереди.
        /// Игроки из разных команд меняются местами парами (баланс не меняется), остальные — пока команды
        /// не станут неравными; кому не хватило места, запрос снимается. Сидящий на ракете ждёт, пока слезет.
        /// </summary>
        public void ServerApplyQueuedSwitches()
        {
            _switchQueue.RemoveAll(p => p == null || !p.IsSpawned || !p.TeamSwitchQueued);
            if (_switchQueue.Count == 0)
                return;
            var ready = _switchQueue.FindAll(p => !p.IsRiding);
            var from0 = ready.FindAll(p => p.Health.Team == 0);
            var from1 = ready.FindAll(p => p.Health.Team == 1);
            int pairs = Mathf.Min(from0.Count, from1.Count);
            for (int i = 0; i < pairs; i++)
            {
                ApplyTeamSwitch(from0[i].Health);
                ApplyTeamSwitch(from1[i].Health);
            }
            foreach (var p in ready)
            {
                if (!p.TeamSwitchQueued)
                    continue;
                if (Unbalanced(p.Health))
                {
                    p.ServerSetTeamSwitchQueued(false);
                    Debug.Log($"[RocketWars] {p.Health.DisplayName}: queued team switch dropped, teams would be uneven");
                }
                else
                {
                    ApplyTeamSwitch(p.Health);
                }
            }
        }

        /// <summary>Сервер: перевести игрока в другую команду — полное HP, без лута и берсерка, на свой новый остров.</summary>
        void ApplyTeamSwitch(PlayerHealth player)
        {
            var np = player.GetComponent<NetworkPlayer>();
            np.ServerSetTeamSwitchQueued(false);
            _switchQueue.Remove(np);
            // чужой флаг на спине возвращается домой — в новой команде он стал бы своим
            foreach (var flag in _flags)
                if (flag != null && flag.State == FlagState.Carried && flag.CarrierId == player.NetworkObjectId)
                    flag.ServerReturnHome();
            int team = 1 - player.Team;
            player.ServerSetTeam(team);
            player.ResetFull();
            np.ServerResetLoadout();
            np.ServerTeleport(_islands[team].GetRandomSpawnPoint(1f), FacingEnemy(team));
            _botBalanceTimer = 0f; // боты-балансиры переставляются сразу, а не через 15 с
            Debug.Log($"[RocketWars] {player.DisplayName} switched to team {team}");
        }

        // ---------- боты-балансиры ----------

        /// <summary>
        /// Сервер, не чаще раза в 15 с: каждая команда добивается ботами до размера
        /// max(<see cref="MinTeamSize"/>, людей в большей команде) — один человек играет 2×2
        /// (он + бот против двух ботов). Как только людей хватает, лишние боты уходят.
        /// В режиме тестовых ботов (разработка) балансир выключен.
        /// </summary>
        void UpdateBotBalance()
        {
            _botBalanceTimer -= Time.deltaTime;
            if (_botBalanceTimer > 0f || SpawnTestBots)
                return;
            _botBalanceTimer = BotBalanceInterval;

            _balanceBots.RemoveAll(b => b == null || !b.IsSpawned);
            CountHumans(out int h0, out int h1);
            int size = h0 == 0 && h1 == 0 ? 0 : Mathf.Max(MinTeamSize, h0, h1);
            int need0 = size - h0, need1 = size - h1;

            // лишние — уходят (сначала лежащие: их уход не решает исход раунда)
            int keep0 = 0, keep1 = 0;
            _balanceBots.Sort((a, b) => b.IsDowned.CompareTo(a.IsDowned));
            for (int i = _balanceBots.Count - 1; i >= 0; i--)
            {
                var bot = _balanceBots[i];
                if (bot.Team == 0 && keep0 < need0) { keep0++; continue; }
                if (bot.Team == 1 && keep1 < need1) { keep1++; continue; }
                Debug.Log($"[RocketWars] balance bot {bot.DisplayName} leaves (humans {h0}:{h1})");
                _balanceBots.RemoveAt(i);
                bot.NetworkObject.Despawn(true);
            }

            for (int i = keep0; i < need0; i++)
                SpawnBalanceBot(0, h0, h1);
            for (int i = keep1; i < need1; i++)
                SpawnBalanceBot(1, h0, h1);
        }

        void SpawnBalanceBot(int team, int h0, int h1)
        {
            var role = BotBrain.PickRole(team); // до создания: новый бот не должен считаться сам
            var bot = CreatePlayer(team, $"Bot {++_botCounter}", true);
            bot.gameObject.AddComponent<BotBrain>().Role = role;
            bot.NetworkObject.Spawn(true);
            _balanceBots.Add(bot);
            Debug.Log($"[RocketWars] balance bot {bot.DisplayName} joins team {team} (humans {h0}:{h1})");
        }

        PlayerHealth CreatePlayer(int team, string displayName, bool bot)
        {
            var prefab = NetworkBootstrap.Instance.playerPrefab;
            var go = Instantiate(prefab, _islands[team].GetRandomSpawnPoint(1f), FacingEnemy(team));
            var health = go.GetComponent<PlayerHealth>();
            // боты — в случайном скине; игрок свой скин ставит сам (PlayerHealth.Skin пишет владелец)
            health.ServerInit(team, displayName, bot ? Skins.RandomForBot() : -1);
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
