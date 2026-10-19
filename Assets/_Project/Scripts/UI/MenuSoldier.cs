using RocketWars.Core;
using UnityEngine;

namespace RocketWars.UI
{
    /// <summary>
    /// Солдат для сцены главного меню — та же воксельная модель, что в игре (PlayerVisual), но без сети и здоровья:
    /// бегает, целится и стреляет из пистолета, отлетает от взрывов, лежит и снова встаёт (бессмертный).
    /// Поза задаётся полями (Move, Aim, Knock...) из MenuBattle, анимация — здесь.
    /// </summary>
    public class MenuSoldier
    {
        const float StrideLength = 1.6f;
        const float MaxSwing = 35f;
        const float Gravity = 20f;
        const float GetUpTime = 0.8f;
        static readonly Quaternion AimArm = Quaternion.Euler(-85f, 0f, 0f);

        public readonly int Team;
        public readonly Transform Root;
        readonly Transform _pivot, _body, _armL, _armR, _legL, _legR, _helmet, _muzzle;

        public enum State { Stand, Run, Flying, Lying, GettingUp }
        public State Current { get; private set; } = State.Stand;

        /// <summary>Куда смотрит (рысканье, градусы) и куда бежит.</summary>
        public float Yaw;
        public Vector3 RunTarget;
        public float RunSpeed = 4.5f;
        /// <summary>Целится (рука с пистолетом вскинута).</summary>
        public bool Aiming;

        Vector3 _velocity;
        Vector3 _spinAxis;
        float _spinSpeed, _phase, _swing, _recoil, _stateTime, _lieTime, _aimBlend;
        Quaternion _fallRotation;

        public bool CanAct => Current == State.Stand || Current == State.Run;
        public Vector3 Position => Root.position;
        public Vector3 Chest => Root.position + Vector3.up * 1.2f;
        public Vector3 MuzzlePosition => _muzzle.position;

        public MenuSoldier(Transform parent, int team, Vector3 position, float yaw)
        {
            Team = team;
            Yaw = yaw;
            Root = new GameObject($"Soldier_T{team}").transform;
            Root.SetParent(parent, false);
            Root.position = position;
            _pivot = new GameObject("Pivot").transform;
            _pivot.SetParent(Root, false);
            _body = Build(_pivot, team, out _armL, out _armR, out _legL, out _legR, out _helmet);

            var (pistolMesh, pistolMats) = Models.Pistol();
            var pistol = BoxModel.Spawn(pistolMesh, pistolMats, _armR, new Vector3(0f, -0.64f, 0.02f), "Pistol").transform;
            pistol.localRotation = Quaternion.Euler(90f, 0f, 0f);
            _muzzle = new GameObject("Muzzle").transform;
            _muzzle.SetParent(pistol, false);
            _muzzle.localPosition = new Vector3(0f, 0.045f, Models.PistolMuzzle);
            Root.rotation = Quaternion.Euler(0f, Yaw, 0f);
        }

        /// <summary>Воксельный солдат цвета команды (тело, руки, ноги, каска, бинокль на груди) под parent.</summary>
        public static Transform Build(Transform parent, int team, out Transform armL, out Transform armR,
            out Transform legL, out Transform legR, out Transform helmet)
        {
            var body = new GameObject("Body").transform;
            body.SetParent(parent, false);
            var (bodyMesh, bodyMats) = Models.CharacterBody(team);
            BoxModel.Spawn(bodyMesh, bodyMats, body, Vector3.zero, "Torso");
            var (armMesh, armMats) = Models.CharacterArm(team);
            armL = BoxModel.Spawn(armMesh, armMats, body, new Vector3(-0.4f, 1.48f, 0f), "ArmL").transform;
            armR = BoxModel.Spawn(armMesh, armMats, body, new Vector3(0.4f, 1.48f, 0f), "ArmR").transform;
            var (legMesh, legMats) = Models.CharacterLeg();
            legL = BoxModel.Spawn(legMesh, legMats, body, new Vector3(-0.14f, 0.84f, 0f), "LegL").transform;
            legR = BoxModel.Spawn(legMesh, legMats, body, new Vector3(0.14f, 0.84f, 0f), "LegR").transform;
            var (helmetMesh, helmetMats) = Models.CharacterHelmet(team);
            helmet = BoxModel.Spawn(helmetMesh, helmetMats, body, new Vector3(0f, Models.HelmetBaseY, 0f), "Helmet").transform;
            var (binoMesh, binoMats) = Models.Binoculars();
            BoxModel.Spawn(binoMesh, binoMats, body, new Vector3(0f, 1.24f, 0.24f), "Binoculars").transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
            return body;
        }

        /// <summary>Мгновенно переставить (стоя) в точку position, лицом по yaw.</summary>
        public void Teleport(Vector3 position, float yaw)
        {
            Root.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Yaw = yaw;
            Current = State.Stand;
        }

        public void Run(Vector3 target)
        {
            if (!CanAct)
                return;
            RunTarget = target;
            Current = State.Run;
        }

        /// <summary>Выстрел: рука дёргается отдачей.</summary>
        public void Shoot() => _recoil = 1f;

