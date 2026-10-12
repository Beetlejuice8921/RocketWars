using System;
using System.Collections.Generic;
using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Terrain;
using Unity.Netcode;
using UnityEngine;

namespace RocketWars.Loot
{
    /// <summary>Что лежит в ящике.</summary>
    public enum LootKind : byte
    {
        /// <summary>Две аптечки по +60 HP: остаются в открытом ящике, применяются кнопкой действия рядом с ним.</summary>
        Medkits,
        /// <summary>Пулемёт с 30 патронами — сразу в руки открывшему.</summary>
        MachineGun,
        /// <summary>Снайперская винтовка с 3 патронами: пуля вдвое быстрее, урон 80, оптика ×4 (врагам виден блик).</summary>
        Sniper,
        /// <summary>Базука с 2 зарядами: взрыв 100 урона до 2 м и 50 до 4 м, задевает и своих.</summary>
        Bazooka,
        /// <summary>Маяк: открывшему на 15 с показывает вражеские шахты на пульте.</summary>
        Beacon,
        /// <summary>Бронежилет: +100 брони, тратится раньше здоровья.</summary>
        Armor
    }

    public enum CrateState : byte
    {
        Closed,
        Open,
        Destroyed
    }

    /// <summary>Сетевой снимок ящика (NetworkList у MatchNet). Тип содержимого не шлётся — он детерминирован seed'ом.</summary>
    public struct CrateSync : INetworkSerializeByMemcpy, IEquatable<CrateSync>
    {
        public byte State;
        /// <summary>Сколько осталось внутри: аптечек (0–2) или 1/0 — лежит ли ещё предмет.</summary>
        public byte Items;

        public bool Equals(CrateSync o) => State == o.State && Items == o.Items;
        public override bool Equals(object obj) => obj is CrateSync o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(State, Items);
    }

    /// <summary>Ящик, сброшенный с неба посреди раунда: где, что внутри и когда начал опускаться.</summary>
    public struct CrateDrop : INetworkSerializeByMemcpy, IEquatable<CrateDrop>
    {
        public int RoundId;
        public double StartTime;
        public short X, Z;
        public byte Team, Kind, YawStep;

        public bool Equals(CrateDrop o) => RoundId == o.RoundId && StartTime.Equals(o.StartTime) && X == o.X && Z == o.Z &&
                                           Team == o.Team && Kind == o.Kind && YawStep == o.YawStep;
        public override bool Equals(object obj) => obj is CrateDrop o && Equals(o);
        public override int GetHashCode() => HashCode.Combine(RoundId, X, Z, Team);
    }

    /// <summary>
    /// Ящик с припасами размером в один кубик. Открывается удержанием E 1,5 с: крышка откидывается, видно содержимое.
    /// HP 1000, повреждают его только взрывы. Если из-под ящика выбили землю — падает ниже (или с острова).
    /// Ящики генерируются у всех из seed раунда; решения (открытие, аптечки, урон) принимает сервер (IsAuthority).
    /// </summary>
    public class LootCrate : MonoBehaviour, IDamageable
    {
        public const float MaxHp = 1000f;
        public const float OpenTime = 1.5f;
        /// <summary>С какого расстояния (по горизонтали от ног до центра ящика) можно открыть ящик или взять аптечку.</summary>
        public const float UseRange = 1.9f;
        public const int MedkitCount = 2;
        public const float MedkitHeal = 60f;

        const float LidOpenAngle = 245f; // крышка проворачивается на петлях и повисает вдоль задней стенки
        const float LidSpeed = 300f;
        const float ItemTakeTime = 0.3f;
        const float FallGravity = -20f;

        /// <summary>Сброс с неба: с какой высоты над землёй и с какой скоростью опускается на парашютике.</summary>
        public const float DropHeight = 45f;
        public const float DropSpeed = 4f;
        const float ChuteScale = 0.45f;
        const float ChuteFoldTime = 0.5f;

        public static readonly List<LootCrate> All = new List<LootCrate>();

