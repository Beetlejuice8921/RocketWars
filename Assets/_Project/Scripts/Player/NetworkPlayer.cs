using System;
using RocketWars.Combat;
using RocketWars.Flags;
using RocketWars.Core;
using RocketWars.Launcher;
using RocketWars.Loot;
using RocketWars.Net;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>Действие с удержанием E, время которого проверяет сервер.</summary>
    public enum HoldKind : byte
    {
        None,
        Revive,
        Crate
    }

    /// <summary>
    /// Сетевой «хаб» игрока: кто им управляет (владелец / бот), синхронизация приседа и
    /// все запросы клиента к серверу (падение, подъём союзника, кнопки пульта, выстрелы, ящики с припасами).
    /// Движение — у владельца (NetworkTransform в режиме Owner), всё остальное решает сервер.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth), typeof(FpsController))]
    public class NetworkPlayer : NetworkBehaviour
    {
        const float ReviveRangeSlack = 1.5f;

        /// <summary>Жёсткое приземление: до 5 м без урона, дальше линейно до −80 HP с 25 м.</summary>
        public const float SafeFallHeight = 5f;
        public const float MaxDamageFallHeight = 25f;
        public const float MaxFallDamage = 80f;
        const float ConsoleRange = 5f;

        public static NetworkPlayer LocalPlayer { get; private set; }

        public const float BeaconDuration = 15f;
        /// <summary>Сервер проверяет дистанцию до ящика с запасом на задержку.</summary>
        const float CrateRangeSlack = 1.5f;

        readonly NetworkVariable<bool> _isBot = new NetworkVariable<bool>();
        readonly NetworkVariable<bool> _crouch = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Игрок попросил перейти в другую команду, когда закончится матч (ставит сервер).</summary>
        readonly NetworkVariable<bool> _teamSwitchQueued = new NetworkVariable<bool>();

        public bool IsBot => _isBot.Value;
        public bool TeamSwitchQueued => IsSpawned && _teamSwitchQueued.Value;
        public bool IsLocalHuman => IsOwner && !IsBot;
        public bool IsCrouching => _crouch.Value;

        readonly NetworkVariable<bool> _binoculars = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Игрок смотрит в бинокль (видят все).</summary>
        public bool UsingBinoculars => IsSpawned && _binoculars.Value;
        /// <summary>Владелец: поднял/опустил бинокль (ставит компонент Binoculars).</summary>
        public bool LocalBinoculars { get; set; }

        /// <summary>Снайпер смотрит в оптику: видят все (враги — блик), стрелять в прицел можно.</summary>
        readonly NetworkVariable<bool> _sniperAim = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        /// <summary>Владелец: оптика снайперки у глаз (ставит Binoculars).</summary>
        public bool LocalSniperAim { get; set; }
        public bool SniperAiming => IsSpawned && (IsLocalHuman ? LocalSniperAim : _sniperAim.Value);

        /// <summary>Сервер: бот поднял/опустил бинокль или оптику снайперки (живому игроку их ставит Binoculars).</summary>
        public void SetBotOptics(bool binoculars, bool sniperAim)
        {
            if (!IsBot || !IsOwner || !IsSpawned)
                return;
            if (_binoculars.Value != binoculars) _binoculars.Value = binoculars;
            if (_sniperAim.Value != sniperAim) _sniperAim.Value = sniperAim;
        }

        /// <summary>За ракету какой шахты держится игрок (−1 — ни за какую) и в каком из 4 слотов.</summary>
        readonly NetworkVariable<int> _rideSilo = new NetworkVariable<int>(-1,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<byte> _rideSlot = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
        readonly NetworkVariable<bool> _parachute = new NetworkVariable<bool>(false,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        /// <summary>Трюк на ракете (зажат Q в полёте) — видят все.</summary>
        readonly NetworkVariable<byte> _trick = new NetworkVariable<byte>(0,
            NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        public int RideSilo => IsSpawned ? _rideSilo.Value : -1;
        public int RideSlot => _rideSlot.Value;
        public bool IsRiding => RideSilo >= 0;
        public RideTrick RideTrick => IsRiding ? (RideTrick)_trick.Value : RideTrick.None;

        /// <summary>Владелец: начал / закончил трюк (ставит RocketRider).</summary>
        public void SetTrick(RideTrick trick)
        {
            if (IsOwner && IsSpawned && _trick.Value != (byte)trick)
                _trick.Value = (byte)trick;
        }
        public bool ParachuteOpen => IsSpawned && _parachute.Value;

        // ---------- берсерк (награда за доставленный флаг) ----------

        /// <summary>Начало и конец режима берсерка по общему времени NetClock (−1 — не было). Пишет сервер.</summary>
        readonly NetworkVariable<double> _berserkStart = new NetworkVariable<double>(-1);
        readonly NetworkVariable<double> _berserkEnd = new NetworkVariable<double>(-1);
        float _armorBeforeBerserk;
        bool _berserkReverted = true;
        float? _levitateBaseY;

        double SinceBerserkStart => _berserkStart.Value < 0 ? double.MaxValue : NetClock.Now - _berserkStart.Value;
        /// <summary>Режим берсерка идёт (включая 2 с превращения): золотые пулемёты, бесконечные патроны.</summary>
        public bool IsBerserk => IsSpawned && _berserkStart.Value >= 0 && NetClock.Now >= _berserkStart.Value && NetClock.Now < _berserkEnd.Value;
        /// <summary>Идёт анимация превращения (вид от третьего лица, лучи, вознесение).</summary>
        public bool InBerserkTransform => IsBerserk && SinceBerserkStart < BerserkSpec.TransformTime;
        /// <summary>Доля анимации превращения 0..1 (1 — превращение закончено).</summary>
        public float BerserkTransform01 => IsBerserk ? Mathf.Clamp01((float)(SinceBerserkStart / BerserkSpec.TransformTime)) : 0f;
        public float BerserkTimeLeft => IsBerserk ? (float)(_berserkEnd.Value - NetClock.Now) : 0f;
        /// <summary>Сколько секунд назад режим закончился (для облачков обратного превращения); MaxValue — не заканчивался.</summary>
        public float SinceBerserkEnd => _berserkEnd.Value < 0 || IsBerserk ? float.MaxValue : (float)(NetClock.Now - _berserkEnd.Value);

        /// <summary>Сервер: донёс вражеский флаг до своего острова — превращение в берсерка.</summary>
        void OnFlagDelivered(TeamFlag flag, PlayerHealth carrier)
        {
            if (carrier == _health)
                ServerStartBerserk();
        }

        public void ServerStartBerserk()
        {
            if (!IsServer || _health.IsDowned)
                return;
            if (_berserkReverted)
                _armorBeforeBerserk = _health.Armor;
            _berserkReverted = false;
            double now = NetClock.Now;
            _berserkStart.Value = now;
            _berserkEnd.Value = now + BerserkSpec.TransformTime + BerserkSpec.Duration;
            _health.SetArmor(BerserkSpec.Armor);
            Debug.Log($"[RocketWars] {_health.DisplayName} BERSERK for {BerserkSpec.Duration} s");
        }

        /// <summary>Сервер: режим закончился (по времени, игрока положили, новый раунд) — броня берсерка исчезает.</summary>
        void ServerUpdateBerserk()
        {
            if (_berserkReverted || _berserkStart.Value < 0)
                return;
            if (IsBerserk && !_health.IsDowned)
                return;
            if (NetClock.Now < _berserkEnd.Value)
                _berserkEnd.Value = NetClock.Now; // закончили досрочно
            _health.SetArmor(Mathf.Min(_health.Armor, _armorBeforeBerserk));
            _berserkReverted = true;
        }

        /// <summary>Владелец: на время превращения «возносится» на 2 м над тем местом, где его застало превращение.</summary>
        void UpdateLevitation()
        {
            if (InBerserkTransform)
            {
                if (!_levitateBaseY.HasValue)
                    _levitateBaseY = transform.position.y;
                float t = BerserkTransform01;
                // плавный подъём за первую половину, зависание, в самом конце — отпускаем
                _fps.HoverTarget = t < 0.95f
                    ? _levitateBaseY.Value + BerserkSpec.LevitateHeight * Mathf.SmoothStep(0f, 1f, t / 0.5f)
                    : (float?)null;
            }
            else if (_levitateBaseY.HasValue)
            {
                _levitateBaseY = null;
                _fps.HoverTarget = null;
            }
        }

        /// <summary>Патроны пулемёта из ящика (0 — в руках пистолет). Пишет сервер, видят все; в памяти зашифрованы.</summary>
        readonly NetworkVariable<ObscuredInt> _machineGunAmmo = new NetworkVariable<ObscuredInt>(0);
        public int MachineGunAmmo => _machineGunAmmo.Value;
        public bool HasMachineGun => IsSpawned && _machineGunAmmo.Value > 0;

        /// <summary>Патроны снайперки из ящика; пока они есть, в руках снайперка (она важнее пулемёта).</summary>
        readonly NetworkVariable<ObscuredInt> _sniperAmmo = new NetworkVariable<ObscuredInt>(0);
        public int SniperAmmo => _sniperAmmo.Value;
        public bool HasSniper => IsSpawned && _sniperAmmo.Value > 0;

        /// <summary>Заряды базуки из ящика; пока они есть, в руках базука (важнее снайперки и пулемёта).</summary>
        readonly NetworkVariable<ObscuredInt> _bazookaAmmo = new NetworkVariable<ObscuredInt>(0);
        public int BazookaAmmo => _bazookaAmmo.Value;
        public bool HasBazooka => IsSpawned && _bazookaAmmo.Value > 0;

        float _beaconUntil = -1f;
        /// <summary>Владелец: сколько ещё секунд маяк подсвечивает вражеские шахты на пульте.</summary>
        public float BeaconTimeLeft => Mathf.Max(0f, _beaconUntil - Time.time);
        /// <summary>Владелец: открыл ящик и что в нём было (для подсказки на экране).</summary>
        public event Action<LootKind> LootReceived;

        /// <summary>Владелец: повис / отцепился (ставит RocketRider).</summary>
        public void SetRide(int silo, int slot)
        {
            if (!IsOwner)
                return;
            if (_rideSilo.Value != silo) _rideSilo.Value = silo;
            if (_rideSlot.Value != slot) _rideSlot.Value = (byte)slot;
        }

        /// <summary>Схватился за ракету — слышат все (у владельца срабатывает сразу при записи).</summary>
        void OnRideChanged(int previous, int current)
        {
            if (previous < 0 && current >= 0)
                Sfx.PlayAt(Sfx.RocketGrab, transform.position + Vector3.up, IsLocalHuman ? 0.5f : 0.7f, 25f);
        }

        /// <summary>Сервер: игрок упал с острова и был возвращён (для статистики «Лемминг»).</summary>
        public static event Action<NetworkPlayer> AnyFell;
        /// <summary>Сервер: этот игрок поднял союзника (для статистики «Медик»).</summary>
        public static event Action<NetworkPlayer, PlayerHealth> AnyRevivedAlly;

        FpsController _fps;
        PlayerHealth _health;
        double _lastShotServer = -10;

        public PlayerHealth Health => _health;

        /// <summary>У всех, кроме самого стрелка: игрок выстрелил (вспышка, звук, вскинутая рука).</summary>
        public event Action Shot;
        /// <summary>У стрелка: сервер подтвердил попадание (маркер у прицела); true — цель от этого выстрела легла.</summary>
        public event Action<bool> HitConfirmed;

        void Awake()
        {
            _fps = GetComponent<FpsController>();
            _health = GetComponent<PlayerHealth>();
        }

        /// <summary>Сервер, до Spawn.</summary>
        public void ServerInit(bool isBot) => _pendingBot = isBot;

        bool _pendingBot;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                _isBot.Value = _pendingBot;
                Bullet.ServerHit += OnServerBulletHit;
                TeamFlag.FlagDelivered += OnFlagDelivered;
                // движение удалённых игроков считают их клиенты — сервер проверяет скорость (боты — свои)
                if (!IsOwner)
                    gameObject.AddComponent<MovementGuard>();
            }
            _rideSilo.OnValueChanged += OnRideChanged;
            _fps.IsLocal = IsLocalHuman;
            _fps.Simulate = IsOwner;
            if (IsOwner)
                _fps.HardLanding += OnHardLanding;
            if (IsLocalHuman)
            {
                LocalPlayer = this;
                LocalPlayerRig.Attach(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            _fps.HardLanding -= OnHardLanding;
            _rideSilo.OnValueChanged -= OnRideChanged;
            if (IsServer)
            {
                Bullet.ServerHit -= OnServerBulletHit;
                TeamFlag.FlagDelivered -= OnFlagDelivered;
            }
            if (LocalPlayer == this)
            {
                LocalPlayerRig.Detach();
                LocalPlayer = null;
            }
        }

        void Update()
        {
            if (!IsSpawned)
                return;

            if (IsLocalHuman)
            {
                _fps.MovementLocked = MatchManager.FreezePlayers;
                if (_crouch.Value != _fps.IsCrouching)
                    _crouch.Value = _fps.IsCrouching;
                if (_binoculars.Value != LocalBinoculars)
                    _binoculars.Value = LocalBinoculars;
                if (_sniperAim.Value != LocalSniperAim)
                    _sniperAim.Value = LocalSniperAim;
                HandleDebugKeys();
            }

            if (IsServer)
                ServerUpdateBerserk();

            if (IsOwner && IsBot)
            {
                // присед бота виден всем (у живого игрока его пишет ветка IsLocalHuman выше)
                if (_crouch.Value != _fps.IsCrouching)
                    _crouch.Value = _fps.IsCrouching;
            }

            if (IsOwner)
            {
                UpdateLevitation();
                // парашют раскрывается и у ботов (они симулируются на сервере-владельце)
                if (_parachute.Value != _fps.ParachuteOpen)
                    _parachute.Value = _fps.ParachuteOpen;
            }
            else
            {
                _fps.RemoteCrouch = _crouch.Value;
            }
        }

        /// <summary>Отладочные клавиши — только в редакторе и dev-сборках.</summary>
        void HandleDebugKeys()
        {
            if (!Debug.isDebugBuild)
                return;
            var kb = Keyboard.current;
            if (kb == null || Cursor.lockState != CursorLockMode.Locked)
                return;

            // F — тестовый взрыв в точку прицела (до конца разработки)
            if (kb.fKey.wasPressedThisFrame && Camera.main != null)
            {
                var cam = Camera.main.transform;
                if (Physics.Raycast(cam.position, cam.forward, out var hit, 300f, ~0, QueryTriggerInteraction.Ignore))
                    DebugExplosionRpc(hit.point);
            }

            // R — переиграть раунд на новых островах (только хост)
            if (kb.rKey.wasPressedThisFrame && IsServer && MatchManager.Instance != null)
                MatchManager.Instance.DebugRestartRound();
        }

        // ---------- удержание E (античит) ----------

        /// <summary>
        /// Удержания E считает владелец, но сервер засекает их сам: клиент сообщает начало, и подъём союзника
        /// или открытие ящика принимается, только если с начала прошло почти всё время удержания
        /// (подменённый в памяти прогресс ничего не даёт). Боты — серверные, им верим.
        /// </summary>
        HoldKind _holdKind;
        ulong _holdTarget;
        double _holdStart;

        /// <summary>Доля времени удержания, которую сервер требует (остальное — запас на разброс задержки сети).</summary>
        const float HoldTolerance = 0.8f;

        /// <summary>Владелец: начал удерживать E (target — NetworkObjectId союзника или номер ящика).</summary>
        public void BeginHold(HoldKind kind, ulong target)
        {
            if (!IsBot)
                BeginHoldRpc(kind, target);
        }

        [Rpc(SendTo.Server)]
        void BeginHoldRpc(HoldKind kind, ulong target)
        {
            _holdKind = kind;
            _holdTarget = target;
            _holdStart = NetClock.Now;
        }

        /// <summary>Сервер: удержание этого действия длилось достаточно; отметка начала сгорает.</summary>
        bool ServerHeldLongEnough(HoldKind kind, ulong target, float duration)
        {
            if (IsBot)
                return true;
            bool ok = _holdKind == kind && _holdTarget == target && NetClock.Now - _holdStart >= duration * HoldTolerance;
            _holdKind = HoldKind.None;
            if (!ok)
                Debug.LogWarning($"[AntiCheat] {_health.DisplayName}: {kind} without a full hold, rejected");
            return ok;
        }

        // ---------- запросы владельца ----------

        public void ReportFall() => FellRpc();

        void OnHardLanding(float height)
        {
            if (height > SafeFallHeight)
                HardLandingRpc(height);
        }

        public static float FallDamage(float height) =>
            Mathf.Clamp01((height - SafeFallHeight) / (MaxDamageFallHeight - SafeFallHeight)) * MaxFallDamage;

        [Rpc(SendTo.Server)]
        void HardLandingRpc(float height)
        {
            float damage = FallDamage(height); // не больше 80 при любой присланной высоте
            if (damage <= 0f)
                return;
            _health.TakeDamage(new DamageInfo(damage, transform.position, gameObject, _health.Team, DamageKind.Fall));
            Debug.Log($"[RocketWars] {_health.DisplayName} hard landing {height:0.0} m -> {damage:0} dmg, hp {_health.Hp}");
        }

        public void RequestRevive(PlayerHealth target)
        {
            if (target != null && target.NetworkObject != null)
                ReviveRpc(target.NetworkObject);
        }

        /// <param name="pressTime">Момент нажатия по общим часам (NetClock): сервер применяет нажатие на этот момент.</param>
        public void PressConsole(int siloIndex, ConsoleButtonKind kind, double pressTime) => PressConsoleRpc(siloIndex, (byte)kind, pressTime);

        // ---------- ящики с припасами ----------

        public void OpenCrate(LootCrate crate) => OpenCrateRpc(crate.NetIndex);
        public void UseMedkit(LootCrate crate) => UseMedkitRpc(crate.NetIndex);
        public void TakeLoot(LootCrate crate) => TakeLootRpc(crate.NetIndex);

        // ---------- флаги ----------

        /// <summary>Владелец: забрать вражеский флаг (кнопкой действия рядом с ним).</summary>
        public void TakeFlag(TeamFlag flag) => TakeFlagRpc(flag.Team);

        [Rpc(SendTo.Server)]
        void TakeFlagRpc(int team)
        {
            var flag = MatchNet.Instance != null ? MatchNet.Instance.GetFlag(team) : null;
            if (flag == null || flag.Team == _health.Team || _health.IsDowned || MatchManager.FreezePlayers)
                return;
            Vector3 d = flag.Position - transform.position;
            if (d.y < -2.5f || d.y > 3f)
                return;
            d.y = 0f;
            if (d.magnitude > TeamFlag.PickupRange + CrateRangeSlack)
                return;
            flag.ServerTake(_health);
        }

        LootCrate CrateInReach(int index)
        {
            var crate = MatchNet.Instance != null ? MatchNet.Instance.GetCrate(index) : null;
            if (crate == null || !crate.gameObject.activeSelf || _health.IsDowned || MatchManager.FreezePlayers)
                return null;
            Vector3 d = crate.Center - transform.position;
            float vertical = d.y;
            d.y = 0f;
            return d.magnitude <= LootCrate.UseRange + CrateRangeSlack && vertical > -2.5f && vertical < 3f ? crate : null;
        }

        [Rpc(SendTo.Server)]
        void OpenCrateRpc(int index)
        {
            var crate = CrateInReach(index);
            if (crate == null || !ServerHeldLongEnough(HoldKind.Crate, (ulong)index, LootCrate.OpenTime) || !crate.ServerOpen())
                return;
            Debug.Log($"[RocketWars] {_health.DisplayName} opened crate {index}: {crate.Kind}");

            // предметы остаются в ящике до нажатия E; аптечку раненый применяет сразу
            if (crate.Kind == LootKind.Medkits)
            {
                LootRpc((byte)crate.Kind);
                TryHeal(crate);
            }
        }

        [Rpc(SendTo.Server)]
        void TakeLootRpc(int index)
        {
            var crate = CrateInReach(index);
            if (crate == null || !crate.ServerTakeItem())
                return;

            switch (crate.Kind)
            {
                case LootKind.MachineGun:
                    _machineGunAmmo.Value = MachineGunSpec.Ammo;
                    break;
                case LootKind.Sniper:
                    _sniperAmmo.Value = SniperSpec.Ammo;
                    break;
                case LootKind.Bazooka:
                    _bazookaAmmo.Value = BazookaSpec.Ammo;
                    break;
                case LootKind.Armor:
                    _health.GiveArmor();
                    break;
                case LootKind.Beacon:
                    BeaconRpc(BeaconDuration);
                    break;
            }
            LootRpc((byte)crate.Kind);
            Debug.Log($"[RocketWars] {_health.DisplayName} took {crate.Kind} from crate {index}");
        }

        [Rpc(SendTo.Server)]
        void UseMedkitRpc(int index)
        {
            var crate = CrateInReach(index);
            if (crate != null)
                TryHeal(crate);
        }

        /// <summary>Сервер: раненый лечится аптечкой из открытого ящика.</summary>
        void TryHeal(LootCrate crate)
        {
            if (crate.Medkits <= 0 || _health.Hp >= PlayerHealth.MaxHp)
                return;
            if (crate.ServerTakeMedkit())
            {
                _health.Heal(LootCrate.MedkitHeal);
                HealedRpc();
            }
        }

        [Rpc(SendTo.Owner)]
        void BeaconRpc(float duration) => _beaconUntil = duration > 0f ? Time.time + duration : -1f;

        [Rpc(SendTo.Owner)]
        void LootRpc(byte kind) => LootReceived?.Invoke((LootKind)kind);

        [Rpc(SendTo.ClientsAndHost)]
        void HealedRpc() => Sfx.PlayAt(Sfx.Heal, transform.position + Vector3.up, 0.9f, 30f);

        /// <summary>Сервер, новый раунд: пулемёт и маяк не переходят в следующий раунд (броню сбрасывает PlayerHealth).</summary>
        public void ServerResetLoadout()
        {
            _machineGunAmmo.Value = 0;
            _sniperAmmo.Value = 0;
            _bazookaAmmo.Value = 0;
            BeaconRpc(0f);
            if (IsBerserk)
                _berserkEnd.Value = NetClock.Now; // берсерк в новый раунд не переходит
        }

        // ---------- пистолет ----------

        /// <summary>Владелец: выстрел из глаз в направлении взгляда (свой трассер он уже показал сам).</summary>
        public void Shoot(Vector3 origin, Vector3 direction) => ShootRpc(origin, direction);

        [Rpc(SendTo.Server)]
        void ShootRpc(Vector3 origin, Vector3 direction)
        {
            if (_health.IsDowned || MatchManager.FreezePlayers || _binoculars.Value || direction.sqrMagnitude < 1e-6f)
                return; // лёжа, во время отсчёта и с биноклем у глаз не стреляют
            double now = NetClock.Now;
            bool berserk = IsBerserk;
            bool bazooka = !berserk && _bazookaAmmo.Value > 0;
            bool sniper = !berserk && !bazooka && _sniperAmmo.Value > 0;
            bool machineGun = !berserk && !bazooka && !sniper && _machineGunAmmo.Value > 0;
            float cooldown = berserk ? BerserkSpec.Cooldown : bazooka ? BazookaSpec.Cooldown : sniper ? SniperSpec.Cooldown
                : machineGun ? MachineGunSpec.Cooldown : PistolSpec.Cooldown;
            if (now - _lastShotServer < cooldown * 0.8f)
                return; // защита от слишком частой стрельбы
            _lastShotServer = now;
            if (bazooka)
            {
                _bazookaAmmo.Value--;
                Vector3 eyeNow = _fps.CameraRoot.position;
                if ((origin - eyeNow).sqrMagnitude > 2.5f * 2.5f)
                    origin = eyeNow;
                direction.Normalize();
                BazookaShell.Spawn(origin, direction, gameObject, _health.Team, true);
                BazookaFxRpc(origin, direction, now);
                return;
            }
            if (sniper)
                _sniperAmmo.Value--;
            else if (machineGun)
                _machineGunAmmo.Value--; // у берсерка патроны бесконечные
            var kind = sniper ? BulletKind.Sniper : BulletKind.Normal;

            // выстрел должен идти из глаз игрока (небольшой допуск на задержку)
            Vector3 eye = _fps.CameraRoot.position;
            if ((origin - eye).sqrMagnitude > 2.5f * 2.5f)
                origin = eye;
            direction.Normalize();

            Bullet.Spawn(origin, direction, gameObject, _health.Team, true, 0f, kind);
            BulletFxRpc(origin, direction, now, (byte)kind);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BulletFxRpc(Vector3 origin, Vector3 direction, double fireTime, byte kind)
        {
            if (IsLocalHuman)
                return; // стрелок уже показал выстрел у себя без задержки
            Shot?.Invoke();
            // на хосте летит серверная пуля — она же и трассер
            if (!IsServer)
                Bullet.Spawn(origin + direction * PistolSpec.MuzzleForward, direction, gameObject, _health.Team, false,
                    (float)(NetClock.Now - fireTime), (BulletKind)kind);
        }

        [Rpc(SendTo.ClientsAndHost)]
        void BazookaFxRpc(Vector3 origin, Vector3 direction, double fireTime)
        {
            if (IsLocalHuman)
                return; // стрелок уже показал выстрел у себя
            Shot?.Invoke();
            if (!IsServer)
                BazookaShell.Spawn(origin, direction, gameObject, _health.Team, false, (float)(NetClock.Now - fireTime));
        }

        /// <summary>Сервер: снаряд этого игрока взорвался — эффект всем.</summary>
        public void ServerShellExploded(Vector3 point) => BazookaBoomRpc(point);

        [Rpc(SendTo.ClientsAndHost)]
        void BazookaBoomRpc(Vector3 point) => BazookaShell.ExplosionFx(point);

        void OnServerBulletHit(GameObject shooter, PlayerHealth target)
        {
            if (shooter == gameObject && !IsBot)
                HitConfirmRpc(target.IsDowned);
            if (target.gameObject == gameObject && !IsBot)
                GotShotRpc();
        }

        [Rpc(SendTo.Owner)]
        void HitConfirmRpc(bool knockedDown) => HitConfirmed?.Invoke(knockedDown);

        /// <summary>В этого игрока попала пуля — звук только ему (остальные слышат обычный Hurt).</summary>
        [Rpc(SendTo.Owner)]
        void GotShotRpc() => Sfx.Play2D(Sfx.BulletHitMe, 0.8f);

        [Rpc(SendTo.Server)]
        void FellRpc()
        {
            _health.ApplyFallPenalty();
            // сорвался с острова с флагом — флаг возвращается на базу
            if (MatchNet.Instance != null)
                foreach (var flag in MatchNet.Instance.Flags)
                    flag.ServerCarrierFell(_health);
            Debug.Log($"[RocketWars] {_health.DisplayName} fell off the island, hp {_health.Hp}");
            AnyFell?.Invoke(this);
        }

        [Rpc(SendTo.Server)]
        void ReviveRpc(NetworkObjectReference targetRef)
        {
            if (!targetRef.TryGet(out var targetObj) || !targetObj.TryGetComponent(out PlayerHealth target))
                return;
            if (_health.IsDowned || !target.IsDowned || target.Team != _health.Team || target == _health)
                return;
            if (ReviveInteraction.DistanceToBody(transform.position, target) > ReviveInteraction.Range + ReviveRangeSlack)
                return;
            if (!ServerHeldLongEnough(HoldKind.Revive, target.NetworkObjectId, ReviveInteraction.ReviveDuration))
                return;

            target.Revive();
            AnyRevivedAlly?.Invoke(this, target);
        }

        [Rpc(SendTo.Server)]
        void PressConsoleRpc(int siloIndex, byte kind, double pressTime)
        {
            if (_health.IsDowned || MatchNet.Instance == null)
                return;
            var silo = MatchNet.Instance.GetSilo(siloIndex);
            if (silo == null || Vector3.Distance(transform.position, silo.Console.transform.position) > ConsoleRange)
                return;
            silo.Console.ServerPress((ConsoleButtonKind)kind, this, pressTime);
        }

        /// <summary>
        /// Владелец: кнопка «Перейти в команду» в паузе. В лобби и после матча — переход сразу, во время матча —
        /// запрос на переход после матча (повторное нажатие отменяет). Можно ли — решает сервер.
        /// </summary>
        public void RequestSwitchTeam() => SwitchTeamRpc();

        [Rpc(SendTo.Server)]
        void SwitchTeamRpc() => MatchNet.Instance?.ServerRequestTeamSwitch(this);

        /// <summary>Сервер: запрос на смену команды после матча поставлен / снят.</summary>
        public void ServerSetTeamSwitchQueued(bool queued)
        {
            if (IsServer && _teamSwitchQueued.Value != queued)
                _teamSwitchQueued.Value = queued;
        }

        [Rpc(SendTo.Server)]
        void DebugExplosionRpc(Vector3 position) => Explosion.Detonate(position, gameObject, _health.Team);

        /// <summary>Сервер: переставить игрока так, чтобы MovementGuard не принял это за читерский телепорт.</summary>
        public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            if (TryGetComponent(out MovementGuard guard))
                guard.Grace(2f);
            TeleportRpc(position, rotation);
        }

        /// <summary>Сервер → владелец: переставить игрока (движение у владельца).</summary>
        [Rpc(SendTo.Owner)]
        public void TeleportRpc(Vector3 position, Quaternion rotation)
        {
            transform.rotation = rotation;
            _fps.Teleport(position);
        }
    }
}
