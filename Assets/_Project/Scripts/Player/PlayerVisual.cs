using RocketWars.Core;
using UnityEngine;

namespace RocketWars.Player
{
    /// <summary>
    /// Воксельный солдат (камуфляж, каска с цветом команды): шаговая анимация рук и ног, каска надвигается в приседе,
    /// бинокль на груди / у лица (ПКМ), пистолет в правой руке (вскидывается при выстреле), руки вверх на ракете
    /// и на стропах, купол парашюта, поза «лежит», звуки шагов,
    /// урона, падения на землю и подъёма. Скорость считается по смещению — работает и для чужих игроков.
    /// Берсерк (превращение, золотые пулемёты, облачки) — в PlayerVisual.Berserk.cs.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public partial class PlayerVisual : MonoBehaviour
    {
        const float StrideLength = 1.6f;   // метров на полный цикл шага (два шага)
        const float MaxSwing = 35f;

        // бинокль: висит на груди линзами вниз / поднят к глазам
        static readonly Vector3 BinocularsChest = new Vector3(0f, 1.24f, 0.24f);
        static readonly Quaternion BinocularsChestRot = Quaternion.Euler(90f, 0f, 0f);
        static readonly Vector3 BinocularsFace = new Vector3(0f, 1.78f, 0.34f);
        static readonly Vector3 ShoulderL = new Vector3(-0.4f, 1.48f, 0f), ShoulderR = new Vector3(0.4f, 1.48f, 0f);

        Transform _body, _armL, _armR, _legL, _legR, _helmet, _binoculars, _pistolMuzzle, _gunMuzzle, _sniperMuzzle, _bazookaMuzzle, _parachute;
        GameObject _pistolModel, _gunModel, _sniperModel, _bazookaModel, _vest;
        Mesh _helmetMesh;
        Material[] _helmetMats;

        /// <summary>Покачивание каски при ходьбе: наклон вперёд-назад и вбок (градусы), подпрыгивание (м).</summary>
        const float HelmetNod = 9f, HelmetRoll = 7f, HelmetBob = 0.03f;
        float _hands, _canopy;
        static readonly Quaternion HandsUpL = Quaternion.Euler(180f, 0f, -12f), HandsUpR = Quaternion.Euler(180f, 0f, 12f);
        NetworkPlayer _net;
        float _raise, _shotPose;

        const float ShotPoseTime = 0.5f;
        static readonly Quaternion AimRotation = Quaternion.Euler(-85f, 0f, 0f);
        PlayerHealth _health;
        FpsController _controller;
        CharacterController _cc;
        Vector3 _lastPos;
        float _phase, _swing;
        bool _wasDowned;

        void Start()
        {
            _health = GetComponent<PlayerHealth>();
            _controller = GetComponent<FpsController>();
            _cc = GetComponent<CharacterController>();
            int team = _health.Team;

            _body = new GameObject("Body").transform;
            _body.SetParent(transform, false);
            var (bodyMesh, bodyMats) = Models.CharacterBody(team);
            BoxModel.Spawn(bodyMesh, bodyMats, _body, Vector3.zero, "Torso");
            var (armMesh, armMats) = Models.CharacterArm(team);
            _armL = BoxModel.Spawn(armMesh, armMats, _body, new Vector3(-0.4f, 1.48f, 0f), "ArmL").transform;
            _armR = BoxModel.Spawn(armMesh, armMats, _body, new Vector3(0.4f, 1.48f, 0f), "ArmR").transform;
            var (legMesh, legMats) = Models.CharacterLeg();
            _legL = BoxModel.Spawn(legMesh, legMats, _body, new Vector3(-0.14f, 0.84f, 0f), "LegL").transform;
            _legR = BoxModel.Spawn(legMesh, legMats, _body, new Vector3(0.14f, 0.84f, 0f), "LegR").transform;
            var (helmetMesh, helmetMats) = Models.CharacterHelmet(team);
            _helmet = BoxModel.Spawn(helmetMesh, helmetMats, _body, new Vector3(0f, Models.HelmetBaseY, 0f), "Helmet").transform;
            _helmetMesh = helmetMesh;
            _helmetMats = helmetMats;
            var (binoMesh, binoMats) = Models.Binoculars();
            _binoculars = BoxModel.Spawn(binoMesh, binoMats, _body, BinocularsChest, "Binoculars").transform;
            _binoculars.localRotation = BinocularsChestRot;
            _net = GetComponent<NetworkPlayer>();

            // пистолет в правой руке: в опущенной руке смотрит в землю, во вскинутой — вперёд
            var (pistolMesh, pistolMats) = Models.Pistol();
            var pistol = BoxModel.Spawn(pistolMesh, pistolMats, _armR, new Vector3(0f, -0.64f, 0.02f), "Pistol").transform;
            pistol.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _pistolMuzzle = new GameObject("Muzzle").transform;
            _pistolMuzzle.SetParent(pistol, false);
            _pistolMuzzle.localPosition = new Vector3(0f, 0.045f, Models.PistolMuzzle);
            _pistolModel = pistol.gameObject;

            // пулемёт из ящика — в той же руке вместо пистолета
            var (gunMesh, gunMats) = Models.MachineGun();
            var gun = BoxModel.Spawn(gunMesh, gunMats, _armR, new Vector3(0f, -0.64f, 0.02f), "MachineGun").transform;
            gun.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _gunMuzzle = new GameObject("Muzzle").transform;
            _gunMuzzle.SetParent(gun, false);
            _gunMuzzle.localPosition = new Vector3(0f, 0.065f, Models.MachineGunMuzzle);
            _gunModel = gun.gameObject;
            _gunModel.SetActive(false);

            // снайперка из ящика — тоже в правой руке; с оптикой у глаз вскинута обеими руками
            var (rifleMesh, rifleMats) = Models.SniperRifle();
            var rifle = BoxModel.Spawn(rifleMesh, rifleMats, _armR, new Vector3(0f, -0.64f, 0.02f), "SniperRifle").transform;
            rifle.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _sniperMuzzle = new GameObject("Muzzle").transform;
            _sniperMuzzle.SetParent(rifle, false);
            _sniperMuzzle.localPosition = new Vector3(0f, 0.065f, Models.SniperMuzzle);
            _sniperModel = rifle.gameObject;
            _sniperModel.SetActive(false);
            InitGlint();

            // базука из ящика — в той же руке
            var (bazMesh, bazMats) = Models.Bazooka();
            var baz = BoxModel.Spawn(bazMesh, bazMats, _armR, new Vector3(0f, -0.64f, 0.02f), "Bazooka").transform;
            baz.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _bazookaMuzzle = new GameObject("Muzzle").transform;
            _bazookaMuzzle.SetParent(baz, false);
            _bazookaMuzzle.localPosition = new Vector3(0f, Models.BazookaAxisY, Models.BazookaMuzzle);
            _bazookaModel = baz.gameObject;
            _bazookaModel.SetActive(false);

            // бронежилет поверх куртки — пока есть броня
            var (vestMesh, vestMats) = Models.WornVest();
            _vest = BoxModel.Spawn(vestMesh, vestMats, _body, Vector3.zero, "Vest");
            _vest.SetActive(false);
            if (_net != null)
                _net.Shot += OnRemoteShot;

            // парашют (купол со стропами) — раскрывается по сетевому флагу
            var (chuteMesh, chuteMats) = Models.Parachute();
            _parachute = BoxModel.Spawn(chuteMesh, chuteMats, transform, Vector3.zero, "Parachute").transform;
            _parachute.localScale = Vector3.zero;
            _parachute.gameObject.SetActive(false);

            InitBerserk(team);

            // своё тело не рисуем в свою же камеру, но тень оставляем
            // (true — включая пока скрытые жилет и пулемёт, иначе они появятся прямо перед глазами)
            if (_controller != null && _controller.IsLocal)
                foreach (var r in _body.GetComponentsInChildren<Renderer>(true))
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;

            _health.StateChanged += OnStateChanged;
            _health.Hurt += OnHurt;
            _wasDowned = _health.IsDowned;
            _lastPos = transform.position;
            UpdatePose();
        }

        void OnDestroy()
        {
            if (_net != null)
                _net.Shot -= OnRemoteShot;
            if (_health == null)
                return;
            _health.StateChanged -= OnStateChanged;
            _health.Hurt -= OnHurt;
        }

        /// <summary>Вскинуть руку с пистолетом (свой выстрел — вызывает PistolView).</summary>
        public void OnShotPose() => _shotPose = ShotPoseTime;

        /// <summary>Чужой выстрел: поза, вспышка у дула, звук.</summary>
        void OnRemoteShot()
        {
            OnShotPose();
            var dir = transform.forward;
            bool bazooka = _bazookaModel != null && _bazookaModel.activeSelf && !BerserkGear;
            bool sniper = _sniperModel != null && _sniperModel.activeSelf && !BerserkGear;
            bool gun = (_gunModel != null && _gunModel.activeSelf) || BerserkGear;
            var muzzle = bazooka ? _bazookaMuzzle : sniper ? _sniperMuzzle : gun ? _gunMuzzle : _pistolMuzzle;
            if (bazooka)
            {
                OnShotPose();
                PistolView.MuzzleFlash(muzzle.position, dir);
                PistolView.BackBlast(muzzle.position - muzzle.forward * 1.2f, -muzzle.forward);
                Sfx.PlayAt(Sfx.BazookaShot, transform.position + Vector3.up * 1.5f, 1f, 200f, Random.Range(0.95f, 1.05f));
                return;
            }
            PistolView.MuzzleFlash(TryBerserkMuzzle(out var goldMuzzle) ? goldMuzzle
                : muzzle != null ? muzzle.position : transform.position + Vector3.up * 1.5f + dir * 0.7f, dir);
            if (sniper)
                Sfx.PlayAt(Sfx.SniperShot, transform.position + Vector3.up * 1.5f, 1f, 250f, Random.Range(0.95f, 1.03f));
            else
                Sfx.PlayAt(Sfx.Gunshot, transform.position + Vector3.up * 1.5f, gun ? 0.85f : 1f, 120f,
                    gun ? Random.Range(0.85f, 0.92f) : Random.Range(0.95f, 1.05f));
        }

        void OnHurt(float amount)
        {
            if (!_health.IsDowned)
                Sfx.PlayAt(Sfx.Hurt, transform.position + Vector3.up, 0.8f, 40f, Random.Range(0.9f, 1.1f));
        }

        void OnStateChanged(PlayerState state)
        {
            bool downed = state == PlayerState.Downed;
            if (downed && !_wasDowned)
            {
                Sfx.PlayAt(Sfx.Downed, transform.position + Vector3.up, 1f, 60f);
                // в момент падения каска слетает с головы и скачет по земле
                if (_helmet != null && _helmet.gameObject.activeSelf)
                    Fx.FlyingHelmet.Spawn(_helmetMesh, _helmetMats, _helmet, gameObject);
                else if (_bCap != null && _bCap.activeSelf)
                    Fx.FlyingHelmet.Spawn(_capMesh, _capMats, _bCap.transform, gameObject); // берсерк теряет фуражку
            }
            else if (!downed && _wasDowned && _health.Hp < PlayerHealth.MaxHp) // полный сброс в начале раунда — без звука
                Sfx.PlayAt(Sfx.Revive, transform.position + Vector3.up, 1f, 50f);
            _wasDowned = downed;
            UpdatePose();
        }

        void Update()
        {
            if (_body == null)
                return;

            // у берсерка в руках золотые пулемёты — обычное оружие спрятано
            bool bazookaInHand = _net != null && _net.HasBazooka && !BerserkGear;
            bool sniperInHand = _net != null && _net.HasSniper && !bazookaInHand && !BerserkGear;
            bool gunInHand = _net != null && _net.HasMachineGun && !sniperInHand && !bazookaInHand && !BerserkGear;
            bool pistolInHand = !gunInHand && !sniperInHand && !bazookaInHand && !BerserkGear;
            if (_bazookaModel.activeSelf != bazookaInHand)
                _bazookaModel.SetActive(bazookaInHand);
            if (_sniperModel.activeSelf != sniperInHand)
                _sniperModel.SetActive(sniperInHand);
            if (_gunModel.activeSelf != gunInHand)
                _gunModel.SetActive(gunInHand);
            if (_pistolModel.activeSelf != pistolInHand)
                _pistolModel.SetActive(pistolInHand);
            bool vest = _health.Armor > 0f;
            if (_vest.activeSelf != vest)
                _vest.SetActive(vest);

            Vector3 delta = transform.position - _lastPos;
            _lastPos = transform.position;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 horizontal = new Vector3(delta.x, 0f, delta.z);
            float speed = horizontal.magnitude / dt;
            bool grounded = Mathf.Abs(delta.y / dt) < 1.5f && speed < 20f; // телепорт не считаем шагом

            // купол парашюта — и у лежащих (их тоже может сбросить с высоты)
            UpdateParachute(_net != null && _net.ParachuteOpen, dt);
            UpdateBerserk(dt);
            UpdateGlint(dt);

            if (_health.IsDowned)
                return;

            // присед — сжатие по высоте, каска глубже надвигается на голову
            if (_controller != null && _cc != null)
            {
                _body.localScale = new Vector3(1f, Mathf.Clamp01(_cc.height / _controller.standHeight), 1f);
                _helmet.localPosition = new Vector3(0f, Models.HelmetBaseY - Models.HelmetCrouchDrop * _controller.CrouchAmount, 0f);
            }

            // шаговая анимация
            float targetSwing = grounded ? Mathf.Clamp01(speed / 6f) * MaxSwing : MaxSwing * 0.3f;
            _swing = Mathf.MoveTowards(_swing, targetSwing, 200f * dt);
            if (grounded && speed > 0.5f)
            {
                float prev = _phase;
                _phase += horizontal.magnitude / StrideLength * 2f * Mathf.PI;
                // шаг на каждом полуцикле
                if (Mathf.Floor(prev / Mathf.PI) != Mathf.Floor(_phase / Mathf.PI))
                    Sfx.PlayAt(Sfx.Step, transform.position, _controller != null && _controller.IsLocal ? 0.35f : 0.5f, 25f, Random.Range(0.85f, 1.15f));
            }

            float s = Mathf.Sin(_phase) * _swing;
            _legL.localRotation = Quaternion.Euler(s, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-s, 0f, 0f);

            // каска болтается в такт шагам: кивает на каждом шаге, качается с боку на бок и подпрыгивает
            float wobble = _swing / MaxSwing;
            _helmet.localRotation = Quaternion.Euler(Mathf.Sin(_phase * 2f) * HelmetNod * wobble, 0f, Mathf.Sin(_phase) * HelmetRoll * wobble);
            float crouch = _controller != null ? _controller.CrouchAmount : 0f;
            _helmet.localPosition = new Vector3(0f,
                Models.HelmetBaseY - Models.HelmetCrouchDrop * crouch + Mathf.Abs(Mathf.Sin(_phase)) * HelmetBob * wobble, 0f);

            // бинокль: свой — сразу по нажатию, чужой — по сетевому флагу
            bool up = _net != null && (_net.IsLocalHuman ? _net.LocalBinoculars : _net.UsingBinoculars);
            _raise = Mathf.MoveTowards(_raise, up ? 1f : 0f, dt / Binoculars.RaiseTime);
            float k = _raise * _raise * (3f - 2f * _raise);
            _binoculars.localPosition = Vector3.Lerp(BinocularsChest, BinocularsFace, k);
            _binoculars.localRotation = Quaternion.Slerp(BinocularsChestRot, Quaternion.identity, k);

            // руки: шаг ↔ держат бинокль у лица (кисти по бокам от бинокля)
            _armL.localRotation = Quaternion.Slerp(Quaternion.Euler(-s * 0.8f, 0f, 0f), HoldRotation(ShoulderL, -1f), k);
            _armR.localRotation = Quaternion.Slerp(Quaternion.Euler(s * 0.8f, 0f, 0f), HoldRotation(ShoulderR, 1f), k);
            // рука прямая, без локтя: «сгибаем» её укорочением, чтобы кисть легла сбоку бинокля, а не перед лицом
            // выстрел: правая рука вскидывается вперёд (если не держит бинокль)
            _shotPose = Mathf.Max(0f, _shotPose - dt);
            if (_shotPose > 0f && k < 0.5f)
            {
                float aim = Mathf.Clamp01((ShotPoseTime - _shotPose) / 0.06f) * Mathf.Clamp01(_shotPose / 0.15f);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, AimRotation, aim);
            }
            ApplyScopePose();

            // держится за ракету или за стропы парашюта — руки вверх
            bool riding = _net != null && _net.IsRiding;
            bool chute = _net != null && _net.ParachuteOpen;
            _hands = Mathf.MoveTowards(_hands, riding || chute ? 1f : 0f, dt * 6f);
            if (_hands > 0f)
            {
                _armL.localRotation = Quaternion.Slerp(_armL.localRotation, HandsUpL, _hands);
                _armR.localRotation = Quaternion.Slerp(_armR.localRotation, HandsUpR, _hands);
                if (riding)
                    _legL.localRotation = _legR.localRotation = Quaternion.identity; // висит, ноги не идут
            }

            var armScale = new Vector3(1f, Mathf.Lerp(1f, HoldArmScale, k), 1f);
            _armL.localScale = armScale;
            _armR.localScale = armScale;

            ApplyBerserkPose(_swing / MaxSwing);
            ApplyMorph();
        }