        public int NetIndex { get; set; }
        public bool IsAuthority { get; set; } = true;
        public LootKind Kind { get; private set; }
        public CrateState State { get; private set; } = CrateState.Closed;
        /// <summary>Сколько осталось внутри: аптечек или 1 — предмет ещё не забрали.</summary>
        public int Items { get; private set; }
        public int Medkits => Kind == LootKind.Medkits ? Items : 0;
        /// <summary>В открытом ящике лежит предмет (не аптечки) — его можно взять кнопкой действия.</summary>
        public bool HasItem => Kind != LootKind.Medkits && Items > 0;
        public float Hp { get; private set; } = MaxHp;
        public VoxelIsland Island { get; private set; }
        public bool IsFalling => _falling || _airborne;
        /// <summary>Клетка острова, на которой стоит (или куда опускается) ящик.</summary>
        public Vector2 Cell => new Vector2(_cellX, _cellZ);
        /// <summary>Центр ящика (для расстояний и подсказок).</summary>
        public Vector3 Center => transform.position + Vector3.up * 0.5f;

        Transform _lid, _item;
        readonly List<GameObject> _medkitModels = new List<GameObject>();
        float _lidAngle, _itemTakenAt = -10f;
        float _fallSpeed, _restY;
        bool _falling, _intoVoid;
        int _cellX, _cellZ;
        // сброс с неба: высоту считаем от общего сетевого времени, чтобы у всех ящик опускался одинаково
        bool _airborne;
        double _dropStart;
        float _dropBaseY, _yaw, _chuteFold = -1f;
        Transform _chute;

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);

        public static LootCrate Create(VoxelIsland island, int x, int z, LootKind kind, float yaw, Transform parent)
        {
            var go = new GameObject($"Crate_{kind}");
            go.transform.SetParent(parent, true);
            go.transform.position = island.BlockToWorld(x + 0.5f, island.GetTopHeight(x, z), z + 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);

            var col = go.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.5f, 0f);
            col.size = Vector3.one;

            var (bodyMesh, bodyMats) = Models.CrateBody();
            BoxModel.Spawn(bodyMesh, bodyMats, go.transform, Vector3.zero, "Body");
            var (lidMesh, lidMats) = Models.CrateLid();
            var lid = BoxModel.Spawn(lidMesh, lidMats, go.transform, new Vector3(0f, Models.CrateBodyHeight, -0.5f), "Lid");