        /// <summary>Взрывом подбросило: летит, кувыркаясь, падает и лежит lieTime секунд.</summary>
        public void Knock(Vector3 velocity, float lieTime)
        {
            if (Current == State.Flying)
                return;
            _velocity = velocity;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            _spinAxis = flat.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, flat.normalized) : Vector3.right;
            _spinSpeed = Random.Range(260f, 480f);
            _lieTime = lieTime;
            Root.position += Vector3.up * 0.05f;
            Current = State.Flying;
            _stateTime = 0f;
            Aiming = false;
        }

        public void Update(float dt)
        {
            _stateTime += dt;
            switch (Current)
            {
                case State.Run: UpdateRun(dt); break;
                case State.Flying: UpdateFlying(dt); return;
                case State.Lying: UpdateLying(); return;
                case State.GettingUp: UpdateGettingUp(); break;
            }
            if (Current == State.Stand || Current == State.Run)
                Root.rotation = Quaternion.RotateTowards(Root.rotation, Quaternion.Euler(0f, Yaw, 0f), 360f * dt);
            Animate(dt);
        }

        void UpdateRun(float dt)
        {
            Vector3 to = RunTarget - Root.position;
            to.y = 0f;
            float step = RunSpeed * dt;
            if (to.magnitude <= step)
            {
                Root.position = new Vector3(RunTarget.x, Root.position.y, RunTarget.z);
                Current = State.Stand;
                return;
            }
            Root.position += to.normalized * step;
            if (!Aiming)
                Yaw = Quaternion.LookRotation(to).eulerAngles.y;
        }

        void UpdateFlying(float dt)
        {
            _velocity.y -= Gravity * dt;
            Root.position += _velocity * dt;
            _pivot.localRotation = Quaternion.AngleAxis(_spinSpeed * dt, Root.InverseTransformDirection(_spinAxis)) * _pivot.localRotation;
            _pivot.localPosition = Vector3.up - _pivot.localRotation * Vector3.up; // кувыркается вокруг пояса, а не вокруг пяток
            // руки и ноги болтаются
            float flail = Mathf.Sin(_stateTime * 14f) * 30f;
            _armL.localRotation = Quaternion.Euler(150f + flail, 0f, -30f);
            _armR.localRotation = Quaternion.Euler(150f - flail, 0f, 30f);
            _legL.localRotation = Quaternion.Euler(flail * 0.6f, 0f, -18f);
            _legR.localRotation = Quaternion.Euler(-flail * 0.6f, 0f, 18f);

            if (Root.position.y > MenuGround.Top || _velocity.y > 0f)
                return;
            // упал на спину, головой туда, куда летел
            Root.position = new Vector3(Root.position.x, MenuGround.Top, Root.position.z);
            Vector3 flat = new Vector3(_velocity.x, 0f, _velocity.z);
            Yaw = flat.sqrMagnitude > 0.01f ? Quaternion.LookRotation(-flat).eulerAngles.y : Yaw;
            Root.rotation = Quaternion.Euler(0f, Yaw, 0f);
            _fallRotation = Quaternion.Euler(-90f, 0f, 0f);
            _pivot.localRotation = _fallRotation;
            _pivot.localPosition = new Vector3(0f, 0.2f, 0f);
            _armL.localRotation = Quaternion.Euler(170f, 0f, -40f);
            _armR.localRotation = Quaternion.Euler(170f, 0f, 40f);
            _legL.localRotation = Quaternion.Euler(0f, 0f, -12f);
            _legR.localRotation = Quaternion.Euler(0f, 0f, 12f);
            Current = State.Lying;
            _stateTime = 0f;
        }

        void UpdateLying()
        {
            if (_stateTime >= _lieTime)
            {
                Current = State.GettingUp;
                _stateTime = 0f;
            }
        }

        void UpdateGettingUp()
        {
            float k = Mathf.SmoothStep(0f, 1f, _stateTime / GetUpTime);
            _pivot.localRotation = Quaternion.Slerp(_fallRotation, Quaternion.identity, k);
            _pivot.localPosition = Vector3.Lerp(new Vector3(0f, 0.2f, 0f), Vector3.zero, k);
            if (k >= 1f)
            {
                Current = State.Stand;
                _stateTime = 0f;
            }
        }

        void Animate(float dt)
        {
            bool running = Current == State.Run;
            if (running)
                _phase += RunSpeed * dt / StrideLength * Mathf.PI * 2f;
            _swing = Mathf.MoveTowards(_swing, running ? 1f : 0f, dt * 6f);
            _aimBlend = Mathf.MoveTowards(_aimBlend, Aiming ? 1f : 0f, dt * 5f);
            _recoil = Mathf.MoveTowards(_recoil, 0f, dt * 6f);

            float s = Mathf.Sin(_phase) * MaxSwing * _swing;
            _legL.localRotation = Quaternion.Euler(s, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(-s, 0f, 0f);
            _armL.localRotation = Quaternion.Euler(-s * 0.8f, 0f, -4f);
            var armR = Quaternion.Euler(s * 0.8f, 0f, 4f);
            _armR.localRotation = Quaternion.Slerp(armR, AimArm * Quaternion.Euler(-_recoil * 25f, 0f, 0f), _aimBlend);
            _body.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Cos(_phase)) * 0.06f * _swing, 0f);
            _helmet.localRotation = Quaternion.Euler(Mathf.Sin(_phase * 2f) * 6f * _swing, 0f, 0f);
        }
    }
}