        /// <summary>Длина руки до кисти (0.62) → расстояние от плеча до бока бинокля.</summary>
        static readonly float HoldArmScale = Vector3.Distance(ShoulderR, BinocularsFace + new Vector3(0.13f, -0.04f, -0.02f)) / 0.62f;

        /// <summary>
        /// Чужой игрок на ракете: тело рисуем точно на нашей копии ракеты (позиция из сети приходит с задержкой
        /// и на скорости ракеты отставала бы на метры). Свой — двигается сам.
        /// </summary>
        void LateUpdate()
        {
            if (_body == null || _net == null || _net.IsOwner || _health.IsDowned)
                return;
            if (_net.IsRiding && RocketRider.TryGetHangPosition(_net.RideSilo, _net.RideSlot, out var feet))
                _body.position = feet;
            else if (_body.localPosition != Vector3.zero)
                _body.localPosition = Vector3.zero;
        }

        /// <summary>Купол раскрывается за ~0.4 с и так же сворачивается.</summary>
        void UpdateParachute(bool open, float dt)
        {
            _canopy = Mathf.MoveTowards(_canopy, open ? 1f : 0f, dt * 2.5f);
            bool visible = _canopy > 0.001f;
            if (_parachute.gameObject.activeSelf != visible)
                _parachute.gameObject.SetActive(visible);
            float k = _canopy * _canopy * (3f - 2f * _canopy);
            // раскрытие: вырастает вверх и вширь из-за спины
            _parachute.localScale = new Vector3(k, Mathf.Lerp(0.3f, 1f, k), k);
        }

