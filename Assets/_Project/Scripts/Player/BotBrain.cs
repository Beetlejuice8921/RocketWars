using System.Collections.Generic;
using RocketWars.Core;
using RocketWars.Flags;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>Характер бота: меняет только то, чем он охотнее занимается, а не меткость и реакцию.</summary>
    public enum BotRole : byte
    {
        /// <summary>Чаще всех у пульта, держится возле шахты, любит снайперку.</summary>
        Artillery,
        /// <summary>Летает на ракетах, носит флаг, лезет на чужой остров, любит базуку.</summary>
        Assault,
        /// <summary>Держится рядом с союзниками и первым бежит поднимать.</summary>
        Medic,
        /// <summary>Собирает ящики, берёт любое оружие, обстреливает шахты из базуки.</summary>
        Scavenger
    }

    /// <summary>
    /// Поведение бота-балансира (только на сервере, бот принадлежит серверу).
    /// «Руки» и «ноги» работают раздельно: руки стреляют по видимому врагу, ноги уклоняются от ракет,
    /// а остальное время бот занят одним делом. Дело выбирается раз в 1–2,5 с по очкам полезности:
    /// ситуация × вес характера (<see cref="BotRole"/>) × немного случайности, текущее дело получает надбавку,
    /// чтобы бот не метался. Дела: поднять союзника, ящик, пульт шахты (пуск по врагу, иногда — с полётом верхом),
    /// флаг (забрать, увезти домой на вражеской ракете), охота на врага на своём острове, обстрел шахты из базуки,
    /// держаться рядом с союзником, осмотреться в бинокль, патруль.
    /// Меткость у всех характеров одна (30%); действия идут через те же запросы NetworkPlayer, что и у живого игрока.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer))]
    public partial class BotBrain : MonoBehaviour
    {
        const float ThinkMin = 1.2f, ThinkMax = 2.5f;
        /// <summary>Надбавка текущему делу при выборе — чтобы бот не бросал начатое из-за мелкой разницы в очках.</summary>
        const float KeepBonus = 1.3f;
        const float WoundedHp = 70f;            // ниже — идёт лечиться аптечкой
        const float CrateReach = 1.3f;
        const float ReviveReach = 1.6f;
        const float BlacklistTime = 30f;

        enum BotTask
        {
            None,
            Patrol,
            Follow,
            Lookout,
            Revive,
            Loot,
            Artillery,
            Ride,
            TakeFlag,
            CarryFlag,
            Hunt,
            Bombard
        }

        /// <summary>Веса дел для характера (1 — обычный интерес).</summary>
        struct Traits
        {
            public float Artillery, Ride, Revive, Loot, Flag, Hunt, Follow, Lively;
            /// <summary>С какой вероятностью после своего пуска садится на ракету.</summary>
            public float RideChance;
            public bool TakesSniper, TakesBazooka;

            public static Traits For(BotRole role)
            {
                switch (role)
                {
                    case BotRole.Artillery:
                        return new Traits { Artillery = 2.2f, Ride = 0.4f, RideChance = 0.1f, Revive = 1f, Loot = 0.7f, Flag = 0.5f,
                            Hunt = 0.7f, Follow = 0.3f, Lively = 0.6f, TakesSniper = true };
                    case BotRole.Assault:
                        return new Traits { Artillery = 0.9f, Ride = 1.6f, RideChance = 0.6f, Revive = 0.7f, Loot = 0.8f, Flag = 1.8f,
                            Hunt = 1.5f, Follow = 0.2f, Lively = 1.2f, TakesBazooka = true };
                    case BotRole.Medic:
                        return new Traits { Artillery = 0.7f, Ride = 0.3f, RideChance = 0.1f, Revive = 2.2f, Loot = 1f, Flag = 0.6f,
                            Hunt = 0.6f, Follow = 1.5f, Lively = 0.8f };
                    default:
                        return new Traits { Artillery = 0.8f, Ride = 0.6f, RideChance = 0.25f, Revive = 0.9f, Loot = 2.2f, Flag = 0.8f,
                            Hunt = 1f, Follow = 0.3f, Lively = 1f, TakesSniper = true, TakesBazooka = true };
                }
            }
        }

        /// <summary>Характер; ставится сразу после AddComponent (до первого кадра).</summary>
        public BotRole Role { get; set; }

        /// <summary>Сервер: характер для нового бота команды — тот, которого в ней пока меньше всего (при равенстве — случайный).</summary>
        public static BotRole PickRole(int team)
        {
            var counts = new int[4];
            foreach (var p in PlayerHealth.All)
                if (p != null && p.Team == team && p.TryGetComponent(out BotBrain brain))
                    counts[(int)brain.Role]++;
            int min = Mathf.Min(Mathf.Min(counts[0], counts[1]), Mathf.Min(counts[2], counts[3]));
            var options = new List<BotRole>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] == min)
                    options.Add((BotRole)i);
            return options[Random.Range(0, options.Count)];
        }

        NetworkPlayer _net;
        PlayerHealth _health;
        FpsController _fps;
        Traits _traits;

        // текущее дело
        BotTask _task;
        float _thinkTimer, _taskTime;
        PlayerHealth _taskPlayer;      // кого поднимаем / за кем идём / на кого охотимся
        LootCrate _taskCrate;
        RocketSilo _taskSilo;          // пульт, шахта для полёта или цель базуки
        Vector3 _taskPoint;
        float _lookoutCooldown, _artilleryCooldown, _bombardCooldown;

        /// <summary>Остров под ногами (null — над пропастью).</summary>
        VoxelIsland _here;

        // удержание «E» (подъём / открытие ящика)
        Object _holdTarget;
        float _holdProgress;

        // патруль, «живость» и застревание
        Vector3? _patrolPoint;
        float _patrolTimer, _pauseTimer, _lookTimer;
        Quaternion _lookAround;
        Vector3 _stuckCheckPos;
        float _stuckWindow, _stuckTime;
        bool _blocked;
        readonly Dictionary<Object, float> _blacklist = new Dictionary<Object, float>();

        void Awake()
        {
            _net = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();
            _fps = GetComponent<FpsController>();
        }

        void Start()
        {
            _traits = Traits.For(Role);
            _thinkTimer = Random.Range(0.2f, 1f);
            _artilleryCooldown = Random.Range(3f, 12f) / _traits.Artillery; // в начале раунда к пультам бегут не все разом
            Debug.Log($"[RocketWars] bot {_health.DisplayName} is {Role}");
        }

        void OnEnable() => _health.Hurt += OnHurt;

        void OnDisable()
        {
            _health.Hurt -= OnHurt;
            ReleaseConsole();
            if (_rideSilo >= 0)
                ReleaseRide();
        }

        /// <summary>Урон сбивает подъём и открытие ящика — как у игрока.</summary>
        void OnHurt(float amount) => _holdProgress = 0f;

        VoxelIsland MyIsland => VoxelIsland.ForTeam(_health.Team);
        VoxelIsland EnemyIsland => VoxelIsland.ForTeam(1 - _health.Team);
        Vector3 Eye => _fps.CameraRoot.position;
        bool OnEnemyIsland => _here != null && _here.Team != _health.Team;

        void Update()
        {
            if (!_net.IsSpawned || !_net.IsServer)
                return;

            _fps.MovementLocked = MatchManager.FreezePlayers;
            bool active = MatchManager.Instance != null && MatchManager.Instance.Phase == MatchPhase.Round;

            if (_rideSilo >= 0)
            {
                UpdateRide(active);
                return;
            }

            if (!active || _health.IsDowned || MyIsland == null || _fps.Attached)
            {
                ResetAll();
                return;
            }

            _here = IslandUnder(transform.position);
            float dt = Time.deltaTime;
            _lookoutCooldown -= dt;
            _artilleryCooldown -= dt;
            _bombardCooldown -= dt;
            _taskTime += dt;

            UpdateCombat();
            if (Dodge())
                return;

            // над пропастью (сошли с ракеты) — рулим парашютом к ближайшему острову
            if (_here == null)
            {
                SteerInAir();
                return;
            }

            _thinkTimer -= dt;
            if (_thinkTimer <= 0f || _task == BotTask.None)
                Think();

            if (_enemy != null && !IgnoresEnemies())
            {
                CombatMove();
                return;
            }
            _fps.BotCrouch = false;

            if (!RunTask())
            {
                EndTask();
                _thinkTimer = 0f;
            }
        }

        void ResetAll()
        {
            Stop();
            _fps.BotCrouch = false;
            _net.SetBotOptics(false, false);
            _enemy = null;
            _holdProgress = 0f;
            _patrolPoint = null;
            EndTask();
        }

        // ---------- выбор дела ----------

        void Think()
        {
            _thinkTimer = Random.Range(ThinkMin, ThinkMax);
            // бежим к ракете под сиреной — 2,5 с, не передумываем
            if (_task == BotTask.Ride && _taskSilo != null && _taskSilo.State == SiloState.Launching)
                return;

            var best = BotTask.Patrol;
            float bestScore = 0.3f;
            PlayerHealth bestPlayer = null;
            LootCrate bestCrate = null;
            RocketSilo bestSilo = null;
            Vector3 bestPoint = default;

            void Consider(BotTask task, float score, PlayerHealth player = null, LootCrate crate = null, RocketSilo silo = null, Vector3 point = default)
            {
                if (score <= 0f)
                    return;
                score *= Random.Range(0.85f, 1.15f);
                if (task == _task)
                    score *= KeepBonus;
                if (score <= bestScore)
                    return;
                best = task;
                bestScore = score;
                bestPlayer = player;
                bestCrate = crate;
                bestSilo = silo;
                bestPoint = point;
            }

            // несём вражеский флаг — важнее всего, остальное подождёт
            if (CarriedFlag() != null)
                Consider(BotTask.CarryFlag, 100f);

            Consider(BotTask.Revive, ScoreRevive(out var ally), player: ally);
            Consider(BotTask.Loot, ScoreLoot(out var crate), crate: crate);
            Consider(BotTask.Artillery, ScoreArtillery(out var console), silo: console);
            Consider(BotTask.Ride, ScoreRide(out var launching), silo: launching);
            Consider(BotTask.TakeFlag, ScoreTakeFlag());
            Consider(BotTask.Hunt, ScoreHunt(out var prey), player: prey);
            Consider(BotTask.Bombard, ScoreBombard(out var enemySilo), silo: enemySilo);
            Consider(BotTask.Follow, ScoreFollow(out var friend), player: friend);
            Consider(BotTask.Lookout, ScoreLookout(out var spot), point: spot);

            if (best == _task)
            {
                // то же дело: ящик, союзника, добычу можно сменить на более близкие, остальное не бросаем на полпути
                if (_task == BotTask.Revive || _task == BotTask.Loot || _task == BotTask.Hunt || _task == BotTask.Follow)
                {
                    _taskPlayer = bestPlayer;
                    _taskCrate = bestCrate;
                }
                return;
            }

            EndTask();
            _task = best;
            _taskTime = 0f;
            _taskPlayer = bestPlayer;
            _taskCrate = bestCrate;
            _taskSilo = bestSilo;
            _taskPoint = bestPoint;
            if (_task == BotTask.Artillery || _task == BotTask.CarryFlag)
                BeginConsole(_task == BotTask.CarryFlag);
        }

        bool RunTask()
        {
            switch (_task)
            {
                case BotTask.Revive: return RunRevive();
                case BotTask.Loot: return RunLoot();
                case BotTask.Artillery: return RunConsole();
                case BotTask.CarryFlag: return RunCarryFlag();
                case BotTask.Ride: return RunRideApproach();
                case BotTask.TakeFlag: return RunTakeFlag();
                case BotTask.Hunt: return RunHunt();
                case BotTask.Bombard: return RunBombard();
                case BotTask.Follow: return RunFollow();
                case BotTask.Lookout: return RunLookout();
                default:
                    Patrol();
                    return true;
            }
        }

        /// <summary>Дела, которые бот не бросает, даже когда видит врага (стреляет на ходу).</summary>
        bool IgnoresEnemies()
        {
            switch (_task)
            {
                case BotTask.CarryFlag:
                case BotTask.Ride:
                    return true;
                case BotTask.Artillery:
                    return _consoleStep >= ConsoleStep.Think; // уже целимся — доводим пуск (пока ждём перезарядку — отстреливаемся)
                case BotTask.TakeFlag:
                    return _traits.Flag > 1f;
                default:
                    return false;
            }
        }

        void EndTask()
        {
            if (_task == BotTask.Lookout)
                _net.SetBotOptics(false, false);
            if (_task == BotTask.Artillery || _task == BotTask.CarryFlag)
                ReleaseConsole();
            _task = BotTask.None;
            _taskPlayer = null;
            _taskCrate = null;
            _taskSilo = null;
        }

        // ---------- над пропастью ----------

        void SteerInAir()
        {
            if (_fps.IsGrounded)
            {
                Stop();
                return;
            }
            // к ближайшему острову (обычно — тому, куда летела ракета)
            VoxelIsland nearest = null;
            float best = float.MaxValue;
            foreach (var island in VoxelIsland.All)
            {
                float d = Flat(island.Center - transform.position).magnitude;
                if (d < best)
                {
                    best = d;
                    nearest = island;
                }
            }
            if (nearest != null)
            {
                _fps.BotMove = Flat(nearest.Center - transform.position).normalized;
                _fps.BotSprint = false;
                if (_enemy == null)
                    Face(_fps.BotMove);
            }
        }

        // ---------- патруль ----------

        void Patrol()
        {
            // «живость»: иногда останавливается и оглядывается
            if (_pauseTimer > 0f)
            {
                _pauseTimer -= Time.deltaTime;
                Stop();
                _lookTimer -= Time.deltaTime;
                if (_lookTimer <= 0f)
                {
                    _lookTimer = Random.Range(0.6f, 1.4f);
                    _lookAround = Quaternion.Euler(0f, Random.Range(-120f, 120f), 0f) * transform.rotation;
                }
                transform.rotation = Quaternion.RotateTowards(transform.rotation, _lookAround, 120f * Time.deltaTime);
                _fps.SetPitch(Mathf.Sin(Time.time * 0.7f) * 6f);
                return;
            }

            _patrolTimer -= Time.deltaTime;
            if (_patrolPoint == null || _patrolTimer <= 0f || _blocked)
            {
                _patrolPoint = _here.GetRandomSpawnPoint(0f);
                _patrolTimer = 20f;
                _blocked = false;
            }
            if (MoveTo(_patrolPoint.Value, false, 1.5f))
            {
                _patrolPoint = null; // дошли — следующая точка
                if (Random.value < 0.5f * _traits.Lively)
                {
                    _pauseTimer = Random.Range(1f, 3f);
                    _lookTimer = 0f;
                }
            }
            else if (Random.value < 0.004f * _traits.Lively)
            {
                _fps.BotJump(); // просто так подпрыгнул на бегу
            }
        }

        // ---------- движение ----------

        /// <summary>Идти к точке; true — уже рядом. false с _blocked — путь не найден (край, застряли).</summary>
        bool MoveTo(Vector3 point, bool sprint, float stopDistance)
        {
            Vector3 to = Flat(point - transform.position);
            if (to.magnitude <= stopDistance)
            {
                Stop();
                return true;
            }

            Vector3 dir = to.normalized;
            if (!GroundAhead(dir))
            {
                // обход ямы или края: пробуем отклониться
                bool found = false;
                foreach (float turn in new[] { 35f, -35f, 70f, -70f })
                {
                    Vector3 alt = Quaternion.Euler(0f, turn, 0f) * dir;
                    if (GroundAhead(alt))
                    {
                        dir = alt;
                        found = true;
                        break;
                    }
                }
                if (!found)
                {
                    Stop();
                    _blocked = true;
                    return false;
                }
            }
            Drive(dir, sprint);
            return !_blocked;
        }

        void Drive(Vector3 dir, bool sprint)
        {
            _fps.BotMove = dir;
            _fps.BotSprint = sprint;
            if (_enemy == null)
            {
                Face(dir);
                _fps.SetPitch(0f);
            }
            CheckStuck();
        }

        void Stop()
        {
            _fps.BotMove = Vector3.zero;
            _fps.BotSprint = false;
            _stuckWindow = 0f;
            _stuckTime = 0f;
            _stuckCheckPos = transform.position;
        }

        /// <summary>Каждые 0,5 с: если почти не сдвинулись — прыгаем; застряли больше 3 с — путь закрыт.</summary>
        void CheckStuck()
        {
            _stuckWindow += Time.deltaTime;
            if (_stuckWindow < 0.5f)
                return;
            float moved = Flat(transform.position - _stuckCheckPos).magnitude;
            _stuckCheckPos = transform.position;
            if (moved < 0.4f && _fps.IsGrounded)
            {
                _stuckTime += _stuckWindow;
                _fps.BotCrouch = false;
                _fps.BotJump();
                if (_stuckTime > 3f)
                {
                    _blocked = true;
                    _stuckTime = 0f;
                }
            }
            else
            {
                _stuckTime = 0f;
                _blocked = false;
            }
            _stuckWindow = 0f;
        }

        /// <summary>Есть ли впереди земля не глубже ~2,5 м (иначе край острова или глубокая воронка).</summary>
        bool GroundAhead(Vector3 dir)
        {
            if (!_fps.IsGrounded)
                return true; // в воздухе не выбираем
            foreach (float dist in new[] { 1f, 2f })
            {
                Vector3 origin = transform.position + dir * dist + Vector3.up * 1.5f;
                if (!Physics.Raycast(origin, Vector3.down, 4.1f, ~0, QueryTriggerInteraction.Ignore))
                    return false;
            }
            return true;
        }

        /// <summary>Довернуть корпус (рысканье) и взгляд (тангаж) к направлению; возвращает оставшийся угол.</summary>
        float Face(Vector3 dir)
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 1e-4f)
            {
                var want = Quaternion.LookRotation(flat);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, want, TurnSpeed * Time.deltaTime);
            }
            float pitch = -Mathf.Atan2(dir.y, Mathf.Max(0.01f, flat.magnitude)) * Mathf.Rad2Deg;
            _fps.SetPitch(pitch);
            return Vector3.Angle(transform.forward, flat);
        }

        // ---------- удержание «E» ----------

        /// <summary>Копит время удержания на цели; true — время вышло (действие выполнено, счётчик сброшен).</summary>
        bool Hold(Object target, float duration)
        {
            ResetHold(target);
            _holdProgress += Time.deltaTime;
            if (_holdProgress < duration)
                return false;
            _holdProgress = 0f;
            return true;
        }

        void ResetHold(Object target)
        {
            if (_holdTarget == target)
                return;
            _holdTarget = target;
            _holdProgress = 0f;
        }

        // ---------- недостижимые цели ----------

        bool IsBlacklisted(Object target) => target != null && _blacklist.TryGetValue(target, out float until) && Time.time < until;

        void Blacklist(Object target)
        {
            if (!_blocked)
                return;
            _blacklist[target] = Time.time + BlacklistTime;
            _blocked = false;
        }

        // ---------- общее ----------

        /// <summary>Остров, над которым точка (по плану, без высоты); null — пропасть.</summary>
        static VoxelIsland IslandUnder(Vector3 p)
        {
            foreach (var island in VoxelIsland.All)
            {
                var b = island.WorldBounds;
                if (p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z)
                    return island;
            }
            return null;
        }

        /// <summary>Точка внутри острова не ближе margin к краю его прямоугольника.</summary>
        static bool InInterior(VoxelIsland island, Vector3 p, float margin)
        {
            var b = island.WorldBounds;
            return p.x >= b.min.x + margin && p.x <= b.max.x - margin && p.z >= b.min.z + margin && p.z <= b.max.z - margin;
        }

        /// <summary>Флаг противника, который несёт этот бот (null — не несёт).</summary>
        TeamFlag CarriedFlag()
        {
            var flag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(1 - _health.Team) : null;
            return flag != null && flag.State == FlagState.Carried && flag.CarrierId == _health.NetworkObjectId ? flag : null;
        }

        static Vector3 Flat(Vector3 v) => new Vector3(v.x, 0f, v.z);
    }
}
