using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Net;
using RocketWars.Terrain;
using UnityEngine;

namespace RocketWars.Launcher
{
    public enum SiloState : byte
    {
        Ready,
        Launching,
        Reloading,
        Destroyed
    }

    /// <summary>Параметры пуска — одинаковы на сервере и клиентах.</summary>
    public struct LaunchInfo
    {
        public Vector3 Start;
        public Vector3 Target;
        public bool HasGround;
        public GameObject Source;
        public int SourceTeam;
        public double LaunchTime;
        /// <summary>Из какой шахты — по нему за ракету «цепляются» те, кто висел на ней во время сирены.</summary>
        public int SiloIndex;
    }

    /// <summary>
    /// Ракетная шахта команды: HP 150, пульт наведения, сирена при пуске, перезарядка 60 с.
    /// Если под площадкой не осталось кубиков — падает и считается уничтоженной.
    /// Шахты генерируются у всех из seed раунда; решения (урон, пуск, перезарядка) принимает сервер (IsAuthority),
    /// клиенты получают состояние через SiloSync.
    /// </summary>
    public class RocketSilo : MonoBehaviour, IDamageable
    {
        public const float MaxHp = 150f;
        public const float ReloadTime = 60f;
        public const float SirenTime = 2.5f;
        public const int Footprint = 3;
        const float FallGravity = -20f;
        /// <summary>Сопло стоящей ракеты — внутри башни, на этой высоте над плитой.</summary>
        const float LoadedNozzleHeight = 1.2f;
        /// <summary>Центр стоящей ракеты: отсюда же стартует летящая (бесшовный пуск).</summary>
        static readonly float LaunchHeight = LoadedNozzleHeight + Models.RocketTail;

        public static readonly List<RocketSilo> All = new List<RocketSilo>();
        static readonly HashSet<Collider> LoadedRocketColliders = new HashSet<Collider>();

        /// <summary>Это корпус стоящей в шахте ракеты (его пропускает наведение: цель — земля/шахта, а не нос ракеты).</summary>
        public static bool IsLoadedRocket(Collider collider) => LoadedRocketColliders.Contains(collider);

        public int Team { get; private set; }
        public float Hp { get; private set; } = MaxHp;
        public SiloState State { get; private set; } = SiloState.Ready;
        public double ReloadEnd { get; private set; }
        public float ReloadLeft => State == SiloState.Reloading ? Mathf.Max(0f, (float)(ReloadEnd - NetClock.Now)) : 0f;
        public VoxelIsland Island { get; private set; }
        public AimConsole Console { get; private set; }
        /// <summary>Мировая точка носа заряженной ракеты (за неё хватаются во время сирены).</summary>
        public Vector3 LoadedRocketTop => transform.position + Vector3.up * (LaunchHeight + Models.RocketNose);
        public Vector3 LoadedRocketCenter => transform.position + Vector3.up * LaunchHeight;
        public Vector3 LoadedRocketBottom => transform.position + Vector3.up * LoadedNozzleHeight;
        public int NetIndex { get; set; }
        public bool IsAuthority { get; set; } = true;

        /// <summary>Сервер: шахта уничтожена.</summary>
        public event Action<RocketSilo, DamageInfo> Destroyed;
        /// <summary>Сервер: любая шахта уничтожена — для статистики «Сапёр».</summary>
        public static event Action<RocketSilo, DamageInfo> AnyDestroyed;
        /// <summary>Сервер: ракета вылетела.</summary>
        public event Action<RocketSilo, LaunchInfo> Launched;

        /// <summary>За сколько секунд до готовности индикатор становится жёлтым.</summary>
        public const float AlmostReadyTime = 5f;

        const float IndicatorGlow = 0.7f; // сильнее — цвет «выгорает» в белёсый
        static readonly Color IndicatorRed = new Color(0.95f, 0.05f, 0.03f);
        static readonly Color IndicatorYellow = new Color(1f, 0.82f, 0.1f);
        static readonly Color IndicatorGreen = new Color(0.15f, 0.95f, 0.2f);