        /// <summary>Поворот руки (висит вдоль −Y), при котором кисть оказывается сбоку от поднятого бинокля.</summary>
        static Quaternion HoldRotation(Vector3 shoulder, float side)
        {
            Vector3 hand = BinocularsFace + new Vector3(0.13f * side, -0.04f, -0.02f);
            return Quaternion.FromToRotation(Vector3.down, (hand - shoulder).normalized);
        }

        void UpdatePose()
        {
            if (_body == null)
                return;
            // лёжа каски нет — она слетела (FlyingHelmet); поднялся — снова на голове (у берсерка вместо неё фуражка)
            _helmet.gameObject.SetActive(!_health.IsDowned && !BerserkGear);
            _helmet.localRotation = Quaternion.identity;
            if (_health.IsDowned)
            {
                _body.localScale = Vector3.one;
                _helmet.localPosition = new Vector3(0f, Models.HelmetBaseY, 0f);
                _raise = 0f;
                _binoculars.localPosition = BinocularsChest;
                _binoculars.localRotation = BinocularsChestRot;
                _body.localRotation = Quaternion.Euler(-90f, 0f, 0f);
                _body.localPosition = new Vector3(0f, 0.2f, -0.9f);
                _legL.localRotation = _legR.localRotation = Quaternion.identity;
                // руки раскинуты
                _armL.localScale = _armR.localScale = Vector3.one;
                _armL.localRotation = Quaternion.Euler(0f, 0f, -70f);
                _armR.localRotation = Quaternion.Euler(0f, 0f, 70f);
            }
            else
            {
                _body.localRotation = Quaternion.identity;
                _body.localPosition = Vector3.zero;
            }
        }
    }
}