            var crate = go.AddComponent<LootCrate>();
            crate.Island = island;
            crate.Kind = kind;
            crate._lid = lid.transform;
            crate._cellX = x;
            crate._cellZ = z;
            crate._restY = go.transform.position.y;
            crate._yaw = yaw;
            crate.Items = kind == LootKind.Medkits ? MedkitCount : 1;
            crate.BuildContents();
            island.BlocksChanged += crate.CheckSupport;
            return crate;
        }

        /// <summary>
        /// Ящик, сброшенный с неба в момент startTime (NetClock): опускается на мини-парашюте с высоты DropHeight.
        /// Если сброс уже давно был (поздний вход) — сразу стоит на земле.
        /// </summary>
        public static LootCrate CreateAirDrop(VoxelIsland island, int x, int z, LootKind kind, float yaw, Transform parent, double startTime)
        {
            var crate = Create(island, x, z, kind, yaw, parent);
            island.ClearGrass(x, z, x, z);
            crate._airborne = true;
            crate._dropStart = startTime;
            crate._dropBaseY = crate._restY;

            var (chuteMesh, chuteMats) = Models.Parachute();
            crate._chute = BoxModel.Spawn(chuteMesh, chuteMats, crate.transform, new Vector3(0f, 0.3f, 0f), "Parachute").transform;
            crate._chute.localScale = Vector3.one * ChuteScale;
            crate.UpdateAirDrop();
            return crate;
        }

        /// <summary>Содержимое внутри: видно, когда крышка открыта.</summary>
        void BuildContents()
        {
            float floor = Models.CrateFill;
            switch (Kind)
            {
                case LootKind.Medkits:
                    var (medMesh, medMats) = Models.Medkit();
                    for (int i = 0; i < MedkitCount; i++)
                    {
                        var m = BoxModel.Spawn(medMesh, medMats, transform, new Vector3(0f, floor, i == 0 ? 0.19f : -0.19f), "Medkit");
                        m.transform.localRotation = Quaternion.Euler(0f, i == 0 ? 6f : -5f, 0f);
                        m.transform.localScale = Vector3.one * 1.25f;
                        _medkitModels.Add(m);
                    }
                    break;
                case LootKind.MachineGun:
                    var (gunMesh, gunMats) = Models.MachineGun();
                    // на боку, по диагонали ящика; середина пулемёта (от приклада до дула) — в центре ящика
                    var gunRot = Quaternion.Euler(0f, 45f, 90f);
                    float gs = ItemScale(Kind);
                    Vector3 gunMid = gunRot * new Vector3(0f, 0f, (Models.MachineGunMuzzle - 0.32f) * 0.5f * gs);
                    _item = BoxModel.Spawn(gunMesh, gunMats, transform, new Vector3(-gunMid.x, floor + 0.1f, -gunMid.z), "MachineGun").transform;
                    _item.localRotation = gunRot;
                    _item.localScale = Vector3.one * gs;
                    break;
                case LootKind.Sniper:
                    var (rifleMesh, rifleMats) = Models.SniperRifle();
                    // на боку, по диагонали ящика (длинная — уменьшена, чтобы влезла между стенками)
                    var rifleRot = Quaternion.Euler(0f, 45f, 90f);
                    float rs = ItemScale(Kind);
                    Vector3 rifleMid = rifleRot * new Vector3(0f, 0f, (Models.SniperMuzzle - 0.47f) * 0.5f * rs);
                    _item = BoxModel.Spawn(rifleMesh, rifleMats, transform, new Vector3(-rifleMid.x, floor + 0.12f, -rifleMid.z), "SniperRifle").transform;
                    _item.localRotation = rifleRot;
                    _item.localScale = Vector3.one * rs;
                    break;
                case LootKind.Bazooka:
                    var (bazMesh, bazMats) = Models.Bazooka();
                    // на боку, по диагонали ящика
                    var bazRot = Quaternion.Euler(0f, 45f, 90f);
                    float bzs = ItemScale(Kind);
                    Vector3 bazMid = bazRot * new Vector3(0f, Models.BazookaAxisY * bzs, (Models.BazookaMuzzle - 0.55f) * 0.5f * bzs);
                    _item = BoxModel.Spawn(bazMesh, bazMats, transform, new Vector3(-bazMid.x, floor + 0.15f, -bazMid.z), "Bazooka").transform;
                    _item.localRotation = bazRot;
                    _item.localScale = Vector3.one * bzs;
                    break;
                case LootKind.Beacon:
                    var (beaconMesh, beaconMats) = Models.Beacon();
                    // лёжа на боку лампой вверх: стоя антенна торчала бы из закрытого ящика
                    float s = ItemScale(Kind);
                    _item = BoxModel.Spawn(beaconMesh, beaconMats, transform, new Vector3(-0.24f * s, floor + 0.1f * s, 0f), "Beacon").transform;
                    _item.localRotation = Quaternion.Euler(0f, 0f, -90f);
                    _item.localScale = Vector3.one * s;
                    break;
                case LootKind.Armor:
                    var (vestMesh, vestMats) = Models.ArmorVest();
                    _item = BoxModel.Spawn(vestMesh, vestMats, transform, new Vector3(0f, floor, 0f), "Armor").transform;
                    _item.localScale = Vector3.one * ItemScale(Kind);
                    break;
            }
        }

        /// <summary>Масштаб предмета в ящике: всё должно помещаться между стенками (внутри ±0,42 м).</summary>
        static float ItemScale(LootKind kind) => kind == LootKind.MachineGun ? 1f : kind == LootKind.Sniper ? 0.78f : kind == LootKind.Bazooka ? 0.75f
            : kind == LootKind.Armor ? 1.1f : 1.3f;

        void OnDestroy()
        {
            if (Island != null)
                Island.BlocksChanged -= CheckSupport;
        }

        // ---------- сервер ----------

        /// <summary>Сервер: открыть ящик. false — уже открыт/разбит.</summary>
        public bool ServerOpen()
        {
            if (!IsAuthority || State != CrateState.Closed || IsFalling)
                return false;
            SetState(CrateState.Open);
            return true;
        }

        /// <summary>Сервер: взять одну аптечку из открытого ящика.</summary>
        public bool ServerTakeMedkit()
        {
            if (!IsAuthority || State != CrateState.Open || Medkits <= 0)
                return false;
            SetItems(Items - 1);
            return true;
        }

        /// <summary>Сервер: забрать предмет (пулемёт, снайперку, броню, маяк) из открытого ящика.</summary>
        public bool ServerTakeItem()
        {
            if (!IsAuthority || State != CrateState.Open || !HasItem)
                return false;
            SetItems(0);
            return true;
        }

        public void TakeDamage(in DamageInfo info)
        {
            if (!IsAuthority || State == CrateState.Destroyed || info.Kind != DamageKind.Explosion)
                return;
            Hp -= info.Amount;
            if (Hp <= 0f)
            {
                Hp = 0f;
                SetState(CrateState.Destroyed);
            }
        }

        public CrateSync ToSync() => new CrateSync { State = (byte)State, Items = (byte)Items };

        // ---------- клиент ----------

        public void ApplySync(in CrateSync sync)
        {
            if (Items != sync.Items)
                SetItems(sync.Items);
            if (sync.State != (byte)State)
                SetState((CrateState)sync.State);
        }

        // ---------- общее ----------

        void SetState(CrateState state)
        {
            var previous = State;
            State = state;
            if (state == CrateState.Open && previous == CrateState.Closed)
                Sfx.PlayAt(Sfx.CrateOpen, Center, 0.9f, 40f);
            if (state == CrateState.Destroyed)
                Shatter();
        }

        void SetItems(int items)
        {
            if (Kind != LootKind.Medkits && Items > 0 && items <= 0)
                _itemTakenAt = Time.time; // предмет забрали — короткая анимация «взлетел и пропал»
            Items = items;
            RefreshMedkits();
        }

        void RefreshMedkits()
        {
            for (int i = 0; i < _medkitModels.Count; i++)
                _medkitModels[i].SetActive(i < Medkits);
        }

        /// <summary>Ящик разбит взрывом: щепки и пропал вместе с содержимым.</summary>
        void Shatter()
        {
            if (!gameObject.activeSelf)
                return;
            if (_intoVoid)
            {
                gameObject.SetActive(false); // упал с острова — щепки в пустоте никто не увидит
                return;
            }
            var pieces = new List<Vector3>();
            var kinds = new List<BlockKind>();
            for (int i = 0; i < 6; i++)
            {
                pieces.Add(Center + UnityEngine.Random.insideUnitSphere * 0.4f);
                kinds.Add(BlockKind.Wood);
            }
            VoxelFx.Debris(Center, pieces, kinds);
            gameObject.SetActive(false);
        }

        /// <summary>Под ящиком выбили землю — падает на новый верх столбца или с острова.</summary>
        void CheckSupport()
        {
            if (State == CrateState.Destroyed || _intoVoid || Island == null)
                return;
            int top = Island.GetTopHeight(_cellX, _cellZ);
            if (top == 0)
            {
                _intoVoid = true;
                _falling = !_airborne; // ещё в воздухе — дальше опускается на парашюте, мимо острова
                _restY = float.NegativeInfinity;
                return;
            }
            float y = Island.transform.position.y + top;
            if (_airborne)
            {
                _restY = Mathf.Min(_restY, y); // приземлится на новый верх столбца
                return;
            }
            if (y < transform.position.y - 0.01f)
            {
                _restY = y;
                _falling = true;
            }
        }

        void Update()
        {
            if (_airborne)
                UpdateAirDrop();
            else if (_falling)
                UpdateFall();
            UpdateChuteFold();

            // крышка неподвижна, пока ящик открывают (идёт полоса загрузки), и откидывается после открытия
            float target = State == CrateState.Open ? LidOpenAngle : 0f;
            _lidAngle = Mathf.MoveTowards(_lidAngle, target, LidSpeed * Time.deltaTime);
            _lid.localRotation = Quaternion.Euler(-_lidAngle, 0f, 0f);

            // предмет лежит в открытом ящике, пока его не возьмут (E); забрали — быстро сжимается и пропадает
            if (_item != null)
            {
                float since = Time.time - _itemTakenAt;
                bool gone = Items <= 0 && since > ItemTakeTime;
                if (_item.gameObject.activeSelf == gone)
                    _item.gameObject.SetActive(!gone);
                if (!gone)
                {
                    float k = Items <= 0 ? Mathf.Clamp01(since / ItemTakeTime) : 0f;
                    _item.localScale = Vector3.one * ItemScale(Kind) * (1f - k * k);
                }
            }
        }

        /// <summary>Спуск на парашютике: покачивается под куполом, у земли купол складывается.</summary>
        void UpdateAirDrop()
        {
            float t = (float)(Net.NetClock.Now - _dropStart);
            float y = _dropBaseY + DropHeight - DropSpeed * Mathf.Max(0f, t);
            var p = transform.position;
            if (y <= _restY)
            {
                p.y = _restY;
                transform.position = p;
                transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
                _airborne = false;
                _chuteFold = 0f;
                if (t < DropHeight / DropSpeed + 1f) // поздний вход — без звука и пыли
                    Sfx.PlayAt(Sfx.CrateOpen, Center, 0.5f, 30f, 0.6f);
                return;
            }

            p.y = y;
            transform.position = p;
            // маятник под куполом; к земле качание затухает
            float sway = Mathf.Clamp01((y - _restY) / 6f);
            float phase = NetIndex * 1.7f;
            transform.rotation = Quaternion.Euler(Mathf.Sin(t * 1.9f + phase) * 7f * sway, _yaw + Mathf.Sin(t * 0.7f + phase) * 10f,
                Mathf.Cos(t * 1.5f + phase) * 6f * sway);

            if (_intoVoid && p.y < Island.transform.position.y - 60f)
            {
                _airborne = false;
                if (IsAuthority)
                    SetState(CrateState.Destroyed);
                else
                    gameObject.SetActive(false);
            }
        }

        /// <summary>Приземлился — купол оседает набок и пропадает.</summary>
        void UpdateChuteFold()
        {
            if (_chute == null || _chuteFold < 0f)
                return;
            _chuteFold += Time.deltaTime / ChuteFoldTime;
            if (_chuteFold >= 1f)
            {
                Destroy(_chute.gameObject);
                _chute = null;
                return;
            }
            float k = _chuteFold;
            _chute.localScale = new Vector3(ChuteScale * (1f + 0.3f * k), ChuteScale * (1f - k), ChuteScale * (1f - 0.6f * k));
            _chute.localRotation = Quaternion.Euler(0f, 0f, 40f * k);
        }

        void UpdateFall()
        {
            _fallSpeed += FallGravity * Time.deltaTime;
            var p = transform.position;
            p.y += _fallSpeed * Time.deltaTime;
            if (p.y <= _restY)
            {
                p.y = _restY;
                _fallSpeed = 0f;
                _falling = false;
            }
            transform.position = p;

            if (_intoVoid && p.y < Island.transform.position.y - 60f)
            {
                _falling = false;
                if (IsAuthority)
                    SetState(CrateState.Destroyed);
                else
                    gameObject.SetActive(false);
            }
        }
    }
}
