using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.UI;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace RocketWars.Player
{
    /// <summary>
    /// Оружие локального игрока: ЛКМ — выстрел (если прицел не на кнопке пульта и бинокль опущен), оружие в руке внизу справа
    /// с отдачей, свой трассер и вспышка сразу, без ожидания сервера. Урон считает сервер (NetworkPlayer.Shoot).
    /// Пока есть патроны пулемёта из ящика — в руках пулемёт и стрельба очередью при зажатой ЛКМ, потом снова пистолет.
    /// Снайперка из ящика важнее пулемёта: одиночные выстрелы, ПКМ — оптика ×4 (стрелять в прицел можно, от бедра — с разбросом).
    /// Берсерк — по золотому пулемёту в каждой руке, стреляют по очереди, патроны бесконечные.
    /// </summary>
    [RequireComponent(typeof(NetworkPlayer), typeof(PlayerHealth))]
    public class PistolView : MonoBehaviour
    {
        static readonly Vector3 RestPosition = new Vector3(0.25f, -0.25f, 0.62f);
        static readonly Quaternion RestRotation = Quaternion.Euler(0f, -4f, 0f); // ствол чуть к центру экрана
        const float PistolScale = 1.1f;
        const float GunViewScale = 0.6f;
        const float SniperViewScale = 0.42f;
        const float BazookaViewScale = 0.45f;
        // правее и ближе к плечу, дуло повёрнуто к центру экрана — задний раструб уходит за край экрана
        static readonly Vector3 BazookaOffset = new Vector3(0.2f, -0.02f, -0.12f);
        static readonly Quaternion BazookaYaw = Quaternion.Euler(0f, -8f, 0f);
        const float RecoilKick = 0.07f;
        const float RecoilPitch = 9f;
        const float RecoilRecover = 12f;
        public const float HitMarkerTime = 0.4f;

        public Camera cam;

        /// <summary>Время последнего подтверждённого попадания (для маркера у прицела).</summary>
        public float LastHitTime { get; private set; } = -10f;
        /// <summary>Последнее попадание положило цель (маркер крупнее и красный, другой звук).</summary>
        public bool LastHitKnockedDown { get; private set; }
        /// <summary>Патроны пулемёта с учётом своих ещё не подтверждённых сервером выстрелов.</summary>
        public int MachineGunAmmo => _ammo;
        public bool HoldingMachineGun => _ammo > 0 && !HoldingSniper && !HoldingBazooka;
        /// <summary>Заряды базуки (с учётом своих неподтверждённых выстрелов); базука важнее остального оружия из ящиков.</summary>
        public int BazookaAmmo => _bazookaAmmo;
        public bool HoldingBazooka => _bazookaAmmo > 0 && !HoldingGoldGuns;
        /// <summary>Патроны снайперки (с учётом своих неподтверждённых выстрелов).</summary>
        public int SniperAmmo => _sniperAmmo;
        public bool HoldingSniper => _sniperAmmo > 0 && !HoldingGoldGuns && !HoldingBazooka;
        /// <summary>Берсерк: в руках два золотых пулемёта (после вспышки превращения).</summary>
        public bool HoldingGoldGuns => _net != null && _net.IsBerserk && _net.BerserkTransform01 >= 0.5f;

        // два золотых пулемёта у берсерка: слева и справа внизу экрана
        static readonly Vector3 DualPosition = new Vector3(0f, -0.27f, 0.6f);
        const float DualSpread = 0.24f;
        Transform _dual, _goldL, _goldR, _goldMuzzleL, _goldMuzzleR;
        float _recoilL, _recoilR;
        bool _leftNext;

        NetworkPlayer _net;
        PlayerHealth _health;
        Binoculars _binoculars;
        PlayerVisual _visual;
        Transform _model, _muzzle, _gunMuzzle, _sniperMuzzle, _bazookaMuzzle;
        GameObject _pistol, _gun, _sniper, _bazooka;
        float _cooldown, _recoil;
        int _ammo, _serverAmmo, _sniperAmmo, _sniperServerAmmo, _bazookaAmmo, _bazookaServerAmmo;
        float _boltAt = -1f;
        float _lastShotTime = -10f;

        void Start()
        {
            _net = GetComponent<NetworkPlayer>();
            _health = GetComponent<PlayerHealth>();
            _binoculars = GetComponent<Binoculars>();
            _visual = GetComponent<PlayerVisual>();
            _net.HitConfirmed += OnHitConfirmed;

            // пистолет в руке «от первого лица»: дочерний объект камеры
            var root = new GameObject("PistolViewModel").transform;
            root.SetParent(cam.transform, false);
            root.localPosition = RestPosition;
            var (handMesh, handMats) = Models.ViewModelHand(_health.Team);

            _pistol = new GameObject("PistolHolder");
            _pistol.transform.SetParent(root, false);
            var (mesh, mats) = Models.Pistol();
            var pistol = BoxModel.Spawn(mesh, mats, _pistol.transform, Vector3.zero, "Pistol");
            pistol.transform.localScale = Vector3.one * PistolScale;
            // перчатка на рукояти и манжета рукава (вся рука от первого лица не нужна — она перекрывает обзор)
            BoxModel.Spawn(handMesh, handMats, _pistol.transform, new Vector3(0f, -0.04f, -0.035f) * PistolScale, "Hand");

            // пулемёт из ящика: та же кисть на пистолетной рукояти, ствол уходит вперёд к центру экрана
            _gun = new GameObject("MachineGunHolder");
            _gun.transform.SetParent(root, false);
            var (gunMesh, gunMats) = Models.MachineGun();
            var gun = BoxModel.Spawn(gunMesh, gunMats, _gun.transform, Vector3.zero, "MachineGun");
            gun.transform.localScale = Vector3.one * GunViewScale; // модель рассчитана на мир — в руке у камеры уменьшаем
            BoxModel.Spawn(handMesh, handMats, _gun.transform, new Vector3(0f, -0.03f, -0.03f), "Hand");
            _gunMuzzle = new GameObject("Muzzle").transform;
            _gunMuzzle.SetParent(gun.transform, false);
            _gunMuzzle.localPosition = new Vector3(0f, 0.065f, Models.MachineGunMuzzle);
            _gun.SetActive(false);

            // снайперка: так же в правой руке, длинный ствол к центру экрана
            _sniper = new GameObject("SniperHolder");
            _sniper.transform.SetParent(root, false);
            var (rifleMesh, rifleMats) = Models.SniperRifle();
            var rifle = BoxModel.Spawn(rifleMesh, rifleMats, _sniper.transform, Vector3.zero, "SniperRifle");
            rifle.transform.localScale = Vector3.one * SniperViewScale; // длинная — мельче пулемёта, иначе закрывает пол-экрана
            BoxModel.Spawn(handMesh, handMats, _sniper.transform, new Vector3(0f, -0.03f, -0.03f), "Hand");
            _sniperMuzzle = new GameObject("Muzzle").transform;
            _sniperMuzzle.SetParent(rifle.transform, false);
            _sniperMuzzle.localPosition = new Vector3(0f, 0.065f, Models.SniperMuzzle);
            _sniper.SetActive(false);

            // базука: труба выше и ближе к плечу, уходит вперёд к центру экрана
            _bazooka = new GameObject("BazookaHolder");
            _bazooka.transform.SetParent(root, false);
            _bazooka.transform.localPosition = BazookaOffset;
            _bazooka.transform.localRotation = BazookaYaw;
            var (bazMesh, bazMats) = Models.Bazooka();
            var baz = BoxModel.Spawn(bazMesh, bazMats, _bazooka.transform, Vector3.zero, "Bazooka");
            baz.transform.localScale = Vector3.one * BazookaViewScale;
            BoxModel.Spawn(handMesh, handMats, _bazooka.transform, new Vector3(0f, -0.03f, -0.03f), "Hand");
            _bazookaMuzzle = new GameObject("Muzzle").transform;
            _bazookaMuzzle.SetParent(baz.transform, false);
            _bazookaMuzzle.localPosition = new Vector3(0f, Models.BazookaAxisY, Models.BazookaMuzzle);
            _bazooka.SetActive(false);
            root.localRotation = RestRotation;
            foreach (var r in root.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(pistol.transform, false);
            _muzzle.localPosition = new Vector3(0f, 0.045f, Models.PistolMuzzle);
            _model = root;

            _dual = new GameObject("GoldGunsViewModel").transform;
            _dual.SetParent(cam.transform, false);
            _dual.localPosition = DualPosition;
            var (goldMesh, goldMats) = Models.MachineGun(true);
            _goldL = SpawnGoldGun(_dual, goldMesh, goldMats, handMesh, handMats, -1f, out _goldMuzzleL);
            _goldR = SpawnGoldGun(_dual, goldMesh, goldMats, handMesh, handMats, 1f, out _goldMuzzleR);
            foreach (var r in _dual.GetComponentsInChildren<Renderer>())
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _dual.gameObject.SetActive(false);
        }

        static Transform SpawnGoldGun(Transform parent, Mesh mesh, Material[] mats, Mesh hand, Material[] handMats, float side, out Transform muzzle)
        {
            var holder = new GameObject(side < 0 ? "GoldLeft" : "GoldRight").transform;
            holder.SetParent(parent, false);
            holder.localPosition = new Vector3(DualSpread * side, 0f, 0f);
            holder.localRotation = Quaternion.Euler(0f, -5f * side, 0f); // стволы чуть сходятся к прицелу
            var gun = BoxModel.Spawn(mesh, mats, holder, Vector3.zero, "GoldMachineGun");
            gun.transform.localScale = Vector3.one * GunViewScale;
            BoxModel.Spawn(hand, handMats, holder, new Vector3(0f, -0.03f, -0.03f), "Hand");
            muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(gun.transform, false);
            muzzle.localPosition = new Vector3(0f, 0.065f, Models.MachineGunMuzzle);
            return holder;
        }

        void OnDestroy()
        {
            if (_net != null)
                _net.HitConfirmed -= OnHitConfirmed;
            if (_model != null)
                Destroy(_model.gameObject);
            if (_dual != null)
                Destroy(_dual.gameObject);
        }

        void OnHitConfirmed(bool knockedDown)
        {
            LastHitTime = Time.time;
            LastHitKnockedDown = knockedDown;
            Sfx.Play2D(knockedDown ? Sfx.HitKill : Sfx.HitTick, knockedDown ? 0.8f : 0.6f);
        }

        void Update()
        {
            _cooldown -= Time.deltaTime;
            TrackAmmo();
            _pistol.SetActive(!HoldingMachineGun && !HoldingSniper && !HoldingBazooka);
            _bazooka.SetActive(HoldingBazooka);
            _gun.SetActive(HoldingMachineGun);
            _sniper.SetActive(HoldingSniper);
            if (_boltAt > 0f && Time.time >= _boltAt)
            {
                _boltAt = -1f;
                Sfx.Play2D(Sfx.Bolt, 0.5f); // передёрнул затвор
            }
            bool scoped = _binoculars != null && _binoculars.Amount > 0.3f;
            // на время превращения камера сзади (вид от третьего лица) — оружие «в камере» не показываем и не стреляем
            bool canUse = !_health.IsDowned && !UiRoot.CursorFree && !_net.InBerserkTransform
                && _net.RideTrick == RideTrick.None; // во время трюка — вид от третьего лица, руки заняты
            bool gold = HoldingGoldGuns;
            _model.gameObject.SetActive(canUse && !scoped && !gold);
            _dual.gameObject.SetActive(canUse && !scoped && gold);

            var mouse = Mouse.current;
            var interactor = PlayerInteractor.Local;
            bool onConsole = interactor != null && interactor.Hovered != null;
            // с биноклем у глаз (и пока он поднимается/опускается) не стреляем; в оптику снайперки — можно
            bool binocularsInUse = _binoculars != null && !_binoculars.IsScope && (_binoculars.IsUp || _binoculars.Amount > 0f);
            // пулемёт стреляет очередью, пока зажата ЛКМ; пистолет — по одному нажатию
            bool automatic = HoldingMachineGun || gold;
            bool trigger = mouse != null && (automatic ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame);
            if (canUse && !onConsole && !binocularsInUse && !MatchManager.FreezePlayers && _cooldown <= 0f && trigger)
                Fire();

            // отдача: откат назад и подброс ствола, плавный возврат
            _recoil = Mathf.MoveTowards(_recoil, 0f, RecoilRecover * Time.deltaTime * Mathf.Max(0.2f, _recoil));
            _model.localPosition = RestPosition + new Vector3(0f, 0f, -RecoilKick * _recoil);
            _model.localRotation = RestRotation * Quaternion.Euler(-RecoilPitch * _recoil, 0f, 0f);
            _recoilL = Mathf.MoveTowards(_recoilL, 0f, RecoilRecover * Time.deltaTime);
            _recoilR = Mathf.MoveTowards(_recoilR, 0f, RecoilRecover * Time.deltaTime);
            _goldL.localPosition = new Vector3(-DualSpread, 0f, -RecoilKick * _recoilL);
            _goldR.localPosition = new Vector3(DualSpread, 0f, -RecoilKick * _recoilR);
        }

        /// <summary>
        /// Свой счётчик патронов: сервер подтверждает выстрелы с задержкой, поэтому вычитаем сразу.
        /// Больше, чем у сервера, быть не может; подобрали пулемёт — берём серверное; а если после очереди
        /// расхождение держится (сервер отбросил выстрел) — тоже верим серверу.
        /// </summary>
        void TrackAmmo()
        {
            Track(ref _ammo, ref _serverAmmo, _net.MachineGunAmmo);
            Track(ref _sniperAmmo, ref _sniperServerAmmo, _net.SniperAmmo);
            Track(ref _bazookaAmmo, ref _bazookaServerAmmo, _net.BazookaAmmo);
        }

        void Track(ref int ammo, ref int lastServer, int server)
        {
            if (server > lastServer)
                ammo = server;
            ammo = Mathf.Min(ammo, server);
            if (ammo != server && Time.time - _lastShotTime > 0.6f)
                ammo = server;
            lastServer = server;
        }

        void Fire()
        {
            if (HoldingGoldGuns)
            {
                FireGold();
                return;
            }
            if (HoldingBazooka)
            {
                FireBazooka();
                return;
            }
            bool sniper = HoldingSniper;
            bool machineGun = HoldingMachineGun;
            _lastShotTime = Time.time;
            _cooldown = sniper ? SniperSpec.Cooldown : machineGun ? MachineGunSpec.Cooldown : PistolSpec.Cooldown;
            _recoil = sniper ? 1.6f : machineGun ? 0.45f : 1f;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            if (machineGun)
            {
                _ammo--;
                Vector2 spread = Random.insideUnitCircle * MachineGunSpec.Spread;
                dir = cam.transform.rotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
            }
            if (sniper)
            {
                _sniperAmmo--;
                if (_sniperAmmo > 0)
                    _boltAt = Time.time + 0.45f;
                // в оптику — точно в перекрестие, от бедра — с разбросом
                bool aimed = _binoculars != null && _binoculars.IsScope && _binoculars.Amount > 0.9f;
                if (!aimed)
                {
                    Vector2 spread = Random.insideUnitCircle * SniperSpec.HipSpread;
                    dir = cam.transform.rotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
                }
            }
            Transform muzzle = sniper ? _sniperMuzzle : machineGun ? _gunMuzzle : _muzzle;
            bool armed = _model.gameObject.activeSelf;
            // в оптике винтовки не видно — трассер выходит чуть ниже перекрестия, а не из глаз
            bool fromMuzzle = armed || sniper;
            Vector3 muzzlePos = armed ? muzzle.position : cam.transform.TransformPoint(0f, -0.15f, 0.4f);
            var kind = sniper ? BulletKind.Sniper : BulletKind.Normal;
            if (fromMuzzle)
                Bullet.SetLocalMuzzle(gameObject, muzzlePos); // трассер — из дула (и у хоста, где пулю создаёт сервер)
            _net.Shoot(origin, dir);

            // свой трассер — сразу (на хосте летит серверная пуля, второй не нужен)
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer)
            {
                if (fromMuzzle)
                    Bullet.SetLocalMuzzle(gameObject, muzzlePos);
                Bullet.Spawn(origin + dir * PistolSpec.MuzzleForward, dir, gameObject, _health.Team, false, 0f, kind);
            }

            if (sniper)
                Sfx.Play2D(Sfx.SniperShot, 0.85f);
            else
                Sfx.Play2D(Sfx.Gunshot, machineGun ? 0.55f : 0.7f);
            if (armed)
                MuzzleFlash(muzzle.position, dir);
            else if (!sniper)
                MuzzleFlash(origin + dir * 0.5f, dir);
            if (_visual != null)
                _visual.OnShotPose();
        }

        /// <summary>Базука: снаряд из трубы, сильная отдача и облако дыма позади.</summary>
        void FireBazooka()
        {
            _cooldown = BazookaSpec.Cooldown;
            _lastShotTime = Time.time;
            _recoil = 1.8f;
            _bazookaAmmo--;

            Vector3 origin = cam.transform.position;
            Vector3 dir = cam.transform.forward;
            bool armed = _model.gameObject.activeSelf;
            Vector3 muzzle = armed ? _bazookaMuzzle.position : cam.transform.TransformPoint(0.15f, -0.15f, 0.5f);
            BazookaShell.SetLocalMuzzle(gameObject, muzzle);
            _net.Shoot(origin, dir);
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer)
            {
                BazookaShell.SetLocalMuzzle(gameObject, muzzle);
                BazookaShell.Spawn(origin, dir, gameObject, _health.Team, false);
            }

            Sfx.Play2D(Sfx.BazookaShot, 0.85f);
            MuzzleFlash(muzzle, dir); // выхлоп назад у себя не показываем — он был бы прямо в камере (его видят другие)
            if (_visual != null)
                _visual.OnShotPose();
        }

        /// <summary>Выхлоп базуки назад: клубы дыма и немного огня.</summary>
        public static void BackBlast(Vector3 pos, Vector3 back)
        {
            for (int i = 0; i < 10; i++)
                VoxelParticles.Instance.Emit(pos + Random.insideUnitSphere * 0.1f, back * Random.Range(3f, 7f) + Random.insideUnitSphere * 1.2f,
                    Random.Range(0.5f, 0.9f), 0.15f, Random.Range(0.35f, 0.55f), VoxelParticles.RandomSmoke(), drag: 3f,
                    growEnd: 0.2f, shrinkStart: 0.5f);
            for (int i = 0; i < 4; i++)
                VoxelParticles.Instance.Emit(pos, back * Random.Range(4f, 8f) + Random.insideUnitSphere, Random.Range(0.08f, 0.15f),
                    0.1f, 0.07f, Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, growEnd: 0.1f, shrinkStart: 0.3f);
        }

        /// <summary>Берсерк: золотые пулемёты стреляют по очереди — левый, правый; патроны не тратятся.</summary>
        void FireGold()
        {
            _cooldown = BerserkSpec.Cooldown;
            _lastShotTime = Time.time;
            _leftNext = !_leftNext;
            if (_leftNext) _recoilL = 0.6f;
            else _recoilR = 0.6f;

            Vector3 origin = cam.transform.position;
            Vector2 spread = Random.insideUnitCircle * MachineGunSpec.Spread;
            Vector3 dir = cam.transform.rotation * Quaternion.Euler(spread.y, spread.x, 0f) * Vector3.forward;
            var goldMuzzle = (_leftNext ? _goldMuzzleL : _goldMuzzleR).position;
            Bullet.SetLocalMuzzle(gameObject, goldMuzzle);
            _net.Shoot(origin, dir);
            bool isServer = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
            if (!isServer)
            {
                Bullet.SetLocalMuzzle(gameObject, goldMuzzle);
                Bullet.Spawn(origin + dir * PistolSpec.MuzzleForward, dir, gameObject, _health.Team, false);
            }
            Sfx.Play2D(Sfx.Gunshot, 0.5f);
            MuzzleFlash((_leftNext ? _goldMuzzleL : _goldMuzzleR).position, dir);
            if (_visual != null)
                _visual.OnShotPose();
        }

        public static void MuzzleFlash(Vector3 pos, Vector3 dir)
        {
            for (int i = 0; i < 4; i++)
                VoxelParticles.Instance.Emit(pos, dir * Random.Range(2f, 5f) + Random.insideUnitSphere * 0.8f, Random.Range(0.05f, 0.09f),
                    0.07f, 0.04f, Random.value < 0.5f ? VoxelParticles.Fire0 : VoxelParticles.Fire1, growEnd: 0.1f, shrinkStart: 0.3f);
        }
    }
}