        Renderer[] _renderers;
        MeshRenderer _indicator;
        GameObject _loadedRocket;
        Light _sirenLight;
        AudioSource _sirenAudio;
        LaunchInfo _pending;
        double _launchAt;
        float _fallSpeed;
        bool _falling;
        bool _burnt;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static RocketSilo Create(VoxelIsland island, Vector3 position, Transform parent)
        {
            int team = island.Team;
            var go = new GameObject($"Silo_T{team}");
            go.transform.SetParent(parent, true);
            go.transform.position = position;

            // плита 3×3 и круглая башня (восьмигранная призма до верха крыши) — без «воздушных» углов
            var slab = go.AddComponent<BoxCollider>();
            slab.center = new Vector3(0, 0.15f, 0);
            slab.size = new Vector3(Footprint, 0.3f, Footprint);
            var tower = go.AddComponent<MeshCollider>();
            tower.sharedMesh = TowerColliderMesh();
            tower.convex = true;

            // воксельная модель: плита, круглая башня в цвет команды, крыша с люком
            var (siloMesh, siloMats) = Models.Silo(team);
            BoxModel.Spawn(siloMesh, siloMats, go.transform, Vector3.zero, "Model");

            var silo = go.AddComponent<RocketSilo>();
            silo.Team = team;
            silo.Island = island;

            // заряженная ракета торчит из люка носом вверх
            var (rocketMesh, rocketMats) = Models.Rocket(team);
            silo._loadedRocket = BoxModel.Spawn(rocketMesh, rocketMats, go.transform, new Vector3(0, LaunchHeight, 0), "LoadedRocket");
            silo._loadedRocket.transform.localScale = Vector3.one * Models.RocketScale;
            silo._loadedRocket.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            // корпус стоящей ракеты твёрдый: сквозь неё не пройти (стабилизаторы — внутри башни, их не учитываем)
            var rocketCol = silo._loadedRocket.AddComponent<BoxCollider>();
            rocketCol.center = new Vector3(0f, 0f, (Models.RocketNose - Models.RocketTail) * 0.5f / Models.RocketScale);
            rocketCol.size = new Vector3(Models.RocketRadius * 2f, Models.RocketRadius * 2f, Models.RocketTail + Models.RocketNose) / Models.RocketScale;
            LoadedRocketColliders.Add(rocketCol);

            // индикатор готовности по верхнему краю башни: красный / жёлтый (≤5 с) / зелёный
            var indicator = BoxModel.Spawn(Models.SiloIndicator(), new[] { RuntimeMaterials.Emissive(IndicatorGreen, IndicatorGlow) },
                go.transform, Vector3.zero, "ReadyIndicator");
            silo._indicator = indicator.GetComponent<MeshRenderer>();
            silo._indicator.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // сирена: прожектор у лампы на углу башни
            var sirenGo = new GameObject("SirenLight");
            sirenGo.transform.SetParent(go.transform, false);
            sirenGo.transform.localPosition = new Vector3(0.75f, 2.65f, 0.75f);
            silo._sirenLight = sirenGo.AddComponent<Light>();
            silo._sirenLight.type = LightType.Spot;
            silo._sirenLight.color = new Color(1f, 0.15f, 0.1f);
            silo._sirenLight.intensity = 60f;
            silo._sirenLight.range = 30f;
            silo._sirenLight.spotAngle = 70f;
            silo._sirenLight.shadows = LightShadows.None;
            silo._sirenLight.enabled = false;
            silo._sirenAudio = Sfx.Configure(go.AddComponent<AudioSource>(), 90f);
            silo._sirenAudio.clip = Sfx.Siren;
            silo._sirenAudio.loop = true;

            silo.Console = AimConsole.Create(silo);
            silo._renderers = go.GetComponentsInChildren<MeshRenderer>();
            island.BlocksChanged += silo.CheckSupport;
            return silo;
        }

        static Mesh _towerCollider;

        /// <summary>Восьмигранная призма по контуру башни с крышей (для коллайдера).</summary>
        static Mesh TowerColliderMesh()
        {
            if (_towerCollider != null)
                return _towerCollider;
            const int sides = 8;
            float r = Models.SiloRadius * 1.13f / Mathf.Cos(Mathf.PI / sides); // описанный вокруг крыши
            var verts = new Vector3[sides * 2];
            for (int i = 0; i < sides; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / sides;
                var p = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                verts[i] = p + Vector3.up * 0.3f;
                verts[i + sides] = p + Vector3.up * Models.SiloRoofTop;
            }
            var tris = new System.Collections.Generic.List<int>();
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                tris.AddRange(new[] { i, i + sides, j, j, i + sides, j + sides });
                if (i > 0 && i < sides - 1)
                {
                    tris.AddRange(new[] { 0, j, i });
                    tris.AddRange(new[] { sides, sides + i, sides + j });
                }
            }
            _towerCollider = new Mesh { name = "SiloTowerCollider", vertices = verts, triangles = tris.ToArray() };
            return _towerCollider;
        }

        void OnDestroy()
        {
            LoadedRocketColliders.RemoveWhere(c => c == null || c.transform.IsChildOf(transform));
            if (Island != null)
                Island.BlocksChanged -= CheckSupport;
        }

        // ---------- сервер ----------

