using RocketWars.Combat;
using RocketWars.Core;
using RocketWars.Fx;
using RocketWars.Net;
using UnityEngine;

namespace RocketWars.Launcher
{
    /// <summary>
    /// Ракета первую секунду поднимается строго вверх, затем плавно заваливается на дугу к цели
    /// (детерминированная кривая start → target за заданное время
    /// (одинаково на всех клиентах по старту, цели и времени пуска). Урон наносит только серверная ракета,
    /// клиентские — визуал, взрыв к ним приходит отдельной рассылкой.
    /// Если под целью пустота — пролетает точку цели и падает вниз без урона.
    /// </summary>
    public class Rocket : MonoBehaviour
    {
        const float VoidFallLimit = 120f;
        const float SmokeInterval = 0.035f;
        /// <summary>Вертикальный разгон из шахты: время и набранная высота (скорость в конце — 2·BoostHeight/BoostTime).</summary>
        const float BoostTime = 1f;
        const float BoostHeight = 12f;

        Vector3 _start, _target;
        float _arcHeight, _duration, _t;
        Vector3 _boostEnd, _c1, _c2, _right, _launchBack; // точка конца разгона, контрольные точки дуги, «вправо» плоскости полёта
        bool _hasGround, _free, _authoritative;
        Vector3 _freeVelocity;
        GameObject _source;
        int _team;
        float _smokeTimer;

        /// <summary>Ракеты в полёте (у каждого участника свои копии).</summary>
        public static readonly System.Collections.Generic.List<Rocket> Active = new System.Collections.Generic.List<Rocket>();
        /// <summary>Новая ракета вылетела из шахты.</summary>
        public static event System.Action<Rocket> Spawned;

        public int SiloIndex { get; private set; }
        public Vector3 CurrentVelocity { get; private set; }
        /// <summary>Точка падения и сколько секунд до неё; под пустотой ракета не взрывается (боты не убегают).</summary>
        public Vector3 Target => _target;
        public float TimeToImpact => _duration - _t;
        public bool HitsGround => _hasGround && !_free;
        /// <summary>Как далеко прошёл доворот крена на вертикальном разгоне: 0 — стоит как в шахте, 1 — на курсе.</summary>
        public float LaunchBlend => _free ? 1f : Mathf.SmoothStep(0f, 1f, _t / BoostTime);

        void OnEnable() => Active.Add(this);
        void OnDisable() => Active.Remove(this);

        public static float FlightTime(float distance) => 3f + distance / 30f;
        public static float ArcHeight(float distance) => Mathf.Max(25f, distance * 0.4f);

        public static Rocket Spawn(in LaunchInfo info, bool authoritative)
        {
            var go = new GameObject("Rocket");
            go.transform.position = info.Start;
            // нос — в цвет шахты (как у ракеты, стоявшей в ней), даже если пуск нажал гость с чужого острова
            var silo = MatchNet.Instance != null ? MatchNet.Instance.GetSilo(info.SiloIndex) : null;
            BuildVisual(go.transform, silo != null ? silo.Team : info.SourceTeam);

            var rocket = go.AddComponent<Rocket>();
            rocket._start = info.Start;
            rocket._target = info.Target;
            rocket._hasGround = info.HasGround;
            rocket._source = info.Source;
            rocket._team = info.SourceTeam;
            rocket._authoritative = authoritative;
            float distance = Vector3.Distance(new Vector3(info.Start.x, 0, info.Start.z), new Vector3(info.Target.x, 0, info.Target.z));
            rocket._duration = FlightTime(distance);
            rocket._arcHeight = ArcHeight(distance);
            rocket.BuildCurve();
            // клиент мог получить пуск с задержкой — догоняем
            rocket._t = Mathf.Max(0f, (float)(NetClock.Now - info.LaunchTime));
            rocket.SiloIndex = info.SiloIndex;
            rocket.CurrentVelocity = rocket.Velocity(Mathf.Min(rocket._t, rocket._duration));
            go.transform.position = rocket.Evaluate(Mathf.Min(rocket._t, rocket._duration));
            go.transform.rotation = rocket.Orientation(rocket.CurrentVelocity, rocket._t);

            // в звуке пуска уже и старт, и полёт — он летит вместе с ракетой: слышно, как она проходит над головой
            var audio = Sfx.Configure(go.AddComponent<AudioSource>(), 150f);
            audio.clip = Sfx.Launch;
            audio.dopplerLevel = 0.7f;
            audio.Play();
            Spawned?.Invoke(rocket);
            return rocket;
        }

        static void BuildVisual(Transform root, int team)
        {
            var (mesh, materials) = Models.Rocket(team);
            BoxModel.Spawn(mesh, materials, root, Vector3.zero, "Model").transform.localScale = Vector3.one * Models.RocketScale;
        }

        /// <summary>Дымный след кубиками и пламя из сопла.</summary>
        void EmitTrail(Vector3 velocity)
        {
            _smokeTimer -= Time.deltaTime;
            Vector3 tail = transform.position - transform.forward * Models.RocketTail;
            VoxelFx.RocketFlame(tail, -transform.forward);
            while (_smokeTimer <= 0f)
            {
                _smokeTimer += SmokeInterval;
                VoxelFx.RocketSmoke(tail, velocity);
            }
        }
        /// <summary>
        /// Дуга после разгона — кубическая кривая Безье: выходит вертикально с той же скоростью, что набрана при разгоне,
        /// и приходит в цель под тем же углом, что и прежняя парабола высотой _arcHeight.
        /// </summary>
        void BuildCurve()
        {
            float arcTime = _duration - BoostTime;
            _boostEnd = _start + Vector3.up * BoostHeight;
            float boostSpeed = 2f * BoostHeight / BoostTime;
            _c1 = _boostEnd + Vector3.up * (boostSpeed * arcTime / 3f);
            _c2 = _target - ((_target - _boostEnd) - Vector3.up * (4f * _arcHeight)) / 3f;

            Vector3 flat = _target - _start;
            flat.y = 0f;
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            _right = Vector3.Cross(Vector3.up, flat.normalized);

            // в шахте ракета стоит брюхом к −Z; модель симметрична на 90°, поэтому берём ближайшую к цели
            // из четырёх равноценных сторон — при разгоне крен довернётся не больше чем на 45°
            Vector3 back = -flat.normalized;
            _launchBack = Mathf.Abs(back.x) > Mathf.Abs(back.z)
                ? new Vector3(Mathf.Sign(back.x), 0f, 0f)
                : new Vector3(0f, 0f, Mathf.Sign(back.z));
        }

        /// <summary>Позиция через t секунд после пуска.</summary>
        Vector3 Evaluate(float t)
        {
            if (t < BoostTime)
            {
                float b = t / BoostTime;
                return _start + Vector3.up * (BoostHeight * b * b);
            }
            float u = (t - BoostTime) / (_duration - BoostTime), v = 1f - u;
            return v * v * v * _boostEnd + 3f * v * v * u * _c1 + 3f * v * u * u * _c2 + u * u * u * _target;
        }

        /// <summary>Скорость (м/с) через t секунд после пуска.</summary>
        Vector3 Velocity(float t)
        {
            if (t < BoostTime)
                return Vector3.up * (2f * BoostHeight * t / (BoostTime * BoostTime));
            float arcTime = _duration - BoostTime;
            float u = (t - BoostTime) / arcTime, v = 1f - u;
            return (3f * v * v * (_c1 - _boostEnd) + 6f * v * u * (_c2 - _c1) + 3f * u * u * (_target - _c2)) / arcTime;
        }

        /// <summary>Нос — по скорости, брюхо — «вниз» относительно плоскости полёта; на разгоне плавно доворачивает крен из шахты.</summary>
        Quaternion Orientation(Vector3 velocity, float t)
        {
            if (velocity.sqrMagnitude < 1e-4f)
                velocity = Vector3.up;
            Vector3 forward = velocity.normalized;
            if (t < BoostTime)
                return Quaternion.LookRotation(forward, Vector3.Slerp(_launchBack, Vector3.Cross(Vector3.up, _right), Mathf.SmoothStep(0f, 1f, t / BoostTime)));
            return Quaternion.LookRotation(forward, Vector3.Cross(forward, _right));
        }

        void Update()
        {
            if (_free)
            {
                _freeVelocity += Physics.gravity * Time.deltaTime;
                transform.position += _freeVelocity * Time.deltaTime;
                transform.rotation = Orientation(_freeVelocity, _duration);
                CurrentVelocity = _freeVelocity;
                EmitTrail(_freeVelocity);
                if (transform.position.y < _target.y - VoidFallLimit)
                    Destroy(gameObject);
                return;
            }

            _t += Time.deltaTime;
            if (_t < _duration)
            {
                transform.position = Evaluate(_t);
                CurrentVelocity = Velocity(_t);
                transform.rotation = Orientation(CurrentVelocity, _t);
                EmitTrail(CurrentVelocity);
                return;
            }

            if (_hasGround)
            {
                if (_authoritative)
                    Explosion.Detonate(_target, _source, _team);
                Destroy(gameObject);
            }
            else
            {
                _free = true;
                _freeVelocity = Velocity(_duration);
                transform.position = _target;
            }
        }
    }
}