        public bool ServerLaunch(Vector3 target, bool hasGround, GameObject source, int sourceTeam)
        {
            if (!IsAuthority || State != SiloState.Ready)
                return false;
            _launchAt = NetClock.Now + SirenTime;
            _pending = new LaunchInfo
            {
                Start = transform.position + Vector3.up * LaunchHeight,
                Target = target,
                HasGround = hasGround,
                Source = source,
                SourceTeam = sourceTeam,
                SiloIndex = NetIndex
            };
            SetState(SiloState.Launching);
            return true;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAuthority || State == SiloState.Destroyed)
                return;
            Hp -= info.Amount;
            if (Hp > 0f)
                return;

            Hp = 0f;
            SetState(SiloState.Destroyed);
            Destroyed?.Invoke(this, info);
            AnyDestroyed?.Invoke(this, info);
        }

        public SiloSync ToSync()
        {
            var sync = Console.ToSync();
            sync.State = (byte)State;
            sync.ReloadEnd = ReloadEnd;
            return sync;
        }

        // ---------- клиент ----------

        public void ApplySync(in SiloSync sync)
        {
            ReloadEnd = sync.ReloadEnd;
            if (sync.State != (byte)State)
                SetState((SiloState)sync.State);
            Console.ApplySync(sync);
        }

        // ---------- общее ----------

        void SetState(SiloState state)
        {
            State = state;
            bool siren = state == SiloState.Launching;
            if (_sirenLight.enabled != siren)
            {
                _sirenLight.enabled = siren;
                if (siren) _sirenAudio.Play();
                else _sirenAudio.Stop();
            }
            _loadedRocket.SetActive(state == SiloState.Ready || state == SiloState.Launching);

            if (state == SiloState.Destroyed && !_burnt)
            {
                _burnt = true;
                var burnt = RuntimeMaterials.Lit(new Color(0.15f, 0.13f, 0.12f));
                foreach (var r in _renderers)
                {
                    if (r == null || r.GetComponentInParent<Canvas>() != null)
                        continue;
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = burnt;
                    r.sharedMaterials = mats;
                }
                transform.localScale = new Vector3(1f, 0.4f, 1f);
                if (TacticalMap.Instance != null)
                    TacticalMap.Instance.MarkDirty();
            }
        }

        /// <summary>Зелёный — готова, жёлтый — готова через ≤5 с, красный — не готова (пуск, перезарядка).</summary>
        void UpdateIndicator()
        {
            if (_burnt)
                return; // у разрушенной шахты индикатор погас вместе с остальным
            Color color = State == SiloState.Ready ? IndicatorGreen
                : State == SiloState.Reloading && ReloadLeft <= AlmostReadyTime ? IndicatorYellow
                : IndicatorRed;
            var mat = RuntimeMaterials.Emissive(color, IndicatorGlow);
            if (_indicator.sharedMaterial != mat)
                _indicator.sharedMaterial = mat;
        }

        /// <summary>Если под площадкой 3×3 не осталось ни одного кубика — шахта падает и считается уничтоженной.</summary>
        void CheckSupport()
        {
            if (_falling)
                return;
            Vector3 local = transform.position - Island.transform.position;
            int cx = Mathf.FloorToInt(local.x), cz = Mathf.FloorToInt(local.z);
            int y = Mathf.RoundToInt(local.y) - 1;
            for (int dx = -1; dx <= 1; dx++)
            for (int dz = -1; dz <= 1; dz++)
                if (Island.IsSolid(cx + dx, y, cz + dz))
                    return;

            _falling = true;
            if (IsAuthority && State != SiloState.Destroyed)
                TakeDamage(new DamageInfo(MaxHp, transform.position, null, -1, DamageKind.Fall));
        }

        void Update()
        {
            if (_falling)
            {
                _fallSpeed += FallGravity * Time.deltaTime;
                transform.position += Vector3.up * (_fallSpeed * Time.deltaTime);
                if (transform.position.y < Island.transform.position.y - 60f)
                    gameObject.SetActive(false);
            }

            UpdateIndicator();

            if (State == SiloState.Launching)
                _sirenLight.transform.localRotation = Quaternion.Euler(20f, Time.time * 360f, 0f);

            if (!IsAuthority)
                return;

            if (State == SiloState.Launching && NetClock.Now >= _launchAt)
            {
                _pending.LaunchTime = NetClock.Now;
                Rocket.Spawn(_pending, true);
                ReloadEnd = NetClock.Now + ReloadTime;
                SetState(SiloState.Reloading);
                Launched?.Invoke(this, _pending);
            }
            else if (State == SiloState.Reloading && NetClock.Now >= ReloadEnd)
            {
                SetState(SiloState.Ready);
            }
        }
    }
}
